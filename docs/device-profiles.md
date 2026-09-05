# Device Profiles & Hot Reload

OmniHID uses declarative JSON profiles (supporting JSONC single-line comments `//`) to define peripheral metadata, protocol drivers, endpoint routing, and dual-mode connectivity pairings.

---

## Profile Locations & Discovery Order

`DeviceRegistry` scans the following locations:

1. **External AppData:** `%APPDATA%\OmniHid\devices\**\*.json` (User-created or downloaded profiles).
2. **Local Working Directory:** `./devices/**/*.json` or `<AppDomain.BaseDirectory>\devices\**\*.json`.
3. **Embedded Assembly Resources:** Profiles bundled inside `OmniHid.Core.dll` at compile time.

> **Priority:** External profiles take precedence over embedded defaults. If you place a custom profile for an existing VID/PID in `%APPDATA%\OmniHid\devices\`, OmniHID will override the built-in profile without modifying source code or rebuilding binaries.

---

## Hot Reload Mechanism

OmniHID monitors external profile directories using native .NET `FileSystemWatcher` instances:
- **Instant Detection:** When you add, edit, or delete a `.json` profile in any watched folder, a 300 ms debounced file event triggers `DeviceRegistry.Reload()`.
- **Automatic Rescan:** `OmniManager` automatically initiates a bus rescan and updates `ConnectedDevices`.
- **Visual Tagging:** Custom/external profiles are marked with the `📄` icon in CLI tables and have `IOmniDevice.IsCustomProfile == true` in the API.

---

## JSON Profile Schema & Fields

### Complete Annotated Example

```jsonc
{
  // User-facing model name displayed in CLI and GUI apps
  "model_name": "ARDOR GAMING Prime X",

  // 16-bit USB Vendor ID (hex string or integer)
  "vendor_id": "0x25A7",

  // List of all USB Product IDs associated with this model
  // (e.g. wired cable, 2.4GHz wireless dongle, or Bluetooth PID)
  "product_ids": [
    "0xFA7B", // Direct USB Type-C wired mode
    "0xFA7C"  // 2.4GHz wireless dongle receiver mode
  ],

  // Product IDs that represent a direct wired cable connection.
  // Enables Smart Dual-Mode Deduplication: when this PID is active,
  // the companion wireless dongle is automatically hidden.
  "wired_product_ids": [
    "0xFA7B"
  ],

  // Peripheral category: "Mouse", "Keyboard", "Headset", or "Gamepad"
  "category": "Mouse",

  // Driver ID to handle telemetry communication:
  // "logitech-hidpp", "logitech-centurion", "areson", "royuan", "compx",
  // "sinowealth", "steelseries", "razer", "corsair-headset",
  // "hyperx-headset", "sony-dualsense", "xbox-controller",
  // "generic-keyboard", "generic-peripheral"
  "protocol": "areson",

  // Optional: preferred HID Usage Page for telemetry Feature/Output reports
  "target_usage_page": "0xFF02",

  // Optional: preferred HID Usage under the target usage page
  "target_usage": "0x0002",

  // Rated battery endurance in hours (used for remaining runtime estimation)
  "battery_life_hours": 60,

  // Declared hardware capabilities
  "capabilities": [
    "BatteryLevel",
    "ChargingStatus",
    "VoltageReading"
  ]
}
```

### Field Definitions

| Field | Type | Required | Description |
| :--- | :--- | :--- | :--- |
| `model_name` | String | Yes | Human-readable product name. |
| `vendor_id` | String / Int | Yes | USB Vendor ID (e.g. `"0x25A7"` or `9639`). |
| `product_ids` | Array | Yes | USB Product IDs handled by this profile. |
| `wired_product_ids` | Array | No | PIDs corresponding to direct cable mode for smart deduplication. |
| `category` | String | Yes | `"Mouse"`, `"Keyboard"`, `"Headset"`, or `"Gamepad"`. |
| `protocol` | String | Yes | Protocol handler ID (registered in `OmniManager`). |
| `target_usage_page` | String / Int | No | Target HID Usage Page filter for telemetry endpoints (e.g. `"0xFF02"`). |
| `target_usage` | String / Int | No | Target HID Usage filter under target usage page (e.g. `"0x0002"`). |
| `battery_life_hours` | Number | No | Rated battery life in hours for runtime estimation. |
| `capabilities` | Array | No | Feature tags: `"BatteryLevel"`, `"ChargingStatus"`, `"VoltageReading"`, `"TimeEstimation"`, `"RgbLighting"`, `"DpiSettings"`. |

---

## Step-by-Step: Adding a New Device

### 1. Identify VID and PID
Plug in your peripheral and run:
```cmd
omni-hid list
```
Look for your device's Vendor ID and Product ID in the output (e.g., `VID: 0x25A7, PID: 0xFA7C`).

### 2. Check IC Fingerprint
Run:
```cmd
omni-hid debug
```
OmniHID will identify the microcontroller family (e.g. *CompX / Areson architecture*).

### 3. Create the JSON File
Create a new file in `devices/mice/` (or `%APPDATA%\OmniHid\devices\mice\`):
```json
{
  "model_name": "My Custom Wireless Mouse",
  "vendor_id": "0x25A7",
  "product_ids": ["0xFA7C"],
  "category": "Mouse",
  "protocol": "areson",
  "battery_life_hours": 50,
  "capabilities": ["BatteryLevel", "ChargingStatus"]
}
```

### 4. Verify Live
Run:
```cmd
omni-hid scan
```
Your device will immediately appear in the table with the `📄` icon and live battery level.

---

## Embedding Profiles into `OmniHid.Core.dll`

When building with `build.bat`, all profiles inside the `devices/` directory tree are automatically compiled into `OmniHid.Core.dll` as embedded resources:

```cmd
build.bat
```

This ensures the resulting binary is completely portable with zero external file requirements.
