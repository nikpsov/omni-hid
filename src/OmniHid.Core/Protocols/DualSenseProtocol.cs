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
    public class DualSenseProtocol : IProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public string ProtocolId { get { return "sony-dualsense"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public string ProtocolName { get { return "Sony DualSense / DualShock Protocol"; } }

        /// <summary>
        /// Gets a value indicating whether this protocol can query telemetry when no Windows HID interface handles exist.
        /// Sony PlayStation controllers require direct communication via HID Input or Feature reports.
        /// </summary>
        public bool CanQueryWithoutHidInterfaces { get { return false; } }

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
        public BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            if (interfaces == null || interfaces.Count == 0)
                return BatteryTelemetry.Offline("Controller not found");

            // Check Windows PnP battery property cache first
            foreach (var iface in interfaces)
            {
                int pnpLevel = transport.GetPnpBatteryLevel(iface.DevicePath);
                if (pnpLevel >= 0 && pnpLevel <= 100)
                {
                    return BatteryTelemetry.Online(pnpLevel, BatteryState.Discharging);
                }
            }

            // Rank candidate gamepad endpoints
            List<HidDeviceInfo> candidates = GetCandidateInterfaces(interfaces, profile);

            foreach (var targetDev in candidates)
            {
                // Read Input Report via Overlapped I/O with Feature Report 0x05 fallback
                int bufLen = Math.Max(78, (int)targetDev.InputReportByteLength);
                byte[] buffer = new byte[bufLen];
                bool ok = transport.ReadInputReport(targetDev.DevicePath, buffer, 300);
                if (!ok)
                {
                    buffer = new byte[bufLen];
                    ok = transport.GetFeatureReport(targetDev.DevicePath, 0x05, buffer);
                }

                if (!ok)
                {
                    continue;
                }

                byte batteryByte = 0;

                // Report ID 0x01: USB standard DualSense input frame (offset 53)
                if (buffer[0] == 0x01 && buffer.Length >= 54)
                {
                    batteryByte = buffer[53];
                }
                // Report ID 0x31: Bluetooth extended DualSense input frame (offset 54)
                else if (buffer[0] == 0x31 && buffer.Length >= 55)
                {
                    batteryByte = buffer[54];
                }
                // Legacy DualShock 4 frame (offset 30)
                else if (buffer.Length >= 31)
                {
                    batteryByte = buffer[30];
                }
                else
                {
                    continue;
                }

                // Lower 4 bits: 0..10 level (multiply by 10 to get 0..100 percentage)
                int rawLevel = batteryByte & 0x0F;
                int percent = Math.Min(100, rawLevel * 10);

                // Bit 4 (0x10): 1 = charging, 0 = discharging
                bool isCharging = (batteryByte & 0x10) != 0;

                return BatteryTelemetry.Online(percent, isCharging ? BatteryState.Charging : BatteryState.Discharging);
            }

            return BatteryTelemetry.Offline("Controller sleeping or disconnected");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Interface Selection Helpers
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Ranks HID interfaces prioritizing profiles, gamepad collections (UsagePage 0x01, Usage 0x05), and input buffer capacity.
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

            // Priority 1: Gamepad collection (Generic Desktop 0x0001, Gamepad 0x0005)
            foreach (var iface in interfaces)
            {
                if (iface.UsagePage == 0x0001 && iface.Usage == 0x0005 && !candidates.Contains(iface))
                {
                    candidates.Add(iface);
                }
            }

            // Priority 2: Endpoints with input reports >= 64 bytes
            foreach (var iface in interfaces)
            {
                if (iface.InputReportByteLength >= 64 && !candidates.Contains(iface))
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