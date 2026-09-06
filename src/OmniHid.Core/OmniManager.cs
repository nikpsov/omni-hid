using System;
using System.Collections.Generic;
using System.Threading;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Devices;
using OmniHid.Core.Profiles;
using OmniHid.Core.Protocols;
using OmniHid.Core.Transport;
using OmniHid.Core.Transport.Win32;

namespace OmniHid.Core
{
    /// <summary>
    /// Central manager for the OmniHID peripheral telemetry engine.
    /// Coordinates USB HID discovery, XInput polling, multi-interface logical consolidation,
    /// dynamic background polling, and real-time USB PnP device arrival/removal notifications.
    /// </summary>
    public class OmniManager : IOmniManager
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Public Properties & Events
        // ═══════════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════════
        // Public Properties & Events
        // ═══════════════════════════════════════════════════════════════════════

        private volatile IOmniDevice[] _connectedDevicesSnapshot = new IOmniDevice[0];

        /// <summary>
        /// Gets a thread-safe snapshot list of all currently tracked and active peripheral devices.
        /// Zero allocations on retrieval.
        /// </summary>
        public IReadOnlyList<IOmniDevice> ConnectedDevices
        {
            get { return _connectedDevicesSnapshot; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether companion wireless dongle receivers should be automatically
        /// deduplicated and suppressed from active devices when the peripheral is directly connected via USB cable.
        /// Default is <c>true</c>.
        /// </summary>
        public bool DeduplicateWiredWireless { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether only peripherals with validated declarative JSON profiles are tracked.
        /// When <c>true</c>, unprofiled generic peripherals and dynamic vendor fallbacks are excluded.
        /// Default is <c>false</c>.
        /// </summary>
        public bool RegisteredOnly { get; set; }

        /// <summary>Raised when a new peripheral device is discovered and connected.</summary>
        public event Action<IOmniDevice> DeviceConnected;

        /// <summary>Raised when a peripheral is disconnected or powered off.</summary>
        public event Action<IOmniDevice> DeviceDisconnected;

        /// <summary>Raised whenever a peripheral's battery telemetry reading is refreshed.</summary>
        public event Action<IOmniDevice, BatteryTelemetry> TelemetryUpdated;

        /// <summary>
        /// Raised whenever a hardware scan cycle completes and all device states have been refreshed.
        /// Provides a thread-safe snapshot list of all currently tracked devices.
        /// </summary>
        public event Action<IReadOnlyList<IOmniDevice>> DevicesUpdated;

        /// <summary>Gets the peripheral profile and hardware identification catalog.</summary>
        public DeviceRegistry Registry { get { return _registry; } }

        // ═══════════════════════════════════════════════════════════════════════
        // Internal State & Subsystems
        // ═══════════════════════════════════════════════════════════════════════

        private readonly object _lock = new object();
        private readonly IHidTransport _transport;
        private readonly DeviceRegistry _registry;
        private readonly bool _ownsRegistry;
        private readonly Dictionary<string, IProtocolHandler> _protocols = new Dictionary<string, IProtocolHandler>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, OmniDevice> _activeDevices = new Dictionary<string, OmniDevice>();
        private readonly HashSet<string> _seenDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<OmniDevice> _toRemove = new List<OmniDevice>();

        private Timer _pollTimer;
        private Timer _debounceTimer;
        private Win32DeviceWatcher _watcher;
        private bool _isPolling;

        /// <summary>
        /// Temporary grouping container used during bus reconciliation to aggregate multiple HID interfaces.
        /// </summary>
        private class LogicalDeviceGroup
        {
            public string DeviceId;
            public DeviceProfile Profile;
            public IProtocolHandler Protocol;
            public List<HidDeviceInfo> Interfaces = new List<HidDeviceInfo>();
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Constructors & Initialization
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Initializes a new instance of the <see cref="OmniManager"/> class with custom or default transport and registry.
        /// Registers all built-in hardware protocol drivers and starts the device change watcher.
        /// </summary>
        public OmniManager(IHidTransport transport = null, DeviceRegistry registry = null)
        {
            _transport = transport ?? new Win32HidTransport();
            _ownsRegistry = (registry == null);
            _registry = registry ?? new DeviceRegistry();

            // Register standard protocol drivers
            RegisterProtocol(new RoyuanProtocol(), "royuan-keyboard", "akko", "akko-keyboard", "yichip");
            RegisterProtocol(new AresonProtocol());
            RegisterProtocol(new LogitechHidppProtocol());
            RegisterProtocol(new LogitechCenturionProtocol());
            RegisterProtocol(new SinoWealthProtocol());
            RegisterProtocol(new CompxProtocol());
            RegisterProtocol(new SteelSeriesProtocol());
            RegisterProtocol(new DualSenseProtocol());
            RegisterProtocol(new XboxProtocol());
            RegisterProtocol(new CorsairHeadsetProtocol());
            RegisterProtocol(new HyperXHeadsetProtocol());
            RegisterProtocol(new RazerProtocol(), "razer", "razer-chroma", "razer-hyperspeed");
            RegisterProtocol(new GenericKeyboardProtocol());
            RegisterProtocol(new GenericPeripheralProtocol());

            _pollTimer = new Timer(OnPollTimer, null, Timeout.Infinite, Timeout.Infinite);
            _debounceTimer = new Timer(OnDebounceTimer, null, Timeout.Infinite, Timeout.Infinite);

            try
            {
                _watcher = new Win32DeviceWatcher();
                _watcher.DeviceChanged += OnUsbDeviceChanged;
            }
            catch
            {
                // Fallback to polling if message-only window creation fails in headless / non-GUI service
            }

            _registry.ProfilesReloaded += OnProfilesReloaded;
            DeduplicateWiredWireless = true;
            RegisteredOnly = false;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Public API Methods
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Registers a custom protocol handler with the manager.
        /// </summary>
        /// <param name="protocol">The protocol handler instance.</param>
        /// <param name="aliases">Optional alternate protocol identifier aliases.</param>
        public void RegisterProtocol(IProtocolHandler protocol, params string[] aliases)
        {
            if (protocol != null)
            {
                _protocols[protocol.ProtocolId] = protocol;
                if (aliases != null)
                {
                    foreach (var alias in aliases)
                    {
                        if (!string.IsNullOrEmpty(alias))
                        {
                            _protocols[alias] = protocol;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Starts periodic background telemetry polling at the specified interval.
        /// </summary>
        /// <param name="pollIntervalMs">Interval between telemetry refresh passes in milliseconds.</param>
        public void StartMonitoring(int pollIntervalMs = 15000)
        {
            ForceRefresh();
            _pollTimer.Change(pollIntervalMs, pollIntervalMs);
        }

        /// <summary>
        /// Stops periodic background polling.
        /// </summary>
        public void StopMonitoring()
        {
            _pollTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Reloads device profiles from embedded resources and external filesystem locations,
        /// and triggers an immediate asynchronous bus scan and telemetry refresh.
        /// </summary>
        public void ForceRefresh()
        {
            _registry.Reload();
            ThreadPool.QueueUserWorkItem(state => ScanAndUpdate());
        }

        /// <summary>
        /// Synchronously scans the hardware bus, refreshes telemetry, and returns the list of active devices.
        /// </summary>
        /// <returns>List of discovered <see cref="IOmniDevice"/> instances.</returns>
        public List<IOmniDevice> ScanDevices()
        {
            ScanAndUpdate();
            return new List<IOmniDevice>(_connectedDevicesSnapshot);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Event Handlers
        // ═══════════════════════════════════════════════════════════════════════

        private void OnProfilesReloaded()
        {
            ThreadPool.QueueUserWorkItem(state => ScanAndUpdate());
        }

        private void OnPollTimer(object state)
        {
            ScanAndUpdate();
        }

        private void OnDebounceTimer(object state)
        {
            ScanAndUpdate();
        }

        private void OnUsbDeviceChanged()
        {
            // Debounce rapid plug/unplug events with a 200ms timer delay without blocking a ThreadPool thread
            if (_debounceTimer != null)
            {
                _debounceTimer.Change(200, Timeout.Infinite);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Core Bus Scan & Device Reconciliation Pipeline
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Executes the complete multi-phase bus scan:
        /// 1. Enumerates all active HID interfaces via Win32 SetupAPI.
        /// 2. Groups interfaces by (VID, PID) and consolidates them into physical logical devices.
        /// 3. Probes XInput controller slots and correlates them with enumerated Xbox HID devices.
        /// 4. Reconciles active devices, instantiates new devices, and queries telemetry.
        /// 5. Prunes disconnected devices and dispatches lifecycle events.
        /// </summary>
        private void ScanAndUpdate()
        {
            lock (_lock)
            {
                if (_isPolling) return;
                _isPolling = true;
            }

            try
            {
                List<HidDeviceInfo> allHid = _transport.Enumerate();

                // ── Phase 1: Group raw HID interfaces by physical device instance ──────
                Dictionary<string, List<HidDeviceInfo>> byPhysicalDevice = new Dictionary<string, List<HidDeviceInfo>>(StringComparer.OrdinalIgnoreCase);
                foreach (var dev in allHid)
                {
                    string physId = ExtractPhysicalDeviceId(dev.DevicePath, dev.VendorId, dev.ProductId, dev.UsagePage, dev.Usage);
                    List<HidDeviceInfo> list;
                    if (!byPhysicalDevice.TryGetValue(physId, out list))
                    {
                        list = new List<HidDeviceInfo>();
                        byPhysicalDevice[physId] = list;
                    }
                    list.Add(dev);
                }

                // ── Phase 2: Consolidate into Logical Physical Devices ─────────
                Dictionary<string, LogicalDeviceGroup> logicalGroups = new Dictionary<string, LogicalDeviceGroup>(StringComparer.OrdinalIgnoreCase);

                foreach (var kvp in byPhysicalDevice)
                {
                    string physId = kvp.Key;
                    var devList = kvp.Value;
                    ushort vid = devList[0].VendorId;
                    ushort pid = devList[0].ProductId;
                    string productString = null;
                    foreach (var iface in devList)
                    {
                        if (!string.IsNullOrEmpty(iface.ProductString) && !string.IsNullOrEmpty(iface.ProductString.Trim()))
                        {
                            productString = iface.ProductString.Trim();
                            break;
                        }
                    }

                    DeviceProfile profile = _registry.FindProfile(vid, pid, productString, DeviceCategory.Unknown, !RegisteredOnly);
                    if (profile == null && !RegisteredOnly)
                    {
                        profile = DetectGenericPeripheral(vid, pid, productString, devList);
                    }
                    if (profile == null) continue;
                    if (RegisteredOnly && !profile.IsRegisteredProfile) continue;

                    IProtocolHandler protocol;
                    if (!_protocols.TryGetValue(profile.ProtocolId, out protocol))
                    {
                        if (!_protocols.TryGetValue("generic-peripheral", out protocol))
                        {
                            continue;
                        }
                    }

                    // Clone profile so each physical device instance maintains its own runtime state (e.g. AssignedSlot)
                    DeviceProfile instanceProfile = profile.Clone();

                    // Extract instance tag for deterministic, collision-free DeviceId
                    string instanceTag = physId.IndexOf('#') >= 0
                        ? physId.Substring(physId.IndexOf('#') + 1).Replace('&', '_')
                        : (physId.StartsWith("GAMEPAD_", StringComparison.OrdinalIgnoreCase)
                            ? physId.Replace('&', '_')
                            : string.Format("{0:X4}", pid));

                    string deviceId = string.Format("{0:X4}:{1:X4}:{2}:{3}",
                        vid, pid, instanceTag, instanceProfile.ProtocolId);

                    logicalGroups[physId] = new LogicalDeviceGroup
                    {
                        DeviceId = deviceId,
                        Profile = instanceProfile,
                        Protocol = protocol,
                        Interfaces = devList
                    };
                }

                // ── Phase 2a: Direct Interface Pinning (TargetUsagePage / TargetUsage) ──
                foreach (var grp in logicalGroups.Values)
                {
                    if (grp.Profile != null && grp.Profile.TargetUsagePage != 0 && grp.Interfaces != null && grp.Interfaces.Count > 1)
                    {
                        int targetIdx = -1;
                        for (int i = 0; i < grp.Interfaces.Count; i++)
                        {
                            var iface = grp.Interfaces[i];
                            if (iface.UsagePage == grp.Profile.TargetUsagePage &&
                                (grp.Profile.TargetUsage == 0 || iface.Usage == grp.Profile.TargetUsage))
                            {
                                targetIdx = i;
                                break;
                            }
                        }

                        if (targetIdx > 0)
                        {
                            HidDeviceInfo targetIface = grp.Interfaces[targetIdx];
                            grp.Interfaces.RemoveAt(targetIdx);
                            grp.Interfaces.Insert(0, targetIface);
                        }
                    }
                }

                // ── Phase 2b: Probe XInput Controllers ────────────────────────
                if (Win32XInputNative.IsAvailable)
                {
                    IProtocolHandler xboxProtocol;
                    if (_protocols.TryGetValue("xbox-controller", out xboxProtocol))
                    {
                        int hidXboxCount = 0;
                        foreach (var grp in logicalGroups.Values)
                        {
                            if (grp.Profile != null && (grp.Profile.ProtocolId == "xbox-controller" || (grp.Profile.VendorId == 0x045E && grp.Profile.Category == DeviceCategory.Gamepad)))
                            {
                                hidXboxCount++;
                            }
                        }

                        int xinputCount = 0;
                        for (int slot = 0; slot < 4; slot++)
                        {
                            Win32XInputNative.XINPUT_STATE xState;
                            int stateRes = Win32XInputNative.GetState(slot, out xState);

                            Win32XInputNative.XINPUT_BATTERY_INFORMATION xBatt;
                            int battRes = Win32XInputNative.GetBatteryInformation(slot, Win32XInputNative.BATTERY_DEVTYPE_GAMEPAD, out xBatt);

                            if (stateRes == Win32XInputNative.ERROR_SUCCESS ||
                                (battRes == Win32XInputNative.ERROR_SUCCESS && xBatt.BatteryType != Win32XInputNative.BATTERY_TYPE_DISCONNECTED))
                            {
                                xinputCount++;
                                if (xinputCount <= hidXboxCount)
                                {
                                    continue; // Already covered by enumerated physical HID device
                                }

                                string logicalKey = string.Format("045E:Xbox_Controller_Slot_{0}:Gamepad:xbox-controller", slot);
                                if (!logicalGroups.ContainsKey(logicalKey))
                                {
                                    DeviceProfile xboxProfile = _registry.FindProfile(0x045E, 0x0B12, "Xbox Wireless Controller", DeviceCategory.Gamepad, !RegisteredOnly);
                                    if (xboxProfile == null)
                                    {
                                        if (RegisteredOnly)
                                        {
                                            continue;
                                        }

                                        xboxProfile = new DeviceProfile
                                        {
                                            ModelName = string.Format("Xbox Wireless Controller (Slot {0})", slot + 1),
                                            VendorId = 0x045E,
                                            ProductIds = new ushort[] { 0x0B12 },
                                            Category = DeviceCategory.Gamepad,
                                            ProtocolId = "xbox-controller",
                                            BatteryLifeHours = 40
                                        };
                                    }
                                    else
                                    {
                                        xboxProfile = xboxProfile.Clone();
                                    }

                                    xboxProfile.AssignedSlot = slot;

                                    logicalGroups[logicalKey] = new LogicalDeviceGroup
                                    {
                                        DeviceId = string.Format("045E:Xbox_Controller_{0}:xbox-controller", slot + 1),
                                        Profile = xboxProfile,
                                        Protocol = xboxProtocol,
                                        Interfaces = new List<HidDeviceInfo>()
                                    };
                                }
                            }
                        }

                        // ── Phase 2c: Map Active XInput Slots to Physical HID Devices
                        int[] activeSlots = new int[4];
                        int activeSlotCount = 0;
                        for (int slot = 0; slot < 4; slot++)
                        {
                            Win32XInputNative.XINPUT_STATE xState;
                            int stateRes = Win32XInputNative.GetState(slot, out xState);
                            Win32XInputNative.XINPUT_BATTERY_INFORMATION xBatt;
                            int battRes = Win32XInputNative.GetBatteryInformation(slot, Win32XInputNative.BATTERY_DEVTYPE_GAMEPAD, out xBatt);

                            if (stateRes == Win32XInputNative.ERROR_SUCCESS ||
                                (battRes == Win32XInputNative.ERROR_SUCCESS && xBatt.BatteryType != Win32XInputNative.BATTERY_TYPE_DISCONNECTED))
                            {
                                activeSlots[activeSlotCount++] = slot;
                            }
                        }

                        int slotIdx = 0;
                        foreach (var grp in logicalGroups.Values)
                        {
                            if (grp.Profile != null && (grp.Profile.ProtocolId == "xbox-controller" || (grp.Profile.VendorId == 0x045E && grp.Profile.Category == DeviceCategory.Gamepad)))
                            {
                                if (grp.Profile.AssignedSlot < 0 && slotIdx < activeSlotCount)
                                {
                                    grp.Profile.AssignedSlot = activeSlots[slotIdx++];
                                }
                            }
                        }
                    }
                }

                // ── Phase 2d: Deduplicate Wired Cable & Wireless Receiver Pairs ──────
                if (DeduplicateWiredWireless && logicalGroups.Count > 1)
                {
                    // Identify all peripheral models currently connected via direct USB cable
                    HashSet<string> activeWiredModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var grp in logicalGroups.Values)
                    {
                        if (grp.Profile != null && grp.Interfaces != null && grp.Interfaces.Count > 0)
                        {
                            ushort pid = grp.Interfaces[0].ProductId;
                            if (grp.Profile.IsWiredProductId(pid))
                            {
                                string modelKey = string.Format("{0:X4}:{1}", grp.Profile.VendorId, grp.Profile.ModelName);
                                activeWiredModels.Add(modelKey);
                            }
                        }
                    }

                    if (activeWiredModels.Count > 0)
                    {
                        List<string> keysToSuppress = new List<string>();

                        foreach (var kvp in logicalGroups)
                        {
                            var grp = kvp.Value;
                            if (grp.Profile != null && grp.Interfaces != null && grp.Interfaces.Count > 0)
                            {
                                ushort pid = grp.Interfaces[0].ProductId;
                                bool isWired = grp.Profile.IsWiredProductId(pid);
                                string modelKey = string.Format("{0:X4}:{1}", grp.Profile.VendorId, grp.Profile.ModelName);

                                // If this model has an active wired connection, suppress its companion wireless dongle receiver
                                if (!isWired && activeWiredModels.Contains(modelKey))
                                {
                                    keysToSuppress.Add(kvp.Key);
                                }
                            }
                        }

                        for (int i = 0; i < keysToSuppress.Count; i++)
                        {
                            logicalGroups.Remove(keysToSuppress[i]);
                        }
                    }
                }

                bool collectionChanged = false;

                // ── Phase 3: Update or Create OmniDevice Instances ────────────
                lock (_lock)
                {
                    _seenDeviceIds.Clear();
                }

                List<OmniDevice> newDevices = new List<OmniDevice>();
                List<KeyValuePair<IOmniDevice, BatteryTelemetry>> updatedTelemetry = new List<KeyValuePair<IOmniDevice, BatteryTelemetry>>();

                foreach (var kvp in logicalGroups)
                {
                    var group = kvp.Value;
                    string deviceId = group.DeviceId;

                    OmniDevice device;
                    bool isNew = false;
                    lock (_lock)
                    {
                        _seenDeviceIds.Add(deviceId);
                        if (!_activeDevices.TryGetValue(deviceId, out device))
                        {
                            device = new OmniDevice(deviceId, group.Profile.Clone(), group.Protocol, _transport, group.Interfaces);
                            _activeDevices[deviceId] = device;
                            isNew = true;
                            collectionChanged = true;
                        }
                        else
                        {
                            device.Profile.AssignedSlot = group.Profile.AssignedSlot;
                            device.UpdateInterfaces(group.Interfaces);
                        }
                    }

                    if (isNew)
                    {
                        newDevices.Add(device);
                    }

                    BatteryTelemetry telemetry = device.RefreshTelemetry();
                    updatedTelemetry.Add(new KeyValuePair<IOmniDevice, BatteryTelemetry>(device, telemetry));
                }

                // ── Phase 4: Clean Up Disconnected Devices ────────────────────
                List<OmniDevice> notifyRemoved = null;
                lock (_lock)
                {
                    _toRemove.Clear();
                    foreach (var kvp in _activeDevices)
                    {
                        if (!_seenDeviceIds.Contains(kvp.Key))
                        {
                            _toRemove.Add(kvp.Value);
                        }
                    }

                    if (_toRemove.Count > 0)
                    {
                        notifyRemoved = new List<OmniDevice>(_toRemove);
                        foreach (var dev in _toRemove)
                        {
                            _activeDevices.Remove(dev.Id);
                        }
                        collectionChanged = true;
                    }

                    if (collectionChanged)
                    {
                        UpdateConnectedDevicesSnapshotLocked();
                    }
                }

                // ── Phase 5: Event Notifications (Snapshot is already updated) ─
                if (notifyRemoved != null)
                {
                    foreach (var dev in notifyRemoved)
                    {
                        dev.UpdateInterfaces(null);
                        Action<IOmniDevice> handler = DeviceDisconnected;
                        if (handler != null) handler(dev);
                    }
                }

                foreach (var dev in newDevices)
                {
                    Action<IOmniDevice> handler = DeviceConnected;
                    if (handler != null) handler(dev);
                }

                foreach (var pair in updatedTelemetry)
                {
                    Action<IOmniDevice, BatteryTelemetry> telHandler = TelemetryUpdated;
                    if (telHandler != null) telHandler(pair.Key, pair.Value);
                }

                Action<IReadOnlyList<IOmniDevice>> batchHandler = DevicesUpdated;
                if (batchHandler != null)
                {
                    batchHandler(_connectedDevicesSnapshot);
                }
            }
            finally
            {
                lock (_lock)
                {
                    _isPolling = false;
                }
            }
        }

        private void UpdateConnectedDevicesSnapshotLocked()
        {
            OmniDevice[] snapshot = new OmniDevice[_activeDevices.Count];
            _activeDevices.Values.CopyTo(snapshot, 0);
            _connectedDevicesSnapshot = snapshot;
        }

        /// <summary>
        /// Extracts a normalized physical device identifier from a Win32 HID device interface path.
        /// Consolidates composite USB peripherals (mice, keyboards, headsets) by (VID, PID) so their
        /// vendor-specific telemetry endpoints remain coupled with standard desktop input interfaces.
        /// Gamepads are isolated per physical controller instance so multi-controller wireless adapters
        /// map each gamepad independently to its respective XInput slot.
        /// </summary>
        /// <param name="devicePath">Win32 HID interface path.</param>
        /// <param name="vid">USB Vendor Identifier.</param>
        /// <param name="pid">USB Product Identifier.</param>
        /// <param name="usagePage">HID Usage Page descriptor.</param>
        /// <param name="usage">HID Usage descriptor.</param>
        /// <returns>Normalized grouping key representing the physical device.</returns>
        internal static string ExtractPhysicalDeviceId(string devicePath, ushort vid, ushort pid, ushort usagePage = 0, ushort usage = 0)
        {
            // 1. Detect gamepads: UsagePage 0x0001 with Usage 0x0004/0x0005, XInput &ig_ paths, or known gamepad PIDs
            bool isGamepad = (usagePage == 0x0001 && (usage == 0x0004 || usage == 0x0005)) ||
                             (!string.IsNullOrEmpty(devicePath) && devicePath.IndexOf("&ig_", StringComparison.OrdinalIgnoreCase) >= 0) ||
                             (vid == 0x045E && DeviceRegistry.IsXboxGamepadPid(pid)) ||
                             (vid == 0x054C && DeviceRegistry.IsSonyGamepadPid(pid));

            if (isGamepad && !string.IsNullOrEmpty(devicePath))
            {
                // Gamepad: preserve physical instance tag so each controller gets its own logical device & XInput slot
                string[] parts = devicePath.Split('#');
                if (parts.Length >= 3)
                {
                    string deviceId = parts[1];   // e.g. vid_045e&pid_02ea&ig_00
                    string instanceId = parts[2]; // e.g. 7&9cfa6dc&2&0000
                    int lastAmp = instanceId.LastIndexOf('&');
                    string parentInstance = lastAmp > 0 ? instanceId.Substring(0, lastAmp) : instanceId;
                    return string.Format("GAMEPAD_{0:X4}_{1:X4}_{2}_{3}", vid, pid, deviceId, parentInstance).ToLowerInvariant();
                }
                return string.Format("GAMEPAD_{0:X4}_{1:X4}_{2}", vid, pid, devicePath.ToLowerInvariant());
            }

            // 2. Composite USB peripherals (Mice, Keyboards, Headsets):
            // All interfaces (mi_00, mi_01, etc.) belonging to the same peripheral must remain aggregated
            // so vendor-specific feature endpoints and desktop endpoints are not separated.
            return string.Format("{0:X4}:{1:X4}", vid, pid);
        }

        /// <summary>
        /// Synthesizes a generic profile for unprofiled peripherals by inspecting HID Usage Page and Usage descriptors.
        /// </summary>
        private DeviceProfile DetectGenericPeripheral(ushort vid, ushort pid, string productString, List<HidDeviceInfo> interfaces)
        {
            DeviceCategory category = DeviceCategory.Unknown;
            string protocolId = "generic-peripheral";

            if (interfaces != null)
            {
                foreach (var iface in interfaces)
                {
                    if (iface.UsagePage == 0x0001) // Generic Desktop Controls
                    {
                        if (iface.Usage == 0x0002) // Mouse
                        {
                            category = DeviceCategory.Mouse;
                            break;
                        }
                        if (iface.Usage == 0x0006) // Keyboard
                        {
                            category = DeviceCategory.Keyboard;
                            protocolId = "generic-keyboard";
                            break;
                        }
                        if (iface.Usage == 0x0004 || iface.Usage == 0x0005) // Gamepad / Joystick
                        {
                            category = DeviceCategory.Gamepad;
                            break;
                        }
                    }
                    else if (iface.UsagePage == 0x000B || iface.UsagePage == 0x000C) // Telephony / Consumer Audio
                    {
                        category = DeviceCategory.Headset;
                        break;
                    }
                }
            }

            if (category == DeviceCategory.Unknown)
            {
                return null;
            }

            // 1. Delegate vendor protocol resolution to the central DeviceRegistry using category hint
            DeviceProfile profile = _registry.FindProfile(vid, pid, productString, category);
            if (profile != null)
            {
                return profile;
            }

            // 2. Fallback to generic unprofiled peripheral driver
            string name = !string.IsNullOrEmpty(productString)
                ? productString.Trim()
                : string.Format("Generic {0} (0x{1:X4}:0x{2:X4})", category, vid, pid);

            return new DeviceProfile
            {
                ModelName = name,
                VendorId = vid,
                ProductIds = new ushort[] { pid },
                Category = category,
                ProtocolId = protocolId,
                IsCustomProfile = false,
                BatteryLifeHours = 0
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Disposal
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Stops background monitoring and disposes internal timers, window watchers, and transport resources.
        /// </summary>
        public void Dispose()
        {
            StopMonitoring();
            if (_watcher != null)
            {
                _watcher.Dispose();
                _watcher = null;
            }
            if (_pollTimer != null)
            {
                _pollTimer.Dispose();
                _pollTimer = null;
            }
            if (_debounceTimer != null)
            {
                _debounceTimer.Dispose();
                _debounceTimer = null;
            }
            if (_registry != null)
            {
                _registry.ProfilesReloaded -= OnProfilesReloaded;
                if (_ownsRegistry)
                {
                    _registry.Dispose();
                }
            }
            _transport.Dispose();
        }
    }
}