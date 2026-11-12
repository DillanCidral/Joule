using System.Runtime.InteropServices;
using System.Text;
using BatteryTray.Core;

namespace BatteryTray.Interop;

/// <summary>Um dispositivo Bluetooth cuja bateria o Windows ja conhece.</summary>
public sealed class BluetoothBatteryDevice
{
    /// <summary>Endereco do radio, hex minusculo sem separadores. Chave estavel.</summary>
    public required string Address { get; init; }
    public required string Name { get; init; }
    public required int Percent { get; init; }

    /// <summary>Quando o Windows atualizou o percentual. Null se o devnode nao disse.</summary>
    public DateTime? UpdatedUtc { get; init; }

    public bool IsConnected { get; init; }
    public bool IsLowEnergy { get; init; }

    /// <summary>Class of Device do BT classico (major/minor). Null no BLE, que nao tem.</summary>
    public uint? ClassOfDevice { get; init; }
}

/// <summary>
/// Leitura SOMENTE DE METADADOS dos devnodes Bluetooth: nada e' aberto, nada
/// e' enviado por radio. O percentual sai de DEVPKEY_Bluetooth_Battery, a
/// mesma propriedade que alimenta a tela de Configuracoes do Windows -- quem
/// falou com o dispositivo foi a pilha do sistema, nao este app.
///
/// Duas assimetrias entre os transportes, medidas em 8 capturas do coletor:
///
///  - BLE (enumerador BTHLE): bateria e estado de conexao no MESMO devnode,
///    o raiz BTHLE\DEV_&lt;mac&gt;.
///  - Bluetooth classico (BTHENUM): a bateria fica no devnode FILHO do
///    servico (o Hands-Free AG, sufixo _C00000000), e nesse filho o
///    DEVPKEY_Device_IsConnected e' sempre false, mesmo com o fone tocando.
///    Quem sabe da conexao e' o devnode raiz BTHENUM\DEV_&lt;mac&gt;.
///
/// Por isso a coleta e' feita numa passada so e depois casada por endereco: e'
/// o unico jeito de nao dizer "desconectado" para um fone que esta ligado.
///
/// O valor CONGELA quando o dispositivo desliga -- o Windows mantem o ultimo
/// percentual conhecido indefinidamente (medido: 17 min sem mudar, com o
/// aparelho desligado). Por isso <see cref="BluetoothBatteryDevice.UpdatedUtc"/>
/// acompanha o numero em todo lugar: sem a idade, o app afirmaria como atual
/// um valor morto.
/// </summary>
public static class BluetoothBatteryNodes
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public Guid Fmtid;
        public uint Pid;

        public DevPropKey(string guid, uint id)
        {
            Fmtid = new Guid(guid);
            Pid = id;
        }
    }

    private const int DIGCF_PRESENT = 0x02;
    private const int DIGCF_ALLCLASSES = 0x04;

    private const uint DEVPROP_TYPE_SBYTE = 0x02;
    private const uint DEVPROP_TYPE_BYTE = 0x03;
    private const uint DEVPROP_TYPE_UINT16 = 0x05;
    private const uint DEVPROP_TYPE_INT32 = 0x06;
    private const uint DEVPROP_TYPE_UINT32 = 0x07;
    private const uint DEVPROP_TYPE_FILETIME = 0x10;
    private const uint DEVPROP_TYPE_BOOLEAN = 0x11;
    private const uint DEVPROP_TYPE_STRING = 0x12;

    /// <summary>DEVPKEY_Bluetooth_Battery: o percentual que o Windows ja conhece.</summary>
    private static readonly DevPropKey KeyBattery = new("104ea319-6ee2-4701-bd47-8ddbf425bbe5", 2);

    /// <summary>Carimbo de tempo do percentual acima.</summary>
    private static readonly DevPropKey KeyBatteryUpdated = new("104ea319-6ee2-4701-bd47-8ddbf425bbe5", 7);

    private static readonly DevPropKey KeyAddress = new("2bd67d8b-8beb-48d5-87e0-6cda3428040a", 1);
    private static readonly DevPropKey KeyClassOfDevice = new("2bd67d8b-8beb-48d5-87e0-6cda3428040a", 10);
    private static readonly DevPropKey KeyIsConnected = new("83da6326-97a6-4088-9453-a1923f573b29", 15);
    private static readonly DevPropKey KeyFriendlyName = new("a45c254e-df1c-4efd-8020-67d146a850e0", 14);
    private static readonly DevPropKey KeyDeviceDesc = new("a45c254e-df1c-4efd-8020-67d146a850e0", 2);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetClassDevsW")]
    private static extern IntPtr SetupDiGetClassDevsAll(IntPtr classGuid, IntPtr enumerator, IntPtr parent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr devInfo, int index, ref HidNative.SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetupDiGetDeviceInstanceIdW")]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr devInfo, ref HidNative.SP_DEVINFO_DATA data, StringBuilder id, int idSize, out int required);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDevicePropertyW(
        IntPtr devInfo, ref HidNative.SP_DEVINFO_DATA data, ref DevPropKey key,
        out uint propertyType, byte[]? buffer, int bufferSize, out int requiredSize, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr devInfo);

    /// <summary>Um devnode Bluetooth cru, antes de casar filho com raiz.</summary>
    internal sealed record RawNode(
        string Enumerator,
        string Address,
        string? Name,
        int? Percent,
        DateTime? UpdatedUtc,
        bool IsConnected,
        bool IsRoot,
        uint? ClassOfDevice = null);

    /// <summary>Le os devnodes Bluetooth presentes e devolve so quem tem bateria.</summary>
    public static List<BluetoothBatteryDevice> Collect() => Merge(Scan());

    internal static List<RawNode> Scan()
    {
        var raw = new List<RawNode>();

        IntPtr set = SetupDiGetClassDevsAll(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (set == new IntPtr(-1))
        {
            Log.Write($"Bluetooth: SetupDiGetClassDevs falhou (err {Marshal.GetLastWin32Error()}).");
            return raw;
        }

        try
        {
            for (int index = 0; ; index++)
            {
                var info = new HidNative.SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<HidNative.SP_DEVINFO_DATA>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref info)) break;

                try
                {
                    var node = Describe(set, ref info);
                    if (node is not null) raw.Add(node);
                }
                catch (Exception ex)
                {
                    Log.Error($"Bluetooth.devnode#{index}", ex);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return raw;
    }

    private static RawNode? Describe(IntPtr set, ref HidNative.SP_DEVINFO_DATA info)
    {
        string instanceId = InstanceId(set, ref info);
        string enumerator = instanceId.Split('\\').FirstOrDefault() ?? "";

        // So a arvore Bluetooth interessa: BTHENUM, BTHLE, BTHLEDEVICE, BTH...
        if (!enumerator.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)) return null;

        string? address = ReadString(set, ref info, KeyAddress);
        if (string.IsNullOrWhiteSpace(address)) return null;

        // Raiz e' o devnode do aparelho (BTHLE\DEV_<mac> ou BTHENUM\DEV_<mac>);
        // os outros sao servicos dele. So a raiz sabe se esta conectado.
        bool isRoot = instanceId.Contains("\\DEV_", StringComparison.OrdinalIgnoreCase);

        return new RawNode(
            Enumerator: enumerator,
            Address: NormalizeAddress(address),
            Name: ReadString(set, ref info, KeyFriendlyName) ?? ReadString(set, ref info, KeyDeviceDesc),
            Percent: ReadNumber(set, ref info, KeyBattery),
            UpdatedUtc: ReadFileTime(set, ref info, KeyBatteryUpdated),
            IsConnected: ReadBool(set, ref info, KeyIsConnected) ?? false,
            IsRoot: isRoot,
            ClassOfDevice: ReadNumber(set, ref info, KeyClassOfDevice) is int cod ? (uint)cod : null);
    }

    /// <summary>
    /// Casa os devnodes de um mesmo aparelho: o percentual vem de quem o tem
    /// (no classico, o filho do servico), a conexao vem da raiz, e o nome vem
    /// da raiz porque o do filho carrega o sufixo do perfil.
    /// </summary>
    internal static List<BluetoothBatteryDevice> Merge(IReadOnlyList<RawNode> nodes)
    {
        var result = new List<BluetoothBatteryDevice>();

        foreach (var group in nodes.GroupBy(n => n.Address, StringComparer.OrdinalIgnoreCase))
        {
            // Entre varios com bateria, vale o carimbo mais novo.
            var withBattery = group
                .Where(n => n.Percent is >= 0 and <= 100)
                .OrderByDescending(n => n.UpdatedUtc ?? DateTime.MinValue)
                .ToList();

            if (withBattery.Count == 0) continue;

            var best = withBattery[0];
            var root = group.FirstOrDefault(n => n.IsRoot);

            string name = FirstUseful(CleanName(root?.Name), CleanName(best.Name), group.Select(n => CleanName(n.Name)))
                          ?? $"Bluetooth {group.Key}";

            result.Add(new BluetoothBatteryDevice
            {
                Address = group.Key.ToLowerInvariant(),
                Name = name,
                Percent = best.Percent!.Value,
                UpdatedUtc = best.UpdatedUtc,
                IsConnected = group.Any(n => n.IsConnected),
                IsLowEnergy = group.Any(n => n.Enumerator.StartsWith("BTHLE", StringComparison.OrdinalIgnoreCase)),
                ClassOfDevice = group.Select(n => n.ClassOfDevice).FirstOrDefault(c => c is not null and not 0),
            });
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return result;
    }

    /// <summary>
    /// O devnode do perfil se chama "Fone X Hands-Free AG". O nome do aparelho
    /// e' o que o usuario reconhece; o sufixo do perfil so polui a lista.
    /// </summary>
    internal static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        string s = name.Trim();
        foreach (string suffix in new[] { " Hands-Free AG", " Hands-Free", " Stereo", " AG" })
        {
            if (s.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                s = s[..^suffix.Length].TrimEnd();
        }

        return s.Length > 0 ? s : null;
    }

    /// <summary>Hex minusculo sem separadores: o BLE devolve minusculo, o classico maiusculo.</summary>
    internal static string NormalizeAddress(string address) =>
        new(address.Where(Uri.IsHexDigit).Select(char.ToLowerInvariant).ToArray());

    private static string? FirstUseful(string? first, string? second, IEnumerable<string?> rest) =>
        new[] { first, second }.Concat(rest).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

    private static string InstanceId(IntPtr set, ref HidNative.SP_DEVINFO_DATA info)
    {
        var sb = new StringBuilder(1024);
        return SetupDiGetDeviceInstanceId(set, ref info, sb, sb.Capacity, out _) ? sb.ToString() : "";
    }

    private static (uint Type, byte[] Data)? ReadRaw(IntPtr set, ref HidNative.SP_DEVINFO_DATA info, DevPropKey key)
    {
        SetupDiGetDevicePropertyW(set, ref info, ref key, out _, null, 0, out int required, 0);
        if (required <= 0) return null;

        var buffer = new byte[required];
        if (!SetupDiGetDevicePropertyW(set, ref info, ref key, out uint type, buffer, buffer.Length, out _, 0)) return null;
        return (type, buffer);
    }

    private static string? ReadString(IntPtr set, ref HidNative.SP_DEVINFO_DATA info, DevPropKey key)
    {
        var raw = ReadRaw(set, ref info, key);
        if (raw is null || raw.Value.Type != DEVPROP_TYPE_STRING) return null;

        string s = Encoding.Unicode.GetString(raw.Value.Data).TrimEnd('\0').Trim();
        return s.Length > 0 ? s : null;
    }

    private static int? ReadNumber(IntPtr set, ref HidNative.SP_DEVINFO_DATA info, DevPropKey key)
    {
        var raw = ReadRaw(set, ref info, key);
        if (raw is null || raw.Value.Data.Length < 1) return null;

        return raw.Value.Type switch
        {
            DEVPROP_TYPE_BYTE or DEVPROP_TYPE_SBYTE => raw.Value.Data[0],
            DEVPROP_TYPE_UINT16 when raw.Value.Data.Length >= 2 => BitConverter.ToUInt16(raw.Value.Data),
            DEVPROP_TYPE_UINT32 or DEVPROP_TYPE_INT32 when raw.Value.Data.Length >= 4 => BitConverter.ToInt32(raw.Value.Data),
            _ => null,
        };
    }

    private static bool? ReadBool(IntPtr set, ref HidNative.SP_DEVINFO_DATA info, DevPropKey key)
    {
        var raw = ReadRaw(set, ref info, key);
        if (raw is null || raw.Value.Type != DEVPROP_TYPE_BOOLEAN || raw.Value.Data.Length < 1) return null;
        return raw.Value.Data[0] != 0;
    }

    private static DateTime? ReadFileTime(IntPtr set, ref HidNative.SP_DEVINFO_DATA info, DevPropKey key)
    {
        var raw = ReadRaw(set, ref info, key);
        if (raw is null || raw.Value.Type != DEVPROP_TYPE_FILETIME || raw.Value.Data.Length < 8) return null;

        long ticks = BitConverter.ToInt64(raw.Value.Data);
        if (ticks <= 0) return null;

        try { return DateTime.FromFileTimeUtc(ticks); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
