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
    public class GenericKeyboardProtocol : IProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public string ProtocolId { get { return "generic-keyboard"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public string ProtocolName { get { return "Generic Keyboard Protocol"; } }

        /// <summary>
        /// Gets a value indicating whether this protocol can query telemetry when no Windows HID interface handles exist.
        /// Generic keyboards query via HID interface PnP properties.
        /// </summary>
        public bool CanQueryWithoutHidInterfaces { get { return false; } }

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Query Implementation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Queries the generic keyboard. Returns an offline state with informational message if vendor telemetry is absent.
        /// </summary>
        public BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            if (interfaces == null || interfaces.Count == 0)
                return BatteryTelemetry.Offline("Keyboard not detected");

            // 1. Check Windows PnP device property cache (common for Bluetooth / standard battery drivers)
            foreach (var iface in interfaces)
            {
                int pnpLevel = transport.GetPnpBatteryLevel(iface.DevicePath);
                if (pnpLevel >= 0 && pnpLevel <= 100)
                {
                    return BatteryTelemetry.Online(pnpLevel, BatteryState.Discharging);
                }
            }

            // 2. Inform caller that proprietary vendor protocol is required
            return BatteryTelemetry.Offline("Connected (telemetry requires proprietary vendor protocol)");
        }
    }
}