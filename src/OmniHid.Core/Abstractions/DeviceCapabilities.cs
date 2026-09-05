using System;

namespace OmniHid.Core.Abstractions
{
    /// <summary>
    /// Bitwise flags indicating peripheral hardware telemetry capabilities and feature sets.
    /// </summary>
    [Flags]
    public enum DeviceCapabilities
    {
        /// <summary>
        /// No special capabilities declared.
        /// </summary>
        None = 0,

        /// <summary>
        /// Device supports querying battery percentage (0..100%).
        /// </summary>
        BatteryLevel = 1 << 0,

        /// <summary>
        /// Device supports reading raw millivolt battery voltage.
        /// </summary>
        BatteryVoltage = 1 << 1,

        /// <summary>
        /// Device reports real-time charging status (charging vs discharging).
        /// </summary>
        ChargingStatus = 1 << 2,

        /// <summary>
        /// Firmware or library estimates runtime remaining until empty or full.
        /// </summary>
        TimeEstimation = 1 << 3,

        /// <summary>
        /// Peripheral supports configurable inactivity/sleep timers.
        /// </summary>
        InactiveSleepTimer = 1 << 4,

        /// <summary>
        /// Headset supports microphone sidetone level control.
        /// </summary>
        Sidetone = 1 << 5,

        /// <summary>
        /// Peripheral supports RGB lighting control or battery-indicator illumination.
        /// </summary>
        RgbLighting = 1 << 6,

        /// <summary>
        /// Mouse supports reading or adjusting DPI sensor stages.
        /// </summary>
        DpiSettings = 1 << 7
    }
}
