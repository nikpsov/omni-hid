# OmniHID

<div align="center">

**English** | [Русский](README.ru.md)

[![Platform](https://img.shields.io/badge/platform-Windows%207%E2%80%9311-0078D6.svg?style=flat-square&logo=windows)](https://github.com/)
[![Runtime](https://img.shields.io/badge/.NET-%3E%3D%204.8%20%7C%206.0%2B-512BD4.svg?style=flat-square&logo=dotnet)](https://github.com/)
[![Dependencies](https://img.shields.io/badge/dependencies-Zero%20(Native%20Win32)-brightgreen.svg?style=flat-square)](https://github.com/)
[![Protocols](https://img.shields.io/badge/protocols-14%20Built--in-orange.svg?style=flat-square)](docs/protocol-development.md)
[![License](https://img.shields.io/badge/license-MIT-blue.svg?style=flat-square)](LICENSE)

*Lightweight C# telemetry engine and diagnostic CLI for querying wireless gaming peripheral battery levels via native Win32 HID APIs — no vendor bloatware required.*

</div>

---

## Overview

Modern gaming peripherals (mice, keyboards, headsets, and gamepads) offer high-polling wireless connectivity and onboard telemetry. However, accessing battery levels usually requires running bloated proprietary software suites (Logitech G HUB, Razer Synapse, Corsair iCUE, SteelSeries GG) that consume hundreds of megabytes of RAM, run multiple persistent background services, and introduce input latency.

**OmniHID** replaces them with a clean, unified telemetry engine:
- **For End-Users & Gamers:** A standalone diagnostic CLI with an interactive dashboard, live battery scanning, real-time packet sniffing with diff highlighting, and automated A-B calibration.
- **For Developers & Integrators:** A modular, zero-dependency C# library (`OmniHid.Core.dll`) featuring an event-driven lifecycle, lock-free thread-safe snapshots, hot-reloadable declarative JSON profiles, and an extensible hardware driver architecture.

> **Featured Application**: Check out [**OmniHID Taskbar Battery Indicator**](https://github.com/nikpsov/omni-hid-taskbar-battery-indicator) — a lightweight Windows taskbar battery monitor built on top of this engine.

---

## Why OmniHID?

| Feature | Proprietary Vendor Software | OmniHID |
| :--- | :--- | :--- |
| **Memory Footprint** | 350 MB – 800 MB (Chromium / Electron) | **< 10 MB RAM** |
| **External Dependencies** | Multiple GBs, VC++ runtimes, web services | **Zero** (Pure Win32 P/Invoke) |
| **Background Services** | 4 to 8 active Windows services | **0 services** (In-process or on-demand) |
| **Network & Telemetry** | Mandatory analytics, cloud sync, telemetry | **100% Offline** (No telemetry / tracking) |
| **Input Latency Impact** | Low-level keyboard/mouse hooks (`WH_*_LL`) | **Zero impact** (Non-blocking HID control pipes) |
| **Smart Dual-Mode** | Confusing duplicate entries for cable vs dongle | **Automatic deduplication** (Wired priority) |
| **Hardware Extensibility** | Locked vendor ecosystem | **JSON profiles** with instant hot-reload |

---

## Architecture at a Glance

```
┌────────────────────────────────────────────────────────┐
│                   Consumer Application                 │
│      (WPF / WinForms / Service / CLI / Game Overlay)   │
└───────────────────────────▲────────────────────────────┘
                            │ Events / Telemetry Snapshots
┌───────────────────────────┴────────────────────────────┐
│                       OmniManager                      │
│  - Multi-Interface Aggregation                         │
│  - Smart Dual-Mode Wired/Wireless Deduplication        │
│  - Real-Time PnP USB Device Arrival/Removal Pump       │
└─────────────┬───────────────────────────┬──────────────┘
              │                           │
┌─────────────▼──────────────┐ ┌──────────▼──────────────┐
│       DeviceRegistry       │ │    14+ Protocol Drivers │
│  - Embedded JSON profiles  │ │  - logitech-hidpp / cent │
│  - External JSON profiles  │ │  - areson / royuan / ...│
│  - Hot-reload file watcher │ │  - razer / steelseries  │
└────────────────────────────┘ └──────────┬──────────────┘
                                          │
┌─────────────────────────────────────────▼──────────────┐
│                    Win32HidTransport                   │
│  - SetupAPI.dll: GUID_DEVINTERFACE_HID enumeration     │
│  - Hid.dll: HidD_GetFeature, Overlapped I/O, Exchange  │
│  - XInput: Gamepad battery status (Slots 0..3)         │
│  - Windows 10/11 PnP: DEVPKEY_Device_BatteryLevel      │
└────────────────────────────────────────────────────────┘
```

---

## Quick Start in 60 Seconds

### 1. Build from Source

OmniHID requires no external build tools or package managers. It compiles in ~1 second using the system C# compiler (`csc.exe`) bundled with Windows:

```cmd
build.bat
```

**Output:**
- `bin\OmniHid.Core.dll` — Core telemetry library (.NET Framework 4.8 / .NET 6+ compatible).
- `bin\omni-hid.exe` — Standalone CLI utility.

---

### 2. For Users: Run the CLI

Launch the interactive console menu:
```cmd
omni-hid
```

Or query connected peripherals directly:
```cmd
omni-hid scan
```

```text
Category     Device Name                      VID:PID      Battery        Status         Voltage    Protocol           Endpoints  Hints
────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
🖱 Mouse     ARDOR GAMING Prime X             25A7:FA7B    100%           Full (Wired)   4190 mV    areson             3 EPs      [⚡ Direct Cable]
⌨ Keyboard  ROYUAN Wireless Mechanical       3151:3008    82%            Discharging    --         royuan             4 EPs      [~48h remaining]
🎧 Headset   Logitech G PRO X 2 Lightspeed    046D:0AF7    94%            Discharging    3940 mV    logitech-centurion 2 EPs      [~42h remaining]
🎮 Gamepad   Xbox Wireless Controller         045E:0B12    80%            Discharging    --         xbox-controller    1 EPs      [XInput Slot 0]
```

> **Smart Dual-Mode Priority**: When a peripheral supports both a 2.4 GHz wireless dongle and a direct USB cable (e.g. ARDOR Gaming Prime X), connecting the USB cable automatically promotes the wired connection (`Full (Wired)` / `Charging`) and hides the companion dongle. Unplugging the cable immediately restores the wireless receiver. Use `--all` (or `-a`) to view all raw interfaces simultaneously.

---

### 3. For Developers: C# Library Integration

Reference `bin\OmniHid.Core.dll` in your project and start monitoring with 10 lines of code:

```csharp
using System;
using OmniHid.Core;
using OmniHid.Core.Abstractions;

class Program
{
    static void Main()
    {
        using (var manager = new OmniManager())
        {
            manager.DeviceConnected += dev => 
                Console.WriteLine($"[+] Connected: {dev.Name} ({dev.Category})");

            manager.TelemetryUpdated += (dev, tel) => 
                Console.WriteLine($"[*] {dev.Name}: {tel.LevelPercent}% [{tel.StateDescription}]");

            // Start polling every 15s + monitor USB PnP plug/unplug events
            manager.StartMonitoring(pollIntervalMs: 15000);

            Console.WriteLine("Monitoring peripheral battery levels. Press Enter to exit...");
            Console.ReadLine();
        }
    }
}
```

---

## CLI Diagnostic Toolkit

All commands can be invoked by keyword or interactive menu number (`0`..`8`):

```cmd
omni-hid [command|number] [filter] [options]
```

| # | Command | Syntax | Description |
| :-: | :--- | :--- | :--- |
| `[1]` | `scan` | `omni-hid scan [filter] [--all]` | Scans supported peripherals and queries live battery percentage, state, and voltage. |
| `[2]` | `list` | `omni-hid list [filter] [--flat]` | Dumps all Windows HID interfaces grouped by physical device (`VID:PID`) with report buffer sizes. |
| `[3]` | `debug` | `omni-hid debug [filter]` | Full hardware audit: XInput slots 0–3, IC Fingerprinting, endpoint inspection, and protocol coverage. |
| `[4]` | `hunt` | `omni-hid hunt [filter]` | Automated Feature report sweep (`0x00`..`0xFF`), value range analysis, and Top-5 candidate battery byte scoring. |
| `[5]` | `sniff` | `omni-hid sniff [filter] [--timeout <sec>]` | Live incoming report sniffer with diff-highlighted changed bytes and auto-saved timestamped dump file. |
| `[6]` | `monitor` | `omni-hid monitor` | Real-time USB PnP arrival/removal event tracker via Win32 `WM_DEVICECHANGE`. |
| `[7]` | `calibrate` | `omni-hid calibrate [filter]` | Guided A-B calibration wizard: compares state on battery vs charging cable to isolate charging and battery bytes. |
| `[8]` | `export` | `omni-hid export [filter]` | Generates `device_spec_<VID>_<PID>.md` with endpoint topology and a ready-made LLM prompt for writing a C# driver. |
| `[0]` | `help` | `omni-hid help` | Displays syntax, options, and usage examples. |

---

## Supported Protocols

OmniHID comes with 14 built-in protocol drivers covering common gaming MCU platforms and proprietary vendor standards:

| Protocol ID | Hardware Platform / Peripherals | Telemetry Method |
| :--- | :--- | :--- |
| `logitech-hidpp` | Logitech HID++ 2.0 / 1.0 (Nordic / TI) | 20-byte Long Reports (Feature `0x1000` / `0x1004`) |
| `logitech-centurion` | Logitech G PRO X 2 Lightspeed Audio | 64-byte Audio Control Report (Report ID `0x51`) |
| `areson` | Areson Wireless MCU (e.g. ARDOR Gaming Prime X) | Feature Report `0x05` with `0x55` XOR checksum |
| `royuan` | ROYUAN / YiChip Mechanical Keyboards (Akko, Epomaker) | Output Report `0x83` / `0x80` Overlapped Exchange |
| `compx` | CompX Gaming Wireless Microcontroller (CX52850) | Vendor Feature Reports |
| `sinowealth` | SinoWealth 8051 Wireless Gaming Mice | Vendor Feature Reports |
| `steelseries` | SteelSeries Aerox / Rival / Arctis Wireless | SteelSeries Proprietary HID Control Pipe |
| `razer` | Razer HyperSpeed / Chroma Peripherals | 90-byte Razer Unified HID Report Frame |
| `corsair-headset` | Corsair VOID / HS / Virtuoso Wireless Headsets | Corsair Wireless Audio Protocol |
| `hyperx-headset` | HyperX Cloud / Flight / Stinger Wireless | HyperX Audio Control HID Reports |
| `sony-dualsense` | Sony DualSense & DualShock 4 Gamepads | Direct HID Input Report 0x01 / 0x31 |
| `xbox-controller` | Microsoft Xbox Wireless Controllers | XInput Slot Polling & Bluetooth GATT Battery (`DEVPKEY`) |
| `generic-keyboard` | Fallback Standard HID Keyboards | Standard HID Input Reports |
| `generic-peripheral` | Windows Standard HID Battery Service | HID Battery Service (`UsagePage 0x0085` / `0x0084`) |

---

## Adding New Peripherals

### 1. Declarative JSON Profiles (No Code Required)

If the device uses one of the supported protocol drivers, simply add a `.json` file to `devices/` (or `%APPDATA%\OmniHid\devices\`). Single-line comments (`//`) are fully supported (JSONC):

```jsonc
{
  "model_name": "ARDOR GAMING Prime X",
  "vendor_id": "0x25A7",
  "product_ids": [
    "0xFA7B", // Wired USB cable mode
    "0xFA7C"  // 2.4GHz wireless dongle receiver mode
  ],
  "wired_product_ids": [
    "0xFA7B"  // Declares wired PID for smart dual-mode deduplication
  ],
  "category": "Mouse",
  "protocol": "areson",
  "target_usage_page": "0xFF02",
  "target_usage": "0x0002",
  "battery_life_hours": 60,
  "capabilities": [
    "BatteryLevel",
    "ChargingStatus",
    "VoltageReading"
  ]
}
```

> **Hot Reload**: Files placed in `%APPDATA%\OmniHid\devices\` or `./devices/` are automatically detected via `FileSystemWatcher` and hot-reloaded at runtime without restarting your application. Custom profiles are tagged with `📄` in the CLI.

### 2. Unknown Protocols — 3-Step Reverse Engineering

1. **Classify:** Run `omni-hid debug <device>` to inspect endpoints and detect the MCU architecture via IC Fingerprinting.
2. **Isolate:** Run `omni-hid calibrate <device>` (A-B cable plug diff) or `omni-hid hunt <device>` (Feature sweep) to locate battery and charging bytes.
3. **Generate:** Run `omni-hid export <device>` to create `device_spec_<VID>_<PID>.md`. Paste the pre-formatted prompt into ChatGPT, Claude, or Gemini to generate a ready-to-use C# protocol driver!

---

## Documentation Hub

Explore detailed documentation in the [`docs/`](docs/) directory:

- 📖 [**Getting Started**](docs/getting-started.md) — System requirements, build targets, first run.
- 💻 [**Developer Guide**](docs/developer-guide.md) — C# library integration, lifecycle, UI thread dispatching (WPF/WinForms), system tray sample.
- 📚 [**API Reference**](docs/api-reference.md) — Public types: `IOmniManager`, `IOmniDevice`, `BatteryTelemetry`, `IHidTransport`, etc.
- ⚙️ [**CLI Reference**](docs/cli-reference.md) — Full manual for all 8 `omni-hid` subcommands and diagnostic tools.
- 📄 [**Device Profiles & Hot Reload**](docs/device-profiles.md) — JSON profile schema, hot reload directories, and dual-mode configuration.
- 🔬 [**Protocol Development**](docs/protocol-development.md) — Reverse-engineering wire formats and implementing `IProtocolHandler`.
- 🏛️ [**Architecture & Internals**](docs/architecture.md) — Win32 P/Invoke subsystem, multi-interface aggregation, and zero-allocation snapshot design.

---

## Ecosystem & Projects Built with OmniHID

- 🔋 [**OmniHID Taskbar Battery Indicator**](https://github.com/nikpsov/omni-hid-taskbar-battery-indicator) — Lightweight Windows taskbar and system tray battery monitor for wireless gaming peripherals, powered by the `OmniHid.Core` telemetry engine.

---

## Project Structure

```
omni-hid/
├── devices/                 # Declarative JSON device profiles (embedded at build)
│   ├── gamepads/            # Controller profiles (Xbox, etc.)
│   ├── headsets/            # Headset profiles (Logitech, Corsair, etc.)
│   ├── keyboards/           # Keyboard profiles (ROYUAN, Akko, etc.)
│   └── mice/                # Mouse profiles (Areson, CompX, etc.)
├── docs/                    # Complete technical and developer documentation
│   ├── api-reference.md     # Core C# API documentation
│   ├── architecture.md      # Architecture, Win32 P/Invoke, and aggregation
│   ├── cli-reference.md     # Complete CLI manual and command guide
│   ├── developer-guide.md   # Integration guide for .NET applications
│   ├── device-profiles.md   # JSON profile schema and hot reload manual
│   ├── getting-started.md   # Quickstart, installation, and build options
│   └── protocol-development.md # Reverse engineering & writing IProtocolHandler
├── installer/               # Inno Setup Windows installer script
├── reference/               # Hardware specs and open-source references
├── src/
│   ├── OmniHid.Core/        # Core engine: P/Invoke, transport, registry, protocols
│   └── OmniHid.Cli/         # Command-line diagnostic and analysis tool
├── build.bat                # 1-second build via system csc.exe
└── OmniHid.sln              # Visual Studio / MSBuild solution
```

---

## License

OmniHID is open-source software released under the [MIT License](LICENSE).