using System;
using System.Collections.Generic;
using System.Threading;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Diagnostics
{
    /// <summary>
    /// Classification type for a candidate battery telemetry reading discovered during report sweeping.
    /// </summary>
    public enum CandidateKind
    {
        /// <summary>Direct 1-byte battery percentage value (5..100%).</summary>
        Percentage,

        /// <summary>2-byte little-endian battery voltage reading in millivolts (3000..4350 mV).</summary>
        VoltageLittleEndian,

        /// <summary>2-byte big-endian battery voltage reading in millivolts (3000..4350 mV).</summary>
        VoltageBigEndian
    }

    /// <summary>
    /// Represents a scored candidate battery reading discovered within raw HID report bytes.
    /// </summary>
    public class BatteryCandidate
    {
        /// <summary>Calculated priority score (higher score indicates higher confidence).</summary>
        public int Score { get; set; }

        /// <summary>Human-readable description of the candidate location and interpretation.</summary>
        public string Description { get; set; }

        /// <summary>Source label identifying the report and interface endpoint.</summary>
        public string SourceLabel { get; set; }

        /// <summary>Zero-based byte offset within the report buffer.</summary>
        public int ByteOffset { get; set; }

        /// <summary>Raw byte or word value extracted from the report.</summary>
        public int RawValue { get; set; }

        /// <summary>The interpretation category for this reading.</summary>
        public CandidateKind Kind { get; set; }

        /// <summary>Estimated battery percentage normalized to 0..100%.</summary>
        public int EstimatedPercentage { get; set; }

        /// <summary>Diagnostic hints (e.g. adjacent charging flags or vendor usage).</summary>
        public string Hints { get; set; }
    }

    /// <summary>
    /// Aggregated result of an automated battery hunter sweep across a peripheral's endpoints.
    /// </summary>
    public class BatteryHunterResult
    {
        /// <summary>Number of non-zero reports received from the device.</summary>
        public int ReportsReceived { get; set; }

        /// <summary>List of discovered candidate battery telemetry bytes, sorted by score descending.</summary>
        public List<BatteryCandidate> Candidates { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BatteryHunterResult"/> class.
        /// </summary>
        public BatteryHunterResult()
        {
            Candidates = new List<BatteryCandidate>();
        }
    }

    /// <summary>
    /// Diagnostic engine that probes HID Feature and Input reports across device endpoints
    /// to discover and rank potential battery telemetry bytes and charging flags.
    /// </summary>
    public static class BatteryHunter
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Report Sweeping Pipeline
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Executes a multi-phase battery discovery sweep across all interfaces of a peripheral.
        /// </summary>
        /// <param name="transport">Transport layer abstraction for HID communication.</param>
        /// <param name="interfaces">List of HID interfaces belonging to the target device.</param>
        /// <param name="progressCallback">Optional callback for status notifications during the sweep.</param>
        /// <returns>A populated <see cref="BatteryHunterResult"/> with ranked candidate findings.</returns>
        public static BatteryHunterResult Hunt(IHidTransport transport, List<HidDeviceInfo> interfaces, Action<string> progressCallback = null)
        {
            var result = new BatteryHunterResult();
            if (transport == null || interfaces == null || interfaces.Count == 0)
            {
                return result;
            }

            int reportsReceived = 0;
            var allCandidates = new List<BatteryCandidate>();

            // ── Phase 1: Feature Report Sweep (0x00 .. 0xFF) ─────────────────
            if (progressCallback != null) progressCallback("Sweeping Feature Reports (0x00 .. 0xFF)...");

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
                            InspectBuffer(featBuf, label, isVendorEp, allCandidates);
                        }
                    }
                }
            }

            // ── Phase 2: Input Report Probe (0x00 .. 0x20) ───────────────────
            if (progressCallback != null) progressCallback("Probing Input Reports (0x00 .. 0x20)...");

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
                            InspectBuffer(inBuf, label, isVendorEp, allCandidates);
                        }
                    }
                }
            }

            // ── Phase 3: Known Vendor Query Probes ────────────────────────────
            if (progressCallback != null) progressCallback("Testing Known Vendor Battery Query Sequences...");

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
                                    InspectBuffer(resp, label, isVendorEp, allCandidates);
                                }
                            }
                        }
                        catch
                        {
                            // Hardware endpoint may reject query command or disconnect during probe
                        }
                    }
                }
            }

            // Sort candidates by score descending
            allCandidates.Sort((a, b) => b.Score.CompareTo(a.Score));

            result.ReportsReceived = reportsReceived;
            result.Candidates = allCandidates;
            return result;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Heuristic Candidate Scoring
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Analyzes a payload buffer for plausible battery percentage (5..100) or voltage (3000..4350 mV).
        /// Appends scored candidate findings to <paramref name="candidates"/>.
        /// </summary>
        /// <param name="buffer">Raw report buffer.</param>
        /// <param name="sourceLabel">Descriptive origin label for the report.</param>
        /// <param name="isVendorEndpoint">True if the endpoint belongs to a vendor usage page (>= 0xFF00).</param>
        /// <param name="candidates">Target collection receiving candidate objects.</param>
        public static void InspectBuffer(byte[] buffer, string sourceLabel, bool isVendorEndpoint, List<BatteryCandidate> candidates)
        {
            if (buffer == null || buffer.Length < 2 || candidates == null) return;

            // 1. Percentage check (5% .. 100%)
            for (int i = 1; i < buffer.Length; i++)
            {
                byte val = buffer[i];
                if (val >= 5 && val <= 100)
                {
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

                    candidates.Add(new BatteryCandidate
                    {
                        Score = score,
                        Description = desc,
                        SourceLabel = sourceLabel,
                        ByteOffset = i,
                        RawValue = val,
                        Kind = CandidateKind.Percentage,
                        EstimatedPercentage = val,
                        Hints = hints
                    });
                }
            }

            // 2. Voltage check (3000 .. 4350 mV Li-ion battery range)
            for (int i = 1; i < buffer.Length - 1; i++)
            {
                // Little-Endian word
                ushort mvLe = (ushort)(buffer[i] | (buffer[i + 1] << 8));
                if (mvLe >= 3000 && mvLe <= 4350)
                {
                    int approxPct = Math.Max(0, Math.Min(100, (int)((mvLe - 3400) * 100 / 800)));
                    int score = 2;
                    if (isVendorEndpoint) score += 2;
                    if (i <= 4) score += 1;

                    string desc = string.Format("{0} -> Bytes[{1}..{2}] = {3} mV (LE) => Estimated: ~{4}%",
                        sourceLabel, i, i + 1, mvLe, approxPct);

                    candidates.Add(new BatteryCandidate
                    {
                        Score = score,
                        Description = desc,
                        SourceLabel = sourceLabel,
                        ByteOffset = i,
                        RawValue = mvLe,
                        Kind = CandidateKind.VoltageLittleEndian,
                        EstimatedPercentage = approxPct,
                        Hints = "Li-ion Voltage (LE)"
                    });
                }

                // Big-Endian word
                ushort mvBe = (ushort)((buffer[i] << 8) | buffer[i + 1]);
                if (mvBe >= 3000 && mvBe <= 4350)
                {
                    int approxPct = Math.Max(0, Math.Min(100, (int)((mvBe - 3400) * 100 / 800)));
                    int score = 2;
                    if (isVendorEndpoint) score += 2;
                    if (i <= 4) score += 1;

                    string desc = string.Format("{0} -> Bytes[{1}..{2}] = {3} mV (BE) => Estimated: ~{4}%",
                        sourceLabel, i, i + 1, mvBe, approxPct);

                    candidates.Add(new BatteryCandidate
                    {
                        Score = score,
                        Description = desc,
                        SourceLabel = sourceLabel,
                        ByteOffset = i,
                        RawValue = mvBe,
                        Kind = CandidateKind.VoltageBigEndian,
                        EstimatedPercentage = approxPct,
                        Hints = "Li-ion Voltage (BE)"
                    });
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Internal Helpers
        // ═══════════════════════════════════════════════════════════════════════

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
