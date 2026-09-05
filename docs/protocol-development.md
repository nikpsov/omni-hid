# Protocol Development & Reverse Engineering

This guide covers reverse-engineering unknown peripheral protocols and implementing custom C# drivers (`IProtocolHandler`) for OmniHID.

---

## The 4-Phase Reverse Engineering Workflow

OmniHID provides an end-to-end toolchain to decode battery protocols without commercial USB sniffers or proprietary software:

```
┌──────────────────┐     ┌──────────────────┐     ┌──────────────────┐     ┌──────────────────┐
│   Phase 1: Map   │ ──> │ Phase 2: Calibrate│ ──> │  Phase 3: Sniff  │ ──> │  Phase 4: Export │
│ `omni-hid debug` │     │`omni-hid calibrate│     │ `omni-hid sniff` │     │ `omni-hid export`│
│ Endpoint layout  │     │ A/B delta isolate │     │ Live report diff │     │ AI prompt & spec │
└──────────────────┘     └──────────────────┘     └──────────────────┘     └──────────────────┘
```

---

### Phase 1: Endpoint Topology (`debug`)

Run `omni-hid debug <filter>` to inspect interface descriptors:
- Find endpoints with **Usage Page `>= 0xFF00`** (vendor-specific) or **Report ID collections**.
- Note the `FeatureReportByteLength`, `InputReportByteLength`, and `OutputReportByteLength`.
- Check the **IC Fingerprinter** match (e.g. Areson, ROYUAN/YiChip, CompX, SinoWealth).

---

### Phase 2: Byte Isolation (`calibrate` & `hunt`)

#### Guided A-B Calibration (`calibrate`)
Run `omni-hid calibrate <device>`:
1. Capture state on battery power.
2. Connect charging cable and capture second sample.
3. OmniHID compares both snapshots and outputs changed byte offsets:
   - Identifies the charging flag (bit transition `0` -> `1`).
   - Identifies the voltage rise (e.g. `3800 mV` -> `4200 mV`).

#### Feature Report Sweeper (`hunt`)
Run `omni-hid hunt <device>`:
- Sweeps Feature Report IDs `0x00`..`0xFF`.
- Evaluates byte variability and scores candidates likely representing battery percentage (`0..100`).

---

### Phase 3: Live Verification (`sniff`)

Run `omni-hid sniff <device>`:
- Monitors live Input Reports while operating the peripheral.
- Confirms whether battery reports arrive autonomously (unsolicited) or require an explicit request command.

---

### Phase 4: AI Spec Generation (`export`)

Run `omni-hid export <device>`:
- Generates `device_spec_<VID>_<PID>.md`.
- Includes device descriptors, report dumps, and a ready-made prompt for LLMs (ChatGPT, Claude, Gemini) to generate an initial C# `IProtocolHandler` implementation.

---

## Implementing `IProtocolHandler` in C#

Every hardware protocol in OmniHID implements `IProtocolHandler`:

```csharp
using System;
using System.Collections.Generic;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Protocols
{
    /// <summary>
    /// Protocol handler for Acme Wireless Gaming Peripherals.
    /// </summary>
    public class AcmeProtocol : IProtocolHandler
    {
        public string ProtocolId => "acme";
        public string ProtocolName => "Acme Wireless Gaming Protocol";
        public bool CanQueryWithoutHidInterfaces => false;

        public BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            // 1. Locate the vendor configuration interface (UsagePage >= 0xFF00)
            HidDeviceInfo endpoint = interfaces.Find(i => i.UsagePage >= 0xFF00 && i.FeatureReportByteLength > 0);
            if (endpoint == null)
            {
                return BatteryTelemetry.Offline("No vendor telemetry endpoint found");
            }

            // 2. Query Feature Report 0x05
            byte[] buffer = new byte[endpoint.FeatureReportByteLength];
            buffer[0] = 0x05; // Report ID

            if (!transport.GetFeatureReport(endpoint.DevicePath, 0x05, buffer))
            {
                return BatteryTelemetry.Offline("GetFeatureReport failed");
            }

            // 3. Decode payload bytes
            // Byte 0: Report ID (0x05)
            // Byte 1: Status flag (0x01 = Online)
            // Byte 2: Battery Percentage (0..100)
            // Byte 3: Charging Flag (0 = Discharging, 1 = Charging)
            int percent = buffer[2];
            bool charging = buffer[3] == 0x01;
            BatteryState state = charging ? BatteryState.Charging : BatteryState.Discharging;

            // Optional: calculate remaining runtime based on profile rating
            var telemetry = BatteryTelemetry.Online(percent, state);
            if (profile != null && profile.BatteryLifeHours > 0 && !charging)
            {
                telemetry.TimeToEmptyMinutes = (int)(profile.BatteryLifeHours * 60.0 * (percent / 100.0));
            }

            return telemetry;
        }
    }
}
```

---

## Communication Patterns via `IHidTransport`

`IHidTransport` provides native Win32 methods for various hardware communication styles:

### 1. Synchronous Feature Report (`GetFeatureReport` / `SetFeatureReport`)
Used by Areson, SinoWealth, and many gaming mice:

```csharp
byte[] buffer = new byte[16];
buffer[0] = 0x05; // Report ID
if (transport.GetFeatureReport(devicePath, 0x05, buffer))
{
    int battery = buffer[4];
}
```

### 2. Atomic Exchange: Write-then-Read (`Exchange`)
Used by ROYUAN/YiChip keyboards and Razer controllers. Sends a request command (Output or Feature Report) and immediately reads the response over an Overlapped Input Report pipe:

```csharp
byte[] request = new byte[65];
request[0] = 0x00; // Report ID
request[1] = 0x83; // Command byte: Query Telemetry

byte[] response = new byte[65];
bool success = transport.Exchange(
    writePath: configEndpoint.DevicePath,
    request: request,
    readPath: configEndpoint.DevicePath,
    response: response,
    timeoutMs: 500,
    expectedReportId: 0x00
);

if (success)
{
    int battery = response[3];
}
```

### 3. Windows PnP Bluetooth GATT Battery (`GetPnpBatteryLevel`)
Used by Bluetooth Low Energy (BLE) peripherals (such as Xbox Wireless Controllers connected via standard Bluetooth):

```csharp
int battery = transport.GetPnpBatteryLevel(endpoint.DevicePath);
if (battery >= 0)
{
    return BatteryTelemetry.Online(battery, BatteryState.Discharging);
}
```

---

## Case Studies: Built-in Protocols

### Case Study 1: Areson Wireless MCU (`AresonProtocol.cs`)
- **Transport:** Feature Report `0x05` (7 bytes).
- **Checksum:** Requires a 1-byte checksum: `checksum = (sum of bytes 1..5) ^ 0x55`.
- **Decoding:** Byte 4 contains battery percentage (`0..100`); bit 0 of Byte 5 signals charging.

### Case Study 2: Logitech HID++ 2.0 (`LogitechHidppProtocol.cs`)
- **Transport:** 20-byte Long Report (`Report ID 0x11`).
- **Feature Query:** Queries Feature `0x1000` (Unified Battery) or Feature `0x1004` (Battery Voltage in mV).
- **Payload:** Byte 4 contains percentage, Byte 5 contains charging status flags (`0x00` = Discharging, `0x01` = Charging, `0x02` = Recharging).

### Case Study 3: Microsoft Xbox Controllers (`XboxProtocol.cs`)
- **Dual Support:**
  - When connected via USB cable or Xbox Wireless Adapter: queries `XInputGetBatteryInformation` across slots 0..3 (`XINPUT_BATTERY_DEVTYPE_GAMEPAD`).
  - When connected via standard Bluetooth: queries `DEVPKEY_Device_BatteryLevel` from the Windows SetupAPI device property store.
