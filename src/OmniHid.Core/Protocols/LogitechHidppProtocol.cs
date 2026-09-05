using System;
using System.Collections.Generic;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Protocols
{
    /// <summary>
    /// Implements battery telemetry query protocol for Logitech HID++ 2.0 peripherals.
    /// Supports Logitech G PRO Wireless, Superlight 1 &amp; 2, G502 Lightspeed, G305, MX Master 3/3S, G915, G733, etc.
    /// </summary>
    /// <remarks>
    /// Protocol Overview:
    /// - Uses 20-byte Long Reports (Report ID 0x11) transmitted via Feature or Output report.
    /// - Dynamic Feature Discovery:
    ///   Queries Root Feature (0x0000, function 0x00 GetFeature) to dynamically locate the feature index
    ///   for Feature 0x1000 (Battery Level Status) or Feature 0x1004 (Unified Battery Voltage).
    /// - Caches discovered feature indices per device interface path for zero-overhead subsequent queries.
    /// </remarks>
    public class LogitechHidppProtocol : IProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Constants
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public string ProtocolId { get { return "logitech-hidpp"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public string ProtocolName { get { return "Logitech HID++ 2.0 Protocol"; } }

        /// <summary>Gets a value indicating whether this protocol can query telemetry without HID interfaces.</summary>
        public bool CanQueryWithoutHidInterfaces { get { return false; } }

        private const byte REPORT_ID_LONG = 0x11;
        private const byte DEV_DEFAULT    = 0x01;
        private const byte DEV_RECEIVER   = 0xFF;

        private const ushort FEATURE_ROOT            = 0x0000;
        private const ushort FEATURE_BATTERY_STATUS  = 0x1000;
        private const ushort FEATURE_UNIFIED_BATTERY = 0x1004;

        // ═══════════════════════════════════════════════════════════════════════
        // Dynamic Feature Cache
        // ═══════════════════════════════════════════════════════════════════════

        private class FeatureCacheEntry
        {
            public byte DeviceIndex;
            public ushort FeatureId;
            public byte FeatureIndex;
        }

        private static readonly Dictionary<string, FeatureCacheEntry> _featureCache =
            new Dictionary<string, FeatureCacheEntry>(StringComparer.OrdinalIgnoreCase);

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Query Implementation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Queries the Logitech HID++ 2.0 peripheral for battery percentage, voltage, and charging status.
        /// </summary>
        /// <param name="transport">Transport layer abstraction.</param>
        /// <param name="interfaces">List of HID interfaces associated with this device.</param>
        /// <param name="profile">Declarative profile information.</param>
        /// <returns>Populated <see cref="BatteryTelemetry"/> instance.</returns>
        public BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            if (interfaces == null || interfaces.Count == 0)
                return BatteryTelemetry.Offline("Device not found");

            // Look for vendor configuration collection (UsagePage >= 0xFF00) or report length >= 20
            HidDeviceInfo targetDev = null;
            if (profile != null && profile.TargetUsagePage != 0)
            {
                for (int i = 0; i < interfaces.Count; i++)
                {
                    var d = interfaces[i];
                    if (d.UsagePage == profile.TargetUsagePage && (profile.TargetUsage == 0 || d.Usage == profile.TargetUsage))
                    {
                        targetDev = d;
                        break;
                    }
                }
            }

            if (targetDev == null)
            {
                foreach (var dev in interfaces)
                {
                    if (dev.UsagePage >= 0xFF00 || dev.OutputReportByteLength >= 20 || dev.FeatureReportByteLength >= 20)
                    {
                        targetDev = dev;
                        break;
                    }
                }
            }
            if (targetDev == null) targetDev = interfaces[0];

            string devPath = targetDev.DevicePath;

            // 1. Try resolving battery feature via dynamic cache
            FeatureCacheEntry cached;
            lock (_featureCache)
            {
                _featureCache.TryGetValue(devPath, out cached);
            }

            if (cached != null)
            {
                BatteryTelemetry tel = QueryCachedFeature(transport, devPath, cached);
                if (tel != null && tel.IsAvailable)
                {
                    return tel;
                }

                // Invalidate stale cache if device configuration changed or disconnected
                lock (_featureCache)
                {
                    _featureCache.Remove(devPath);
                }
            }

            // 2. Dynamic Feature Discovery: Probe target device index (0x01, then 0xFF)
            byte[] devIndices = new byte[] { DEV_DEFAULT, DEV_RECEIVER };
            foreach (byte devIdx in devIndices)
            {
                // A. Check for Feature 0x1000 (Battery Level Status)
                byte featIdx1000 = DiscoverFeatureIndex(transport, devPath, devIdx, FEATURE_BATTERY_STATUS);
                if (featIdx1000 > 0)
                {
                    var entry = new FeatureCacheEntry { DeviceIndex = devIdx, FeatureId = FEATURE_BATTERY_STATUS, FeatureIndex = featIdx1000 };
                    BatteryTelemetry tel = QueryCachedFeature(transport, devPath, entry);
                    if (tel != null && tel.IsAvailable)
                    {
                        lock (_featureCache) { _featureCache[devPath] = entry; }
                        return tel;
                    }
                }

                // B. Check for Feature 0x1004 (Unified Battery Voltage)
                byte featIdx1004 = DiscoverFeatureIndex(transport, devPath, devIdx, FEATURE_UNIFIED_BATTERY);
                if (featIdx1004 > 0)
                {
                    var entry = new FeatureCacheEntry { DeviceIndex = devIdx, FeatureId = FEATURE_UNIFIED_BATTERY, FeatureIndex = featIdx1004 };
                    BatteryTelemetry tel = QueryCachedFeature(transport, devPath, entry);
                    if (tel != null && tel.IsAvailable)
                    {
                        lock (_featureCache) { _featureCache[devPath] = entry; }
                        return tel;
                    }
                }
            }

            // 3. Fallback to heuristic slots (Slot 0x08 for 0x1000, Slot 0x06 for 0x1004) for legacy firmware
            BatteryTelemetry fallback = QueryHeuristicSlots(transport, devPath);
            if (fallback != null && fallback.IsAvailable)
            {
                return fallback;
            }

            return BatteryTelemetry.Offline("Device sleeping or offline");
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Dynamic Feature Discovery & Query Helpers
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Queries the Logitech IRoot feature (0x0000) to find the dynamic feature slot index for a given feature ID.
        /// </summary>
        private static byte DiscoverFeatureIndex(IHidTransport transport, string devicePath, byte devIdx, ushort featureId)
        {
            byte[] rootQuery = new byte[20];
            rootQuery[0] = REPORT_ID_LONG;
            rootQuery[1] = devIdx;
            rootQuery[2] = 0x00; // Feature 0x0000 (IRoot)
            rootQuery[3] = 0x00; // Function 0x00 (GetFeature)
            rootQuery[4] = (byte)(featureId >> 8);
            rootQuery[5] = (byte)(featureId & 0xFF);

            byte[] resp = new byte[20];
            bool ok = transport.Exchange(devicePath, rootQuery, devicePath, resp, 250);
            if (!ok || resp.Length < 7 || resp[0] != REPORT_ID_LONG)
                return 0;

            // Check for error code 0x8F
            if (resp[2] == 0x8F || resp[2] == 0xFF)
                return 0;

            // Byte 4 holds the assigned feature index (or 0 if unsupported)
            return resp[4];
        }

        /// <summary>
        /// Queries battery telemetry from a discovered feature index.
        /// </summary>
        private static BatteryTelemetry QueryCachedFeature(IHidTransport transport, string devicePath, FeatureCacheEntry entry)
        {
            byte[] req = new byte[20];
            req[0] = REPORT_ID_LONG;
            req[1] = entry.DeviceIndex;
            req[2] = entry.FeatureIndex;
            req[3] = 0x00; // Function 0x00 (Query)

            byte[] resp = new byte[20];
            bool ok = transport.Exchange(devicePath, req, devicePath, resp, 250);
            if (!ok || resp.Length < 7 || resp[0] != REPORT_ID_LONG)
                return null;

            if (resp[2] == 0x8F || resp[2] == 0xFF)
                return null;

            if (entry.FeatureId == FEATURE_BATTERY_STATUS)
            {
                // Byte 4: Level percentage (0..100)
                // Byte 6: Charging status (0x01 = Discharging, 0x02 = Charging, 0x04 = Almost full)
                int level = resp[4];
                byte chargeStatus = resp[6];
                bool isCharging = (chargeStatus == 0x01 || chargeStatus == 0x02 || chargeStatus == 0x04);
                BatteryState state = isCharging ? BatteryState.Charging : BatteryState.Discharging;

                return BatteryTelemetry.Online(level, state);
            }
            else if (entry.FeatureId == FEATURE_UNIFIED_BATTERY)
            {
                // Bytes 4-5: Measured voltage in millivolts
                // Byte 6: Status flag (0x01, 0x03 = charging)
                int mv = (resp[4] << 8) | resp[5];
                byte statusByte = resp[6];
                bool isCharging = (statusByte == 0x03 || statusByte == 0x01);

                // Map 3500mV (empty) .. 4200mV (full LiPo)
                int percent = (mv <= 3500) ? 0 : (mv >= 4200 ? 100 : (mv - 3500) * 100 / 700);
                return BatteryTelemetry.Online(percent, isCharging ? BatteryState.Charging : BatteryState.Discharging, mv);
            }

            return null;
        }

        /// <summary>
        /// Fallback query testing standard legacy slots (Slot 8 for Feature 0x1000, Slot 6 for Feature 0x1004).
        /// </summary>
        private static BatteryTelemetry QueryHeuristicSlots(IHidTransport transport, string devicePath)
        {
            byte[] req1000 = new byte[20];
            req1000[0] = REPORT_ID_LONG;
            req1000[1] = DEV_DEFAULT;
            req1000[2] = 0x08;
            req1000[3] = 0x00;

            byte[] resp = new byte[20];
            if (transport.Exchange(devicePath, req1000, devicePath, resp, 250))
            {
                if (resp[2] != 0x8F && resp[2] != 0xFF && resp[0] == REPORT_ID_LONG)
                {
                    int level = resp[4];
                    byte chargeStatus = resp[6];
                    bool isCharging = (chargeStatus == 0x01 || chargeStatus == 0x02 || chargeStatus == 0x04);
                    return BatteryTelemetry.Online(level, isCharging ? BatteryState.Charging : BatteryState.Discharging);
                }
            }

            byte[] reqVolt = new byte[20];
            reqVolt[0] = REPORT_ID_LONG;
            reqVolt[1] = DEV_DEFAULT;
            reqVolt[2] = 0x06;
            reqVolt[3] = 0x00;

            if (transport.Exchange(devicePath, reqVolt, devicePath, resp, 250))
            {
                if (resp[2] != 0x8F && resp[2] != 0xFF && resp[0] == REPORT_ID_LONG)
                {
                    int mv = (resp[4] << 8) | resp[5];
                    byte statusByte = resp[6];
                    bool isCharging = (statusByte == 0x03 || statusByte == 0x01);
                    int percent = (mv <= 3500) ? 0 : (mv >= 4200 ? 100 : (mv - 3500) * 100 / 700);
                    return BatteryTelemetry.Online(percent, isCharging ? BatteryState.Charging : BatteryState.Discharging, mv);
                }
            }

            return null;
        }
    }
}