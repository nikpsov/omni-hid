---
name: New Device Support / Verification
about: Submit diagnostic dumps, verify support, or request reverse engineering for a new peripheral
title: '[Device]: <Manufacturer> <Model Name>'
labels: 'device-support'
assignees: ''
---

### 1. Device Information

- **Manufacturer / Brand:** <!-- e.g., Razer, Logitech, VGN, Darmoshark, ATK, Ardor Gaming -->
- **Model Name:** <!-- e.g., Dragonfly F1 Pro, Viper V2 Pro, M3 4K, G915 -->
- **Device Category:**
  - [ ] Mouse
  - [ ] Keyboard
  - [ ] Headset
  - [ ] Gamepad
- **Connection Mode(s) Tested:**
  - [ ] 2.4GHz Wireless Dongle
  - [ ] Bluetooth (BLE / Classic)
  - [ ] Direct USB Wired Cable
- **USB Vendor ID (VID):** `0x0000` <!-- e.g., 0x25A7 -->
- **USB Product ID (PID):** `0x0000` <!-- e.g., 0xFA08 (Include both wireless and wired PIDs if dual-mode) -->

---

### 2. Support & Telemetry Status

<!-- Please check all options that currently apply when running OmniHID -->

#### Device & Battery Support:
- [ ] **Supported**: Battery percentage / level is accurately detected by OmniHID (e.g. `85%`)
- [ ] **Charging State Detected**: Status changes between Discharging and Charging when cable is connected
- [ ] **Protocol Identified**: Peripheral communicates over an existing protocol handler (e.g. `compx`, `areson`, `razer`, `logitech-hidpp`, `sinowealth`, `steelseries`, `corsair-headset`, `dualsense`, `xbox`)
- [ ] **Declarative Profile Ready**: Verified JSON profile exists or is provided in this issue
- [ ] **Unsupported / Work-in-Progress**: Device is visible in Windows HID, but battery reading is unavailable, 0%, or requires a new protocol driver

---

### 3. OmniHID Diagnostic Report

<!--
Run the OmniHID diagnostic exporter to collect all hardware descriptors, feature reports, and telemetry:
  1. Run `omni-hid export` (or choose menu option [8] in `omni-hid`)
  2. Select your device from the list
  3. Open the generated `device_spec_VID_PID.md` file and paste its full contents below
-->

<details>
<summary>📋 Click to expand OmniHID Diagnostic Specification (.md)</summary>

```markdown
<!-- PASTE THE CONTENTS OF device_spec_VID_PID.md HERE -->
```

</details>

---

### 4. OEM Software & Additional Context

- **Official Software:** <!-- e.g., Razer Synapse 3, VGN HUB v1.0.8, ATK V HUB, G HUB -->
- **Battery Reading in OEM App:** <!-- e.g., 85% in official app vs what OmniHID reports -->
- **Hardware Notes / Physical Behavior:** <!-- e.g., RGB battery indicator turns amber when charging, device sleeps after 5 minutes of inactivity -->
