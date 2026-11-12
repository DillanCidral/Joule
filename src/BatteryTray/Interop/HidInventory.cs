using System.Runtime.InteropServices;
using System.Text;
using static BatteryTray.Interop.HidNative;

namespace BatteryTray.Interop;

/// <summary>Uma colecao HID presente no sistema (um "top level collection").</summary>
public sealed class HidCollection
{
    public required string Path { get; init; }
    public ushort Vid { get; init; }
    public ushort Pid { get; init; }
    public ushort Version { get; init; }
    public ushort UsagePage { get; init; }
    public ushort Usage { get; init; }
    public int InputLen { get; init; }
    public int OutputLen { get; init; }
    public int FeatureLen { get; init; }
    public string Product { get; init; } = "";
    public string Manufacturer { get; init; } = "";

    public string HardwareId => $"{Vid:X4}:{Pid:X4}";

    public override string ToString() =>
        $"{HardwareId} page=0x{UsagePage:X4} usage=0x{UsagePage:X4}/{Usage:X2} in={InputLen} \"{Product}\"";
}

/// <summary>
/// Enumeracao SOMENTE-LEITURA de metadados HID.
/// Todo handle aberto aqui usa dwDesiredAccess = 0 (so metadados), fechado
/// imediatamente. Nunca abre para leitura de dados, nunca escreve.
/// </summary>
public static class HidInventory
{
    /// <summary>
    /// Colecoes que o Windows reserva para si (mouse/teclado de sistema).
    /// Abrir estas para leitura falha ou trava; nunca tocar.
    /// </summary>
    public static bool IsSystemReserved(ushort usagePage, ushort usage)
        => usagePage == 0x0001 && (usage == 0x0002 || usage == 0x0006);

    public static List<HidCollection> Scan()
    {
        var result = new List<HidCollection>();
        HidD_GetHidGuid(out Guid hidGuid);

        IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == INVALID_HANDLE_VALUE) return result;

        try
        {
            for (int index = 0; ; index++)
            {
                var ifData = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index, ref ifData)) break;

                string? path = GetDetailPath(set, ref ifData);
                if (path is null) continue;

                var col = Describe(path);
                if (col is not null) result.Add(col);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return result;
    }

    private static string? GetDetailPath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA ifData)
    {
        var info = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
        int required = 0;
        SetupDiGetDeviceInterfaceDetail(set, ref ifData, IntPtr.Zero, 0, ref required, ref info);
        if (required <= 0) return null;

        IntPtr buffer = Marshal.AllocHGlobal(required);
        try
        {
            // cbSize do SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 em x64, 6 em x86.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize);
            int written = 0;
            if (!SetupDiGetDeviceInterfaceDetail(set, ref ifData, buffer, required, ref written, ref info)) return null;
            return Marshal.PtrToStringUni(buffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Abre com acesso 0 so para ler atributos/caps/strings. Fecha sempre. Publico para o aviso de conexao descrever SO a colecao que entrou.</summary>
    public static HidCollection? Describe(string path)
    {
        IntPtr handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == INVALID_HANDLE_VALUE) return null;

        try
        {
            var attrs = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
            if (!HidD_GetAttributes(handle, ref attrs)) return null;

            if (!HidD_GetPreparsedData(handle, out IntPtr preparsed)) return null;
            HIDP_CAPS caps;
            try
            {
                if (HidP_GetCaps(preparsed, out caps) != HIDP_STATUS_SUCCESS) return null;
            }
            finally
            {
                HidD_FreePreparsedData(preparsed);
            }

            return new HidCollection
            {
                Path = path,
                Vid = attrs.VendorID,
                Pid = attrs.ProductID,
                Version = attrs.VersionNumber,
                UsagePage = caps.UsagePage,
                Usage = caps.Usage,
                InputLen = caps.InputReportByteLength,
                OutputLen = caps.OutputReportByteLength,
                FeatureLen = caps.FeatureReportByteLength,
                Product = ReadString(handle, HidD_GetProductString),
                Manufacturer = ReadString(handle, HidD_GetManufacturerString),
            };
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private delegate bool StringGetter(IntPtr handle, byte[] buffer, int length);

    private static string ReadString(IntPtr handle, StringGetter getter)
    {
        var buffer = new byte[512];
        try
        {
            if (!getter(handle, buffer, buffer.Length)) return "";
            string s = Encoding.Unicode.GetString(buffer);
            int nul = s.IndexOf('\0');
            if (nul >= 0) s = s[..nul];
            return s.Trim();
        }
        catch
        {
            return "";
        }
    }
}
