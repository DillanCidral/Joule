using System.Runtime.InteropServices;

namespace BatteryTray.Interop;

/// <summary>
/// P/Invokes crus de setupapi / hid / kernel32. Nada aqui abre handle sozinho:
/// quem decide politica de acesso e' o HidInventory / HidPassiveReader.
/// </summary>
internal static class HidNative
{
    internal const int DIGCF_PRESENT = 0x02;
    internal const int DIGCF_DEVICEINTERFACE = 0x10;

    internal const uint GENERIC_READ = 0x80000000;
    internal const uint GENERIC_WRITE = 0x40000000;
    internal const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    internal const uint FILE_SHARE_READ = 0x01;
    internal const uint FILE_SHARE_WRITE = 0x02;
    internal const uint OPEN_EXISTING = 3;

    internal const int HIDP_STATUS_SUCCESS = 0x00110000;

    internal const int ERROR_OPERATION_ABORTED = 995;
    internal const int ERROR_IO_PENDING = 997;
    internal const int ERROR_DEVICE_NOT_CONNECTED = 1167;

    internal static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
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

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiEnumDeviceInterfaces(IntPtr devInfo, IntPtr devInfoData, ref Guid interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr devInfo, ref SP_DEVICE_INTERFACE_DATA ifData, IntPtr detail, int detailSize, ref int required, ref SP_DEVINFO_DATA infoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr devInfo);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr security, uint creationDisposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool ReadFile(IntPtr handle, byte[] buffer, int count, out int read, IntPtr overlapped);

    /// <summary>So o HidRequest usa: e' a unica escrita do projeto.</summary>
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "WriteFile")]
    internal static extern bool WriteFileOverlapped(IntPtr handle, IntPtr buffer, int count, IntPtr written, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetOverlappedResult(IntPtr handle, IntPtr overlapped, out int transferred, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CancelIoEx(IntPtr handle, IntPtr overlapped);

    [DllImport("hid.dll")]
    internal static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("hid.dll")]
    internal static extern bool HidD_GetAttributes(IntPtr handle, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll")]
    internal static extern bool HidD_GetPreparsedData(IntPtr handle, out IntPtr preparsed);

    [DllImport("hid.dll")]
    internal static extern bool HidD_FreePreparsedData(IntPtr preparsed);

    [DllImport("hid.dll")]
    internal static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);

    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    internal static extern bool HidD_GetProductString(IntPtr handle, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    internal static extern bool HidD_GetManufacturerString(IntPtr handle, byte[] buffer, int bufferLength);

    /// <summary>So o HidRequest.Feature usa (Razer): pergunta e resposta por relatorio de feature.</summary>
    [DllImport("hid.dll", SetLastError = true)]
    internal static extern bool HidD_SetFeature(IntPtr handle, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    internal static extern bool HidD_GetFeature(IntPtr handle, byte[] buffer, int bufferLength);

    [DllImport("hid.dll")]
    internal static extern bool HidD_SetNumInputBuffers(IntPtr handle, int numberBuffers);
}
