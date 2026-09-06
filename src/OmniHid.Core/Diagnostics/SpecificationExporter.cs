using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Diagnostics
{
    /// <summary>
    /// Generates structured hardware specifications, markdown telemetry dumps,
    /// GitHub Issue diagnostic bundles, and AI-ready reverse engineering prompts for peripherals.
    /// Supports both verified/working devices and unsupported devices requiring protocol analysis.
    /// </summary>
    public static class SpecificationExporter
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Markdown Specification & Issue Report Export
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Generates a comprehensive markdown specification and GitHub Issue bundle for the given peripheral.
        /// Captures live battery telemetry, protocol assignments, declarative profiles, endpoint topologies,
        /// and active hardware probe results.
        /// </summary>
        /// <param name="transport">Active HID transport layer for querying feature and input reports.</param>
        /// <param name="devName">Device model display name.</param>
        /// <param name="vid">USB Vendor ID.</param>
        /// <param name="pid">USB Product ID.</param>
        /// <param name="interfaces">List of HID interfaces belonging to the device.</param>
        /// <param name="outputFilePath">Target filesystem destination path for the markdown document.</param>
        /// <param name="device">Optional resolved <see cref="IOmniDevice"/> instance with live telemetry.</param>
        /// <param name="profile">Optional matched declarative <see cref="DeviceProfile"/>.</param>
        public static void ExportMarkdownSpecification(
            IHidTransport transport,
            string devName,
            ushort vid,
            ushort pid,
            List<HidDeviceInfo> interfaces,
            string outputFilePath,
            IOmniDevice device = null,
            DeviceProfile profile = null)
        {
            if (string.IsNullOrEmpty(outputFilePath)) throw new ArgumentNullException("outputFilePath");

            var fp = IcFingerprinter.Identify(vid, pid, interfaces, devName);

            // Extract manufacturer string and product string from endpoints if available
            string mfr = "";
            string prod = "";
            if (interfaces != null)
            {
                foreach (var iface in interfaces)
                {
                    if (string.IsNullOrEmpty(mfr) && !string.IsNullOrEmpty(iface.ManufacturerString))
                        mfr = iface.ManufacturerString.Trim();
                    if (string.IsNullOrEmpty(prod) && !string.IsNullOrEmpty(iface.ProductString))
                        prod = iface.ProductString.Trim();
                }
            }

            // Determine telemetry and support status
            int battLevel = -1;
            string battState = "Unknown";
            int voltageMv = 0;
            string statusMsg = "Not queried";
            string protocolId = "generic-peripheral";
            string categoryStr = "Mouse";
            bool isCharging = false;
            bool isWired = false;

            if (device != null)
            {
                categoryStr = device.Category.ToString();
                protocolId = !string.IsNullOrEmpty(device.ProtocolId) ? device.ProtocolId : "generic-peripheral";
                isWired = device.IsWired;

                var tel = device.Telemetry;
                if (tel != null)
                {
                    if (tel.IsAvailable)
                    {
                        battLevel = tel.LevelPercent;
                    }
                    battState = tel.StateDescription ?? "Unknown";
                    voltageMv = tel.VoltageMv;
                    statusMsg = tel.StatusMessage ?? "";
                    isCharging = tel.IsCharging;
                    if (tel.IsWired) isWired = true;
                }
            }
            else if (profile != null)
            {
                categoryStr = profile.Category.ToString();
                protocolId = !string.IsNullOrEmpty(profile.ProtocolId) ? profile.ProtocolId : "generic-peripheral";
            }

            // Probe Windows PnP Battery level (DEVPKEY_Device_BatteryLevel)
            int pnpBatt = -1;
            if (transport != null && interfaces != null)
            {
                foreach (var iface in interfaces)
                {
                    int b = transport.GetPnpBatteryLevel(iface.DevicePath);
                    if (b >= 0)
                    {
                        pnpBatt = b;
                        break;
                    }
                }
            }

            bool isSupported = battLevel >= 0 || pnpBatt >= 0;
            bool hasKnownProtocol = !string.IsNullOrEmpty(protocolId) &&
                                    protocolId != "generic-peripheral" &&
                                    protocolId != "generic-keyboard";
            bool hasProfile = (device != null && (device.IsCustomProfile || device.IsRegisteredProfile)) ||
                              (profile != null && (profile.IsCustomProfile || profile.IsRegisteredProfile));

            using (var sw = new StreamWriter(outputFilePath, false, Encoding.UTF8))
            {
                sw.WriteLine("# Hardware Telemetry Specification & Device Report");
                sw.WriteLine();
                sw.WriteLine("## {0} (`0x{1:X4}:0x{2:X4}`)", devName, vid, pid);
                sw.WriteLine();
                sw.WriteLine("> **Generated by:** OmniHID Universal Peripheral Telemetry Engine on {0:yyyy-MM-dd HH:mm:ss}", DateTime.Now);
                sw.WriteLine("> **Device Status:** {0}", isSupported ? "Supported / Battery Telemetry Available" : "Diagnostics Captured / Telemetry Inactive or Unsupported");
                sw.WriteLine();

                // ═════════════════════════════════════════════════════════════════
                // Section 1: GitHub Issue Quick-Copy Section
                // ═════════════════════════════════════════════════════════════════
                sw.WriteLine("---");
                sw.WriteLine("## 1. GitHub Issue Quick-Copy Section");
                sw.WriteLine("*Copy and paste the checklist and device block below directly into your GitHub Issue at:*");
                sw.WriteLine("*https://github.com/nikpsov/omni-hid/issues/new?template=new_device.md*");
                sw.WriteLine();
                sw.WriteLine("```markdown");
                sw.WriteLine("### Device Information");
                sw.WriteLine("- **Manufacturer / Brand:** {0}", !string.IsNullOrEmpty(mfr) ? mfr : "Unknown");
                sw.WriteLine("- **Model Name:** {0}", devName);
                sw.WriteLine("- **Device Category:** {0}", categoryStr);
                sw.WriteLine("- **Connection Mode:** {0}", isWired ? "USB Wired Cable" : "2.4GHz Wireless Dongle / Bluetooth");
                sw.WriteLine("- **USB Vendor ID (VID):** `0x{0:X4}`", vid);
                sw.WriteLine("- **USB Product ID (PID):** `0x{0:X4}`", pid);
                sw.WriteLine();
                sw.WriteLine("### Support & Telemetry Status");
                sw.WriteLine("- [{0}] **Supported**: Battery level detected ({1})",
                    isSupported ? "x" : " ",
                    battLevel >= 0 ? string.Format("{0}%", battLevel) : (pnpBatt >= 0 ? string.Format("{0}% (PnP)", pnpBatt) : "Unavailable"));
                sw.WriteLine("- [{0}] **Charging State Detected**: {1}",
                    isCharging ? "x" : (isSupported ? "x" : " "),
                    battState);
                sw.WriteLine("- [{0}] **Protocol Identified**: `{1}`",
                    hasKnownProtocol ? "x" : " ",
                    protocolId);
                sw.WriteLine("- [{0}] **Declarative Profile Ready**: {1}",
                    hasProfile ? "x" : " ",
                    hasProfile ? (profile != null ? profile.ModelName : "Verified Profile in devices/") : "None (Candidate JSON generated below)");
                sw.WriteLine("- [{0}] **Unsupported / Work-in-Progress**: {1}",
                    !isSupported ? "x" : " ",
                    !isSupported ? "Device detected, but battery level is unavailable" : "No (Supported)");
                sw.WriteLine("```");
                sw.WriteLine();

                // ═════════════════════════════════════════════════════════════════
                // Section 2: Live Telemetry & Device Overview
                // ═════════════════════════════════════════════════════════════════
                sw.WriteLine("## 2. Device Overview & Live Telemetry Snapshot");
                sw.WriteLine();
                sw.WriteLine("| Property | Value |");
                sw.WriteLine("|---|---|");
                sw.WriteLine("| **Model Name** | {0} |", devName);
                sw.WriteLine("| **USB Vendor ID** | `0x{0:X4}` |", vid);
                sw.WriteLine("| **USB Product ID** | `0x{0:X4}` |", pid);
                sw.WriteLine("| **Peripheral Category** | {0} |", categoryStr);
                sw.WriteLine("| **Assigned Protocol** | `{0}` |", protocolId);
                sw.WriteLine("| **Support Status** | {0} |", isSupported ? "**Supported**" : "*Unsupported / Telemetry Inactive*");
                sw.WriteLine("| **Current Battery Level** | {0} |", battLevel >= 0 ? string.Format("**{0}%**", battLevel) : "*Unavailable*");
                sw.WriteLine("| **Battery State** | {0} |", battState);
                if (voltageMv > 0)
                {
                    sw.WriteLine("| **Battery Voltage** | {0} mV |", voltageMv);
                }
                if (pnpBatt >= 0)
                {
                    sw.WriteLine("| **Windows PnP Battery** | {0}% (`DEVPKEY_Device_BatteryLevel`) |", pnpBatt);
                }
                sw.WriteLine("| **Active Connection** | {0} |", isWired ? "Direct USB Wired" : "Wireless (Dongle / Bluetooth)");
                sw.WriteLine("| **Telemetry Status Message** | {0} |", !string.IsNullOrEmpty(statusMsg) ? statusMsg : "OK");
                sw.WriteLine("| **Total HID Endpoints** | {0} |", interfaces != null ? interfaces.Count : 0);
                sw.WriteLine();

                // ═════════════════════════════════════════════════════════════════
                // Section 3: IC Fingerprint & Architecture
                // ═════════════════════════════════════════════════════════════════
                sw.WriteLine("## 3. Controller IC Architecture & Fingerprint");
                sw.WriteLine();
                sw.WriteLine("| Property | Value |");
                sw.WriteLine("|---|---|");
                sw.WriteLine("| **Identified IC Family** | {0} |", fp.ChipsetFamily);
                sw.WriteLine("| **Identification Confidence** | {0} |", fp.Confidence);
                sw.WriteLine("| **Non-Battery Hardware?** | {0} |", fp.IsNonBatteryDevice ? "Yes (Skip battery probing)" : "No (Battery device)");
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

                // ═════════════════════════════════════════════════════════════════
                // Section 4: HID Endpoints Table
                // ═════════════════════════════════════════════════════════════════
                sw.WriteLine("## 4. HID Interface Collections (Endpoints)");
                sw.WriteLine();
                sw.WriteLine("| # | Usage Page | Usage | InLen | OutLen | FeatLen | Path |");
                sw.WriteLine("|---|---|---|---|---|---|---|");

                if (interfaces != null)
                {
                    for (int i = 0; i < interfaces.Count; i++)
                    {
                        var ep = interfaces[i];
                        sw.WriteLine("| {0} | `0x{1:X4}` | `0x{2:X4}` | {3}B | {4}B | {5}B | `{6}` |",
                            i + 1, ep.UsagePage, ep.Usage,
                            ep.InputReportByteLength, ep.OutputReportByteLength, ep.FeatureReportByteLength, ep.DevicePath);
                    }
                }
                sw.WriteLine();

                // ═════════════════════════════════════════════════════════════════
                // Section 5: Diagnostic Probes (Feature & Input Reports)
                // ═════════════════════════════════════════════════════════════════
                sw.WriteLine("## 5. Diagnostic Probes & Hardware Responses");
                sw.WriteLine();

                byte[] candidateFeatureIds = new byte[] {
                    0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C,
                    0x10, 0x11, 0x12, 0x20, 0x80, 0x81, 0x82, 0x83, 0x84, 0x8F
                };

                int respondedFeatCount = 0;
                if (transport != null && interfaces != null)
                {
                    for (int i = 0; i < interfaces.Count; i++)
                    {
                        var ep = interfaces[i];
                        int featLen = Math.Max(64, ep.FeatureReportByteLength > 0 ? (int)ep.FeatureReportByteLength : 64);

                        foreach (byte fid in candidateFeatureIds)
                        {
                            byte[] buf = new byte[featLen];
                            if (transport.GetFeatureReport(ep.DevicePath, fid, buf) && HasNonZeroData(buf))
                            {
                                respondedFeatCount++;
                                sw.WriteLine("### EP #{0} (`0x{1:X4}:0x{2:X4}`) - Responding Feature Report `0x{3:X2}`",
                                    i + 1, ep.UsagePage, ep.Usage, fid);
                                sw.WriteLine("```");
                                sw.WriteLine(FormatHex(buf, 32));
                                sw.WriteLine("```");
                                sw.WriteLine();
                            }
                        }
                    }
                }

                if (respondedFeatCount == 0)
                {
                    sw.WriteLine("*No non-zero Feature Reports responded to standard polled queries.*");
                    sw.WriteLine();
                }

                // Input Report Probes
                byte[] candidateInputIds = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x08, 0x09 };
                int respondedInCount = 0;
                if (transport != null && interfaces != null)
                {
                    for (int i = 0; i < interfaces.Count; i++)
                    {
                        var ep = interfaces[i];
                        int inLen = Math.Max(64, ep.InputReportByteLength > 0 ? (int)ep.InputReportByteLength : 64);

                        foreach (byte iid in candidateInputIds)
                        {
                            byte[] inBuf = new byte[inLen];
                            if (transport.GetInputReport(ep.DevicePath, iid, inBuf) && HasNonZeroData(inBuf))
                            {
                                respondedInCount++;
                                sw.WriteLine("### EP #{0} (`0x{1:X4}:0x{2:X4}`) - Responding Input Report `0x{3:X2}`",
                                    i + 1, ep.UsagePage, ep.Usage, iid);
                                sw.WriteLine("```");
                                sw.WriteLine(FormatHex(inBuf, 32));
                                sw.WriteLine("```");
                                sw.WriteLine();
                            }
                        }
                    }
                }

                // ═════════════════════════════════════════════════════════════════
                // Section 6: Next Steps, JSON Profile & AI Generator
                // ═════════════════════════════════════════════════════════════════
                sw.WriteLine("---");
                sw.WriteLine("## 6. Next Steps & Artifacts");
                sw.WriteLine();

                string safeModelFile = GenerateSafeFileName(devName, vid, pid);

                if (isSupported || hasKnownProtocol)
                {
                    sw.WriteLine("### Candidate Declarative Profile (`devices/{0}.json`)", safeModelFile);
                    sw.WriteLine("Since this peripheral is recognized by an existing OmniHID protocol driver,");
                    sw.WriteLine("you can officially add it to the device database by adding this JSON profile:");
                    sw.WriteLine();
                    sw.WriteLine("```json");
                    sw.WriteLine("{");
                    sw.WriteLine("  \"modelName\": \"{0}\",", EscapeJson(devName));
                    sw.WriteLine("  \"vendorId\": \"0x{0:X4}\",", vid);
                    sw.WriteLine("  \"productIds\": [");
                    sw.WriteLine("    \"0x{0:X4}\"", pid);
                    sw.WriteLine("  ],");
                    sw.WriteLine("  \"category\": \"{0}\",", categoryStr);
                    sw.WriteLine("  \"protocolId\": \"{0}\",", protocolId);
                    sw.WriteLine("  \"capabilities\": [");
                    sw.WriteLine("    \"BatteryPercentage\",");
                    sw.WriteLine("    \"ChargingStatus\"");
                    sw.WriteLine("  ],");
                    sw.WriteLine("  \"batteryLifeHours\": 70.0");
                    sw.WriteLine("}");
                    sw.WriteLine("```");
                    sw.WriteLine();
                }

                sw.WriteLine("### AI Assistant Reverse-Engineering Prompt");
                sw.WriteLine("If battery telemetry is incomplete or requires custom parsing, copy this prompt into an LLM (Gemini, Claude, ChatGPT):");
                sw.WriteLine();
                sw.WriteLine("````markdown");
                sw.WriteLine("I am developing an open-source C# library called OmniHID (Windows USB HID Peripheral Telemetry).");
                sw.WriteLine("Here is the hardware specification dump for a peripheral:");
                sw.WriteLine("- Device Name: {0}", devName);
                sw.WriteLine("- Vendor ID: 0x{0:X4}", vid);
                sw.WriteLine("- Product ID: 0x{0:X4}", pid);
                sw.WriteLine("- Category: {0}", categoryStr);
                sw.WriteLine("- Current Protocol: {0}", protocolId);
                sw.WriteLine("- Architecture: {0}", fp.ChipsetFamily);
                sw.WriteLine("- Recommended Approach: {0}", fp.RecommendedApproach);
                if (isSupported)
                {
                    sw.WriteLine("- Current Battery: {0}% ({1})", battLevel, battState);
                }
                sw.WriteLine();
                sw.WriteLine("Based on the endpoint topology, Feature Report dumps, and Input Reports in this specification:");
                sw.WriteLine("1. Verify or implement the C# class implementing `IProtocolHandler` for OmniHID.");
                sw.WriteLine("2. Generate the declarative JSON profile for `devices/{0}.json`.", safeModelFile);
                sw.WriteLine("3. Accurately handle battery level percentage, charging status flags, and sleep/offline detection.");
                sw.WriteLine("````");
                sw.WriteLine();
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

        private static string FormatHex(byte[] data, int maxBytes)
        {
            if (data == null || data.Length == 0) return "";
            int len = Math.Min(data.Length, maxBytes);
            var sb = new StringBuilder();
            for (int i = 0; i < len; i++)
            {
                sb.Append(data[i].ToString("X2"));
                if (i < len - 1) sb.Append(" ");
            }
            if (data.Length > maxBytes) sb.Append(" ...");
            return sb.ToString();
        }

        private static string GenerateSafeFileName(string devName, ushort vid, ushort pid)
        {
            if (string.IsNullOrEmpty(devName)) return string.Format("device_{0:x4}_{1:x4}", vid, pid);
            var sb = new StringBuilder();
            foreach (char c in devName.ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                    sb.Append(c);
                else if (c == ' ' || c == '-' || c == '_')
                    sb.Append('_');
            }
            string s = sb.ToString().Trim('_');
            while (s.Contains("__")) s = s.Replace("__", "_");
            if (string.IsNullOrEmpty(s)) s = string.Format("device_{0:x4}_{1:x4}", vid, pid);
            return s;
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
