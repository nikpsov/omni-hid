using System;
using System.Collections.Generic;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Protocols
{
    /// <summary>
    /// Fallback protocol driver for unrecognized or unprofiled HID peripherals.
    /// Attempts to query Windows Bluetooth GATT PnP properties (<c>DEVPKEY_Device_BatteryLevel</c>).
    /// </summary>
    public class GenericPeripheralProtocol : IProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public string ProtocolId { get { return "generic-peripheral"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public string ProtocolName { get { return "Generic HID Peripheral Protocol"; } }

        /// <summary>
        /// Gets a value indicating whether this protocol can query telemetry when no Windows HID interface handles exist.
        /// Generic peripherals query via HID interface PnP properties.
        /// </summary>
        public bool CanQueryWithoutHidInterfaces { get { return false; } }

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Query Implementation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Attempts to query the peripheral battery via Windows PnP interface properties.
        /// </summary>
        public BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            if (transport != null && interfaces != null)
            {
                foreach (var iface in interfaces)
                {
                    int pnp = transport.GetPnpBatteryLevel(iface.DevicePath);
                    if (pnp >= 0 && pnp <= 100)
                    {
                        return BatteryTelemetry.Online(pnp, BatteryState.Discharging, 0, "Windows PnP");
                    }
                }
            }

            return BatteryTelemetry.Offline("Connected (Unprofiled generic peripheral)");
        }
    }
}
