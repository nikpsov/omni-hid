using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;
using OmniHid.Core.Transport.Win32;
using OmniHid.Cli.Formatting;

namespace OmniHid.Cli.Commands
{
    /// <summary>
    /// Executes the live input packet sniffer and real-time differential telemetry capture engine.
    /// Captures raw packets, tracks byte-level deltas across frames, logs to disk, and monitors hotplug PID shifts.
    /// </summary>
    public static class SnifferCommand
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Configuration
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Gets or sets the sniffer auto-stop duration in seconds. 0 indicates manual termination.
        /// </summary>
        public static int TimeoutSeconds { get; set; }

        // ═══════════════════════════════════════════════════════════════════════
        // Command Execution
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Launches the live packet sniffer on endpoints matching the specified peripheral filter.
        /// </summary>
        /// <param name="filter">Optional name, VID, or PID filter string.</param>
        public static void Execute(string filter = null)
        {
            CliFormatter.PrintBanner();
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

                if (!DeviceSelector.SelectTargetDevice(transport, filter, out devName, out vid, out pid, out targetInterfaces, out profile, out targetPids))
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
                    if (TimeoutSeconds > 0)
                    {
                        dumpWriter.WriteLine(" Timeout: {0} seconds", TimeoutSeconds);
                    }
                    else
                    {
                        dumpWriter.WriteLine(" Timeout: Unlimited (manual stop)");
                    }
                    dumpWriter.WriteLine("═══════════════════════════════════════════════════════════════════════════\n");

                    // Section 1: Interface breakdown in dump file
                    dumpWriter.WriteLine("── 1. Enumerated Interface Endpoints ({0} total) ──", targetInterfaces.Count);
                    for (int i = 0; i < targetInterfaces.Count; i++)
                    {
                        var iface = targetInterfaces[i];
                        dumpWriter.WriteLine("Interface #{0}: Usage 0x{1:X4}:0x{2:X4} ({3})",
                            i + 1, iface.UsagePage, iface.Usage, CliFormatter.FormatUsage(iface.UsagePage, iface.Usage));
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
                            if (transport.GetFeatureReport(iface.DevicePath, fId, feat) && CliFormatter.HasNonZeroData(feat))
                            {
                                dumpWriter.WriteLine("  EP #{0} (0x{1:X4}:0x{2:X4}) Feature 0x{3:X2}: {4}",
                                    i + 1, iface.UsagePage, iface.Usage, fId, CliFormatter.FormatHex(feat, 32));
                            }
                        }
                    }
                    dumpWriter.WriteLine();

                    // Section 4: Live capture header
                    dumpWriter.WriteLine("── 4. Live Capture ──");

                    string timeoutDisplay;
                    if (TimeoutSeconds > 0)
                    {
                        timeoutDisplay = string.Format(" Auto-stop in {0} seconds (or press Enter/Escape to finish early).", TimeoutSeconds);
                    }
                    else
                    {
                        timeoutDisplay = " Press Enter or Escape to stop capture.";
                    }

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

                    List<HidOverlappedReader> readers = new List<HidOverlappedReader>();
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
                            var reader = new HidOverlappedReader(iface, handle, i + 1, (int)iface.InputReportByteLength);
                            if (reader.StartRead())
                            {
                                readers.Add(reader);
                                waitHandles.Add(reader.WaitEvent);
                                Console.ForegroundColor = ConsoleColor.DarkGreen;
                                Console.WriteLine("  [+] Endpoint #{0} (0x{1:X4}:0x{2:X4} - {3}): Listening for live packets.",
                                    i + 1, iface.UsagePage, iface.Usage, CliFormatter.FormatUsage(iface.UsagePage, iface.Usage));
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
                            if (TimeoutSeconds > 0 && (DateTime.UtcNow - captureStart).TotalSeconds >= TimeoutSeconds)
                            {
                                break;
                            }

                            bool hasKey = false;
                            try
                            {
                                hasKey = !Console.IsInputRedirected && Console.KeyAvailable;
                            }
                            catch
                            {
                                // Console input check unsupported in current host environment
                            }

                            if (hasKey)
                            {
                                ConsoleKeyInfo keyInfo = default(ConsoleKeyInfo);
                                try
                                {
                                    keyInfo = Console.ReadKey(true);
                                }
                                catch
                                {
                                    // Ignored
                                }

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

                            // Dynamic hotplug detection: check every 1500ms for connection changes
                            if ((DateTime.UtcNow - lastHotplugCheck).TotalMilliseconds >= 1500)
                            {
                                lastHotplugCheck = DateTime.UtcNow;
                                var liveInterfaces = DeviceSelector.ReEnumerateTargetInterfaces(transport, vid, targetPids);
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
                                            var newReader = new HidOverlappedReader(liveIface, h, newIdx, (int)liveIface.InputReportByteLength);
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

                            // Active telemetry pulse: periodically trigger response packets every 2.5s
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
                                        bytesTransferred, repId, CliFormatter.FormatHex(reader.Buffer, (int)bytesTransferred));

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
                        string diffStr = diffPos.Count > 0 ? CliFormatter.FormatDiffPositions(diffPos) : "none";
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

        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Stimulation Pulses
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Sends active protocol query pulses across candidate vendor feature/output endpoints during live sniffing.
        /// Elicits spontaneous telemetry response frames from command-driven peripherals.
        /// </summary>
        private static void SendSnifferTelemetryPulse(
            Win32HidTransport transport,
            List<HidOverlappedReader> readers,
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
    }
}
