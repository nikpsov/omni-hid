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

            using (var manager = new OmniManager(transport))
            {
                var allDevices = manager.ScanDevices();
                var targets = new List<IOmniDevice>();

                foreach (var d in allDevices)
                {
                    if (CliFormatter.MatchesFilter(d, filter))
                    {
                        targets.Add(d);
                    }
                }

                if (targets.Count >= 1)
                {
                    IOmniDevice td = null;
                    if (targets.Count == 1 || !interactiveMode)
                    {
                        td = targets[0];
                    }
                    else
                    {
                        Console.WriteLine("Detected {0} matching peripheral(s):", targets.Count);
                        for (int i = 0; i < targets.Count; i++)
                        {
                            Console.WriteLine("  [{0}] {1} (VID: 0x{2:X4}, PID: 0x{3:X4}, Endpoints: {4})",
                                i + 1, targets[i].Name, targets[i].VendorId, targets[i].ProductId, targets[i].Interfaces.Count);
                        }
                        Console.Write(string.Format("\nSelect device [1-{0}]: ", targets.Count));
                        string choice = Console.ReadLine();
                        int selIdx = 1;
                        if (!int.TryParse(choice != null ? choice.Trim() : "", out selIdx) || selIdx < 1 || selIdx > targets.Count)
                            selIdx = 1;

                        td = targets[selIdx - 1];
                    }

                    devName = td.Name;
                    vid = td.VendorId;
                    pid = td.ProductId;
                    targetInterfaces = new List<HidDeviceInfo>(td.Interfaces);

                    var omniDev = td as OmniDevice;
                    profile = omniDev != null ? omniDev.Profile : null;
                    if (profile == null)
                    {
                        profile = manager.Registry.FindProfile(vid, pid, devName);
                    }

                    if (profile != null && profile.ProductIds != null)
                    {
                        for (int i = 0; i < profile.ProductIds.Length; i++)
                        {
                            targetPids.Add(profile.ProductIds[i]);
                        }
                    }
                    targetPids.Add(pid);
                    return true;
                }

                // Fallback: search raw HID endpoints if not matched by OmniManager
                var allRaw = transport.Enumerate();
                var rawMatching = new List<HidDeviceInfo>();
                foreach (var r in allRaw)
                {
                    if (CliFormatter.MatchesFilter(r, filter)) rawMatching.Add(r);
                }

                if (rawMatching.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("No matching HID devices found for filter '{0}'.", filter ?? "");
                    Console.WriteLine("Tip: Run 'omni-hid list' (option [2]) to view all present hardware devices.");
                    Console.ResetColor();
                    return false;
                }

                // Group raw endpoints by VID:PID
                Dictionary<uint, List<HidDeviceInfo>> byVidPid = new Dictionary<uint, List<HidDeviceInfo>>();
                foreach (var r in rawMatching)
                {
                    uint key = ((uint)r.VendorId << 16) | r.ProductId;
                    List<HidDeviceInfo> list;
                    if (!byVidPid.TryGetValue(key, out list))
                    {
                        list = new List<HidDeviceInfo>();
                        byVidPid[key] = list;
                    }
                    list.Add(r);
                }

                List<List<HidDeviceInfo>> deviceGroups = new List<List<HidDeviceInfo>>(byVidPid.Values);
                List<HidDeviceInfo> chosen = null;

                if (deviceGroups.Count == 1 || !interactiveMode)
                {
                    chosen = deviceGroups[0];
                }
                else
                {
                    Console.WriteLine("Multiple device groups found:");
                    for (int i = 0; i < deviceGroups.Count; i++)
                    {
                        var g = deviceGroups[i];
                        string title = !string.IsNullOrEmpty(g[0].ProductString) ? g[0].ProductString : g[0].ManufacturerString ?? "Device";
                        Console.WriteLine("  [{0}] {1} (VID: 0x{2:X4}, PID: 0x{3:X4}, Endpoints: {4})",
                            i + 1, title, g[0].VendorId, g[0].ProductId, g.Count);
                    }
                    Console.Write(string.Format("\nSelect device group [1-{0}]: ", deviceGroups.Count));
                    string choice = Console.ReadLine();
                    int selIdx = 1;
                    if (!int.TryParse(choice != null ? choice.Trim() : "", out selIdx) || selIdx < 1 || selIdx > deviceGroups.Count)
                        selIdx = 1;

                    chosen = deviceGroups[selIdx - 1];
                }

                vid = chosen[0].VendorId;
                pid = chosen[0].ProductId;
                devName = !string.IsNullOrEmpty(chosen[0].ProductString) ? chosen[0].ProductString : "USB HID Device (0x" + vid.ToString("X4") + ")";
                targetInterfaces = chosen;

                profile = manager.Registry.FindProfile(vid, pid, devName);
                if (profile != null && profile.ProductIds != null)
                {
                    for (int i = 0; i < profile.ProductIds.Length; i++)
                    {
                        targetPids.Add(profile.ProductIds[i]);
                    }
                }
                targetPids.Add(pid);
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
