using System;
using OmniHid.Core.Abstractions;

namespace OmniHid.Core.Profiles
{
    /// <summary>
    /// Declarative hardware profile describing a supported peripheral model, vendor/product IDs, and telemetry ratings.
    /// </summary>
    public class DeviceProfile
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Profile Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Gets or sets the commercial product name (e.g., "Logitech G PRO X Superlight 2").
        /// </summary>
        public string ModelName { get; set; }

        /// <summary>
        /// Gets or sets the 16-bit USB Vendor ID (VID).
        /// </summary>
        public ushort VendorId { get; set; }

        /// <summary>
        /// Gets or sets array of supported 16-bit USB Product IDs (PIDs) for wired/wireless modes and dongles.
        /// </summary>
        public ushort[] ProductIds { get; set; }

        /// <summary>
        /// Gets or sets the functional peripheral category (Mouse, Keyboard, Headset, Gamepad).
        /// </summary>
        public DeviceCategory Category { get; set; }

        /// <summary>
        /// Gets or sets the protocol identifier used to query this device (e.g., "logitech-hidpp").
        /// </summary>
        public string ProtocolId { get; set; }

        /// <summary>
        /// Gets or sets the bitwise flags of telemetry capabilities supported by this device.
        /// </summary>
        public DeviceCapabilities Capabilities { get; set; }

        /// <summary>
        /// Gets or sets the manufacturer's rated full battery endurance in operating hours (used for time-remaining estimates).
        /// </summary>
        public double BatteryLifeHours { get; set; }

        /// <summary>
        /// Gets or sets the assigned XInput controller user slot (0..3), or -1 if unassigned.
        /// </summary>
        public int AssignedSlot { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this profile was loaded dynamically from an external JSON profile file.
        /// </summary>
        public bool IsCustomProfile { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this profile was loaded from a validated declarative JSON profile definition.
        /// </summary>
        public bool IsRegisteredProfile { get; set; }

        /// <summary>
        /// Gets or sets the target HID Usage Page required for configuration commands (e.g. 0xFF02 for Areson mice).
        /// When specified, the interface matching this Usage Page is prioritized during device grouping.
        /// </summary>
        public ushort TargetUsagePage { get; set; }

        /// <summary>
        /// Gets or sets the target HID Usage within <see cref="TargetUsagePage"/> (e.g. 0x0002 for Areson mice).
        /// </summary>
        public ushort TargetUsage { get; set; }

        /// <summary>
        /// Gets or sets the list of USB Product IDs corresponding to direct wired USB cable mode.
        /// </summary>
        public ushort[] WiredProductIds { get; set; }

        // ═══════════════════════════════════════════════════════════════════════
        // Constructors & Methods
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceProfile"/> class with default settings.
        /// </summary>
        public DeviceProfile()
        {
            Category = DeviceCategory.Unknown;
            Capabilities = DeviceCapabilities.BatteryLevel | DeviceCapabilities.ChargingStatus;
            ProductIds = new ushort[0];
            WiredProductIds = new ushort[0];
            BatteryLifeHours = 0;
            AssignedSlot = -1;
            IsCustomProfile = false;
            IsRegisteredProfile = false;
            TargetUsagePage = 0;
            TargetUsage = 0;
        }

        /// <summary>
        /// Creates a deep copy of this device profile.
        /// </summary>
        /// <returns>A new cloned instance of <see cref="DeviceProfile"/>.</returns>
        public DeviceProfile Clone()
        {
            return new DeviceProfile
            {
                ModelName = this.ModelName,
                VendorId = this.VendorId,
                ProductIds = this.ProductIds,
                WiredProductIds = this.WiredProductIds,
                Category = this.Category,
                ProtocolId = this.ProtocolId,
                Capabilities = this.Capabilities,
                BatteryLifeHours = this.BatteryLifeHours,
                AssignedSlot = this.AssignedSlot,
                IsCustomProfile = this.IsCustomProfile,
                IsRegisteredProfile = this.IsRegisteredProfile,
                TargetUsagePage = this.TargetUsagePage,
                TargetUsage = this.TargetUsage
            };
        }

        /// <summary>
        /// Determines whether the given Product ID corresponds to a direct wired cable connection.
        /// </summary>
        /// <param name="pid">16-bit USB Product ID.</param>
        /// <returns><c>true</c> if PID is declared in <see cref="WiredProductIds"/>; otherwise, <c>false</c>.</returns>
        public bool IsWiredProductId(ushort pid)
        {
            if (WiredProductIds != null && WiredProductIds.Length > 0)
            {
                for (int i = 0; i < WiredProductIds.Length; i++)
                {
                    if (WiredProductIds[i] == pid) return true;
                }
                return false;
            }

            // Automatic heuristic: if profile declares multiple Product IDs (e.g. wired cable + wireless dongle),
            // the first PID is by standard peripheral convention the wired USB connection.
            if (ProductIds != null && ProductIds.Length > 1)
            {
                return pid == ProductIds[0];
            }

            return false;
        }

        /// <summary>
        /// Determines whether this profile matches the given Vendor ID and Product ID.
        /// </summary>
        /// <param name="vid">16-bit USB Vendor ID.</param>
        /// <param name="pid">16-bit USB Product ID.</param>
        /// <returns><c>true</c> if VID matches and PID is in <see cref="ProductIds"/> or wildcard; otherwise, <c>false</c>.</returns>
        public bool Matches(ushort vid, ushort pid)
        {
            if (VendorId != vid) return false;
            if (ProductIds == null || ProductIds.Length == 0) return true; // Wildcard PID for vendor

            for (int i = 0; i < ProductIds.Length; i++)
            {
                if (ProductIds[i] == pid) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns a formatted string representation of this profile.
        /// </summary>
        public override string ToString()
        {
            return string.Format("{0} [{1}] (VID: 0x{2:X4}, Protocol: {3})",
                ModelName, Category, VendorId, ProtocolId);
        }
    }
}
