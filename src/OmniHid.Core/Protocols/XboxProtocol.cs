using System;
using System.Collections.Generic;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;
using OmniHid.Core.Transport.Win32;

namespace OmniHid.Core.Protocols
{
    /// <summary>
    /// Implements battery telemetry query protocol for Microsoft Xbox Wireless Controllers.
    /// Supports Xbox Series X/S, Xbox One, Elite Series 1 &amp; 2, and Xbox 360 controllers.
    /// </summary>
    /// <remarks>
    /// Multi-tiered query architecture:
    /// 1. XInput API (<c>XInputGetBatteryInformation</c>):
    ///    Used for controllers connected via Xbox Wireless Adapter, direct USB, or paired Bluetooth.
    ///    Maps 4-stage discrete battery levels (EMPTY=10%, LOW=30%, MEDIUM=65%, FULL=100%).
    /// 2. Windows Bluetooth GATT PnP property (<c>DEVPKEY_Device_BatteryLevel</c>):
    ///    Provides precise 0..100% percentage on Windows 10/11 when paired via standard Bluetooth.
    /// 3. Bluetooth HID Input Reports:
    ///    Queries Report 0x01 (offset 18) or Feature Report 0x04 for raw percentage.
    /// </remarks>
    public class XboxProtocol : IProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public string ProtocolId { get { return "xbox-controller"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public string ProtocolName { get { return "Xbox Wireless / XInput Protocol"; } }

        /// <summary>Gets a value indicating whether this protocol can query telemetry without HID interfaces.</summary>
        public bool CanQueryWithoutHidInterfaces { get { return true; } }

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Query Implementation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Queries the Xbox controller for current battery level and connection type using XInput, PnP, and HID fallback.
        /// </summary>
        /// <param name="transport">Transport layer abstraction.</param>
        /// <param name="interfaces">List of HID interfaces associated with this controller.</param>
        /// <param name="profile">Declarative profile information containing the assigned XInput user slot.</param>
        /// <returns>Populated <see cref="BatteryTelemetry"/> instance.</returns>
        public BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            int slotToQuery = profile != null ? profile.AssignedSlot : -1;

            // ═══════════════════════════════════════════════════════════════════
            // Phase 1: Query via XInput API
            // ═══════════════════════════════════════════════════════════════════
            bool anyXInputActive = false;
            if (Win32XInputNative.IsAvailable)
            {
                int startSlot = slotToQuery >= 0 && slotToQuery < 4 ? slotToQuery : 0;
                int endSlot   = slotToQuery >= 0 && slotToQuery < 4 ? slotToQuery : 3;

                for (int slot = startSlot; slot <= endSlot; slot++)
                {
                    Win32XInputNative.XINPUT_STATE state;
                    int stateRes = Win32XInputNative.GetState(slot, out state);

                    Win32XInputNative.XINPUT_BATTERY_INFORMATION batt;
                    int battRes = Win32XInputNative.GetBatteryInformation(slot, Win32XInputNative.BATTERY_DEVTYPE_GAMEPAD, out batt);

                    if (stateRes == Win32XInputNative.ERROR_SUCCESS || battRes == Win32XInputNative.ERROR_SUCCESS)
                    {
                        anyXInputActive = true;

                        // Wired USB connection
                        if (batt.BatteryType == Win32XInputNative.BATTERY_TYPE_WIRED)
                        {
                            var wiredTel = BatteryTelemetry.Online(100, BatteryState.Full, 0, "Wired (USB)");
                            wiredTel.IsWired = true;
                            return wiredTel;
                        }

                        // Wireless connection with known battery chemistry
                        if (batt.BatteryType == Win32XInputNative.BATTERY_TYPE_ALKALINE ||
                            batt.BatteryType == Win32XInputNative.BATTERY_TYPE_NIMH ||
                            (batt.BatteryType == Win32XInputNative.BATTERY_TYPE_UNKNOWN && batt.BatteryLevel <= 3))
                        {
                            int percent = 0;
                            switch (batt.BatteryLevel)
                            {
                                case Win32XInputNative.BATTERY_LEVEL_EMPTY:
                                    percent = 10;
                                    break;
                                case Win32XInputNative.BATTERY_LEVEL_LOW:
                                    percent = 30;
                                    break;
                                case Win32XInputNative.BATTERY_LEVEL_MEDIUM:
                                    percent = 65;
                                    break;
                                case Win32XInputNative.BATTERY_LEVEL_FULL:
                                    percent = 100;
                                    break;
                                default:
                                    percent = 50;
                                    break;
                            }

                            string battTypeStr = "Wireless Gamepad";
                            if (batt.BatteryType == Win32XInputNative.BATTERY_TYPE_ALKALINE) battTypeStr = "Alkaline AA";
                            else if (batt.BatteryType == Win32XInputNative.BATTERY_TYPE_NIMH) battTypeStr = "NiMH Battery";

                            return BatteryTelemetry.Online(percent, BatteryState.Discharging, 0, battTypeStr);
                        }

                        if (stateRes == Win32XInputNative.ERROR_SUCCESS)
                        {
                            // Trigger keepalive probe via zero-vibration command
                            Win32XInputNative.XINPUT_VIBRATION vib = new Win32XInputNative.XINPUT_VIBRATION();
                            Win32XInputNative.SetState(slot, ref vib);

                            return BatteryTelemetry.Offline("Connected (Move stick or press any button to read battery)");
                        }
                    }
                }
            }

            // ═══════════════════════════════════════════════════════════════════
            // Phase 2: Bluetooth HID / Windows PnP Fallback
            // ═══════════════════════════════════════════════════════════════════
            if (transport != null && interfaces != null && interfaces.Count > 0)
            {
                foreach (var dev in interfaces)
                {
                    // A. Check Windows Bluetooth GATT PnP property (DEVPKEY_Device_BatteryLevel)
                    int pnpLevel = transport.GetPnpBatteryLevel(dev.DevicePath);
                    if (pnpLevel >= 0 && pnpLevel <= 100)
                    {
                        return BatteryTelemetry.Online(pnpLevel, BatteryState.Discharging, 0, "Bluetooth (Windows PnP)");
                    }

                    // B. Try synchronous GetInputReport (Report 0x01)
                    byte[] inReport = new byte[64];
                    if (transport.GetInputReport(dev.DevicePath, 0x01, inReport))
                    {
                        if (inReport.Length >= 19 && inReport[18] > 0 && inReport[18] <= 100)
                        {
                            return BatteryTelemetry.Online(inReport[18], BatteryState.Discharging, 0, "Bluetooth HID (0x01)");
                        }
                    }

                    // C. Try Feature Report 0x04
                    byte[] featReport = new byte[64];
                    if (transport.GetFeatureReport(dev.DevicePath, 0x04, featReport))
                    {
                        if (featReport.Length >= 2 && featReport[1] > 0 && featReport[1] <= 100)
                        {
                            return BatteryTelemetry.Online(featReport[1], BatteryState.Discharging, 0, "Bluetooth Feature (0x04)");
                        }
                    }

                    // D. Try reading asynchronous Input Report with timeout
                    byte[] buffer = new byte[64];
                    bool ok = transport.ReadInputReport(dev.DevicePath, buffer, 100);
                    if (ok && buffer.Length >= 19)
                    {
                        if (buffer[0] == 0x01 && buffer[18] > 0 && buffer[18] <= 100)
                        {
                            return BatteryTelemetry.Online(buffer[18], BatteryState.Discharging, 0, "Bluetooth HID");
                        }
                    }
                }
            }

            if (anyXInputActive)
            {
                return BatteryTelemetry.Offline("Connected (Press any button on controller to refresh)");
            }

            return BatteryTelemetry.Offline("Controller sleeping or disconnected");
        }
    }
}
