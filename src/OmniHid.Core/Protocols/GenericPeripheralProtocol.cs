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
    public class GenericPeripheralProtocol : BaseProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public override string ProtocolId { get { return "generic-peripheral"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public override string ProtocolName { get { return "Generic HID Peripheral Protocol"; } }

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Query Implementation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Attempts to query the peripheral battery via Windows PnP interface properties.
        /// </summary>
        public override BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            if (transport != null && interfaces != null)
            {
                BatteryTelemetry pnpTelemetry;
                if (TryGetPnpBattery(transport, interfaces, out pnpTelemetry))
                {
                    pnpTelemetry.StatusMessage = "Windows PnP";
                    return pnpTelemetry;
                }
            }

            return BatteryTelemetry.Offline("Connected (Unprofiled generic peripheral)");
        }
    }
}
