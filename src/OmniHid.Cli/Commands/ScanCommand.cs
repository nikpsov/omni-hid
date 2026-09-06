using System;
using System.Collections.Generic;
using System.Text;
using OmniHid.Cli.Formatting;
using OmniHid.Core;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Transport.Win32;

namespace OmniHid.Cli.Commands
{
    /// <summary>
    /// Implements the 'scan' command, querying all connected peripherals and presenting battery telemetry.
    /// </summary>
    public static class ScanCommand
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Command Execution
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Executes the 'scan' command.
        /// </summary>
        /// <param name="filter">Optional substring filter for peripheral names or IDs.</param>
        /// <param name="showAll">If true, suppresses wired/wireless receiver deduplication.</param>
        /// <param name="registeredOnly">If true, shows only devices with validated declarative (.json) profiles.</param>
        public static void Execute(string filter = null, bool showAll = false, bool registeredOnly = false)
        {
            CliFormatter.PrintBanner();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            if (registeredOnly)
            {
                Console.WriteLine(string.IsNullOrEmpty(filter)
                    ? "Scanning for registered (.json) wireless & gaming peripherals..."
                    : string.Format("Scanning for registered (.json) peripherals matching '{0}'...", filter));
            }
            else
            {
                Console.WriteLine(string.IsNullOrEmpty(filter)
                    ? "Scanning for supported wireless & gaming peripherals (unfiltered)..."
                    : string.Format("Scanning for peripherals matching '{0}'...", filter));
            }
            Console.ResetColor();
            Console.WriteLine();

            using (var transport = new Win32HidTransport())
            using (var manager = new OmniManager(transport))
            {
                manager.RegisteredOnly = registeredOnly;
                manager.DeduplicateWiredWireless = !showAll;
                var allDevices = manager.ScanDevices();
                var devices = new List<IOmniDevice>();

                foreach (var d in allDevices)
                {
                    if (CliFormatter.MatchesFilter(d, filter))
                    {
                        devices.Add(d);
                    }
                }

                if (devices.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    if (registeredOnly)
                    {
                        Console.WriteLine("No registered (.json) peripherals detected.");
                        Console.WriteLine("Tip: Place your device profile in devices/*.json or %APPDATA%\\OmniHid\\devices\\");
                        Console.WriteLine("     Run 'omni-hid scan' (or option [1]) to view all detected devices with heuristics.");
                        Console.WriteLine("     Run 'omni-hid list' (or option [2]) to view all active HID hardware interfaces.");
                        Console.WriteLine("     Run 'omni-hid hunt' (or option [4]) to reverse-engineer battery reports from any device.");
                    }
                    else
                    {
                        Console.WriteLine("No recognized peripherals detected.");
                        Console.WriteLine("Tip: Run 'omni-hid list' (or option [2]) to view all active HID hardware interfaces.");
                        Console.WriteLine("     Run 'omni-hid hunt' (or option [4]) to reverse-engineer battery reports from any device.");
                    }
                    Console.ResetColor();
                    return;
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                if (registeredOnly)
                {
                    Console.Write("Mode: Registered .json profiles only (verified hardware)");
                }
                else
                {
                    Console.Write("Mode: All supported peripherals (Tip: Run 'omni-hid 9' or 'omni-hid scan -r' for .json profiles only)");
                }
                if (showAll)
                {
                    Console.Write("  |  [--all mode: showing all interfaces without wired deduplication]");
                }
                Console.WriteLine();
                Console.ResetColor();
                Console.WriteLine();

                Console.WriteLine("{0,-12} {1,-32} {2,-12} {3,-14} {4,-14} {5,-10} {6,-18} {7,-10} {8}",
                    "Category", "Device Name", "VID:PID", "Battery", "Status", "Voltage", "Protocol", "Endpoints", "Hints");
                Console.WriteLine(new string('─', 140));

                // Statistics accumulators
                int withBattery = 0;
                int withoutBattery = 0;
                int chargingCount = 0;
                int totalVendorEps = 0;
                int customProfileCount = 0;

                foreach (var dev in devices)
                {
                    string catIcon = CliFormatter.GetCategoryIcon(dev.Category);
                    string catStr = string.Format("{0} {1}", catIcon, dev.Category);

                    bool isCustom = dev.IsCustomProfile;
                    if (isCustom) customProfileCount++;
                    string devName = (isCustom ? "📄 " : "   ") + dev.Name;
                    if (devName.Length > 30) devName = devName.Substring(0, 27) + "...";

                    string vidPid = string.Format("{0:X4}:{1:X4}", dev.VendorId, dev.ProductId);

                    // Endpoint analysis
                    int epCount = dev.Interfaces.Count;
                    int vendorEpCount = 0;
                    bool hasBatteryPage = false;
                    bool hasPowerPage = false;
                    int pnpBattLevel = -1;
                    foreach (var iface in dev.Interfaces)
                    {
                        if (iface.UsagePage >= 0xFF00) vendorEpCount++;
                        if (iface.UsagePage == 0x0085) hasBatteryPage = true;
                        if (iface.UsagePage == 0x0084) hasPowerPage = true;
                        if (pnpBattLevel < 0)
                        {
                            int pnp = transport.GetPnpBatteryLevel(iface.DevicePath);
                            if (pnp >= 0) pnpBattLevel = pnp;
                        }
                    }
                    totalVendorEps += vendorEpCount;

                    string epStr = vendorEpCount > 0
                        ? string.Format("{0} ({1}xV)", epCount, vendorEpCount)
                        : epCount.ToString();

                    // Hints
                    var hints = new StringBuilder();
                    if (showAll && !dev.IsWired)
                    {
                        for (int w = 0; w < devices.Count; w++)
                        {
                            if (devices[w].IsWired &&
                                devices[w].VendorId == dev.VendorId &&
                                string.Equals(devices[w].Name, dev.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                hints.Append("⏸ Standby (Wired active) ");
                                break;
                            }
                        }
                    }
                    if (hasBatteryPage) hints.Append("Battery 0x85 ");
                    if (hasPowerPage) hints.Append("Power 0x84 ");
                    if (pnpBattLevel >= 0) hints.AppendFormat("PnP:{0}% ", pnpBattLevel);

                    Console.Write("{0,-12} {1,-32} {2,-12} ", catStr, devName, vidPid);

                    var tel = dev.Telemetry;
                    if (!tel.IsAvailable)
                    {
                        withoutBattery++;
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        string statusMsg = tel.StatusMessage ?? "Unknown";
                        if (statusMsg.Length > 12) statusMsg = statusMsg.Substring(0, 9) + "...";
                        Console.Write("{0,-14} {1,-14} ", "--", statusMsg);
                        Console.ResetColor();

                        if (hints.Length == 0 && vendorEpCount == 0)
                            hints.Append("⚠ No telemetry");
                        else if (hints.Length == 0)
                            hints.Append("⚠ Try [4] hunt");
                    }
                    else
                    {
                        withBattery++;
                        if (tel.IsCharging || tel.State == BatteryState.Full || tel.IsWired) chargingCount++;

                        // Color-code based on battery level
                        if (tel.IsCharging || tel.State == BatteryState.Full || tel.IsWired) Console.ForegroundColor = ConsoleColor.Cyan;
                        else if (tel.LevelPercent >= 50) Console.ForegroundColor = ConsoleColor.Green;
                        else if (tel.LevelPercent >= 20) Console.ForegroundColor = ConsoleColor.Yellow;
                        else Console.ForegroundColor = ConsoleColor.Red;

                        string timeStr = (tel.State == BatteryState.Discharging && !string.IsNullOrEmpty(tel.FormattedTimeRemaining))
                            ? " (" + tel.FormattedTimeRemaining + ")"
                            : "";
                        string battStr = string.Format("{0}%{1}", tel.LevelPercent, timeStr);
                        Console.Write("{0,-14} ", battStr);

                        Console.ForegroundColor = (tel.IsCharging || tel.State == BatteryState.Full || tel.IsWired) ? ConsoleColor.Cyan : ConsoleColor.White;
                        string stateStr = tel.StateDescription;
                        Console.Write("{0,-14} ", stateStr);
                        Console.ResetColor();
                    }

                    string voltStr = tel.VoltageMv > 0 ? string.Format("{0} mV", tel.VoltageMv) : "--";
                    Console.Write("{0,-10} {1,-18} {2,-10} ", voltStr, dev.ProtocolId, epStr);

                    // Hints coloring
                    string hintStr = hints.ToString().Trim();
                    if (hintStr.StartsWith("⚠"))
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                    }
                    else if (!string.IsNullOrEmpty(hintStr))
                    {
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                    }
                    Console.Write("{0}", hintStr);
                    Console.ResetColor();
                    Console.WriteLine();
                }

                // Summary section
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(new string('─', 140));
                Console.ResetColor();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("── Summary ── ");
                Console.ResetColor();

                Console.WriteLine("Found {0} peripheral(s): {1} with battery data, {2} charging, {3} without telemetry",
                    devices.Count, withBattery, chargingCount, withoutBattery);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("Vendor-specific endpoints in system: {0} (candidates for protocol development)", totalVendorEps);
                if (customProfileCount > 0)
                    Console.WriteLine("Custom profiles loaded: {0} from devices/*.json", customProfileCount);
                Console.ResetColor();
            }
            Console.WriteLine();
        }
    }
}
