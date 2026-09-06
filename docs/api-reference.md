# API Reference: OmniHID

This document provides detailed API specifications for the public types in `OmniHid.Core`.

---

## Table of Contents

- [Namespace: OmniHid.Core.Abstractions](#namespace-omnihidcoreabstractions)
  - [IOmniManager](#iomnimanager)
  - [IOmniDevice](#iomnidevice)
  - [BatteryTelemetry](#batterytelemetry)
  - [BatteryState](#batterystate)
  - [DeviceCategory](#devicecategory)
  - [DeviceCapabilities](#devicecapabilities)
  - [IProtocolHandler](#iprotocolhandler)
- [Namespace: OmniHid.Core](#namespace-omnihidcore)
  - [OmniManager](#omnimanager)
- [Namespace: OmniHid.Core.Devices](#namespace-omnihidcoredevices)
  - [DeviceRegistry](#deviceregistry)
  - [OmniDevice](#omnidevice)
- [Namespace: OmniHid.Core.Profiles](#namespace-omnihidcoreprofiles)
  - [DeviceProfile](#deviceprofile)
  - [JsonProfileLoader](#jsonprofileloader)
- [Namespace: OmniHid.Core.Transport](#namespace-omnihidcoretransport)
  - [IHidTransport](#ihidtransport)
  - [HidDeviceInfo](#hiddeviceinfo)
  - [HidOverlappedReader](#hidoverlappedreader)
- [Namespace: OmniHid.Core.Diagnostics](#namespace-omnihidcorediagnostics)
  - [IcFingerprinter](#icfingerprinter)
  - [IcFingerprintResult](#icfingerprintresult)
  - [BatteryHunter](#batteryhunter)
  - [CalibrationEngine](#calibrationengine)
  - [SpecificationExporter](#specificationexporter)

---

## Namespace: OmniHid.Core.Abstractions

### `IOmniManager`

Defines the central manager contract for discovering, polling, and watching peripheral devices.

```csharp
public interface IOmniManager : IDisposable
```

#### Properties

| Property | Type | Description |
| :--- | :--- | :--- |
| `ConnectedDevices` | `IReadOnlyList<IOmniDevice>` | Thread-safe snapshot of all currently tracked and active devices. |
| `RegisteredOnly` | `bool` | Gets or sets whether only peripherals with validated declarative (.json) profiles are tracked. |

#### Methods

| Method | Return Type | Description |
| :--- | :--- | :--- |
| `StartMonitoring(int pollIntervalMs = 15000)` | `void` | Begins periodic background polling and starts the Win32 USB PnP arrival/removal watcher. |
| `StopMonitoring()` | `void` | Suspends the background polling timer. |
| `ForceRefresh()` | `void` | Triggers an immediate asynchronous bus scan and telemetry refresh pass. |

#### Events

| Event | Type | Description |
| :--- | :--- | :--- |
| `DeviceConnected` | `Action<IOmniDevice>` | Raised when a new peripheral device is discovered. |
| `DeviceDisconnected` | `Action<IOmniDevice>` | Raised when an existing peripheral is disconnected or powered off. |
| `TelemetryUpdated` | `Action<IOmniDevice, BatteryTelemetry>` | Raised whenever a peripheral's battery reading is updated. |
| `DevicesUpdated` | `Action<IReadOnlyList<IOmniDevice>>` | Raised after a full scan cycle completes with a fresh list snapshot. |

---

### `IOmniDevice`

Represents a monitored hardware peripheral device managed by OmniHID.

```csharp
public interface IOmniDevice
```

#### Properties

| Property | Type | Description |
| :--- | :--- | :--- |
| `Id` | `string` | Unique logical identifier (e.g., `"046D:C094:logitech-hidpp"`). |
| `Name` | `string` | Friendly model name (e.g., `"Logitech G PRO X Superlight 2"`). |
| `VendorId` | `ushort` | 16-bit USB Vendor ID (VID). |
| `ProductId` | `ushort` | 16-bit USB Product ID (PID). |
| `Category` | `DeviceCategory` | Functional category (`Mouse`, `Keyboard`, `Headset`, `Gamepad`). |
| `Capabilities` | `DeviceCapabilities` | Bitwise flags of supported features (`BatteryLevel`, `ChargingStatus`, etc.). |
| `ProtocolId` | `string` | Identifier of the driver handling this device (e.g. `"areson"`, `"royuan"`). |
| `IsConnected` | `bool` | `true` if peripheral is currently reachable and online. |
| `IsWired` | `bool` | `true` if connected via direct USB cable rather than wireless receiver. |
| `IsCustomProfile` | `bool` | `true` if instantiated from an external JSON profile. |
| `IsRegisteredProfile` | `bool` | `true` if instantiated from a validated declarative JSON profile. |
| `Telemetry` | `BatteryTelemetry` | Most recent cached battery telemetry snapshot. |
| `Interfaces` | `IReadOnlyList<HidDeviceInfo>` | Aggregated physical Win32 HID interfaces belonging to this device. |

#### Methods

| Method | Return Type | Description |
| :--- | :--- | :--- |
| `RefreshTelemetry()` | `BatteryTelemetry` | Actively queries the physical hardware over HID to refresh telemetry. |

---

### `BatteryTelemetry`

Rich telemetry snapshot representing a device's battery and charging state.

```csharp
public class BatteryTelemetry
```

#### Properties

| Property | Type | Description |
| :--- | :--- | :--- |
| `IsAvailable` | `bool` | `true` if battery data was successfully obtained. |
| `LevelPercent` | `int` | Battery percentage (`0` to `100`). `-1` if unavailable. |
| `State` | `BatteryState` | Charging/discharging status (`Discharging`, `Charging`, `Full`, `Unavailable`). |
| `VoltageMv` | `int` | Measured battery voltage in millivolts, or `0` if unsupported. |
| `TimeToEmptyMinutes` | `int` | Estimated remaining runtime in minutes, or `0` if unknown. |
| `TimeToFullMinutes` | `int` | Estimated charge time in minutes until full, or `0` if unknown. |
| `Timestamp` | `DateTime` | UTC timestamp when this reading was acquired. |
| `StatusMessage` | `string` | Informational status or diagnostic message (e.g., `"Charging"`). |
| `IsWired` | `bool` | `true` if the reading was obtained over a direct wired cable connection. |
| `IsCharging` | `bool` | Helper property returning `State == BatteryState.Charging`. |
| `IsFull` | `bool` | Helper property returning `true` if battery reached full capacity. |
| `StateDescription` | `string` | Formatted status: `"Charging"`, `"Full (Wired)"`, `"Full"`, `"Wired"`, `"Discharging"`. |
| `FormattedTimeRemaining` | `string` | Formatted remaining time string (e.g. `"~34h 15m"`), or `null`. |

#### Factory Methods

- `BatteryTelemetry.Online(int percent, BatteryState state, int voltageMv = 0, string msg = null)`
- `BatteryTelemetry.Offline(string reason = "Device offline")`

---

### `BatteryState`

Enum representing power and charging status.

```csharp
public enum BatteryState
{
    Unavailable = 0, // State unreadable or device offline
    Discharging = 1, // Running on battery power
    Charging    = 2, // Connected to charger/cable and actively charging
    Full        = 3  // Connected to power and battery has reached 100%
}
```

---

### `DeviceCategory`

Enum classifying peripheral types.

```csharp
public enum DeviceCategory
{
    Unknown  = 0,
    Mouse    = 1,
    Keyboard = 2,
    Headset  = 3,
    Gamepad  = 4,
    Other    = 5
}
```

---

### `DeviceCapabilities`

Flags enum declaring peripheral hardware capabilities.

```csharp
[Flags]
public enum DeviceCapabilities
{
    None               = 0,
    BatteryLevel       = 1 << 0, // Battery percentage query supported
    BatteryVoltage     = 1 << 1, // Millivolt voltage telemetry supported
    ChargingStatus     = 1 << 2, // Charging vs discharging status supported
    TimeEstimation     = 1 << 3, // Remaining runtime estimation supported
    InactiveSleepTimer = 1 << 4, // Configurable sleep timeout supported
    Sidetone           = 1 << 5, // Headset microphone sidetone control supported
    RgbLighting        = 1 << 6, // RGB lighting control supported
    DpiSettings        = 1 << 7  // Mouse DPI sensor stage configuration supported
}
```

---

### `IProtocolHandler`

Contract for hardware protocol drivers that query device telemetry over HID or vendor APIs.

```csharp
public interface IProtocolHandler
{
    string ProtocolId { get; }
    string ProtocolName { get; }
    bool CanQueryWithoutHidInterfaces { get; }
    BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile);
}
```

---

## Namespace: OmniHid.Core

### `OmniManager`

Central orchestrator for peripheral discovery, polling, and lifecycle management. Implements `IOmniManager`.

```csharp
public class OmniManager : IOmniManager
```

#### Constructors

- `OmniManager(IHidTransport transport = null, DeviceRegistry registry = null)`

#### Properties

| Property | Type | Description |
| :--- | :--- | :--- |
| `ConnectedDevices` | `IReadOnlyList<IOmniDevice>` | Thread-safe snapshot list of all currently tracked devices. |
| `DeduplicateWiredWireless` | `bool` | Gets/sets whether wireless receivers are suppressed when wired mode is active. Default: `true`. |
| `Registry` | `DeviceRegistry` | The device catalog and profile registry. |

#### Methods

| Method | Return Type | Description |
| :--- | :--- | :--- |
| `RegisterProtocol(IProtocolHandler protocol, params string[] aliases)` | `void` | Registers a custom protocol handler instance and optional aliases. |
| `StartMonitoring(int pollIntervalMs = 15000)` | `void` | Starts periodic background polling and USB PnP device monitoring. |
| `StopMonitoring()` | `void` | Stops periodic background polling. |
| `ForceRefresh()` | `void` | Reloads profiles and triggers an immediate asynchronous bus scan. |
| `ScanDevices()` | `List<IOmniDevice>` | Synchronously scans the hardware bus and returns active devices. |
| `Dispose()` | `void` | Disposes background timers, file watchers, and native window hooks. |

---

## Namespace: OmniHid.Core.Transport

### `IHidTransport`

Low-level transport abstraction for USB/Bluetooth HID I/O communication.

```csharp
public interface IHidTransport : IDisposable
```

#### Methods

| Method | Return Type | Description |
| :--- | :--- | :--- |
| `Enumerate(ushort vendorId = 0, ushort productId = 0)` | `List<HidDeviceInfo>` | Enumerates connected HID interfaces via Win32 SetupAPI. |
| `GetFeatureReport(string devicePath, byte reportId, byte[] buffer)` | `bool` | Reads a Feature Report using Win32 `HidD_GetFeature`. |
| `SetFeatureReport(string devicePath, byte[] buffer)` | `bool` | Writes a Feature Report using Win32 `HidD_SetFeature`. |
| `WriteOutputReport(string devicePath, byte[] buffer)` | `bool` | Writes an Output Report using `HidD_SetOutputReport` or `WriteFile`. |
| `ReadInputReport(string devicePath, byte[] buffer, int timeoutMs)` | `bool` | Reads an Input Report using asynchronous Overlapped I/O. |
| `GetInputReport(string devicePath, byte reportId, byte[] buffer)` | `bool` | Reads an Input Report synchronously via `HidD_GetInputReport`. |
| `SendReport(string devicePath, byte[] reportData, bool isFeatureReport = true)` | `bool` | Sends a command report using either SetFeatureReport or WriteOutputReport. |
| `OpenOverlappedReader(HidDeviceInfo iface, SafeFileHandle handle, int index = 0, int bufferLength = 0)` | `HidOverlappedReader` | Creates an active overlapped reader for asynchronous non-blocking packet reception. |
| `GetPnpBatteryLevel(string devicePath)` | `int` | Reads Windows 10/11 PnP property `DEVPKEY_Device_BatteryLevel` (returns 0..100 or -1). |
| `Exchange(string writePath, byte[] request, string readPath, byte[] response, int timeoutMs, byte expectedReportId = 0)` | `bool` | Executes atomic Write-then-Read sequence with overlapped I/O and optional Report ID filtering. |

---

### `HidDeviceInfo`

Contains Win32 metadata describing an enumerated HID interface collection.

```csharp
public class HidDeviceInfo
{
    public string DevicePath { get; set; }
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    public ushort VersionNumber { get; set; }
    public ushort UsagePage { get; set; }
    public ushort Usage { get; set; }
    public ushort InputReportByteLength { get; set; }
    public ushort OutputReportByteLength { get; set; }
    public ushort FeatureReportByteLength { get; set; }
    public string Manufacturer { get; set; }
    public string Product { get; set; }
    public string SerialNumber { get; set; }
}
```

---

### `HidOverlappedReader`

Encapsulates an active non-blocking asynchronous Win32 Overlapped Read operation on a HID interface endpoint. Provides reusable streaming, event-driven signaling, and memory-safe unmanaged resource disposal.

```csharp
public class HidOverlappedReader : IDisposable
```

#### Properties

| Property | Type | Description |
| :--- | :--- | :--- |
| `Interface` | `HidDeviceInfo` | The target HID interface descriptor. |
| `Handle` | `SafeFileHandle` | Active file handle opened for overlapped read access. |
| `Buffer` | `byte[]` | Buffer storing received report bytes. |
| `InterfaceIndex` | `int` | Optional 1-based display index of this interface collection. |
| `LastBuffer` | `byte[]` | Optional buffer retaining previous frame data for differential analysis. |
| `WaitEvent` | `ManualResetEvent` | Wait handle signaled when an asynchronous read operation completes. |
| `IsPending` | `bool` | Gets a value indicating whether an asynchronous I/O operation is currently pending. |
| `IsCompleted` | `bool` | Gets a value indicating whether the read completed synchronously. |

#### Methods

| Method | Return Type | Description |
| :--- | :--- | :--- |
| `StartRead()` | `bool` | Initiates an asynchronous overlapped `ReadFile` on this endpoint. |
| `CompleteRead(out uint bytesTransferred)` | `bool` | Retrieves the transferred byte count without blocking. |
| `CancelPendingRead()` | `void` | Cancels any currently pending asynchronous read via `CancelIoEx`. |
| `Dispose()` | `void` | Safely cancels pending I/O and releases unmanaged native overlapped structures. |

---

## Namespace: OmniHid.Core.Diagnostics

### `IcFingerprinter`

Analyzes HID collections, report lengths, and vendor identifiers to recognize underlying microcontroller and IC architectures.

```csharp
public static class IcFingerprinter
{
    public static IcFingerprintResult Identify(ushort vid, ushort pid, IReadOnlyList<HidDeviceInfo> interfaces, string devName = null);
}
```

### `IcFingerprintResult`

```csharp
public class IcFingerprintResult
{
    public string ChipsetFamily { get; set; }             // e.g. "CompX / Areson", "ROYUAN / YiChip"
    public IcFingerprintConfidence Confidence { get; set; } // None, Low, Medium, High
    public string Description { get; set; }
    public string RecommendedApproach { get; set; }
    public string MatchedProtocolId { get; set; }
    public bool IsNonBatteryDevice { get; set; }
}
```

---

### `BatteryHunter`

Automated reverse-engineering probe engine. Sweeps Feature Report IDs `0x00`..`0xFF` and known query sequences on vendor endpoints, applies heuristic scoring, and isolates candidate battery percentage bytes.

```csharp
public static class BatteryHunter
{
    public static BatteryHunterResult Hunt(IHidTransport transport, List<HidDeviceInfo> interfaces, Action<string> logger = null);
}
```

#### `BatteryHunterResult`

| Property | Type | Description |
| :--- | :--- | :--- |
| `ReportsProbed` | `int` | Total number of report permutations probed. |
| `ReportsReceived` | `int` | Number of non-zero report responses received. |
| `Candidates` | `List<BatteryCandidate>` | Ranked list of candidate battery byte offsets with heuristic scores. |

---

### `CalibrationEngine`

Differential A-B calibration engine. Captures complete report matrices in discharging (State A) and charging (State B) modes, then performs byte-by-byte delta analysis to isolate charging flag toggles and battery percentage changes.

```csharp
public static class CalibrationEngine
{
    public static CalibrationSnapshot CaptureSnapshot(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile = null);
    public static CalibrationResult AnalyzeDiff(CalibrationSnapshot snapA, CalibrationSnapshot snapB);
}
```

---

### `SpecificationExporter`

Compiles comprehensive reverse-engineering hardware specifications, endpoint topologies, and report byte dumps into an AI-ready Markdown document (`device_spec_<VID>_<PID>.md`).

```csharp
public static class SpecificationExporter
{
    public static void ExportMarkdownSpecification(
        IHidTransport transport,
        string devName,
        ushort vid,
        ushort pid,
        List<HidDeviceInfo> interfaces,
        string outputFilePath);
}
```
