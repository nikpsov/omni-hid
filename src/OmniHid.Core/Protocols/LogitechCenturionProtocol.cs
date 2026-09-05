using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Profiles;
using OmniHid.Core.Transport;
using OmniHid.Core.Transport.Win32;

namespace OmniHid.Core.Protocols
{
    /// <summary>
    /// Implements battery telemetry query protocol for Logitech Centurion headsets (Logitech G PRO X 2 Lightspeed).
    /// </summary>
    /// <remarks>
    /// Protocol Architecture:
    /// - Operates over vendor audio control endpoint (UsagePage 0xFFA0, 64-byte frames, Report ID 0x51).
    /// - Multi-layer protocol:
    ///   1. Transport Framing: 64-byte reports with Report ID 0x51 and length prefix.
    ///   2. Host Feature Set: Queries root feature (0x0000) to find CenturionBridge (0x0003).
    ///   3. Centurion Bridge Tunnel: Fragments sub-device packets through the dongle bridge to the wireless headset.
    ///   4. Sub-device Feature Discovery: Discovers sub-device features to locate Battery SoC (0x0104).
    ///   5. Battery Query: Reads battery percentage (byte 0) and charging status (byte 2).
    /// - Feature indices are discovered dynamically and cached per device path.
    /// </remarks>
    public class LogitechCenturionProtocol : IProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Constants
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public string ProtocolId { get { return "logitech-centurion"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public string ProtocolName { get { return "Logitech Centurion Protocol (G PRO X 2)"; } }

        /// <summary>Gets a value indicating whether this protocol can query telemetry without HID interfaces.</summary>
        public bool CanQueryWithoutHidInterfaces { get { return false; } }

        private const byte REPORT_ID = 0x51;
        private const int FRAME_SIZE = 64;
        private const byte SOFTWARE_ID = 0x01;
        private const byte BRIDGE_SEND_FRAGMENT_FN = 0x10;
        private const byte BRIDGE_MESSAGE_EVENT_FN = 0x10;

        private const ushort FEATURE_ROOT = 0x0000;
        private const ushort FEATURE_FEATURE_SET = 0x0001;
        private const ushort FEATURE_CENTURION_BRIDGE = 0x0003;
        private const ushort FEATURE_BATTERY_SOC = 0x0104;

        // ═══════════════════════════════════════════════════════════════════════
        // Cached Feature Discovery Information
        // ═══════════════════════════════════════════════════════════════════════

        private static readonly Dictionary<string, CenturionDiscoveryInfo> _cachedInfo =
            new Dictionary<string, CenturionDiscoveryInfo>(StringComparer.OrdinalIgnoreCase);

        private class CenturionDiscoveryInfo
        {
            public byte BridgeIndex;
            public byte BatterySubFeatureIndex;
            public bool Discovered;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Query Implementation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Queries the Logitech Centurion headset for battery percentage and charging state via bridged HID tunneling.
        /// </summary>
        /// <param name="transport">Transport layer abstraction.</param>
        /// <param name="interfaces">List of HID interfaces associated with this headset.</param>
        /// <param name="profile">Declarative profile information.</param>
        /// <returns>Populated <see cref="BatteryTelemetry"/> instance.</returns>
        public BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            if (interfaces == null || interfaces.Count == 0)
                return BatteryTelemetry.Offline("Device not found");

            // Prioritize UsagePage 0xFFA0 (Logitech vendor audio control)
            HidDeviceInfo targetDev = null;
            foreach (var dev in interfaces)
            {
                if (dev.UsagePage == 0xFFA0)
                {
                    targetDev = dev;
                    break;
                }
            }

            if (targetDev == null)
            {
                foreach (var dev in interfaces)
                {
                    if (dev.OutputReportByteLength >= 64 || dev.FeatureReportByteLength >= 64)
                    {
                        targetDev = dev;
                        break;
                    }
                }
            }
            if (targetDev == null) targetDev = interfaces[0];

            return ExecuteCenturionQuery(targetDev.DevicePath);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Centurion Pipeline Execution
        // ═══════════════════════════════════════════════════════════════════════

        private static BatteryTelemetry ExecuteCenturionQuery(string devicePath)
        {
            using (SafeFileHandle hDev = Win32HidNative.CreateFile(
                devicePath,
                Win32HidNative.GENERIC_READ | Win32HidNative.GENERIC_WRITE,
                Win32HidNative.FILE_SHARE_READ | Win32HidNative.FILE_SHARE_WRITE,
                IntPtr.Zero,
                Win32HidNative.OPEN_EXISTING,
                Win32HidNative.FILE_FLAG_OVERLAPPED,
                IntPtr.Zero))
            {
                if (hDev.IsInvalid)
                {
                    return BatteryTelemetry.Offline("Cannot open headset handle");
                }

                CenturionDiscoveryInfo info;
                lock (_cachedInfo)
                {
                    if (_cachedInfo.Count > 32) _cachedInfo.Clear();
                    if (!_cachedInfo.TryGetValue(devicePath, out info))
                    {
                        info = new CenturionDiscoveryInfo();
                        _cachedInfo[devicePath] = info;
                    }
                }

                // Discover bridge & battery feature indices if not cached
                if (!info.Discovered)
                {
                    if (!DiscoverCenturionFeatures(hDev, info))
                    {
                        return BatteryTelemetry.Offline("Headset offline or turned off");
                    }
                }

                // Query Battery SoC via bridge
                byte[] reply = SendBridgeRequest(hDev, info.BridgeIndex, info.BatterySubFeatureIndex, 0x00, null);
                if (reply == null || reply.Length < 1)
                {
                    info.Discovered = false;
                    return BatteryTelemetry.Offline("Headset offline or turned off");
                }

                int level = reply[0];
                if (level < 0 || level > 100)
                {
                    return BatteryTelemetry.Offline("Invalid battery reading");
                }

                byte chargingState = reply.Length >= 3 ? reply[2] : (byte)0;
                bool isCharging = (chargingState == 1 || chargingState == 2);

                return BatteryTelemetry.Online(level, isCharging ? BatteryState.Charging : BatteryState.Discharging);
            }
        }

        /// <summary>
        /// Navigates the Centurion feature tree to locate bridge and battery feature indices.
        /// </summary>
        private static bool DiscoverCenturionFeatures(SafeFileHandle hDev, CenturionDiscoveryInfo info)
        {
            // 1. Root -> FeatureSet (0x0001)
            byte[] fsReply = SendDirectRequest(hDev, (byte)FEATURE_ROOT, 0x00, new byte[] { (byte)(FEATURE_FEATURE_SET >> 8), (byte)(FEATURE_FEATURE_SET & 0xFF) });
            if (fsReply == null || fsReply.Length == 0 || fsReply[0] == 0)
            {
                return false;
            }
            byte featureSetIndex = fsReply[0];

            // 2. Query feature count from featureSetIndex
            byte[] countReply = SendDirectRequest(hDev, featureSetIndex, 0x00, null);
            if (countReply == null || countReply.Length == 0)
            {
                return false;
            }
            byte featureCount = countReply[0];

            // 3. Find CenturionBridge (0x0003)
            byte bridgeIndex = 0xFF;
            for (byte i = 0; i < featureCount; i++)
            {
                byte[] itemReply = SendDirectRequest(hDev, featureSetIndex, 0x10, new byte[] { i });
                if (itemReply != null && itemReply.Length >= 3)
                {
                    ushort featId = (ushort)((itemReply[1] << 8) | itemReply[2]);
                    if (featId == FEATURE_CENTURION_BRIDGE)
                    {
                        bridgeIndex = i;
                        break;
                    }
                }
            }

            if (bridgeIndex == 0xFF) return false;
            info.BridgeIndex = bridgeIndex;

            // 4. Discover sub-device FeatureSet over bridge
            byte[] subFsReply = SendBridgeRequest(hDev, bridgeIndex, (byte)FEATURE_ROOT, 0x00, new byte[] { (byte)(FEATURE_FEATURE_SET >> 8), (byte)(FEATURE_FEATURE_SET & 0xFF) });
            if (subFsReply == null || subFsReply.Length == 0 || subFsReply[0] == 0)
            {
                return false;
            }
            byte subFeatureSetIndex = subFsReply[0];

            // 5. Query sub-feature count
            byte[] subCountReply = SendBridgeRequest(hDev, bridgeIndex, subFeatureSetIndex, 0x00, null);
            if (subCountReply == null || subCountReply.Length == 0)
            {
                return false;
            }
            byte subFeatureCount = subCountReply[0];

            // 6. Find CenturionBatterySoc (0x0104)
            byte batteryIndex = 0xFF;
            for (byte i = 0; i < subFeatureCount; i++)
            {
                byte[] itemReply = SendBridgeRequest(hDev, bridgeIndex, subFeatureSetIndex, 0x10, new byte[] { i });
                if (itemReply != null && itemReply.Length >= 3)
                {
                    ushort featId = (ushort)((itemReply[1] << 8) | itemReply[2]);
                    if (featId == FEATURE_BATTERY_SOC)
                    {
                        batteryIndex = i;
                        break;
                    }
                }
            }

            if (batteryIndex == 0xFF) return false;
            info.BatterySubFeatureIndex = batteryIndex;
            info.Discovered = true;
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Frame Transmission & Reception
        // ═══════════════════════════════════════════════════════════════════════

        private static byte[] SendDirectRequest(SafeFileHandle hDev, byte featureIndex, byte function, byte[] parameters)
        {
            int paramLen = parameters != null ? parameters.Length : 0;
            byte[] payload = new byte[2 + paramLen];
            payload[0] = featureIndex;
            payload[1] = (byte)((function & 0xF0) | SOFTWARE_ID);
            if (paramLen > 0) Array.Copy(parameters, 0, payload, 2, paramLen);

            byte[] frame = BuildCenturionFrame(payload);
            if (!WriteFrame(hDev, frame)) return null;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                byte[] resp = ReadFrame(hDev, 150);
                if (resp == null) continue;

                byte[] reply = ExtractPayload(resp);
                if (reply != null && reply.Length >= 2 && reply[0] == featureIndex)
                {
                    byte[] data = new byte[reply.Length - 2];
                    Array.Copy(reply, 2, data, 0, data.Length);
                    return data;
                }
            }

            return null;
        }

        private static byte[] SendBridgeRequest(SafeFileHandle hDev, byte bridgeIndex, byte subFeatureIndex, byte function, byte[] parameters)
        {
            int paramLen = parameters != null ? parameters.Length : 0;
            byte[] subMsg = new byte[3 + paramLen];
            subMsg[0] = 0x00;
            subMsg[1] = subFeatureIndex;
            subMsg[2] = (byte)((function & 0xF0) | SOFTWARE_ID);
            if (paramLen > 0) Array.Copy(parameters, 0, subMsg, 3, paramLen);

            byte[] layer3 = new byte[4 + subMsg.Length];
            layer3[0] = bridgeIndex;
            layer3[1] = (byte)(BRIDGE_SEND_FRAGMENT_FN | SOFTWARE_ID);
            layer3[2] = (byte)((subMsg.Length >> 8) & 0x0F);
            layer3[3] = (byte)(subMsg.Length & 0xFF);
            Array.Copy(subMsg, 0, layer3, 4, subMsg.Length);

            byte[] frame = BuildCenturionFrame(layer3);
            if (!WriteFrame(hDev, frame)) return null;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                byte[] resp = ReadFrame(hDev, 250);
                if (resp == null) continue;

                byte[] reply = ExtractPayload(resp);
                if (reply == null || reply.Length < 6) continue;

                if (reply[0] == bridgeIndex)
                {
                    byte funcSw = reply[1];
                    if ((funcSw >> 4) == (BRIDGE_MESSAGE_EVENT_FN >> 4))
                    {
                        if (reply[5] == 0xFF) return null;

                        if (reply[5] == subFeatureIndex)
                        {
                            if (reply.Length >= 7)
                            {
                                byte[] data = new byte[reply.Length - 7];
                                Array.Copy(reply, 7, data, 0, data.Length);
                                return data;
                            }
                        }
                    }
                }
            }

            return null;
        }

        private static byte[] BuildCenturionFrame(byte[] payload)
        {
            byte[] frame = new byte[FRAME_SIZE];
            frame[0] = REPORT_ID;
            frame[1] = (byte)(payload.Length + 1);
            frame[2] = 0x00;

            int copyLen = Math.Min(payload.Length, FRAME_SIZE - 3);
            Array.Copy(payload, 0, frame, 3, copyLen);
            return frame;
        }

        private static byte[] ExtractPayload(byte[] frame)
        {
            if (frame == null || frame.Length < 4 || frame[0] != REPORT_ID) return null;
            int cpl = frame[1];
            if (cpl <= 1 || (cpl + 2) > frame.Length) return null;

            byte[] payload = new byte[cpl - 1];
            Array.Copy(frame, 3, payload, 0, payload.Length);
            return payload;
        }

        private static bool WriteFrame(SafeFileHandle hDev, byte[] frame)
        {
            uint written;
            bool ok = Win32HidNative.WriteFile(hDev, frame, (uint)frame.Length, out written, IntPtr.Zero);
            if (!ok)
            {
                ok = Win32HidNative.HidD_SetOutputReport(hDev, frame, (uint)frame.Length);
            }
            return ok;
        }

        private static byte[] ReadFrame(SafeFileHandle hDev, int timeoutMs)
        {
            byte[] buf = new byte[FRAME_SIZE];
            using (ManualResetEvent evt = new ManualResetEvent(false))
            {
                NativeOverlapped ov = new NativeOverlapped { EventHandle = evt.SafeWaitHandle.DangerousGetHandle() };
                IntPtr pOverlapped = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
                bool pendingIo = false;
                try
                {
                    Marshal.StructureToPtr(ov, pOverlapped, false);
                    uint bytesRead;
                    bool ok = Win32HidNative.ReadFile(hDev, buf, (uint)buf.Length, out bytesRead, pOverlapped);
                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (err == Win32HidNative.ERROR_IO_PENDING)
                        {
                            pendingIo = true;
                            if (evt.WaitOne(timeoutMs))
                            {
                                if (Win32HidNative.GetOverlappedResult(hDev, pOverlapped, out bytesRead, false))
                                {
                                    pendingIo = false;
                                    return buf;
                                }
                            }
                        }
                        return null;
                    }
                    return buf;
                }
                finally
                {
                    if (pendingIo)
                    {
                        uint bytesRead;
                        Win32HidNative.CancelIoEx(hDev, pOverlapped);
                        Win32HidNative.GetOverlappedResult(hDev, pOverlapped, out bytesRead, true);
                    }
                    Marshal.FreeHGlobal(pOverlapped);
                }
            }
        }
    }
}