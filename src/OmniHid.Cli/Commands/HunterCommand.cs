using System;
using System.Collections.Generic;
using OmniHid.Cli.Formatting;
using OmniHid.Core.Diagnostics;
using OmniHid.Core.Transport;
using OmniHid.Core.Transport.Win32;

namespace OmniHid.Cli.Commands
{
    /// <summary>
    /// Implements the 'hunt' command, executing automated report sweeping and heuristic candidate scoring.
    /// </summary>
    public static class HunterCommand
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Command Execution
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Executes the 'hunt' command.
        /// </summary>
        /// <param name="filter">Optional device name or VID/PID filter.</param>
        /// <param name="interactiveMode">True if running interactively.</param>
        public static void Execute(string filter = null, bool interactiveMode = false)
        {
            CliFormatter.PrintBanner();
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

                if (!DeviceSelector.SelectTargetDevice(transport, filter, interactiveMode, out devName, out vid, out pid, out interfaces))
                {
                    return;
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Target Device: {0} (VID: 0x{1:X4}, PID: 0x{2:X4})", devName, vid, pid);
                Console.WriteLine("Total Endpoint Collections: {0}", interfaces.Count);
                Console.ResetColor();
                Console.WriteLine("----------------------------------------------------------------------------");

                // Check Windows PnP battery cache first
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

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("\nStarting automated battery discovery sweep across device endpoints...");
                Console.ResetColor();

                var huntResult = BatteryHunter.Hunt(transport, interfaces, msg =>
                {
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine("  -> {0}", msg);
                    Console.ResetColor();
                });

                // Display candidate findings
                foreach (var c in huntResult.Candidates)
                {
                    if (c.Score >= 3)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("    ⚡ [{0} | Score:{1}] {2}", c.Kind, c.Score, c.Description);
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine("    ·  [{0} | Score:{1}] {2}", c.Kind, c.Score, c.Description);
                    }
                    Console.ResetColor();
                }

                // Summary Results
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("============================================================================");
                Console.WriteLine(" [HUNTER SUMMARY] Probed {0} active reports across {1} endpoint collection(s).",
                    huntResult.ReportsReceived, interfaces.Count);

                var allCandidates = huntResult.Candidates;
                if (allCandidates.Count > 0)
                {
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
    }
}
