using System;
using System.Collections.Generic;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Protocols
{
    /// <summary>
    /// Implements battery telemetry query protocol for Sony PlayStation controllers (DualSense, DualSense Edge, DualShock 4).
    /// Supports both USB direct connection and Bluetooth wireless link.
    /// </summary>
    /// <remarks>
    /// Report Formats:
    /// - USB Connection: Standard Input Report (Report ID 0x01, 64 bytes).
    ///   Byte 53 contains battery data: lower 4 bits (0x0F) = charge level (0..10), bit 4 (0x10) = charging flag.
    /// - Bluetooth Connection: Extended Input Report (Report ID 0x31, 78 bytes).
    ///   Byte 54 contains battery data.
    /// - Legacy DualShock 4: Input Report 0x01, Byte 30 contains battery level.
    /// </remarks>
    public class DualSenseProtocol : BaseProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Constants
        // ═══════════════════════════════════════════════════════════════════════

        private const byte REPORT_ID_USB            = 0x01;
        private const byte REPORT_ID_BT             = 0x31;
        private const byte REPORT_ID_FEATURE        = 0x05;

        private const ushort USAGE_PAGE_GENERIC     = 0x0001;
        private const ushort USAGE_GAMEPAD          = 0x0005;

        private const int OFFSET_USB_BATTERY        = 53;
        private const int OFFSET_BT_BATTERY         = 54;
        private const int OFFSET_DS4_BATTERY        = 30;

        private const byte MASK_BATTERY_LEVEL       = 0x0F;
        private const byte MASK_CHARGING            = 0x10;

        private const int MIN_BUFFER_LENGTH         = 78;
        private const int TIMEOUT_INPUT_MS          = 100;

        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public override string ProtocolId { get { return "sony-dualsense"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public override string ProtocolName { get { return "Sony DualSense / DualShock Protocol"; } }

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Query Implementation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Queries the Sony controller for current battery percentage and charging state.
        /// </summary>
        /// <param name="transport">Transport layer abstraction.</param>
        /// <param name="interfaces">List of HID interfaces associated with this controller.</param>
        /// <param name="profile">Declarative profile information.</param>
        /// <returns>Populated <see cref="BatteryTelemetry"/> instance.</returns>
        public override BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            if (interfaces == null || interfaces.Count == 0)
                return BatteryTelemetry.Offline("Controller not found");

            // Check Windows PnP battery property cache first
            BatteryTelemetry pnpTelemetry;
            if (TryGetPnpBattery(transport, interfaces, out pnpTelemetry))
            {
                return pnpTelemetry;
            }

            // Rank candidate gamepad endpoints (prioritizing 0x0001:0x0005 and reports >= 64 bytes)
            List<HidDeviceInfo> candidates = GetCandidateInterfaces(interfaces, profile, minInputLen: 64, customUsagePage: USAGE_PAGE_GENERIC, customUsage: USAGE_GAMEPAD);

            foreach (var targetDev in candidates)
            {
                // Read Input Report via Overlapped I/O with fast Feature Report fallback
                int bufLen = Math.Max(MIN_BUFFER_LENGTH, (int)targetDev.InputReportByteLength);
                byte[] buffer = new byte[bufLen];
                bool ok = transport.ReadInputReport(targetDev.DevicePath, buffer, TIMEOUT_INPUT_MS);
                if (!ok)
                {
                    buffer = new byte[bufLen];
                    ok = transport.GetFeatureReport(targetDev.DevicePath, REPORT_ID_FEATURE, buffer);
                }

                if (!ok)
                {
                    continue;
                }

                byte batteryByte = 0;

                // Report ID 0x01: USB standard DualSense input frame (offset 53)
                if (buffer[0] == REPORT_ID_USB && buffer.Length > OFFSET_USB_BATTERY)
                {
                    batteryByte = buffer[OFFSET_USB_BATTERY];
                }
                // Report ID 0x31: Bluetooth extended DualSense input frame (offset 54)
                else if (buffer[0] == REPORT_ID_BT && buffer.Length > OFFSET_BT_BATTERY)
                {
                    batteryByte = buffer[OFFSET_BT_BATTERY];
                }
                // Legacy DualShock 4 frame (offset 30)
                else if (buffer.Length > OFFSET_DS4_BATTERY)
                {
                    batteryByte = buffer[OFFSET_DS4_BATTERY];
                }
                else
                {
                    continue;
                }

                // Lower 4 bits: 0..10 level (multiply by 10 to get 0..100 percentage)
                int rawLevel = batteryByte & MASK_BATTERY_LEVEL;
                int percent = Math.Min(100, rawLevel * 10);

                // Bit 4 (0x10): 1 = charging, 0 = discharging
                bool isCharging = (batteryByte & MASK_CHARGING) != 0;

                return BatteryTelemetry.Online(percent, isCharging ? BatteryState.Charging : BatteryState.Discharging);
            }

            return BatteryTelemetry.Offline("Controller sleeping or disconnected");
        }
    }
}