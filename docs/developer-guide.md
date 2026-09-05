# Developer Guide: Integrating OmniHID

This guide explains how to integrate `OmniHid.Core` into your .NET applications (WPF, WinForms, Avalonia, Windows Services, Background Daemons, Console utilities, or Game Overlays).

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Lifecycle & Initialization](#lifecycle--initialization)
3. [Event-Driven Telemetry](#event-driven-telemetry)
4. [Polling & On-Demand Scanning](#polling--on-demand-scanning)
5. [Smart Dual-Mode Deduplication](#smart-dual-mode-deduplication)
6. [Thread Safety & UI Dispatching](#thread-safety--ui-dispatching)
7. [Working with Devices & Telemetry](#working-with-devices--telemetry)
8. [Registering Custom Protocols](#registering-custom-protocols)
9. [Complete Example: System Tray Battery Monitor](#complete-example-system-tray-battery-monitor)
10. [Resource Cleanup & Disposal](#resource-cleanup--disposal)

---

## Architecture Overview

OmniHID decouples low-level Windows HID communication from peripheral telemetry business logic:

```
┌────────────────────────────────────────────────────────┐
│                   Consumer App                         │
│       (WPF / WinForms / Service / CLI / Overlay)       │
└───────────────────────────▲────────────────────────────┘
                            │ Events / Snapshots
┌───────────────────────────┴────────────────────────────┐
│                    OmniManager                         │
│  - Device reconciliation  - Periodic polling timer     │
│  - Dual-mode deduplication - PnP arrival/removal pump  │
└─────────────┬───────────────────────────┬──────────────┘
              │                           │
┌─────────────▼──────────────┐ ┌──────────▼──────────────┐
│       DeviceRegistry       │ │    IProtocolHandler     │
│  - Embedded JSON profiles  │ │  - logitech-hidpp / cent │
│  - External JSON profiles  │ │  - areson / royuan / ...│
│  - Hot-reload file watcher │ │  - razer / steelseries  │
└────────────────────────────┘ └──────────┬──────────────┘
                                          │
┌─────────────────────────────────────────▼──────────────┐
│                    IHidTransport                       │
│  - Win32 SetupAPI enumeration (GUID_DEVINTERFACE_HID)  │
│  - HidD_GetFeature / HidD_SetFeature / Overlapped I/O  │
│  - XInput slot polling (Controllers 0..3)              │
│  - Windows 10/11 PnP DEVPKEY_Device_BatteryLevel       │
└────────────────────────────────────────────────────────┘
```

- **`IOmniManager` / `OmniManager`**: The top-level orchestrator. Discovers devices, correlates multi-endpoint HID interfaces, tracks plug/unplug events, and polls telemetry.
- **`IOmniDevice`**: Represents a physical peripheral (consolidating all its logical HID interfaces, such as mouse endpoints, consumer controls, and vendor configuration pipes).
- **`BatteryTelemetry`**: An immutable-style snapshot of a device's current power state (percentage, charging flag, voltage, time estimations, and formatted status).
- **`DeviceRegistry`**: The catalog mapping USB Vendor/Product IDs to declarative hardware profiles.

---

## Lifecycle & Initialization

### Basic Setup

To start monitoring, instantiate `OmniManager`:

```csharp
using OmniHid.Core;
using OmniHid.Core.Abstractions;

// Uses default Win32HidTransport and built-in DeviceRegistry
IOmniManager manager = new OmniManager();
```

### Dependency Injection / Custom Components

If you need custom transport mocks for unit testing or an isolated registry:

```csharp
var customTransport = new Win32HidTransport();
var customRegistry = new DeviceRegistry();

IOmniManager manager = new OmniManager(customTransport, customRegistry);
```

---

## Event-Driven Telemetry

`OmniManager` provides four primary events to notify your application of state changes without requiring manual polling loops:

```csharp
// 1. A new peripheral was plugged in, turned on, or connected via Bluetooth
manager.DeviceConnected += (IOmniDevice device) =>
{
    Console.WriteLine($"Connected: {device.Name} [{device.Category}] (VID: {device.VendorId:X4}, PID: {device.ProductId:X4})");
};

// 2. A peripheral was disconnected, turned off, or removed
manager.DeviceDisconnected += (IOmniDevice device) =>
{
    Console.WriteLine($"Disconnected: {device.Name}");
};

// 3. A single peripheral's battery reading was refreshed
manager.TelemetryUpdated += (IOmniDevice device, BatteryTelemetry telemetry) =>
{
    if (telemetry.IsAvailable)
    {
        Console.WriteLine($"{device.Name}: {telemetry.LevelPercent}% ({telemetry.StateDescription})");
    }
    else
    {
        Console.WriteLine($"{device.Name}: Telemetry unavailable ({telemetry.StatusMessage})");
    }
};

// 4. A full scan cycle completed and all device states have been refreshed
manager.DevicesUpdated += (IReadOnlyList<IOmniDevice> allDevices) =>
{
    Console.WriteLine($"Scan finished. Total connected devices: {allDevices.Count}");
};
```

---

## Polling & On-Demand Scanning

OmniHID supports both continuous background monitoring and purely on-demand querying.

### Continuous Background Monitoring

Calling `StartMonitoring()` activates two mechanisms:
1. **Background Polling Timer**: Periodically wakes up to query battery endpoints (default: every 15,000 ms).
2. **Win32 PnP Device Watcher**: Listens for system-wide `WM_DEVICECHANGE` window messages. When a USB device is inserted or removed, OmniHID debounces rapid hardware events (200 ms delay) and automatically triggers a fresh bus scan.

```csharp
// Start monitoring with an update interval of 30 seconds
manager.StartMonitoring(pollIntervalMs: 30000);

// Pause or stop monitoring
manager.StopMonitoring();
```

### On-Demand Querying

If your application prefers manual queries (e.g. on user button click or CLI command):

```csharp
// Synchronously scan the hardware bus and return all active devices
List<IOmniDevice> activeDevices = manager.ScanDevices();

// Refresh telemetry for an individual device
IOmniDevice mouse = activeDevices.FirstOrDefault(d => d.Category == DeviceCategory.Mouse);
if (mouse != null)
{
    BatteryTelemetry freshTelemetry = mouse.RefreshTelemetry();
    Console.WriteLine($"Updated: {freshTelemetry.LevelPercent}%");
}
```

---

## Smart Dual-Mode Deduplication

Many modern gaming mice and keyboards can be connected in two ways simultaneously:
1. Via **2.4 GHz wireless receiver dongle** plugged into a USB port.
2. Via direct **Type-C wired cable** plugged directly into the host PC.

When the user plugs in the charging cable, Windows sees *both* the 2.4 GHz receiver dongle and the wired device. Without deduplication, your UI would display two mice (e.g., "Prime X Mouse" and "Prime X Wireless Dongle").

OmniHID solves this natively via `DeduplicateWiredWireless`:

```csharp
// Enabled by default (true)
manager.DeduplicateWiredWireless = true;
```

- When the wired USB cable is connected, the companion wireless dongle is automatically recognized as dormant and hidden from `ConnectedDevices`. The wired device is reported with `IsWired = true` and state `Full (Wired)` or `Charging`.
- As soon as the cable is disconnected, the wireless dongle entry is seamlessly restored within milliseconds.
- If you need to view raw logical hardware interfaces without deduplication (e.g. for low-level diagnostics), set `DeduplicateWiredWireless = false`.

---

## Thread Safety & UI Dispatching

### Snapshot Thread Safety
`manager.ConnectedDevices` returns a thread-safe, lock-free `IReadOnlyList<IOmniDevice>` snapshot (`volatile` array reference). You can safely enumerate `ConnectedDevices` from any thread without risking collection modification exceptions.

### UI Thread Dispatching
Events such as `TelemetryUpdated` and `DevicesUpdated` are raised from background `ThreadPool` threads or native timer callbacks. When updating UI elements in WPF or Windows Forms, you **must dispatch to the UI thread**:

#### WPF Example

```csharp
manager.TelemetryUpdated += (device, telemetry) =>
{
    Application.Current.Dispatcher.Invoke(() =>
    {
        BatteryProgressBar.Value = telemetry.LevelPercent;
        BatteryStatusLabel.Text = $"{telemetry.LevelPercent}% - {telemetry.StateDescription}";
    });
};
```

#### Windows Forms Example

```csharp
manager.TelemetryUpdated += (device, telemetry) =>
{
    if (this.InvokeRequired)
    {
        this.BeginInvoke(new Action(() => UpdateBatteryUI(device, telemetry)));
    }
    else
    {
        UpdateBatteryUI(device, telemetry);
    }
};
```

---

## Working with Devices & Telemetry

### Inspecting `IOmniDevice`

```csharp
foreach (IOmniDevice device in manager.ConnectedDevices)
{
    Console.WriteLine($"ID:            {device.Id}");
    Console.WriteLine($"Name:          {device.Name}");
    Console.WriteLine($"VID:PID:       {device.VendorId:X4}:{device.ProductId:X4}");
    Console.WriteLine($"Category:      {device.Category}");       // Mouse, Keyboard, Headset, Gamepad
    Console.WriteLine($"Protocol:      {device.ProtocolId}");     // e.g. "logitech-hidpp", "areson"
    Console.WriteLine($"Connected:     {device.IsConnected}");
    Console.WriteLine($"Wired Mode:    {device.IsWired}");
    Console.WriteLine($"Custom Profile:{device.IsCustomProfile}"); // True if loaded from external JSON
    Console.WriteLine($"Endpoints:     {device.Interfaces.Count} HID collections");

    // Check declared hardware capabilities
    if ((device.Capabilities & DeviceCapabilities.BatteryVoltage) != 0)
    {
        Console.WriteLine("  Supports voltage telemetry!");
    }
}
```

### Inspecting `BatteryTelemetry`

```csharp
BatteryTelemetry t = device.Telemetry;

if (t.IsAvailable)
{
    int percent = t.LevelPercent;               // 0 to 100
    BatteryState state = t.State;               // Discharging, Charging, Full, Unavailable
    bool isCharging = t.IsCharging;             // State == BatteryState.Charging
    bool isFull = t.IsFull;                     // State == Full || (100% && Wired)
    int millivolts = t.VoltageMv;               // e.g. 3920 mV (0 if unsupported)
    string formattedTime = t.FormattedTimeRemaining; // "~34h 15m" or null
    string statusText = t.StateDescription;     // "Charging", "Full (Wired)", "Discharging"
    DateTime readingUtc = t.Timestamp;          // UTC timestamp
}
```

---

## Registering Custom Protocols

If you have implemented a custom protocol driver (see [Protocol Development](protocol-development.md)), you can register it with your `OmniManager` instance at runtime:

```csharp
using OmniHid.Core.Abstractions;

public class CustomMcuProtocol : IProtocolHandler
{
    public string ProtocolId => "custom-mcu";
    public string ProtocolName => "Custom Gaming MCU Protocol";
    public bool CanQueryWithoutHidInterfaces => false;

    public BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
    {
        // Custom communication logic
        byte[] buffer = new byte[64];
        if (transport.GetFeatureReport(interfaces[0].DevicePath, 0x05, buffer))
        {
            int percent = buffer[4];
            BatteryState state = buffer[5] == 1 ? BatteryState.Charging : BatteryState.Discharging;
            return BatteryTelemetry.Online(percent, state);
        }
        return BatteryTelemetry.Offline("Read failed");
    }
}

// Register with aliases
manager.RegisterProtocol(new CustomMcuProtocol(), "custom-mcu-alias", "my-vendor-protocol");
```

---

## Complete Example: System Tray Battery Monitor

Here is a complete, minimal console/tray background monitor using `OmniManager`:

```csharp
using System;
using System.Threading;
using OmniHid.Core;
using OmniHid.Core.Abstractions;

class Program
{
    private static readonly AutoResetEvent ExitEvent = new AutoResetEvent(false);

    static void Main(string[] args)
    {
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            ExitEvent.Set();
        };

        using (var manager = new OmniManager())
        {
            manager.DeviceConnected += dev =>
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[+] Connected: {dev.Name} ({dev.Category})");
                Console.ResetColor();
            };

            manager.DeviceDisconnected += dev =>
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[-] Disconnected: {dev.Name}");
                Console.ResetColor();
            };

            manager.TelemetryUpdated += (dev, tel) =>
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                string chargeIcon = tel.IsCharging ? "⚡ Charging" : "🔋 Discharging";
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {dev.Name}: {tel.LevelPercent}% ({chargeIcon})");
                Console.ResetColor();
            };

            // Start polling every 10 seconds with automatic PnP plug/unplug tracking
            manager.StartMonitoring(pollIntervalMs: 10000);

            Console.WriteLine("OmniHID Background Monitor running. Press Ctrl+C to exit.");
            ExitEvent.WaitOne();

            manager.StopMonitoring();
        }
    }
}
```

---

## Resource Cleanup & Disposal

`OmniManager` manages unmanaged resources, including background timers, FileSystemWatcher instances, and Win32 message-only window hooks. Always invoke `Dispose()` when shutting down your application or tearing down your service container:

```csharp
manager.Dispose();
```

If registered with dependency injection (e.g., `Microsoft.Extensions.DependencyInjection`), register it as a singleton:

```csharp
services.AddSingleton<IOmniManager, OmniManager>();
```
The DI container will automatically dispose of `OmniManager` when the host shuts down.
