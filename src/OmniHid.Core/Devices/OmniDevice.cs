using System;
using System.Collections.Generic;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Devices
{
    /// <summary>
    /// Represents an active, monitored physical hardware peripheral within OmniHID.
    /// Manages underlying HID interface collections, telemetry refreshing, and runtime estimation.
    /// </summary>
    public class OmniDevice : IOmniDevice
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Properties
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique logical device ID (e.g., "046D:C094:logitech-hidpp").</summary>
        public string Id { get; private set; }

        /// <summary>Commercial model or peripheral name.</summary>
        public string Name { get; private set; }

        /// <summary>16-bit USB Vendor ID.</summary>
        public ushort VendorId { get; private set; }

        /// <summary>16-bit USB Product ID.</summary>
        public ushort ProductId { get; private set; }

        /// <summary>Device category (Mouse, Keyboard, Headset, Gamepad).</summary>
        public DeviceCategory Category { get; private set; }

        /// <summary>Declared hardware capabilities.</summary>
        public DeviceCapabilities Capabilities { get; private set; }

        /// <summary>Driver protocol identifier.</summary>
        public string ProtocolId { get; private set; }

        /// <summary>Gets a value indicating whether the peripheral is currently connected.</summary>
        public bool IsConnected { get; private set; }

        /// <summary>Gets a value indicating whether the active connection is via direct USB cable.</summary>
        public bool IsWired
        {
            get
            {
                return (_profile != null && _profile.IsWiredProductId(ProductId)) ||
                       (Telemetry != null && Telemetry.IsWired);
            }
        }

        /// <summary>Gets a value indicating whether this device was loaded from an external JSON profile.</summary>
        public bool IsCustomProfile { get { return _profile != null && _profile.IsCustomProfile; } }

        /// <summary>Gets a value indicating whether this device was instantiated from a validated declarative JSON profile.</summary>
        public bool IsRegisteredProfile { get { return _profile != null && _profile.IsRegisteredProfile; } }

        /// <summary>Gets a value indicating whether this device profile belongs to verified repository profiles.</summary>
        public bool IsVerified { get { return _profile == null || _profile.IsVerified; } }

        /// <summary>Most recent battery telemetry snapshot.</summary>
        public BatteryTelemetry Telemetry { get; private set; }

        /// <summary>
        /// Gets or sets the assigned XInput controller user slot (0..3), or -1 if unassigned.
        /// </summary>
        public int AssignedSlot { get; set; }

        /// <summary>Reference to the associated declarative profile.</summary>
        public DeviceProfile Profile { get { return _profile; } }

        /// <summary>Gets the list of physical HID interface endpoints aggregated under this logical peripheral.</summary>
        public IReadOnlyList<HidDeviceInfo> Interfaces
        {
            get { return _cachedInterfacesSnapshot; }
        }

        private readonly object _lock = new object();
        private DeviceProfile _profile;
        private IProtocolHandler _protocol;
        private readonly IHidTransport _transport;
        private readonly List<HidDeviceInfo> _interfaces;
        private volatile HidDeviceInfo[] _cachedInterfacesSnapshot;
        private volatile List<HidDeviceInfo> _cachedInterfacesList;

        /// <summary>
        /// Dynamically updates the active profile and protocol handler when profiles are reloaded from disk.
        /// </summary>
        /// <param name="profile">Updated hardware profile.</param>
        /// <param name="protocol">Updated protocol driver instance.</param>
        public void UpdateProfile(DeviceProfile profile, IProtocolHandler protocol)
        {
            lock (_lock)
            {
                if (profile != null)
                {
                    _profile = profile;
                    Name = profile.ModelName;
                    Category = profile.Category;
                    Capabilities = profile.Capabilities;
                    ProtocolId = profile.ProtocolId;
                    AssignedSlot = profile.AssignedSlot;
                }
                if (protocol != null)
                {
                    _protocol = protocol;
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Constructors
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Initializes a new instance of the <see cref="OmniDevice"/> class with a custom logical ID.
        /// </summary>
        public OmniDevice(string id, DeviceProfile profile, IProtocolHandler protocol, IHidTransport transport, List<HidDeviceInfo> interfaces)
        {
            _profile = profile;
            _protocol = protocol;
            _transport = transport;
            _interfaces = interfaces != null ? new List<HidDeviceInfo>(interfaces) : new List<HidDeviceInfo>();
            _cachedInterfacesSnapshot = _interfaces.ToArray();
            _cachedInterfacesList = new List<HidDeviceInfo>(_interfaces);

            VendorId = profile.VendorId;
            ProductId = _interfaces.Count > 0 ? _interfaces[0].ProductId : (profile.ProductIds != null && profile.ProductIds.Length > 0 ? profile.ProductIds[0] : (ushort)0);
            Id = !string.IsNullOrEmpty(id) ? id : string.Format("{0:X4}:{1:X4}:{2}", VendorId, ProductId, profile.ProtocolId);
            Name = profile.ModelName;
            Category = profile.Category;
            Capabilities = profile.Capabilities;
            ProtocolId = profile.ProtocolId;
            AssignedSlot = profile.AssignedSlot;
            IsConnected = true;
            Telemetry = BatteryTelemetry.Offline("Initializing...");
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="OmniDevice"/> class with auto-generated ID.
        /// </summary>
        public OmniDevice(DeviceProfile profile, IProtocolHandler protocol, IHidTransport transport, List<HidDeviceInfo> interfaces)
            : this(null, profile, protocol, transport, interfaces)
        {
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Device Interface Management & Telemetry Refresh
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Updates the collection of active HID interfaces associated with this physical peripheral.
        /// </summary>
        public void UpdateInterfaces(List<HidDeviceInfo> interfaces)
        {
            lock (_lock)
            {
                _interfaces.Clear();
                if (interfaces != null && interfaces.Count > 0)
                {
                    _interfaces.AddRange(interfaces);
                    ProductId = interfaces[0].ProductId;
                }
                bool canRunWithoutHid = _protocol != null && _protocol.CanQueryWithoutHidInterfaces;
                if (_interfaces.Count == 0 && !canRunWithoutHid)
                {
                    IsConnected = false;
                }
                _cachedInterfacesSnapshot = _interfaces.ToArray();
                _cachedInterfacesList = new List<HidDeviceInfo>(_interfaces);
            }
        }

        /// <summary>
        /// Polls the peripheral for updated battery level and power telemetry.
        /// Refines power state (Charging, Full, Wired) and runtime estimation.
        /// </summary>
        public BatteryTelemetry RefreshTelemetry()
        {
            List<HidDeviceInfo> currentInterfaces = _cachedInterfacesList;

            bool canRunWithoutHid = _protocol != null && _protocol.CanQueryWithoutHidInterfaces;
            if (currentInterfaces.Count == 0 && !canRunWithoutHid)
            {
                IsConnected = false;
                Telemetry = BatteryTelemetry.Offline("Device disconnected");
                return Telemetry;
            }

            try
            {
                if (AssignedSlot >= 0 && _profile != null && _profile.AssignedSlot != AssignedSlot)
                {
                    _profile.AssignedSlot = AssignedSlot;
                }

                Telemetry = _protocol.QueryBattery(_transport, currentInterfaces, _profile);
                if (Telemetry.IsAvailable)
                {
                    IsConnected = true;

                    // Detect whether the active peripheral interface is direct wired USB
                    bool isWired = IsWired;
                    Telemetry.IsWired = isWired;

                    // Universal Power Management state refinement across all protocols:
                    // 1. If connected via direct wired USB cable:
                    //    - Battery at 100% or flagged Full is Full (charge complete / float standby).
                    //    - Battery < 100% is Charging (device cannot discharge while receiving VBUS 5V).
                    if (isWired)
                    {
                        if (Telemetry.LevelPercent >= 100 || Telemetry.State == BatteryState.Full)
                        {
                            Telemetry.State = BatteryState.Full;
                        }
                        else if (Telemetry.State == BatteryState.Discharging)
                        {
                            Telemetry.State = BatteryState.Charging;
                        }
                    }
                    else if (Telemetry.LevelPercent >= 100 && Telemetry.State == BatteryState.Charging)
                    {
                        Telemetry.State = BatteryState.Full;
                    }

                    // 2. Runtime estimation: calculate TimeToEmpty when operating wirelessly on battery
                    bool isOperatingOnBattery = !isWired && !Telemetry.IsCharging;
                    if (isOperatingOnBattery && _profile.BatteryLifeHours > 0 && Telemetry.LevelPercent >= 0)
                    {
                        if (Telemetry.TimeToEmptyMinutes <= 0)
                        {
                            Telemetry.TimeToEmptyMinutes = (int)Math.Round((Telemetry.LevelPercent / 100.0) * _profile.BatteryLifeHours * 60.0);
                        }
                    }
                    else if (!isOperatingOnBattery)
                    {
                        // Suppress runtime depletion timer when plugged in (Charging / Full / Wired)
                        Telemetry.TimeToEmptyMinutes = 0;
                    }

                    // 3. Charge time estimation: calculate TimeToFull when actively charging over USB
                    if (Telemetry.IsCharging && Telemetry.LevelPercent >= 0 && Telemetry.LevelPercent < 100)
                    {
                        if (Telemetry.TimeToFullMinutes <= 0)
                        {
                            // Standard peripheral USB charging profile: ~120 minutes (2.0h) for 0..100% cycle
                            Telemetry.TimeToFullMinutes = Math.Max(1, (int)Math.Round(((100 - Telemetry.LevelPercent) / 100.0) * 120.0));
                        }
                    }
                    else if (!Telemetry.IsCharging)
                    {
                        Telemetry.TimeToFullMinutes = 0;
                    }
                }
                else
                {
                    IsConnected = false;
                }
            }
            catch (Exception ex)
            {
                IsConnected = false;
                Telemetry = BatteryTelemetry.Offline("Query error: " + ex.Message);
            }

            return Telemetry;
        }

        /// <summary>
        /// Returns a formatted string representation of the peripheral and its telemetry.
        /// </summary>
        public override string ToString()
        {
            return string.Format("{0} ({1}): {2}", Name, Category, Telemetry);
        }
    }
}