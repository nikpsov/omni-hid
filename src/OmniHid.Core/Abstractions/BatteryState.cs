namespace OmniHid.Core.Abstractions
{
    /// <summary>
    /// Represents the current charging and operational power state of a peripheral battery.
    /// </summary>
    public enum BatteryState
    {
        /// <summary>
        /// Battery state is unavailable, unreadable, or device is offline.
        /// </summary>
        Unavailable = 0,

        /// <summary>
        /// Device is actively running on battery power (discharging).
        /// </summary>
        Discharging = 1,

        /// <summary>
        /// Device is plugged into a charger or USB cable and currently charging.
        /// </summary>
        Charging = 2,

        /// <summary>
        /// Device is connected to power and the battery has reached maximum capacity.
        /// </summary>
        Full = 3
    }
}
