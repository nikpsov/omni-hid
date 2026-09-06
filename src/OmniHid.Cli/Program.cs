using System;
using System.Reflection;
using System.Text;
using OmniHid.Cli.Commands;
using OmniHid.Cli.Formatting;

namespace OmniHid.Cli
{
    /// <summary>
    /// Command-line diagnostic, monitoring, packet capture, and battery protocol hunter entry point.
    /// Provides interactive numbered menu navigation, deep device scanning, and automated battery dump calculation.
    /// </summary>
    class Program
    {
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
                catch
                {
                    // Fall back to standard probing path if embedded resource loading fails
                }
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
                RunInteractiveMenu();
                return;
            }

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
                    ListCommand.FlatMode = true;
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
                        SnifferCommand.TimeoutSeconds = t;
                    }
                    i++; // Skip the value argument
                }
                else if (aLow.StartsWith("--timeout="))
                {
                    int t;
                    if (int.TryParse(aLow.Substring("--timeout=".Length), out t) && t > 0)
                    {
                        SnifferCommand.TimeoutSeconds = t;
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
                    ScanCommand.Execute(filter, showAll, registeredOnly);
                    break;
                case "9":
                case "registered":
                case "scan-registered":
                case "json":
                    ScanCommand.Execute(filter, showAll, true);
                    break;
                case "2":
                case "list":
                    ListCommand.Execute(filter);
                    break;
                case "3":
                case "debug":
                case "diag":
                    DebugCommand.Execute(filter);
                    break;
                case "4":
                case "hunt":
                case "battery":
                    HunterCommand.Execute(filter);
                    break;
                case "5":
                case "sniff":
                case "dump":
                    SnifferCommand.Execute(filter);
                    break;
                case "6":
                case "monitor":
                    MonitorCommand.Execute();
                    break;
                case "7":
                case "calibrate":
                case "cal":
                    CalibrateCommand.Execute(filter);
                    break;
                case "8":
                case "export":
                case "spec":
                case "issue":
                case "report":
                case "--export-spec":
                case "--issue":
                    ExportCommand.Execute(filter);
                    break;
                case "--help":
                case "-h":
                case "help":
                case "?":
                default:
                    CliFormatter.PrintHelp();
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
                catch
                {
                    // Clear may fail when output is redirected
                }

                CliFormatter.PrintBanner();

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
                Console.WriteLine("    [8] 📋 Export Device Diagnostics (.md for GitHub Issue / AI)");
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
                        ScanCommand.Execute(null, false, false);
                        CliFormatter.SafeWaitForKey();
                        break;
                    case "1 --all":
                    case "1 -a":
                    case "scan --all":
                    case "scan -a":
                        ScanCommand.Execute(null, true, false);
                        CliFormatter.SafeWaitForKey();
                        break;
                    case "1 -r":
                    case "1 --registered":
                    case "scan -r":
                    case "scan --registered":
                    case "9":
                    case "registered":
                    case "scan-registered":
                    case "json":
                        ScanCommand.Execute(null, false, true);
                        CliFormatter.SafeWaitForKey();
                        break;
                    case "9 --all":
                    case "9 -a":
                    case "registered --all":
                        ScanCommand.Execute(null, true, true);
                        CliFormatter.SafeWaitForKey();
                        break;
                    case "2":
                    case "list":
                        ListCommand.Execute(null);
                        CliFormatter.SafeWaitForKey();
                        break;
                    case "3":
                    case "debug":
                    case "diag":
                        DebugCommand.Execute(null);
                        CliFormatter.SafeWaitForKey();
                        break;
                    case "4":
                    case "hunt":
                    case "battery":
                        HunterCommand.Execute(null);
                        CliFormatter.SafeWaitForKey();
                        break;
                    case "5":
                    case "sniff":
                    case "dump":
                        SnifferCommand.Execute(null);
                        CliFormatter.SafeWaitForKey();
                        break;
                    case "6":
                    case "monitor":
                        MonitorCommand.Execute();
                        CliFormatter.SafeWaitForKey();
                        break;
                    case "7":
                    case "calibrate":
                    case "cal":
                        CalibrateCommand.Execute(null);
                        CliFormatter.SafeWaitForKey();
                        break;
                    case "8":
                    case "export":
                    case "spec":
                    case "issue":
                    case "report":
                        ExportCommand.Execute(null);
                        CliFormatter.SafeWaitForKey();
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
                        CliFormatter.SafeWaitForKey();
                        break;
                }
            }
        }
    }
}