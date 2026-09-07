using System;
using System.Collections.Generic;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Protocols
{
    /// <summary>
    /// Implements battery telemetry query protocol for SteelSeries wireless peripherals.
    /// Supports SteelSeries Arctis Nova 7, Arctis 7+, Aerox 3 Wireless, Rival 650, etc.
    /// </summary>
    /// <remarks>
    /// Protocol Overview:
    /// - Command Packet: 32 bytes via Feature or Output Report with prefix [0x06, 0x12, ...].
    /// - Response Packet: 32 bytes, where Byte 2 contains battery level (0..100) and Byte 3 indicates charging status (0x01 = charging).
    /// </remarks>
    public class SteelSeriesProtocol : BaseProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Constants
        // ═══════════════════════════════════════════════════════════════════════

        private const byte REPORT_ID_QUERY          = 0x06;
        private const byte CMD_QUERY_BATTERY        = 0x12;
        private const int PACKET_LENGTH             = 32;
        private const int OFFSET_BATTERY_LEVEL      = 2;
        private const int OFFSET_CHARGING_STATUS    = 3;
        private const byte STATUS_CHARGING          = 0x01;
        private const int TIMEOUT_EXCHANGE_MS       = 400;

        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public override string ProtocolId { get { return "steelseries"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public override string ProtocolName { get { return "SteelSeries Wireless Protocol"; } }

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Query Implementation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Queries the SteelSeries peripheral for current battery and charging status.
        /// </summary>
        /// <param name="transport">Transport layer abstraction.</param>
        /// <param name="interfaces">List of HID interfaces associated with this device.</param>
        /// <param name="profile">Declarative profile information.</param>
        /// <returns>Populated <see cref="BatteryTelemetry"/> instance.</returns>
        public override BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            if (interfaces == null || interfaces.Count == 0)
                return BatteryTelemetry.Offline("Device not found");

            // Check Windows PnP battery property cache first
            BatteryTelemetry pnpTelemetry;
            if (TryGetPnpBattery(transport, interfaces, out pnpTelemetry))
            {
                return pnpTelemetry;
            }

            // Rank candidate configuration endpoints (at least 32 bytes Output or Feature report)
            List<HidDeviceInfo> candidates = GetCandidateInterfaces(interfaces, profile, minFeatureLen: PACKET_LENGTH, minOutputLen: PACKET_LENGTH);

            foreach (var targetDev in candidates)
            {
                // Command buffer: Report ID 0x06, Command 0x12 (Battery Request)
                int repLen = Math.Max(PACKET_LENGTH, Math.Max((int)targetDev.OutputReportByteLength, (int)targetDev.FeatureReportByteLength));
                byte[] cmd = new byte[repLen];
                cmd[0] = REPORT_ID_QUERY;
                cmd[1] = CMD_QUERY_BATTERY;

                byte[] resp = new byte[repLen];
                bool ok = transport.Exchange(targetDev.DevicePath, cmd, targetDev.DevicePath, resp, TIMEOUT_EXCHANGE_MS);
                if (ok && (resp[0] != 0 || resp[1] != 0 || resp[OFFSET_BATTERY_LEVEL] != 0))
                {
                    // Byte 2: Battery level (0..100)
                    int level = resp[OFFSET_BATTERY_LEVEL];
                    if (level > 100) level = 100;

                    // Byte 3: Charging status (0x01 = Charging)
                    bool isCharging = (resp[OFFSET_CHARGING_STATUS] == STATUS_CHARGING);

                    return BatteryTelemetry.Online(level, isCharging ? BatteryState.Charging : BatteryState.Discharging);
                }
            }

            return BatteryTelemetry.Offline("Device offline or turned off");
        }
    }
}