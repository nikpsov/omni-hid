# Device Profiles, Catalog Organization & OTA Sync

OmniHID uses declarative JSON profiles (supporting JSONC single-line comments `//`) to define peripheral metadata, protocol drivers, endpoint routing, and dual-mode connectivity pairings without recompilation.

---

## Catalog Structure: `verified` vs `unverified`

The profile catalog is partitioned into two primary groups across all peripheral categories:

```
devices/
├── verified/                   # Production-grade tested repository profiles
│   ├── gamepads/               # Tested controller definitions (e.g. Xbox)
│   ├── headsets/               # Tested headset definitions (e.g. Logitech G PRO X 2)
│   ├── keyboards/              # Tested keyboard definitions
│   └── mice/                   # Tested mouse definitions (e.g. ARDOR Gaming Prime X)
└── unverified/                 # Experimental, custom, or community-contributed profiles
    ├── gamepads/               # Sony DualSense, Xbox Elite, 360, etc.
    ├── headsets/               # Razer, SteelSeries, Corsair, HyperX, etc.
    ├── keyboards/              # Akko, Epomaker, Razer, Logitech G, etc.
    └── mice/                   # Lamzu, Pulsar, Razer, Glorious, Logitech, etc.
```

### Profile Status Rules
- **`verified/`**: Loaded with `IsVerified = true`. Discovered devices are treated as fully certified hardware models.
- **`unverified/`**: Loaded with `IsVerified = false`. Marked with the `🧪` icon in CLI output and an `Unverified` golden badge in GUI/Flyout windows.
- **Promotion Workflow**: To promote an experimental profile from `unverified` to `verified`, the developer simply moves the file into `devices/verified/<category>/` in git. No code modifications or profile edits are required.

---

## Profile Discovery Order

`DeviceRegistry` scans disk directories in the following priority order:

1. **Shared User AppData:** `%APPDATA%\OmniHid\devices\` (Shared profile catalog for both CLI and GUI frontend).
2. **Local Portable Directory:** `<AppDomain.BaseDirectory>\devices\` or `./devices/` (Enables standalone portable installations).
3. **Development Parent Tree:** Scans up to 3 parent levels (`..\devices`, `..\..\devices`) for seamless developer workflows inside the repository.

> **Disk-First Architecture:** As of v0.2.0, `OmniHid.Core.dll` contains **zero embedded JSON resources**. The catalog is purely external and disk/network based, allowing instant updates and additions without recompiling or rebuilding binaries.

---

## Over-The-Air (OTA) Catalog Updates

OmniHID provides zero-dependency Over-The-Air (OTA) synchronization directly from the upstream GitHub repository:

- **Clean Payload (~5–8 KB):** GitHub Actions automatically packages the `devices/` directory tree into `devices.zip` upon every commit to `main`, publishing it to the dedicated `catalog` branch.
- **CLI Sync:** Run `omni-hid update` (or `omni-hid sync`) to pull the latest catalog.
- **GUI Sync:** Right-click the taskbar widget and select **«Update Profiles from GitHub»**.
- **Offline Resilient:** If network is unavailable or GitHub is unreachable, local profiles remain completely intact and the engine automatically falls back to local disk definitions.

---

## Hot Reload Mechanism

OmniHID monitors external profile directories using native .NET `FileSystemWatcher` instances:
- **Instant Detection:** When you add, edit, or delete a `.json` profile in any watched folder, a 300 ms debounced file event triggers `DeviceRegistry.Reload()`.
- **Automatic Device Rescan:** `OmniManager` automatically updates all active device instances in-place via `IOmniDevice.UpdateProfile(...)`.
- **Visual Tagging:** Custom/external profiles are marked with the `📄` icon in CLI tables and have `IOmniDevice.IsCustomProfile == true`.

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
Create a new file in `devices/unverified/mice/` (or `%APPDATA%\OmniHid\devices\unverified\mice\`):
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
Your device will immediately appear in the table with the `🧪` (unverified) or `📄` (custom) icon and live battery level.
