using System;
using System.Threading;
using OmniHid.Cli.Formatting;
using OmniHid.Core;
using OmniHid.Core.Abstractions;

namespace OmniHid.Cli.Commands
{
    /// <summary>
    /// Implements the 'monitor' command, listening for real-time USB connection events and battery telemetry changes.
    /// </summary>
    public static class MonitorCommand
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Command Execution
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Executes the 'monitor' command.
        /// </summary>
        public static void Execute()
        {
            CliFormatter.PrintBanner();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[MONITOR MODE STARTED] Listening for USB connection and battery events...");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Plug in / unplug dongles or power on devices. Press Enter to stop.\n");
            Console.ResetColor();

            using (var manager = new OmniManager())
            {
                manager.DeviceConnected += dev =>
                {
                    string icon = dev.IsCustomProfile ? "📄 " : "";
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[{0:HH:mm:ss}] [+] DEVICE CONNECTED: {1}{2} ({3}) [{4}]",
                        DateTime.Now, icon, dev.Name, dev.Category, dev.ProtocolId);
                    Console.ResetColor();
                };

                manager.DeviceDisconnected += dev =>
                {
                    string icon = dev.IsCustomProfile ? "📄 " : "";
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[{0:HH:mm:ss}] [-] DEVICE DISCONNECTED: {1}{2} ({3})",
                        DateTime.Now, icon, dev.Name, dev.Category);
                    Console.ResetColor();
                };

                manager.TelemetryUpdated += (dev, tel) =>
                {
                    if (!tel.IsAvailable) return;
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    string icon = dev.IsCustomProfile ? "📄 " : "";
                    string timeInfo = (tel.State == BatteryState.Discharging && !string.IsNullOrEmpty(tel.FormattedTimeRemaining))
                        ? " [" + tel.FormattedTimeRemaining + " remaining]"
                        : "";
                    Console.WriteLine("[{0:HH:mm:ss}] [~] {1}{2}: Battery {3}% ({4}){5}{6}",
                        DateTime.Now, icon, dev.Name, tel.LevelPercent,
                        tel.StateDescription,
                        timeInfo,
                        tel.VoltageMv > 0 ? " [" + tel.VoltageMv + " mV]" : "");
                    Console.ResetColor();
                };

                manager.StartMonitoring(10000);

                while (true)
                {
                    if (!Console.IsInputRedirected && Console.KeyAvailable)
                    {
                        try { Console.ReadKey(true); } catch { /* Ignore console input read errors */ }
                        break;
                    }
                    Thread.Sleep(200);
                }
            }

            Console.WriteLine("\nMonitor stopped.");
        }
    }
}
