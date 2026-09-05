using System;
using System.Collections.Generic;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Protocols
{
    /// <summary>
    /// Implements battery telemetry query protocol for CompX CX52850 wireless gaming mouse MCUs.
    /// Used by ARDOR Gaming Phantom PRO, Fury, Immortality, and other CompX-based OEM peripherals.
    /// </summary>
    /// <remarks>
    /// Protocol Overview:
    /// - Command Packet: 32 bytes via Feature Report starting with [0x02, 0x03, ...].
    /// - Response Packet: 32 bytes, where Byte 2 contains battery percentage (0..100) and Byte 3 indicates charging status (0x01 = charging).
    /// </remarks>
    public class CompxProtocol : IProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public string ProtocolId { get { return "compx"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public string ProtocolName { get { return "Compx CX52850 Protocol"; } }

        /// <summary>
        /// Gets a value indicating whether this protocol can query telemetry when no Windows HID interface handles exist.
        /// CompX peripherals require direct communication via HID Feature reports.
        /// </summary>
        public bool CanQueryWithoutHidInterfaces { get { return false; } }

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Query Implementation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Queries the CompX wireless peripheral for current battery and charging status.
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
                // Command buffer: Report ID 0x02, Subcommand 0x03 (Battery Query)
                int repLen = Math.Max(32, (int)targetDev.FeatureReportByteLength);
                byte[] cmd = new byte[repLen];
                cmd[0] = 0x02;
                cmd[1] = 0x03;

                byte[] resp = new byte[repLen];
                bool ok = transport.Exchange(targetDev.DevicePath, cmd, targetDev.DevicePath, resp, 400);
                if (ok && (resp[2] > 0 || resp[3] == 0x01))
                {
                    // Byte 2: Battery level (0..100)
                    int level = resp[2];
                    if (level > 100) level = 100;

                    // Byte 3: Charging flag (0x01 = Charging)
                    bool isCharging = (resp[3] == 0x01);

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

            // Priority 1: Interfaces with feature report length >= 32
            foreach (var iface in interfaces)
            {
                if (iface.FeatureReportByteLength >= 32 && !candidates.Contains(iface))
                {
                    candidates.Add(iface);
                }
            }

            // Priority 2: Vendor-defined usage pages
            foreach (var iface in interfaces)
            {
                if (iface.UsagePage >= 0xFF00 && !candidates.Contains(iface))
                {
                    candidates.Add(iface);
                }
            }

            // Priority 3: Fallback to all interfaces
            if (candidates.Count == 0)
            {
                candidates.AddRange(interfaces);
            }

            return candidates;
        }
    }
}