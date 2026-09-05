using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace OmniHid.Core.Transport.Win32
{
    /// <summary>
    /// Default Windows implementation of <see cref="IHidTransport"/> utilizing native SetupAPI and HidD Win32 calls.
    /// Supports overlapped asynchronous reads, synchronous feature/output writes, and PnP battery properties.
    /// </summary>
    public class Win32HidTransport : IHidTransport
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Device Interface Enumeration (SetupAPI)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Enumerates all currently present HID devices on the host matching optional VID/PID filters.
        /// </summary>
        /// <param name="vendorId">USB Vendor ID to filter by, or 0 for any vendor.</param>
        /// <param name="productId">USB Product ID to filter by, or 0 for any product.</param>
        /// <returns>A list of discovered <see cref="HidDeviceInfo"/> interfaces.</returns>
        public List<HidDeviceInfo> Enumerate(ushort vendorId = 0, ushort productId = 0)
        {
            var results = new List<HidDeviceInfo>();

            Guid hidGuid;
            Win32HidNative.HidD_GetHidGuid(out hidGuid);

            // Retrieve a device information set containing all present HID interfaces
            IntPtr devInfoSet = Win32HidNative.SetupDiGetClassDevs(
                ref hidGuid,
                IntPtr.Zero,
                IntPtr.Zero,
                Win32HidNative.DIGCF_PRESENT | Win32HidNative.DIGCF_DEVICEINTERFACE);

            if (devInfoSet == IntPtr.Zero || devInfoSet == new IntPtr(-1))
            {
                return results;
            }

            try
            {
                var ifData = new Win32HidNative.SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = (uint)Marshal.SizeOf(ifData);

                uint index = 0;
                while (Win32HidNative.SetupDiEnumDeviceInterfaces(devInfoSet, IntPtr.Zero, ref hidGuid, index, ref ifData))
                {
                    index++;

                    // Query required buffer size for interface details
                    uint requiredSize;
                    Win32HidNative.SetupDiGetDeviceInterfaceDetail(
                        devInfoSet, ref ifData, IntPtr.Zero, 0, out requiredSize, IntPtr.Zero);

                    if (requiredSize == 0) continue;

                    IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                    try
                    {
                        // cbSize must match bitness: 8 bytes for 64-bit, 5 or 6 bytes for 32-bit
                        Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : (4 + Marshal.SystemDefaultCharSize));

                        if (Win32HidNative.SetupDiGetDeviceInterfaceDetail(
                            devInfoSet, ref ifData, detailDataBuffer, requiredSize, out requiredSize, IntPtr.Zero))
                        {
                            // Skip cbSize (4 bytes) to reach the DevicePath character array
                            IntPtr pDevicePath = new IntPtr(detailDataBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                            InspectAndAddDevice(NormalizeDevicePath(devicePath), vendorId, productId, results);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailDataBuffer);
                    }
                }
            }
            finally
            {
                Win32HidNative.SetupDiDestroyDeviceInfoList(devInfoSet);
            }

            return results;
        }

        /// <summary>
        /// Opens a device handle in query mode to extract descriptor strings, capabilities, and VID/PID.
        /// </summary>
        private static void InspectAndAddDevice(string devicePath, ushort vendorId, ushort productId, List<HidDeviceInfo> results)
        {
            if (string.IsNullOrEmpty(devicePath)) return;

            // Open handle with 0 access (device query only) to avoid permission conflicts
            using (SafeFileHandle handle = Win32HidNative.CreateFile(
                devicePath,
                0,
                Win32HidNative.FILE_SHARE_READ | Win32HidNative.FILE_SHARE_WRITE,
                IntPtr.Zero,
                Win32HidNative.OPEN_EXISTING,
                0,
                IntPtr.Zero))
            {
                if (handle.IsInvalid) return;

                var attrs = new Win32HidNative.HIDD_ATTRIBUTES();
                attrs.Size = (uint)Marshal.SizeOf(attrs);
                if (!Win32HidNative.HidD_GetAttributes(handle, ref attrs)) return;

                if (vendorId != 0 && attrs.VendorID != vendorId) return;
                if (productId != 0 && attrs.ProductID != productId) return;

                var info = new HidDeviceInfo
                {
                    DevicePath = devicePath,
                    VendorId = attrs.VendorID,
                    ProductId = attrs.ProductID,
                    VersionNumber = attrs.VersionNumber
                };

                // Query preparsed HID parser capabilities (Usage Page, report lengths)
                IntPtr preparsed;
                if (Win32HidNative.HidD_GetPreparsedData(handle, out preparsed))
                {
                    try
                    {
                        var caps = new Win32HidNative.HIDP_CAPS();
                        if (Win32HidNative.HidP_GetCaps(preparsed, ref caps) >= 0)
                        {
                            info.Usage = caps.Usage;
                            info.UsagePage = caps.UsagePage;
                            info.InputReportByteLength = caps.InputReportByteLength;
                            info.OutputReportByteLength = caps.OutputReportByteLength;
                            info.FeatureReportByteLength = caps.FeatureReportByteLength;
                        }
                    }
                    finally
                    {
                        Win32HidNative.HidD_FreePreparsedData(preparsed);
                    }
                }

                // Query friendly descriptor strings using thread-static buffer to eliminate allocations
                if (_tlStrBuf == null) _tlStrBuf = new byte[256];
                Array.Clear(_tlStrBuf, 0, _tlStrBuf.Length);

                if (Win32HidNative.HidD_GetProductString(handle, _tlStrBuf, (uint)_tlStrBuf.Length))
                {
                    info.ProductString = Encoding.Unicode.GetString(_tlStrBuf).TrimEnd('\0');
                }
                Array.Clear(_tlStrBuf, 0, _tlStrBuf.Length);
                if (Win32HidNative.HidD_GetManufacturerString(handle, _tlStrBuf, (uint)_tlStrBuf.Length))
                {
                    info.ManufacturerString = Encoding.Unicode.GetString(_tlStrBuf).TrimEnd('\0');
                }
                Array.Clear(_tlStrBuf, 0, _tlStrBuf.Length);
                if (Win32HidNative.HidD_GetSerialNumberString(handle, _tlStrBuf, (uint)_tlStrBuf.Length))
                {
                    info.SerialNumber = Encoding.Unicode.GetString(_tlStrBuf).TrimEnd('\0');
                }

                results.Add(info);
            }
        }

        [ThreadStatic]
        private static byte[] _tlStrBuf;
        [ThreadStatic]
        private static byte[] _tlPropBuf;

        // ═══════════════════════════════════════════════════════════════════════
        // Synchronous Feature & Output Reports
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Reads a Feature Report synchronously from the specified device path.
        /// </summary>
        public bool GetFeatureReport(string devicePath, byte reportId, byte[] buffer)
        {
            if (string.IsNullOrEmpty(devicePath) || buffer == null || buffer.Length == 0) return false;

            using (SafeFileHandle handle = OpenDevice(devicePath, Win32HidNative.GENERIC_READ | Win32HidNative.GENERIC_WRITE, false))
            {
                if (handle.IsInvalid) return false;

                buffer[0] = reportId;
                return Win32HidNative.HidD_GetFeature(handle, buffer, (uint)buffer.Length);
            }
        }

        /// <summary>
        /// Writes a Feature Report synchronously to the specified device path.
        /// </summary>
        public bool SetFeatureReport(string devicePath, byte[] buffer)
        {
            if (string.IsNullOrEmpty(devicePath) || buffer == null || buffer.Length == 0) return false;

            using (SafeFileHandle handle = OpenDevice(devicePath, Win32HidNative.GENERIC_READ | Win32HidNative.GENERIC_WRITE, false))
            {
                if (handle.IsInvalid) return false;

                return Win32HidNative.HidD_SetFeature(handle, buffer, (uint)buffer.Length);
            }
        }

        /// <summary>
        /// Writes an Output Report to the device path, attempting <c>HidD_SetOutputReport</c> first with <c>WriteFile</c> fallback.
        /// </summary>
        public bool WriteOutputReport(string devicePath, byte[] buffer)
        {
            if (string.IsNullOrEmpty(devicePath) || buffer == null || buffer.Length == 0) return false;

            using (SafeFileHandle handle = OpenDevice(devicePath, Win32HidNative.GENERIC_WRITE, false))
            {
                if (handle.IsInvalid) return false;

                if (Win32HidNative.HidD_SetOutputReport(handle, buffer, (uint)buffer.Length))
                {
                    return true;
                }

                uint written;
                return Win32HidNative.WriteFile(handle, buffer, (uint)buffer.Length, out written, IntPtr.Zero);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Asynchronous / Overlapped Input Reports
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Reads an Input Report with timeout using Win32 non-blocking Overlapped I/O.
        /// </summary>
        public bool ReadInputReport(string devicePath, byte[] buffer, int timeoutMs)
        {
            if (string.IsNullOrEmpty(devicePath) || buffer == null || buffer.Length == 0) return false;

            using (SafeFileHandle handle = OpenDevice(devicePath, Win32HidNative.GENERIC_READ, true))
            {
                if (handle.IsInvalid) return false;

                using (ManualResetEvent evt = new ManualResetEvent(false))
                {
                    NativeOverlapped overlapped = new NativeOverlapped();
                    overlapped.EventHandle = evt.SafeWaitHandle.DangerousGetHandle();

                    IntPtr pOverlapped = Marshal.AllocHGlobal(Marshal.SizeOf(overlapped));
                    try
                    {
                        Marshal.StructureToPtr(overlapped, pOverlapped, false);

                        uint bytesRead;
                        bool success = Win32HidNative.ReadFile(handle, buffer, (uint)buffer.Length, out bytesRead, pOverlapped);
                        if (!success)
                        {
                            int error = Marshal.GetLastWin32Error();
                            if (error == Win32HidNative.ERROR_IO_PENDING)
                            {
                                if (evt.WaitOne(timeoutMs))
                                {
                                    return Win32HidNative.GetOverlappedResult(handle, pOverlapped, out bytesRead, false);
                                }
                                else
                                {
                                    Win32HidNative.CancelIoEx(handle, pOverlapped);
                                    Win32HidNative.GetOverlappedResult(handle, pOverlapped, out bytesRead, true);
                                    return false;
                                }
                            }
                            return false;
                        }
                        return true;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(pOverlapped);
                    }
                }
            }
        }

        /// <summary>
        /// Reads an Input Report synchronously via the <c>HidD_GetInputReport</c> control transfer.
        /// </summary>
        public bool GetInputReport(string devicePath, byte reportId, byte[] buffer)
        {
            if (string.IsNullOrEmpty(devicePath) || buffer == null || buffer.Length == 0) return false;

            using (SafeFileHandle handle = OpenDevice(devicePath, Win32HidNative.GENERIC_READ, false))
            {
                if (handle.IsInvalid) return false;
                buffer[0] = reportId;
                return Win32HidNative.HidD_GetInputReport(handle, buffer, (uint)buffer.Length);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Windows PnP Device Property Telemetry
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Reads battery percentage directly from the Windows PnP Configuration Manager property cache.
        /// </summary>
        public int GetPnpBatteryLevel(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath)) return -1;
            try
            {
                var key = Win32HidNative.DEVPKEY_Device_BatteryLevel;
                uint propType;
                if (_tlPropBuf == null) _tlPropBuf = new byte[16];
                uint bufSize = (uint)_tlPropBuf.Length;
                int cr = Win32HidNative.CM_Get_Device_Interface_PropertyW(devicePath, ref key, out propType, _tlPropBuf, ref bufSize, 0);
                if (cr == 0 && bufSize > 0)
                {
                    int val = bufSize >= 4 ? BitConverter.ToInt32(_tlPropBuf, 0) : _tlPropBuf[0];
                    if (val >= 0 && val <= 100) return val;
                }
            }
            catch { }
            return -1;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Atomic Exchange (Write Request -> Read Response)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Performs an atomic Write-then-Read sequence with smart packet stream filtering.
        /// Discards extraneous streaming packets (such as active mouse motion on 1000/8000Hz gaming mice)
        /// while waiting for the requested report ID or command response within the timeout window.
        /// </summary>
        public bool Exchange(string writePath, byte[] request, string readPath, byte[] response, int timeoutMs, byte expectedReportId = 0)
        {
            if (string.IsNullOrEmpty(writePath) || request == null || string.IsNullOrEmpty(readPath) || response == null)
                return false;

            byte filterReportId = expectedReportId != 0 ? expectedReportId : response[0];

            using (SafeFileHandle readHandle = OpenDevice(readPath, Win32HidNative.GENERIC_READ, true))
            {
                if (readHandle.IsInvalid) return false;

                using (ManualResetEvent readEvent = new ManualResetEvent(false))
                {
                    NativeOverlapped readOverlapped = new NativeOverlapped();
                    readOverlapped.EventHandle = readEvent.SafeWaitHandle.DangerousGetHandle();

                    IntPtr pReadOverlapped = Marshal.AllocHGlobal(Marshal.SizeOf(readOverlapped));
                    bool pendingIo = false;
                    try
                    {
                        Marshal.StructureToPtr(readOverlapped, pReadOverlapped, false);

                        uint bytesRead;
                        bool readInitiated = Win32HidNative.ReadFile(readHandle, response, (uint)response.Length, out bytesRead, pReadOverlapped);
                        int readError = Marshal.GetLastWin32Error();

                        if (!readInitiated && readError != Win32HidNative.ERROR_IO_PENDING)
                        {
                            return false;
                        }
                        pendingIo = !readInitiated;

                        // Send request via Feature Report, falling back to Output Report
                        bool writeOk = SetFeatureReport(writePath, request);
                        if (!writeOk)
                        {
                            writeOk = WriteOutputReport(writePath, request);
                        }

                        if (!writeOk)
                        {
                            return false;
                        }

                        Stopwatch sw = Stopwatch.StartNew();

                        do
                        {
                            int remainingMs = timeoutMs - (int)sw.ElapsedMilliseconds;
                            if (remainingMs <= 0) break;

                            if (!readInitiated)
                            {
                                if (!readEvent.WaitOne(remainingMs))
                                {
                                    break;
                                }

                                if (!Win32HidNative.GetOverlappedResult(readHandle, pReadOverlapped, out bytesRead, false))
                                {
                                    break;
                                }
                                pendingIo = false;
                            }

                            // If a specific report ID is expected, filter out extraneous streaming packets (e.g. mouse coordinates)
                            if (filterReportId == 0 || response[0] == filterReportId)
                            {
                                return true;
                            }

                            // Discard extraneous packet and queue next read
                            readEvent.Reset();
                            readOverlapped.OffsetLow = 0;
                            readOverlapped.OffsetHigh = 0;
                            Marshal.StructureToPtr(readOverlapped, pReadOverlapped, false);

                            readInitiated = Win32HidNative.ReadFile(readHandle, response, (uint)response.Length, out bytesRead, pReadOverlapped);
                            readError = Marshal.GetLastWin32Error();
                            if (!readInitiated && readError != Win32HidNative.ERROR_IO_PENDING)
                            {
                                break;
                            }
                            pendingIo = !readInitiated;

                        } while (sw.ElapsedMilliseconds < timeoutMs);

                        return false;
                    }
                    finally
                    {
                        if (pendingIo)
                        {
                            uint bytesRead;
                            Win32HidNative.CancelIoEx(readHandle, pReadOverlapped);
                            Win32HidNative.GetOverlappedResult(readHandle, pReadOverlapped, out bytesRead, true);
                        }
                        Marshal.FreeHGlobal(pReadOverlapped);
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Internal Helpers & Disposal
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Normalizes a Windows HID device interface path by stripping virtual filter driver suffixes such as "\kbd".
        /// </summary>
        public static string NormalizeDevicePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path.EndsWith(@"\kbd", StringComparison.OrdinalIgnoreCase))
            {
                return path.Substring(0, path.Length - 4);
            }
            if (path.EndsWith("/kbd", StringComparison.OrdinalIgnoreCase))
            {
                return path.Substring(0, path.Length - 4);
            }
            return path;
        }

        /// <summary>
        /// Creates a file handle for a given Win32 device path with shared read/write access and graceful privilege fallback.
        /// </summary>
        public static SafeFileHandle OpenDevice(string path, uint access, bool overlapped)
        {
            string cleanPath = NormalizeDevicePath(path);
            uint flags = overlapped ? Win32HidNative.FILE_FLAG_OVERLAPPED : 0;

            // 1. Try requested access on normalized path
            SafeFileHandle handle = Win32HidNative.CreateFile(
                cleanPath,
                access,
                Win32HidNative.FILE_SHARE_READ | Win32HidNative.FILE_SHARE_WRITE,
                IntPtr.Zero,
                Win32HidNative.OPEN_EXISTING,
                flags,
                IntPtr.Zero);

            if (!handle.IsInvalid) return handle;

            // 2. Fallback: If combined read/write failed (common on Windows keyboard endpoints), try WRITE only
            if ((access & Win32HidNative.GENERIC_WRITE) != 0 && (access & Win32HidNative.GENERIC_READ) != 0)
            {
                handle = Win32HidNative.CreateFile(
                    cleanPath,
                    Win32HidNative.GENERIC_WRITE,
                    Win32HidNative.FILE_SHARE_READ | Win32HidNative.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    Win32HidNative.OPEN_EXISTING,
                    flags,
                    IntPtr.Zero);

                if (!handle.IsInvalid) return handle;
            }

            // 3. Fallback: try READ only
            if ((access & Win32HidNative.GENERIC_READ) != 0)
            {
                handle = Win32HidNative.CreateFile(
                    cleanPath,
                    Win32HidNative.GENERIC_READ,
                    Win32HidNative.FILE_SHARE_READ | Win32HidNative.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    Win32HidNative.OPEN_EXISTING,
                    flags,
                    IntPtr.Zero);

                if (!handle.IsInvalid) return handle;
            }

            // 4. Fallback: query access (0 access) is only valid when query-only access was requested
            if (access == 0)
            {
                handle = Win32HidNative.CreateFile(
                    cleanPath,
                    0,
                    Win32HidNative.FILE_SHARE_READ | Win32HidNative.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    Win32HidNative.OPEN_EXISTING,
                    flags,
                    IntPtr.Zero);

                return handle;
            }

            return handle;
        }

        /// <summary>
        /// Disposes transient resources (no long-lived unmanaged handles are held).
        /// </summary>
        public void Dispose()
        {
        }
    }
}
