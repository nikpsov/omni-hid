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
    }
}
