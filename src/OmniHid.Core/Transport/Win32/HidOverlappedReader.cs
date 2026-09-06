using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace OmniHid.Core.Transport.Win32
{
    /// <summary>
    /// Encapsulates an active non-blocking asynchronous Win32 Overlapped Read operation on a HID interface endpoint.
    /// Provides reusable streaming, event-driven signaling, and memory-safe unmanaged resource disposal.
    /// </summary>
    public class HidOverlappedReader : IDisposable
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>The target HID interface descriptor associated with this reader.</summary>
        public HidDeviceInfo Interface { get; private set; }

        /// <summary>Active file handle opened for overlapped read access.</summary>
        public SafeFileHandle Handle { get; private set; }

        /// <summary>Buffer storing received report bytes.</summary>
        public byte[] Buffer { get; private set; }

        /// <summary>Optional 1-based display index of this interface collection.</summary>
        public int InterfaceIndex { get; set; }

        /// <summary>Optional buffer retaining previous frame data for differential analysis.</summary>
        public byte[] LastBuffer { get; set; }

        /// <summary>Wait handle signaled when an asynchronous read operation completes.</summary>
        public ManualResetEvent WaitEvent { get; private set; }

        /// <summary>Gets a value indicating whether an asynchronous I/O operation is currently pending.</summary>
        public bool IsPending { get { return _isPending; } }

        /// <summary>Gets a value indicating whether the read completed synchronously.</summary>
        public bool IsCompleted { get { return _completed; } }

        private IntPtr _pOverlapped = IntPtr.Zero;
        private bool _isPending = false;
        private bool _completed = false;
        private bool _disposed = false;

        // ═══════════════════════════════════════════════════════════════════════
        // Constructors
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Initializes a new instance of the <see cref="HidOverlappedReader"/> class.
        /// </summary>
        /// <param name="iface">Target HID interface descriptor.</param>
        /// <param name="handle">Open file handle with FILE_FLAG_OVERLAPPED and GENERIC_READ access.</param>
        /// <param name="bufferLength">Report buffer byte capacity (defaults to interface report length or 64).</param>
        /// <param name="initialReportId">Optional initial report ID byte to place in buffer[0].</param>
        public HidOverlappedReader(HidDeviceInfo iface, SafeFileHandle handle, int bufferLength = 0, byte initialReportId = 0)
            : this(iface, handle, 0, bufferLength, initialReportId)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HidOverlappedReader"/> class with an interface display index.
        /// </summary>
        /// <param name="iface">Target HID interface descriptor.</param>
        /// <param name="handle">Open file handle with FILE_FLAG_OVERLAPPED and GENERIC_READ access.</param>
        /// <param name="index">1-based interface display index.</param>
        /// <param name="bufferLength">Report buffer byte capacity.</param>
        /// <param name="initialReportId">Optional initial report ID byte.</param>
        public HidOverlappedReader(HidDeviceInfo iface, SafeFileHandle handle, int index, int bufferLength, byte initialReportId = 0)
        {
            Interface = iface;
            Handle = handle;
            InterfaceIndex = index;

            int len = bufferLength > 0 ? bufferLength : (iface != null && iface.InputReportByteLength > 0 ? (int)iface.InputReportByteLength : 64);
            Buffer = new byte[Math.Max(16, len)];
            if (initialReportId != 0)
            {
                Buffer[0] = initialReportId;
            }

            WaitEvent = new ManualResetEvent(false);
            NativeOverlapped ov = new NativeOverlapped
            {
                EventHandle = WaitEvent.SafeWaitHandle.DangerousGetHandle()
            };
            _pOverlapped = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
            Marshal.StructureToPtr(ov, _pOverlapped, false);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Read Pipeline Methods
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Cancels any currently pending asynchronous read operation on this endpoint.
        /// </summary>
        public void CancelPendingRead()
        {
            if (_isPending && !_completed && Handle != null && !Handle.IsInvalid && !Handle.IsClosed)
            {
                Win32HidNative.CancelIoEx(Handle, _pOverlapped);
                uint bytesRead;
                Win32HidNative.GetOverlappedResult(Handle, _pOverlapped, out bytesRead, true);
                _isPending = false;
            }
        }

        /// <summary>
        /// Initiates a non-blocking asynchronous read request on the HID endpoint.
        /// </summary>
        /// <returns><c>true</c> if read was queued (pending) or completed synchronously; otherwise, <c>false</c>.</returns>
        public bool StartRead()
        {
            if (_disposed || Handle == null || Handle.IsInvalid || Handle.IsClosed) return false;

            if (_isPending)
            {
                CancelPendingRead();
            }

            WaitEvent.Reset();
            _completed = false;
            _isPending = false;

            uint bytesRead;
            bool ok = Win32HidNative.ReadFile(Handle, Buffer, (uint)Buffer.Length, out bytesRead, _pOverlapped);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == Win32HidNative.ERROR_IO_PENDING)
                {
                    _isPending = true;
                    return true;
                }
                return false;
            }

            _completed = true;
            WaitEvent.Set();
            return true;
        }

        /// <summary>
        /// Checks whether pending I/O has finished and retrieves the transferred byte count without blocking.
        /// </summary>
        /// <param name="bytesTransferred">Receives the number of bytes transferred if completed.</param>
        /// <returns><c>true</c> if read is complete; otherwise, <c>false</c>.</returns>
        public bool CompleteRead(out uint bytesTransferred)
        {
            bytesTransferred = 0;
            if (_completed)
            {
                bytesTransferred = (uint)Buffer.Length;
                return true;
            }

            if (_isPending && Handle != null && !Handle.IsInvalid && !Handle.IsClosed)
            {
                if (Win32HidNative.GetOverlappedResult(Handle, _pOverlapped, out bytesTransferred, false))
                {
                    _completed = true;
                    _isPending = false;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks whether the pending read operation has completed without blocking.
        /// </summary>
        /// <returns><c>true</c> if read is complete; otherwise, <c>false</c>.</returns>
        public bool CheckCompleted()
        {
            uint unused;
            return CompleteRead(out unused);
        }

        /// <summary>
        /// Resets the wait event, clears completion flags, and restarts asynchronous reading.
        /// </summary>
        /// <returns><c>true</c> if restarted successfully; otherwise, <c>false</c>.</returns>
        public bool RestartRead()
        {
            if (_disposed) return false;

            if (_isPending)
            {
                CancelPendingRead();
            }

            _completed = false;
            _isPending = false;
            WaitEvent.Reset();

            NativeOverlapped ov = new NativeOverlapped
            {
                EventHandle = WaitEvent.SafeWaitHandle.DangerousGetHandle()
            };
            Marshal.StructureToPtr(ov, _pOverlapped, false);

            uint bytesRead;
            bool ok = Win32HidNative.ReadFile(Handle, Buffer, (uint)Buffer.Length, out bytesRead, _pOverlapped);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (err == Win32HidNative.ERROR_IO_PENDING)
                {
                    _isPending = true;
                    return true;
                }
                return false;
            }

            _completed = true;
            WaitEvent.Set();
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Disposal
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Cancels pending I/O, waits for unmanaged driver cleanup, frees overlapped memory, and closes handles.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_isPending && !_completed && Handle != null && !Handle.IsInvalid && !Handle.IsClosed)
                {
                    Win32HidNative.CancelIoEx(Handle, _pOverlapped);
                    uint bytesRead;
                    Win32HidNative.GetOverlappedResult(Handle, _pOverlapped, out bytesRead, true);
                }
            }
            catch
            {
                // Suppress cancellation exceptions during final object disposal
            }

            if (_pOverlapped != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_pOverlapped);
                _pOverlapped = IntPtr.Zero;
            }

            if (WaitEvent != null)
            {
                WaitEvent.Close();
                WaitEvent = null;
            }

            if (Handle != null && !Handle.IsInvalid && !Handle.IsClosed)
            {
                Handle.Close();
                Handle = null;
            }
        }
    }
}
