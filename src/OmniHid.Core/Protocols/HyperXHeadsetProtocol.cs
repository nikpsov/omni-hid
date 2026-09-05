using System;
using System.Collections.Generic;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Protocols
{
    /// <summary>
    /// Implements battery telemetry query protocol for HyperX / HP wireless gaming headsets.
    /// Supports HyperX Cloud Alpha Wireless, Cloud II Wireless, and related wireless dongles.
    /// </summary>
    /// <remarks>
    /// Protocol Overview:
    /// - Two-phase query:
    ///   1. Battery Level: Send 52-byte Output Report [0x21, 0xBB, 0x0B, 0x02, ...].
    ///      Response header is [0x06, 0xFF, 0xBB], Byte 7 = percentage (0..100), Bytes 5-6 = voltage in mV.
    ///   2. Charging Status: Send 52-byte Output Report [0x21, 0xBB, 0x0B, 0x03, ...].
    ///      Response Byte 4 = 1 if actively charging.
    /// </remarks>
    public class HyperXHeadsetProtocol : IProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Properties & Commands
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public string ProtocolId { get { return "hyperx-headset"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public string ProtocolName { get { return "HyperX Headset Protocol"; } }

        /// <summary>
        /// Gets a value indicating whether this protocol can query telemetry when no Windows HID interface handles exist.
        /// HyperX headsets require direct communication via HID Output and Input reports.
        /// </summary>
        public bool CanQueryWithoutHidInterfaces { get { return false; } }

        private const byte CMD_GET_BATTERY_LEVEL    = 0x02;
        private const byte CMD_GET_BATTERY_CHARGING = 0x03;

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Query Implementation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Queries the HyperX wireless headset for battery level percentage, voltage, and charging state.
        /// </summary>
        /// <param name="transport">Transport layer abstraction.</param>
        /// <param name="interfaces">List of HID interfaces associated with this headset dongle.</param>
        /// <param name="profile">Declarative profile information.</param>
        /// <returns>Populated <see cref="BatteryTelemetry"/> instance.</returns>
        public BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            if (interfaces == null || interfaces.Count == 0)
                return BatteryTelemetry.Offline("Headset dongle not connected");

            // Check Windows PnP cache first
            foreach (var iface in interfaces)
            {
                int pnpLevel = transport.GetPnpBatteryLevel(iface.DevicePath);
                if (pnpLevel >= 0 && pnpLevel <= 100)
                {
                    return BatteryTelemetry.Online(pnpLevel, BatteryState.Discharging);
                }
            }

            // Rank candidate interfaces for 52-byte Output Reports
            List<HidDeviceInfo> candidates = GetCandidateInterfaces(interfaces, profile);

            foreach (var targetDev in candidates)
            {
                // 1. Send battery level request (CMD 0x02)
                byte[] cmd = new byte[Math.Max(52, (int)targetDev.OutputReportByteLength)];
                cmd[0] = 0x21;
                cmd[1] = 0xBB;
                cmd[2] = 0x0B;
                cmd[3] = CMD_GET_BATTERY_LEVEL;

                bool written = transport.WriteOutputReport(targetDev.DevicePath, cmd);
                if (!written)
                {
                    continue;
                }

                // Read 20-byte response frame
                byte[] response = new byte[20];
                bool readOk = transport.ReadInputReport(targetDev.DevicePath, response, 350);
                if (!readOk || response.Length < 8)
                {
                    continue;
                }

                // Verify response magic header [0x06, 0xFF, 0xBB]
                if (response[0] == 0x06 && response[1] == 0xFF && response[2] == 0xBB)
                {
                    int level = response[7];
                    int voltage = (response[5] << 8) | response[6];

                    if (level > 100) level = 100;
                    if (level < 0) level = 0;

                    // 2. Query charging status (CMD 0x03)
                    byte[] chargeCmd = new byte[cmd.Length];
                    chargeCmd[0] = 0x21;
                    chargeCmd[1] = 0xBB;
                    chargeCmd[2] = 0x0B;
                    chargeCmd[3] = CMD_GET_BATTERY_CHARGING;

                    bool isCharging = false;
                    if (transport.WriteOutputReport(targetDev.DevicePath, chargeCmd))
                    {
                        byte[] chargeResp = new byte[20];
                        if (transport.ReadInputReport(targetDev.DevicePath, chargeResp, 200))
                        {
                            if (chargeResp.Length >= 5 && chargeResp[4] == 1)
                            {
                                isCharging = true;
                            }
                        }
                    }

                    return BatteryTelemetry.Online(level, isCharging ? BatteryState.Charging : BatteryState.Discharging, voltage);
                }
            }

            return BatteryTelemetry.Offline("Headset offline or sleeping");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Interface Selection Helpers
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Ranks HID interfaces prioritizing profiles, output buffer capacity, and vendor collections.
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

            // Priority 1: Interfaces capable of output reports of at least 52 bytes
            foreach (var iface in interfaces)
            {
                if (iface.OutputReportByteLength >= 52 && !candidates.Contains(iface))
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
