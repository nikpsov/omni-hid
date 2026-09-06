using System;
using System.Collections.Generic;
using System.Threading;
using OmniHid.Cli.Formatting;
using OmniHid.Core;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Diagnostics;
using OmniHid.Core.Protocols;
using OmniHid.Core.Transport;
using OmniHid.Core.Transport.Win32;

namespace OmniHid.Cli.Commands
{
    /// <summary>
    /// Implements the 'debug' command, performing deep hardware diagnostic inspection across
    /// all connected peripherals, XInput gamepads, PnP battery properties, and raw HID endpoints.
    /// </summary>
    public static class DebugCommand
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Command Execution
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Executes the 'debug' command.
        /// </summary>
        /// <param name="filter">Optional filter to narrow inspection to specific peripherals or VIDs/PIDs.</param>
        public static void Execute(string filter = null)
        {
            CliFormatter.PrintBanner();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("============================================================================");
            Console.WriteLine("        OmniHID Universal Hardware Diagnostic & Deep Protocol Inspector     ");
            Console.WriteLine("============================================================================");
            if (!string.IsNullOrEmpty(filter))
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine(" Active Filter: \"{0}\"", filter);
            }
            Console.ResetColor();
            Console.WriteLine();

            bool checkXInput = string.IsNullOrEmpty(filter) ||
                CliFormatter.MatchesFilterString("xinput xbox controller gamepad 045e", filter);

            // ── Section 1: XInput Subsystem ──────────────────────────────────
            if (checkXInput)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("[1] XInput Subsystem Inspection (Xbox Gamepads):");
                Console.ResetColor();

                if (!Win32XInputNative.IsAvailable)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("    [!] XInput DLL (xinput1_4 / xinput1_3 / xinput9_1_0) is NOT available!");
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine("    [+] XInput DLL successfully loaded.");
                    for (int slot = 0; slot < 4; slot++)
                    {
                        Win32XInputNative.XINPUT_STATE state;
                        int stateRes = Win32XInputNative.GetState(slot, out state);

                        Win32XInputNative.XINPUT_BATTERY_INFORMATION batt;
                        int battRes = Win32XInputNative.GetBatteryInformation(slot, Win32XInputNative.BATTERY_DEVTYPE_GAMEPAD, out batt);

                        string typeStr;
                        switch (batt.BatteryType)
                        {
                            case Win32XInputNative.BATTERY_TYPE_DISCONNECTED: typeStr = "Disconnected (0x00)"; break;
                            case Win32XInputNative.BATTERY_TYPE_WIRED: typeStr = "Wired (0x01)"; break;
                            case Win32XInputNative.BATTERY_TYPE_ALKALINE: typeStr = "Alkaline AA (0x02)"; break;
                            case Win32XInputNative.BATTERY_TYPE_NIMH: typeStr = "NiMH Battery Pack (0x03)"; break;
                            case Win32XInputNative.BATTERY_TYPE_UNKNOWN: typeStr = "Unknown (0xFF)"; break;
                            default: typeStr = "0x" + batt.BatteryType.ToString("X2"); break;
                        }

                        string levelStr;
                        switch (batt.BatteryLevel)
                        {
                            case Win32XInputNative.BATTERY_LEVEL_EMPTY: levelStr = "Empty (0x00 ~10%)"; break;
                            case Win32XInputNative.BATTERY_LEVEL_LOW: levelStr = "Low (0x01 ~30%)"; break;
                            case Win32XInputNative.BATTERY_LEVEL_MEDIUM: levelStr = "Medium (0x02 ~65%)"; break;
                            case Win32XInputNative.BATTERY_LEVEL_FULL: levelStr = "Full (0x03 ~100%)"; break;
                            default: levelStr = "0x" + batt.BatteryLevel.ToString("X2"); break;
                        }

                        if (stateRes == Win32XInputNative.ERROR_SUCCESS || battRes == Win32XInputNative.ERROR_SUCCESS)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("    -> Slot {0}: CONNECTED", slot);
                            Console.ResetColor();
                            Console.WriteLine("       - State Code:   {0} (Packet: {1}, Buttons: 0x{2:X4})", stateRes, state.PacketNumber, state.Gamepad.wButtons);
                            Console.WriteLine("       - Battery Code: {0}", battRes);
                            Console.WriteLine("       - Battery Type:  {0}", typeStr);
                            Console.WriteLine("       - Battery Level: {0}", levelStr);
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine("    -> Slot {0}: Not connected (Code {1})", slot, stateRes);
                            Console.ResetColor();
                        }
                    }
                }
                Console.WriteLine();
            }

            // ── Section 2: Universal Hardware Inspection ──────────────────────
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("[2] Universal Peripheral & HID Endpoint Inspection (All Devices):");
            Console.ResetColor();

            using (var transport = new Win32HidTransport())
            using (var manager = new OmniManager(transport))
            {
                var allDevices = manager.ScanDevices();
                var targets = new List<IOmniDevice>();

                foreach (var dev in allDevices)
                {
                    if (CliFormatter.MatchesFilter(dev, filter))
                    {
                        targets.Add(dev);
                    }
                }

                if (targets.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("    No consolidated peripherals matched the specified criteria.");
                    Console.ResetColor();
                }
                else
                {
                    int devNum = 0;
                    foreach (var dev in targets)
                    {
                        devNum++;
                        Console.WriteLine();
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("    ── Device [{0}]: {1} ──────────────────────────────────────", devNum, dev.Name);
                        Console.ResetColor();
                        Console.WriteLine("       Category: {0} | VID: 0x{1:X4} | PID: 0x{2:X4} | Protocol: {3}",
                            dev.Category, dev.VendorId, dev.ProductId, dev.ProtocolId);

                        var fp = IcFingerprinter.Identify(dev.VendorId, dev.ProductId, dev.Interfaces, dev.Name);
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write("       IC Architecture: ");
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine("{0} (Confidence: {1})", fp.ChipsetFamily, fp.Confidence);
                        if (!string.IsNullOrEmpty(fp.Description))
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine("       Architecture Note: {0}", fp.Description);
                        }
                        Console.ResetColor();

                        var tel = dev.Telemetry;
                        Console.Write("       Live Telemetry: ");
                        if (tel.IsAvailable)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("{0}% ({1}) - Status: {2}", tel.LevelPercent,
                                tel.StateDescription, tel.StatusMessage);
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine("Unavailable ({0})", tel.StatusMessage);
                        }
                        Console.ResetColor();

                        Console.WriteLine("       Associated Interfaces ({0} endpoints):", dev.Interfaces.Count);
                        int ifIdx = 0;
                        foreach (var iface in dev.Interfaces)
                        {
                            ifIdx++;
                            InspectInterface(transport, iface, ifIdx);
                        }

                        // Run Protocol Probes
                        ProbeActiveProtocols(transport, dev.Interfaces, dev.VendorId, dev.ProductId);
                    }
                }

                // ── Section 3: Inspect Raw Unmatched HID Interfaces ───────────
                var allRaw = transport.Enumerate();
                var unhandled = new List<HidDeviceInfo>();
                var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var dev in allDevices)
                {
                    foreach (var iface in dev.Interfaces)
                    {
                        seenPaths.Add(iface.DevicePath);
                    }
                }

                foreach (var r in allRaw)
                {
                    if (!seenPaths.Contains(r.DevicePath) && CliFormatter.MatchesFilter(r, filter))
                    {
                        unhandled.Add(r);
                    }
                }

                if (unhandled.Count > 0)
                {
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("[3] Raw Unmapped HID Interfaces Matching Filter ({0} found):", unhandled.Count);
                    Console.ResetColor();

                    var unhandledGroups = new Dictionary<uint, List<HidDeviceInfo>>();
                    foreach (var r in unhandled)
                    {
                        uint key = ((uint)r.VendorId << 16) | r.ProductId;
                        List<HidDeviceInfo> list;
                        if (!unhandledGroups.TryGetValue(key, out list))
                        {
                            list = new List<HidDeviceInfo>();
                            unhandledGroups[key] = list;
                        }
                        list.Add(r);
                    }

                    foreach (var kvp in unhandledGroups)
                    {
                        var group = kvp.Value;
                        var first = group[0];
                        string pName = first.ProductString ?? first.ManufacturerString ?? "Unknown Device";
                        var fpRaw = IcFingerprinter.Identify(first.VendorId, first.ProductId, group, pName);
                        if (fpRaw.IsNonBatteryDevice)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkYellow;
                            Console.WriteLine("    ℹ  Device [0x{0:X4}:0x{1:X4} \"{2}\"]: {3}",
                                first.VendorId, first.ProductId, pName.Trim(), fpRaw.ChipsetFamily);
                            Console.WriteLine("       └─ {0}", fpRaw.Description);
                            Console.ResetColor();
                        }
                    }

                    int rawIdx = 0;
                    foreach (var r in unhandled)
                    {
                        rawIdx++;
                        InspectInterface(transport, r, rawIdx);
                    }
                }

                // ── Section 4: Protocol Coverage Summary ──────────────────────
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("[4] Protocol Coverage Summary:");
                Console.ResetColor();

                foreach (var dev in targets)
                {
                    var tel = dev.Telemetry;
                    string icon;
                    ConsoleColor statusColor;
                    if (tel.IsAvailable)
                    {
                        icon = "✅";
                        statusColor = ConsoleColor.Green;
                    }
                    else
                    {
                        icon = "⚠ ";
                        statusColor = ConsoleColor.DarkYellow;
                    }

                    Console.ForegroundColor = statusColor;
                    Console.Write("    {0} {1}", icon, dev.Name);
                    Console.ResetColor();
                    Console.Write(" → {0}: ", dev.ProtocolId);

                    if (tel.IsAvailable)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("Battery {0}% ({1})", tel.LevelPercent, tel.StateDescription);
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine("No battery data ({0})", tel.StatusMessage ?? "Unknown");
                        Console.ResetColor();

                        var fpDev = IcFingerprinter.Identify(dev.VendorId, dev.ProductId, dev.Interfaces, dev.Name);
                        string pid = dev.ProtocolId ?? "";
                        bool hasKnownProtocol = !string.IsNullOrEmpty(pid) &&
                            pid != "generic-peripheral" && pid != "generic-keyboard";

                        string statusLower = (tel.StatusMessage ?? "").ToLowerInvariant();
                        bool isSleepOrOffline = statusLower.Contains("offline") || statusLower.Contains("sleep") ||
                            statusLower.Contains("not queried") || statusLower.Contains("unavailable");

                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.Write("       └─ ");

                        if (hasKnownProtocol && isSleepOrOffline)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkYellow;
                            Console.WriteLine("Device may be in sleep mode or powered off. Wake the device and re-run debug.");
                        }
                        else if (hasKnownProtocol && !isSleepOrOffline)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkYellow;
                            Console.WriteLine("Protocol '{0}' assigned but query returned no data. Check endpoint access or device firmware.", pid);
                        }
                        else if (!string.IsNullOrEmpty(fpDev.RecommendedApproach))
                        {
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine("HINT [{0} ({1})]: {2}", fpDev.ChipsetFamily, fpDev.Confidence, fpDev.RecommendedApproach);
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine("No vendor-specific endpoints. PnP and standard HID battery pages only.");
                        }
                        Console.ResetColor();
                    }
                }

                if (unhandled.Count > 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.Write("    ❌ ");
                    Console.ResetColor();
                    Console.WriteLine("{0} raw unmapped interface(s) not assigned to any protocol.", unhandled.Count);
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine("       └─ HINT: These may belong to unrecognized peripherals. Run [4] hunt with a VID filter to probe.");
                    Console.ResetColor();
                }
            }

            Console.WriteLine();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Diagnostics Inspection Helpers
        // ═══════════════════════════════════════════════════════════════════════

        private static void InspectInterface(Win32HidTransport transport, HidDeviceInfo iface, int index)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("       [{0}] Usage: 0x{1:X4}:0x{2:X4} ({3})",
                index, iface.UsagePage, iface.Usage, CliFormatter.FormatUsage(iface.UsagePage, iface.Usage));
            Console.ResetColor();

            Console.WriteLine("           VID: 0x{0:X4} | PID: 0x{1:X4} | InLen: {2}B | OutLen: {3}B | FeatLen: {4}B",
                iface.VendorId, iface.ProductId,
                iface.InputReportByteLength, iface.OutputReportByteLength, iface.FeatureReportByteLength);
            Console.WriteLine("           Path: {0}", iface.DevicePath);

            if (!string.IsNullOrEmpty(iface.ProductString) || !string.IsNullOrEmpty(iface.ManufacturerString))
            {
                Console.WriteLine("           Mfr: \"{0}\" | Prod: \"{1}\"", iface.ManufacturerString ?? "", iface.ProductString ?? "");
            }

            // PnP battery property
            int pnpBatt = transport.GetPnpBatteryLevel(iface.DevicePath);
            if (pnpBatt >= 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("           [PnP DEVPKEY_Device_BatteryLevel]: {0}%", pnpBatt);
                Console.ResetColor();
            }

            // Feature Reports probe
            byte[] featCandidates = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x10, 0x11, 0x12, 0x20, 0x80, 0x81, 0x82, 0x83, 0x84, 0x8F };
            int bufLen = Math.Max(64, iface.FeatureReportByteLength > 0 ? (int)iface.FeatureReportByteLength : 64);

            foreach (byte fId in featCandidates)
            {
                byte[] feat = new byte[bufLen];
                if (transport.GetFeatureReport(iface.DevicePath, fId, feat))
                {
                    if (CliFormatter.HasNonZeroData(feat))
                    {
                        Console.WriteLine("           [Feature Report 0x{0:X2}] (len {1}): {2}", fId, feat.Length, CliFormatter.FormatHex(feat, 24));
                    }
                }
            }

            // Input Report probe via GetInputReport
            byte[] inCandidates = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x08, 0x09 };
            foreach (byte inId in inCandidates)
            {
                byte[] inRep = new byte[Math.Max(64, iface.InputReportByteLength > 0 ? (int)iface.InputReportByteLength : 64)];
                if (transport.GetInputReport(iface.DevicePath, inId, inRep))
                {
                    if (CliFormatter.HasNonZeroData(inRep))
                    {
                        Console.WriteLine("           [Input Report 0x{0:X2} (via GetInputReport)]: {1}", inId, CliFormatter.FormatHex(inRep, 24));
                    }
                }
            }

            // Non-blocking short overlapped read (100ms)
            if (iface.InputReportByteLength > 0)
            {
                byte[] asyncIn = new byte[Math.Max(64, (int)iface.InputReportByteLength)];
                if (transport.ReadInputReport(iface.DevicePath, asyncIn, 100))
                {
                    if (CliFormatter.HasNonZeroData(asyncIn))
                    {
                        Console.WriteLine("           [Async Input Report]: {0}", CliFormatter.FormatHex(asyncIn, 24));
                    }
                }
            }
        }

        private static void ProbeActiveProtocols(Win32HidTransport transport, IEnumerable<HidDeviceInfo> interfaces, ushort vid, ushort pid)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("       [Protocol Heuristics Probe]:");
            Console.ResetColor();

            // 1. Areson Protocol Probe
            if (vid == 0x25A7 || vid == 0x0000)
            {
                byte[] aresonCmd = AresonProtocol.BuildQueryCommand(AresonProtocol.CMD_QUERY_STATUS);

                foreach (var iface in interfaces)
                {
                    if (iface.FeatureReportByteLength >= 17 || iface.UsagePage >= 0xFF00)
                    {
                        byte[] sendBuf = aresonCmd;
                        if (iface.FeatureReportByteLength > 17)
                        {
                            sendBuf = new byte[iface.FeatureReportByteLength];
                            Array.Copy(aresonCmd, sendBuf, aresonCmd.Length);
                        }

                        bool sentFeat = transport.SetFeatureReport(iface.DevicePath, sendBuf);
                        bool sentOut = !sentFeat && transport.WriteOutputReport(iface.DevicePath, sendBuf);

                        Console.WriteLine("         [Areson Probe 0x08] Endpoint 0x{0:X4}:0x{1:X4} (FeatLen {2}B) -> SetFeature: {3}, SetOutput: {4}",
                            iface.UsagePage, iface.Usage, iface.FeatureReportByteLength, sentFeat ? "OK" : "Failed", sentOut ? "OK" : "Failed");

                        // Try unnumbered Report ID 0x00 format for 65-byte collections
                        if (iface.FeatureReportByteLength >= 65)
                        {
                            byte[] unnumbered = new byte[iface.FeatureReportByteLength];
                            unnumbered[0] = 0x00;
                            Array.Copy(aresonCmd, 0, unnumbered, 1, Math.Min(aresonCmd.Length, unnumbered.Length - 1));

                            bool sentUnnumbered = transport.SetFeatureReport(iface.DevicePath, unnumbered);
                            bool sentOutUnnumbered = !sentUnnumbered && transport.WriteOutputReport(iface.DevicePath, unnumbered);

                            Console.WriteLine("         [Areson Probe 0x00] Endpoint 0x{0:X4}:0x{1:X4} (Unnumbered 65B) -> SetFeature: {2}, SetOutput: {3}",
                                iface.UsagePage, iface.Usage, sentUnnumbered ? "OK" : "Failed", sentOutUnnumbered ? "OK" : "Failed");
                        }

                        // Try reading responses across all endpoints
                        foreach (var inIface in interfaces)
                        {
                            if (inIface.InputReportByteLength > 0)
                            {
                                byte[] resp = new byte[Math.Max(64, (int)inIface.InputReportByteLength)];
                                if (transport.ReadInputReport(inIface.DevicePath, resp, 250))
                                {
                                    if (CliFormatter.HasNonZeroData(resp))
                                    {
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.WriteLine("         [Areson Response on 0x{0:X4}:0x{1:X4}]: {2}",
                                            inIface.UsagePage, inIface.Usage, CliFormatter.FormatHex(resp, 24));
                                        Console.ResetColor();
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 2. ROYUAN / YiChip Keyboard Protocol Probe
            if (vid == 0x25A7 || vid == 0x3151 || vid == 0x0461 || vid == 0x0000)
            {
                foreach (var iface in interfaces)
                {
                    if (iface.FeatureReportByteLength >= 65)
                    {
                        byte[] cmdList = new byte[] { 0x83, 0x80, 0x81, 0x82, 0x8F };
                        foreach (byte cmd in cmdList)
                        {
                            byte[] unnumSend = new byte[iface.FeatureReportByteLength];
                            unnumSend[0] = 0x00;
                            unnumSend[1] = cmd;
                            bool setOkA = transport.SetFeatureReport(iface.DevicePath, unnumSend);
                            Thread.Sleep(20);

                            byte[] unnumResp = new byte[iface.FeatureReportByteLength];
                            unnumResp[0] = 0x00;
                            bool getOkA = transport.GetFeatureReport(iface.DevicePath, 0x00, unnumResp);

                            if (getOkA && CliFormatter.HasNonZeroData(unnumResp))
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine("         [ROYUAN Probe Unnumbered 0x{0:X2}] SetFeature: {1}, GetFeature: OK -> {2}",
                                    cmd, setOkA ? "OK" : "Failed", CliFormatter.FormatHex(unnumResp, 24));
                                Console.ResetColor();
                            }

                            byte[] numSend = new byte[iface.FeatureReportByteLength];
                            numSend[0] = cmd;
                            bool setOkB = transport.SetFeatureReport(iface.DevicePath, numSend);
                            Thread.Sleep(20);

                            byte[] numResp = new byte[iface.FeatureReportByteLength];
                            numResp[0] = cmd;
                            bool getOkB = transport.GetFeatureReport(iface.DevicePath, cmd, numResp);

                            if (getOkB && CliFormatter.HasNonZeroData(numResp))
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine("         [ROYUAN Probe Numbered 0x{0:X2}] SetFeature: {1}, GetFeature: OK -> {2}",
                                    cmd, setOkB ? "OK" : "Failed", CliFormatter.FormatHex(numResp, 24));
                                Console.ResetColor();
                            }
                        }
                    }
                }
            }

            // 3. Vendor 0xFFFF and CompX Protocol Probe
            foreach (var iface in interfaces)
            {
                if (iface.UsagePage == 0xFFFF || iface.UsagePage >= 0xFF00)
                {
                    byte[] vIn = new byte[Math.Max(64, (int)iface.InputReportByteLength)];
                    if (transport.ReadInputReport(iface.DevicePath, vIn, 200))
                    {
                        if (CliFormatter.HasNonZeroData(vIn))
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("         [Vendor 0x{0:X4}:0x{1:X4} Spontaneous Packet]: {2}",
                                iface.UsagePage, iface.Usage, CliFormatter.FormatHex(vIn, 16));
                            Console.ResetColor();
                        }
                    }
                }
            }
        }
    }
}
