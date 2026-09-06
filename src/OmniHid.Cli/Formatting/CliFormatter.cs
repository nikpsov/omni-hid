using System;
using System.Collections.Generic;
using System.Text;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;

namespace OmniHid.Cli.Formatting
{
    /// <summary>
    /// Formatter and console layout helper for CLI banners, tables, usage strings, and hex outputs.
    /// </summary>
    public static class CliFormatter
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Console UI Banners & Help
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Prints the ASCII art banner and application title to standard output.
        /// </summary>
        public static void PrintBanner()
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
        /// Displays available command-line actions, flags, and usage examples.
        /// </summary>
        public static void PrintHelp()
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
            Console.WriteLine("  [8] export    [filter]   Export device diagnostics (.md for GitHub Issue / AI spec & profile)");
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
            Console.WriteLine("  omni-hid 8 vgn                    (Export diagnostics and GitHub Issue report for VGN mouse)");
            Console.WriteLine("  omni-hid 4 25a7                   (Hunt battery telemetry on Ardor/Areson 0x25A7)");
            Console.WriteLine("  omni-hid sniff mouse              (Sniff live HID packets from mouse endpoints)");
            Console.WriteLine("  omni-hid sniff --timeout 60       (Sniff all devices for 60 seconds max)");
            Console.WriteLine("  omni-hid list --flat              (Flat table without device grouping)");
            Console.WriteLine("  omni-hid debug akko               (Inspect Akko wireless keyboard endpoints & probes)");
            Console.ResetColor();
            Console.WriteLine();
        }

        /// <summary>
        /// Prompts the user to press a key to return to the interactive menu or exit.
        /// </summary>
        /// <param name="interactiveMode">True if running in interactive session mode.</param>
        public static void SafeWaitForKey(bool interactiveMode = true)
        {
            if (!interactiveMode) return;
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
            catch
            {
                // Console read failure in non-interactive or redirected streams
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Filtering Helpers
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Tests if a peripheral device matches an optional filter query.
        /// </summary>
        public static bool MatchesFilter(IOmniDevice dev, string filter)
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
        public static bool MatchesFilter(HidDeviceInfo iface, string filter)
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
        public static bool MatchesFilterString(string haystack, string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            return haystack.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Formatting Helpers
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns a human-friendly string for standard USB HID Usage Page and Usage definitions.
        /// </summary>
        public static string FormatUsage(ushort page, ushort usage)
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
        public static string FormatDiffPositions(HashSet<int> positions)
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
        public static string GetCategoryIcon(DeviceCategory cat)
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
        public static string FormatHex(byte[] data, int maxBytes)
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
        public static string FormatHexFull(byte[] data)
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
        public static bool HasNonZeroData(byte[] buf)
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
