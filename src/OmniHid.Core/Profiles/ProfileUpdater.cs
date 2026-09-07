using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading;

namespace OmniHid.Core.Profiles
{
    // ═══════════════════════════════════════════════════════════════════════════
    // Profile Update Result Model
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Encapsulates the execution result of an Over-The-Air (OTA) profile synchronization pass.
    /// </summary>
    public class ProfileUpdateResult
    {
        /// <summary>
        /// Gets or sets a value indicating whether network download and profile synchronization succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether an offline fallback was used due to network or server issues.
        /// </summary>
        public bool IsOfflineFallback { get; set; }

        /// <summary>
        /// Gets or sets the count of device profile files written or updated on disk.
        /// </summary>
        public int UpdatedCount { get; set; }

        /// <summary>
        /// Gets or sets the target filesystem directory where profiles were written.
        /// </summary>
        public string TargetDirectory { get; set; }

        /// <summary>
        /// Gets or sets the actual download URL used for the synchronization pass.
        /// </summary>
        public string SourceUrl { get; set; }

        /// <summary>
        /// Gets or sets an error message detailing network or filesystem failures, if any.
        /// </summary>
        public string ErrorMessage { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Over-The-Air (OTA) Device Profile Synchronization Service
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Provides zero-dependency OTA downloading and synchronization of declarative peripheral profiles
    /// directly from the upstream OmniHID GitHub repository without recompilation or binary releases.
    /// </summary>
    public static class ProfileUpdater
    {
        /// <summary>
        /// Dedicated Over-The-Air devices catalog zip URL generated automatically by GitHub Actions workflow (~5-8 KB).
        /// </summary>
        public const string DefaultArchiveUrl = "https://raw.githubusercontent.com/nikpsov/omni-hid/catalog/devices.zip";

        /// <summary>
        /// Fallback URL pointing to the full repository archive in case the dedicated catalog branch has not yet been initialized.
        /// </summary>
        public const string FallbackArchiveUrl = "https://github.com/nikpsov/omni-hid/archive/refs/heads/main.zip";

        /// <summary>
        /// Default User-Agent header passed to GitHub HTTP requests.
        /// </summary>
        public const string DefaultUserAgent = "OmniHID-OTA-Client/1.0";

        /// <summary>
        /// Synchronously connects to GitHub, downloads the latest device profile tree, and extracts all JSON profiles
        /// into the active devices directory (both verified and unverified categories).
        /// </summary>
        /// <param name="targetDevicesDir">Optional explicit target devices directory. If null, auto-resolves portable or %APPDATA% path.</param>
        /// <param name="archiveUrl">Optional custom download URL (defaults to <see cref="DefaultArchiveUrl"/> with fallback to <see cref="FallbackArchiveUrl"/>).</param>
        /// <returns>A populated <see cref="ProfileUpdateResult"/> reflecting synchronization outcome.</returns>
        public static ProfileUpdateResult UpdateFromGitHub(string targetDevicesDir = null, string archiveUrl = null)
        {
            string targetDir = ResolveTargetDevicesDirectory(targetDevicesDir);

            try
            {
                // Ensure TLS 1.2+ is permitted across all host Windows environments
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                byte[] zipBytes = null;
                string usedUrl = archiveUrl;

                if (!string.IsNullOrEmpty(archiveUrl))
                {
                    usedUrl = archiveUrl;
                    zipBytes = DownloadArchive(archiveUrl);
                }
                else
                {
                    // Primary attempt: dedicated lightweight devices.zip (~5-8 KB) from catalog branch
                    try
                    {
                        usedUrl = DefaultArchiveUrl;
                        zipBytes = DownloadArchive(DefaultArchiveUrl);
                    }
                    catch (WebException)
                    {
                        // Fallback attempt: full repository zip archive (~60-80 KB) if catalog branch does not exist yet
                        usedUrl = FallbackArchiveUrl;
                        zipBytes = DownloadArchive(FallbackArchiveUrl);
                    }
                }

                if (zipBytes == null || zipBytes.Length == 0)
                {
                    return new ProfileUpdateResult
                    {
                        Success = false,
                        IsOfflineFallback = true,
                        TargetDirectory = targetDir,
                        SourceUrl = usedUrl,
                        ErrorMessage = "Empty archive payload received from remote repository."
                    };
                }

                int updatedCount = 0;
                using (var memoryStream = new MemoryStream(zipBytes))
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue; // Skip directory entries
                        if (!entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

                        string relPath = ResolveRelativeProfilePath(entry.FullName);
                        if (string.IsNullOrEmpty(relPath)) continue;

                        string destinationPath = Path.Combine(targetDir, relPath);
                        string destinationDir = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                        {
                            Directory.CreateDirectory(destinationDir);
                        }

                        using (var entryStream = entry.Open())
                        using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            entryStream.CopyTo(fileStream);
                        }

                        updatedCount++;
                    }
                }

                return new ProfileUpdateResult
                {
                    Success = true,
                    IsOfflineFallback = false,
                    UpdatedCount = updatedCount,
                    TargetDirectory = targetDir,
                    SourceUrl = usedUrl
                };
            }
            catch (Exception ex)
            {
                return new ProfileUpdateResult
                {
                    Success = false,
                    IsOfflineFallback = true,
                    TargetDirectory = targetDir,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// Asynchronously downloads and synchronizes device profiles from GitHub on a background worker thread.
        /// </summary>
        /// <param name="callback">Delegate invoked upon completion with the synchronization result.</param>
        /// <param name="targetDevicesDir">Optional explicit target devices directory.</param>
        /// <param name="archiveUrl">Optional custom download URL.</param>
        public static void UpdateFromGitHubAsync(Action<ProfileUpdateResult> callback, string targetDevicesDir = null, string archiveUrl = null)
        {
            ThreadPool.QueueUserWorkItem(state =>
            {
                ProfileUpdateResult result = UpdateFromGitHub(targetDevicesDir, archiveUrl);
                if (callback != null)
                {
                    try
                    {
                        callback(result);
                    }
                    catch (Exception)
                    {
                        // Suppress subscriber callback exceptions
                    }
                }
            });
        }

        /// <summary>
        /// Resolves the destination relative path for a profile entry across different archive layouts
        /// (clean devices.zip, devices/ root, or full repository zip).
        /// </summary>
        private static string ResolveRelativeProfilePath(string entryFullName)
        {
            if (string.IsNullOrEmpty(entryFullName)) return null;

            string normalized = entryFullName.Replace('\\', '/').TrimStart('/');

            // Layout A: Full repository zip containing '/devices/' or beginning with 'devices/'
            int idx = normalized.IndexOf("/devices/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                string rel = normalized.Substring(idx + "/devices/".Length);
                return rel.Replace('/', Path.DirectorySeparatorChar);
            }
            if (normalized.StartsWith("devices/", StringComparison.OrdinalIgnoreCase))
            {
                string rel = normalized.Substring("devices/".Length);
                return rel.Replace('/', Path.DirectorySeparatorChar);
            }

            // Layout B: Clean devices.zip directly starting with 'verified/' or 'unverified/'
            if (normalized.StartsWith("verified/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("unverified/", StringComparison.OrdinalIgnoreCase))
            {
                return normalized.Replace('/', Path.DirectorySeparatorChar);
            }

            return null;
        }

        /// <summary>
        /// Downloads raw archive bytes via HTTP WebClient with predefined user agent headers.
        /// </summary>
        private static byte[] DownloadArchive(string url)
        {
            using (var client = new WebClient())
            {
                client.Headers.Add(HttpRequestHeader.UserAgent, DefaultUserAgent);
                return client.DownloadData(url);
            }
        }

        /// <summary>
        /// Resolves the effective target devices directory, prioritizing portable local directory if writable.
        /// </summary>
        private static string ResolveTargetDevicesDirectory(string explicitTarget)
        {
            if (!string.IsNullOrEmpty(explicitTarget))
            {
                return explicitTarget;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (!string.IsNullOrEmpty(baseDir))
            {
                string localDevices = Path.Combine(baseDir, "devices");
                if (Directory.Exists(localDevices))
                {
                    return localDevices;
                }
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
            {
                string appDataDevices = Path.Combine(appData, "OmniHid", "devices");
                if (!Directory.Exists(appDataDevices))
                {
                    Directory.CreateDirectory(appDataDevices);
                }
                return appDataDevices;
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "devices");
        }
    }
}
