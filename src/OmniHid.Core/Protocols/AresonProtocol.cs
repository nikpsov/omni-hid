using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// Implements battery telemetry query protocol for Areson wireless gaming mouse MCUs.
    /// Used by ARDOR Gaming Prime X, Prime Wireless, and various Areson-based OEM peripherals.
    /// </summary>
    /// <remarks>
    /// Protocol Overview:
    /// - Command Report: 17 bytes via Feature Report (ID 0x08), starting with [0x08, 0x04, ...] and ending with a checksum.
    /// - Response Report: 17 bytes via Input Report (ID 0x09), where Byte 6 is battery percentage (0..100) and Byte 7 is charging flag.
    /// - Multi-interface concurrency: Uses pre-listening overlapped readers across all input endpoints to ensure asynchronous packet capture.
    /// </remarks>
    public class AresonProtocol : IProtocolHandler
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Protocol Constants
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Unique protocol identifier.</summary>
        public string ProtocolId { get { return "areson"; } }

        /// <summary>Human-readable display name of the protocol.</summary>
        public string ProtocolName { get { return "Areson Wireless MCU Protocol"; } }

        /// <summary>
        /// Gets a value indicating whether this protocol can query telemetry when no Windows HID interface handles exist.
        /// Areson devices require direct communication via HID Feature/Input reports.
        /// </summary>
        public bool CanQueryWithoutHidInterfaces { get { return false; } }

        /// <summary>Report ID used to send query commands via SetFeature.</summary>
        public const byte REPORT_ID_FEATURE_CMD = 0x08;

        /// <summary>Report ID returned by the mouse containing telemetry payload.</summary>
        public const byte REPORT_ID_INPUT_RESP  = 0x09;

        /// <summary>Command opcode for device status &amp; battery telemetry.</summary>
        public const byte CMD_QUERY_STATUS      = 0x04;

        /// <summary>Standard report frame length for Areson packets (17 bytes).</summary>
        public const int PACKET_LENGTH          = 17;

        // ═══════════════════════════════════════════════════════════════════════
        // Telemetry Query Implementation
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Queries the Areson wireless mouse for battery telemetry using multi-reader overlapped I/O.
        /// </summary>
        /// <param name="transport">Transport layer abstraction.</param>
        /// <param name="interfaces">List of HID interfaces associated with this mouse.</param>
        /// <param name="profile">Declarative profile information.</param>
        /// <returns>Populated <see cref="BatteryTelemetry"/> instance.</returns>
        public BatteryTelemetry QueryBattery(IHidTransport transport, List<HidDeviceInfo> interfaces, DeviceProfile profile)
        {
            if (interfaces == null || interfaces.Count == 0)
            {
                return BatteryTelemetry.Offline("Device not found");
            }

            // 1. Check Windows PnP battery property cache first
            foreach (var iface in interfaces)
            {
                int pnpLevel = transport.GetPnpBatteryLevel(iface.DevicePath);
                if (pnpLevel >= 0 && pnpLevel <= 100)
                {
                    return BatteryTelemetry.Online(pnpLevel, BatteryState.Discharging);
                }
            }

            byte[] cmdReport = BuildQueryCommand(CMD_QUERY_STATUS);

            List<HidDeviceInfo> featureCandidates = new List<HidDeviceInfo>();
            List<HidDeviceInfo> inputCandidates = new List<HidDeviceInfo>();

            foreach (var d in interfaces)
            {
                // Feature candidates: vendor collections (0xFF02, 0xFFFF, >= 0xFF00) or endpoints with FeatureReportByteLength >= 17
                if (d.FeatureReportByteLength >= 17 || d.UsagePage >= 0xFF00 || d.UsagePage == 0xFF02)
                    featureCandidates.Add(d);

                // Input candidates: readable endpoints with InputReportByteLength > 0
                // Exclude standard keyboard usage (0x0001:0x0006) which Windows kbdclass locks exclusively
                if (d.InputReportByteLength > 0 && !(d.UsagePage == 0x0001 && d.Usage == 0x0006))
                    inputCandidates.Add(d);
            }

            // Pin target interface if explicitly specified by profile (e.g. UsagePage 0xFF02 for Areson configuration)
            if (profile != null && profile.TargetUsagePage != 0)
            {
                for (int i = 0; i < interfaces.Count; i++)
                {
                    var d = interfaces[i];
                    if (d.UsagePage == profile.TargetUsagePage && (profile.TargetUsage == 0 || d.Usage == profile.TargetUsage))
                    {
                        featureCandidates.Remove(d);
                        featureCandidates.Insert(0, d);
                        inputCandidates.Remove(d);
                        inputCandidates.Insert(0, d);
                        break;
                    }
                }
            }

            if (featureCandidates.Count == 0) featureCandidates = interfaces;
            if (inputCandidates.Count == 0) inputCandidates = interfaces;

            return ExecuteOverlappedQuery(featureCandidates, inputCandidates, cmdReport);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Frame Construction & Overlapped I/O
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds a 17-byte command packet with Areson checksum byte at offset 16.
        /// </summary>
        private static byte[] BuildQueryCommand(byte cmd)
        {
            byte[] packet = new byte[PACKET_LENGTH];
            packet[0] = REPORT_ID_FEATURE_CMD;
            packet[1] = cmd;

            // Checksum algorithm: (0x55 - sum of bytes 0..15)
            byte sum = 0;
            for (int i = 0; i < PACKET_LENGTH - 1; i++)
            {
                sum += packet[i];
            }
            packet[PACKET_LENGTH - 1] = (byte)(0x55 - sum);
            return packet;
        }

        /// <summary>
        /// Initiates asynchronous pre-reads on candidate input endpoints, transmits the feature report command,
        /// and waits for the mouse response frame.
        /// </summary>
        private static BatteryTelemetry ExecuteOverlappedQuery(
            List<HidDeviceInfo> featureDevs,
            List<HidDeviceInfo> inputDevs,
            byte[] cmdReport)
        {
            List<ActiveReader> readers = new List<ActiveReader>();
            List<WaitHandle> waitHandles = new List<WaitHandle>();

            try
            {
                // 1. Pre-listen on all candidate input collections before sending command
                foreach (var inDev in inputDevs)
                {
                    SafeFileHandle hRead = Win32HidTransport.OpenDevice(inDev.DevicePath, Win32HidNative.GENERIC_READ, true);
                    if (!hRead.IsInvalid)
                    {
                        ActiveReader reader = new ActiveReader(inDev, hRead);
                        if (reader.StartRead())
                        {
                            readers.Add(reader);
                            waitHandles.Add(reader.WaitEvent);
                            if (waitHandles.Count >= 60) break;
                        }
                        else
                        {
                            reader.Dispose();
                        }
                    }
                }

                // 2. Send feature report command to candidate feature interface
                bool featureSent = false;
                foreach (var fDev in featureDevs)
                {
                    using (SafeFileHandle hWrite = Win32HidTransport.OpenDevice(fDev.DevicePath, Win32HidNative.GENERIC_WRITE, false))
                    {
                        if (!hWrite.IsInvalid)
                        {
                            // A. Standard Report ID 0x08 command frame
                            byte[] sendBuf = cmdReport;
                            if (fDev.FeatureReportByteLength > cmdReport.Length)
                            {
                                sendBuf = new byte[fDev.FeatureReportByteLength];
                                Array.Copy(cmdReport, sendBuf, cmdReport.Length);
                            }

                            if (Win32HidNative.HidD_SetFeature(hWrite, sendBuf, (uint)sendBuf.Length) ||
                                Win32HidNative.HidD_SetOutputReport(hWrite, sendBuf, (uint)sendBuf.Length))
                            {
                                featureSent = true;
                                break;
                            }

                            // B. Unnumbered Feature Report frame (Report ID 0x00 prefix for single-collection endpoints)
                            if (fDev.FeatureReportByteLength >= 65)
                            {
                                byte[] unnumbered = new byte[fDev.FeatureReportByteLength];
                                unnumbered[0] = 0x00;
                                Array.Copy(cmdReport, 0, unnumbered, 1, Math.Min(cmdReport.Length, unnumbered.Length - 1));

                                if (Win32HidNative.HidD_SetFeature(hWrite, unnumbered, (uint)unnumbered.Length) ||
                                    Win32HidNative.HidD_SetOutputReport(hWrite, unnumbered, (uint)unnumbered.Length))
                                {
                                    featureSent = true;
                                    break;
                                }
                            }

                            // C. Short 2-byte Output Report (supported by secondary keyboard endpoints)
                            if (fDev.OutputReportByteLength == 2)
                            {
                                byte[] shortOut = new byte[] { 0x00, 0x04 };
                                if (Win32HidNative.HidD_SetOutputReport(hWrite, shortOut, (uint)shortOut.Length))
                                {
                                    featureSent = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (!featureSent)
                {
                    return BatteryTelemetry.Offline("Failed to send feature report");
                }

                // 3. Event-driven wait with 1200ms total timeout
                Stopwatch sw = Stopwatch.StartNew();
                const int totalTimeoutMs = 1200;
                WaitHandle[] waitArr = waitHandles.ToArray();

                while (sw.ElapsedMilliseconds < totalTimeoutMs && waitHandles.Count > 0)
                {
                    int remaining = (int)(totalTimeoutMs - sw.ElapsedMilliseconds);
                    if (remaining <= 0) break;

                    int signaledIndex = WaitHandle.WaitAny(waitArr, Math.Min(remaining, 400));
                    if (signaledIndex != WaitHandle.WaitTimeout && signaledIndex >= 0 && signaledIndex < readers.Count)
                    {
                        var reader = readers[signaledIndex];
                        if (reader.CheckCompleted())
                        {
                            BatteryTelemetry bRes = ParseBatteryPacket(reader.Buffer);
                            if (bRes != null && bRes.IsAvailable)
                            {
                                return bRes;
                            }

                            // Discard extraneous streaming packet (e.g. mouse motion coordinates) and continue reading
                            if (reader.RestartRead())
                            {
                                continue;
                            }
                        }
                        waitHandles.RemoveAt(signaledIndex);
                        readers.RemoveAt(signaledIndex);
                        waitArr = waitHandles.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                return BatteryTelemetry.Offline("Query error: " + ex.Message);
            }
            finally
            {
                foreach (var r in readers)
                {
                    r.Dispose();
                }
            }

            return BatteryTelemetry.Offline("Device offline or sleeping");
        }

        /// <summary>
        /// Parses telemetry payload from an incoming Areson Input Report 0x09 frame.
        /// </summary>
        private static BatteryTelemetry ParseBatteryPacket(byte[] resp)
        {
            if (resp == null || resp.Length < 8) return null;

            int offset = 0;
            if (resp[0] == REPORT_ID_INPUT_RESP)
                offset = 0;
            else if (resp.Length > 1 && resp[1] == REPORT_ID_INPUT_RESP)
                offset = 1;

            if (resp[offset + 1] != CMD_QUERY_STATUS)
                return null;

            // Byte 6: Battery percentage (0..100)
            // Byte 7: Charging status (0x01 = Charging, 0x00 = Discharging)
            int batteryVal = resp[offset + 6];
            bool isCharging = (resp.Length > offset + 7) && (resp[offset + 7] != 0);

            // Alternate firmware offset fallback
            if (batteryVal == 0 && resp.Length > offset + 10 && resp[offset + 10] > 0 && resp[offset + 10] <= 100)
            {
                batteryVal = resp[offset + 10];
            }

            int level = Math.Min(100, Math.Max(0, batteryVal));
            BatteryState state;
            if (isCharging)
                state = BatteryState.Charging;
            else if (level >= 100)
                state = BatteryState.Full;
            else
                state = BatteryState.Discharging;

            return BatteryTelemetry.Online(level, state);
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Asynchronous Reader Tracking Class
        // ═══════════════════════════════════════════════════════════════════════

        private class ActiveReader : IDisposable
        {
            public HidDeviceInfo Device { get; private set; }
            public SafeFileHandle Handle { get; private set; }
            public byte[] Buffer { get; private set; }
            public ManualResetEvent WaitEvent { get; private set; }
            private IntPtr _pOverlapped;
            private bool _isPending = false;
            private bool _completed = false;

            public ActiveReader(HidDeviceInfo dev, SafeFileHandle handle)
            {
                Device = dev;
                Handle = handle;
                int len = dev.InputReportByteLength > 0 ? dev.InputReportByteLength : 17;
                Buffer = new byte[Math.Max(17, len)];
                Buffer[0] = REPORT_ID_INPUT_RESP;
                WaitEvent = new ManualResetEvent(false);

                NativeOverlapped ov = new NativeOverlapped { EventHandle = WaitEvent.SafeWaitHandle.DangerousGetHandle() };
                _pOverlapped = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
                Marshal.StructureToPtr(ov, _pOverlapped, false);
            }

            public bool StartRead()
            {
                uint bytesRead;
                bool ok = Win32HidNative.ReadFile(Handle, Buffer, (uint)Buffer.Length, out bytesRead, _pOverlapped);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == Win32HidNative.ERROR_IO_PENDING)
                    {
                        _isPending = true;
                        return true;
                    }
                    return false;
                }
                _completed = true;
                return true;
            }

            public bool CheckCompleted()
            {
                if (_completed) return true;
                if (!_isPending) return false;

                uint bytesTransferred;
                if (Win32HidNative.GetOverlappedResult(Handle, _pOverlapped, out bytesTransferred, false))
                {
                    _completed = true;
                    return true;
                }
                return false;
            }

            public bool RestartRead()
            {
                _completed = false;
                _isPending = false;
                WaitEvent.Reset();
                NativeOverlapped ov = new NativeOverlapped { EventHandle = WaitEvent.SafeWaitHandle.DangerousGetHandle() };
                Marshal.StructureToPtr(ov, _pOverlapped, false);

                uint bytesRead;
                bool ok = Win32HidNative.ReadFile(Handle, Buffer, (uint)Buffer.Length, out bytesRead, _pOverlapped);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == Win32HidNative.ERROR_IO_PENDING)
                    {
                        _isPending = true;
                        return true;
                    }
                    return false;
                }
                _completed = true;
                WaitEvent.Set();
                return true;
            }

            public void Dispose()
            {
                try
                {
                    if (_isPending && !_completed && !Handle.IsInvalid && !Handle.IsClosed)
                    {
                        Win32HidNative.CancelIoEx(Handle, _pOverlapped);
                        uint bytesRead;
                        Win32HidNative.GetOverlappedResult(Handle, _pOverlapped, out bytesRead, true);
                    }
                }
                catch { }

                if (_pOverlapped != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_pOverlapped);
                    _pOverlapped = IntPtr.Zero;
                }

                if (WaitEvent != null)
                {
                    WaitEvent.Close();
                    WaitEvent = null;
                }

                if (Handle != null && !Handle.IsInvalid && !Handle.IsClosed)
                {
                    Handle.Close();
                }
            }
        }
    }
}