using System;

namespace OmniHid.Core.Transport
{
    /// <summary>
    /// Encapsulates metadata, endpoint descriptors, and device path for an enumerated HID interface on Windows.
    /// </summary>
    public class HidDeviceInfo
    {
        /// <summary>
        /// Gets or sets the Win32 device interface path used to open file handles (e.g., "\\?\hid#vid_046d&amp;pid_c094...").
        /// </summary>
        public string DevicePath { get; set; }

        /// <summary>
        /// Gets or sets the 16-bit USB Vendor ID (VID).
        /// </summary>
        public ushort VendorId { get; set; }

        /// <summary>
        /// Gets or sets the 16-bit USB Product ID (PID).
        /// </summary>
        public ushort ProductId { get; set; }

        /// <summary>
        /// Gets or sets the 16-bit binary-coded decimal hardware revision/version number.
        /// </summary>
        public ushort VersionNumber { get; set; }

        /// <summary>
        /// Gets or sets the HID Top-Level Collection Usage identifier (e.g., 0x0002 for Mouse, 0x0006 for Keyboard).
        /// </summary>
        public ushort Usage { get; set; }

        /// <summary>
        /// Gets or sets the HID Top-Level Collection Usage Page (e.g., 0x0001 for Generic Desktop, 0xFF00+ for Vendor).
        /// </summary>
        public ushort UsagePage { get; set; }

        /// <summary>
        /// Gets or sets the manufacturer description string read from the device USB descriptor.
        /// </summary>
        public string ManufacturerString { get; set; }

        /// <summary>
        /// Gets or sets the product name string read from the device USB descriptor.
        /// </summary>
        public string ProductString { get; set; }

        /// <summary>
        /// Gets or sets the device serial number string, if provided by the firmware.
        /// </summary>
        public string SerialNumber { get; set; }

        /// <summary>
        /// Gets or sets the maximum byte length of an Input Report supported by this interface (including Report ID).
        /// </summary>
        public ushort InputReportByteLength { get; set; }

        /// <summary>
        /// Gets or sets the maximum byte length of an Output Report supported by this interface (including Report ID).
        /// </summary>
        public ushort OutputReportByteLength { get; set; }

        /// <summary>
        /// Gets or sets the maximum byte length of a Feature Report supported by this interface (including Report ID).
        /// </summary>
        public ushort FeatureReportByteLength { get; set; }

        /// <summary>
        /// Returns a formatted string representing the device interface IDs and name.
        /// </summary>
        /// <returns>Summary of VID, PID, Usage Page, Usage, and Product name.</returns>
        public override string ToString()
        {
            return string.Format("VID:0x{0:X4} PID:0x{1:X4} Page:0x{2:X4} Usage:0x{3:X4} '{4}'",
                VendorId, ProductId, UsagePage, Usage, ProductString ?? "");
        }
    }
}
