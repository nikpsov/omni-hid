namespace OmniHid.Core.Abstractions
{
    /// <summary>
    /// Functional peripheral device categories classified by OmniHID.
    /// </summary>
    public enum DeviceCategory
    {
        /// <summary>
        /// Device category is unknown or unclassified.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Wireless or wired computer pointing device (Mouse).
        /// </summary>
        Mouse = 1,

        /// <summary>
        /// Wireless or wired alphanumeric input device (Keyboard).
        /// </summary>
        Keyboard = 2,

        /// <summary>
        /// Wireless gaming or office audio headset with integrated battery.
        /// </summary>
        Headset = 3,

        /// <summary>
        /// Wireless console or PC game controller (Gamepad / Joystick).
        /// </summary>
        Gamepad = 4,

        /// <summary>
        /// Other peripheral category not covered by primary types.
        /// </summary>
        Other = 5
    }
}
