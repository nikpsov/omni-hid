using System;
using System.Collections.Generic;
using OmniHid.Core.Abstractions;
using OmniHid.Core.Transport;

namespace OmniHid.Core.Diagnostics
{
    /// <summary>
    /// Confidence level of the heuristic IC fingerprinting match.
    /// </summary>
    public enum IcFingerprintConfidence
    {
        None,
        Low,
        Medium,
        High
    }

    /// <summary>
    /// Result structure produced by the hardware IC fingerprinting engine.
    /// </summary>
    public class IcFingerprintResult
    {
        /// <summary>Family or OEM vendor name (e.g. "CompX / Areson", "ROYUAN / YiChip").</summary>
        public string ChipsetFamily { get; set; }

        /// <summary>Confidence score of the heuristic match.</summary>
        public IcFingerprintConfidence Confidence { get; set; }

        /// <summary>Technical description of the architecture and characteristic traits.</summary>
        public string Description { get; set; }

        /// <summary>Recommended reverse engineering / protocol polling approach.</summary>
        public string RecommendedApproach { get; set; }

        /// <summary>Existing OmniHID protocol ID that likely handles this architecture (or null if unsupported).</summary>
        public string MatchedProtocolId { get; set; }

        /// <summary>True if this hardware is known to NOT be a battery-powered peripheral (e.g. SuperIO, DDC monitor).</summary>
        public bool IsNonBatteryDevice { get; set; }
    }

    /// <summary>
    /// Analyzes USB HID interface collections, report lengths, and vendor identifiers to recognize
    /// OEM microcontroller and firmware architectures (CompX, Areson, ROYUAN/YiChip, SinoWealth, etc.).
    /// </summary>
    public static class IcFingerprinter
    {
        /// <summary>
        /// Analyzes a peripheral device and its aggregated HID endpoints to classify the underlying chipset architecture.
        /// </summary>
        public static IcFingerprintResult Identify(ushort vid, ushort pid, IReadOnlyList<HidDeviceInfo> interfaces, string devName = null)
        {
            if (interfaces == null || interfaces.Count == 0)
            {
                return new IcFingerprintResult
                {
                    ChipsetFamily = "Unknown Hardware",
                    Confidence = IcFingerprintConfidence.None,
                    Description = "No HID interfaces available for inspection.",
                    RecommendedApproach = "Ensure device is connected and accessible."
                };
            }

            int vendorEps = 0;
            bool has65BFeature = false;
            bool has65BInput = false;
            bool has17BFeature = false;
            bool has8BFeature = false;
            bool has17BInput = false;
            bool hasVendorFF01_FF04 = false;
            bool hasVendorFFFF = false;
            bool hasVendorFFA0_FF00 = false;
            bool hasBatteryPage85 = false;
            bool hasPowerPage84 = false;
            bool hasKeyboardEp = false;

            for (int i = 0; i < interfaces.Count; i++)
            {
                var ep = interfaces[i];
                if (ep.UsagePage >= 0xFF00) vendorEps++;
                if (ep.UsagePage == 0x0085) hasBatteryPage85 = true;
                if (ep.UsagePage == 0x0084) hasPowerPage84 = true;
                if (ep.UsagePage == 0x0001 && ep.Usage == 0x0006) hasKeyboardEp = true;

                if (ep.UsagePage >= 0xFF01 && ep.UsagePage <= 0xFF04) hasVendorFF01_FF04 = true;
                if (ep.UsagePage == 0xFFFF) hasVendorFFFF = true;
                if (ep.UsagePage == 0xFFA0 || ep.UsagePage == 0xFF00) hasVendorFFA0_FF00 = true;

                if (ep.FeatureReportByteLength >= 65) has65BFeature = true;
                if (ep.FeatureReportByteLength == 17) has17BFeature = true;
                if (ep.FeatureReportByteLength == 8) has8BFeature = true;

                if (ep.InputReportByteLength >= 65) has65BInput = true;
                if (ep.InputReportByteLength == 17) has17BInput = true;
            }

            // ── 1. Non-Peripheral Hardware Filters ────────────────────────────

            // ITE Tech SuperIO / Motherboard RGB Controller
            if (vid == 0x048D && interfaces.Count <= 4)
            {
                bool hasFF89 = false;
                foreach (var ep in interfaces)
                {
                    if (ep.UsagePage == 0xFF89) hasFF89 = true;
                }
                if (hasFF89)
                {
                    return new IcFingerprintResult
                    {
                        ChipsetFamily = "ITE Tech Embedded Controller / SuperIO",
                        Confidence = IcFingerprintConfidence.High,
                        IsNonBatteryDevice = true,
                        Description = "Motherboard / RGB lighting hardware controller (ITE IT82xx). Not a wireless peripheral.",
                        RecommendedApproach = "Ignore for battery telemetry. No battery is present on this controller."
                    };
                }
            }

            // LG Monitor / DDC-CI Display Controls
            if (vid == 0x043E)
            {
                return new IcFingerprintResult
                {
                    ChipsetFamily = "LG Electronics Display Controller",
                    Confidence = IcFingerprintConfidence.High,
                    IsNonBatteryDevice = true,
                    Description = "Monitor On-Screen Display (OSD) and DDC/CI USB management interface.",
                    RecommendedApproach = "Ignore for battery telemetry. Mains-powered desktop display."
                };
            }

            // ── 2. Standard USB HID Battery / Power Class (0x0085 / 0x0084) ───
            if (hasBatteryPage85 || hasPowerPage84)
            {
                return new IcFingerprintResult
                {
                    ChipsetFamily = hasBatteryPage85 ? "Standard USB HID Battery Class (0x0085)" : "Standard USB HID Power Device (0x0084)",
                    Confidence = IcFingerprintConfidence.High,
                    Description = "Device declares standard USB HID Battery/Power Usage Page (0x0085 / 0x0084).",
                    RecommendedApproach = "Read standard Input/Feature report on Usage Page 0x0085 / 0x0084.",
                    MatchedProtocolId = "generic-peripheral"
                };
            }

            // ── 3. ROYUAN / YiChip Microelectronics ───────────────────────────
            // Characteristic traits: 65-byte Feature Reports on standard keyboard endpoint (0x0001:0x0006)
            // or 0xFFFF vendor endpoint. Responds to numbered/unnumbered 0x83/0x80/0x8F queries.
            // Found in: Akko, Machenike, Keychron, Ajazz, MonsGeek, Epomaker.
            if (vid == 0x3151 || (has65BFeature && (hasVendorFFFF || hasKeyboardEp && (vid == 0x25A7 || vid == 0x0461))))
            {
                return new IcFingerprintResult
                {
                    ChipsetFamily = "ROYUAN / YiChip (Multi-modes Wireless Keyboard/Mouse)",
                    Confidence = (vid == 0x3151) ? IcFingerprintConfidence.High : IcFingerprintConfidence.Medium,
                    Description = "65-byte Feature Reports on Keyboard endpoint. Responds to Feature 0x83 (query) with battery in byte[4].",
                    RecommendedApproach = "Send SetFeature 0x83 (or unnumbered 65B starting with 0x00 0x83) then GetFeature 0x83. Battery is at byte 4.",
                    MatchedProtocolId = "royuan"
                };
            }

            // ── 4. CompX / Areson (2.4G Dual-Mode) ─────────────────────────────
            // Characteristic traits: Vendor endpoints 0xFF01..0xFF04 with InLen 17B, FeatLen 8B/17B.
            // Feature 0x06 has battery level at byte 3 and charging status at byte 4.
            // Found in: ARDOR Gaming, Redragon, DEXP, Ajazz, Delux, Genesis, Motospeed.
            if ((vid == 0x25A7 || vid == 0x24AE) && (hasVendorFF01_FF04 || (has17BInput && (has8BFeature || has17BFeature))))
            {
                return new IcFingerprintResult
                {
                    ChipsetFamily = "CompX / Areson (2.4G Wireless Dual-Mode)",
                    Confidence = IcFingerprintConfidence.High,
                    Description = "Vendor-defined endpoints on 0xFF01..0xFF04 with 17B Input and 8B/17B Feature reports. Feature 0x06 holds DPI & Battery.",
                    RecommendedApproach = "Read Feature Report 0x06: byte[3] is Battery % (0..100), byte[4] is Charging Flag (0=Discharging, 1=Charging). Also probe Feature 0x08.",
                    MatchedProtocolId = "areson"
                };
            }

            // ── 5. Logitech HID++ ─────────────────────────────────────────────
            if (vid == 0x046D)
            {
                return new IcFingerprintResult
                {
                    ChipsetFamily = "Logitech (HID++ 1.0 / 2.0 Protocol)",
                    Confidence = IcFingerprintConfidence.High,
                    Description = "Proprietary Logitech Unifying / Lightspeed wireless communication architecture.",
                    RecommendedApproach = "Send HID++ 2.0 Unified Battery Feature query (0x1000 or 0x1004) over Report 0x10/0x11, or Centurion packet on 0xFF13/0xFFA0.",
                    MatchedProtocolId = "logitech-hidpp"
                };
            }

            // ── 6. SinoWealth Electronics (SH68Fxxx) ──────────────────────────
            // Characteristic traits: VID 0x258A with 65-byte buffers or vendor pages 0xFFA0.
            // Found in: Glorious, Fantech, Redragon mice.
            if (vid == 0x258A || (has65BInput && hasVendorFFA0_FF00))
            {
                return new IcFingerprintResult
                {
                    ChipsetFamily = "SinoWealth (SH68Fxxx Microcontroller)",
                    Confidence = (vid == 0x258A) ? IcFingerprintConfidence.High : IcFingerprintConfidence.Medium,
                    Description = "Widely used in gaming mice. Reports battery percentage via spontaneous Input Reports on vendor collections.",
                    RecommendedApproach = "Sniff spontaneous vendor input packets or send 65-byte feature query with magic byte 0x02.",
                    MatchedProtocolId = "sinowealth"
                };
            }

            // ── 7. Telink Semiconductor (TLSR82xx) ─────────────────────────────
            if (vid == 0x248A || vid == 0x1915 && hasVendorFF01_FF04)
            {
                return new IcFingerprintResult
                {
                    ChipsetFamily = "Telink Semiconductor (TLSR82xx Bluetooth / 2.4G SoC)",
                    Confidence = IcFingerprintConfidence.Medium,
                    Description = "Multi-protocol SoC. Transmits battery status via standard BLE GATT Battery Service or proprietary 2.4G frames.",
                    RecommendedApproach = "Run A-B calibration or live sniffer while interacting with device."
                };
            }

            // ── 8. Generic Vendor Endpoint Peripheral ─────────────────────────
            if (vendorEps > 0)
            {
                return new IcFingerprintResult
                {
                    ChipsetFamily = string.Format("Generic OEM Wireless ({0} Vendor Endpoints)", vendorEps),
                    Confidence = IcFingerprintConfidence.Low,
                    Description = string.Format("Device exposes {0} vendor-defined collection(s). Specific IC signature is unindexed.", vendorEps),
                    RecommendedApproach = "Use option [7] A-B Guided Calibration or option [4] Hunter to reverse-engineer report offsets."
                };
            }

            // ── 9. Fallback Standard Desktop ──────────────────────────────────
            return new IcFingerprintResult
            {
                ChipsetFamily = "Standard HID Peripheral (No Vendor Endpoints)",
                Confidence = IcFingerprintConfidence.Low,
                Description = "No vendor-specific endpoints detected. Device relies on OS PnP driver or basic desktop reports.",
                RecommendedApproach = "Check if Windows PnP provides DEVPKEY_Device_BatteryLevel or run Live Sniffer."
            };
        }
    }
}
