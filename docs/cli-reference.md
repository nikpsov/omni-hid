# CLI Reference: omni-hid

`omni-hid` is an interactive diagnostic CLI, peripheral scanner, and packet analysis suite built on top of `OmniHid.Core`.

---

## Invocation Syntax

```cmd
omni-hid [command|number] [filter] [options]
```

- **Without arguments:** Launches the interactive numbered menu (`0`..`8`).
- **Commands:** Can be called by keyword (`scan`, `list`, `debug`, etc.) or by menu number (`1`..`8`).
- **Filter:** An optional case-insensitive substring matching USB Vendor ID, Product ID, peripheral model name, manufacturer, or category (e.g. `mouse`, `ardor`, `25a7`, `logitech`).

---

## Interactive Menu

Running `omni-hid` without arguments presents the numbered console dashboard:

```text
  ____  __  __ _   _ ___   _   _ ___ ____  
 / __ \|  \/  | \ | |_ _| | | | |_ _|  _ \ 
| |  | | |\/| |  \| || |  | |_| || || | | |
| |__| | |  | | |\  || |  |  _  || || |_| |
 \____/|_|  |_|_| \_|___| |_| |_|___|____/ 
 Universal Hardware Peripheral Telemetry Engine
 Mice | Keyboards | Headsets | Gamepads (Win32 HID)
----------------------------------------------------------------------------

  SELECT AN ACTION:
    [1] ⚡ Scan Supported Peripherals & Query Live Battery
    [2] 📋 List All System HID Devices & Interfaces (Detailed Breakdown)
    [3] 🔍 Deep Hardware Diagnostics & Protocol Inspection (Debug)
    [4] 🔋 Battery Protocol Hunter & Report Calculator (Dump & Analyze)
    [5] 📡 Live Input Report Sniffer & Real-Time Diff Monitor
    [6] 🔄 Real-Time USB Arrival / Removal Event Monitor
    [7] 🎯 A-B Battery & Charger Calibration (Guided Plug/Unplug Diff Engine)
    [8] 🤖 Export AI-Ready Protocol Specification (.md)
    [0] 🚪 Exit

  Enter choice [0-8] or command:
```

In interactive mode, options can also be combined (e.g. entering `1 --all` or `5 mouse`).

---

## Command Reference

### 1. `scan` — Query Peripheral Battery & Telemetry

Discovers supported wireless and gaming peripherals, executes the respective protocol driver, and displays real-time battery status in a formatted table.

```cmd
omni-hid scan
omni-hid scan logitech
omni-hid scan mouse
omni-hid scan --all
```

#### Example Output

```text
Category     Device Name                      VID:PID      Battery        Status         Voltage    Protocol           Endpoints  Hints
────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────
🖱 Mouse     ARDOR GAMING Prime X             25A7:FA7B    100%           Full (Wired)   4190 mV    areson             3 EPs      [⚡ Direct Cable]
⌨ Keyboard  ROYUAN Wireless Mechanical       3151:3008    82%            Discharging    --         royuan             4 EPs      [~48h remaining]
🎧 Headset   Logitech G PRO X 2 Lightspeed    046D:0AF7    94%            Discharging    3940 mV    logitech-centurion 2 EPs      [~42h remaining]
🎮 Gamepad   Xbox Wireless Controller         045E:0B12    80%            Discharging    --         xbox-controller    1 EPs      [XInput Slot 0]
```

#### Flags

- `--all`, `-a`, `--no-dedup`: Disables automatic wired/wireless companion receiver deduplication. Displays all physical and logical endpoints simultaneously, tagging dormant dongles with `⏸ Standby`.

---

### 2. `list` — HID Interface Tree & Endpoint Map

Enumerates all registered Windows HID interfaces grouped by physical device (`VID:PID`). Shows `UsagePage:Usage`, buffer sizes, access rights, and PnP tags.

```cmd
omni-hid list
omni-hid list 25a7
omni-hid list --flat
```

#### Flags

- `--flat`: Displays interfaces as a flat table instead of grouping by physical device.

#### Legend & Tag Highlights

- `[🧪 Vendor]`: Vendor-defined usage page (`>= 0xFF00`), prime candidate for telemetry pipes.
- `[🔋 Battery]`: Standard HID Battery Service interface (`UsagePage 0x0085` or `0x0084`).
- `[⚡ PnP: X%]`: Windows 10/11 PnP Bluetooth GATT battery level.
- `[🎮 Gamepad]`: Generic desktop gamepad / joystick usage collection.

---

### 3. `debug` — Deep Diagnostic Hardware Audit

Executes a full diagnostic sweep on connected hardware:
- Probes XInput controller slots (0 through 3).
- Audits all HID interface paths, report lengths, and permissions.
- Runs the **IC Fingerprinting Engine** (`IcFingerprinter`) to detect the microcontroller family (Areson, ROYUAN/YiChip, CompX, SinoWealth, Nordic).
- Summarizes protocol coverage and identifies candidate endpoints.

```cmd
omni-hid debug
omni-hid debug akko
omni-hid debug 25a7
```

---

### 4. `hunt` — Battery Protocol Hunter

Automated reverse-engineering probe. Sweeps Feature Report IDs `0x00` through `0xFF` on vendor endpoints, analyzes returned byte patterns across multiple iterations, applies weighted heuristic scoring, and ranks the Top-5 battery byte candidates.

```cmd
omni-hid hunt
omni-hid hunt 25a7
omni-hid hunt prime
```

#### How Scoring Works

- Values in range `0..100` receive positive probability weighting.
- Stable values (not fluctuating randomly between frames) receive stability bonuses.
- Values that correlate with typical battery percentage intervals receive candidate tags.

---

### 5. `sniff` — Live HID Packet Sniffer

Captures incoming Input Reports in real time with live diff highlighting:
- Displays hex and ASCII representation.
- Highlights bytes that changed since the last packet in bold/color.
- Prints periodic throughput and packet count statistics every 5 seconds.
- Automatically saves a complete timestamped packet dump to `sniff_dump_<VID>_<PID>.txt`.

```cmd
omni-hid sniff
omni-hid sniff mouse
omni-hid sniff 25a7 --timeout 30
```

#### Flags

- `--timeout <sec>`, `--timeout=<sec>`: Automatically stops capturing after the specified duration in seconds. (Without timeout, capture runs until `Enter` or `Escape` is pressed).

---

### 6. `monitor` — Real-Time USB PnP Event Monitor

Listens for hardware arrival and removal notifications via native Win32 `WM_DEVICECHANGE` message pumping.

```cmd
omni-hid monitor
```

When you plug in or unplug a USB cable, wireless receiver, or Bluetooth peripheral, `monitor` prints the event timestamp, device path, and interface class details in real time.

---

### 7. `calibrate` — Guided A-B Calibration

A 3-step interactive wizard designed to isolate battery percentage and charging state bytes:

1. **Phase A (Discharging):** The tool records telemetry packets while the peripheral runs on battery power.
2. **Phase B (Charging):** Prompts the user to connect the USB charging cable, then captures a second packet sample.
3. **Diff Analysis:** Compares Phase A and Phase B buffers to pinpoint exact byte offsets that changed (e.g. charging bit toggled `0x00` -> `0x01`, voltage jumped `3750` -> `4150` mV).

```cmd
omni-hid calibrate ardor
omni-hid calibrate akko
```

---

### 8. `export` — AI Protocol Spec Generator

Generates a complete, markdown-formatted hardware specification file (`device_spec_<VID>_<PID>.md`). 

```cmd
omni-hid export akko
omni-hid export 25a7
```

The generated file includes:
- USB VID, PID, Manufacturer, and Product strings.
- Complete HID endpoint topology and report buffer lengths.
- Feature and Input Report byte dumps.
- A pre-formatted LLM prompt (for Claude, ChatGPT, or Gemini) containing all hardware traces and requesting a complete C# protocol handler adhering to `IProtocolHandler`.

---

## Troubleshooting & Permissions

- **Run as Administrator:** Some proprietary vendor software or anti-cheat drivers open vendor-defined HID endpoints with `FILE_SHARE_READ` but not `FILE_SHARE_WRITE`. If `omni-hid` reports access denied on specific endpoints, launch your command prompt as Administrator.
- **Dormant Receivers:** If a wireless mouse is asleep, moving it or clicking a button wakes up the RF transceiver, allowing OmniHID to retrieve telemetry immediately.
