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
    public class CorsairHeadsetProtocol : IProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public string ProtocolId { get { return "corsair-headset"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public string ProtocolName { get { return "Corsair Headset Protocol"; } }

        /// <summary>
        /// Gets a value indicating whether this protocol can query telemetry when no Windows HID interface handles exist.
        /// Corsair headsets require direct communication via HID Input or Feature reports.
        /// </summary>
        public bool CanQueryWithoutHidInterfaces { get { return false; } }

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
        public BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            if (interfaces == null || interfaces.Count == 0)
                return BatteryTelemetry.Offline("Headset receiver not connected");

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
                // Read status report via non-blocking Input Report with Feature fallback
                byte[] buffer = new byte[64];
                bool ok = transport.ReadInputReport(targetDev.DevicePath, buffer, 250);
                if (!ok)
                {
                    buffer = new byte[64];
                    ok = transport.GetFeatureReport(targetDev.DevicePath, 0x00, buffer);
                }

                if (!ok || buffer.Length < 5)
                {
                    continue;
                }

                // Byte 4: Connection status (0 = disconnected from dongle)
                byte statusByte = buffer[4];
                if (statusByte == 0)
                {
                    continue;
                }

                // Status 4 or 5 indicates actively charging
                bool isCharging = (statusByte == 4 || statusByte == 5);

                // Byte 2: Battery gauge (Bit 7 is mic status; lower 7 bits is 0..100 level)
                byte batteryByte = buffer[2];
                int level = batteryByte & 0x7F;

                if (level > 100) level = 100;
                if (level < 0) level = 0;

                return BatteryTelemetry.Online(level, isCharging ? BatteryState.Charging : BatteryState.Discharging);
            }

            return BatteryTelemetry.Offline("Headset offline or unreachable");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Interface Selection Helpers
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Ranks HID interfaces prioritizing profiles, battery telemetry collection (0xFFC5), and vendor collections.
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

            // Priority 1: Corsair dedicated battery usage page (UsagePage 0xFFC5, Usage 0x0001)
            foreach (var iface in interfaces)
            {
                if (iface.UsagePage == 0xFFC5 && iface.Usage == 0x0001 && !candidates.Contains(iface))
                {
                    candidates.Add(iface);
                }
            }

            // Priority 2: Other vendor-defined collections
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
