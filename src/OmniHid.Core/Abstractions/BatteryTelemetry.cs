using System;

namespace OmniHid.Core.Abstractions
{
    /// <summary>
    /// Rich battery telemetry snapshot for a monitored peripheral device.
    /// </summary>
    public class BatteryTelemetry
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Gets or sets a value indicating whether battery data is actively available.
        /// </summary>
        public bool IsAvailable { get; set; }

        /// <summary>
        /// Gets or sets the battery percentage (0 to 100). Returns -1 if unavailable.
        /// </summary>
        public int LevelPercent { get; set; }

        /// <summary>
        /// Gets or sets the charging/discharging state of the peripheral battery.
        /// </summary>
        public BatteryState State { get; set; }

        /// <summary>
        /// Gets or sets the measured battery voltage in millivolts, or 0 if unsupported.
        /// </summary>
        public int VoltageMv { get; set; }

        /// <summary>
        /// Gets or sets estimated remaining runtime in minutes until depleted, or 0 if unknown.
        /// </summary>
        public int TimeToEmptyMinutes { get; set; }

        /// <summary>
        /// Gets or sets estimated time in minutes until fully recharged, or 0 if unknown.
        /// </summary>
        public int TimeToFullMinutes { get; set; }

        /// <summary>
        /// Gets or sets the UTC timestamp when this telemetry reading was acquired.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Gets or sets an informational status or diagnostic message (e.g., error explanation).
        /// </summary>
        public string StatusMessage { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the peripheral is connected via direct wired USB cable.
        /// </summary>
        public bool IsWired { get; set; }

        /// <summary>
        /// Gets a value indicating whether the device is currently connected to power and charging.
        /// </summary>
        public bool IsCharging
        {
            get { return State == BatteryState.Charging; }
        }

        /// <summary>
        /// Gets a value indicating whether the battery has reached full charge capacity (100%).
        /// </summary>
        public bool IsFull
        {
            get { return State == BatteryState.Full || (LevelPercent >= 100 && (IsCharging || IsWired)); }
        }

        /// <summary>
        /// Gets human-readable battery power state (Charging, Full (Wired), Full, Wired, Discharging).
        /// </summary>
        public string StateDescription
        {
            get
            {
                if (State == BatteryState.Charging) return "Charging";
                if (State == BatteryState.Full) return IsWired ? "Full (Wired)" : "Full";
                if (IsWired) return "Wired";
                if (State == BatteryState.Discharging) return "Discharging";
                return "Online";
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Constructors & Factory Methods
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Initializes a new instance of the <see cref="BatteryTelemetry"/> class in an unavailable state.
        /// </summary>
        public BatteryTelemetry()
        {
            Timestamp = DateTime.UtcNow;
            LevelPercent = -1;
            State = BatteryState.Unavailable;
        }

        /// <summary>
        /// Creates an offline/unavailable telemetry instance with an explanatory status message and current timestamp.
        /// </summary>
        /// <param name="reason">Explanatory message describing why telemetry is unavailable.</param>
        /// <returns>A new <see cref="BatteryTelemetry"/> representing an offline or disconnected state.</returns>
        public static BatteryTelemetry Offline(string reason = "Device offline")
        {
            return new BatteryTelemetry
            {
                IsAvailable = false,
                LevelPercent = -1,
                State = BatteryState.Unavailable,
                StatusMessage = string.IsNullOrEmpty(reason) ? "Device offline" : reason,
                Timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Creates a valid online telemetry snapshot with battery percentage, state, and optional voltage.
        /// </summary>
        public static BatteryTelemetry Online(int percent, BatteryState state, int voltageMv = 0, string msg = null)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            return new BatteryTelemetry
            {
                IsAvailable = true,
                LevelPercent = percent,
                State = state,
                VoltageMv = voltageMv,
                StatusMessage = msg ?? (state == BatteryState.Charging ? "Charging" : "Discharging")
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Formatting Helpers
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Gets human-readable remaining runtime formatted as "~Xh Ym", or null if unavailable.
        /// </summary>
        public string FormattedTimeRemaining
        {
            get
            {
                if (TimeToEmptyMinutes <= 0) return null;
                int hours = TimeToEmptyMinutes / 60;
                int mins = TimeToEmptyMinutes % 60;
                if (hours > 0 && mins > 0) return string.Format("~{0}h {1}m", hours, mins);
                if (hours > 0) return string.Format("~{0}h", hours);
                return string.Format("~{0}m", mins);
            }
        }

        /// <summary>
        /// Returns a formatted string representation of the current telemetry.
        /// </summary>
        /// <returns>A summary string containing percentage, charging status, and voltage.</returns>
        public override string ToString()
        {
            if (!IsAvailable) return "Offline (" + StatusMessage + ")";
            string stateStr = State != BatteryState.Discharging ? " [" + StateDescription + "]" : "";
            string timeStr = (State == BatteryState.Discharging && TimeToEmptyMinutes > 0) ? " (" + FormattedTimeRemaining + " remaining)" : "";
            string voltStr = VoltageMv > 0 ? " (" + VoltageMv + " mV)" : "";
            return LevelPercent + "%" + stateStr + timeStr + voltStr;
        }
    }
}
