using System.Runtime.InteropServices;
using System.Text;
using BatteryTray.Core;
using BatteryTray.Interop;
using static BatteryTray.Interop.HidNative;

namespace BatteryTray.Scan;

/// <summary>
/// Inventario HID detalhado. Alem do que o app ja enumera, le o numero de
/// serie e o descritor que o Windows tem em memoria (preparsed data), para
/// descobrir se o dispositivo DECLARA bateria de forma padronizada.
///
/// Todo handle aqui e' aberto com dwDesiredAccess = 0 -- so metadados, sem
/// leitura de dados e sem escrita. E' a mesma politica do HidInventory.
/// </summary>
public static class HidScan
{
    private const int MaxValueCapsPerCollection = 40;

    public static List<HidDeviceInfo> Collect(IReadOnlyList<HidCollection> collections, List<string> warnings)
    {
        var devices = new List<HidDeviceInfo>();

        foreach (var group in collections.GroupBy(c => c.HardwareId).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group.OrderBy(c => c.UsagePage).ThenBy(c => c.Usage).ToList();
            var first = ordered[0];

            var device = new HidDeviceInfo
            {
                HardwareId = group.Key,
                KnownName = Known.Name(first.Vid, first.Pid),
                Product = ordered.Select(c => c.Product).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "",
                Manufacturer = ordered.Select(c => c.Manufacturer).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "",
                Version = first.Version,
                Transport = HidUsages.Transport(first.Path),
            };

            foreach (var col in ordered)
            {
                var info = new HidCollectionInfo
                {
                    Path = col.Path,
                    UsagePage = $"0x{col.UsagePage:X4}",
                    UsagePageName = HidUsages.UsagePageName(col.UsagePage),
                    Usage = $"0x{col.Usage:X4}",
                    InputLen = col.InputLen,
                    OutputLen = col.OutputLen,
                    FeatureLen = col.FeatureLen,
                    SystemReserved = HidInventory.IsSystemReserved(col.UsagePage, col.Usage),
                };

                try
                {
                    Describe(col, info, device);
                }
                catch (Exception ex)
                {
                    info.CapsError = $"{ex.GetType().Name}: {ex.Message}";
                    warnings.Add($"HID {group.Key}: falha lendo caps ({ex.Message})");
                }

                device.Collections.Add(info);
            }

            device.DeclaredBattery = device.Collections
                .SelectMany(c => c.ValueCaps)
                .Where(v => v.Battery is not null)
                .Select(v => v.Battery!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            devices.Add(device);
        }

        return devices;
    }

    /// <summary>Abre com acesso 0 (so metadados), le serie e caps, fecha sempre.</summary>
    private static void Describe(HidCollection col, HidCollectionInfo info, HidDeviceInfo device)
    {
        IntPtr handle = CreateFile(col.Path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle == INVALID_HANDLE_VALUE)
        {
            info.CapsError = $"nao abriu para metadados (err {Marshal.GetLastWin32Error()})";
            return;
        }

        try
        {
            if (device.Serial.Length == 0)
            {
                string serial = ReadHidString(handle);
                if (serial.Length > 0) device.Serial = serial;
            }

            if (!HidD_GetPreparsedData(handle, out IntPtr preparsed))
            {
                info.CapsError = "HidD_GetPreparsedData falhou";
                return;
            }

            try
            {
                if (HidP_GetCaps(preparsed, out HIDP_CAPS caps) != HIDP_STATUS_SUCCESS)
                {
                    info.CapsError = "HidP_GetCaps falhou";
                    return;
                }

                var all = new List<HidValueCapInfo>();
                ReadValueCaps(preparsed, ScanNative.HIDP_INPUT, caps.NumberInputValueCaps, "input", all, info);
                ReadValueCaps(preparsed, ScanNative.HIDP_FEATURE, caps.NumberFeatureValueCaps, "feature", all, info);
                ReadValueCaps(preparsed, ScanNative.HIDP_OUTPUT, caps.NumberOutputValueCaps, "output", all, info);

                // Corta o excesso, mas as de bateria nunca sao cortadas.
                info.ValueCaps = all.Count <= MaxValueCapsPerCollection
                    ? all
                    : all.Where(v => v.Battery is not null)
                         .Concat(all.Where(v => v.Battery is null).Take(MaxValueCapsPerCollection))
                         .ToList();
            }
            finally
            {
                HidD_FreePreparsedData(preparsed);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static void ReadValueCaps(IntPtr preparsed, int reportType, ushort count, string label,
                                      List<HidValueCapInfo> into, HidCollectionInfo info)
    {
        if (count == 0) return;

        var buffer = new ScanNative.HIDP_VALUE_CAPS[count];
        ushort length = count;
        int status = ScanNative.HidP_GetValueCaps(reportType, buffer, ref length, preparsed);
        if (status != HIDP_STATUS_SUCCESS)
        {
            info.CapsError = $"HidP_GetValueCaps({label}) status 0x{status:X8}";
            return;
        }

        for (int i = 0; i < length && i < buffer.Length; i++)
        {
            var c = buffer[i];
            bool isRange = c.IsRange != 0;

            into.Add(new HidValueCapInfo
            {
                ReportType = label,
                ReportId = c.ReportID,
                UsagePage = $"0x{c.UsagePage:X4}",
                Usage = isRange ? $"0x{c.UsageMin:X4}-0x{c.UsageMax:X4}" : $"0x{c.UsageMin:X4}",
                BitSize = c.BitSize,
                ReportCount = c.ReportCount,
                LogicalMin = c.LogicalMin,
                LogicalMax = c.LogicalMax,
                Battery = HidUsages.BatteryUsage(c.UsagePage, c.UsageMin),
            });
        }
    }

    private static string ReadHidString(IntPtr handle)
    {
        var buffer = new byte[512];
        try
        {
            if (!ScanNative.HidD_GetSerialNumberString(handle, buffer, buffer.Length)) return "";
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
