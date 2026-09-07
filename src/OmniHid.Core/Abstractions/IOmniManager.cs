using System;
using System.Collections.Generic;

namespace OmniHid.Core.Abstractions
{
    /// <summary>
    /// Central manager responsible for discovering, polling, and watching peripheral devices.
    /// </summary>
    public interface IOmniManager : IDisposable
    {
        /// <summary>
        /// Gets a snapshot list of all currently tracked and connected peripheral devices.
        /// </summary>
        IReadOnlyList<IOmniDevice> ConnectedDevices { get; }

        /// <summary>
        /// Gets or sets a value indicating whether only peripherals with validated declarative JSON profiles are tracked.
        /// When true, unprofiled generic peripherals and dynamic vendor fallbacks are excluded.
        /// </summary>
        bool RegisteredOnly { get; set; }

        /// <summary>
        /// Raised when a new peripheral device is discovered and connected.
        /// </summary>
        event Action<IOmniDevice> DeviceConnected;

        /// <summary>
        /// Raised when an existing peripheral is unplugged or disconnected.
        /// </summary>
        event Action<IOmniDevice> DeviceDisconnected;

        /// <summary>
        /// Raised whenever a peripheral's battery telemetry reading is updated.
        /// </summary>
        event Action<IOmniDevice, BatteryTelemetry> TelemetryUpdated;

        /// <summary>
        /// Raised whenever a hardware scan cycle completes and all device states have been refreshed.
        /// Provides a thread-safe snapshot list of all currently tracked devices.
        /// </summary>
        event Action<IReadOnlyList<IOmniDevice>> DevicesUpdated;

        /// <summary>
        /// Begins periodic background telemetry polling and enables USB PnP arrival/removal monitoring.
        /// </summary>
        /// <param name="pollIntervalMs">Polling interval in milliseconds (default is 15000ms / 15 seconds).</param>
        void StartMonitoring(int pollIntervalMs = 15000);

        /// <summary>
        /// Suspends the periodic background polling timer.
        /// </summary>
        void StopMonitoring();

        /// <summary>
        /// Triggers an immediate asynchronous bus scan and telemetry refresh across all devices.
        /// </summary>
        void ForceRefresh();

        /// <summary>
        /// Triggers an immediate asynchronous telemetry refresh pass across existing devices without full bus re-enumeration.
        /// </summary>
        void RefreshTelemetry();

        /// <summary>
        /// Reloads device profiles from embedded resources and external filesystem locations.
        /// </summary>
        void ReloadProfiles();

        /// <summary>
        /// Updates the periodic background telemetry polling frequency without triggering an immediate bus scan.
        /// </summary>
        /// <param name="pollIntervalMs">New interval between refresh passes in milliseconds.</param>
        void SetPollInterval(int pollIntervalMs);

        /// <summary>
        /// Gets or sets a value indicating whether the internal Win32DeviceWatcher background thread is enabled.
        /// When false, host applications can forward system PnP events via <see cref="ProcessDeviceChangeNotification"/>.
        /// </summary>
        bool EnableInternalDeviceWatcher { get; set; }

        /// <summary>
        /// Processes a PnP hardware change notification forwarded by a host application with its own message pump.
        /// </summary>
        void ProcessDeviceChangeNotification();
    }
}
