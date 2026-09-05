using System;
using System.Collections.Generic;
using System.Threading;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Protocols
{
    /// <summary>
    /// Implements battery telemetry query protocol for Razer wireless peripherals (mice, keyboards, headsets).
    /// Reverse-engineered from OpenRazer kernel drivers and Razer Synapse USB control transfers.
    /// </summary>
    /// <remarks>
    /// Protocol Overview:
    /// - Control Transfers: Transmitted over Endpoint 0 via HID Feature Reports (Report ID 0x00).
    /// - Buffer Layout: 91 bytes on Windows (Byte 0 = Report ID 0x00 + 90 bytes Razer report).
    /// - Packet Framing:
    ///   - Byte 1: Status (0x00 = New Command, device replies with 0x02 = Success, 0x01 = Busy).
    ///   - Byte 2: Transaction ID (0x1F = standard modern mice/wired, 0x9F = wireless keyboards, 0x3F / 0xFF = legacy).
    ///   - Bytes 3-4: Remaining Packets (0x0000 Big Endian).
    ///   - Byte 5: Protocol Type (0x00).
    ///   - Byte 6: Data Size (0x02 for battery and charging queries).
    ///   - Byte 7: Command Class (0x07 = Power / Battery).
    ///   - Byte 8: Command ID (0x80 = Get Battery Level, 0x84 = Get Charging Status).
    ///   - Bytes 9-88: Arguments (80 bytes payload).
    ///   - Byte 89: Checksum (XOR of bytes 3..88 inclusive).
    ///   - Byte 90: Reserved (0x00).
    /// - Battery Scaling: Raw level in arguments[1] (Byte 10) ranges from 0 to 255 (255 = 100%).
    /// </remarks>
    public class RazerProtocol : IProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Constants
        // ═══════════════════════════════════════════════════════════════════════

        private const byte CMD_CLASS_POWER = 0x07;
        private const byte CMD_GET_BATTERY_LEVEL = 0x80;
        private const byte CMD_GET_CHARGING_STATUS = 0x84;

        private const byte RAZER_CMD_BUSY = 0x01;
        private const byte RAZER_CMD_SUCCESSFUL = 0x02;

        private const byte TX_ID_STANDARD = 0x1F;
        private const byte TX_ID_KEYBOARD_WIRELESS = 0x9F;
        private const byte TX_ID_ALT = 0x3F;
        private const byte TX_ID_LEGACY = 0xFF;

        private const int RAZER_REPORT_SIZE = 91;

        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public string ProtocolId { get { return "razer"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public string ProtocolName { get { return "Razer Peripheral Protocol"; } }

        /// <summary>
        /// Gets a value indicating whether this protocol can query telemetry when no Windows HID interface handles exist.
        /// Razer peripherals require direct communication via HID Feature reports.
        /// </summary>
        public bool CanQueryWithoutHidInterfaces { get { return false; } }

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Query Implementation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Queries the physical Razer peripheral for current battery percentage and charging state.
        /// </summary>
        /// <param name="transport">Transport layer abstraction.</param>
        /// <param name="interfaces">List of HID interfaces associated with this peripheral.</param>
        /// <param name="profile">Declarative profile information.</param>
        /// <returns>Populated <see cref="BatteryTelemetry"/> instance.</returns>
        public BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            if (interfaces == null || interfaces.Count == 0)
                return BatteryTelemetry.Offline("Device not found");

            // 1. Check Windows PnP device property cache first (fast, used by wireless headset audio dongles)
            foreach (var iface in interfaces)
            {
                int pnpLevel = transport.GetPnpBatteryLevel(iface.DevicePath);
                if (pnpLevel >= 0 && pnpLevel <= 100)
                {
                    return BatteryTelemetry.Online(pnpLevel, BatteryState.Discharging);
                }
            }

            // 2. Select target HID interface for Razer Feature Reports (Report size >= 90 or vendor usage page)
            List<HidDeviceInfo> targetCandidates = GetTargetInterfaces(interfaces, profile);
            if (targetCandidates.Count == 0)
            {
                return BatteryTelemetry.Offline("Razer control interface not found");
            }

            // Determine primary Transaction ID based on device category
            byte primaryTxId = TX_ID_STANDARD;
            if (profile != null && profile.Category == DeviceCategory.Keyboard)
            {
                primaryTxId = TX_ID_KEYBOARD_WIRELESS;
            }

            byte[] txIdCandidates = new byte[] { primaryTxId, TX_ID_STANDARD, TX_ID_KEYBOARD_WIRELESS, TX_ID_ALT, TX_ID_LEGACY };

            // 3. Query battery level across candidate interfaces and transaction IDs
            foreach (var dev in targetCandidates)
            {
                HashSet<byte> triedTx = new HashSet<byte>();
                foreach (byte txId in txIdCandidates)
                {
                    if (triedTx.Contains(txId)) continue;
                    triedTx.Add(txId);

                    byte[] request = BuildRazerReport(txId, CMD_CLASS_POWER, CMD_GET_BATTERY_LEVEL, 0x02);
                    if (!transport.SetFeatureReport(dev.DevicePath, request))
                    {
                        continue;
                    }

                    Thread.Sleep(20);

                    byte[] response = new byte[RAZER_REPORT_SIZE];
                    response[0] = 0x00;
                    if (!transport.GetFeatureReport(dev.DevicePath, 0x00, response))
                    {
                        continue;
                    }

                    // Verify response validity and command echo
                    byte status = response[1];
                    if ((status == RAZER_CMD_SUCCESSFUL || status == RAZER_CMD_BUSY) &&
                        response[7] == CMD_CLASS_POWER && response[8] == CMD_GET_BATTERY_LEVEL)
                    {
                        // Byte 10 corresponds to arguments[1]: raw level 0..255
                        int rawBattery = response[10];
                        int batteryPercent = (int)Math.Round((rawBattery / 255.0) * 100.0);
                        if (batteryPercent > 100) batteryPercent = 100;
                        if (batteryPercent < 0) batteryPercent = 0;

                        // Check charging status unless device uses disposable AA/AAA batteries
                        bool isCharging = false;
                        if (SupportsChargingQuery(profile))
                        {
                            isCharging = QueryChargingStatus(transport, dev.DevicePath, txId);
                        }

                        BatteryState state = isCharging ? BatteryState.Charging : BatteryState.Discharging;
                        return BatteryTelemetry.Online(batteryPercent, state);
                    }
                }
            }

            return BatteryTelemetry.Offline("Razer device offline or sleeping");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Frame Construction & Checksum Helpers
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds a 91-byte Windows HID Feature Report for the Razer control endpoint.
        /// </summary>
        /// <param name="transactionId">Transaction / routing identifier.</param>
        /// <param name="commandClass">Command class (e.g., 0x07 for Power).</param>
        /// <param name="commandId">Command ID (e.g., 0x80 for Battery Level).</param>
        /// <param name="dataSize">Payload argument count.</param>
        /// <returns>Formatted 91-byte buffer ready for <c>HidD_SetFeature</c>.</returns>
        private static byte[] BuildRazerReport(byte transactionId, byte commandClass, byte commandId, byte dataSize)
        {
            byte[] report = new byte[RAZER_REPORT_SIZE];

            report[0] = 0x00;           // Windows Report ID (0x00 for unnumbered Feature Reports)
            report[1] = 0x00;           // Status: New Command (0x00)
            report[2] = transactionId;  // Transaction ID / Routing
            report[3] = 0x00;           // Remaining Packets (MSB)
            report[4] = 0x00;           // Remaining Packets (LSB)
            report[5] = 0x00;           // Protocol Type (0x00)
            report[6] = dataSize;       // Data Size
            report[7] = commandClass;   // Command Class
            report[8] = commandId;      // Command ID

            // Calculate XOR checksum over bytes 3..88 inclusive
            byte crc = 0;
            for (int i = 3; i <= 88; i++)
            {
                crc ^= report[i];
            }
            report[89] = crc;           // CRC byte
            report[90] = 0x00;           // Reserved byte

            return report;
        }

        /// <summary>
        /// Queries the device for active battery charging status.
        /// </summary>
        private static bool QueryChargingStatus(IHidTransport transport, string devicePath, byte transactionId)
        {
            byte[] request = BuildRazerReport(transactionId, CMD_CLASS_POWER, CMD_GET_CHARGING_STATUS, 0x02);
            if (!transport.SetFeatureReport(devicePath, request))
                return false;

            Thread.Sleep(15);

            byte[] response = new byte[RAZER_REPORT_SIZE];
            response[0] = 0x00;
            if (!transport.GetFeatureReport(devicePath, 0x00, response))
                return false;

            byte status = response[1];
            if ((status == RAZER_CMD_SUCCESSFUL || status == RAZER_CMD_BUSY) &&
                response[7] == CMD_CLASS_POWER && response[8] == CMD_GET_CHARGING_STATUS)
            {
                // Byte 10 corresponds to arguments[1]: 0x01 = charging, 0x00 = discharging
                return response[10] == 0x01;
            }

            return false;
        }

        /// <summary>
        /// Filters and ranks HID interfaces to find candidate Razer control endpoints.
        /// Prioritizes declarative profile TargetUsagePage, then 90-byte Feature Report collections, then vendor pages.
        /// </summary>
        private static List<HidDeviceInfo> GetTargetInterfaces(List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            List<HidDeviceInfo> targets = new List<HidDeviceInfo>();

            // Priority 0: Explicit target usage page from declarative profile
            if (profile != null && profile.TargetUsagePage != 0)
            {
                foreach (var iface in interfaces)
                {
                    if (iface.UsagePage == profile.TargetUsagePage &&
                        (profile.TargetUsage == 0 || iface.Usage == profile.TargetUsage))
                    {
                        targets.Add(iface);
                    }
                }
            }

            // Priority 1: Interfaces with FeatureReportByteLength >= 90 (matches 90-byte Razer report layout)
            foreach (var iface in interfaces)
            {
                if (iface.FeatureReportByteLength >= 90 && !targets.Contains(iface))
                {
                    targets.Add(iface);
                }
            }

            // Priority 2: Vendor-defined usage pages (UsagePage >= 0xFF00)
            if (targets.Count == 0)
            {
                foreach (var iface in interfaces)
                {
                    if (iface.UsagePage >= 0xFF00 && !targets.Contains(iface))
                    {
                        targets.Add(iface);
                    }
                }
            }

            // Priority 3: Fallback to all interfaces with feature report capability
            if (targets.Count == 0)
            {
                foreach (var iface in interfaces)
                {
                    if (iface.FeatureReportByteLength > 0 && !targets.Contains(iface))
                    {
                        targets.Add(iface);
                    }
                }
            }

            // Priority 4: Last resort, all interfaces
            if (targets.Count == 0)
            {
                targets.AddRange(interfaces);
            }

            return targets;
        }

        /// <summary>
        /// Checks whether the peripheral supports battery charging telemetry queries.
        /// Peripherals using disposable AA/AAA batteries or profiles without <see cref="DeviceCapabilities.ChargingStatus"/>
        /// do not support charging state detection.
        /// </summary>
        private static bool SupportsChargingQuery(DeviceProfile profile)
        {
            if (profile != null)
            {
                // If the profile explicitly lacks ChargingStatus capability, suppress charging queries
                if ((profile.Capabilities & DeviceCapabilities.ChargingStatus) == 0)
                {
                    return false;
                }
            }

            // Also check known disposable-battery models in case generic/fallback profile was used
            string name = (profile != null && profile.ModelName != null) ? profile.ModelName : string.Empty;
            return name.IndexOf("Orochi", StringComparison.OrdinalIgnoreCase) < 0 &&
                   name.IndexOf("Atheris", StringComparison.OrdinalIgnoreCase) < 0 &&
                   name.IndexOf("Basilisk X", StringComparison.OrdinalIgnoreCase) < 0 &&
                   name.IndexOf("Viper V3 HyperSpeed", StringComparison.OrdinalIgnoreCase) < 0 &&
                   name.IndexOf("DeathAdder V2 X", StringComparison.OrdinalIgnoreCase) < 0;
        }
    }
}
