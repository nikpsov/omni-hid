using System;
using System.Collections.Generic;
using System.Threading;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Protocols
{
    /// <summary>
    /// Implements battery telemetry query protocol for SinoWealth wireless mouse MCUs.
    /// Used by Glorious Model O/D Wireless, Lamzu, and OEM gaming mice.
    /// </summary>
    /// <remarks>
    /// Protocol Overview:
    /// - Command Packet: Feature Report 0x04 with payload [0x04, 0x11, ...].
    /// - Response Packet: Byte 2 (or 3) contains battery percentage (0..100), Byte 4 indicates charging status (1 or 2 = charging).
    /// </remarks>
    public class SinoWealthProtocol : BaseProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Constants
        // ═══════════════════════════════════════════════════════════════════════

        private const byte REPORT_ID_FEATURE        = 0x04;
        private const byte CMD_QUERY_BATTERY        = 0x11;
        private const int MIN_REPORT_LENGTH         = 8;
        private const int OFFSET_BATTERY_PRIMARY    = 2;
        private const int OFFSET_BATTERY_SECONDARY  = 3;
        private const int OFFSET_CHARGING_STATUS    = 4;
        private const int DELAY_FEATURE_MS          = 10;
        private const int TIMEOUT_EXCHANGE_MS       = 250;

        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public override string ProtocolId { get { return "sinowealth"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public override string ProtocolName { get { return "SinoWealth Wireless Protocol"; } }

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Query Implementation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Queries the SinoWealth wireless peripheral for current battery and charging status.
        /// </summary>
        /// <param name="transport">Transport layer abstraction.</param>
        /// <param name="interfaces">List of HID interfaces associated with this mouse.</param>
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

            // Rank candidate configuration endpoints (at least 8 bytes Feature report)
            List<HidDeviceInfo> candidates = GetCandidateInterfaces(interfaces, profile, minFeatureLen: MIN_REPORT_LENGTH);

            foreach (var targetDev in candidates)
            {
                // Command buffer: Report ID 0x04, Command 0x11
                int repLen = Math.Max(MIN_REPORT_LENGTH, (int)targetDev.FeatureReportByteLength);
                byte[] cmd = new byte[repLen];
                cmd[0] = REPORT_ID_FEATURE;
                cmd[1] = CMD_QUERY_BATTERY;

                byte[] resp = new byte[repLen];
                bool ok = false;

                // Transmit query feature command first, then read response report with brief delay
                if (transport.SetFeatureReport(targetDev.DevicePath, cmd))
                {
                    Thread.Sleep(DELAY_FEATURE_MS);
                    ok = transport.GetFeatureReport(targetDev.DevicePath, REPORT_ID_FEATURE, resp);
                }

                if (!ok)
                {
                    ok = transport.Exchange(targetDev.DevicePath, cmd, targetDev.DevicePath, resp, TIMEOUT_EXCHANGE_MS);
                }

                if (ok && (resp[1] != 0 || resp[OFFSET_BATTERY_PRIMARY] != 0 || resp[OFFSET_BATTERY_SECONDARY] != 0))
                {
                    // Byte 2 or 3: Battery percentage
                    int level = resp[OFFSET_BATTERY_PRIMARY] > 0 ? resp[OFFSET_BATTERY_PRIMARY] : resp[OFFSET_BATTERY_SECONDARY];
                    if (level > 100) level = 100;

                    // Byte 4: Charging flag (1 or 2 = Charging)
                    byte chgByte = resp[OFFSET_CHARGING_STATUS];
                    bool isCharging = (chgByte == 1 || chgByte == 2);

                    return BatteryTelemetry.Online(level, isCharging ? BatteryState.Charging : BatteryState.Discharging);
                }
            }

            return BatteryTelemetry.Offline("Device offline or sleeping");
        }
    }
}