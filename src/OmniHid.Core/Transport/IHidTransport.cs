using System;
using System.Collections.Generic;

namespace OmniHid.Core.Transport
{
    /// <summary>
    /// Abstraction for low-level USB/Bluetooth HID I/O communication and device interface enumeration.
    /// </summary>
    public interface IHidTransport : IDisposable
    {
        /// <summary>
        /// Enumerates all currently connected HID device interfaces matching optional VID/PID filters.
        /// </summary>
        /// <param name="vendorId">Optional Vendor ID filter, or 0 to match all manufacturers.</param>
        /// <param name="productId">Optional Product ID filter, or 0 to match all products.</param>
        /// <returns>List of matching <see cref="HidDeviceInfo"/> instances.</returns>
        List<HidDeviceInfo> Enumerate(ushort vendorId = 0, ushort productId = 0);

        /// <summary>
        /// Reads a Feature Report from the device interface synchronously using <c>HidD_GetFeature</c>.
        /// </summary>
        /// <param name="devicePath">Win32 device path to open.</param>
        /// <param name="reportId">The expected 1-byte Feature Report ID.</param>
        /// <param name="buffer">Target buffer where report bytes will be copied.</param>
        /// <returns><c>true</c> if the feature report was successfully retrieved; otherwise, <c>false</c>.</returns>
        bool GetFeatureReport(string devicePath, byte reportId, byte[] buffer);

        /// <summary>
        /// Writes a Feature Report to the device interface synchronously using <c>HidD_SetFeature</c>.
        /// </summary>
        /// <param name="devicePath">Win32 device path to open.</param>
        /// <param name="buffer">Byte buffer containing the Feature Report starting with Report ID.</param>
        /// <returns><c>true</c> if successfully sent; otherwise, <c>false</c>.</returns>
        bool SetFeatureReport(string devicePath, byte[] buffer);

        /// <summary>
        /// Writes an Output Report to the device interface using <c>HidD_SetOutputReport</c> or <c>WriteFile</c>.
        /// </summary>
        /// <param name="devicePath">Win32 device path to open.</param>
        /// <param name="buffer">Byte buffer containing the Output Report starting with Report ID.</param>
        /// <returns><c>true</c> if written successfully; otherwise, <c>false</c>.</returns>
        bool WriteOutputReport(string devicePath, byte[] buffer);

        /// <summary>
        /// Reads an asynchronous Input Report with timeout using non-blocking Win32 Overlapped I/O.
        /// </summary>
        /// <param name="devicePath">Win32 device path to read from.</param>
        /// <param name="buffer">Buffer to receive report bytes.</param>
        /// <param name="timeoutMs">Timeout in milliseconds before cancelling the pending read.</param>
        /// <returns><c>true</c> if a report was read within the timeout window; otherwise, <c>false</c>.</returns>
        bool ReadInputReport(string devicePath, byte[] buffer, int timeoutMs);

        /// <summary>
        /// Reads an Input Report synchronously via the <c>HidD_GetInputReport</c> control request.
        /// </summary>
        /// <param name="devicePath">Win32 device path to query.</param>
        /// <param name="reportId">Report ID to query in the first byte.</param>
        /// <param name="buffer">Target buffer to receive data.</param>
        /// <returns><c>true</c> if the input report was returned; otherwise, <c>false</c>.</returns>
        bool GetInputReport(string devicePath, byte reportId, byte[] buffer);

        /// <summary>
        /// Reads device battery percentage directly from Windows PnP property (<c>DEVPKEY_Device_BatteryLevel</c>).
        /// </summary>
        /// <remarks>
        /// Supported on Windows 10/11 for Bluetooth GATT peripherals (e.g. Xbox Wireless Controller).
        /// </remarks>
        /// <param name="devicePath">Win32 device interface path.</param>
        /// <returns>Battery percentage (0..100) if exposed by the driver; otherwise, -1.</returns>
        int GetPnpBatteryLevel(string devicePath);

        /// <summary>
        /// Performs an atomic Write-then-Read sequence using Overlapped I/O with optional stream filtering.
        /// Opens the read handle prior to transmitting to guarantee no response frames are dropped.
        /// </summary>
        /// <param name="writePath">Device path to transmit request to.</param>
        /// <param name="request">Request payload buffer (Feature or Output Report).</param>
        /// <param name="readPath">Device path to receive response from.</param>
        /// <param name="response">Response buffer to receive incoming report bytes.</param>
        /// <param name="timeoutMs">Maximum milliseconds to wait for the device response.</param>
        /// <param name="expectedReportId">Optional expected Report ID to filter incoming stream packets (e.g., discarding mouse motion packets).</param>
        /// <returns><c>true</c> if request was sent and matching response received within timeout; otherwise, <c>false</c>.</returns>
        bool Exchange(string writePath, byte[] request, string readPath, byte[] response, int timeoutMs, byte expectedReportId = 0);
    }
}
