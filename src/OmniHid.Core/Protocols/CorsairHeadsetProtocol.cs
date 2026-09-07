using System;
using System.Collections.Generic;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Protocols
{
    /// <summary>
    /// Implements battery telemetry query protocol for Corsair wireless headsets.
    /// Supports Corsair Virtuoso XT, Void Elite Wireless, HS70, and related dongles.
    /// </summary>
    /// <remarks>
    /// Protocol Overview:
    /// - Telemetry Frame: 5 bytes read from battery endpoint (UsagePage 0xFFC5, Usage 0x01).
    /// - Byte Layout:
    ///   [0] = 100
    ///   [1] = 0
    ///   [2] = Battery Level (Bit 7 is microphone mute flag, Bits 0..6 represent percentage)
    ///   [3] = 177
    ///   [4] = Headset Connection &amp; Charging Status:
    ///         0 = Disconnected from receiver dongle
    ///         1 = Normal operation (discharging)
    ///         2 = Low battery warning
    ///         4 or 5 = Actively charging
    /// </remarks>
    public class CorsairHeadsetProtocol : BaseProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Constants
        // ═══════════════════════════════════════════════════════════════════════

        private const ushort USAGE_PAGE_BATTERY   = 0xFFC5;
        private const ushort USAGE_BATTERY        = 0x0001;
        private const int BUFFER_SIZE             = 64;
        private const int MIN_REPORT_LENGTH       = 5;
        private const int TIMEOUT_INPUT_MS        = 100;

        private const int OFFSET_BATTERY          = 2;
        private const int OFFSET_STATUS           = 4;
        private const byte STATUS_DISCONNECTED    = 0;
        private const byte STATUS_CHARGING_4      = 4;
        private const byte STATUS_CHARGING_5      = 5;

        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public override string ProtocolId { get { return "corsair-headset"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public override string ProtocolName { get { return "Corsair Headset Protocol"; } }

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Query Implementation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Queries the Corsair wireless headset for current battery charge and receiver status.
        /// </summary>
        /// <param name="transport">Transport layer abstraction.</param>
        /// <param name="interfaces">List of HID interfaces associated with this headset receiver.</param>
        /// <param name="profile">Declarative profile information.</param>
        /// <returns>Populated <see cref="BatteryTelemetry"/> instance.</returns>
        public override BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            if (interfaces == null || interfaces.Count == 0)
                return BatteryTelemetry.Offline("Headset receiver not connected");

            // Check Windows PnP battery property cache first
            BatteryTelemetry pnpTelemetry;
            if (TryGetPnpBattery(transport, interfaces, out pnpTelemetry))
            {
                return pnpTelemetry;
            }

            // Rank candidate configuration endpoints (prioritizing 0xFFC5:0x0001)
            List<HidDeviceInfo> candidates = GetCandidateInterfaces(interfaces, profile, customUsagePage: USAGE_PAGE_BATTERY, customUsage: USAGE_BATTERY);

            foreach (var targetDev in candidates)
            {
                // Read status report via non-blocking Input Report with fast Feature fallback
                byte[] buffer = new byte[BUFFER_SIZE];
                bool ok = transport.ReadInputReport(targetDev.DevicePath, buffer, TIMEOUT_INPUT_MS);
                if (!ok)
                {
                    buffer = new byte[BUFFER_SIZE];
                    ok = transport.GetFeatureReport(targetDev.DevicePath, 0x00, buffer);
                }

                if (!ok || buffer.Length < MIN_REPORT_LENGTH)
                {
                    continue;
                }

                // Byte 4: Connection status (0 = disconnected from dongle)
                byte statusByte = buffer[OFFSET_STATUS];
                if (statusByte == STATUS_DISCONNECTED)
                {
                    continue;
                }

                // Status 4 or 5 indicates actively charging
                bool isCharging = (statusByte == STATUS_CHARGING_4 || statusByte == STATUS_CHARGING_5);

                // Byte 2: Battery gauge (Bit 7 is mic status; lower 7 bits is 0..100 level)
                byte batteryByte = buffer[OFFSET_BATTERY];
                int level = batteryByte & 0x7F;

                if (level > 100) level = 100;
                if (level < 0) level = 0;

                return BatteryTelemetry.Online(level, isCharging ? BatteryState.Charging : BatteryState.Discharging);
            }

            return BatteryTelemetry.Offline("Headset offline or unreachable");
        }
    }
}
