using System.Runtime.InteropServices;
using System.Text;
using static BatteryTray.Scan.ScanNative;

namespace BatteryTray.Scan;

/// <summary>
/// Varredura dos devnodes presentes (setupapi), somente leitura.
///
/// E' aqui que mora a esperanca do Bluetooth: quando o Windows sabe a bateria
/// de um dispositivo BLE (o mesmo numero que aparece em Configuracoes >
/// Bluetooth), ele guarda em DEVPKEY_Bluetooth_Battery,
/// {104EA319-6EE2-4701-BD47-8DDBF425BBE5},2. Se esse valor existir numa
/// maquina, da' para escrever um provider generico de Bluetooth sem decifrar
/// protocolo nenhum.
///
/// Para nao depender de eu ter decorado o numero certo de cada propriedade,
/// o coletor ENUMERA todas as chaves de cada devnode interessante e despeja
/// os valores. Chave desconhecida aparece como {GUID},pid -- crua, mas la'.
/// </summary>
public static class DeviceNodeScan
{
    private static readonly DEVPROPKEY DevPkeyDeviceDesc = new("a45c254e-df1c-4efd-8020-67d146a850e0", 2);
    private static readonly DEVPROPKEY DevPkeyHardwareIds = new("a45c254e-df1c-4efd-8020-67d146a850e0", 3);
    private static readonly DEVPROPKEY DevPkeyClass = new("a45c254e-df1c-4efd-8020-67d146a850e0", 9);
    private static readonly DEVPROPKEY DevPkeyManufacturer = new("a45c254e-df1c-4efd-8020-67d146a850e0", 13);
    private static readonly DEVPROPKEY DevPkeyFriendlyName = new("a45c254e-df1c-4efd-8020-67d146a850e0", 14);
    private static readonly DEVPROPKEY DevPkeyName = new("b725f130-47ef-101a-a5f1-02608c9eebac", 10);

    /// <summary>DEVPKEY_Bluetooth_Battery: o percentual que o Windows ja conhece.</summary>
    private static readonly DEVPROPKEY DevPkeyBluetoothBattery = new("104ea319-6ee2-4701-bd47-8ddbf425bbe5", 2);

    private static readonly Dictionary<string, string> KnownKeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["{A45C254E-DF1C-4EFD-8020-67D146A850E0},2"] = "DeviceDesc",
        ["{A45C254E-DF1C-4EFD-8020-67D146A850E0},3"] = "HardwareIds",
        ["{A45C254E-DF1C-4EFD-8020-67D146A850E0},4"] = "CompatibleIds",
        ["{A45C254E-DF1C-4EFD-8020-67D146A850E0},6"] = "Service",
        ["{A45C254E-DF1C-4EFD-8020-67D146A850E0},9"] = "Class",
        ["{A45C254E-DF1C-4EFD-8020-67D146A850E0},10"] = "ClassGuid",
        ["{A45C254E-DF1C-4EFD-8020-67D146A850E0},11"] = "Driver",
        ["{A45C254E-DF1C-4EFD-8020-67D146A850E0},13"] = "Manufacturer",
        ["{A45C254E-DF1C-4EFD-8020-67D146A850E0},14"] = "FriendlyName",
        ["{A45C254E-DF1C-4EFD-8020-67D146A850E0},15"] = "LocationInfo",
        ["{A45C254E-DF1C-4EFD-8020-67D146A850E0},16"] = "PDOName",
        ["{B725F130-47EF-101A-A5F1-02608C9EEBAC},10"] = "Name",
        ["{8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C},2"] = "ContainerId",
        ["{104EA319-6EE2-4701-BD47-8DDBF425BBE5},2"] = "Bluetooth_Battery",
        ["{2BD67D8B-8BEB-48D5-87E0-6CDA3428040A},1"] = "Bluetooth_DeviceAddress",
        ["{2BD67D8B-8BEB-48D5-87E0-6CDA3428040A},2"] = "Bluetooth_ServiceGuid",
        ["{2BD67D8B-8BEB-48D5-87E0-6CDA3428040A},8"] = "Bluetooth_LastConnectedTime",
        ["{2BD67D8B-8BEB-48D5-87E0-6CDA3428040A},10"] = "Bluetooth_ClassOfDevice",
    };

    /// <summary>Classes/enumeradores que podem carregar bateria: dump completo.</summary>
    private static readonly HashSet<string> DeepClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "HIDClass", "Bluetooth", "Mouse", "Keyboard", "MEDIA", "AudioEndpoint",
        "XnaComposite", "XboxComposite", "Battery",
    };

    private static readonly HashSet<string> DeepEnumerators = new(StringComparer.OrdinalIgnoreCase)
    {
        "BTHENUM", "BTHLE", "BTHLEDEVICE", "BTHHFENUM", "BTH", "HID",
    };

    private static readonly HashSet<string> ShallowEnumerators = new(StringComparer.OrdinalIgnoreCase)
    {
        "USB", "USBHUB3", "SWD", "ACPI",
    };

    public static List<DeviceNodeInfo> Collect(List<string> warnings, out int skipped)
    {
        var result = new List<DeviceNodeInfo>();
        skipped = 0;

        IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (set == new IntPtr(-1))
        {
            warnings.Add($"SetupDiGetClassDevs falhou (err {Marshal.GetLastWin32Error()})");
            return result;
        }

        try
        {
            for (int index = 0; ; index++)
            {
                var info = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref info)) break;

                DeviceNodeInfo? node;
                try
                {
                    node = Describe(set, ref info);
                }
                catch (Exception ex)
                {
                    warnings.Add($"devnode #{index}: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (node is null) { skipped++; continue; }
                result.Add(node);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return result;
    }

    private static DeviceNodeInfo? Describe(IntPtr set, ref SP_DEVINFO_DATA info)
    {
        string instanceId = GetInstanceId(set, ref info);
        string enumerator = instanceId.Split('\\').FirstOrDefault() ?? "";
        string cls = ReadString(set, ref info, DevPkeyClass) ?? "";

        bool deep = DeepClasses.Contains(cls) || DeepEnumerators.Contains(enumerator);
        bool shallow = ShallowEnumerators.Contains(enumerator);

        // Bateria Bluetooth e' sempre motivo para incluir o devnode, seja qual
        // for a classe: e' exatamente o que estamos procurando.
        int? btBattery = ReadByte(set, ref info, DevPkeyBluetoothBattery);

        if (!deep && !shallow && btBattery is null) return null;

        var node = new DeviceNodeInfo
        {
            InstanceId = instanceId,
            Enumerator = enumerator,
            Class = cls,
            Name = ReadString(set, ref info, DevPkeyName)
                   ?? ReadString(set, ref info, DevPkeyFriendlyName)
                   ?? ReadString(set, ref info, DevPkeyDeviceDesc)
                   ?? "",
            Manufacturer = ReadString(set, ref info, DevPkeyManufacturer) ?? "",
            BluetoothBattery = btBattery,
        };

        if (ReadRaw(set, ref info, DevPkeyHardwareIds) is { } hw && hw.Type == DEVPROP_TYPE_STRING_LIST)
            node.HardwareIds = SplitStringList(hw.Data);

        if (deep || btBattery is not null)
        {
            foreach (var (key, value) in ReadAllProperties(set, ref info))
            {
                node.Properties[key] = value;
                if (key.Contains("battery", StringComparison.OrdinalIgnoreCase))
                    node.BatteryProperties[key] = value;
            }
        }

        return node;
    }

    private static List<(string Key, string Value)> ReadAllProperties(IntPtr set, ref SP_DEVINFO_DATA info)
    {
        var result = new List<(string, string)>();

        SetupDiGetDevicePropertyKeys(set, ref info, null, 0, out int required, 0);
        if (required <= 0) return result;

        var keys = new DEVPROPKEY[required];
        if (!SetupDiGetDevicePropertyKeys(set, ref info, keys, keys.Length, out int written, 0)) return result;

        for (int i = 0; i < written && i < keys.Length; i++)
        {
            var key = keys[i];
            string raw = key.ToString();
            string name = KnownKeyNames.TryGetValue(raw, out var known) ? $"{known} [{raw}]" : raw;

            var value = ReadRaw(set, ref info, key);
            if (value is null) continue;

            result.Add((name, RenderValue(value.Value.Type, value.Value.Data)));
        }

        return result;
    }

    private static string GetInstanceId(IntPtr set, ref SP_DEVINFO_DATA info)
    {
        var sb = new StringBuilder(1024);
        return SetupDiGetDeviceInstanceId(set, ref info, sb, sb.Capacity, out _) ? sb.ToString() : "";
    }

    private static (uint Type, byte[] Data)? ReadRaw(IntPtr set, ref SP_DEVINFO_DATA info, DEVPROPKEY key)
    {
        SetupDiGetDevicePropertyW(set, ref info, ref key, out uint type, null, 0, out int required, 0);
        if (required <= 0) return null;

        var buffer = new byte[required];
        if (!SetupDiGetDevicePropertyW(set, ref info, ref key, out type, buffer, buffer.Length, out _, 0)) return null;
        return (type, buffer);
    }

    private static string? ReadString(IntPtr set, ref SP_DEVINFO_DATA info, DEVPROPKEY key)
    {
        var raw = ReadRaw(set, ref info, key);
        if (raw is null || raw.Value.Type != DEVPROP_TYPE_STRING) return null;
        string s = Encoding.Unicode.GetString(raw.Value.Data).TrimEnd('\0').Trim();
        return s.Length > 0 ? s : null;
    }

    private static int? ReadByte(IntPtr set, ref SP_DEVINFO_DATA info, DEVPROPKEY key)
    {
        var raw = ReadRaw(set, ref info, key);
        if (raw is null || raw.Value.Data.Length < 1) return null;
        return raw.Value.Type switch
        {
            DEVPROP_TYPE_BYTE or DEVPROP_TYPE_SBYTE => raw.Value.Data[0],
            DEVPROP_TYPE_UINT16 or DEVPROP_TYPE_INT16 when raw.Value.Data.Length >= 2 => BitConverter.ToUInt16(raw.Value.Data),
            DEVPROP_TYPE_UINT32 or DEVPROP_TYPE_INT32 when raw.Value.Data.Length >= 4 => BitConverter.ToInt32(raw.Value.Data),
            _ => null,
        };
    }

    private static List<string> SplitStringList(byte[] data) =>
        Encoding.Unicode.GetString(data)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

    /// <summary>Renderiza qualquer DEVPROP como texto. Tipo desconhecido vira hex.</summary>
    internal static string RenderValue(uint type, byte[] data)
    {
        try
        {
            switch (type)
            {
                case DEVPROP_TYPE_STRING:
                    return Encoding.Unicode.GetString(data).TrimEnd('\0').Trim();

                case DEVPROP_TYPE_STRING_LIST:
                    return string.Join(" | ", SplitStringList(data));

                case DEVPROP_TYPE_BYTE:
                    return data.Length >= 1 ? data[0].ToString() : "";

                case DEVPROP_TYPE_SBYTE:
                    return data.Length >= 1 ? ((sbyte)data[0]).ToString() : "";

                case DEVPROP_TYPE_INT16:
                    return data.Length >= 2 ? BitConverter.ToInt16(data).ToString() : "";

                case DEVPROP_TYPE_UINT16:
                    return data.Length >= 2 ? BitConverter.ToUInt16(data).ToString() : "";

                case DEVPROP_TYPE_INT32:
                    return data.Length >= 4 ? BitConverter.ToInt32(data).ToString() : "";

                case DEVPROP_TYPE_UINT32:
                    return data.Length >= 4 ? BitConverter.ToUInt32(data).ToString() : "";

                case DEVPROP_TYPE_INT64:
                    return data.Length >= 8 ? BitConverter.ToInt64(data).ToString() : "";

                case DEVPROP_TYPE_UINT64:
                    return data.Length >= 8 ? BitConverter.ToUInt64(data).ToString() : "";

                case DEVPROP_TYPE_BOOLEAN:
                    return data.Length >= 1 && data[0] != 0 ? "true" : "false";

                case DEVPROP_TYPE_GUID:
                    return data.Length >= 16 ? new Guid(data.AsSpan(0, 16)).ToString() : "";

                case DEVPROP_TYPE_FILETIME:
                    if (data.Length < 8) return "";
                    long ft = BitConverter.ToInt64(data);
                    return ft <= 0 ? "" : DateTime.FromFileTimeUtc(ft).ToString("yyyy-MM-dd HH:mm:ss") + " UTC";

                case DEVPROP_TYPE_EMPTY:
                case DEVPROP_TYPE_NULL:
                    return "";

                default:
                    return Hex(data);
            }
        }
        catch
        {
            return Hex(data);
        }
    }

    private static string Hex(byte[] data)
    {
        const int max = 64;
        string hex = BitConverter.ToString(data, 0, Math.Min(data.Length, max)).Replace('-', ' ');
        return data.Length > max ? $"{hex} ... ({data.Length} bytes)" : hex;
    }
}
