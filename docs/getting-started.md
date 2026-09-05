# Getting Started with OmniHID

OmniHID is a zero-dependency, high-performance C# telemetry engine and diagnostic suite designed to query battery levels, charging states, and hardware parameters across wireless gaming mice, keyboards, headsets, and controllers via native Win32 HID APIs.

---

## System Requirements

- **Operating System:** Windows 7, 8, 8.1, 10, or 11 (32-bit or 64-bit).
- **Runtime:** .NET Framework 4.8+ or .NET 6.0+ (OmniHID binaries are built against .NET Framework 4.8, which is pre-installed on modern Windows versions).
- **Dependencies:** **Zero external dependencies.** Uses native Windows system libraries (`hid.dll`, `setupapi.dll`, `xinput1_4.dll`).
- **Privileges:** Standard user privileges are sufficient for most queries. Administrative privileges may be required if third-party software has exclusive write locks on vendor-specific HID endpoints.

---

## Installation & Compilation

### Option 1: Fast Build via Batch Script (Recommended)

No external SDKs, Visual Studio, or package managers are required. OmniHID compiles using the system C# compiler (`csc.exe`) bundled with Windows:

```cmd
build.bat
```

The script performs two steps in under 2 seconds:
1. Embeds all declarative JSON profiles from `devices/` into `bin\OmniHid.Core.dll`.
2. Compiles the command-line utility into `bin\omni-hid.exe`.

### Option 2: Visual Studio / MSBuild

Open `OmniHid.sln` in Visual Studio 2019/2022 or build via MSBuild:

```cmd
msbuild OmniHid.sln /p:Configuration=Release
```

### Option 3: Modern .NET CLI (`dotnet`)

If you want to use modern .NET SDK tools or package OmniHID for .NET 6/8/9 projects:

```cmd
dotnet build OmniHid.sln -c Release
```

---

## Quick Start for Users (CLI Tool)

Once compiled, run `omni-hid.exe` directly from `bin\`:

```cmd
# Run interactive menu (options 0 to 8)
bin\omni-hid.exe

# Scan and display battery levels for all connected devices
bin\omni-hid.exe scan

# Scan devices with a filter (e.g. mouse, logitech, 25a7)
bin\omni-hid.exe scan mouse

# Show all interfaces without wired/wireless deduplication
bin\omni-hid.exe scan --all
```

For full details on all 8 CLI subcommands and diagnostic tools, see the [CLI Reference](cli-reference.md).

---

## Quick Start for Developers (C# Library Integration)

To use OmniHID inside your own .NET application (WPF, WinForms, Avalonia, Windows Service, Console, etc.), reference `bin\OmniHid.Core.dll`.

### Minimal 60-Second Example

```csharp
using System;
using OmniHid.Core;
using OmniHid.Core.Abstractions;

class Program
{
    static void Main()
    {
        // 1. Instantiate the central telemetry manager
        using (var manager = new OmniManager())
        {
            // 2. Subscribe to real-time events
            manager.DeviceConnected += device =>
            {
                Console.WriteLine($"[+] Device Connected: {device.Name} ({device.Category})");
            };

            manager.TelemetryUpdated += (device, telemetry) =>
            {
                Console.WriteLine($"[*] {device.Name}: {telemetry.LevelPercent}% | {telemetry.StateDescription} | {telemetry.VoltageMv} mV");
            };

            manager.DeviceDisconnected += device =>
            {
                Console.WriteLine($"[-] Device Disconnected: {device.Name}");
            };

            // 3. Start background polling (every 15 seconds) + USB PnP arrival/removal watcher
            manager.StartMonitoring(pollIntervalMs: 15000);

            Console.WriteLine("Press Enter to scan on-demand or stop...");
            Console.ReadLine();

            // On-demand synchronous scan
            var devices = manager.ScanDevices();
            foreach (var dev in devices)
            {
                Console.WriteLine($"Discovered: {dev.Name} - {dev.Telemetry}");
            }
        }
    }
}
```

---

## Next Steps

- **Integrating with Applications:** Read the [Developer Guide](developer-guide.md) for thread safety, UI dispatching, and lifecycle management.
- **API Reference:** Check [API Reference](api-reference.md) for type specifications and member descriptions.
- **Adding Devices:** Check [Device Profiles](device-profiles.md) to add custom peripherals via JSON.
- **Reverse Engineering:** See [Protocol Development](protocol-development.md) to reverse-engineer unknown hardware protocols.
