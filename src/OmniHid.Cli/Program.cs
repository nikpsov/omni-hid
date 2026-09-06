using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using OmniHid.Core;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Devices;
using OmniHid.Core.Diagnostics;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;
using OmniHid.Core.Transport.Win32;

namespace OmniHid.Cli
{
    /// <summary>
    /// Command-line diagnostic, monitoring, packet capture, and battery protocol hunter entry point.
    /// Provides interactive numbered menu navigation, deep device scanning, and automated battery dump calculation.
    /// </summary>
    class Program
    {
        private static bool _interactiveMode = true;
        private static bool _flatListMode = false;
        private static int _sniffTimeoutSeconds = 0;

        static Program()
        {
            // Resolves OmniHid.Core.dll from embedded manifest resources when omni-hid.exe is run standalone
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    string assemblyName = new AssemblyName(args.Name).Name;
                    if (string.Equals(assemblyName, "OmniHid.Core", StringComparison.OrdinalIgnoreCase))
                    {
                        var asm = Assembly.GetExecutingAssembly();
                        using (var stream = asm.GetManifestResourceStream("OmniHid.Core.dll"))
                        {
                            if (stream != null)
                            {
                                byte[] buffer = new byte[stream.Length];
                                stream.Read(buffer, 0, buffer.Length);
                                return Assembly.Load(buffer);
                            }
                        }
                    }
                }
                catch { }
                return null;
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Main Entry Point & Command Dispatch
        // ═══════════════════════════════════════════════════════════════════════

        static void Main(string[] args)
        {
            Console.Title = "OmniHID - Universal Peripheral Telemetry & Battery Probe";
            Console.OutputEncoding = Encoding.UTF8;

            if (args == null || args.Length == 0)
            {
                _interactiveMode = true;
                RunInteractiveMenu();
                return;
            }

            _interactiveMode = false;

            // Parse positional and flag arguments
            string command = null;
            string filter = null;
            bool showAll = false;
            bool registeredOnly = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].Trim();
                string aLow = a.ToLowerInvariant();
                if (aLow == "--flat")
                {
                    _flatListMode = true;
                }
                else if (aLow == "--all" || aLow == "-a" || aLow == "--no-dedup")
                {
                    showAll = true;
                }
                else if (aLow == "--registered" || aLow == "-r" || aLow == "--registered-only" || aLow == "--json")
                {
                    registeredOnly = true;
                }
                else if (aLow == "--timeout" && i + 1 < args.Length)
                {
                    int t;
                    if (int.TryParse(args[i + 1].Trim(), out t) && t > 0)
                    {
                        _sniffTimeoutSeconds = t;
                    }
                    i++; // Skip the value argument
                }
                else if (aLow.StartsWith("--timeout="))
                {
                    int t;
                    if (int.TryParse(aLow.Substring("--timeout=".Length), out t) && t > 0)
                    {
                        _sniffTimeoutSeconds = t;
                    }
                }
                else if (command == null)
                {
                    command = aLow;
                }
                else if (filter == null)
                {
                    filter = a;
                }
            }

            if (command == null) command = "help";

            switch (command)
            {
                case "1":
                case "scan":
                    RunScan(filter, showAll, registeredOnly);
                    break;
                case "9":
                case "registered":
                case "scan-registered":
                case "json":
                    RunScan(filter, showAll, true);
                    break;
                case "2":
                case "list":
                    RunList(filter);
                    break;
                case "3":
                case "debug":
                case "diag":
                    RunDebug(filter);
                    break;
                case "4":
                case "hunt":
                case "battery":
                    RunBatteryHunter(filter);
                    break;
                case "5":
                case "sniff":
                case "dump":
                    RunSniff(filter);
                    break;
                case "6":
                case "monitor":
                    RunMonitor();
                    break;
                case "7":
                case "calibrate":
                case "cal":
                    RunCalibrate(filter);
                    break;
                case "8":
                case "export":
                case "spec":
                case "--export-spec":
                    RunExportSpec(filter);
                    break;
                case "--help":
                case "-h":
                case "help":
                case "?":
                default:
                    PrintHelp();
                    break;
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Interactive Numbered Menu
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Runs the interactive numbered console menu loop when the application is launched without CLI arguments.
        /// </summary>
        private static void RunInteractiveMenu()
        {
            while (true)
            {
                try
                {
                    Console.Clear();
                }
                catch { }

                PrintBanner();

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  SELECT AN ACTION:");
                Console.ResetColor();
                Console.WriteLine("    [1] ⚡ Scan All Peripherals & Live Battery (Standard / Unfiltered)");
                Console.WriteLine("    [2] 📋 List All System HID Devices & Interfaces (Detailed Breakdown)");
                Console.WriteLine("    [3] 🔍 Deep Hardware Diagnostics & Protocol Inspection (Debug)");
                Console.WriteLine("    [4] 🔋 Battery Protocol Hunter & Report Calculator (Dump & Analyze)");
                Console.WriteLine("    [5] 📡 Live Input Report Sniffer & Real-Time Diff Monitor");
                Console.WriteLine("    [6] 🔄 Real-Time USB Arrival / Removal Event Monitor");
                Console.WriteLine("    [7] 🎯 A-B Battery & Charger Calibration (Guided Plug/Unplug Diff Engine)");
                Console.WriteLine("    [8] 🤖 Export AI-Ready Protocol Specification (.md)");
                Console.WriteLine("    [9] 📄 Scan Registered Devices Only (Verified .json Profiles)");
                Console.WriteLine("    [0] 🚪 Exit");
                Console.WriteLine();
                Console.Write("  Enter choice [0-9] or command: ");

                string rawChoice = Console.ReadLine();
                string choice = rawChoice != null ? rawChoice.Trim().ToLowerInvariant() : "";
                Console.WriteLine();

                switch (choice)
                {
                    case "1":
                    case "scan":
                        RunScan(null, false, false);
                        SafeWaitForKey();
                        break;
                    case "1 --all":
                    case "1 -a":
                    case "scan --all":
                    case "scan -a":
                        RunScan(null, true, false);
                        SafeWaitForKey();
                        break;
                    case "1 -r":
                    case "1 --registered":
                    case "scan -r":
                    case "scan --registered":
                    case "9":
                    case "registered":
                    case "scan-registered":
                    case "json":
                        RunScan(null, false, true);
                        SafeWaitForKey();
                        break;
                    case "9 --all":
                    case "9 -a":
                    case "registered --all":
                        RunScan(null, true, true);
                        SafeWaitForKey();
                        break;
                    case "2":
                    case "list":
                        RunList(null);
                        SafeWaitForKey();
                        break;
                    case "3":
                    case "debug":
                    case "diag":
                        RunDebug(null);
                        SafeWaitForKey();
                        break;
                    case "4":
                    case "hunt":
                    case "battery":
                        RunBatteryHunter(null);
                        SafeWaitForKey();
                        break;
                    case "5":
                    case "sniff":
                    case "dump":
                        RunSniff(null);
                        SafeWaitForKey();
                        break;
                    case "6":
                    case "monitor":
                        RunMonitor();
                        SafeWaitForKey();
                        break;
                    case "7":
                    case "calibrate":
                    case "cal":
                        RunCalibrate(null);
                        SafeWaitForKey();
                        break;
                    case "8":
                    case "export":
                    case "spec":
                        RunExportSpec(null);
                        SafeWaitForKey();
                        break;
                    case "0":
                    case "exit":
                    case "quit":
                    case "q":
                        return;
                    default:
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine("Unknown choice. Please enter a number from 0 to 9.");
                        Console.ResetColor();
                        SafeWaitForKey();
                        break;
                }
            }
        }

        /// <summary>
        /// Prompts the user to press a key to return to the interactive menu or exit.
        /// </summary>
        private static void SafeWaitForKey()
        {
            if (!_interactiveMode) return;
            try
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("Press any key to return to menu (or '0' / 'q' to exit)...");
                Console.ResetColor();
                if (!Console.IsInputRedirected)
                {
                    ConsoleKeyInfo key = Console.ReadKey(true);
                    if (key.KeyChar == '0' || key.KeyChar == 'q' || key.KeyChar == 'Q' || key.Key == ConsoleKey.Escape)
                    {
                        Environment.Exit(0);
                    }
                }
            }
            catch { }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // UI Banners & Help
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Prints the ASCII art banner and application title to stdout.
        /// </summary>
        static void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"  ____  __  __ _   _ ___   _   _ ___ ____  ");
            Console.WriteLine(@" / __ \|  \/  | \ | |_ _| | | | |_ _|  _ \ ");
            Console.WriteLine(@"| |  | | |\/| |  \| || |  | |_| || || | | |");
            Console.WriteLine(@"| |__| | |  | | |\  || |  |  _  || || |_| |");
            Console.WriteLine(@" \____/|_|  |_|_| \_|___| |_| |_|___|____/ ");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(" Universal Hardware Peripheral Telemetry Engine");
            Console.WriteLine(" Mice | Keyboards | Headsets | Gamepads (Win32 HID)");
            Console.WriteLine(new string('-', 76));
            Console.ResetColor();
        }

        /// <summary>
        /// Displays available command-line options, descriptions, and usage examples.
        /// </summary>
        static void PrintHelp()
        {
            PrintBanner();
            Console.WriteLine();
            Console.WriteLine("Usage: omni-hid [command|number] [filter] [options]");
            Console.WriteLine();
            Console.WriteLine("Actions (use name or number):");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  [1] scan      [filter]   Detect peripherals & query live battery (standard / unfiltered)");
            Console.WriteLine("  [2] list      [filter]   List all connected HID device interfaces, VIDs, PIDs & Usages");
            Console.WriteLine("  [3] debug     [filter]   Deep diagnostic for ALL devices (XInput, PnP, endpoints, protocols)");
            Console.WriteLine("  [4] hunt      [filter]   Automated battery protocol hunter & report calculator (dumps & % heuristics)");
            Console.WriteLine("  [5] sniff     [filter]   Live raw HID packet sniffer with real-time diff highlighting & dump file");
            Console.WriteLine("  [6] monitor              Real-time event monitor (watches USB arrivals/removals live)");
            Console.WriteLine("  [7] calibrate [filter]   Guided A-B calibration (diff state on battery vs charging cable)");
            Console.WriteLine("  [8] export    [filter]   Export AI-ready protocol spec (.md) with LLM prompt & dumps");
            Console.WriteLine("  [9] registered [filter]  Scan only verified peripherals with declarative (.json) profiles");
            Console.WriteLine("  [0] help                 Show this help information");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("  --registered, -r     [scan] Filter and show only verified declarative (.json) devices");
            Console.WriteLine("  --all, -a            [scan] Show all devices without wired/wireless receiver deduplication");
            Console.WriteLine("  --flat               [list] Show interfaces as a flat table without device grouping");
            Console.WriteLine("  --timeout <sec>      [sniff] Auto-stop capture after <sec> seconds (default: unlimited)");
            Console.ResetColor();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  Note: In interactive mode (no CLI args), sniffer runs until Enter/Escape is pressed.");
            Console.WriteLine("  When launched via terminal with no --timeout, sniffer also runs until Enter/Escape.");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("  omni-hid                          (Launch interactive numbered menu)");
            Console.WriteLine("  omni-hid 1                        (Quick scan via number)");
            Console.WriteLine("  omni-hid 7 ardor                  (A-B calibrate battery & charging flag on Ardor mouse)");
            Console.WriteLine("  omni-hid 8 akko                   (Export AI-ready protocol spec markdown for Akko)");
            Console.WriteLine("  omni-hid 4 25a7                   (Hunt battery telemetry on Ardor/Areson 0x25A7)");
            Console.WriteLine("  omni-hid sniff mouse              (Sniff live HID packets from mouse endpoints)");
            Console.WriteLine("  omni-hid sniff --timeout 60       (Sniff all devices for 60 seconds max)");
            Console.WriteLine("  omni-hid list --flat              (Flat table without device grouping)");
            Console.WriteLine("  omni-hid debug akko               (Inspect Akko wireless keyboard endpoints & probes)");
            Console.ResetColor();
            Console.WriteLine();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Command Handlers: List & Scan
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Dumps all active HID device interface endpoints present on the host system with detailed usage and report lengths.
        /// </summary>
        /// <param name="filter">Optional substring to filter by VID, PID, manufacturer, or product name.</param>
        static void RunList(string filter = null)
        {
            PrintBanner();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(string.IsNullOrEmpty(filter)
                ? "Scanning all connected USB HID device interfaces in system..."
                : string.Format("Scanning USB HID device interfaces matching '{0}'...", filter));
            if (_flatListMode)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  (--flat mode: showing flat table without device grouping)");
            }
            Console.ResetColor();
            Console.WriteLine();

            using (var transport = new Win32HidTransport())
            {
                var devs = transport.Enumerate();
                var matched = new List<HidDeviceInfo>();

                foreach (var d in devs)
                {
                    if (MatchesFilter(d, filter))
                    {
                        matched.Add(d);
                    }
                }

                if (matched.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("No HID interfaces matched the specified filter.");
                    Console.ResetColor();
                    return;
                }

                Console.WriteLine("Found {0} HID interface collection(s):\n", matched.Count);

                if (_flatListMode)
                {
                    // ── Flat table mode (original behavior) ──
                    PrintListFlat(transport, matched);
                }
                else
                {
                    // ── Grouped mode (default): group by VID:PID with device headers ──
                    PrintListGrouped(transport, matched);
                }

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("Tip: Endpoints marked with [🧪 Vendor] or [🔋 Battery] are prime candidates for battery telemetry.");
                Console.WriteLine("     Run 'omni-hid hunt' to automatically probe Feature Reports and calculate battery level.");
                Console.WriteLine("     Use '--flat' flag for a flat table without device grouping.");
                Console.ResetColor();
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Prints HID interfaces in the original flat table format.
        /// </summary>
        private static void PrintListFlat(Win32HidTransport transport, List<HidDeviceInfo> matched)
        {
            Console.WriteLine("{0,-10} {1,-10} {2,-32} {3,-24} {4,-24} {5}",
                "VID", "PID", "Usage (Page:Usage)", "Report Buffers", "Device Info", "Battery / Tags");
            Console.WriteLine(new string('─', 124));

            foreach (var d in matched)
            {
                PrintListInterfaceRow(transport, d, false);
            }
        }

        /// <summary>
        /// Prints HID interfaces grouped by VID:PID with device headers and indented interface rows.
        /// </summary>
        private static void PrintListGrouped(Win32HidTransport transport, List<HidDeviceInfo> matched)
        {
            // Group by VID:PID
            var groups = new Dictionary<uint, List<HidDeviceInfo>>();
            var groupOrder = new List<uint>();
            foreach (var d in matched)
            {
                uint key = ((uint)d.VendorId << 16) | d.ProductId;
                List<HidDeviceInfo> list;
                if (!groups.TryGetValue(key, out list))
                {
                    list = new List<HidDeviceInfo>();
                    groups[key] = list;
                    groupOrder.Add(key);
                }
                list.Add(d);
            }

            int deviceNum = 0;
            foreach (uint gKey in groupOrder)
            {
                deviceNum++;
                var group = groups[gKey];
                var first = group[0];
                string prod = !string.IsNullOrEmpty(first.ProductString) ? first.ProductString.Trim() : "";
                string mfr = !string.IsNullOrEmpty(first.ManufacturerString) ? first.ManufacturerString.Trim() : "";
                string title = !string.IsNullOrEmpty(prod) ? prod : (!string.IsNullOrEmpty(mfr) ? mfr : "Unknown Device");

                // Count vendor endpoints and check for special pages
                int vendorCount = 0;
                bool hasBattery = false;
                foreach (var iface in group)
                {
                    if (iface.UsagePage >= 0xFF00) vendorCount++;
                    if (iface.UsagePage == 0x0085) hasBattery = true;
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("#{0}  ", deviceNum);
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("{0} ", title);
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("(VID: 0x{0:X4}, PID: 0x{1:X4}) — {2} interface(s)", first.VendorId, first.ProductId, group.Count);
                if (vendorCount > 0)
                {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.Write(" [{0} vendor]", vendorCount);
                }
                if (hasBattery)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write(" [Battery]");
                }
                Console.ResetColor();
                Console.WriteLine();

                // Sub-header
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("    {0,-14} {1,-10} {2,-28} {3,-7} {4,-7} {5,-7} {6}",
                    "Usage Page", "Usage", "Type", "In", "Out", "Feat", "Tags");
                Console.ResetColor();

                foreach (var d in group)
                {
                    PrintListInterfaceRow(transport, d, true);
                }
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Prints a single HID interface row for the list command (flat or indented).
        /// </summary>
        private static void PrintListInterfaceRow(Win32HidTransport transport, HidDeviceInfo d, bool indented)
        {
            string prefix = indented ? "    " : "";

            string mfr = (d.ManufacturerString ?? "").Trim();
            string prod = (d.ProductString ?? "").Trim();
            string devTitle = string.IsNullOrEmpty(prod) ? mfr : prod;
            if (devTitle.Length > 22) devTitle = devTitle.Substring(0, 19) + "...";

            string usageDesc = FormatUsage(d.UsagePage, d.Usage);
            if (usageDesc.Length > 26) usageDesc = usageDesc.Substring(0, 23) + "...";

            // Tag detection
            string tags = "";
            if (d.UsagePage == 0x0085) tags += "[🔋 Battery 0x85] ";
            else if (d.UsagePage == 0x0084) tags += "[⚡ Power 0x84] ";
            else if (d.UsagePage >= 0xFF00) tags += "[🧪 Vendor 0x" + d.UsagePage.ToString("X4") + "] ";

            int pnpBatt = transport.GetPnpBatteryLevel(d.DevicePath);
            if (pnpBatt >= 0) tags += string.Format("[⚡ PnP: {0}%] ", pnpBatt);

            if (d.UsagePage == 0x0001 && (d.Usage == 0x0005 || d.Usage == 0x0004))
                tags += "[🎮 Gamepad] ";

            if (string.IsNullOrEmpty(tags)) tags = "--";

            // Color code special candidate endpoints
            bool isSpecial = d.UsagePage == 0x0085 || d.UsagePage == 0x0084 || d.UsagePage >= 0xFF00 || pnpBatt >= 0;
            if (isSpecial) Console.ForegroundColor = ConsoleColor.Yellow;

            if (indented)
            {
                Console.WriteLine("{0}0x{1:X4}         0x{2:X4}     {3,-28} {4,3}B    {5,3}B    {6,3}B    {7}",
                    prefix, d.UsagePage, d.Usage, usageDesc,
                    d.InputReportByteLength, d.OutputReportByteLength, d.FeatureReportByteLength, tags);
            }
            else
            {
                string bufLengths = string.Format("In:{0,3}B Out:{1,3}B Feat:{2,3}B",
                    d.InputReportByteLength, d.OutputReportByteLength, d.FeatureReportByteLength);
                Console.WriteLine("0x{0:X4}     0x{1:X4}     {2,-32} {3,-24} {4,-24} {5}",
                    d.VendorId, d.ProductId, usageDesc, bufLengths, devTitle, tags);
            }

            if (isSpecial) Console.ResetColor();
        }

        /// <summary>
        /// Discovers supported peripherals, queries live battery telemetry, and displays a formatted summary table.
        /// </summary>
        /// <param name="filter">Optional substring filter for peripheral names or IDs.</param>
        /// <param name="showAll">If true, suppresses wired/wireless dongle deduplication.</param>
        /// <param name="registeredOnly">If true, shows only devices with validated declarative (.json) profiles.</param>
        static void RunScan(string filter = null, bool showAll = false, bool registeredOnly = false)
        {
            PrintBanner();
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
                    if (MatchesFilter(d, filter))
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
                    string catIcon = GetCategoryIcon(dev.Category);
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

        /// <summary>
        /// Runs a persistent event loop listening for real-time USB peripheral arrival, removal, and telemetry updates.
        /// </summary>
        static void RunMonitor()
        {
            PrintBanner();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[MONITOR MODE STARTED] Listening for USB connection and battery events...");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Plug in / unplug dongles or power on devices. Press Enter to stop.\n");
            Console.ResetColor();

            using (var manager = new OmniManager())
            {
                manager.DeviceConnected += dev =>
                {
                    string icon = dev.IsCustomProfile ? "📄 " : "";
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[{0:HH:mm:ss}] [+] DEVICE CONNECTED: {1}{2} ({3}) [{4}]",
                        DateTime.Now, icon, dev.Name, dev.Category, dev.ProtocolId);
                    Console.ResetColor();
                };

                manager.DeviceDisconnected += dev =>
                {
                    string icon = dev.IsCustomProfile ? "📄 " : "";
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[{0:HH:mm:ss}] [-] DEVICE DISCONNECTED: {1}{2} ({3})",
                        DateTime.Now, icon, dev.Name, dev.Category);
                    Console.ResetColor();
                };

                manager.TelemetryUpdated += (dev, tel) =>
                {
                    if (!tel.IsAvailable) return;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    string icon = dev.IsCustomProfile ? "📄 " : "";
                    string timeInfo = (tel.State == BatteryState.Discharging && !string.IsNullOrEmpty(tel.FormattedTimeRemaining))
                        ? " [" + tel.FormattedTimeRemaining + " remaining]"
                        : "";
                    Console.WriteLine("[{0:HH:mm:ss}] [~] {1}{2}: Battery {3}% ({4}){5}{6}",
                        DateTime.Now, icon, dev.Name, tel.LevelPercent,
                        tel.StateDescription,
                        timeInfo,
                        tel.VoltageMv > 0 ? " [" + tel.VoltageMv + " mV]" : "");
                    Console.ResetColor();
                };

                manager.StartMonitoring(10000);

                while (true)
                {
                    if (!Console.IsInputRedirected && Console.KeyAvailable)
                    {
                        try { Console.ReadKey(true); } catch { }
                        break;
                    }
                    Thread.Sleep(200);
                }
            }

            Console.WriteLine("\nMonitor stopped.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Command Handlers: Deep Diagnostic (Debug Mode for ALL Devices)
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Performs deep diagnostic inspection across all connected peripherals, keyboards, mice, headsets, gamepads, and raw HID endpoints.
        /// </summary>
        /// <param name="filter">Optional filter to narrow inspection to specific peripherals or VIDs/PIDs.</param>
        static void RunDebug(string filter = null)
        {
            PrintBanner();
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
                MatchesFilterString("xinput xbox controller gamepad 045e", filter);

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
                    if (MatchesFilter(dev, filter))
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
                    if (!seenPaths.Contains(r.DevicePath) && MatchesFilter(r, filter))
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

                    // Group unhandled interfaces by VID:PID to show fingerprinting
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

                        // Provide actionable hints for devices without telemetry
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

        /// <summary>
        /// Performs deep inspection of a single HID interface endpoint: Usage, report lengths, PnP properties, and report probing.
        /// </summary>
        private static void InspectInterface(Win32HidTransport transport, HidDeviceInfo iface, int index)
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("       [{0}] Usage: 0x{1:X4}:0x{2:X4} ({3})",
                index, iface.UsagePage, iface.Usage, FormatUsage(iface.UsagePage, iface.Usage));
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

            // Feature Reports probe (IDs 0x00 .. 0x20 + Akko 0x80 .. 0x8F)
            byte[] featCandidates = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x10, 0x11, 0x12, 0x20, 0x80, 0x81, 0x82, 0x83, 0x84, 0x8F };
            int bufLen = Math.Max(64, iface.FeatureReportByteLength > 0 ? (int)iface.FeatureReportByteLength : 64);

            foreach (byte fId in featCandidates)
            {
                byte[] feat = new byte[bufLen];
                if (transport.GetFeatureReport(iface.DevicePath, fId, feat))
                {
                    if (HasNonZeroData(feat))
                    {
                        Console.WriteLine("           [Feature Report 0x{0:X2}] (len {1}): {2}", fId, feat.Length, FormatHex(feat, 24));
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
                    if (HasNonZeroData(inRep))
                    {
                        Console.WriteLine("           [Input Report 0x{0:X2} (via GetInputReport)]: {1}", inId, FormatHex(inRep, 24));
                    }
                }
            }

            // Non-blocking short overlapped read (100ms)
            if (iface.InputReportByteLength > 0)
            {
                byte[] asyncIn = new byte[Math.Max(64, (int)iface.InputReportByteLength)];
                if (transport.ReadInputReport(iface.DevicePath, asyncIn, 100))
                {
                    if (HasNonZeroData(asyncIn))
                    {
                        Console.WriteLine("           [Async Input Report]: {0}", FormatHex(asyncIn, 24));
                    }
                }
            }
        }

        /// <summary>
        /// Actively tests candidate protocol commands (Areson, CompX, SinoWealth) on the device endpoints.
        /// </summary>
        private static void ProbeActiveProtocols(Win32HidTransport transport, IEnumerable<HidDeviceInfo> interfaces, ushort vid, ushort pid)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("       [Protocol Heuristics Probe]:");
            Console.ResetColor();

            // 1. Areson Protocol Probe (Feature 0x08, Subcmd 0x04)
            if (vid == 0x25A7 || vid == 0x0000)
            {
                byte[] aresonCmd = new byte[17];
                aresonCmd[0] = 0x08; // Feature Report ID
                aresonCmd[1] = 0x04; // CMD_QUERY_STATUS
                byte sum = 0;
                for (int i = 0; i < 16; i++) sum += aresonCmd[i];
                aresonCmd[16] = (byte)(0x55 - sum);

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
                                    if (HasNonZeroData(resp))
                                    {
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.WriteLine("         [Areson Response on 0x{0:X4}:0x{1:X4}]: {2}",
                                            inIface.UsagePage, inIface.Usage, FormatHex(resp, 24));
                                        Console.ResetColor();
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // 2. ROYUAN / YiChip Keyboard Protocol Probe (Feature 0x83 GET_BATTERY, 0x80 GET_REV, 0x8F GET_INFOR)
            if (vid == 0x25A7 || vid == 0x3151 || vid == 0x0461 || vid == 0x0000)
            {
                foreach (var iface in interfaces)
                {
                    if (iface.FeatureReportByteLength >= 65)
                    {
                        byte[] cmdList = new byte[] { 0x83, 0x80, 0x81, 0x82, 0x8F };
                        foreach (byte cmd in cmdList)
                        {
                            // Format A: Unnumbered 65-byte Feature Exchange [0x00, cmd, 0x00, ...]
                            byte[] unnumSend = new byte[iface.FeatureReportByteLength];
                            unnumSend[0] = 0x00;
                            unnumSend[1] = cmd;
                            bool setOkA = transport.SetFeatureReport(iface.DevicePath, unnumSend);
                            Thread.Sleep(20);

                            byte[] unnumResp = new byte[iface.FeatureReportByteLength];
                            unnumResp[0] = 0x00;
                            bool getOkA = transport.GetFeatureReport(iface.DevicePath, 0x00, unnumResp);

                            if (getOkA && HasNonZeroData(unnumResp))
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine("         [ROYUAN Probe Unnumbered 0x{0:X2}] SetFeature: {1}, GetFeature: OK -> {2}",
                                    cmd, setOkA ? "OK" : "Failed", FormatHex(unnumResp, 24));
                                Console.ResetColor();
                            }

                            // Format B: Numbered [cmd, 0x00, ...]
                            byte[] numSend = new byte[iface.FeatureReportByteLength];
                            numSend[0] = cmd;
                            bool setOkB = transport.SetFeatureReport(iface.DevicePath, numSend);
                            Thread.Sleep(20);

                            byte[] numResp = new byte[iface.FeatureReportByteLength];
                            numResp[0] = cmd;
                            bool getOkB = transport.GetFeatureReport(iface.DevicePath, cmd, numResp);

                            if (getOkB && HasNonZeroData(numResp))
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine("         [ROYUAN Probe Numbered 0x{0:X2}] SetFeature: {1}, GetFeature: OK -> {2}",
                                    cmd, setOkB ? "OK" : "Failed", FormatHex(numResp, 24));
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
                        if (HasNonZeroData(vIn))
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("         [Vendor 0x{0:X4}:0x{1:X4} Spontaneous Packet]: {2}",
                                iface.UsagePage, iface.Usage, FormatHex(vIn, 16));
                            Console.ResetColor();
                        }
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Command Handlers: Battery Protocol Hunter & Calculator
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Probes Feature Reports and vendor commands to discover and calculate battery telemetry bytes.
        /// </summary>
        /// <param name="filter">Optional device filter.</param>
        static void RunBatteryHunter(string filter = null)
        {
            PrintBanner();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("============================================================================");
            Console.WriteLine("       OmniHID Battery Protocol Hunter & Report Calculation Engine          ");
            Console.WriteLine("============================================================================");
            Console.ResetColor();
            Console.WriteLine();

            using (var transport = new Win32HidTransport())
            {
                string devName;
                ushort vid;
                ushort pid;
                List<HidDeviceInfo> interfaces;

                if (!SelectTargetDevice(transport, filter, out devName, out vid, out pid, out interfaces))
                {
                    return;
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Target Device: {0} (VID: 0x{1:X4}, PID: 0x{2:X4})", devName, vid, pid);
                Console.WriteLine("Total Endpoint Collections: {0}", interfaces.Count);
                Console.ResetColor();
                Console.WriteLine("----------------------------------------------------------------------------");

                // Check Windows PnP cache first
                foreach (var iface in interfaces)
                {
                    int pnpBatt = transport.GetPnpBatteryLevel(iface.DevicePath);
                    if (pnpBatt >= 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("  [+] Windows PnP DEVPKEY_Device_BatteryLevel: {0}% (Endpoint: 0x{1:X4}:0x{2:X4})",
                            pnpBatt, iface.UsagePage, iface.Usage);
                        Console.ResetColor();
                        break;
                    }
                }

                int candidateCount = 0;
                int reportsReceived = 0;
                List<BatteryCandidate> allCandidates = new List<BatteryCandidate>();

                // ── Phase 1: Feature Report Sweep (0x00 .. 0xFF) ─────────────
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n[Phase 1/3] Sweeping Feature Reports (IDs 0x00 .. 0xFF) across endpoints...");
                Console.ResetColor();

                int ifIdx = 0;
                foreach (var iface in interfaces)
                {
                    ifIdx++;
                    int bufLen = Math.Max(64, iface.FeatureReportByteLength > 0 ? (int)iface.FeatureReportByteLength : 64);

                    for (int reportId = 0; reportId <= 255; reportId++)
                    {
                        byte[] featBuf = new byte[bufLen];
                        if (transport.GetFeatureReport(iface.DevicePath, (byte)reportId, featBuf))
                        {
                            if (HasNonZeroData(featBuf))
                            {
                                reportsReceived++;
                                string label = string.Format("Feature Report 0x{0:X2} (EP #{1} 0x{2:X4}:0x{3:X4})",
                                    reportId, ifIdx, iface.UsagePage, iface.Usage);
                                bool isVendorEp = iface.UsagePage >= 0xFF00;
                                InspectPotentialBatteryBytes(featBuf, label, isVendorEp, ref candidateCount, allCandidates);
                            }
                        }
                    }
                }

                // ── Phase 2: Input Report Probe via GetInputReport ───────────
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n[Phase 2/3] Probing Input Reports via Control Transfer (IDs 0x00 .. 0x20)...");
                Console.ResetColor();

                ifIdx = 0;
                foreach (var iface in interfaces)
                {
                    ifIdx++;
                    int inLen = Math.Max(64, iface.InputReportByteLength > 0 ? (int)iface.InputReportByteLength : 64);

                    for (byte inId = 0; inId <= 0x20; inId++)
                    {
                        byte[] inBuf = new byte[inLen];
                        if (transport.GetInputReport(iface.DevicePath, inId, inBuf))
                        {
                            if (HasNonZeroData(inBuf))
                            {
                                reportsReceived++;
                                string label = string.Format("Input Report 0x{0:X2} (EP #{1} 0x{2:X4}:0x{3:X4})",
                                    inId, ifIdx, iface.UsagePage, iface.Usage);
                                bool isVendorEp = iface.UsagePage >= 0xFF00;
                                InspectPotentialBatteryBytes(inBuf, label, isVendorEp, ref candidateCount, allCandidates);
                            }
                        }
                    }
                }

                // ── Phase 3: Known Vendor Query Probes ────────────────────────
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\n[Phase 3/3] Testing Known Vendor Battery Query Sequences...");
                Console.ResetColor();

                // Test queries (Areson, CompX, Generic query payloads)
                List<byte[]> testQueries = new List<byte[]>
                {
                    new byte[] { 0x08, 0x04, 0x00, 0x00 },
                    new byte[] { 0x06, 0x00, 0x00, 0x00 },
                    new byte[] { 0x06, 0x01, 0x00, 0x00 },
                    new byte[] { 0x04, 0x00, 0x00, 0x00 },
                    new byte[] { 0x04, 0x02, 0x00, 0x00 },
                    new byte[] { 0x02, 0x00, 0x00, 0x00 }
                };

                ifIdx = 0;
                foreach (var iface in interfaces)
                {
                    ifIdx++;
                    if (iface.FeatureReportByteLength >= 4 || iface.UsagePage >= 0xFF00)
                    {
                        foreach (var q in testQueries)
                        {
                            try
                            {
                                byte[] sendBuf = new byte[Math.Max((int)iface.FeatureReportByteLength, q.Length)];
                                Array.Copy(q, sendBuf, q.Length);

                                if (transport.SetFeatureReport(iface.DevicePath, sendBuf))
                                {
                                    Thread.Sleep(20);
                                    byte[] resp = new byte[sendBuf.Length];
                                    if (transport.GetFeatureReport(iface.DevicePath, q[0], resp) && HasNonZeroData(resp))
                                    {
                                        reportsReceived++;
                                        string label = string.Format("Query [0x{0:X2} 0x{1:X2}] -> Resp on EP #{2}", q[0], q[1], ifIdx);
                                        bool isVendorEp = iface.UsagePage >= 0xFF00;
                                        InspectPotentialBatteryBytes(resp, label, isVendorEp, ref candidateCount, allCandidates);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }

                // ── Summary Results ──────────────────────────────────────────
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("============================================================================");
                Console.WriteLine(" [HUNTER SUMMARY] Probed {0} active reports across {1} endpoint collection(s).", reportsReceived, interfaces.Count);

                if (allCandidates.Count > 0)
                {
                    // Sort by score descending
                    allCandidates.Sort((a, b) => b.Score.CompareTo(a.Score));

                    int highPriority = 0;
                    foreach (var c in allCandidates) { if (c.Score >= 3) highPriority++; }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine(" [RESULTS] {0} total candidate(s), {1} high-priority:", allCandidates.Count, highPriority);
                    Console.ResetColor();
                    Console.WriteLine();

                    // Show Top-5 ranked candidates
                    int showCount = Math.Min(5, allCandidates.Count);
                    for (int i = 0; i < showCount; i++)
                    {
                        var c = allCandidates[i];
                        if (c.Score >= 3)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.Write("   #{0} ★ ", i + 1);
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write("   #{0}   ", i + 1);
                        }
                        Console.Write("[Score: {0}] ", c.Score);
                        Console.ResetColor();

                        if (c.Score >= 3)
                            Console.ForegroundColor = ConsoleColor.White;
                        else
                            Console.ForegroundColor = ConsoleColor.DarkGray;

                        Console.WriteLine("{0}", c.Description);
                        Console.ResetColor();
                    }

                    if (allCandidates.Count > showCount)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine("   ... and {0} more low-priority candidate(s) shown above in gray.", allCandidates.Count - showCount);
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine(" [NOTE] No static battery bytes detected via polled queries.");
                    Console.WriteLine(" Your device likely sends spontaneous Input Reports on button click or movement.");
                    Console.WriteLine(" Suggestion: Run option [5] Live Sniffer & Monitor to observe real-time diffs!");
                }

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("============================================================================");
                Console.ResetColor();
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Candidate battery telemetry finding with weighted priority score.
        /// </summary>
        private struct BatteryCandidate
        {
            public int Score;
            public string Description;
        }

        /// <summary>
        /// Analyzes a payload buffer for plausible battery percentage (5..100) or battery voltage (3000..4350 mV).
        /// Uses a weighted scoring system: vendor endpoints, adjacent charging flags, and non-ASCII values score higher.
        /// Low-priority candidates are shown in gray, high-priority in green.
        /// </summary>
        private static void InspectPotentialBatteryBytes(byte[] buffer, string sourceLabel, bool isVendorEndpoint, ref int candidateCount, List<BatteryCandidate> candidates)
        {
            if (buffer == null || buffer.Length < 2) return;

            bool foundCandidate = false;
            int bestCandidateOffset = -1;

            // 1. Percentage check (5% .. 100%)
            for (int i = 1; i < buffer.Length; i++)
            {
                byte val = buffer[i];
                if (val >= 5 && val <= 100)
                {
                    foundCandidate = true;
                    candidateCount++;
                    if (bestCandidateOffset < 0) bestCandidateOffset = i;

                    // Score calculation
                    int score = 1;
                    string hints = "";

                    // Higher score for vendor endpoints
                    if (isVendorEndpoint) score += 2;

                    // Higher score for values outside printable ASCII range
                    if (val < 0x20 || val > 0x7E) score += 1;

                    // Adjacent charging/discharging flag boosts score
                    if (i + 1 < buffer.Length)
                    {
                        byte next = buffer[i + 1];
                        if (next == 0x00)
                        {
                            score += 2;
                            hints += string.Format(" [Byte[{0}]=0x00 (Discharging flag?)]", i + 1);
                        }
                        else if (next == 0x01 || next == 0x02)
                        {
                            score += 2;
                            hints += string.Format(" [Byte[{0}]=0x{1:X2} (Charging flag?)]", i + 1, next);
                        }
                    }

                    // Common battery report positions boost
                    if (i <= 4) score += 1;

                    string desc = string.Format("{0} -> Byte[{1}] = {2} (0x{2:X2}) => Possible Level: {2}%{3}",
                        sourceLabel, i, val, hints);

                    candidates.Add(new BatteryCandidate { Score = score, Description = desc });

                    // Display: high-priority = green, low-priority = gray
                    if (score >= 3)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine(string.Format("    ⚡ [BATTERY % | Score:{0}] {1}", score, desc));
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine(string.Format("    ·  [BATTERY % | Score:{0}] {1}", score, desc));
                    }
                    Console.ResetColor();
                }
            }

            // 2. Voltage check (3000 .. 4350 mV Li-ion battery range)
            for (int i = 1; i < buffer.Length - 1; i++)
            {
                // Little-Endian word
                ushort mvLe = (ushort)(buffer[i] | (buffer[i + 1] << 8));
                if (mvLe >= 3000 && mvLe <= 4350)
                {
                    foundCandidate = true;
                    candidateCount++;
                    int approxPct = Math.Max(0, Math.Min(100, (int)((mvLe - 3400) * 100 / 800)));

                    int score = 2;
                    if (isVendorEndpoint) score += 2;
                    if (i <= 4) score += 1;

                    string desc = string.Format("{0} -> Bytes[{1}..{2}] = {3} mV (LE) => Estimated: ~{4}%",
                        sourceLabel, i, i + 1, mvLe, approxPct);

                    candidates.Add(new BatteryCandidate { Score = score, Description = desc });

                    if (score >= 3)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine(string.Format("    🔋 [VOLTAGE LE | Score:{0}] {1}", score, desc));
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine(string.Format("    ·  [VOLTAGE LE | Score:{0}] {1}", score, desc));
                    }
                    Console.ResetColor();
                }

                // Big-Endian word
                ushort mvBe = (ushort)((buffer[i] << 8) | buffer[i + 1]);
                if (mvBe >= 3000 && mvBe <= 4350)
                {
                    foundCandidate = true;
                    candidateCount++;
                    int approxPct = Math.Max(0, Math.Min(100, (int)((mvBe - 3400) * 100 / 800)));

                    int score = 2;
                    if (isVendorEndpoint) score += 2;
                    if (i <= 4) score += 1;

                    string desc = string.Format("{0} -> Bytes[{1}..{2}] = {3} mV (BE) => Estimated: ~{4}%",
                        sourceLabel, i, i + 1, mvBe, approxPct);

                    candidates.Add(new BatteryCandidate { Score = score, Description = desc });

                    if (score >= 3)
                    {
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine(string.Format("    🔋 [VOLTAGE BE | Score:{0}] {1}", score, desc));
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine(string.Format("    ·  [VOLTAGE BE | Score:{0}] {1}", score, desc));
                    }
                    Console.ResetColor();
                }
            }

            if (foundCandidate)
            {
                HexView.PrintHexDump(buffer, 16, -1, bestCandidateOffset);
                Console.WriteLine();
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Command Handlers: Live Packet Sniffer & Real-Time Diff
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Captures live raw HID packets with real-time byte diff highlighting and exports dump log.
        /// </summary>
        /// <param name="filter">Optional device filter.</param>
        static void RunSniff(string filter = null)
        {
            PrintBanner();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("============================================================================");
            Console.WriteLine("     OmniHID Live Packet Sniffer & Real-Time Diff Telemetry Recorder        ");
            Console.WriteLine("============================================================================");
            Console.ResetColor();
            Console.WriteLine();

            using (var transport = new Win32HidTransport())
            {
                string devName;
                ushort vid;
                ushort pid;
                List<HidDeviceInfo> targetInterfaces;
                DeviceProfile profile;
                HashSet<ushort> targetPids;

                if (!SelectTargetDevice(transport, filter, out devName, out vid, out pid, out targetInterfaces, out profile, out targetPids))
                {
                    return;
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string dumpFileName = string.Format("omni_hid_dump_{0:x4}_{1:x4}_{2}.txt", vid, pid, timestamp);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Target Device: {0} (VID: 0x{1:X4}, PID: 0x{2:X4})", devName, vid, pid);
                Console.WriteLine("Total Endpoints: {0}", targetInterfaces.Count);
                if (targetPids.Count > 1)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("Dual-Mode PIDs Monitored: {0}", string.Join(", ", new List<ushort>(targetPids).ConvertAll(p => "0x" + p.ToString("X4")).ToArray()));
                    Console.ForegroundColor = ConsoleColor.Green;
                }
                Console.WriteLine("Export Log File: {0}", dumpFileName);
                Console.ResetColor();
                Console.WriteLine();

                using (StreamWriter dumpWriter = new StreamWriter(dumpFileName, false, Encoding.UTF8))
                {
                    dumpWriter.WriteLine("═══════════════════════════════════════════════════════════════════════════");
                    dumpWriter.WriteLine(" OmniHID Hardware Telemetry & Live Packet Dump");
                    dumpWriter.WriteLine(" Device: {0} (VID: 0x{1:X4}, PID: 0x{2:X4})", devName, vid, pid);
                    dumpWriter.WriteLine(" Captured: {0:yyyy-MM-dd HH:mm:ss}", DateTime.Now);
                    if (_sniffTimeoutSeconds > 0)
                        dumpWriter.WriteLine(" Timeout: {0} seconds", _sniffTimeoutSeconds);
                    else
                        dumpWriter.WriteLine(" Timeout: Unlimited (manual stop)");
                    dumpWriter.WriteLine("═══════════════════════════════════════════════════════════════════════════\n");

                    // Section 1: Interface breakdown in dump file
                    dumpWriter.WriteLine("── 1. Enumerated Interface Endpoints ({0} total) ──", targetInterfaces.Count);
                    for (int i = 0; i < targetInterfaces.Count; i++)
                    {
                        var iface = targetInterfaces[i];
                        dumpWriter.WriteLine("Interface #{0}: Usage 0x{1:X4}:0x{2:X4} ({3})",
                            i + 1, iface.UsagePage, iface.Usage, FormatUsage(iface.UsagePage, iface.Usage));
                        dumpWriter.WriteLine("  Mfr: \"{0}\" | Prod: \"{1}\"", iface.ManufacturerString ?? "", iface.ProductString ?? "");
                        dumpWriter.WriteLine("  ReportLen: Input={0}B, Output={1}B, Feature={2}B",
                            iface.InputReportByteLength, iface.OutputReportByteLength, iface.FeatureReportByteLength);
                        dumpWriter.WriteLine("  Path: {0}", iface.DevicePath);
                        dumpWriter.WriteLine();
                    }

                    // Section 2: Protocol context and PnP battery snapshot
                    dumpWriter.WriteLine("── 2. Protocol Context & PnP Snapshot ──");
                    foreach (var iface in targetInterfaces)
                    {
                        int pnpBatt = transport.GetPnpBatteryLevel(iface.DevicePath);
                        if (pnpBatt >= 0)
                        {
                            dumpWriter.WriteLine("  PnP Battery Level: {0}% (EP 0x{1:X4}:0x{2:X4})", pnpBatt, iface.UsagePage, iface.Usage);
                        }
                    }
                    dumpWriter.WriteLine();

                    // Section 3: Baseline feature report snapshots
                    dumpWriter.WriteLine("── 3. Baseline Feature Reports (pre-capture snapshot) ──");
                    byte[] baselineCandidates = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x10, 0x20, 0x80, 0x81, 0x82, 0x83, 0x8F };
                    for (int i = 0; i < targetInterfaces.Count; i++)
                    {
                        var iface = targetInterfaces[i];
                        int bufLen = Math.Max(64, iface.FeatureReportByteLength > 0 ? (int)iface.FeatureReportByteLength : 64);
                        foreach (byte fId in baselineCandidates)
                        {
                            byte[] feat = new byte[bufLen];
                            if (transport.GetFeatureReport(iface.DevicePath, fId, feat) && HasNonZeroData(feat))
                            {
                                dumpWriter.WriteLine("  EP #{0} (0x{1:X4}:0x{2:X4}) Feature 0x{3:X2}: {4}",
                                    i + 1, iface.UsagePage, iface.Usage, fId, FormatHex(feat, 32));
                            }
                        }
                    }
                    dumpWriter.WriteLine();

                    // Section 4: Live capture header
                    dumpWriter.WriteLine("── 4. Live Capture ──");

                    // Determine timeout mode for console display
                    string timeoutDisplay;
                    if (_sniffTimeoutSeconds > 0)
                        timeoutDisplay = string.Format(" Auto-stop in {0} seconds (or press Enter/Escape to finish early).", _sniffTimeoutSeconds);
                    else
                        timeoutDisplay = " Press Enter or Escape to stop capture.";

                    // Live Sniffer setup
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("============================================================================");
                    Console.WriteLine(" LIVE PACKET SNIFFER ACTIVE (REAL-TIME DIFF HIGHLIGHTING)");
                    Console.WriteLine(" Target: {0}", devName);
                    Console.WriteLine(" ACTION: Interact with device or plug in / unplug charging cable.");
                    Console.WriteLine(" Changed bytes will be highlighted in dark yellow/red.");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(" TIP: Active telemetry pulses are sent every ~2.5s. Press 'P' for manual pulse.");
                    Console.WriteLine("      Plug/unplug the USB cable to test real-time charging diffs across PIDs.");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(timeoutDisplay);
                    Console.WriteLine("============================================================================");
                    Console.ResetColor();
                    Console.WriteLine();

                    List<ActiveSnifferReader> readers = new List<ActiveSnifferReader>();
                    List<WaitHandle> waitHandles = new List<WaitHandle>();

                    for (int i = 0; i < targetInterfaces.Count; i++)
                    {
                        var iface = targetInterfaces[i];
                        // Exclude locked standard keyboard endpoint
                        if (iface.UsagePage == 0x0001 && iface.Usage == 0x0006)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine("  [~] Endpoint #{0} (0x0001:0x0006 - Keyboard): Skipped (exclusive OS keyboard driver).", i + 1);
                            Console.ResetColor();
                            continue;
                        }

                        SafeFileHandle handle = Win32HidTransport.OpenDevice(
                            iface.DevicePath,
                            Win32HidNative.GENERIC_READ | Win32HidNative.GENERIC_WRITE,
                            true);

                        if (handle.IsInvalid)
                        {
                            handle = Win32HidTransport.OpenDevice(iface.DevicePath, Win32HidNative.GENERIC_READ, true);
                        }

                        if (!handle.IsInvalid)
                        {
                            var reader = new ActiveSnifferReader(iface, handle, i + 1);
                            if (reader.StartRead())
                            {
                                readers.Add(reader);
                                waitHandles.Add(reader.WaitEvent);
                                Console.ForegroundColor = ConsoleColor.DarkGreen;
                                Console.WriteLine("  [+] Endpoint #{0} (0x{1:X4}:0x{2:X4} - {3}): Listening for live packets.",
                                    i + 1, iface.UsagePage, iface.Usage, FormatUsage(iface.UsagePage, iface.Usage));
                                Console.ResetColor();
                            }
                            else
                            {
                                reader.Dispose();
                            }
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            if (iface.UsagePage == 0x0001 && iface.Usage == 0x0002)
                            {
                                Console.WriteLine("  [~] Endpoint #{0} (0x0001:0x0002 - Mouse Cursor): Locked by Windows mouclass (cursor motion cannot be tapped via ReadFile).", i + 1);
                            }
                            else
                            {
                                Console.WriteLine("  [!] Endpoint #{0} (0x{1:X4}:0x{2:X4}): Access denied / locked by OS.", i + 1, iface.UsagePage, iface.Usage);
                            }
                            Console.ResetColor();
                        }
                    }

                    int packetsCaptured = 0;
                    DateTime captureStart = DateTime.UtcNow;

                    // Per-endpoint packet counters and diff position tracking
                    Dictionary<int, int> epPacketCounts = new Dictionary<int, int>();
                    Dictionary<int, HashSet<int>> epDiffPositions = new Dictionary<int, HashSet<int>>();
                    foreach (var r in readers)
                    {
                        epPacketCounts[r.InterfaceIndex] = 0;
                        epDiffPositions[r.InterfaceIndex] = new HashSet<int>();
                    }

                    DateTime lastStatsTime = DateTime.UtcNow;
                    DateTime lastHotplugCheck = DateTime.UtcNow;
                    DateTime lastProbePulse = DateTime.UtcNow;

                    try
                    {
                        while (readers.Count > 0)
                        {
                            // Check timeout if configured
                            if (_sniffTimeoutSeconds > 0 && (DateTime.UtcNow - captureStart).TotalSeconds >= _sniffTimeoutSeconds)
                                break;

                            bool hasKey = false;
                            try { hasKey = !Console.IsInputRedirected && Console.KeyAvailable; } catch { }
                            if (hasKey)
                            {
                                ConsoleKeyInfo keyInfo = default(ConsoleKeyInfo);
                                try { keyInfo = Console.ReadKey(true); } catch { }
                                if (keyInfo.Key == ConsoleKey.P)
                                {
                                    Console.ForegroundColor = ConsoleColor.Yellow;
                                    Console.WriteLine("\n  ⚡ [MANUAL PULSE] Sent telemetry query pulse to peripheral...");
                                    Console.ResetColor();
                                    SendSnifferTelemetryPulse(transport, readers, vid, profile);
                                    continue;
                                }
                                break;
                            }

                            // Dynamic hotplug detection: check every 1500ms for connection changes (e.g. cable connected/disconnected)
                            if ((DateTime.UtcNow - lastHotplugCheck).TotalMilliseconds >= 1500)
                            {
                                lastHotplugCheck = DateTime.UtcNow;
                                var liveInterfaces = ReEnumerateTargetInterfaces(transport, vid, targetPids);
                                foreach (var liveIface in liveInterfaces)
                                {
                                    if (liveIface.UsagePage == 0x0001 && liveIface.Usage == 0x0006) continue;
                                    bool alreadyTracked = false;
                                    foreach (var r in readers)
                                    {
                                        if (string.Equals(r.Interface.DevicePath, liveIface.DevicePath, StringComparison.OrdinalIgnoreCase))
                                        {
                                            alreadyTracked = true;
                                            break;
                                        }
                                    }

                                    if (!alreadyTracked)
                                    {
                                        SafeFileHandle h = Win32HidTransport.OpenDevice(liveIface.DevicePath, Win32HidNative.GENERIC_READ | Win32HidNative.GENERIC_WRITE, true);
                                        if (h.IsInvalid) h = Win32HidTransport.OpenDevice(liveIface.DevicePath, Win32HidNative.GENERIC_READ, true);
                                        if (!h.IsInvalid)
                                        {
                                            int newIdx = readers.Count + 1;
                                            var newReader = new ActiveSnifferReader(liveIface, h, newIdx);
                                            if (newReader.StartRead())
                                            {
                                                readers.Add(newReader);
                                                waitHandles.Add(newReader.WaitEvent);
                                                epPacketCounts[newIdx] = 0;
                                                epDiffPositions[newIdx] = new HashSet<int>();

                                                Console.ForegroundColor = ConsoleColor.Cyan;
                                                Console.WriteLine("\n  ⚡ [HOTPLUG] Connection switch detected! Added PID 0x{0:X4} (0x{1:X4}:0x{2:X4}) to live sniffer.",
                                                    liveIface.ProductId, liveIface.UsagePage, liveIface.Usage);
                                                Console.ResetColor();
                                            }
                                            else
                                            {
                                                newReader.Dispose();
                                            }
                                        }
                                    }
                                }
                            }

                            // Active telemetry pulse: periodically trigger response packets from command-driven peripherals every 2.5s
                            if ((DateTime.UtcNow - lastProbePulse).TotalMilliseconds >= 2500)
                            {
                                lastProbePulse = DateTime.UtcNow;
                                SendSnifferTelemetryPulse(transport, readers, vid, profile);
                            }

                            int signaledIdx = WaitHandle.WaitAny(waitHandles.ToArray(), 200);
                            if (signaledIdx != WaitHandle.WaitTimeout && signaledIdx >= 0 && signaledIdx < readers.Count)
                            {
                                var reader = readers[signaledIdx];
                                uint bytesTransferred;
                                if (reader.CompleteRead(out bytesTransferred))
                                {
                                    packetsCaptured++;
                                    epPacketCounts[reader.InterfaceIndex] = epPacketCounts[reader.InterfaceIndex] + 1;
                                    string timeStr = DateTime.Now.ToString("HH:mm:ss.fff");
                                    byte repId = reader.Buffer.Length > 0 ? reader.Buffer[0] : (byte)0;

                                    // Compute byte diff compared to previous packet on this endpoint
                                    int diffOffset = -1;
                                    byte[] prev = reader.LastBuffer;
                                    if (prev != null && prev.Length == reader.Buffer.Length)
                                    {
                                        for (int b = 0; b < bytesTransferred; b++)
                                        {
                                            if (reader.Buffer[b] != prev[b])
                                            {
                                                if (diffOffset < 0) diffOffset = b;
                                                epDiffPositions[reader.InterfaceIndex].Add(b);
                                            }
                                        }
                                    }

                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine("[{0}] [EP #{1} 0x{2:X4}:0x{3:X4}] [Len {4}B] RepID: 0x{5:X2}",
                                        timeStr, reader.InterfaceIndex, reader.Interface.UsagePage, reader.Interface.Usage,
                                        bytesTransferred, repId);
                                    Console.ResetColor();

                                    byte[] printSlice = new byte[bytesTransferred];
                                    Array.Copy(reader.Buffer, printSlice, bytesTransferred);
                                    HexView.PrintHexDump(printSlice, 16, diffOffset);

                                    // Check if changed byte is a candidate battery value
                                    if (diffOffset > 0 && diffOffset < bytesTransferred)
                                    {
                                        byte chgVal = reader.Buffer[diffOffset];
                                        if (chgVal >= 5 && chgVal <= 100)
                                        {
                                            Console.ForegroundColor = ConsoleColor.Yellow;
                                            Console.WriteLine("    ⚡ [LIVE BATTERY HINT] Changed Byte[{0}] = {1} (0x{1:X2}) => Possible Level: {1}%",
                                                diffOffset, chgVal);
                                            Console.ResetColor();
                                        }
                                    }

                                    dumpWriter.WriteLine("[{0}] [EP #{1} 0x{2:X4}:0x{3:X4}] [Len {4}] RepID: 0x{5:X2} | {6}",
                                        timeStr, reader.InterfaceIndex, reader.Interface.UsagePage, reader.Interface.Usage,
                                        bytesTransferred, repId, FormatHex(reader.Buffer, (int)bytesTransferred));

                                    // Store last buffer for diff calculation
                                    reader.LastBuffer = (byte[])reader.Buffer.Clone();

                                    // Prime next read
                                    reader.StartRead();
                                }
                                else
                                {
                                    // Handle disconnection / read error cleanly
                                    int err = Marshal.GetLastWin32Error();
                                    if (err == 1167 || err == 31 || err == 2)
                                    {
                                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                                        Console.WriteLine("\n  [-] Endpoint #{0} (PID 0x{1:X4}) disconnected (PnP removal).",
                                            reader.InterfaceIndex, reader.Interface.ProductId);
                                        Console.ResetColor();
                                        waitHandles.RemoveAt(signaledIdx);
                                        readers.RemoveAt(signaledIdx);
                                        reader.Dispose();
                                    }
                                    else
                                    {
                                        if (!reader.StartRead())
                                        {
                                            waitHandles.RemoveAt(signaledIdx);
                                            readers.RemoveAt(signaledIdx);
                                            reader.Dispose();
                                        }
                                    }
                                }
                            }

                            // Live statistics line every 5 seconds
                            if ((DateTime.UtcNow - lastStatsTime).TotalSeconds >= 5.0 && packetsCaptured > 0)
                            {
                                lastStatsTime = DateTime.UtcNow;
                                double elapsed = (DateTime.UtcNow - captureStart).TotalSeconds;
                                var statParts = new StringBuilder();
                                statParts.AppendFormat("── [Stats] {0} pkts | {1:F1}s", packetsCaptured, elapsed);
                                foreach (var kvp in epPacketCounts)
                                {
                                    if (kvp.Value > 0)
                                    {
                                        double pps = kvp.Value / elapsed;
                                        statParts.AppendFormat(" | EP#{0}: {1:F1} pps", kvp.Key, pps);
                                    }
                                }
                                int totalDiffPos = 0;
                                foreach (var kvp in epDiffPositions) totalDiffPos += kvp.Value.Count;
                                statParts.AppendFormat(" | Diffs: {0} pos ──", totalDiffPos);

                                Console.ForegroundColor = ConsoleColor.DarkCyan;
                                Console.WriteLine(statParts.ToString());
                                Console.ResetColor();
                            }
                        }
                    }
                    finally
                    {
                        foreach (var r in readers) r.Dispose();
                    }

                    double totalElapsed = (DateTime.UtcNow - captureStart).TotalSeconds;

                    // Section 5: Capture statistics in dump file
                    dumpWriter.WriteLine();
                    dumpWriter.WriteLine("── 5. Capture Statistics ──");
                    dumpWriter.WriteLine("Total packets: {0}", packetsCaptured);
                    dumpWriter.WriteLine("Capture duration: {0:F1} seconds", totalElapsed);
                    foreach (var kvp in epPacketCounts)
                    {
                        double pps = totalElapsed > 0 ? kvp.Value / totalElapsed : 0;
                        var diffPos = epDiffPositions.ContainsKey(kvp.Key) ? epDiffPositions[kvp.Key] : new HashSet<int>();
                        string diffStr = diffPos.Count > 0 ? FormatDiffPositions(diffPos) : "none";
                        dumpWriter.WriteLine("  EP #{0}: {1} pkts (avg {2:F1} pps) — Diff positions: [{3}]",
                            kvp.Key, kvp.Value, pps, diffStr);
                    }
                    dumpWriter.Flush();

                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("============================================================================");
                    Console.WriteLine(" [SNIFFER COMPLETE] Captured {0} raw packet(s) in {1:F1}s.", packetsCaptured, totalElapsed);
                    Console.WriteLine(" Dump file saved: {0}", Path.GetFullPath(dumpFileName));

                    // Print per-endpoint summary to console
                    foreach (var kvp in epPacketCounts)
                    {
                        if (kvp.Value > 0)
                        {
                            double pps = totalElapsed > 0 ? kvp.Value / totalElapsed : 0;
                            var diffPos = epDiffPositions.ContainsKey(kvp.Key) ? epDiffPositions[kvp.Key] : new HashSet<int>();
                            Console.WriteLine("   EP #{0}: {1} pkts ({2:F1} pps), {3} diff position(s)",
                                kvp.Key, kvp.Value, pps, diffPos.Count);
                        }
                    }

                    Console.WriteLine("============================================================================");
                    Console.ResetColor();
                    Console.WriteLine();
                }
            }
        }

        /// <summary>
        /// Sends active protocol query pulses across candidate vendor feature/output endpoints during live sniffing.
        /// Elicits spontaneous telemetry response frames from command-driven peripherals (e.g. Areson, CompX, Royuan, SinoWealth).
        /// </summary>
        private static void SendSnifferTelemetryPulse(
            Win32HidTransport transport,
            List<ActiveSnifferReader> readers,
            ushort vid,
            DeviceProfile profile)
        {
            if (readers == null || readers.Count == 0) return;

            // 1. Areson Protocol Pulse (VID 0x25A7 or protocol "areson")
            if (vid == 0x25A7 || (profile != null && string.Equals(profile.ProtocolId, "areson", StringComparison.OrdinalIgnoreCase)))
            {
                byte[] aresonCmd = new byte[17];
                aresonCmd[0] = 0x08; // Feature Report ID
                aresonCmd[1] = 0x04; // CMD_QUERY_STATUS
                byte sum = 0;
                for (int b = 0; b < 16; b++) sum += aresonCmd[b];
                aresonCmd[16] = (byte)(0x55 - sum);

                foreach (var r in readers)
                {
                    var iface = r.Interface;
                    if (iface.FeatureReportByteLength >= 17 || iface.UsagePage >= 0xFF00 || iface.UsagePage == 0xFF02)
                    {
                        byte[] sendBuf = aresonCmd;
                        if (iface.FeatureReportByteLength > 17)
                        {
                            sendBuf = new byte[iface.FeatureReportByteLength];
                            Array.Copy(aresonCmd, sendBuf, aresonCmd.Length);
                        }

                        if (!transport.SetFeatureReport(iface.DevicePath, sendBuf))
                        {
                            transport.WriteOutputReport(iface.DevicePath, sendBuf);
                        }

                        if (iface.FeatureReportByteLength >= 65)
                        {
                            byte[] unnumbered = new byte[iface.FeatureReportByteLength];
                            unnumbered[0] = 0x00;
                            Array.Copy(aresonCmd, 0, unnumbered, 1, Math.Min(aresonCmd.Length, unnumbered.Length - 1));
                            if (!transport.SetFeatureReport(iface.DevicePath, unnumbered))
                            {
                                transport.WriteOutputReport(iface.DevicePath, unnumbered);
                            }
                        }
                    }
                }
            }

            // 2. CompX / PixArt Pulse (VID 0x24AE or protocol "compx")
            if (vid == 0x24AE || (profile != null && string.Equals(profile.ProtocolId, "compx", StringComparison.OrdinalIgnoreCase)))
            {
                byte[] compxCmd = new byte[] { 0x06, 0x00, 0x00, 0x00 };
                foreach (var r in readers)
                {
                    if (r.Interface.FeatureReportByteLength >= 4 || r.Interface.UsagePage >= 0xFF00)
                    {
                        byte[] sendBuf = new byte[Math.Max((int)r.Interface.FeatureReportByteLength, compxCmd.Length)];
                        Array.Copy(compxCmd, sendBuf, compxCmd.Length);
                        transport.SetFeatureReport(r.Interface.DevicePath, sendBuf);
                    }
                }
            }

            // 3. ROYUAN / YiChip Pulse (VID 0x3151, 0x0461, etc.)
            if (vid == 0x3151 || vid == 0x0461 || (profile != null && string.Equals(profile.ProtocolId, "royuan-keyboard", StringComparison.OrdinalIgnoreCase)))
            {
                byte[] royuanCmd = new byte[65];
                royuanCmd[0] = 0x00;
                royuanCmd[1] = 0x83; // GET_BATTERY
                foreach (var r in readers)
                {
                    if (r.Interface.FeatureReportByteLength >= 65)
                    {
                        transport.SetFeatureReport(r.Interface.DevicePath, royuanCmd);
                    }
                }
            }

            // 4. SinoWealth Pulse (VID 0x258A or protocol "sinowealth")
            if (vid == 0x258A || (profile != null && string.Equals(profile.ProtocolId, "sinowealth", StringComparison.OrdinalIgnoreCase)))
            {
                byte[] sinoCmd = new byte[] { 0x04, 0x00, 0x00, 0x00 };
                foreach (var r in readers)
                {
                    if (r.Interface.FeatureReportByteLength >= 4 || r.Interface.UsagePage >= 0xFF00)
                    {
                        byte[] sendBuf = new byte[Math.Max((int)r.Interface.FeatureReportByteLength, sinoCmd.Length)];
                        Array.Copy(sinoCmd, sendBuf, sinoCmd.Length);
                        transport.SetFeatureReport(r.Interface.DevicePath, sendBuf);
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Command Handlers: Guided A-B Battery & Charger Calibration Engine
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Snapshot of all probed HID reports on an interface collection.
        /// </summary>
        private class CalibrationSnapshot
        {
            public Dictionary<string, byte[]> FeatureReports = new Dictionary<string, byte[]>();
            public Dictionary<string, byte[]> InputReports = new Dictionary<string, byte[]>();
            public Dictionary<int, byte[]> SpontaneousReports = new Dictionary<int, byte[]>();
        }

        /// <summary>
        /// Performs two-stage differential calibration (discharging state vs charging state)
        /// to automatically isolate the charging flag and battery percentage bytes.
        /// </summary>
        static void RunCalibrate(string filter = null)
        {
            PrintBanner();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("============================================================================");
            Console.WriteLine("    OmniHID Guided A-B Battery & Charger Differential Calibration Engine    ");
            Console.WriteLine("============================================================================");
            Console.ResetColor();
            Console.WriteLine();

            using (var transport = new Win32HidTransport())
            {
                string devName;
                ushort vid;
                ushort pid;
                List<HidDeviceInfo> targetInterfaces;
                DeviceProfile profile;
                HashSet<ushort> targetPids;

                if (!SelectTargetDevice(transport, filter, out devName, out vid, out pid, out targetInterfaces, out profile, out targetPids))
                {
                    return;
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Target Device: {0} (VID: 0x{1:X4}, PID: 0x{2:X4})", devName, vid, pid);
                Console.WriteLine("Total Endpoints: {0}", targetInterfaces.Count);
                if (targetPids.Count > 1)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("Dual-Mode PIDs Monitored: {0}", string.Join(", ", new List<ushort>(targetPids).ConvertAll(p => "0x" + p.ToString("X4")).ToArray()));
                }
                Console.ResetColor();

                // IC Fingerprinting
                var fp = IcFingerprinter.Identify(vid, pid, targetInterfaces, devName);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("IC Architecture: ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("{0} (Confidence: {1})", fp.ChipsetFamily, fp.Confidence);
                Console.ResetColor();
                if (!string.IsNullOrEmpty(fp.Description))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("Architecture Notes: {0}", fp.Description);
                    Console.ResetColor();
                }

                if (fp.IsNonBatteryDevice)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n[!] WARNING: This device is classified as non-battery hardware!");
                    Console.WriteLine("    Calibration will not discover battery or charging flags.");
                    Console.ResetColor();
                }
                Console.WriteLine("----------------------------------------------------------------------------\n");

                // ── STAGE 1: State A (Battery Power / Cable Disconnected) ─────
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║ STAGE 1/2: DISCHARGING STATE (ON BATTERY)                                ║");
                Console.WriteLine("║ 1. Ensure the USB charging cable is DISCONNECTED from the device.        ║");
                Console.WriteLine("║ 2. Device must be operating wirelessly on its internal battery.          ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
                Console.ResetColor();
                Console.Write("\nPress [ENTER] to capture State A (Battery Baseline)... ");
                try { Console.ReadLine(); } catch { }

                var interfacesA = ReEnumerateTargetInterfaces(transport, vid, targetPids);
                if (interfacesA.Count == 0) interfacesA = targetInterfaces;
                ushort pidA = interfacesA.Count > 0 ? interfacesA[0].ProductId : pid;

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("\n[State A Baseline] Active PID: 0x{0:X4} ({1} active endpoints).", pidA, interfacesA.Count);
                Console.WriteLine("Capturing State A report matrix (feature sweep, active queries, input reports)...");
                Console.ResetColor();
                var snapshotA = CaptureDeviceReports(transport, interfacesA, profile);
                Console.WriteLine(" Captured: {0} Feature reports, {1} Input reports, {2} Spontaneous packets.\n",
                    snapshotA.FeatureReports.Count, snapshotA.InputReports.Count, snapshotA.SpontaneousReports.Count);

                // ── STAGE 2: State B (Charging Power / Cable Connected) ────────
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║ STAGE 2/2: CHARGING STATE (CABLE CONNECTED)                              ║");
                Console.WriteLine("║ 1. CONNECT THE USB CHARGING CABLE to the device now.                     ║");
                Console.WriteLine("║ 2. Wait 2-3 seconds for charging LED/circuit to engage.                  ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════╝");
                Console.ResetColor();
                Console.Write("\nPress [ENTER] after connecting the charging cable... ");
                try { Console.ReadLine(); } catch { }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("\nRescanning bus for charging cable connection & PnP updates...");
                Console.ResetColor();

                // Wait up to 2.5 seconds for USB arrival / PID change if transition takes a moment
                List<HidDeviceInfo> interfacesB = ReEnumerateTargetInterfaces(transport, vid, targetPids);
                for (int retry = 0; retry < 5 && (interfacesB.Count == 0 || (interfacesB.Count > 0 && interfacesB[0].ProductId == pidA && targetPids.Count > 1)); retry++)
                {
                    Thread.Sleep(500);
                    var refreshed = ReEnumerateTargetInterfaces(transport, vid, targetPids);
                    if (refreshed.Count > 0)
                    {
                        bool hasNewPid = false;
                        foreach (var inf in refreshed)
                        {
                            if (inf.ProductId != pidA) { hasNewPid = true; break; }
                        }
                        interfacesB = refreshed;
                        if (hasNewPid) break;
                    }
                }
                if (interfacesB.Count == 0) interfacesB = targetInterfaces;

                ushort pidB = interfacesB.Count > 0 ? interfacesB[0].ProductId : pid;
                if (pidB != pidA)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  ⚡ [Hotplug Sync] Peripheral switched to wired charging mode (PID: 0x{0:X4}) with {1} endpoint(s)!",
                        pidB, interfacesB.Count);
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("  [Bus Sync] Target PID: 0x{0:X4} ({1} active endpoints).", pidB, interfacesB.Count);
                    Console.ResetColor();
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("Capturing State B report matrix (feature sweep, active queries, input reports)...");
                Console.ResetColor();
                var snapshotB = CaptureDeviceReports(transport, interfacesB, profile);
                Console.WriteLine(" Captured: {0} Feature reports, {1} Input reports, {2} Spontaneous packets.\n",
                    snapshotB.FeatureReports.Count, snapshotB.InputReports.Count, snapshotB.SpontaneousReports.Count);

                // ── STAGE 3: Differential Engine Analysis ─────────────────────
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("============================================================================");
                Console.WriteLine("              DIFFERENTIAL CALIBRATION ANALYSIS (A vs B)                    ");
                Console.WriteLine("============================================================================");
                Console.ResetColor();

                AnalyzeCalibrationDiff(snapshotA, snapshotB, interfacesB);
            }
        }

        /// <summary>
        /// Captures a full snapshot of all active Feature, Input, and active query packets from the target endpoints.
        /// </summary>
        private static CalibrationSnapshot CaptureDeviceReports(Win32HidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile = null)
        {
            var snap = new CalibrationSnapshot();

            // 1. Feature reports sweep
            for (int ifIdx = 0; ifIdx < interfaces.Count; ifIdx++)
            {
                var iface = interfaces[ifIdx];
                int featLen = Math.Max(64, iface.FeatureReportByteLength > 0 ? (int)iface.FeatureReportByteLength : 64);
                byte[] candidates = new byte[] {
                    0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C,
                    0x10, 0x11, 0x12, 0x20, 0x80, 0x81, 0x82, 0x83, 0x84, 0x8F, 0x90, 0x92, 0x96, 0x97
                };

                foreach (byte repId in candidates)
                {
                    byte[] buf = new byte[featLen];
                    if (transport.GetFeatureReport(iface.DevicePath, repId, buf) && HasNonZeroData(buf))
                    {
                        string key = string.Format("EP#{0}_Feat_0x{1:X2}", ifIdx + 1, repId);
                        snap.FeatureReports[key] = buf;
                    }
                }

                // 2. Input reports probe via control transfer GetInputReport
                int inLen = Math.Max(64, iface.InputReportByteLength > 0 ? (int)iface.InputReportByteLength : 64);
                for (byte repId = 0; repId <= 0x10; repId++)
                {
                    byte[] buf = new byte[inLen];
                    if (transport.GetInputReport(iface.DevicePath, repId, buf) && HasNonZeroData(buf))
                    {
                        string key = string.Format("EP#{0}_In_0x{1:X2}", ifIdx + 1, repId);
                        snap.InputReports[key] = buf;
                    }
                }
            }

            // 3. Active Protocol Query Probing (Areson, CompX, ROYUAN, SinoWealth, Generic)
            ProbeAndCaptureActiveTelemetries(transport, interfaces, snap, profile);

            // 4. Brief spontaneous read (300ms) on readable endpoints
            for (int ifIdx = 0; ifIdx < interfaces.Count; ifIdx++)
            {
                var iface = interfaces[ifIdx];
                if (iface.UsagePage == 0x0001 && iface.Usage == 0x0006) continue;
                if (iface.InputReportByteLength <= 0) continue;

                byte[] buf = new byte[Math.Max(64, (int)iface.InputReportByteLength)];
                if (transport.ReadInputReport(iface.DevicePath, buf, 300) && HasNonZeroData(buf))
                {
                    snap.SpontaneousReports[ifIdx + 1] = buf;
                }
            }

            return snap;
        }

        /// <summary>
        /// Actively transmits protocol query commands on vendor endpoints and captures incoming telemetry responses via overlapped I/O.
        /// </summary>
        private static void ProbeAndCaptureActiveTelemetries(
            Win32HidTransport transport,
            List<HidDeviceInfo> interfaces,
            CalibrationSnapshot snap,
            DeviceProfile profile)
        {
            if (interfaces == null || interfaces.Count == 0) return;

            var inputCandidates = interfaces.FindAll(d => d.InputReportByteLength > 0 && !(d.UsagePage == 0x0001 && d.Usage == 0x0006));
            var featureCandidates = interfaces.FindAll(d => d.FeatureReportByteLength > 0 || d.OutputReportByteLength > 0 || d.UsagePage >= 0xFF00);
            if (inputCandidates.Count == 0) inputCandidates = interfaces;
            if (featureCandidates.Count == 0) featureCandidates = interfaces;

            ushort vid = interfaces[0].VendorId;
            string protocol = profile != null ? profile.ProtocolId : "";

            // List of candidate command packets: (label, buffer)
            List<KeyValuePair<string, byte[]>> queries = new List<KeyValuePair<string, byte[]>>();

            // 1. Areson Protocol Probe (VID 0x25A7 or protocol "areson")
            if (vid == 0x25A7 || string.Equals(protocol, "areson", StringComparison.OrdinalIgnoreCase))
            {
                byte[] aresonCmd = new byte[17];
                aresonCmd[0] = 0x08; // Feature Report ID
                aresonCmd[1] = 0x04; // CMD_QUERY_STATUS
                byte sum = 0;
                for (int b = 0; b < 16; b++) sum += aresonCmd[b];
                aresonCmd[16] = (byte)(0x55 - sum);
                queries.Add(new KeyValuePair<string, byte[]>("Areson_0x08_0x04", aresonCmd));

                byte[] aresonUnnumbered = new byte[65];
                aresonUnnumbered[0] = 0x00;
                Array.Copy(aresonCmd, 0, aresonUnnumbered, 1, aresonCmd.Length);
                queries.Add(new KeyValuePair<string, byte[]>("Areson_Unnumbered_65B", aresonUnnumbered));
            }

            // 2. CompX / PixArt Probes (VID 0x24AE or protocol "compx")
            if (vid == 0x24AE || string.Equals(protocol, "compx", StringComparison.OrdinalIgnoreCase))
            {
                queries.Add(new KeyValuePair<string, byte[]>("CompX_0x06", new byte[] { 0x06, 0x00, 0x00, 0x00 }));
                queries.Add(new KeyValuePair<string, byte[]>("CompX_0x04", new byte[] { 0x04, 0x02, 0x00, 0x00 }));
            }

            // 3. ROYUAN / YiChip Probes (VID 0x3151, 0x0461, etc.)
            if (vid == 0x3151 || vid == 0x0461 || string.Equals(protocol, "royuan-keyboard", StringComparison.OrdinalIgnoreCase))
            {
                queries.Add(new KeyValuePair<string, byte[]>("Royuan_0x83_GetBattery", new byte[] { 0x00, 0x83, 0x00, 0x00 }));
                queries.Add(new KeyValuePair<string, byte[]>("Royuan_0x8F_GetInfo", new byte[] { 0x00, 0x8F, 0x00, 0x00 }));
            }

            // 4. SinoWealth Probes (VID 0x258A or protocol "sinowealth")
            if (vid == 0x258A || string.Equals(protocol, "sinowealth", StringComparison.OrdinalIgnoreCase))
            {
                queries.Add(new KeyValuePair<string, byte[]>("SinoWealth_0x04", new byte[] { 0x04, 0x00, 0x00, 0x00 }));
                queries.Add(new KeyValuePair<string, byte[]>("SinoWealth_0x07", new byte[] { 0x07, 0x00, 0x00, 0x00 }));
            }

            // 5. Generic Query fallbacks
            queries.Add(new KeyValuePair<string, byte[]>("Generic_0x02", new byte[] { 0x02, 0x00, 0x00, 0x00 }));
            queries.Add(new KeyValuePair<string, byte[]>("Generic_0x01", new byte[] { 0x01, 0x00, 0x00, 0x00 }));

            // Execute query pulses with pre-reading readers
            foreach (var q in queries)
            {
                List<ActiveSnifferReader> readers = new List<ActiveSnifferReader>();
                List<WaitHandle> waitHandles = new List<WaitHandle>();

                try
                {
                    for (int i = 0; i < inputCandidates.Count; i++)
                    {
                        var inIface = inputCandidates[i];
                        SafeFileHandle h = Win32HidTransport.OpenDevice(inIface.DevicePath, Win32HidNative.GENERIC_READ, true);
                        if (!h.IsInvalid)
                        {
                            var r = new ActiveSnifferReader(inIface, h, i + 1);
                            if (r.StartRead())
                            {
                                readers.Add(r);
                                waitHandles.Add(r.WaitEvent);
                            }
                            else
                            {
                                r.Dispose();
                            }
                        }
                    }

                    if (readers.Count == 0) continue;

                    // Send command to candidate feature / output endpoints
                    byte[] rawCmd = q.Value;
                    foreach (var fIface in featureCandidates)
                    {
                        int targetLen = fIface.FeatureReportByteLength > 0 ? (int)fIface.FeatureReportByteLength : rawCmd.Length;
                        byte[] sendBuf = rawCmd;
                        if (targetLen > rawCmd.Length)
                        {
                            sendBuf = new byte[targetLen];
                            Array.Copy(rawCmd, sendBuf, rawCmd.Length);
                        }

                        bool sent = transport.SetFeatureReport(fIface.DevicePath, sendBuf);
                        if (!sent)
                        {
                            transport.WriteOutputReport(fIface.DevicePath, sendBuf);
                        }
                    }

                    // Wait up to 250ms for any response packet
                    int signaled = WaitHandle.WaitAny(waitHandles.ToArray(), 250);
                    if (signaled != WaitHandle.WaitTimeout && signaled >= 0 && signaled < readers.Count)
                    {
                        var r = readers[signaled];
                        uint bytesRead;
                        if (r.CompleteRead(out bytesRead) && bytesRead > 0 && HasNonZeroData(r.Buffer))
                        {
                            byte repId = r.Buffer[0];
                            string snapKey = string.Format("ActiveQuery_{0}_Resp_0x{1:X2}", q.Key, repId);
                            byte[] copy = new byte[bytesRead];
                            Array.Copy(r.Buffer, copy, bytesRead);
                            snap.InputReports[snapKey] = copy;
                        }
                    }
                }
                catch { }
                finally
                {
                    foreach (var r in readers) r.Dispose();
                }
            }
        }

        /// <summary>
        /// Analyzes byte differences between State A (discharging) and State B (charging) snapshots.
        /// Supports cross-endpoint matching by Report ID suffix when physical endpoints shift during connection switches.
        /// </summary>
        private static void AnalyzeCalibrationDiff(CalibrationSnapshot snapA, CalibrationSnapshot snapB, List<HidDeviceInfo> interfaces)
        {
            int diffCount = 0;
            int confirmedMatches = 0;

            Action<string, string, byte[], byte[]> compareBuffers = (reportType, key, bufA, bufB) =>
            {
                int len = Math.Min(bufA.Length, bufB.Length);
                List<int> changedOffsets = new List<int>();
                for (int b = 0; b < len; b++)
                {
                    if (bufA[b] != bufB[b])
                    {
                        changedOffsets.Add(b);
                    }
                }

                if (changedOffsets.Count == 0) return;
                diffCount++;

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("  ── {0} [{1}] (Len {2}B) ──", reportType, key, len);
                Console.ResetColor();

                Console.Write("     Changed Byte Offsets: ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(string.Join(", ", changedOffsets.ConvertAll(i => i.ToString()).ToArray()));
                Console.ResetColor();

                int bestCandOffset = -1;

                foreach (int off in changedOffsets)
                {
                    byte valA = bufA[off];
                    byte valB = bufB[off];

                    // Charging flag transition check (0 -> 1, 0 -> 2, 0 -> 3, etc.)
                    bool isChargingFlagTransition = (valA == 0x00 || valA == 0x02) && (valB == 0x01 || valB == 0x02 || valB == 0x03);

                    if (isChargingFlagTransition)
                    {
                        confirmedMatches++;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("     ★ [CONFIRMED CHARGING FLAG] Byte[{0}]: State A = 0x{1:X2} (Discharging) -> State B = 0x{2:X2} (Charging)!",
                            off, valA, valB);
                        Console.ResetColor();

                        // Look for adjacent battery percentage bytes (within +-4 bytes)
                        for (int n = Math.Max(1, off - 4); n <= Math.Min(len - 1, off + 4); n++)
                        {
                            if (n == off) continue;
                            byte candVal = bufB[n];
                            if (candVal >= 10 && candVal <= 100)
                            {
                                bestCandOffset = n;
                                Console.ForegroundColor = ConsoleColor.Cyan;
                                Console.WriteLine("       └─ Adjacent Byte[{0}] = {1} (0x{1:X2}) => HIGH CONFIDENCE BATTERY LEVEL: {1}%!",
                                    n, candVal);
                                Console.ResetColor();
                            }
                        }
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine("     · Byte[{0}]: State A = 0x{1:X2} ({1}) -> State B = 0x{2:X2} ({2})",
                            off, valA, valB);
                        Console.ResetColor();
                    }
                }

                // Show hex view comparison
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("     State A (Battery):");
                HexView.PrintHexDump(bufA, 16, changedOffsets[0], bestCandOffset);
                Console.WriteLine("     State B (Charging):");
                HexView.PrintHexDump(bufB, 16, changedOffsets[0], bestCandOffset);
                Console.ResetColor();
            };

            Func<string, string> extractReportSuffix = key =>
            {
                int idx = key.LastIndexOf("_0x", StringComparison.OrdinalIgnoreCase);
                return idx >= 0 ? key.Substring(idx) : null;
            };

            // Compare Feature Reports with fallback cross-endpoint matching
            var processedFeatKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in snapA.FeatureReports)
            {
                byte[] bufB;
                if (snapB.FeatureReports.TryGetValue(kvp.Key, out bufB))
                {
                    processedFeatKeys.Add(kvp.Key);
                    compareBuffers("Feature Report", kvp.Key, kvp.Value, bufB);
                }
                else
                {
                    string suffix = extractReportSuffix(kvp.Key);
                    if (!string.IsNullOrEmpty(suffix))
                    {
                        foreach (var bKvp in snapB.FeatureReports)
                        {
                            if (!processedFeatKeys.Contains(bKvp.Key) && bKvp.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                            {
                                processedFeatKeys.Add(bKvp.Key);
                                compareBuffers("Feature Report (Cross-EP)", string.Format("{0} ↔ {1}", kvp.Key, bKvp.Key), kvp.Value, bKvp.Value);
                                break;
                            }
                        }
                    }
                }
            }

            // Compare Input Reports with fallback cross-endpoint matching
            var processedInKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in snapA.InputReports)
            {
                byte[] bufB;
                if (snapB.InputReports.TryGetValue(kvp.Key, out bufB))
                {
                    processedInKeys.Add(kvp.Key);
                    compareBuffers("Input Report", kvp.Key, kvp.Value, bufB);
                }
                else
                {
                    string suffix = extractReportSuffix(kvp.Key);
                    if (!string.IsNullOrEmpty(suffix))
                    {
                        foreach (var bKvp in snapB.InputReports)
                        {
                            if (!processedInKeys.Contains(bKvp.Key) && bKvp.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                            {
                                processedInKeys.Add(bKvp.Key);
                                compareBuffers("Input Report (Cross-EP)", string.Format("{0} ↔ {1}", kvp.Key, bKvp.Key), kvp.Value, bKvp.Value);
                                break;
                            }
                        }
                    }
                }
            }

            // Compare Spontaneous Reports
            foreach (var kvp in snapA.SpontaneousReports)
            {
                byte[] bufB;
                if (snapB.SpontaneousReports.TryGetValue(kvp.Key, out bufB))
                {
                    compareBuffers("Spontaneous Stream", string.Format("EP#{0}", kvp.Key), kvp.Value, bufB);
                }
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("============================================================================");
            if (confirmedMatches > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" [CALIBRATION COMPLETE] Successfully identified {0} charging status transition(s)!", confirmedMatches);
                Console.WriteLine(" Use the highlighted byte offsets above to build your protocol handler or JSON profile.");
            }
            else if (diffCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(" [CALIBRATION COMPLETE] Found {0} report(s) with changed bytes between State A and B.", diffCount);
                Console.WriteLine(" Review the changed byte offsets above for potential battery/power transitions.");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine(" [CALIBRATION COMPLETE] No differences detected between State A and B.");
                Console.WriteLine(" The device may communicate only upon active user interaction (clicks / motion).");
                Console.WriteLine(" Tip: Use option [5] Live Sniffer while plugging/unplugging the cable.");
            }
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("============================================================================");
            Console.ResetColor();
            Console.WriteLine();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Command Handlers: AI-Ready Hardware Protocol Specification Exporter
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Compiles a comprehensive reverse-engineering specification markdown document with an LLM prompt.
        /// </summary>
        static void RunExportSpec(string filter = null)
        {
            PrintBanner();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("============================================================================");
            Console.WriteLine("        OmniHID AI-Ready Protocol Specification & Prompt Generator          ");
            Console.WriteLine("============================================================================");
            Console.ResetColor();
            Console.WriteLine();

            using (var transport = new Win32HidTransport())
            {
                string devName;
                ushort vid;
                ushort pid;
                List<HidDeviceInfo> targetInterfaces;

                if (!SelectTargetDevice(transport, filter, out devName, out vid, out pid, out targetInterfaces))
                {
                    return;
                }

                string fileName = string.Format("device_spec_{0:x4}_{1:x4}.md", vid, pid);
                string fullPath = Path.GetFullPath(fileName);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Generating hardware protocol specification for:");
                Console.WriteLine("  Device: {0} (VID: 0x{1:X4}, PID: 0x{2:X4})", devName, vid, pid);
                Console.WriteLine("  Output File: {0}", fullPath);
                Console.ResetColor();
                Console.WriteLine();

                var fp = IcFingerprinter.Identify(vid, pid, targetInterfaces, devName);

                using (StreamWriter sw = new StreamWriter(fileName, false, Encoding.UTF8))
                {
                    sw.WriteLine("# Hardware Telemetry Specification: {0} (0x{1:X4}:0x{2:X4})", devName, vid, pid);
                    sw.WriteLine();
                    sw.WriteLine("> Generated by OmniHID Diagnostic Engine on {0:yyyy-MM-dd HH:mm:ss}", DateTime.Now);
                    sw.WriteLine();
                    sw.WriteLine("## 1. Device Overview");
                    sw.WriteLine();
                    sw.WriteLine("| Property | Value |");
                    sw.WriteLine("|---|---|");
                    sw.WriteLine("| **Model Name** | {0} |", devName);
                    sw.WriteLine("| **USB Vendor ID** | `0x{0:X4}` |", vid);
                    sw.WriteLine("| **USB Product ID** | `0x{0:X4}` |", pid);
                    sw.WriteLine("| **Identified IC Family** | {0} |", fp.ChipsetFamily);
                    sw.WriteLine("| **Identification Confidence** | {0} |", fp.Confidence);
                    sw.WriteLine("| **Non-Battery Hardware?** | {0} |", fp.IsNonBatteryDevice ? "Yes (Skip battery probing)" : "No (Battery device)");
                    sw.WriteLine("| **Total HID Endpoints** | {0} |", targetInterfaces.Count);
                    sw.WriteLine();

                    if (!string.IsNullOrEmpty(fp.Description))
                    {
                        sw.WriteLine("### Architecture Notes");
                        sw.WriteLine("{0}", fp.Description);
                        sw.WriteLine();
                    }

                    if (!string.IsNullOrEmpty(fp.RecommendedApproach))
                    {
                        sw.WriteLine("### Recommended Polling / Driver Approach");
                        sw.WriteLine("{0}", fp.RecommendedApproach);
                        sw.WriteLine();
                    }

                    // Section 2: Endpoints Table
                    sw.WriteLine("## 2. HID Interface Collections (Endpoints)");
                    sw.WriteLine();
                    sw.WriteLine("| # | Usage Page | Usage | Type | InLen | OutLen | FeatLen | Path |");
                    sw.WriteLine("|---|---|---|---|---|---|---|---|");

                    for (int i = 0; i < targetInterfaces.Count; i++)
                    {
                        var ep = targetInterfaces[i];
                        string uType = FormatUsage(ep.UsagePage, ep.Usage);
                        sw.WriteLine("| {0} | `0x{1:X4}` | `0x{2:X4}` | {3} | {4}B | {5}B | {6}B | `{7}` |",
                            i + 1, ep.UsagePage, ep.Usage, uType,
                            ep.InputReportByteLength, ep.OutputReportByteLength, ep.FeatureReportByteLength, ep.DevicePath);
                    }
                    sw.WriteLine();

                    // Section 3: Feature Report Responses
                    sw.WriteLine("## 3. Responding Feature Reports (Baseline Probes)");
                    sw.WriteLine();
                    byte[] candidateFeatureIds = new byte[] {
                        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C,
                        0x10, 0x11, 0x12, 0x20, 0x80, 0x81, 0x82, 0x83, 0x84, 0x8F
                    };

                    int respondedFeatCount = 0;
                    for (int i = 0; i < targetInterfaces.Count; i++)
                    {
                        var ep = targetInterfaces[i];
                        int featLen = Math.Max(64, ep.FeatureReportByteLength > 0 ? (int)ep.FeatureReportByteLength : 64);

                        foreach (byte fid in candidateFeatureIds)
                        {
                            byte[] buf = new byte[featLen];
                            if (transport.GetFeatureReport(ep.DevicePath, fid, buf) && HasNonZeroData(buf))
                            {
                                respondedFeatCount++;
                                sw.WriteLine("### EP #{0} (`0x{1:X4}:0x{2:X4}`) - Feature Report `0x{3:X2}`", i + 1, ep.UsagePage, ep.Usage, fid);
                                sw.WriteLine("```");
                                sw.WriteLine(FormatHex(buf, 32));
                                sw.WriteLine("```");
                                sw.WriteLine();
                            }
                        }
                    }

                    if (respondedFeatCount == 0)
                    {
                        sw.WriteLine("*No non-zero Feature Reports responded to standard polled queries.*");
                        sw.WriteLine();
                    }

                    // Section 4: Prompt for AI
                    sw.WriteLine("## 4. Prompt for AI Assistant / Protocol Generator");
                    sw.WriteLine();
                    sw.WriteLine("Copy and paste the following prompt into your LLM assistant (e.g. Gemini, Claude, ChatGPT) to generate the complete driver code:");
                    sw.WriteLine();
                    sw.WriteLine("````markdown");
                    sw.WriteLine("I am developing an open-source C# library called OmniHID (Windows USB HID Peripheral Telemetry).");
                    sw.WriteLine("Here is the hardware specification dump for an unsupported device:");
                    sw.WriteLine("- Device: {0}", devName);
                    sw.WriteLine("- Vendor ID: 0x{0:X4}", vid);
                    sw.WriteLine("- Product ID: 0x{0:X4}", pid);
                    sw.WriteLine("- Architecture: {0}", fp.ChipsetFamily);
                    sw.WriteLine("- Recommended Approach: {0}", fp.RecommendedApproach);
                    sw.WriteLine();
                    sw.WriteLine("Based on the endpoint topology and Feature Report dumps in this specification:");
                    sw.WriteLine("1. Implement a complete C# class implementing `IProtocolHandler` for OmniHID.");
                    sw.WriteLine("2. Generate a matching JSON profile file for `devices/`.");
                    sw.WriteLine("3. Handle battery percentage calculation, charging status flags, and offline detection.");
                    sw.WriteLine("````");
                    sw.WriteLine();
                }

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("============================================================================");
                Console.WriteLine(" [SPECIFICATION EXPORT COMPLETE]");
                Console.WriteLine(" File saved: {0}", fullPath);
                Console.WriteLine(" You can open this file, inspect the dumps, or paste section 4 to an AI!");
                Console.WriteLine("============================================================================");
                Console.ResetColor();
                Console.WriteLine();
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Device Selection Helper
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Selects a target peripheral device from consolidated devices or raw HID interface collections.
        /// Resolves the associated declarative profile and all sibling Product IDs for dual-mode devices.
        /// </summary>
        private static bool SelectTargetDevice(
            Win32HidTransport transport,
            string filter,
            out string devName,
            out ushort vid,
            out ushort pid,
            out List<HidDeviceInfo> targetInterfaces,
            out DeviceProfile profile,
            out HashSet<ushort> targetPids)
        {
            devName = "Unknown Device";
            vid = 0;
            pid = 0;
            targetInterfaces = new List<HidDeviceInfo>();
            profile = null;
            targetPids = new HashSet<ushort>();

            using (var manager = new OmniManager(transport))
            {
                var allDevices = manager.ScanDevices();
                var targets = new List<IOmniDevice>();

                foreach (var d in allDevices)
                {
                    if (MatchesFilter(d, filter))
                    {
                        targets.Add(d);
                    }
                }

                if (targets.Count >= 1)
                {
                    IOmniDevice td = null;
                    if (targets.Count == 1 || !_interactiveMode)
                    {
                        td = targets[0];
                    }
                    else
                    {
                        Console.WriteLine("Detected {0} matching peripheral(s):", targets.Count);
                        for (int i = 0; i < targets.Count; i++)
                        {
                            Console.WriteLine("  [{0}] {1} (VID: 0x{2:X4}, PID: 0x{3:X4}, Endpoints: {4})",
                                i + 1, targets[i].Name, targets[i].VendorId, targets[i].ProductId, targets[i].Interfaces.Count);
                        }
                        Console.Write(string.Format("\nSelect device [1-{0}]: ", targets.Count));
                        string choice = Console.ReadLine();
                        int selIdx = 1;
                        if (!int.TryParse(choice != null ? choice.Trim() : "", out selIdx) || selIdx < 1 || selIdx > targets.Count)
                            selIdx = 1;

                        td = targets[selIdx - 1];
                    }

                    devName = td.Name;
                    vid = td.VendorId;
                    pid = td.ProductId;
                    targetInterfaces = new List<HidDeviceInfo>(td.Interfaces);

                    var omniDev = td as OmniDevice;
                    profile = omniDev != null ? omniDev.Profile : null;
                    if (profile == null)
                    {
                        profile = manager.Registry.FindProfile(vid, pid, devName);
                    }

                    if (profile != null && profile.ProductIds != null)
                    {
                        for (int i = 0; i < profile.ProductIds.Length; i++)
                        {
                            targetPids.Add(profile.ProductIds[i]);
                        }
                    }
                    targetPids.Add(pid);
                    return true;
                }

                // Fallback: search raw HID endpoints if not matched by OmniManager
                var allRaw = transport.Enumerate();
                var rawMatching = new List<HidDeviceInfo>();
                foreach (var r in allRaw)
                {
                    if (MatchesFilter(r, filter)) rawMatching.Add(r);
                }

                if (rawMatching.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("No matching HID devices found for filter '{0}'.", filter ?? "");
                    Console.WriteLine("Tip: Run 'omni-hid list' (option [2]) to view all present hardware devices.");
                    Console.ResetColor();
                    return false;
                }

                // Group raw endpoints by VID:PID
                Dictionary<uint, List<HidDeviceInfo>> byVidPid = new Dictionary<uint, List<HidDeviceInfo>>();
                foreach (var r in rawMatching)
                {
                    uint key = ((uint)r.VendorId << 16) | r.ProductId;
                    List<HidDeviceInfo> list;
                    if (!byVidPid.TryGetValue(key, out list))
                    {
                        list = new List<HidDeviceInfo>();
                        byVidPid[key] = list;
                    }
                    list.Add(r);
                }

                List<List<HidDeviceInfo>> deviceGroups = new List<List<HidDeviceInfo>>(byVidPid.Values);
                List<HidDeviceInfo> chosen = null;

                if (deviceGroups.Count == 1 || !_interactiveMode)
                {
                    chosen = deviceGroups[0];
                }
                else
                {
                    Console.WriteLine("Multiple device groups found:");
                    for (int i = 0; i < deviceGroups.Count; i++)
                    {
                        var g = deviceGroups[i];
                        string title = !string.IsNullOrEmpty(g[0].ProductString) ? g[0].ProductString : g[0].ManufacturerString ?? "Device";
                        Console.WriteLine("  [{0}] {1} (VID: 0x{2:X4}, PID: 0x{3:X4}, Endpoints: {4})",
                            i + 1, title, g[0].VendorId, g[0].ProductId, g.Count);
                    }
                    Console.Write(string.Format("\nSelect device group [1-{0}]: ", deviceGroups.Count));
                    string choice = Console.ReadLine();
                    int selIdx = 1;
                    if (!int.TryParse(choice != null ? choice.Trim() : "", out selIdx) || selIdx < 1 || selIdx > deviceGroups.Count)
                        selIdx = 1;

                    chosen = deviceGroups[selIdx - 1];
                }

                vid = chosen[0].VendorId;
                pid = chosen[0].ProductId;
                devName = !string.IsNullOrEmpty(chosen[0].ProductString) ? chosen[0].ProductString : "USB HID Device (0x" + vid.ToString("X4") + ")";
                targetInterfaces = chosen;

                profile = manager.Registry.FindProfile(vid, pid, devName);
                if (profile != null && profile.ProductIds != null)
                {
                    for (int i = 0; i < profile.ProductIds.Length; i++)
                    {
                        targetPids.Add(profile.ProductIds[i]);
                    }
                }
                targetPids.Add(pid);
                return true;
            }
        }

        /// <summary>
        /// Backwards-compatible overload for selecting a target device without returning profile or multi-PID set.
        /// </summary>
        private static bool SelectTargetDevice(
            Win32HidTransport transport,
            string filter,
            out string devName,
            out ushort vid,
            out ushort pid,
            out List<HidDeviceInfo> targetInterfaces)
        {
            DeviceProfile dummyProfile;
            HashSet<ushort> dummyPids;
            return SelectTargetDevice(transport, filter, out devName, out vid, out pid, out targetInterfaces, out dummyProfile, out dummyPids);
        }

        /// <summary>
        /// Re-enumerates all connected HID endpoints matching the target Vendor ID and any of the device's associated Product IDs.
        /// Handles dynamic connection transitions (e.g. 2.4G wireless dongle ↔ wired USB charging cable).
        /// </summary>
        private static List<HidDeviceInfo> ReEnumerateTargetInterfaces(
            Win32HidTransport transport,
            ushort vid,
            HashSet<ushort> allowedPids)
        {
            var allHid = transport.Enumerate();
            var matching = new List<HidDeviceInfo>();
            foreach (var iface in allHid)
            {
                if (iface.VendorId == vid)
                {
                    if (allowedPids == null || allowedPids.Count == 0 || allowedPids.Contains(iface.ProductId))
                    {
                        matching.Add(iface);
                    }
                }
            }
            return matching;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Filtering & Formatting Helpers
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Tests if a peripheral device matches an optional filter query.
        /// </summary>
        private static bool MatchesFilter(IOmniDevice dev, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (dev.Name != null && dev.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (dev.Category.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (dev.ProtocolId != null && dev.ProtocolId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (dev.VendorId.ToString("X4").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (dev.ProductId.ToString("X4").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Tests if an enumerated HID interface matches an optional filter query.
        /// </summary>
        private static bool MatchesFilter(HidDeviceInfo iface, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            if (iface.ProductString != null && iface.ProductString.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (iface.ManufacturerString != null && iface.ManufacturerString.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (iface.VendorId.ToString("X4").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (iface.ProductId.ToString("X4").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (iface.UsagePage.ToString("X4").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Matches a space-delimited keywords string against an input query.
        /// </summary>
        private static bool MatchesFilterString(string haystack, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            return haystack.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Returns a human-friendly string for standard USB HID Usage Page and Usage definitions.
        /// </summary>
        private static string FormatUsage(ushort page, ushort usage)
        {
            if (page == 0x0001)
            {
                switch (usage)
                {
                    case 0x0001: return "Generic: Pointer";
                    case 0x0002: return "Generic: Mouse";
                    case 0x0004: return "Generic: Joystick";
                    case 0x0005: return "Generic: Gamepad";
                    case 0x0006: return "Generic: Keyboard";
                    case 0x0007: return "Generic: Keypad";
                    case 0x0008: return "Generic: Multi-axis Controller";
                    case 0x0080: return "Generic: System Control";
                    default: return string.Format("Generic Desktop (0x{0:X4})", usage);
                }
            }
            if (page == 0x000C)
            {
                switch (usage)
                {
                    case 0x0001: return "Consumer: Consumer Control";
                    case 0x0002: return "Consumer: Numeric Key Pad";
                    default: return string.Format("Consumer (0x{0:X4})", usage);
                }
            }
            if (page == 0x0007) return "Keyboard/Keypad";
            if (page == 0x0008) return "LEDs";
            if (page == 0x0009) return "Button";
            if (page == 0x000B) return "Telephony";
            if (page == 0x000D) return "Digitizer";
            if (page == 0x000E) return "Haptics";
            if (page == 0x0012) return "Eye & Head Tracker";
            if (page == 0x0014) return "Auxiliary Display";
            if (page == 0x0020) return "Sensor";
            if (page == 0x0040) return "Medical Instrument";
            if (page == 0x0041) return "Braille Display";
            if (page == 0x0059) return "Lighting / Illumination";
            if (page == 0x0084) return "Power Device";
            if (page == 0x0085) return "Battery System";
            if (page == 0x008C) return "Bar Code Scanner";
            if (page == 0x008D) return "Scale (Weighing)";
            if (page == 0x0090) return "Camera Control";
            if (page == 0x0091) return "Arcade";
            if (page == 0xF1D0) return "FIDO Alliance";
            if (page >= 0xFF00) return string.Format("Vendor-Defined (0x{0:X4})", page);

            return string.Format("Page 0x{0:X4}: Usage 0x{1:X4}", page, usage);
        }

        /// <summary>
        /// Formats a set of diff byte positions into a compact comma-separated string.
        /// </summary>
        private static string FormatDiffPositions(HashSet<int> positions)
        {
            if (positions == null || positions.Count == 0) return "none";
            var sorted = new List<int>(positions);
            sorted.Sort();
            var sb = new StringBuilder();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(sorted[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns a compact tag icon representing the peripheral category.
        /// </summary>
        static string GetCategoryIcon(DeviceCategory cat)
        {
            switch (cat)
            {
                case DeviceCategory.Mouse: return "[M]";
                case DeviceCategory.Keyboard: return "[K]";
                case DeviceCategory.Headset: return "[H]";
                case DeviceCategory.Gamepad: return "[G]";
                default: return "[?]";
            }
        }

        /// <summary>
        /// Formats a byte array as a space-delimited uppercase hex string up to maxBytes.
        /// </summary>
        static string FormatHex(byte[] data, int maxBytes)
        {
            if (data == null || data.Length == 0) return "";
            int count = Math.Min(data.Length, maxBytes);
            var sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                sb.Append(data[i].ToString("X2")).Append(" ");
            }
            if (data.Length > count) sb.Append("...");
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Formats an entire byte array as uppercase hex.
        /// </summary>
        static string FormatHexFull(byte[] data)
        {
            if (data == null || data.Length == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                sb.Append(data[i].ToString("X2")).Append(" ");
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Checks if a byte buffer contains non-zero payload data beyond the first byte (Report ID).
        /// </summary>
        static bool HasNonZeroData(byte[] buf)
        {
            if (buf == null || buf.Length == 0) return false;
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i] != 0) return true;
            }
            return false;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Asynchronous Sniffer Reader Class
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Tracks an asynchronous overlapped read operation on an active HID endpoint for packet sniffing.
        /// Retains the last transferred buffer to compute byte-level real-time diffs.
        /// </summary>
        private class ActiveSnifferReader : IDisposable
        {
            public HidDeviceInfo Interface { get; private set; }
            public SafeFileHandle Handle { get; private set; }
            public int InterfaceIndex { get; private set; }
            public byte[] Buffer { get; private set; }
            public byte[] LastBuffer { get; set; }
            public ManualResetEvent WaitEvent { get; private set; }
            public NativeOverlapped Overlapped;

            private IntPtr _pOverlapped;
            private bool _isPending;
            private bool _completed;

            public ActiveSnifferReader(HidDeviceInfo iface, SafeFileHandle handle, int index)
            {
                Interface = iface;
                Handle = handle;
                InterfaceIndex = index;
                int len = iface.InputReportByteLength > 0 ? (int)iface.InputReportByteLength : 64;
                Buffer = new byte[Math.Max(64, len)];
                WaitEvent = new ManualResetEvent(false);

                Overlapped = new NativeOverlapped { EventHandle = WaitEvent.SafeWaitHandle.DangerousGetHandle() };
                _pOverlapped = Marshal.AllocHGlobal(Marshal.SizeOf(Overlapped));
                Marshal.StructureToPtr(Overlapped, _pOverlapped, false);
            }

            public bool StartRead()
            {
                WaitEvent.Reset();
                _completed = false;
                _isPending = false;

                uint bytesRead;
                bool ok = Win32HidNative.ReadFile(Handle, Buffer, (uint)Buffer.Length, out bytesRead, _pOverlapped);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == Win32HidNative.ERROR_IO_PENDING)
                    {
                        _isPending = true;
                        return true;
                    }
                    return false;
                }

                _completed = true;
                WaitEvent.Set();
                return true;
            }

            public bool CompleteRead(out uint bytesTransferred)
            {
                bytesTransferred = 0;
                if (_completed)
                {
                    bytesTransferred = (uint)Buffer.Length;
                    return true;
                }

                if (_isPending)
                {
                    if (Win32HidNative.GetOverlappedResult(Handle, _pOverlapped, out bytesTransferred, false))
                    {
                        return true;
                    }
                }
                return false;
            }

            public void Dispose()
            {
                try
                {
                    if (_isPending && !_completed && !Handle.IsInvalid && !Handle.IsClosed)
                    {
                        Win32HidNative.CancelIoEx(Handle, _pOverlapped);
                        uint bytesRead;
                        Win32HidNative.GetOverlappedResult(Handle, _pOverlapped, out bytesRead, true);
                    }
                }
                catch { }

                if (_pOverlapped != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_pOverlapped);
                    _pOverlapped = IntPtr.Zero;
                }

                if (WaitEvent != null)
                {
                    WaitEvent.Close();
                    WaitEvent = null;
                }

                if (Handle != null && !Handle.IsInvalid && !Handle.IsClosed)
                {
                    Handle.Close();
                }
            }
        }
    }
}