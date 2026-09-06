---
name: csharp-commenting-style
description: Standardized English commenting and XML-documentation guidelines for C# codebase in OmniHID
---

# C# Commenting & Documentation Style Guide

This skill defines the uniform commenting and documentation standards for the OmniHID project. All code written or updated in this repository MUST adhere to these rules.

## Core Principles

1. **English Only**: All comments, documentation strings, parameter descriptions, remarks, and commit notes MUST be written in clear English.
2. **Explain the "Why", Not Just the "What"**: Comments should explain intent, hardware peculiarities, protocol quirks, timing considerations, and bitwise layout rather than merely paraphrasing the code.
3. **Professional XML Documentation**: Every public and internal type, method, constructor, property, event, and enum member must have XML doc comments.

---

## 1. XML Documentation (`///`)

### Class and Interface Documentation
Include `<summary>` and optionally `<remarks>` for hardware protocol details, packet structure, or architecture notes:

```csharp
/// <summary>
/// Implements battery telemetry query protocol for Logitech HID++ 2.0 peripherals.
/// </summary>
/// <remarks>
/// HID++ 2.0 communication uses 20-byte Long Reports (Report ID 0x11).
/// Feature 0x1000 provides battery level percentage and charging state.
/// Feature 0x1004 serves as a fallback for voltage readings in millivolts.
/// </remarks>
public class LogitechHidppProtocol : IProtocolHandler
```

### Methods and Constructors
Include `<summary>`, `<param>`, `<returns>`, and `<exception>` where appropriate:

```csharp
/// <summary>
/// Queries the peripheral device for current battery and connectivity telemetry.
/// </summary>
/// <param name="transport">The active low-level HID transport layer for I/O operations.</param>
/// <param name="interfaces">List of enumerated HID interfaces associated with this physical peripheral.</param>
/// <param name="profile">Declarative device profile containing model metadata and ratings.</param>
/// <returns>A populated <see cref="BatteryTelemetry"/> instance representing the device state.</returns>
public BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
```

### Properties and Enum Members
Every property and enum value must have a concise, accurate description:

```csharp
/// <summary>
/// Peripheral is actively connected and consuming battery power.
/// </summary>
Discharging = 1,
```

---

## 2. Visual Section Dividers

For classes with multiple logical phases (e.g., protocol framing, Win32 I/O, discovery, helpers), use standardized 75-character divider lines:

```csharp
// ═══════════════════════════════════════════════════════════════════════════
// Protocol Frame Helpers
// ═══════════════════════════════════════════════════════════════════════════
```

---

## 3. Protocol & Hardware Inline Comments

When interacting with raw HID packets, wire formats, or Win32 structures:
- Document byte indices and meanings:
  ```csharp
  // Byte 0: Report ID (0x09)
  // Byte 1: Command echo (0x04)
  // Byte 6: Battery percentage (0..100)
  // Byte 7: Charging flag (0x01 = charging, 0x00 = discharging)
  ```
- Document bitwise operations:
  ```csharp
  // Mask lower 4 bits to extract raw battery gauge (0..10)
  int rawLevel = batteryByte & 0x0F;
  ```
- Explain fallback reasoning:
  ```csharp
  // Fallback to Feature Report 0x1004 (Unified Battery) if Feature 0x1000 is unavailable
  ```

---

## 4. Anti-Patterns to Avoid

- ❌ Avoid redundant comments that repeat identifier names: `// gets or sets the id`
- ❌ Do NOT use `#region` / `#endregion` blocks (reduces code scannability)
- ❌ Do NOT leave empty XML doc tags (`<param name="foo"></param>`)
- ❌ Do NOT mix languages; avoid non-English text in code comments
