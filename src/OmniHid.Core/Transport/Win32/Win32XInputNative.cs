using System;
using System.Runtime.InteropServices;

namespace OmniHid.Core.Transport.Win32
{
    /// <summary>
    /// Native P/Invoke wrapper for XInput dynamic loading and battery query functions.
    /// Safely probes for xinput1_4.dll, xinput1_3.dll, or xinput9_1_0.dll at runtime.
    /// </summary>
    public static class Win32XInputNative
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Return Codes & Constants
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Operation completed successfully.</summary>
        public const int ERROR_SUCCESS = 0;

        /// <summary>The specified controller slot is not connected.</summary>
        public const int ERROR_DEVICE_NOT_CONNECTED = 1167;

        // Device types
        /// <summary>Query battery for the gamepad itself.</summary>
        public const byte BATTERY_DEVTYPE_GAMEPAD = 0x00;

        /// <summary>Query battery for an attached audio headset.</summary>
        public const byte BATTERY_DEVTYPE_HEADSET = 0x01;

        // Battery chemistry / connection types
        /// <summary>Controller is disconnected.</summary>
        public const byte BATTERY_TYPE_DISCONNECTED = 0x00;

        /// <summary>Controller is wired directly via USB cable (no wireless battery depletion).</summary>
        public const byte BATTERY_TYPE_WIRED        = 0x01;

        /// <summary>Controller uses disposable alkaline AA batteries.</summary>
        public const byte BATTERY_TYPE_ALKALINE     = 0x02;

        /// <summary>Controller uses rechargeable nickel-metal hydride (NiMH) battery pack.</summary>
        public const byte BATTERY_TYPE_NIMH         = 0x03;

        /// <summary>Controller power source cannot be determined by the driver.</summary>
        public const byte BATTERY_TYPE_UNKNOWN      = 0xFF;

        // Battery charge levels
        /// <summary>Battery is empty or critically depleted (~0-10%).</summary>
        public const byte BATTERY_LEVEL_EMPTY  = 0x00;

        /// <summary>Battery is low (~10-35%).</summary>
        public const byte BATTERY_LEVEL_LOW    = 0x01;

        /// <summary>Battery is at medium charge (~35-70%).</summary>
        public const byte BATTERY_LEVEL_MEDIUM = 0x02;

        /// <summary>Battery is full or nearly full (~70-100%).</summary>
        public const byte BATTERY_LEVEL_FULL   = 0x03;

        // ═══════════════════════════════════════════════════════════════════════
        // Native XInput Structures
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Contains battery type and charge level returned by XInputGetBatteryInformation.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_BATTERY_INFORMATION
        {
            public byte BatteryType;
            public byte BatteryLevel;
        }

        /// <summary>
        /// Represents current controller packet number and button/axis state.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_STATE
        {
            public uint PacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        /// <summary>
        /// Describes current button states, trigger analog values, and thumbstick axes.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        /// <summary>
        /// Describes controller hardware capabilities and vibration support.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_CAPABILITIES
        {
            public byte Type;
            public byte SubType;
            public ushort Flags;
            public XINPUT_GAMEPAD Gamepad;
            public ushort wLeftMotorSpeed;
            public ushort wRightMotorSpeed;
        }

        /// <summary>
        /// Specifies vibration motor speeds for haptic feedback.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_VIBRATION
        {
            public ushort wLeftMotorSpeed;
            public ushort wRightMotorSpeed;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Delegates & Dynamic Loading
        // ═══════════════════════════════════════════════════════════════════════

        private delegate int XInputGetBatteryInformationDelegate(int dwUserIndex, byte devType, out XINPUT_BATTERY_INFORMATION pBatteryInformation);
        private delegate int XInputGetStateDelegate(int dwUserIndex, out XINPUT_STATE pState);
        private delegate int XInputGetCapabilitiesDelegate(int dwUserIndex, uint dwFlags, out XINPUT_CAPABILITIES pCapabilities);
        private delegate int XInputSetStateDelegate(int dwUserIndex, ref XINPUT_VIBRATION pVibration);

        private static readonly XInputGetBatteryInformationDelegate _getBattery;
        private static readonly XInputGetStateDelegate _getState;
        private static readonly XInputGetCapabilitiesDelegate _getCapabilities;
        private static readonly XInputSetStateDelegate _setState;
        private static readonly bool _isLoaded;

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        static Win32XInputNative()
        {
            // Try loading in order of modern Windows to legacy fallback
            string[] libraries = { "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll" };
            foreach (var libName in libraries)
            {
                IntPtr hModule = LoadLibrary(libName);
                if (hModule != IntPtr.Zero)
                {
                    IntPtr pBattery = GetProcAddress(hModule, "XInputGetBatteryInformation");
                    IntPtr pState = GetProcAddress(hModule, "XInputGetState");
                    IntPtr pCaps = GetProcAddress(hModule, "XInputGetCapabilities");
                    IntPtr pSetState = GetProcAddress(hModule, "XInputSetState");

                    if (pBattery != IntPtr.Zero && pState != IntPtr.Zero)
                    {
                        _getBattery = (XInputGetBatteryInformationDelegate)Marshal.GetDelegateForFunctionPointer(pBattery, typeof(XInputGetBatteryInformationDelegate));
                        _getState = (XInputGetStateDelegate)Marshal.GetDelegateForFunctionPointer(pState, typeof(XInputGetStateDelegate));
                        if (pCaps != IntPtr.Zero)
                        {
                            _getCapabilities = (XInputGetCapabilitiesDelegate)Marshal.GetDelegateForFunctionPointer(pCaps, typeof(XInputGetCapabilitiesDelegate));
                        }
                        if (pSetState != IntPtr.Zero)
                        {
                            _setState = (XInputSetStateDelegate)Marshal.GetDelegateForFunctionPointer(pSetState, typeof(XInputSetStateDelegate));
                        }
                        _isLoaded = true;
                        break;
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        // Public API Wrappers
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Gets a value indicating whether an XInput dynamic link library was successfully located and loaded.
        /// </summary>
        public static bool IsAvailable
        {
            get { return _isLoaded; }
        }

        /// <summary>
        /// Retrieves battery chemistry and charge level for the controller at the given user slot (0..3).
        /// </summary>
        public static int GetBatteryInformation(int dwUserIndex, byte devType, out XINPUT_BATTERY_INFORMATION info)
        {
            if (!_isLoaded || _getBattery == null)
            {
                info = default(XINPUT_BATTERY_INFORMATION);
                return ERROR_DEVICE_NOT_CONNECTED;
            }
            return _getBattery(dwUserIndex, devType, out info);
        }

        /// <summary>
        /// Queries button and stick state for the controller at the given user slot (0..3).
        /// </summary>
        public static int GetState(int dwUserIndex, out XINPUT_STATE state)
        {
            if (!_isLoaded || _getState == null)
            {
                state = default(XINPUT_STATE);
                return ERROR_DEVICE_NOT_CONNECTED;
            }
            return _getState(dwUserIndex, out state);
        }

        /// <summary>
        /// Retrieves hardware capabilities for the controller at the given user slot (0..3).
        /// </summary>
        public static int GetCapabilities(int dwUserIndex, uint dwFlags, out XINPUT_CAPABILITIES caps)
        {
            if (!_isLoaded || _getCapabilities == null)
            {
                caps = default(XINPUT_CAPABILITIES);
                return ERROR_DEVICE_NOT_CONNECTED;
            }
            return _getCapabilities(dwUserIndex, dwFlags, out caps);
        }

        /// <summary>
        /// Sends vibration commands to the controller at the given user slot (0..3).
        /// </summary>
        public static int SetState(int dwUserIndex, ref XINPUT_VIBRATION vib)
        {
            if (!_isLoaded || _setState == null)
            {
                return ERROR_DEVICE_NOT_CONNECTED;
            }
            return _setState(dwUserIndex, ref vib);
        }
    }
}
