using System;
using System.Collections.Generic;
using System.IO;
using OmniHid.Core;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Diagnostics;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;
using OmniHid.Core.Transport.Win32;
using OmniHid.Cli.Formatting;

namespace OmniHid.Cli.Commands
{
    /// <summary>
    /// Executes the peripheral diagnostic, GitHub Issue bundle, and AI protocol specification exporter.
    /// Gathers live battery telemetry, hardware endpoint topologies, IC controller fingerprints,
    /// and generates a ready-to-use markdown report for GitHub issue submission or AI reverse engineering.
    /// </summary>
    public static class ExportCommand
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Command Execution
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Generates a markdown diagnostic specification and issue report for the device matching the specified filter.
        /// </summary>
        /// <param name="filter">Optional name, VID, or PID filter string.</param>
        public static void Execute(string filter = null)
        {
            CliFormatter.PrintBanner();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("============================================================================");
            Console.WriteLine("   OmniHID Device Diagnostics, Issue Report & AI Specification Exporter    ");
            Console.WriteLine("============================================================================");
            Console.ResetColor();
            Console.WriteLine();

            using (var transport = new Win32HidTransport())
            using (var manager = new OmniManager(transport))
            {
                string devName;
                ushort vid;
                ushort pid;
                List<HidDeviceInfo> targetInterfaces;
                DeviceProfile profile;
                HashSet<ushort> targetPids;

                if (!DeviceSelector.SelectTargetDevice(
                    transport,
                    filter,
                    true,
                    out devName,
                    out vid,
                    out pid,
                    out targetInterfaces,
                    out profile,
                    out targetPids))
                {
                    return;
                }

                // Resolve matching IOmniDevice from active manager scan for live telemetry
                IOmniDevice matchedDevice = null;
                var scannedDevices = manager.ScanDevices();
                foreach (var d in scannedDevices)
                {
                    if (d.VendorId == vid && (d.ProductId == pid || (targetPids != null && targetPids.Contains(d.ProductId))))
                    {
                        matchedDevice = d;
                        try
                        {
                            matchedDevice.RefreshTelemetry();
                        }
                        catch
                        {
                            // Ignore query failures; cached telemetry will be used
                        }
                        break;
                    }
                }

                if (profile == null && matchedDevice != null)
                {
                    profile = manager.Registry.FindProfile(vid, pid, devName);
                }

                string fileName = string.Format("device_spec_{0:x4}_{1:x4}.md", vid, pid);
                string fullPath = Path.GetFullPath(fileName);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Generating hardware diagnostic report and issue specification for:");
                Console.WriteLine("  Device: {0} (VID: 0x{1:X4}, PID: 0x{2:X4})", devName, vid, pid);
                if (matchedDevice != null)
                {
                    var tel = matchedDevice.Telemetry;
                    if (tel != null && tel.IsAvailable)
                    {
                        Console.WriteLine("  Live Battery: {0}% ({1})", tel.LevelPercent, tel.StateDescription);
                    }
                    Console.WriteLine("  Protocol: {0}", matchedDevice.ProtocolId);
                }
                Console.WriteLine("  Output File: {0}", fullPath);
                Console.ResetColor();
                Console.WriteLine();

                try
                {
                    SpecificationExporter.ExportMarkdownSpecification(
                        transport,
                        devName,
                        vid,
                        pid,
                        targetInterfaces,
                        fileName,
                        matchedDevice,
                        profile);

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("============================================================================");
                    Console.WriteLine(" [EXPORT COMPLETE] Diagnostic specification saved successfully!");
                    Console.WriteLine(" File: {0}", fullPath);
                    Console.WriteLine();
                    Console.WriteLine(" Next Steps:");
                    Console.WriteLine(" 1. To report or request support for this peripheral on GitHub:");
                    Console.WriteLine("    https://github.com/nikpsov/omni-hid/issues/new?template=new_device.md");
                    Console.WriteLine("    (Copy and paste Section 1 from the generated file into the issue description)");
                    Console.WriteLine();
                    Console.WriteLine(" 2. To develop or verify a driver using AI (Gemini, Claude, ChatGPT):");
                    Console.WriteLine("    Provide this markdown document directly to the model to generate");
                    Console.WriteLine("    the JSON profile or C# IProtocolHandler implementation.");
                    Console.WriteLine("============================================================================");
                    Console.ResetColor();
                    Console.WriteLine();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Failed to export specification: {0}", ex.Message);
                    Console.ResetColor();
                }
            }
        }
    }
}
