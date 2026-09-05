using System.Collections.Generic;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Abstractions
{
    /// <summary>
    /// Contract for hardware protocol drivers that query device telemetry over HID or vendor APIs.
    /// </summary>
    public interface IProtocolHandler
    {
        /// <summary>
        /// Gets the unique string identifier for this protocol handler (e.g., "logitech-hidpp", "areson").
        /// </summary>
        string ProtocolId { get; }

        /// <summary>
        /// Gets the human-readable display name of the protocol (e.g., "Logitech HID++ 2.0 Protocol").
        /// </summary>
        string ProtocolName { get; }

        /// <summary>
        /// Gets a value indicating whether this protocol can query telemetry without enumerated HID interfaces
        /// (e.g., controllers managed via XInput or proprietary kernel driver APIs).
        /// </summary>
        bool CanQueryWithoutHidInterfaces { get; }

        /// <summary>
        /// Queries the physical peripheral device for battery percentage, charging state, and telemetry.
        /// </summary>
        /// <param name="transport">Transport layer abstraction used to execute low-level HID I/O.</param>
        /// <param name="interfaces">List of enumerated HID interfaces belonging to this physical device.</param>
        /// <param name="profile">Declarative profile information containing model metadata and endurance ratings.</param>
        /// <returns>A populated <see cref="BatteryTelemetry"/> instance representing the current status.</returns>
        BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile);
    }
}
