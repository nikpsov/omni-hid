using System;
using System.Collections.Generic;
using OmniHid.Cli.Formatting;
using OmniHid.Core.Transport;
using OmniHid.Core.Transport.Win32;

namespace OmniHid.Cli.Commands
{
    /// <summary>
    /// Implements the 'list' command, displaying all system HID interfaces, usage pages, and report buffer capacities.
    /// </summary>
    public static class ListCommand
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Gets or sets a value indicating whether flat list mode is globally enabled.
        /// </summary>
        public static bool FlatMode { get; set; }

        // ═══════════════════════════════════════════════════════════════════════
        // Command Execution
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Executes the 'list' command.
        /// </summary>
        /// <param name="filter">Optional substring to filter by VID, PID, manufacturer, or product name.</param>
        /// <param name="flatListMode">True to display a flat tabular view without grouping by physical device.</param>
        public static void Execute(string filter = null, bool flatListMode = false)
        {
            if (FlatMode) flatListMode = true;
            CliFormatter.PrintBanner();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(string.IsNullOrEmpty(filter)
                ? "Scanning all connected USB HID device interfaces in system..."
                : string.Format("Scanning USB HID device interfaces matching '{0}'...", filter));
            if (flatListMode)
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
                    if (CliFormatter.MatchesFilter(d, filter))
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

                if (flatListMode)
                {
                    PrintListFlat(transport, matched);
                }
                else
                {
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

        // ═══════════════════════════════════════════════════════════════════════
        // Table Rendering
        // ═══════════════════════════════════════════════════════════════════════

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

        private static void PrintListGrouped(Win32HidTransport transport, List<HidDeviceInfo> matched)
        {
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

        private static void PrintListInterfaceRow(Win32HidTransport transport, HidDeviceInfo d, bool indented)
        {
            string prefix = indented ? "    " : "";

            string mfr = (d.ManufacturerString ?? "").Trim();
            string prod = (d.ProductString ?? "").Trim();
            string devTitle = string.IsNullOrEmpty(prod) ? mfr : prod;
            if (devTitle.Length > 22) devTitle = devTitle.Substring(0, 19) + "...";

            string usageDesc = CliFormatter.FormatUsage(d.UsagePage, d.Usage);
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
    }
}
