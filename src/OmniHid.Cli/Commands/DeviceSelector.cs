using System;
using System.Collections.Generic;
using OmniHid.Cli.Formatting;
using OmniHid.Core;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Devices;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;
using OmniHid.Core.Transport.Win32;

namespace OmniHid.Cli.Commands
{
    /// <summary>
    /// Interactive device selector and interface re-enumeration helper for CLI diagnostic commands.
    /// </summary>
    public static class DeviceSelector
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Target Device Selection
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Selects a target peripheral device from consolidated devices or raw HID interface collections.
        /// Resolves the associated declarative profile and all sibling Product IDs for dual-mode devices.
        /// </summary>
        /// <param name="transport">Transport layer abstraction for device discovery.</param>
        /// <param name="filter">Optional user-specified filter string.</param>
        /// <param name="interactiveMode">True if the CLI is running interactively.</param>
        /// <param name="devName">Receives the resolved device model name.</param>
        /// <param name="vid">Receives the USB Vendor ID.</param>
        /// <param name="pid">Receives the active USB Product ID.</param>
        /// <param name="targetInterfaces">Receives the collection of HID interfaces belonging to the device.</param>
        /// <param name="profile">Receives the matched declarative profile, or null.</param>
        /// <param name="targetPids">Receives the set of all PIDs associated with this physical model.</param>
        /// <returns><c>true</c> if a target device was selected; otherwise, <c>false</c>.</returns>
        /// <summary>
        /// Represents a consolidated target peripheral candidate for diagnostic interaction.
        /// </summary>
        private sealed class TargetCandidate
        {
            public string Name { get; set; }
            public ushort VendorId { get; set; }
            public ushort ProductId { get; set; }
            public List<HidDeviceInfo> Interfaces { get; set; }
            public DeviceProfile Profile { get; set; }
            public HashSet<ushort> TargetPids { get; set; }
            public string SourceTag { get; set; }
        }

        /// <summary>
        /// Selects a target peripheral device from consolidated devices or raw HID interface collections.
        /// Resolves the associated declarative profile and all sibling Product IDs for dual-mode devices.
        /// </summary>
        /// <param name="transport">Transport layer abstraction for device discovery.</param>
        /// <param name="filter">Optional user-specified filter string.</param>
        /// <param name="interactiveMode">True if the CLI is running interactively.</param>
        /// <param name="devName">Receives the resolved device model name.</param>
        /// <param name="vid">Receives the USB Vendor ID.</param>
        /// <param name="pid">Receives the active USB Product ID.</param>
        /// <param name="targetInterfaces">Receives the collection of HID interfaces belonging to the device.</param>
        /// <param name="profile">Receives the matched declarative profile, or null.</param>
        /// <param name="targetPids">Receives the set of all PIDs associated with this physical model.</param>
        /// <returns><c>true</c> if a target device was selected; otherwise, <c>false</c>.</returns>
        public static bool SelectTargetDevice(
            Win32HidTransport transport,
            string filter,
            bool interactiveMode,
            out string devName,
            out ushort vid,
            out ushort pid,
            out List<HidDeviceInfo> targetInterfaces,
            out DeviceProfile profile,
            out HashSet<ushort> targetPids)
        {
            devName = "Unknown Device";
            vid = 0;
            pid = 0;
            targetInterfaces = new List<HidDeviceInfo>();
            profile = null;
            targetPids = new HashSet<ushort>();

            using (var manager = new OmniManager(transport, enableInternalWatcher: false))
            {
                var candidates = new List<TargetCandidate>();
                var coveredKeys = new HashSet<uint>();

                // 1. Discover all high-level logical devices resolved by OmniManager
                var allDevices = manager.ScanDevices();
                foreach (var d in allDevices)
                {
                    if (!CliFormatter.MatchesFilter(d, filter))
                    {
                        continue;
                    }

                    var pids = new HashSet<ushort>();
                    var omniDev = d as OmniDevice;
                    var prof = omniDev != null ? omniDev.Profile : null;
                    if (prof == null)
                    {
                        prof = manager.Registry.FindProfile(d.VendorId, d.ProductId, d.Name);
                    }

                    if (prof != null && prof.ProductIds != null)
                    {
                        for (int i = 0; i < prof.ProductIds.Length; i++)
                        {
                            pids.Add(prof.ProductIds[i]);
                        }
                    }
                    pids.Add(d.ProductId);

                    string tag;
                    if (prof != null && prof.IsRegisteredProfile)
                    {
                        tag = "Profile: " + (!string.IsNullOrEmpty(prof.ModelName) ? prof.ModelName : prof.ProtocolId);
                    }
                    else if (omniDev != null && omniDev.Category != DeviceCategory.Unknown)
                    {
                        tag = "Generic " + omniDev.Category;
                    }
                    else
                    {
                        tag = "Detected Peripheral";
                    }

                    candidates.Add(new TargetCandidate
                    {
                        Name = d.Name,
                        VendorId = d.VendorId,
                        ProductId = d.ProductId,
                        Interfaces = new List<HidDeviceInfo>(d.Interfaces),
                        Profile = prof,
                        TargetPids = pids,
                        SourceTag = tag
                    });

                    coveredKeys.Add(((uint)d.VendorId << 16) | d.ProductId);
                }

                // 2. Discover raw HID interface groups not covered by high-level devices
                var allRaw = transport.Enumerate();
                var rawMatching = new List<HidDeviceInfo>();
                foreach (var r in allRaw)
                {
                    if (CliFormatter.MatchesFilter(r, filter))
                    {
                        rawMatching.Add(r);
                    }
                }

                var byVidPid = new Dictionary<uint, List<HidDeviceInfo>>();
                foreach (var r in rawMatching)
                {
                    uint key = ((uint)r.VendorId << 16) | r.ProductId;
                    if (coveredKeys.Contains(key))
                    {
                        continue;
                    }

                    List<HidDeviceInfo> list;
                    if (!byVidPid.TryGetValue(key, out list))
                    {
                        list = new List<HidDeviceInfo>();
                        byVidPid[key] = list;
                    }
                    list.Add(r);
                }

                foreach (var kvp in byVidPid)
                {
                    var g = kvp.Value;
                    ushort gVid = g[0].VendorId;
                    ushort gPid = g[0].ProductId;
                    string gName = !string.IsNullOrEmpty(g[0].ProductString)
                        ? g[0].ProductString.Trim()
                        : (!string.IsNullOrEmpty(g[0].ManufacturerString)
                            ? g[0].ManufacturerString.Trim() + " HID Device"
                            : string.Format("USB HID Device (0x{0:X4}:0x{1:X4})", gVid, gPid));

                    var prof = manager.Registry.FindProfile(gVid, gPid, gName);
                    var pids = new HashSet<ushort>();
                    if (prof != null && prof.ProductIds != null)
                    {
                        for (int i = 0; i < prof.ProductIds.Length; i++)
                        {
                            pids.Add(prof.ProductIds[i]);
                        }
                    }
                    pids.Add(gPid);

                    string tag = (prof != null && prof.IsRegisteredProfile)
                        ? "Profile: " + (!string.IsNullOrEmpty(prof.ModelName) ? prof.ModelName : prof.ProtocolId)
                        : "Raw HID";

                    candidates.Add(new TargetCandidate
                    {
                        Name = gName,
                        VendorId = gVid,
                        ProductId = gPid,
                        Interfaces = g,
                        Profile = prof,
                        TargetPids = pids,
                        SourceTag = tag
                    });
                }

                if (candidates.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("No matching HID devices found for filter '{0}'.", filter ?? "");
                    Console.WriteLine("Tip: Run 'omni-hid list' (option [2]) to view all present hardware devices.");
                    Console.ResetColor();
                    return false;
                }

                TargetCandidate chosen = null;

                // Case A: Explicit user CLI filter matched exactly 1 device -> auto-select with confirmation print
                if (candidates.Count == 1 && !string.IsNullOrEmpty(filter))
                {
                    chosen = candidates[0];
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Target Device matched: {0} (VID: 0x{1:X4}, PID: 0x{2:X4})",
                        chosen.Name, chosen.VendorId, chosen.ProductId);
                    Console.ResetColor();
                }
                // Case B: Non-interactive headless execution
                else if (!interactiveMode)
                {
                    chosen = candidates[0];
                }
                // Case C: Single device detected without filter in interactive mode -> prompt user confirmation
                else if (candidates.Count == 1)
                {
                    Console.WriteLine("Detected 1 target peripheral in system:");
                    Console.WriteLine("  [1] {0} (VID: 0x{1:X4}, PID: 0x{2:X4}, Endpoints: {3}) [{4}]",
                        candidates[0].Name, candidates[0].VendorId, candidates[0].ProductId,
                        candidates[0].Interfaces.Count, candidates[0].SourceTag);
                    Console.WriteLine("  [0] Cancel / Return to menu\n");

                    Console.Write("Select device [1] (Press Enter to continue, 0 to cancel): ");
                    string input = Console.ReadLine();
                    string trimmed = input != null ? input.Trim().ToLowerInvariant() : "";

                    if (trimmed == "0" || trimmed == "q" || trimmed == "quit" || trimmed == "cancel" || trimmed == "exit")
                    {
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine("Operation cancelled by user.");
                        Console.ResetColor();
                        return false;
                    }

                    chosen = candidates[0];
                }
                // Case D: Multiple candidate devices detected -> interactive selection
                else
                {
                    Console.WriteLine(string.IsNullOrEmpty(filter)
                        ? string.Format("Detected {0} candidate peripheral(s):", candidates.Count)
                        : string.Format("Detected {0} candidate peripheral(s) matching '{1}':", candidates.Count, filter));

                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var c = candidates[i];
                        Console.WriteLine("  [{0}] {1} (VID: 0x{2:X4}, PID: 0x{3:X4}, Endpoints: {4}) [{5}]",
                            i + 1, c.Name, c.VendorId, c.ProductId, c.Interfaces.Count, c.SourceTag);
                    }
                    Console.WriteLine("  [0] Cancel / Return to menu\n");

                    while (true)
                    {
                        Console.Write("Select device [1-{0}] (default: 1, 0 to cancel): ", candidates.Count);
                        string input = Console.ReadLine();
                        string trimmed = input != null ? input.Trim().ToLowerInvariant() : "";

                        if (string.IsNullOrEmpty(trimmed) || trimmed == "1")
                        {
                            chosen = candidates[0];
                            break;
                        }

                        if (trimmed == "0" || trimmed == "q" || trimmed == "quit" || trimmed == "cancel" || trimmed == "exit")
                        {
                            Console.ForegroundColor = ConsoleColor.DarkYellow;
                            Console.WriteLine("Operation cancelled by user.");
                            Console.ResetColor();
                            return false;
                        }

                        int selIdx;
                        if (int.TryParse(trimmed, out selIdx) && selIdx >= 1 && selIdx <= candidates.Count)
                        {
                            chosen = candidates[selIdx - 1];
                            break;
                        }

                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("Invalid selection. Please enter a number between 1 and {0} (or 0 to cancel).", candidates.Count);
                        Console.ResetColor();
                    }
                }

                devName = chosen.Name;
                vid = chosen.VendorId;
                pid = chosen.ProductId;
                targetInterfaces = chosen.Interfaces;
                profile = chosen.Profile;
                targetPids = chosen.TargetPids;
                return true;
            }
        }

        /// <summary>
        /// Simplified overload for selecting a target device without returning profile or multi-PID set.
        /// </summary>
        public static bool SelectTargetDevice(
            Win32HidTransport transport,
            string filter,
            bool interactiveMode,
            out string devName,
            out ushort vid,
            out ushort pid,
            out List<HidDeviceInfo> targetInterfaces)
        {
            DeviceProfile dummyProfile;
            HashSet<ushort> dummyPids;
            return SelectTargetDevice(transport, filter, interactiveMode, out devName, out vid, out pid, out targetInterfaces, out dummyProfile, out dummyPids);
        }

        /// <summary>
        /// Default interactive overload for selecting a target device with full profile and PID resolution.
        /// </summary>
        public static bool SelectTargetDevice(
            Win32HidTransport transport,
            string filter,
            out string devName,
            out ushort vid,
            out ushort pid,
            out List<HidDeviceInfo> targetInterfaces,
            out DeviceProfile profile,
            out HashSet<ushort> targetPids)
        {
            return SelectTargetDevice(transport, filter, true, out devName, out vid, out pid, out targetInterfaces, out profile, out targetPids);
        }

        /// <summary>
        /// Default interactive overload for selecting a target device.
        /// </summary>
        public static bool SelectTargetDevice(
            Win32HidTransport transport,
            string filter,
            out string devName,
            out ushort vid,
            out ushort pid,
            out List<HidDeviceInfo> targetInterfaces)
        {
            return SelectTargetDevice(transport, filter, true, out devName, out vid, out pid, out targetInterfaces);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Re-Enumeration Helpers
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Re-enumerates all connected HID endpoints matching the target Vendor ID and any of the device's associated Product IDs.
        /// Handles dynamic connection transitions (e.g. 2.4G wireless dongle ↔ wired USB charging cable).
        /// </summary>
        public static List<HidDeviceInfo> ReEnumerateTargetInterfaces(
            Win32HidTransport transport,
            ushort vid,
            HashSet<ushort> allowedPids)
        {
            var allHid = transport.Enumerate();
            var matching = new List<HidDeviceInfo>();
            foreach (var iface in allHid)
            {
                if (iface.VendorId == vid)
                {
                    if (allowedPids == null || allowedPids.Count == 0 || allowedPids.Contains(iface.ProductId))
                    {
                        matching.Add(iface);
                    }
                }
            }
            return matching;
        }
    }
}
