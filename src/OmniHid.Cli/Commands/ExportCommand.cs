using System;
using System.Collections.Generic;
using System.IO;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Diagnostics;
using OmniHid.Core.Transport;
using OmniHid.Core.Transport.Win32;
using OmniHid.Cli.Formatting;

namespace OmniHid.Cli.Commands
{
    /// <summary>
    /// Executes the AI-ready hardware protocol specification and prompt exporter.
    /// Analyzes the peripheral's endpoint descriptors, probes baseline feature reports,
    /// fingerprints the controller IC, and compiles a comprehensive reverse-engineering markdown document.
    /// </summary>
    public static class ExportCommand
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Command Execution
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Generates a markdown hardware specification for the device matching the specified filter.
        /// </summary>
        /// <param name="filter">Optional name, VID, or PID filter string.</param>
        public static void Execute(string filter = null)
        {
            CliFormatter.PrintBanner();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("============================================================================");
            Console.WriteLine("        OmniHID AI-Ready Protocol Specification & Prompt Generator          ");
            Console.WriteLine("============================================================================");
            Console.ResetColor();
            Console.WriteLine();

            using (var transport = new Win32HidTransport())
            {
                string devName;
                ushort vid;
                ushort pid;
                List<HidDeviceInfo> targetInterfaces;

                if (!DeviceSelector.SelectTargetDevice(transport, filter, out devName, out vid, out pid, out targetInterfaces))
                {
                    return;
                }

                string fileName = string.Format("device_spec_{0:x4}_{1:x4}.md", vid, pid);
                string fullPath = Path.GetFullPath(fileName);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Generating hardware protocol specification for:");
                Console.WriteLine("  Device: {0} (VID: 0x{1:X4}, PID: 0x{2:X4})", devName, vid, pid);
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
                        fileName);

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("============================================================================");
                    Console.WriteLine(" [EXPORT COMPLETE] Hardware specification saved successfully!");
                    Console.WriteLine(" File: {0}", fullPath);
                    Console.WriteLine(" You can provide this markdown document directly to an AI model to quickly");
                    Console.WriteLine(" generate a custom OmniHID JSON profile or C# protocol driver implementation.");
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
