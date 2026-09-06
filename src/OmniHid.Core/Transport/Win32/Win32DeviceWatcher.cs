using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace OmniHid.Core.Transport.Win32
{
    /// <summary>
    /// Background listener window that intercepts Windows PnP device change messages (<c>WM_DEVICECHANGE</c>).
    /// Executes a dedicated message pump on a background STA thread so real-time USB plug/unplug events
    /// are captured reliably across GUI, console, and headless applications.
    /// </summary>
    public class Win32DeviceWatcher : IDisposable
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Windows Message & Event Constants
        // ═══════════════════════════════════════════════════════════════════════

        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

        /// <summary>
        /// Raised when a USB or HID hardware peripheral is connected or disconnected from the host.
        /// </summary>
        public event Action DeviceChanged;

        private Thread _thread;
        private InternalWatcherWindow _window;
        private ApplicationContext _context;
        private volatile bool _disposed;

        // ═══════════════════════════════════════════════════════════════════════
        // Constructors & Message Pump Thread
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Initializes a new instance of the <see cref="Win32DeviceWatcher"/> class and starts the background STA message loop.
        /// </summary>
        public Win32DeviceWatcher()
        {
            using (var initEvt = new ManualResetEvent(false))
            {
                _thread = new Thread(() =>
                {
                    try
                    {
                        _context = new ApplicationContext();
                        _window = new InternalWatcherWindow(this);
                        initEvt.Set();
                        Application.Run(_context);
                    }
                    catch
                    {
                        initEvt.Set();
                    }
                })
                {
                    IsBackground = true,
                    Name = "OmniHidDeviceWatcher"
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                initEvt.WaitOne(2000);
            }
        }

        private void OnDeviceChanged()
        {
            Action handler = DeviceChanged;
            if (handler != null)
            {
                handler();
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Disposal
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Releases registered device notifications, terminates the background message pump, and destroys the listener window.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_window != null)
            {
                try { _window.Dispose(); } catch { }
                _window = null;
            }

            if (_context != null)
            {
                try { _context.ExitThread(); } catch { }
                _context = null;
            }

            if (_thread != null && _thread.IsAlive)
            {
                try { _thread.Join(500); } catch { }
                _thread = null;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Internal Listener Window
        // ═══════════════════════════════════════════════════════════════════════

        private class InternalWatcherWindow : NativeWindow, IDisposable
        {
            private readonly Win32DeviceWatcher _owner;
            private IntPtr _hNotification = IntPtr.Zero;

            public InternalWatcherWindow(Win32DeviceWatcher owner)
            {
                _owner = owner;
                CreateHandle(new CreateParams());
                RegisterHidNotification();
            }

            private void RegisterHidNotification()
            {
                try
                {
                    Guid hidGuid;
                    Win32HidNative.HidD_GetHidGuid(out hidGuid);

                    var filter = new Win32HidNative.DEV_BROADCAST_DEVICEINTERFACE();
                    filter.dbcc_size = Marshal.SizeOf(filter);
                    filter.dbcc_devicetype = Win32HidNative.DBT_DEVTYP_DEVICEINTERFACE;
                    filter.dbcc_classguid = hidGuid;

                    IntPtr pFilter = Marshal.AllocHGlobal(filter.dbcc_size);
                    try
                    {
                        Marshal.StructureToPtr(filter, pFilter, false);
                        _hNotification = Win32HidNative.RegisterDeviceNotification(
                            Handle,
                            pFilter,
                            Win32HidNative.DEVICE_NOTIFY_WINDOW_HANDLE);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pFilter);
                    }
                }
                catch { }
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);

                if (m.Msg == WM_DEVICECHANGE)
                {
                    int wParam = m.WParam.ToInt32();
                    if (wParam == DBT_DEVICEARRIVAL || wParam == DBT_DEVICEREMOVECOMPLETE)
                    {
                        _owner.OnDeviceChanged();
                    }
                }
            }

            public void Dispose()
            {
                if (_hNotification != IntPtr.Zero)
                {
                    try { Win32HidNative.UnregisterDeviceNotification(_hNotification); } catch { }
                    _hNotification = IntPtr.Zero;
                }
                DestroyHandle();
            }
        }
    }
}