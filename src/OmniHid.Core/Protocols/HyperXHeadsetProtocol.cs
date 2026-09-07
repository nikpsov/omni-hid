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
    /// - Uses atomic Exchange to prevent race condition between output command and incoming response.
    /// </remarks>
    public class HyperXHeadsetProtocol : BaseProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Constants
        // ═══════════════════════════════════════════════════════════════════════

        private const byte REPORT_ID_OUTPUT         = 0x21;
        private const byte HEADER_BYTE_1            = 0xBB;
        private const byte HEADER_BYTE_2            = 0x0B;
        private const byte CMD_GET_BATTERY_LEVEL    = 0x02;
        private const byte CMD_GET_BATTERY_CHARGING = 0x03;

        private const byte MAGIC_RESP_0             = 0x06;
        private const byte MAGIC_RESP_1             = 0xFF;
        private const byte MAGIC_RESP_2             = 0xBB;

        private const int MIN_OUTPUT_LENGTH         = 52;
        private const int RESPONSE_LENGTH           = 20;

        private const int OFFSET_VOLTAGE_HI         = 5;
        private const int OFFSET_VOLTAGE_LO         = 6;
        private const int OFFSET_BATTERY_LEVEL      = 7;
        private const int OFFSET_CHARGING_FLAG      = 4;

        private const int TIMEOUT_BATTERY_MS        = 350;
        private const int TIMEOUT_CHARGING_MS       = 200;

        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public override string ProtocolId { get { return "hyperx-headset"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public override string ProtocolName { get { return "HyperX Headset Protocol"; } }

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
        public override BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            if (interfaces == null || interfaces.Count == 0)
                return BatteryTelemetry.Offline("Headset dongle not connected");

            // Check Windows PnP cache first
            BatteryTelemetry pnpTelemetry;
            if (TryGetPnpBattery(transport, interfaces, out pnpTelemetry))
            {
                return pnpTelemetry;
            }

            // Rank candidate interfaces for 52-byte Output Reports
            List<HidDeviceInfo> candidates = GetCandidateInterfaces(interfaces, profile, minOutputLen: MIN_OUTPUT_LENGTH);

            foreach (var targetDev in candidates)
            {
                // 1. Send battery level request (CMD 0x02) via atomic Exchange
                byte[] cmd = new byte[Math.Max(MIN_OUTPUT_LENGTH, (int)targetDev.OutputReportByteLength)];
                cmd[0] = REPORT_ID_OUTPUT;
                cmd[1] = HEADER_BYTE_1;
                cmd[2] = HEADER_BYTE_2;
                cmd[3] = CMD_GET_BATTERY_LEVEL;

                byte[] response = new byte[RESPONSE_LENGTH];
                bool exchangeOk = transport.Exchange(targetDev.DevicePath, cmd, targetDev.DevicePath, response, TIMEOUT_BATTERY_MS, expectedReportId: MAGIC_RESP_0);
                if (!exchangeOk || response.Length < 8)
                {
                    continue;
                }

                // Verify response magic header [0x06, 0xFF, 0xBB]
                if (response[0] == MAGIC_RESP_0 && response[1] == MAGIC_RESP_1 && response[2] == MAGIC_RESP_2)
                {
                    int level = response[OFFSET_BATTERY_LEVEL];
                    int voltage = (response[OFFSET_VOLTAGE_HI] << 8) | response[OFFSET_VOLTAGE_LO];

                    if (level > 100) level = 100;
                    if (level < 0) level = 0;

                    // 2. Query charging status (CMD 0x03) via atomic Exchange
                    byte[] chargeCmd = new byte[cmd.Length];
                    chargeCmd[0] = REPORT_ID_OUTPUT;
                    chargeCmd[1] = HEADER_BYTE_1;
                    chargeCmd[2] = HEADER_BYTE_2;
                    chargeCmd[3] = CMD_GET_BATTERY_CHARGING;

                    bool isCharging = false;
                    byte[] chargeResp = new byte[RESPONSE_LENGTH];
                    if (transport.Exchange(targetDev.DevicePath, chargeCmd, targetDev.DevicePath, chargeResp, TIMEOUT_CHARGING_MS, expectedReportId: MAGIC_RESP_0))
                    {
                        if (chargeResp.Length >= 5 && chargeResp[OFFSET_CHARGING_FLAG] == 1)
                        {
                            isCharging = true;
                        }
                    }

                    return BatteryTelemetry.Online(level, isCharging ? BatteryState.Charging : BatteryState.Discharging, voltage);
                }
            }

            return BatteryTelemetry.Offline("Headset offline or sleeping");
        }
    }
}
