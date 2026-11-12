using System.Runtime.InteropServices;
using static BatteryTray.Scan.ScanNative;

namespace BatteryTray.Scan;

/// <summary>
/// Radios e dispositivos Bluetooth JA pareados/conectados.
///
/// A busca ativa (inquiry) fica desligada de proposito: ela e' uma operacao de
/// radio que pode atrapalhar conexoes em uso, e nao precisamos dela -- o que
/// interessa e' o que ja esta pareado nesta maquina. Nada e' escrito, nada e'
/// pareado, nada e' desconectado.
/// </summary>
public static class BluetoothScan
{
    public static BluetoothInfo Collect(List<string> warnings)
    {
        var result = new BluetoothInfo();
        var radios = new List<IntPtr>();

        try
        {
            var findParams = new BLUETOOTH_FIND_RADIO_PARAMS { dwSize = Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
            IntPtr find = BluetoothFindFirstRadio(ref findParams, out IntPtr radio);
            if (find == IntPtr.Zero)
            {
                result.RadioPresent = false;
                return result;
            }

            try
            {
                do { radios.Add(radio); } while (BluetoothFindNextRadio(find, out radio));
            }
            finally
            {
                BluetoothFindRadioClose(find);
            }

            result.RadioPresent = radios.Count > 0;

            foreach (IntPtr handle in radios)
            {
                var info = new BLUETOOTH_RADIO_INFO { dwSize = Marshal.SizeOf<BLUETOOTH_RADIO_INFO>() };
                if (BluetoothGetRadioInfo(handle, ref info) == 0)
                {
                    result.Radios.Add(new BluetoothRadioInfo
                    {
                        Name = info.szName ?? "",
                        Address = FormatAddress(info.address),
                        ClassOfDevice = info.ulClassofDevice,
                        Manufacturer = info.manufacturer,
                    });
                }

                CollectDevices(handle, result, warnings);
            }
        }
        catch (DllNotFoundException)
        {
            result.Error = "bthprops.cpl indisponivel: maquina sem pilha Bluetooth";
        }
        catch (Exception ex)
        {
            result.Error = $"{ex.GetType().Name}: {ex.Message}";
            warnings.Add($"Bluetooth: {ex.Message}");
        }
        finally
        {
            foreach (IntPtr handle in radios)
            {
                try { CloseHandle(handle); } catch { }
            }
        }

        return result;
    }

    private static void CollectDevices(IntPtr radio, BluetoothInfo result, List<string> warnings)
    {
        var search = new BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            dwSize = Marshal.SizeOf<BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
            fReturnAuthenticated = true,
            fReturnRemembered = true,
            fReturnConnected = true,
            fReturnUnknown = false,
            fIssueInquiry = false, // nunca: inquiry e' varredura ativa de radio
            cTimeoutMultiplier = 0,
            hRadio = radio,
        };

        var device = new BLUETOOTH_DEVICE_INFO { dwSize = Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>() };
        IntPtr find = BluetoothFindFirstDevice(ref search, ref device);
        if (find == IntPtr.Zero) return;

        try
        {
            do
            {
                string address = FormatAddress(device.Address);
                if (result.Devices.Any(d => d.Address == address)) continue;

                result.Devices.Add(new BluetoothDeviceInfoDto
                {
                    Name = (device.szName ?? "").Trim(),
                    Address = address,
                    Connected = device.fConnected,
                    Paired = device.fRemembered,
                    Authenticated = device.fAuthenticated,
                    ClassOfDevice = device.ulClassofDevice,
                    DeviceKind = HidUsages.DeviceKindFromCod(device.ulClassofDevice),
                    LastSeenUtc = device.stLastSeen.ToDateTime(),
                    LastUsedUtc = device.stLastUsed.ToDateTime(),
                });

                device = new BLUETOOTH_DEVICE_INFO { dwSize = Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>() };
            }
            while (BluetoothFindNextDevice(find, ref device));
        }
        catch (Exception ex)
        {
            warnings.Add($"Bluetooth (dispositivos): {ex.Message}");
        }
        finally
        {
            BluetoothFindDeviceClose(find);
        }
    }

    /// <summary>rgBytes[0] e' o byte menos significativo: imprime do mais alto para o mais baixo.</summary>
    internal static string FormatAddress(ulong address)
    {
        var parts = new string[6];
        for (int i = 0; i < 6; i++)
            parts[i] = $"{(byte)(address >> (8 * (5 - i))):X2}";
        return string.Join(":", parts);
    }
}
