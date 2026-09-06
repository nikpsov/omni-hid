using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace OmniHid.Core.Transport.Win32
{
    /// <summary>
    /// Native Win32 API declarations, structures, and constants for HID, SetupAPI, and CfgMgr32.
    /// </summary>
    public static class Win32HidNative
    {
        // ═══════════════════════════════════════════════════════════════════════
        // Win32 File Access & Sharing Flags
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Read access right for CreateFile.</summary>
        public const uint GENERIC_READ  = 0x80000000;

        /// <summary>Write access right for CreateFile.</summary>
        public const uint GENERIC_WRITE = 0x40000000;

        /// <summary>Enables subsequent open operations on an object to request read access.</summary>
        public const uint FILE_SHARE_READ  = 0x00000001;

        /// <summary>Enables subsequent open operations on an object to request write access.</summary>
        public const uint FILE_SHARE_WRITE = 0x00000002;

        /// <summary>Opens an existing file or device. If the device does not exist, the function fails.</summary>
        public const uint OPEN_EXISTING = 3;

        /// <summary>The file or device is opened for asynchronous (overlapped) I/O.</summary>
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        /// <summary>Win32 error code indicating an asynchronous I/O operation is in progress.</summary>
        public const int ERROR_IO_PENDING = 997;

        // ═══════════════════════════════════════════════════════════════════════
        // SetupAPI Device Enumeration Flags
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>Return only devices that are currently present in the system.</summary>
        public const uint DIGCF_PRESENT = 0x00000002;

        /// <summary>Return devices that support device interfaces for the specified class.</summary>
        public const uint DIGCF_DEVICEINTERFACE = 0x00000010;

        // ═══════════════════════════════════════════════════════════════════════
        // Native Structures
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Defines a device interface in a device information set (SetupAPI).
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid interfaceClassGuid;
            public uint flags;
            public IntPtr reserved;
        }

        /// <summary>
        /// Contains vendor, product, and version attributes of a HID device.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct HIDD_ATTRIBUTES
        {
            public uint Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        /// <summary>
        /// Contains information about a top-level collection's capabilities and report lengths.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        /// <summary>
        /// Represents a unified property key in the Windows Vista/7/10/11 PnP property model.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct DEVPROPKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        /// <summary>
        /// DEVPKEY_Device_BatteryLevel: {104ea319-6ee2-4701-bd47-8ddbf425bbe5}, PID 2.
        /// Exposed by Windows Bluetooth GATT driver for supported wireless peripherals.
        /// </summary>
        public static readonly DEVPROPKEY DEVPKEY_Device_BatteryLevel = new DEVPROPKEY
        {
            fmtid = new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5"),
            pid = 2
        };

        public const int DBT_DEVTYP_DEVICEINTERFACE = 5;
        public const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct DEV_BROADCAST_DEVICEINTERFACE
        {
            public int dbcc_size;
            public int dbcc_devicetype;
            public int dbcc_reserved;
            public Guid dbcc_classguid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string dbcc_name;
        }

        // ═══════════════════════════════════════════════════════════════════════
        // hid.dll Imports
        // ═══════════════════════════════════════════════════════════════════════

        [DllImport("hid.dll", SetLastError = true)]
        public static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_GetAttributes(SafeFileHandle hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern int HidP_GetCaps(IntPtr preparsedData, ref HIDP_CAPS capabilities);

        [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_GetManufacturerString(
            SafeFileHandle hidDeviceObject,
            [Out] byte[] buffer,
            uint bufferLength);

        [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_GetProductString(
            SafeFileHandle hidDeviceObject,
            [Out] byte[] buffer,
            uint bufferLength);

        [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_GetSerialNumberString(
            SafeFileHandle hidDeviceObject,
            [Out] byte[] buffer,
            uint bufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_GetFeature(
            SafeFileHandle hidDeviceObject,
            [In, Out] byte[] reportBuffer,
            uint reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_SetFeature(
            SafeFileHandle hidDeviceObject,
            [In] byte[] reportBuffer,
            uint reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_SetOutputReport(
            SafeFileHandle hidDeviceObject,
            [In] byte[] reportBuffer,
            uint reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool HidD_GetInputReport(
            SafeFileHandle HidDeviceObject,
            [In, Out] byte[] ReportBuffer,
            uint ReportBufferLength);

        // ═══════════════════════════════════════════════════════════════════════
        // setupapi.dll Imports
        // ═══════════════════════════════════════════════════════════════════════

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            IntPtr enumerator,
            IntPtr hwndParent,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        // ═══════════════════════════════════════════════════════════════════════
        // kernel32.dll Imports
        // ═══════════════════════════════════════════════════════════════════════

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WriteFile(
            SafeFileHandle hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "ReadFile")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadFile(
            SafeFileHandle hFile,
            [Out] byte[] lpBuffer,
            uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead,
            IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "ReadFile")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadFile(
            SafeFileHandle hFile,
            [Out] byte[] lpBuffer,
            uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead,
            [In] ref NativeOverlapped lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetOverlappedResult")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetOverlappedResult(
            SafeFileHandle hFile,
            IntPtr lpOverlapped,
            out uint lpNumberOfBytesTransferred,
            bool bWait);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetOverlappedResult")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetOverlappedResult(
            SafeFileHandle hFile,
            [In] ref NativeOverlapped lpOverlapped,
            out uint lpNumberOfBytesTransferred,
            bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CancelIo(SafeFileHandle hFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CancelIoEx(SafeFileHandle hFile, IntPtr lpOverlapped);

        // ═══════════════════════════════════════════════════════════════════════
        // cfgmgr32.dll Imports (Windows PnP Configuration Manager)
        // ═══════════════════════════════════════════════════════════════════════

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int CM_Get_Device_Interface_PropertyW(
            string pszDeviceInterface,
            ref DEVPROPKEY PropertyKey,
            out uint PropertyType,
            byte[] PropertyBuffer,
            ref uint PropertyBufferSize,
            uint ulFlags);

        // ═══════════════════════════════════════════════════════════════════════
        // user32.dll Imports (PnP Window Device Notifications)
        // ═══════════════════════════════════════════════════════════════════════

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr RegisterDeviceNotification(
            IntPtr hRecipient,
            IntPtr notificationFilter,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterDeviceNotification(IntPtr handle);

        // ═══════════════════════════════════════════════════════════════════════
        // user32.dll Window Management & Message Pump (Zero WinForms dependency)
        // ═══════════════════════════════════════════════════════════════════════

        public static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);
        public const int WM_DESTROY = 0x0002;
        public const int WM_QUIT = 0x0012;
        public const int WM_DEVICECHANGE = 0x0219;
        public const int DBT_DEVICEARRIVAL = 0x8000;
        public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

        public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public WndProcDelegate lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern ushort RegisterClassEx([In] ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool TranslateMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern void PostQuitMessage(int nExitCode);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}