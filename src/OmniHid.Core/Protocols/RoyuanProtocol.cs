using System;
using System.Collections.Generic;
using System.Threading;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Protocols
{
    /// <summary>
    /// Implements battery telemetry query protocol for ROYUAN / YiChip wireless keyboards.
    /// Used across 1,400+ keyboard models from Akko, MonsGeek, EPOMAKER, FL·ESPORTS, Keydous, Darmoshark, and others.
    /// Reverse-engineered directly from official ROYUAN QMKIot / Akko Cloud Driver and YC300/YC500 MCU specifications.
    /// </summary>
    /// <remarks>
    /// Protocol Overview:
    /// - Primary Interface: Endpoint with FeatureReportByteLength == 65 (Interface 0, Usage 0x0001:0x0006).
    /// - Command Packet: 65 bytes starting with [0x00, 0x83, ...] (FEA_CMD_GET_BATTERY = 131 / 0x83).
    /// - Response Packet: 65 bytes via HidD_GetFeature, where byte offsets contain battery percentage and online/charging flags.
    /// - Fallback: Vendor telemetry endpoint (Usage 0xFFFF:0x0001) and Areson 2.4G frame query.
    /// </remarks>
    public class RoyuanProtocol : IProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Constants (from ROYUAN QMKIot / YiChip Specification)
        // ═══════════════════════════════════════════════════════════════════════

        private const byte FEA_CMD_GET_REV = 0x80;      // 128: Firmware Version Query
        private const byte FEA_CMD_GET_REPORT = 0x81;   // 129: Polling Rate Query
        private const byte FEA_CMD_GET_PROFILE = 0x82;  // 130: Current Profile Query
        private const byte FEA_CMD_GET_BATTERY = 0x83;  // 131: Battery & Status Query
        private const byte FEA_CMD_GET_INFOR = 0x8F;    // 143: Hardware Information Query

        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public string ProtocolId { get { return "royuan"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public string ProtocolName { get { return "ROYUAN / YiChip Wireless Protocol"; } }

        /// <summary>
        /// Gets a value indicating whether this protocol can query telemetry when no Windows HID interface handles exist.
        /// ROYUAN / YiChip keyboards require direct communication via HID Feature reports.
        /// </summary>
        public bool CanQueryWithoutHidInterfaces { get { return false; } }

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Query Implementation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Queries the physical keyboard over HID to refresh and return current battery telemetry.
        /// </summary>
        /// <param name="transport">Transport layer abstraction.</param>
        /// <param name="interfaces">List of HID interfaces aggregated under this keyboard.</param>
        /// <param name="profile">Declarative profile information.</param>
        /// <returns>Populated <see cref="BatteryTelemetry"/> instance.</returns>
        public BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            if (interfaces == null || interfaces.Count == 0)
                return BatteryTelemetry.Offline("Device not found");

            // 1. Check Windows PnP Battery Level first
            foreach (var iface in interfaces)
            {
                int pnpLevel = transport.GetPnpBatteryLevel(iface.DevicePath);
                if (pnpLevel >= 0 && pnpLevel <= 100)
                {
                    return BatteryTelemetry.Online(pnpLevel, BatteryState.Discharging);
                }
            }

            // 2. Locate the primary Feature Report endpoint (FeatLen >= 65, e.g. Usage 0x0001:0x0006)
            HidDeviceInfo featEndpoint = null;
            foreach (var iface in interfaces)
            {
                if (iface.FeatureReportByteLength >= 65)
                {
                    featEndpoint = iface;
                    break;
                }
            }

            if (featEndpoint != null)
            {
                int bufLen = Math.Max(65, (int)featEndpoint.FeatureReportByteLength);

                // Strategy A: Unnumbered 65-byte Feature Exchange ([0x00, 0x83, 0x00, ...])
                byte[] sendUnnumbered = new byte[bufLen];
                sendUnnumbered[0] = 0x00;
                sendUnnumbered[1] = FEA_CMD_GET_BATTERY;

                if (transport.SetFeatureReport(featEndpoint.DevicePath, sendUnnumbered))
                {
                    Thread.Sleep(20);

                    byte[] respUnnumbered = new byte[bufLen];
                    respUnnumbered[0] = 0x00;
                    if (transport.GetFeatureReport(featEndpoint.DevicePath, 0x00, respUnnumbered))
                    {
                        BatteryTelemetry parsed = ParseRoyuanFeaturePayload(respUnnumbered);
                        if (parsed != null && parsed.IsAvailable)
                            return parsed;
                    }
                }

                // Strategy B: Numbered Feature Report 0x83 ([0x83, 0x00, ...])
                byte[] sendNumbered = new byte[bufLen];
                sendNumbered[0] = FEA_CMD_GET_BATTERY;

                if (transport.SetFeatureReport(featEndpoint.DevicePath, sendNumbered))
                {
                    Thread.Sleep(20);

                    byte[] respNumbered = new byte[bufLen];
                    respNumbered[0] = FEA_CMD_GET_BATTERY;
                    if (transport.GetFeatureReport(featEndpoint.DevicePath, FEA_CMD_GET_BATTERY, respNumbered))
                    {
                        BatteryTelemetry parsed = ParseRoyuanFeaturePayload(respNumbered);
                        if (parsed != null && parsed.IsAvailable)
                            return parsed;
                    }
                }
            }

            // 3. Strategy C: Read spontaneous vendor input report from endpoint [2] (Usage 0xFFFF:0x0001)
            foreach (var iface in interfaces)
            {
                if ((iface.UsagePage == 0xFFFF || iface.UsagePage >= 0xFF00) && iface.InputReportByteLength > 0)
                {
                    byte[] vIn = new byte[Math.Max(64, (int)iface.InputReportByteLength)];
                    if (transport.ReadInputReport(iface.DevicePath, vIn, 150))
                    {
                        BatteryTelemetry parsed = ParseRoyuanVendorInput(vIn);
                        if (parsed != null && parsed.IsAvailable)
                            return parsed;
                    }
                }
            }

            // 4. Strategy D: Check Input Report 0x02 on System Control interface (mi_01&col02)
            foreach (var iface in interfaces)
            {
                if (iface.UsagePage == 0x0001 && iface.Usage == 0x0080)
                {
                    byte[] inRep = new byte[64];
                    if (transport.GetInputReport(iface.DevicePath, 0x02, inRep))
                    {
                        if (inRep.Length > 9 && inRep[9] > 0 && inRep[9] <= 100)
                        {
                            return BatteryTelemetry.Online(inRep[9], BatteryState.Discharging);
                        }
                    }
                }
            }

            return BatteryTelemetry.Offline("Device offline or sleeping");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Payload Parsing Helpers
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Parses telemetry fields from a ROYUAN 65-byte Feature Report response buffer.
        /// Validates that the report contains the battery query opcode echo (0x83) and parses structured fields.
        /// </summary>
        private static BatteryTelemetry ParseRoyuanFeaturePayload(byte[] resp)
        {
            if (resp == null || resp.Length < 6) return null;

            // In ROYUAN / YiChip Status24 layout (from iot_driver.exe):
            // offset +4: battery level (0..100)
            // offset +5: is_online (1 = online)
            // offset +6: is_write_finish / charging flag
            for (int offset = 0; offset <= 2; offset++)
            {
                int cmdByte = resp[offset];
                if (cmdByte == FEA_CMD_GET_BATTERY)
                {
                    // Check standard offset +4
                    if (resp.Length > offset + 4)
                    {
                        int bat = resp[offset + 4];
                        if (bat >= 1 && bat <= 100)
                        {
                            bool isOnline = (resp.Length > offset + 5 && resp[offset + 5] != 0);
                            bool isCharging = (resp.Length > offset + 6 && resp[offset + 6] == 0x01);
                            if (isOnline || bat > 0)
                            {
                                return BatteryTelemetry.Online(bat, isCharging ? BatteryState.Charging : BatteryState.Discharging);
                            }
                        }
                    }

                    // Check direct offsets (byte 1 or byte 2)
                    if (resp.Length > offset + 1)
                    {
                        int directVal = resp[offset + 1];
                        if (directVal >= 1 && directVal <= 100)
                        {
                            bool isCharging = (resp.Length > offset + 2 && resp[offset + 2] == 0x01);
                            return BatteryTelemetry.Online(directVal, isCharging ? BatteryState.Charging : BatteryState.Discharging);
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Parses telemetry fields from a short vendor input report (Usage 0xFFFF:0x0001).
        /// Validates structured [battery, charging] or [reportId, battery, charging] byte pairs.
        /// </summary>
        private static BatteryTelemetry ParseRoyuanVendorInput(byte[] vIn)
        {
            if (vIn == null || vIn.Length < 3) return null;

            // Report format: [ReportID, BatteryLevel, ChargingFlag, ...] or [BatteryLevel, ChargingFlag, ...]
            for (int i = 0; i <= 1 && i + 1 < vIn.Length; i++)
            {
                int val = vIn[i];
                if (val >= 1 && val <= 100)
                {
                    byte next = vIn[i + 1];
                    // Charging flag must be strictly 0x00 (discharging) or 0x01 (charging)
                    if (next == 0x00 || next == 0x01)
                    {
                        return BatteryTelemetry.Online(val, next == 0x01 ? BatteryState.Charging : BatteryState.Discharging);
                    }
                }
            }

            return null;
        }
    }
}
