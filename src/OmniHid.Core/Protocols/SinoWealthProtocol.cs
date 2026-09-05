using System;
using System.Collections.Generic;
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
    public class SinoWealthProtocol : IProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public string ProtocolId { get { return "sinowealth"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public string ProtocolName { get { return "SinoWealth Wireless Protocol"; } }

        /// <summary>
        /// Gets a value indicating whether this protocol can query telemetry when no Windows HID interface handles exist.
        /// SinoWealth devices require direct communication via HID Feature reports.
        /// </summary>
        public bool CanQueryWithoutHidInterfaces { get { return false; } }

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
        public BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            if (interfaces == null || interfaces.Count == 0)
                return BatteryTelemetry.Offline("Device not found");

            // Check Windows PnP battery property cache first
            foreach (var iface in interfaces)
            {
                int pnpLevel = transport.GetPnpBatteryLevel(iface.DevicePath);
                if (pnpLevel >= 0 && pnpLevel <= 100)
                {
                    return BatteryTelemetry.Online(pnpLevel, BatteryState.Discharging);
                }
            }

            // Rank candidate configuration endpoints
            List<HidDeviceInfo> candidates = GetCandidateInterfaces(interfaces, profile);

            foreach (var targetDev in candidates)
            {
                // Command buffer: Report ID 0x04, Command 0x11
                int repLen = Math.Max(8, (int)targetDev.FeatureReportByteLength);
                byte[] cmd = new byte[repLen];
                cmd[0] = 0x04;
                cmd[1] = 0x11;

                byte[] resp = new byte[repLen];
                bool ok = false;

                // Transmit query feature command first, then read response report
                if (transport.SetFeatureReport(targetDev.DevicePath, cmd))
                {
                    System.Threading.Thread.Sleep(15);
                    ok = transport.GetFeatureReport(targetDev.DevicePath, 0x04, resp);
                }

                if (!ok)
                {
                    ok = transport.Exchange(targetDev.DevicePath, cmd, targetDev.DevicePath, resp, 300);
                }

                if (ok && (resp[1] != 0 || resp[2] != 0 || resp[3] != 0))
                {
                    // Byte 2 or 3: Battery percentage
                    int level = resp[2] > 0 ? resp[2] : resp[3];
                    if (level > 100) level = 100;

                    // Byte 4: Charging flag (1 or 2 = Charging)
                    bool isCharging = (resp[4] == 1 || resp[4] == 2);

                    return BatteryTelemetry.Online(level, isCharging ? BatteryState.Charging : BatteryState.Discharging);
                }
            }

            return BatteryTelemetry.Offline("Device offline or sleeping");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Interface Selection Helpers
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Ranks HID interfaces prioritizing profiles, feature report capacity, and vendor collections.
        /// </summary>
        private static List<HidDeviceInfo> GetCandidateInterfaces(List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            List<HidDeviceInfo> candidates = new List<HidDeviceInfo>();

            // Priority 0: Explicit target usage page from profile
            if (profile != null && profile.TargetUsagePage != 0)
            {
                foreach (var iface in interfaces)
                {
                    if (iface.UsagePage == profile.TargetUsagePage &&
                        (profile.TargetUsage == 0 || iface.Usage == profile.TargetUsage))
                    {
                        candidates.Add(iface);
                    }
                }
            }

            // Priority 1: Vendor collections with feature report capability
            foreach (var iface in interfaces)
            {
                if ((iface.UsagePage >= 0xFF00 || iface.FeatureReportByteLength >= 8) && !candidates.Contains(iface))
                {
                    candidates.Add(iface);
                }
            }

            // Priority 2: Fallback to all interfaces
            if (candidates.Count == 0)
            {
                candidates.AddRange(interfaces);
            }

            return candidates;
        }
    }
}