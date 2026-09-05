using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;

namespace OmniHid.Core.Devices
{
    /// <summary>
    /// Central catalog of known peripheral hardware profiles, fallback heuristics, and external JSON definitions.
    /// Maps USB Vendor ID (VID) and Product ID (PID) to matching <see cref="DeviceProfile"/> definitions.
    /// </summary>
    public class DeviceRegistry
    {
        private readonly List<DeviceProfile> _profiles = new List<DeviceProfile>();
        private readonly Dictionary<uint, DeviceProfile> _exactMap = new Dictionary<uint, DeviceProfile>();
        private readonly List<DeviceProfile> _wildcards = new List<DeviceProfile>();
        private readonly object _syncLock = new object();
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private System.Threading.Timer _hotReloadDebounceTimer;

        /// <summary>
        /// Occurs when device profiles have been reloaded from embedded resources or external files.
        /// </summary>
        public event Action ProfilesReloaded;

        // ═══════════════════════════════════════════════════════════════════════
        // Constructors & Registration
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Initializes a new instance of the <see cref="DeviceRegistry"/> class,
        /// registers built-in assembly embedded profiles, and loads external profiles.
        /// </summary>
        public DeviceRegistry()
        {
            LoadEmbeddedProfiles();
            LoadExternalProfiles();
        }

        /// <summary>
        /// Registers a device profile into the active registry.
        /// External profiles take priority over built-in defaults.
        /// </summary>
        public void Register(DeviceProfile profile)
        {
            if (profile == null) return;

            lock (_syncLock)
            {
                _profiles.Insert(0, profile);
                if (profile.ProductIds != null && profile.ProductIds.Length > 0)
                {
                    for (int i = 0; i < profile.ProductIds.Length; i++)
                    {
                        uint key = ((uint)profile.VendorId << 16) | profile.ProductIds[i];
                        _exactMap[key] = profile;
                    }
                }
                else
                {
                    _wildcards.Insert(0, profile);
                }
            }
        }

        /// <summary>
        /// Clears registered profile caches and reloads all definitions from embedded assembly resources
        /// followed by external filesystem profile locations.
        /// </summary>
        public void Reload()
        {
            lock (_syncLock)
            {
                _profiles.Clear();
                _exactMap.Clear();
                _wildcards.Clear();

                LoadEmbeddedProfiles();
                LoadExternalProfiles();
            }

            var handler = ProfilesReloaded;
            if (handler != null)
            {
                try { handler(); } catch { }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Profile Resolution & Dynamic Vendor Fallbacks
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Resolves a matching <see cref="DeviceProfile"/> for the given VID/PID, falling back to vendor heuristics.
        /// </summary>
        public DeviceProfile FindProfile(ushort vid, ushort pid, string productString = null)
        {
            return FindProfile(vid, pid, productString, DeviceCategory.Unknown);
        }

        /// <summary>
        /// Resolves a matching <see cref="DeviceProfile"/> for the given VID/PID with an optional category hint.
        /// </summary>
        public DeviceProfile FindProfile(ushort vid, ushort pid, string productString, DeviceCategory categoryHint)
        {
            // 1. O(1) exact match against registered profiles
            uint key = ((uint)vid << 16) | pid;
            DeviceProfile profile;
            lock (_syncLock)
            {
                if (_exactMap.TryGetValue(key, out profile))
                {
                    return profile;
                }

                // 2. Wildcard match (e.g. any PID for vendor)
                for (int i = 0; i < _wildcards.Count; i++)
                {
                    if (_wildcards[i].VendorId == vid)
                    {
                        return _wildcards[i];
                    }
                }
            }

            // 3. Dynamic Vendor Fallbacks for Unregistered Product IDs
            return ResolveVendorFallback(vid, pid, productString, categoryHint);
        }

        /// <summary>
        /// Produces a synthesized fallback profile for recognized peripheral vendors when an exact model profile is not registered.
        /// </summary>
        private DeviceProfile ResolveVendorFallback(ushort vid, ushort pid, string productString, DeviceCategory categoryHint)
        {
            // ── Areson / ROYUAN (0x25A7) ───────────────────────────────────────
            // 0x25A7 is shared across Areson mouse controllers and ROYUAN wireless keyboards.
            if (vid == 0x25A7)
            {
                bool isKeyboard = categoryHint == DeviceCategory.Keyboard ||
                    (!string.IsNullOrEmpty(productString) && productString.IndexOf("Keyboard", StringComparison.OrdinalIgnoreCase) >= 0);

                if (isKeyboard)
                {
                    return new DeviceProfile
                    {
                        ModelName = !string.IsNullOrEmpty(productString) ? productString : "Wireless Keyboard (ROYUAN)",
                        VendorId = vid,
                        ProductIds = new ushort[] { pid },
                        Category = DeviceCategory.Keyboard,
                        ProtocolId = "royuan"
                    };
                }

                return new DeviceProfile
                {
                    ModelName = string.IsNullOrEmpty(productString) ? "Areson Wireless Mouse" : productString,
                    VendorId = vid,
                    ProductIds = new ushort[] { pid },
                    Category = DeviceCategory.Mouse,
                    ProtocolId = "areson"
                };
            }

            // ── ROYUAN / YiChip (0x3151) ───────────────────────────────────────
            if (vid == 0x3151)
            {
                return new DeviceProfile
                {
                    ModelName = string.IsNullOrEmpty(productString) ? "ROYUAN Wireless Keyboard" : productString,
                    VendorId = vid,
                    ProductIds = new ushort[] { pid },
                    Category = DeviceCategory.Keyboard,
                    ProtocolId = "royuan"
                };
            }

            // ── Primax / OEM Royuan Keyboard (0x0461) ──────────────────────────
            if (vid == 0x0461 && categoryHint == DeviceCategory.Keyboard)
            {
                return new DeviceProfile
                {
                    ModelName = string.IsNullOrEmpty(productString) ? "Wireless Keyboard" : productString,
                    VendorId = vid,
                    ProductIds = new ushort[] { pid },
                    Category = DeviceCategory.Keyboard,
                    ProtocolId = "royuan"
                };
            }

            // ── Logitech (0x046D) ─────────────────────────────────────────────
            if (vid == 0x046D)
            {
                bool isHeadset = categoryHint == DeviceCategory.Headset ||
                    (!string.IsNullOrEmpty(productString) &&
                     (productString.IndexOf("Headset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      productString.IndexOf("PRO X", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      productString.IndexOf("G733", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      productString.IndexOf("G935", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      productString.IndexOf("G535", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      productString.IndexOf("G533", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      productString.IndexOf("Astro", StringComparison.OrdinalIgnoreCase) >= 0));

                // Only Logitech G PRO X 2 Lightspeed uses the Centurion protocol (Report ID 0x51).
                // All other Logitech wireless headsets (G535, G733, G533, G935, G PRO X Gen 1) use Logitech HID++ 2.0.
                bool isCenturion = isHeadset && !string.IsNullOrEmpty(productString) &&
                    (productString.IndexOf("PRO X 2", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     productString.IndexOf("Centurion", StringComparison.OrdinalIgnoreCase) >= 0);

                return new DeviceProfile
                {
                    ModelName = string.IsNullOrEmpty(productString) ? (isHeadset ? "Logitech Wireless Headset" : "Logitech Wireless Mouse") : productString,
                    VendorId = vid,
                    ProductIds = new ushort[] { pid },
                    Category = isHeadset ? DeviceCategory.Headset : DeviceCategory.Mouse,
                    ProtocolId = isCenturion ? "logitech-centurion" : "logitech-hidpp"
                };
            }

            // ── Sony PlayStation (0x054C) ─────────────────────────────────────
            if (vid == 0x054C)
            {
                return new DeviceProfile
                {
                    ModelName = string.IsNullOrEmpty(productString) ? "Sony PlayStation Controller" : productString,
                    VendorId = vid,
                    ProductIds = new ushort[] { pid },
                    Category = DeviceCategory.Gamepad,
                    ProtocolId = "sony-dualsense",
                    BatteryLifeHours = 12
                };
            }

            // ── Microsoft Xbox (0x045E) ───────────────────────────────────────
            if (vid == 0x045E)
            {
                string modelName = "Xbox Wireless Controller";
                double batteryLife = 40;

                if (pid == 0x02EA || pid == 0x02D1 || pid == 0x0291)
                {
                    modelName = "Xbox One Wireless Controller";
                    batteryLife = 30;
                }
                else if (pid == 0x02E3)
                {
                    modelName = "Xbox One Elite Controller";
                    batteryLife = 30;
                }
                else if (pid == 0x0B00 || pid == 0x0B05)
                {
                    modelName = "Xbox Elite Wireless Controller Series 2";
                    batteryLife = 40;
                }
                else if (pid == 0x0B12 || pid == 0x0B13)
                {
                    modelName = "Xbox Wireless Controller";
                    batteryLife = 40;
                }
                else if (pid == 0x028E || pid == 0x028F)
                {
                    modelName = "Xbox 360 Controller";
                    batteryLife = 30;
                }
                else if (!string.IsNullOrEmpty(productString))
                {
                    modelName = productString;
                }

                return new DeviceProfile
                {
                    ModelName = modelName,
                    VendorId = vid,
                    ProductIds = new ushort[] { pid },
                    Category = DeviceCategory.Gamepad,
                    ProtocolId = "xbox-controller",
                    BatteryLifeHours = batteryLife
                };
            }

            // ── SteelSeries (0x1038) ──────────────────────────────────────────
            if (vid == 0x1038)
            {
                bool isHeadset = categoryHint == DeviceCategory.Headset ||
                    (!string.IsNullOrEmpty(productString) &&
                     (productString.IndexOf("Arctis", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      productString.IndexOf("Nova", StringComparison.OrdinalIgnoreCase) >= 0 ||
                      productString.IndexOf("Headset", StringComparison.OrdinalIgnoreCase) >= 0));

                return new DeviceProfile
                {
                    ModelName = string.IsNullOrEmpty(productString) ? "SteelSeries Wireless Device" : productString,
                    VendorId = vid,
                    ProductIds = new ushort[] { pid },
                    Category = isHeadset ? DeviceCategory.Headset : DeviceCategory.Mouse,
                    ProtocolId = "steelseries"
                };
            }

            // ── Corsair (0x1B1C) ──────────────────────────────────────────────
            if (vid == 0x1B1C)
            {
                return new DeviceProfile
                {
                    ModelName = string.IsNullOrEmpty(productString) ? "Corsair Wireless Headset" : productString,
                    VendorId = vid,
                    ProductIds = new ushort[] { pid },
                    Category = DeviceCategory.Headset,
                    ProtocolId = "corsair-headset"
                };
            }

            // ── HyperX / HP (0x03F0) ──────────────────────────────────────────
            if (vid == 0x03F0)
            {
                return new DeviceProfile
                {
                    ModelName = string.IsNullOrEmpty(productString) ? "HyperX Wireless Headset" : productString,
                    VendorId = vid,
                    ProductIds = new ushort[] { pid },
                    Category = DeviceCategory.Headset,
                    ProtocolId = "hyperx-headset"
                };
            }

            // ── SinoWealth (0x258A) ───────────────────────────────────────────
            if (vid == 0x258A)
            {
                return new DeviceProfile
                {
                    ModelName = string.IsNullOrEmpty(productString) ? "SinoWealth Wireless Mouse" : productString,
                    VendorId = vid,
                    ProductIds = new ushort[] { pid },
                    Category = DeviceCategory.Mouse,
                    ProtocolId = "sinowealth"
                };
            }

            // ── CompX (0x24AE) ────────────────────────────────────────────────
            if (vid == 0x24AE)
            {
                return new DeviceProfile
                {
                    ModelName = string.IsNullOrEmpty(productString) ? "CompX Wireless Mouse" : productString,
                    VendorId = vid,
                    ProductIds = new ushort[] { pid },
                    Category = DeviceCategory.Mouse,
                    ProtocolId = "compx"
                };
            }

            // ── Razer (0x1532) ────────────────────────────────────────────────
            if (vid == 0x1532)
            {
                DeviceCategory category = DeviceCategory.Mouse;
                if (categoryHint != DeviceCategory.Unknown)
                {
                    category = categoryHint;
                }
                else if (!string.IsNullOrEmpty(productString))
                {
                    if (productString.IndexOf("BlackShark", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        productString.IndexOf("Nari", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        productString.IndexOf("Barracuda", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        productString.IndexOf("Kraken", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        productString.IndexOf("Headset", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        category = DeviceCategory.Headset;
                    }
                    else if (productString.IndexOf("Keyboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             productString.IndexOf("BlackWidow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             productString.IndexOf("DeathStalker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             productString.IndexOf("Huntsman", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             productString.IndexOf("Tartarus", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        category = DeviceCategory.Keyboard;
                    }
                }

                string defaultName = category == DeviceCategory.Headset ? "Razer Wireless Headset" :
                                     category == DeviceCategory.Keyboard ? "Razer Wireless Keyboard" :
                                     "Razer Wireless Mouse";

                return new DeviceProfile
                {
                    ModelName = string.IsNullOrEmpty(productString) ? defaultName : productString,
                    VendorId = vid,
                    ProductIds = new ushort[] { pid },
                    Category = category,
                    ProtocolId = "razer"
                };
            }

            return null;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Embedded Resource & External Directory Loading
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Discovers and registers peripheral profiles embedded directly within the OmniHid.Core assembly.
        /// </summary>
        private void LoadEmbeddedProfiles()
        {
            try
            {
                var assembly = typeof(DeviceRegistry).Assembly;
                string[] resourceNames = assembly.GetManifestResourceNames();
                if (resourceNames == null) return;

                foreach (string resourceName in resourceNames)
                {
                    if (resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using (var stream = assembly.GetManifestResourceStream(resourceName))
                            {
                                if (stream != null)
                                {
                                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                                    {
                                        string content = reader.ReadToEnd();
                                        DeviceProfile profile = JsonProfileLoader.ParseProfile(content);
                                        if (profile != null && profile.VendorId > 0)
                                        {
                                            profile.IsCustomProfile = false;
                                            Register(profile);
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Recursively scans and loads external JSON peripheral definitions from a specific directory path.
        /// If the directory exists, a FileSystemWatcher is automatically registered for hot-reloading.
        /// </summary>
        /// <param name="directoryPath">Filesystem path containing device profile JSON files.</param>
        public void LoadProfilesFromDirectory(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath)) return;

            try
            {
                SetupHotReloadWatcher(directoryPath);

                string[] files = Directory.GetFiles(directoryPath, "*.json", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    try
                    {
                        string content = File.ReadAllText(file, Encoding.UTF8);
                        DeviceProfile profile = JsonProfileLoader.ParseProfile(content);
                        if (profile != null && profile.VendorId > 0)
                        {
                            profile.IsCustomProfile = true;
                            Register(profile);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void SetupHotReloadWatcher(string directoryPath)
        {
            try
            {
                if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath)) return;
                string fullPath = Path.GetFullPath(directoryPath);

                lock (_syncLock)
                {
                    for (int i = 0; i < _watchers.Count; i++)
                    {
                        if (string.Equals(_watchers[i].Path, fullPath, StringComparison.OrdinalIgnoreCase))
                            return; // Already watching this directory
                    }

                    var watcher = new FileSystemWatcher(fullPath, "*.json")
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
                    };

                    FileSystemEventHandler handler = (s, e) => OnFileChanged();
                    RenamedEventHandler renameHandler = (s, e) => OnFileChanged();

                    watcher.Changed += handler;
                    watcher.Created += handler;
                    watcher.Deleted += handler;
                    watcher.Renamed += renameHandler;

                    watcher.EnableRaisingEvents = true;
                    _watchers.Add(watcher);
                }
            }
            catch { }
        }

        private void OnFileChanged()
        {
            lock (_syncLock)
            {
                if (_hotReloadDebounceTimer == null)
                {
                    _hotReloadDebounceTimer = new System.Threading.Timer(
                        state => Reload(),
                        null,
                        300,
                        System.Threading.Timeout.Infinite
                    );
                }
                else
                {
                    _hotReloadDebounceTimer.Change(300, System.Threading.Timeout.Infinite);
                }
            }
        }

        private void LoadExternalProfiles()
        {
            var searchDirs = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniHid", "devices"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "devices"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\devices"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\devices"),
                Path.Combine(Directory.GetCurrentDirectory(), "devices"),
                Path.Combine(Directory.GetCurrentDirectory(), @"..\devices")
            };

            var processedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dir in searchDirs)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    string fullDir = Path.GetFullPath(dir);
                    if (!processedDirs.Add(fullDir)) continue;

                    LoadProfilesFromDirectory(fullDir);
                }
                catch { }
            }
        }
    }
}