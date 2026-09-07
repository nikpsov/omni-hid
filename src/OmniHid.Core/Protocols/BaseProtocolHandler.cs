using System;
using System.Collections.Generic;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Protocols
{
    /// <summary>
    /// Abstract base class for hardware protocol drivers providing shared interface selection
    /// heuristics, Windows PnP property queries, and common device ranking logic.
    /// </summary>
    public abstract class BaseProtocolHandler : IProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Contract Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Gets the unique string identifier for this protocol handler.</summary>
        public abstract string ProtocolId { get; }

        /// <summary>Gets the human-readable display name of the protocol.</summary>
        public abstract string ProtocolName { get; }

        /// <summary>
        /// Gets a value indicating whether this protocol can query telemetry without enumerated HID interfaces.
        /// Defaults to <c>false</c>.
        /// </summary>
        public virtual bool CanQueryWithoutHidInterfaces { get { return false; } }

        /// <summary>
        /// Queries the physical peripheral device for battery percentage, charging state, and telemetry.
        /// </summary>
        /// <param name="transport">Transport layer abstraction used to execute low-level HID I/O.</param>
        /// <param name="interfaces">List of enumerated HID interfaces belonging to this physical device.</param>
        /// <param name="profile">Declarative profile information containing model metadata and endurance ratings.</param>
        /// <returns>A populated <see cref="BatteryTelemetry"/> instance representing the current status.</returns>
        public abstract BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile);

        // ═══════════════════════════════════════════════════════════════════════
        // Shared Helper Methods
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Checks the Windows PnP battery level property cache (<c>DEVPKEY_Device_BatteryLevel</c>)
        /// across all provided interfaces.
        /// </summary>
        /// <param name="transport">Transport layer abstraction.</param>
        /// <param name="interfaces">List of HID interfaces associated with this peripheral.</param>
        /// <param name="telemetry">Populated telemetry instance if a valid PnP battery reading was found; otherwise null.</param>
        /// <returns><c>true</c> if a valid PnP battery level (0..100) was retrieved; otherwise <c>false</c>.</returns>
        protected static bool TryGetPnpBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, out BatteryTelemetry telemetry)
        {
            telemetry = null;
            if (transport == null || interfaces == null)
            {
                return false;
            }

            for (int i = 0; i < interfaces.Count; i++)
            {
                int pnpLevel = transport.GetPnpBatteryLevel(interfaces[i].DevicePath);
                if (pnpLevel >= 0 && pnpLevel <= 100)
                {
                    telemetry = BatteryTelemetry.Online(pnpLevel, BatteryState.Discharging);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Filters and ranks HID candidate interfaces based on declarative profile overrides,
        /// report buffer lengths, and vendor-defined usage pages.
        /// </summary>
        /// <param name="interfaces">List of all enumerated interfaces for the physical device.</param>
        /// <param name="profile">Declarative device profile containing optional target usage page/usage.</param>
        /// <param name="minFeatureLen">Minimum Feature report length required (or 0 to ignore).</param>
        /// <param name="minOutputLen">Minimum Output report length required (or 0 to ignore).</param>
        /// <param name="minInputLen">Minimum Input report length required (or 0 to ignore).</param>
        /// <param name="customUsagePage">Optional specific Usage Page to prioritize (e.g. 0xFFC5 for Corsair, 0x0001 for Gamepad).</param>
        /// <param name="customUsage">Optional specific Usage to prioritize with <paramref name="customUsagePage"/>.</param>
        /// <returns>Ranked list of candidate interfaces matching the criteria.</returns>
        protected static List<HidDeviceInfo> GetCandidateInterfaces(
            List<HidDeviceInfo> interfaces,
            DeviceProfile profile,
            int minFeatureLen = 0,
            int minOutputLen = 0,
            int minInputLen = 0,
            ushort customUsagePage = 0,
            ushort customUsage = 0)
        {
            List<HidDeviceInfo> candidates = new List<HidDeviceInfo>();
            if (interfaces == null || interfaces.Count == 0)
            {
                return candidates;
            }

            // Priority 0: Explicit target usage page from profile override
            if (profile != null && profile.TargetUsagePage != 0)
            {
                for (int i = 0; i < interfaces.Count; i++)
                {
                    var iface = interfaces[i];
                    if (iface.UsagePage == profile.TargetUsagePage &&
                        (profile.TargetUsage == 0 || iface.Usage == profile.TargetUsage))
                    {
                        candidates.Add(iface);
                    }
                }
            }

            // Priority 1: Custom protocol-specific usage page / usage (e.g. Corsair 0xFFC5:0x0001 or Gamepad 0x0001:0x0005)
            if (customUsagePage != 0)
            {
                for (int i = 0; i < interfaces.Count; i++)
                {
                    var iface = interfaces[i];
                    if (iface.UsagePage == customUsagePage &&
                        (customUsage == 0 || iface.Usage == customUsage) &&
                        !candidates.Contains(iface))
                    {
                        candidates.Add(iface);
                    }
                }
            }

            // Priority 2: Report buffer capability constraints
            if (minFeatureLen > 0 || minOutputLen > 0 || minInputLen > 0)
            {
                for (int i = 0; i < interfaces.Count; i++)
                {
                    var iface = interfaces[i];
                    bool matches = true;

                    if (minFeatureLen > 0 && iface.FeatureReportByteLength < minFeatureLen) matches = false;
                    if (minOutputLen > 0 && iface.OutputReportByteLength < minOutputLen) matches = false;
                    if (minInputLen > 0 && iface.InputReportByteLength < minInputLen) matches = false;

                    if (matches && !candidates.Contains(iface))
                    {
                        candidates.Add(iface);
                    }
                }
            }

            // Priority 3: Vendor-defined usage pages (UsagePage >= 0xFF00)
            for (int i = 0; i < interfaces.Count; i++)
            {
                var iface = interfaces[i];
                if (iface.UsagePage >= 0xFF00 && !candidates.Contains(iface))
                {
                    candidates.Add(iface);
                }
            }

            // Priority 4: Fallback to all interfaces if no specific candidate was matched
            if (candidates.Count == 0)
            {
                candidates.AddRange(interfaces);
            }

            return candidates;
        }
    }
}
