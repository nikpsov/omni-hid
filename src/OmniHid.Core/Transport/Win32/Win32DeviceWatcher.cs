using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace OmniHid.Core.Transport.Win32
{
    /// <summary>
    /// Background listener window that intercepts Windows PnP device change messages (<c>WM_DEVICECHANGE</c>).
    /// Executes a dedicated message pump on a background thread using a Win32 message-only window (<c>HWND_MESSAGE</c>)
    /// with zero dependencies on System.Windows.Forms.
    /// </summary>
    public class Win32DeviceWatcher : IDisposable
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Events & State
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Raised when a USB or HID hardware peripheral is connected or disconnected from the host.
        /// </summary>
        public event Action DeviceChanged;

        private Thread _thread;
        private IntPtr _hWnd = IntPtr.Zero;
        private IntPtr _hNotification = IntPtr.Zero;
        private Win32HidNative.WndProcDelegate _wndProcDelegate;
        private volatile bool _disposed;
        private const string WindowClassName = "OmniHidDeviceWatcherClass";

        // ═══════════════════════════════════════════════════════════════════════
        // Constructors & Message Pump Thread
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Initializes a new instance of the <see cref="Win32DeviceWatcher"/> class and starts the background Win32 message loop.
        /// </summary>
        public Win32DeviceWatcher()
        {
            using (var initEvt = new ManualResetEvent(false))
            {
                _thread = new Thread(() => MessageLoopThread(initEvt))
                {
                    IsBackground = true,
                    Name = "OmniHidDeviceWatcher"
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                initEvt.WaitOne(2000);
            }
        }

        private void MessageLoopThread(ManualResetEvent initEvt)
        {
            try
            {
                IntPtr hInstance = Win32HidNative.GetModuleHandle(null);

                _wndProcDelegate = CustomWndProc;

                var wcx = new Win32HidNative.WNDCLASSEX();
                wcx.cbSize = (uint)Marshal.SizeOf(wcx);
                wcx.lpfnWndProc = _wndProcDelegate;
                wcx.hInstance = hInstance;
                wcx.lpszClassName = WindowClassName;

                Win32HidNative.RegisterClassEx(ref wcx);

                _hWnd = Win32HidNative.CreateWindowEx(
                    0,
                    WindowClassName,
                    "OmniHidDeviceWatcherWindow",
                    0,
                    0, 0, 0, 0,
                    Win32HidNative.HWND_MESSAGE,
                    IntPtr.Zero,
                    hInstance,
                    IntPtr.Zero);

                if (_hWnd != IntPtr.Zero)
                {
                    RegisterHidNotification(_hWnd);
                }

                initEvt.Set();

                Win32HidNative.MSG msg;
                while (!_disposed && Win32HidNative.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    Win32HidNative.TranslateMessage(ref msg);
                    Win32HidNative.DispatchMessage(ref msg);
                }
            }
            catch
            {
                initEvt.Set();
            }
        }

        private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == Win32HidNative.WM_DEVICECHANGE)
            {
                int wp = wParam.ToInt32();
                if (wp == Win32HidNative.DBT_DEVICEARRIVAL || wp == Win32HidNative.DBT_DEVICEREMOVECOMPLETE)
                {
                    OnDeviceChanged();
                }
            }
            else if (msg == Win32HidNative.WM_DESTROY)
            {
                Win32HidNative.PostQuitMessage(0);
                return IntPtr.Zero;
            }

            return Win32HidNative.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void RegisterHidNotification(IntPtr hWnd)
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
                        hWnd,
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

            if (_hNotification != IntPtr.Zero)
            {
                try { Win32HidNative.UnregisterDeviceNotification(_hNotification); } catch { }
                _hNotification = IntPtr.Zero;
            }

            if (_hWnd != IntPtr.Zero)
            {
                try
                {
                    Win32HidNative.PostMessage(_hWnd, Win32HidNative.WM_DESTROY, IntPtr.Zero, IntPtr.Zero);
                }
                catch { }
                _hWnd = IntPtr.Zero;
            }

            if (_thread != null && _thread.IsAlive)
            {
                try { _thread.Join(1000); } catch { }
                _thread = null;
            }
        }
    }
}