using System;
using System.Collections.Generic;
using System.Threading;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Diagnostics;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;
using OmniHid.Core.Transport.Win32;
using OmniHid.Cli.Formatting;

namespace OmniHid.Cli.Commands
{
    /// <summary>
    /// Executes the guided two-stage A-B battery and charger differential calibration engine.
    /// Captures baseline report snapshots while discharging on battery (State A) and charging via cable (State B),
    /// then performs bit-level delta analysis to isolate charging flags and battery indicators.
    /// </summary>
    public static class CalibrateCommand
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Command Execution
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Runs the interactive two-stage differential calibration wizard.
        /// </summary>
        /// <param name="filter">Optional name, VID, or PID filter string.</param>
        public static void Execute(string filter = null)
        {
            CliFormatter.PrintBanner();
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

                if (!DeviceSelector.SelectTargetDevice(transport, filter, out devName, out vid, out pid, out targetInterfaces, out profile, out targetPids))
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
                try { Console.ReadLine(); } catch { /* Ignore EOF or console input redirection */ }

                var interfacesA = DeviceSelector.ReEnumerateTargetInterfaces(transport, vid, targetPids);
                if (interfacesA.Count == 0) interfacesA = targetInterfaces;
                ushort pidA = interfacesA.Count > 0 ? interfacesA[0].ProductId : pid;

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("\n[State A Baseline] Active PID: 0x{0:X4} ({1} active endpoints).", pidA, interfacesA.Count);
                Console.WriteLine("Capturing State A report matrix (feature sweep, active queries, input reports)...");
                Console.ResetColor();
                var snapshotA = CalibrationEngine.CaptureSnapshot(transport, interfacesA, profile);
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
                try { Console.ReadLine(); } catch { /* Ignore EOF or console input redirection */ }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("\nRescanning bus for charging cable connection & PnP updates...");
                Console.ResetColor();

                List<HidDeviceInfo> interfacesB = DeviceSelector.ReEnumerateTargetInterfaces(transport, vid, targetPids);
                for (int retry = 0; retry < 5 && (interfacesB.Count == 0 || (interfacesB.Count > 0 && interfacesB[0].ProductId == pidA && targetPids.Count > 1)); retry++)
                {
                    Thread.Sleep(500);
                    var refreshed = DeviceSelector.ReEnumerateTargetInterfaces(transport, vid, targetPids);
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
                var snapshotB = CalibrationEngine.CaptureSnapshot(transport, interfacesB, profile);
                Console.WriteLine(" Captured: {0} Feature reports, {1} Input reports, {2} Spontaneous packets.\n",
                    snapshotB.FeatureReports.Count, snapshotB.InputReports.Count, snapshotB.SpontaneousReports.Count);

                // ── STAGE 3: Differential Engine Analysis ─────────────────────
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("============================================================================");
                Console.WriteLine("              DIFFERENTIAL CALIBRATION ANALYSIS (A vs B)                    ");
                Console.WriteLine("============================================================================");
                Console.ResetColor();

                AnalyzeAndPrintDiffs(snapshotA, snapshotB);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Differential Reporting
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Compares report buffers between snapshots and renders colorized differential diagnostics.
        /// </summary>
        private static void AnalyzeAndPrintDiffs(CalibrationSnapshot snapA, CalibrationSnapshot snapB)
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
    }
}
