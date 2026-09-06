using System;
using System.Collections.Generic;
using System.Threading;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Protocols;
using OmniHid.Core.Transport;
using OmniHid.Core.Transport.Win32;

namespace OmniHid.Core.Diagnostics
{
    /// <summary>
    /// Snapshot of all probed HID Feature, Input, and active query reports from a peripheral's endpoints.
    /// </summary>
    public class CalibrationSnapshot
    {
        /// <summary>Dictionary of captured Feature Reports keyed by endpoint and report ID.</summary>
        public Dictionary<string, byte[]> FeatureReports { get; private set; }

        /// <summary>Dictionary of captured Input Reports keyed by endpoint and report ID.</summary>
        public Dictionary<string, byte[]> InputReports { get; private set; }

        /// <summary>Dictionary of spontaneously received Input Reports keyed by endpoint index.</summary>
        public Dictionary<int, byte[]> SpontaneousReports { get; private set; }

        /// <summary>
        /// Initializes a new empty instance of the <see cref="CalibrationSnapshot"/> class.
        /// </summary>
        public CalibrationSnapshot()
        {
            FeatureReports = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            InputReports = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            SpontaneousReports = new Dictionary<int, byte[]>();
        }
    }

    /// <summary>
    /// Represents a detected byte difference between State A (discharging) and State B (charging) for a specific report.
    /// </summary>
    public class CalibrationReportDiff
    {
        /// <summary>Report classification label (e.g. Feature Report, Input Report, Active Query).</summary>
        public string ReportType { get; set; }

        /// <summary>Unique report key identifying endpoint and report ID.</summary>
        public string Key { get; set; }

        /// <summary>Report payload in State A (discharging baseline).</summary>
        public byte[] BufferA { get; set; }

        /// <summary>Report payload in State B (charging cable connected).</summary>
        public byte[] BufferB { get; set; }

        /// <summary>List of byte offsets that differed between State A and State B.</summary>
        public List<int> ChangedOffsets { get; set; }

        /// <summary>Diagnostic annotations and inferred flag meanings for changed byte offsets.</summary>
        public List<string> InferredFlags { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CalibrationReportDiff"/> class.
        /// </summary>
        public CalibrationReportDiff()
        {
            ChangedOffsets = new List<int>();
            InferredFlags = new List<string>();
        }
    }

    /// <summary>
    /// Aggregated result of a differential calibration comparison between discharging and charging device states.
    /// </summary>
    public class CalibrationResult
    {
        /// <summary>Discharging baseline state snapshot.</summary>
        public CalibrationSnapshot SnapshotA { get; set; }

        /// <summary>Charging state snapshot.</summary>
        public CalibrationSnapshot SnapshotB { get; set; }

        /// <summary>List of report differences detected between snapshots.</summary>
        public List<CalibrationReportDiff> Diffs { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CalibrationResult"/> class.
        /// </summary>
        public CalibrationResult()
        {
            Diffs = new List<CalibrationReportDiff>();
        }
    }

    /// <summary>
    /// Engine for capturing multi-state HID report matrices and isolating charging and battery bytes
    /// through differential A/B delta analysis.
    /// </summary>
    public static class CalibrationEngine
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Report Matrix Capture
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Captures a complete snapshot of all active Feature, Input, and active query packets from the target endpoints.
        /// </summary>
        /// <param name="transport">Transport layer abstraction for HID communication.</param>
        /// <param name="interfaces">List of HID interfaces belonging to the target device.</param>
        /// <param name="profile">Optional declarative profile for protocol-specific query probes.</param>
        /// <returns>A populated <see cref="CalibrationSnapshot"/>.</returns>
        public static CalibrationSnapshot CaptureSnapshot(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile = null)
        {
            var snap = new CalibrationSnapshot();
            if (transport == null || interfaces == null || interfaces.Count == 0)
            {
                return snap;
            }

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

        // ═══════════════════════════════════════════════════════════════════════
        // Differential Analysis Engine
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Analyzes byte differences between State A (discharging) and State B (charging) snapshots.
        /// Isolates probable charging flag transitions and battery level indicators.
        /// </summary>
        /// <param name="snapA">Snapshot captured while operating on battery.</param>
        /// <param name="snapB">Snapshot captured while connected to USB charger.</param>
        /// <returns>A populated <see cref="CalibrationResult"/> containing all discovered diffs.</returns>
        public static CalibrationResult AnalyzeDiff(CalibrationSnapshot snapA, CalibrationSnapshot snapB)
        {
            var result = new CalibrationResult
            {
                SnapshotA = snapA,
                SnapshotB = snapB
            };

            if (snapA == null || snapB == null) return result;

            // Compare Feature Reports
            CompareReportDictionary("Feature Report", snapA.FeatureReports, snapB.FeatureReports, result);

            // Compare Input Reports
            CompareReportDictionary("Input Report", snapA.InputReports, snapB.InputReports, result);

            // Compare Spontaneous Reports
            foreach (var kvp in snapA.SpontaneousReports)
            {
                int epIdx = kvp.Key;
                byte[] bufA = kvp.Value;
                byte[] bufB;
                if (snapB.SpontaneousReports.TryGetValue(epIdx, out bufB))
                {
                    CompareBufferPair("Spontaneous Input", string.Format("EP#{0}_Spontaneous", epIdx), bufA, bufB, result);
                }
            }

            return result;
        }

        private static void CompareReportDictionary(
            string reportType,
            Dictionary<string, byte[]> dictA,
            Dictionary<string, byte[]> dictB,
            CalibrationResult result)
        {
            var processedB = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Direct key match
            foreach (var kvp in dictA)
            {
                string key = kvp.Key;
                byte[] bufA = kvp.Value;
                byte[] bufB;
                if (dictB.TryGetValue(key, out bufB))
                {
                    processedB.Add(key);
                    CompareBufferPair(reportType, key, bufA, bufB, result);
                }
            }

            // 2. Cross-endpoint matching by report ID suffix (e.g. if endpoints reorder during plug-in)
            foreach (var kvp in dictA)
            {
                string keyA = kvp.Key;
                if (dictB.ContainsKey(keyA)) continue;

                int lastUnderA = keyA.LastIndexOf('_');
                string suffixA = lastUnderA >= 0 ? keyA.Substring(lastUnderA) : keyA;

                foreach (var kvpB in dictB)
                {
                    string keyB = kvpB.Key;
                    if (processedB.Contains(keyB)) continue;

                    int lastUnderB = keyB.LastIndexOf('_');
                    string suffixB = lastUnderB >= 0 ? keyB.Substring(lastUnderB) : keyB;

                    if (string.Equals(suffixA, suffixB, StringComparison.OrdinalIgnoreCase))
                    {
                        processedB.Add(keyB);
                        CompareBufferPair(reportType, string.Format("{0} -> {1}", keyA, keyB), kvp.Value, kvpB.Value, result);
                        break;
                    }
                }
            }
        }

        private static void CompareBufferPair(
            string reportType,
            string key,
            byte[] bufA,
            byte[] bufB,
            CalibrationResult result)
        {
            int len = Math.Min(bufA.Length, bufB.Length);
            var changedOffsets = new List<int>();
            for (int b = 0; b < len; b++)
            {
                if (bufA[b] != bufB[b])
                {
                    changedOffsets.Add(b);
                }
            }

            if (changedOffsets.Count == 0) return;

            var diff = new CalibrationReportDiff
            {
                ReportType = reportType,
                Key = key,
                BufferA = bufA,
                BufferB = bufB,
                ChangedOffsets = changedOffsets
            };

            // Analyze changed byte semantics
            foreach (int offset in changedOffsets)
            {
                byte valA = bufA[offset];
                byte valB = bufB[offset];

                // Check for charging flag transition: 0x00 -> 0x01 or 0x00 -> 0x02
                if (valA == 0x00 && (valB == 0x01 || valB == 0x02))
                {
                    diff.InferredFlags.Add(string.Format(
                        "Byte[{0}] transitioned 0x00 -> 0x{1:X2} (Strong candidate for Charging Flag)",
                        offset, valB));
                }
                else if (valA != 0 && valB != 0 && Math.Abs((int)valA - (int)valB) <= 5 && valA <= 100 && valB <= 100)
                {
                    diff.InferredFlags.Add(string.Format(
                        "Byte[{0}] shifted {1}% -> {2}% (Possible Battery Percentage gauge)",
                        offset, valA, valB));
                }
            }

            result.Diffs.Add(diff);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Internal Helpers
        // ═══════════════════════════════════════════════════════════════════════

        private static void ProbeAndCaptureActiveTelemetries(
            IHidTransport transport,
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

            List<KeyValuePair<string, byte[]>> queries = new List<KeyValuePair<string, byte[]>>();

            // 1. Areson Protocol Probe
            if (vid == 0x25A7 || string.Equals(protocol, "areson", StringComparison.OrdinalIgnoreCase))
            {
                byte[] aresonCmd = AresonProtocol.BuildQueryCommand(AresonProtocol.CMD_QUERY_STATUS);
                queries.Add(new KeyValuePair<string, byte[]>("Areson_0x08_0x04", aresonCmd));

                byte[] aresonUnnumbered = new byte[65];
                aresonUnnumbered[0] = 0x00;
                Array.Copy(aresonCmd, 0, aresonUnnumbered, 1, aresonCmd.Length);
                queries.Add(new KeyValuePair<string, byte[]>("Areson_Unnumbered_65B", aresonUnnumbered));
            }

            // 2. CompX / PixArt Probes
            if (vid == 0x24AE || string.Equals(protocol, "compx", StringComparison.OrdinalIgnoreCase))
            {
                queries.Add(new KeyValuePair<string, byte[]>("CompX_0x06", new byte[] { 0x06, 0x00, 0x00, 0x00 }));
                queries.Add(new KeyValuePair<string, byte[]>("CompX_0x04", new byte[] { 0x04, 0x02, 0x00, 0x00 }));
            }

            // 3. ROYUAN / YiChip Probes
            if (vid == 0x3151 || vid == 0x0461 || string.Equals(protocol, "royuan-keyboard", StringComparison.OrdinalIgnoreCase))
            {
                queries.Add(new KeyValuePair<string, byte[]>("Royuan_0x83_GetBattery", new byte[] { 0x00, 0x83, 0x00, 0x00 }));
                queries.Add(new KeyValuePair<string, byte[]>("Royuan_0x8F_GetInfo", new byte[] { 0x00, 0x8F, 0x00, 0x00 }));
            }

            // 4. SinoWealth Probes
            if (vid == 0x258A || string.Equals(protocol, "sinowealth", StringComparison.OrdinalIgnoreCase))
            {
                queries.Add(new KeyValuePair<string, byte[]>("SinoWealth_0x04", new byte[] { 0x04, 0x00, 0x00, 0x00 }));
                queries.Add(new KeyValuePair<string, byte[]>("SinoWealth_0x07", new byte[] { 0x07, 0x00, 0x00, 0x00 }));
            }

            // 5. Generic Query fallbacks
            queries.Add(new KeyValuePair<string, byte[]>("Generic_0x02", new byte[] { 0x02, 0x00, 0x00, 0x00 }));
            queries.Add(new KeyValuePair<string, byte[]>("Generic_0x01", new byte[] { 0x01, 0x00, 0x00, 0x00 }));

            foreach (var q in queries)
            {
                List<HidOverlappedReader> readers = new List<HidOverlappedReader>();
                List<WaitHandle> waitHandles = new List<WaitHandle>();

                try
                {
                    for (int i = 0; i < inputCandidates.Count; i++)
                    {
                        var inIface = inputCandidates[i];
                        int len = inIface.InputReportByteLength > 0 ? (int)inIface.InputReportByteLength : 64;
                        var r = transport.OpenOverlappedReader(inIface, len, 0);
                        if (r != null)
                        {
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

                        if (!transport.SetFeatureReport(fIface.DevicePath, sendBuf))
                        {
                            transport.WriteOutputReport(fIface.DevicePath, sendBuf);
                        }
                    }

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
                catch
                {
                    // Query response probe timeout or endpoint access failure
                }
                finally
                {
                    foreach (var r in readers) r.Dispose();
                }
            }
        }

        private static bool HasNonZeroData(byte[] buf)
        {
            if (buf == null || buf.Length == 0) return false;
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i] != 0) return true;
            }
            return false;
        }
    }
}
