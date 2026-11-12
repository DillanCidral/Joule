using System.Runtime.InteropServices;
using System.Text;

namespace BatteryTray.Scan;

/// <summary>
/// P/Invokes usados so pelo coletor. Todos SOMENTE LEITURA:
///  - setupapi: enumerar devnodes e ler propriedades (nao escreve nenhuma);
///  - hid: ler o descritor que o Windows ja tem carregado (preparsed data);
///  - bthprops: listar radios e dispositivos JA pareados. A busca por
///    dispositivos novos (inquiry) fica DESLIGADA de proposito: inquiry e'
///    operacao de radio ativa e pode atrapalhar conexoes existentes.
/// Nao existe aqui nenhuma chamada que escreva em dispositivo.
/// </summary>
internal static class ScanNative
{
    // ---------------- setupapi ----------------

    internal const int DIGCF_PRESENT = 0x02;
    internal const int DIGCF_ALLCLASSES = 0x04;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;

        public DEVPROPKEY(string guid, uint id)
        {
            fmtid = new Guid(guid);
            pid = id;
        }

        public override string ToString() => "{" + fmtid.ToString().ToUpperInvariant() + "}," + pid;
    }

    // Tipos DEVPROP (devpropdef.h).
    internal const uint DEVPROP_TYPE_EMPTY = 0x00;
    internal const uint DEVPROP_TYPE_NULL = 0x01;
    internal const uint DEVPROP_TYPE_SBYTE = 0x02;
    internal const uint DEVPROP_TYPE_BYTE = 0x03;
    internal const uint DEVPROP_TYPE_INT16 = 0x04;
    internal const uint DEVPROP_TYPE_UINT16 = 0x05;
    internal const uint DEVPROP_TYPE_INT32 = 0x06;
    internal const uint DEVPROP_TYPE_UINT32 = 0x07;
    internal const uint DEVPROP_TYPE_INT64 = 0x08;
    internal const uint DEVPROP_TYPE_UINT64 = 0x09;
    internal const uint DEVPROP_TYPE_GUID = 0x0D;
    internal const uint DEVPROP_TYPE_FILETIME = 0x10;
    internal const uint DEVPROP_TYPE_BOOLEAN = 0x11;
    internal const uint DEVPROP_TYPE_STRING = 0x12;
    internal const uint DEVPROP_TYPE_STRING_LIST = 0x2012;
    internal const uint DEVPROP_TYPE_BINARY = 0x1003;

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
    internal static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiEnumDeviceInfo(IntPtr devInfo, int index, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr devInfo);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDeviceInstanceIdW")]
    internal static extern bool SetupDiGetDeviceInstanceId(IntPtr devInfo, ref SP_DEVINFO_DATA data, StringBuilder buffer, int bufferLen, out int required);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool SetupDiGetDevicePropertyW(
        IntPtr devInfo, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key,
        out uint propertyType, byte[]? buffer, int bufferSize, out int requiredSize, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiGetDevicePropertyKeys(
        IntPtr devInfo, ref SP_DEVINFO_DATA data,
        [Out] DEVPROPKEY[]? keys, int keyArraySize, out int requiredKeys, uint flags);

    // ---------------- hid: caps do descritor ----------------

    internal const int HIDP_INPUT = 0;
    internal const int HIDP_OUTPUT = 1;
    internal const int HIDP_FEATURE = 2;

    /// <summary>
    /// HIDP_VALUE_CAPS de hidpi.h. Os campos reservados sao declarados um a um
    /// para a struct continuar blittable; se o tamanho nao bater com o da API,
    /// HidP_GetValueCaps devolve lixo -- por isso o ScanSelfTest confere o
    /// sizeof (72 bytes) antes de qualquer coisa.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct HIDP_VALUE_CAPS
    {
        public ushort UsagePage;
        public byte ReportID;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;
        public byte HasNull;
        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;
        public ushort Reserved2a, Reserved2b, Reserved2c, Reserved2d, Reserved2e;
        public uint UnitsExp;
        public uint Units;
        public int LogicalMin, LogicalMax;
        public int PhysicalMin, PhysicalMax;

        // Uniao Range/NotRange: 8 ushorts. Em NotRange, U0 e' o Usage.
        public ushort U0, U1, U2, U3, U4, U5, U6, U7;

        public ushort UsageMin => U0;
        public ushort UsageMax => IsRange != 0 ? U1 : U0;
    }

    [DllImport("hid.dll")]
    internal static extern int HidP_GetValueCaps(int reportType, [In, Out] HIDP_VALUE_CAPS[] caps, ref ushort capsLength, IntPtr preparsed);

    [DllImport("hid.dll", CharSet = CharSet.Unicode)]
    internal static extern bool HidD_GetSerialNumberString(IntPtr handle, byte[] buffer, int bufferLength);

    // ---------------- bluetooth (bthprops.cpl) ----------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct BLUETOOTH_FIND_RADIO_PARAMS
    {
        public int dwSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct BLUETOOTH_RADIO_INFO
    {
        public int dwSize;
        public ulong address;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string szName;
        public uint ulClassofDevice;
        public ushort lmpSubversion;
        public ushort manufacturer;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEMTIME
    {
        public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;

        public DateTime? ToDateTime()
        {
            if (wYear < 1601 || wMonth is < 1 or > 12 || wDay is < 1 or > 31) return null;
            try { return new DateTime(wYear, wMonth, wDay, wHour, wMinute, Math.Min(wSecond, (ushort)59), DateTimeKind.Utc); }
            catch { return null; }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct BLUETOOTH_DEVICE_INFO
    {
        public int dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        [MarshalAs(UnmanagedType.Bool)] public bool fConnected;
        [MarshalAs(UnmanagedType.Bool)] public bool fRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool fAuthenticated;
        public SYSTEMTIME stLastSeen;
        public SYSTEMTIME stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string szName;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BLUETOOTH_DEVICE_SEARCH_PARAMS
    {
        public int dwSize;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnAuthenticated;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnRemembered;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnUnknown;
        [MarshalAs(UnmanagedType.Bool)] public bool fReturnConnected;

        /// <summary>SEMPRE false: true dispara inquiry de radio, coisa que nao fazemos.</summary>
        [MarshalAs(UnmanagedType.Bool)] public bool fIssueInquiry;

        public byte cTimeoutMultiplier;
        public IntPtr hRadio;
    }

    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr BluetoothFindFirstRadio(ref BLUETOOTH_FIND_RADIO_PARAMS parms, out IntPtr hRadio);

    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool BluetoothFindNextRadio(IntPtr hFind, out IntPtr hRadio);

    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool BluetoothFindRadioClose(IntPtr hFind);

    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode)]
    internal static extern uint BluetoothGetRadioInfo(IntPtr hRadio, ref BLUETOOTH_RADIO_INFO info);

    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS parms, ref BLUETOOTH_DEVICE_INFO info);

    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BLUETOOTH_DEVICE_INFO info);

    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool BluetoothFindDeviceClose(IntPtr hFind);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);
}
