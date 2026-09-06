using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using OmniHid.Core.Abstractions;

namespace OmniHid.Core.Profiles
{
    /// <summary>
    /// Lightweight, zero-dependency JSON parser for reading external device profiles from <c>devices/*.json</c>.
    /// Engineered specifically to allow standalone compilation via <c>csc.exe</c> without requiring third-party JSON libraries.
    /// </summary>
    public static class JsonProfileLoader
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Public Loader Methods
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Recursively searches the specified directory for JSON profile definitions and returns loaded instances.
        /// </summary>
        /// <param name="directoryPath">Root directory path to scan for *.json profile files.</param>
        /// <returns>List of parsed and validated <see cref="DeviceProfile"/> instances.</returns>
        public static List<DeviceProfile> LoadAllFromDirectory(string directoryPath)
        {
            var profiles = new List<DeviceProfile>();
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath)) return profiles;

            try
            {
                string[] files = Directory.GetFiles(directoryPath, "*.json", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    try
                    {
                        string content = File.ReadAllText(file, Encoding.UTF8);
                        DeviceProfile profile = ParseProfile(content);
                        if (profile != null && profile.VendorId > 0)
                        {
                            profile.IsCustomProfile = true;
                            profile.IsRegisteredProfile = true;
                            profiles.Add(profile);
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return profiles;
        }

        /// <summary>
        /// Parses a single JSON string into a <see cref="DeviceProfile"/> instance.
        /// Supports standard JSON as well as JSON with single-line comments (JSONC).
        /// </summary>
        /// <param name="json">Raw JSON content string.</param>
        /// <returns>A populated <see cref="DeviceProfile"/>, or null if input is empty.</returns>
        public static DeviceProfile ParseProfile(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            json = StripComments(json);

            string name = ExtractString(json, "model_name");
            if (string.IsNullOrEmpty(name)) name = ExtractString(json, "name");

            ushort vid = ExtractUshort(json, "vendor_id");
            if (vid == 0) vid = ExtractUshort(json, "vid");

            ushort[] pids = ExtractUshortArray(json, "product_ids");
            if (pids == null || pids.Length == 0)
            {
                ushort singlePid = ExtractUshort(json, "product_id");
                if (singlePid == 0) singlePid = ExtractUshort(json, "pid");
                if (singlePid > 0) pids = new ushort[] { singlePid };
            }

            string catStr = ExtractString(json, "category");
            DeviceCategory category = DeviceCategory.Unknown;
            if (!string.IsNullOrEmpty(catStr))
            {
                if (catStr.IndexOf("Mouse", StringComparison.OrdinalIgnoreCase) >= 0) category = DeviceCategory.Mouse;
                else if (catStr.IndexOf("Keyboard", StringComparison.OrdinalIgnoreCase) >= 0) category = DeviceCategory.Keyboard;
                else if (catStr.IndexOf("Headset", StringComparison.OrdinalIgnoreCase) >= 0) category = DeviceCategory.Headset;
                else if (catStr.IndexOf("Gamepad", StringComparison.OrdinalIgnoreCase) >= 0) category = DeviceCategory.Gamepad;
            }

            string protocol = ExtractString(json, "protocol");
            if (string.IsNullOrEmpty(protocol)) protocol = ExtractString(json, "protocol_id");

            double batteryLife = ExtractDouble(json, "battery_life_hours");
            if (batteryLife <= 0) batteryLife = ExtractDouble(json, "battery_hours");
            if (batteryLife <= 0) batteryLife = ExtractDouble(json, "rated_battery_hours");
            if (batteryLife <= 0) batteryLife = ExtractDouble(json, "endurance_hours");

            ushort targetUsagePage = ExtractUshort(json, "target_usage_page");
            if (targetUsagePage == 0) targetUsagePage = ExtractUshort(json, "usage_page");

            ushort targetUsage = ExtractUshort(json, "target_usage");
            if (targetUsage == 0) targetUsage = ExtractUshort(json, "usage");

            DeviceCapabilities capabilities = ExtractCapabilities(json, "capabilities");

            ushort[] wiredPids = ExtractUshortArray(json, "wired_product_ids");
            if (wiredPids == null || wiredPids.Length == 0)
            {
                wiredPids = ExtractUshortArray(json, "wired_pids");
            }
            if (wiredPids == null || wiredPids.Length == 0)
            {
                ushort singleWired = ExtractUshort(json, "wired_product_id");
                if (singleWired == 0) singleWired = ExtractUshort(json, "wired_pid");
                if (singleWired != 0) wiredPids = new ushort[] { singleWired };
            }

            return new DeviceProfile
            {
                ModelName = name ?? "Unknown Peripheral",
                VendorId = vid,
                ProductIds = pids ?? new ushort[0],
                WiredProductIds = wiredPids ?? new ushort[0],
                Category = category,
                ProtocolId = protocol ?? "generic",
                BatteryLifeHours = batteryLife,
                TargetUsagePage = targetUsagePage,
                TargetUsage = targetUsage,
                Capabilities = capabilities,
                IsRegisteredProfile = true
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Field Extraction Helpers
        // ═══════════════════════════════════════════════════════════════════════

        private static double ExtractDouble(string json, string key)
        {
            int keyIdx = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (keyIdx < 0) return 0;

            int colonIdx = json.IndexOf(':', keyIdx);
            if (colonIdx < 0) return 0;

            int endIdx = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, colonIdx + 1);
            if (endIdx < 0) endIdx = json.Length;

            string valStr = json.Substring(colonIdx + 1, endIdx - colonIdx - 1).Trim().Trim('"', ' ');
            double res;
            if (double.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out res))
                return res;
            return 0;
        }

        private static string ExtractString(string json, string key)
        {
            int keyIdx = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (keyIdx < 0) return null;

            int colonIdx = json.IndexOf(':', keyIdx);
            if (colonIdx < 0) return null;

            int quoteStart = json.IndexOf('"', colonIdx + 1);
            if (quoteStart < 0) return null;

            int quoteEnd = json.IndexOf('"', quoteStart + 1);
            if (quoteEnd < 0) return null;

            return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1).Trim();
        }

        private static ushort ExtractUshort(string json, string key)
        {
            int keyIdx = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (keyIdx < 0) return 0;

            int colonIdx = json.IndexOf(':', keyIdx);
            if (colonIdx < 0) return 0;

            int endIdx = json.IndexOfAny(new char[] { ',', '}', '\r', '\n' }, colonIdx + 1);
            if (endIdx < 0) endIdx = json.Length;

            string valStr = json.Substring(colonIdx + 1, endIdx - colonIdx - 1).Trim().Trim('"', ' ');
            return ParseHexOrDec(valStr);
        }

        private static ushort[] ExtractUshortArray(string json, string key)
        {
            int keyIdx = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (keyIdx < 0) return null;

            int bracketStart = json.IndexOf('[', keyIdx);
            if (bracketStart < 0) return null;

            int bracketEnd = json.IndexOf(']', bracketStart + 1);
            if (bracketEnd < 0) return null;

            string arrayContent = json.Substring(bracketStart + 1, bracketEnd - bracketStart - 1).Trim();
            if (string.IsNullOrEmpty(arrayContent)) return new ushort[0];

            string[] tokens = arrayContent.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            List<ushort> results = new List<ushort>();
            foreach (var token in tokens)
            {
                string clean = token.Trim().Trim('"', ' ');
                ushort val = ParseHexOrDec(clean);
                if (val > 0) results.Add(val);
            }
            return results.ToArray();
        }

        private static ushort ParseHexOrDec(string str)
        {
            if (string.IsNullOrEmpty(str)) return 0;
            ushort res;
            if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (ushort.TryParse(str.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out res))
                    return res;
            }
            if (ushort.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out res))
                return res;
            return 0;
        }

        /// <summary>
        /// Extracts and parses telemetry capability flags from a JSON array into bitwise <see cref="DeviceCapabilities"/>.
        /// </summary>
        /// <param name="json">Raw sanitized JSON content.</param>
        /// <param name="key">Array property key name.</param>
        /// <returns>Parsed bitwise <see cref="DeviceCapabilities"/> flags, or default battery/charging flags if omitted.</returns>
        private static DeviceCapabilities ExtractCapabilities(string json, string key)
        {
            int keyIdx = json.IndexOf("\"" + key + "\"", StringComparison.OrdinalIgnoreCase);
            if (keyIdx < 0) return DeviceCapabilities.BatteryLevel | DeviceCapabilities.ChargingStatus;

            int bracketStart = json.IndexOf('[', keyIdx);
            if (bracketStart < 0) return DeviceCapabilities.BatteryLevel | DeviceCapabilities.ChargingStatus;

            int bracketEnd = json.IndexOf(']', bracketStart + 1);
            if (bracketEnd < 0) return DeviceCapabilities.BatteryLevel | DeviceCapabilities.ChargingStatus;

            string arrayContent = json.Substring(bracketStart + 1, bracketEnd - bracketStart - 1).Trim();
            if (string.IsNullOrEmpty(arrayContent)) return DeviceCapabilities.None;

            string[] tokens = arrayContent.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            DeviceCapabilities caps = DeviceCapabilities.None;

            foreach (var token in tokens)
            {
                string clean = token.Trim().Trim('"', ' ');
                DeviceCapabilities parsed;
                if (Enum.TryParse(clean, true, out parsed))
                {
                    caps |= parsed;
                }
            }

            return caps == DeviceCapabilities.None
                ? (DeviceCapabilities.BatteryLevel | DeviceCapabilities.ChargingStatus)
                : caps;
        }

        /// <summary>
        /// Strips single-line (//) comments from raw JSON text to enable JSONC support.
        /// </summary>
        private static string StripComments(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();
            foreach (var line in lines)
            {
                int commentIdx = line.IndexOf("//", StringComparison.Ordinal);
                if (commentIdx >= 0)
                {
                    sb.AppendLine(line.Substring(0, commentIdx));
                }
                else
                {
                    sb.AppendLine(line);
                }
            }
            return sb.ToString();
        }
    }
}