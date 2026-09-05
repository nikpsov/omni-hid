using System;
using System.Collections.Generic;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Abstractions
{
    /// <summary>
    /// Represents a monitored hardware peripheral device managed by OmniHID.
    /// </summary>
    public interface IOmniDevice
    {
        /// <summary>
        /// Gets the unique logical identifier for this peripheral (e.g., "046D:C094:logitech-hidpp").
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Gets the friendly model name of the device (e.g., "Logitech G PRO X Superlight 2").
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the 16-bit USB Vendor ID (VID) associated with the device manufacturer.
        /// </summary>
        ushort VendorId { get; }

        /// <summary>
        /// Gets the 16-bit USB Product ID (PID) associated with the device model or receiver dongle.
        /// </summary>
        ushort ProductId { get; }

        /// <summary>
        /// Gets the functional category of the peripheral (Mouse, Keyboard, Headset, Gamepad).
        /// </summary>
        DeviceCategory Category { get; }

        /// <summary>
        /// Gets the declared hardware capabilities supported by this device.
        /// </summary>
        DeviceCapabilities Capabilities { get; }

        /// <summary>
        /// Gets the unique identifier of the communication protocol driver used by this device.
        /// </summary>
        string ProtocolId { get; }

        /// <summary>
        /// Gets a value indicating whether the peripheral is currently connected and reachable.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Gets a value indicating whether the active connection is via direct USB cable.
        /// </summary>
        bool IsWired { get; }

        /// <summary>
        /// Gets a value indicating whether this device was instantiated from an external JSON profile.
        /// </summary>
        bool IsCustomProfile { get; }

        /// <summary>
        /// Gets the most recent cached battery and power telemetry snapshot for this device.
        /// </summary>
        BatteryTelemetry Telemetry { get; }

        /// <summary>
        /// Gets the list of physical HID interface endpoints aggregated under this logical peripheral.
        /// </summary>
        IReadOnlyList<HidDeviceInfo> Interfaces { get; }

        /// <summary>
        /// Actively queries the physical hardware over HID to refresh and return current battery telemetry.
        /// </summary>
        /// <returns>A fresh <see cref="BatteryTelemetry"/> instance reflecting current device state.</returns>
        BatteryTelemetry RefreshTelemetry();
    }
}
