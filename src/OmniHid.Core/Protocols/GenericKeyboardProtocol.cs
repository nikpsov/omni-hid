using System;
using System.Collections.Generic;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Protocols
{
    /// <summary>
    /// Fallback protocol driver for wireless keyboards using standard USB HID keyboard interfaces.
    /// Handles unprofiled 2.4GHz / Bluetooth keyboards where proprietary battery endpoints are undocumented.
    /// </summary>
    public class GenericKeyboardProtocol : BaseProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public override string ProtocolId { get { return "generic-keyboard"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public override string ProtocolName { get { return "Generic Keyboard Protocol"; } }

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Query Implementation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Queries the generic keyboard. Returns an offline state with informational message if vendor telemetry is absent.
        /// </summary>
        public override BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            if (interfaces == null || interfaces.Count == 0)
                return BatteryTelemetry.Offline("Keyboard not detected");

            // Check Windows PnP device property cache (common for Bluetooth / standard battery drivers)
            BatteryTelemetry pnpTelemetry;
            if (TryGetPnpBattery(transport, interfaces, out pnpTelemetry))
            {
                return pnpTelemetry;
            }

            // Inform caller that proprietary vendor protocol is required
            return BatteryTelemetry.Offline("Connected (telemetry requires proprietary vendor protocol)");
        }
    }
}