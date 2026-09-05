# Architecture & Internals: OmniHID

This document explains the architectural principles, Win32 subsystems, and internal algorithms that power OmniHID.

---

## Design Principles

1. **Zero External Dependencies:** No NuGet packages, no runtime C++ redistributables, no driver installations. Pure C# via Win32 P/Invoke.
2. **Minimal Resource Footprint:** Sub-10 MB working set memory (compared to 300–600 MB for OEM software suites like Razer Synapse, Logitech G HUB, or Corsair iCUE).
3. **Zero Gaming Latency Impact:** All I/O is asynchronous or handled via dedicated background worker threads. OmniHID never registers low-level input hooks (`WH_MOUSE_LL` / `WH_KEYBOARD_LL`), ensuring zero mouse polling jitter or keyboard lag.
4. **Resilient Hardware Querying:** Non-blocking Win32 Overlapped I/O with strict timeouts prevents the engine from ever hanging if a device powers off mid-transfer.

---

## Subsystem Architecture

```
┌────────────────────────────────────────────────────────────────────────┐
│                          Consumer Application                          │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
┌───────────────────────────────────▼────────────────────────────────────┐
│                              OmniManager                               │
│  - Multi-Interface Aggregation Engine                                  │
│  - Smart Dual-Mode Wired/Wireless Deduplication Arbiter               │
│  - ThreadPool Task Dispatcher & Volatile Snapshot Manager              │
└───────────┬───────────────────────────────────────────────┬────────────┘
            │                                               │
┌───────────▼───────────┐                       ┌───────────▼───────────┐
│     DeviceRegistry    │                       │    Win32DeviceWatcher │
│ - Hot-Reload Engine   │                       │ - Message-only HWND   │
│ - JSON Profile Catalog│                       │ - WM_DEVICECHANGE     │
│ - Heuristic Fallbacks │                       │ - 200ms Debounce      │
└───────────────────────┘                       └───────────────────────┘
            │                                               │
┌───────────▼───────────────────────────────────────────────▼───────────┐
│                           Win32HidTransport                           │
│  ┌──────────────────────┬──────────────────────┬────────────────────┐ │
│  │     SetupAPI.dll     │       Hid.dll        │    XInput1_4.dll   │ │
│  │ - SetupDiGetClassDevs│ - HidD_GetFeature    │ - Dynamic resolver │ │
│  │ - SetupDiEnumDevice..│ - HidD_SetFeature    │ - XInputGetBattery │ │
│  │ - DEVPKEY_Battery    │ - Overlapped Read    │   Information      │ │
│  └──────────────────────┴──────────────────────┴────────────────────┘ │
└───────────────────────────────────────────────────────────────────────┘
```

---

## 1. Native Win32 Subsystems

OmniHID communicates with the Windows kernel via three primary native DLLs:

### `setupapi.dll` (Device Discovery)
- Uses `SetupDiGetClassDevs` with `GUID_DEVINTERFACE_HID` (`{4D1E55B2-F16F-11CF-88CB-001111000030}`) and `DIGCF_PRESENT | DIGCF_DEVICEINTERFACE`.
- Enumerates every registered HID collection on the system and retrieves unique Win32 symbolic device paths (`\\?\hid#vid_...#...`).
- Reads extended PnP device properties via `SetupDiGetDevicePropertyW`, extracting `DEVPKEY_Device_BatteryLevel` for Bluetooth GATT peripherals.

### `hid.dll` (HID Protocol I/O)
- **`HidD_GetAttributes`**: Extracts 16-bit USB Vendor ID, Product ID, and Version Number.
- **`HidD_GetPreparsedData` & `HidP_GetCaps`**: Extracts interface Usage Page, Usage, and buffer lengths (`InputReportByteLength`, `OutputReportByteLength`, `FeatureReportByteLength`).
- **`HidD_GetFeature` & `HidD_SetFeature`**: Issues control transfers to endpoint pipe 0 for hardware configuration and battery query packets.

### `xinput1_4.dll` (Game Controller Telemetry)
- Employs a resilient fallback loader trying `xinput1_4.dll` (Windows 8/10/11), `xinput1_3.dll` (DirectX End-User Runtimes), and `xinput9_1_0.dll` (Windows 7).
- Polls `XInputGetBatteryInformation` for connected controller slots 0 through 3, retrieving battery type (`Alkaline`, `Nimh`, `Lithium`) and level (`Empty`, `Low`, `Medium`, `Full`).

---

## 2. Multi-Interface Logical Aggregation

A single physical USB gaming mouse typically presents between 3 and 6 distinct logical HID collections to Windows:
- **Interface 0 (Usage Page 0x0001, Usage 0x0002):** Standard mouse pointing device (X/Y movement, primary buttons).
- **Interface 1 (Usage Page 0x000C, Usage 0x0001):** Consumer control collection (volume knob, media keys).
- **Interface 2 (Usage Page 0xFF00..0xFFFF):** Vendor-defined proprietary configuration channel (battery status, DPI steps, onboard profiles, lighting).

Without aggregation, applications would see multiple disconnected devices for one physical mouse.

OmniHID solves this by:
1. Enumerating all active interfaces.
2. Grouping collections by physical peripheral identity `(VendorId, ProductId)`.
3. Consolidating all collections into a single `IOmniDevice` instance.
4. Supplying the complete collection list to the assigned `IProtocolHandler`, allowing the protocol to select the exact vendor configuration endpoint while ignoring mouse motion endpoints.

---

## 3. Smart Dual-Mode Wired/Wireless Deduplication

When a wireless peripheral (e.g. mouse or keyboard) is plugged into a USB cable for charging while its 2.4 GHz wireless receiver dongle remains plugged into another port:
- Both the wireless dongle (PID A) and the wired peripheral (PID B) appear on the USB bus.
- Without deduplication, both would be presented as independent devices with identical or conflicting battery readings.

OmniHID's deduplication algorithm:
1. Each device profile declares its known direct-cable Product IDs in `wired_product_ids`.
2. During bus reconciliation, if an active device matches a `wired_product_ids` entry, OmniHID marks the wired device as primary (`IsWired = true`, status `Full (Wired)` or `Charging`).
3. OmniHID detects the paired wireless companion receiver dongle and marks it as dormant (`⏸ Standby`), suppressing it from the default `ConnectedDevices` list.
4. As soon as the wired cable is unplugged, the wireless companion is instantly promoted back to active status without requiring application restarts.

---

## 4. Real-Time USB PnP Arrival/Removal Engine

Instead of relying solely on continuous CPU-intensive polling, OmniHID uses native Win32 device notification hooks:
1. Creates a hidden Win32 message-only window (`HWND_MESSAGE`).
2. Calls `RegisterDeviceNotification` for the HID interface class GUID (`GUID_DEVINTERFACE_HID`).
3. Intercepts `WM_DEVICECHANGE` with `DBT_DEVICEARRIVAL` and `DBT_DEVICEREMOVECOMPLETE`.
4. Passes events through a **200 ms debounce timer**: prevents thread pool starvation when composite USB devices register multiple logical interfaces in rapid succession.

---

## 5. Zero-Allocation Snapshot Architecture

In telemetry-driven applications, UI frameworks or background loops frequently query `manager.ConnectedDevices`.

To prevent thread lock contention and garbage collection pauses:
- `_connectedDevicesSnapshot` is stored as an internal `volatile IOmniDevice[]` array.
- When `ScanAndUpdate()` completes, the active devices dictionary is copied into a new array and atomically swapped in:
  ```csharp
  _connectedDevicesSnapshot = newDevicesList.ToArray();
  ```
- Reading `manager.ConnectedDevices` requires **zero locks** and creates **zero heap allocations**, guaranteeing thread-safe, non-blocking reads from any thread.
