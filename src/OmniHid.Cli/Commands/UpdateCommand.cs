using System;
using OmniHid.Cli.Formatting;
using OmniHid.Core.Devices;
using OmniHid.Core.Profiles;

namespace OmniHid.Cli.Commands
{
    /// <summary>
    /// Implements the 'update' / 'sync' command, downloading and synchronizing declarative
    /// peripheral profiles from the upstream GitHub repository Over-The-Air (OTA).
    /// </summary>
    public static class UpdateCommand
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Command Execution
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Executes the 'update' command, pulling the latest verified and unverified profiles
        /// from the upstream GitHub repository and writing them to the local devices catalog.
        /// </summary>
        /// <param name="targetDir">Optional explicit directory target for device profiles.</param>
        /// <param name="archiveUrl">Optional custom GitHub zip archive URL.</param>
        public static void Execute(string targetDir = null, string archiveUrl = null)
        {
            CliFormatter.PrintBanner();
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Connecting to upstream GitHub repository...");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  Source: " + (!string.IsNullOrEmpty(archiveUrl) ? archiveUrl : ProfileUpdater.DefaultArchiveUrl));
            Console.ResetColor();
            Console.WriteLine();

            ProfileUpdateResult result = ProfileUpdater.UpdateFromGitHub(targetDir, archiveUrl);

            if (result.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(string.Format("✓ Successfully synchronized {0} device profile(s) from GitHub!", result.UpdatedCount));
                Console.ResetColor();
                Console.WriteLine();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                if (!string.IsNullOrEmpty(result.SourceUrl))
                {
                    Console.WriteLine("Source URL:               " + result.SourceUrl);
                }
                Console.WriteLine("Target catalog directory: " + result.TargetDirectory);
                Console.ResetColor();

                var registry = new DeviceRegistry();
                int verifiedCount = 0;
                int unverifiedCount = 0;
                foreach (var profile in registry.AllProfiles)
                {
                    if (profile.IsVerified)
                    {
                        verifiedCount++;
                    }
                    else
                    {
                        unverifiedCount++;
                    }
                }

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Profile catalog status:");
                Console.ResetColor();
                Console.WriteLine(string.Format("  Total profiles:      {0}", registry.AllProfiles.Count));
                Console.WriteLine(string.Format("  Verified profiles:   {0} (tested)", verifiedCount));
                Console.WriteLine(string.Format("  Unverified profiles: {0} (experimental / community)", unverifiedCount));
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✗ Profile synchronization failed!");
                Console.ResetColor();

                if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("  Error: " + result.ErrorMessage);
                    Console.ResetColor();
                }

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("Existing local profiles remain intact at:");
                Console.WriteLine("  " + result.TargetDirectory);
                Console.ResetColor();
            }

            Console.WriteLine();
        }
    }
}
