using System.Buffers.Binary;
using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Mercury I/M1, receptor 0005 e fone 0007. Protocolo AA mapeado no firmware
/// e conferido no aparelho em tres niveis de carga. AA 03 le quatro bytes da
/// RAM pelo dongle, apos validar a versao e o ponteiro da estrutura de energia.
/// Enderecos especificos desta revisao. Toda consulta passa pelo HidRequest.
/// </summary>
public sealed class MercuryProvider : IStandaloneProvider
{
    private const ushort Vid = 0x373B, Pid = 0x107C;
    private const uint PowerPointerAddress = 0x0100FE94, ExpectedPowerPointer = 0x0100FE5C;
    private const uint CapacityAddress = 0x0100FE78;

    public string Name => "ATK Mercury";
    public bool Handles(ushort vid, ushort pid) => vid == Vid && pid == Pid;

    public IEnumerable<DeviceEntry> Discover(IReadOnlyList<HidCollection> collections)
    {
        var claimed = collections.Where(c => Handles(c.Vid, c.Pid)).ToArray();
        if (claimed.Length == 0) yield break;

        yield return new DeviceEntry
        {
            Key = "atk:mercury",
            DisplayName = "ATK Mercury I / M1",
            HardwareId = Known.Id(Vid, Pid),
            ProviderName = Name,
            Kind = DeviceKind.Headset,
            Origin = "Dongle",
            IsConnected = true,
            Reading = Read(claimed),
        };
    }

    internal static bool IsTarget(HidCollection c) =>
        c.Vid == Vid && c.Pid == Pid && c.Version == 0x0005
        && c.UsagePage == 0xFF22 && c.Usage == 1 && c.FeatureLen == 64
        && c.Path.Contains("&mi_00&col02", StringComparison.OrdinalIgnoreCase);

    private static BatteryReading Read(IReadOnlyList<HidCollection> claimed)
    {
        var targets = claimed.Where(IsTarget).ToArray();
        if (targets.Length != 1)
            return BatteryReading.NoData(L.T("requer um receptor Mercury 0005 com canal FF22/1",
                "requires one Mercury receiver 0005 with channel FF22/1"));

        return ReadBattery(request => HidRequest.Feature(targets[0], request,
            TimeSpan.FromMilliseconds(300), _ => false, reads: 1));
    }

    // O transporte injetado permite testar a sequencia e suas interrupcoes sem hardware.
    internal static BatteryReading ReadBattery(Func<byte[], byte[]?> query)
    {
        var versionRequest = new byte[64];
        versionRequest[0] = 0xAA;
        versionRequest[1] = 0x01;
        var version = query(versionRequest);
        if (version is null)
            return BatteryReading.NoData(L.T("fone sem resposta ou consulta desabilitada",
                "headset did not respond or queries are disabled"));
        if (version.Length != 64 || version[0] != 0xAA || version[1] != 1
            || version[2] != 0 || version[3] != 0 || version[4] != 0 || version[5] != 7)
            return BatteryReading.NoData(L.T("versão do fone diferente de 0007", "headset version differs from 0007"));

        if (ParseMemory(query(MemoryRequest(PowerPointerAddress))) != ExpectedPowerPointer)
            return BatteryReading.NoData(L.T("estrutura de bateria não confirmada", "battery layout could not be verified"));

        uint? percent = ParseMemory(query(MemoryRequest(CapacityAddress)));
        return percent is <= 100
            ? BatteryReading.Percentage((int)percent.Value)
            : BatteryReading.NoData(L.T("capacidade ausente ou fora da faixa", "capacity missing or out of range"));
    }

    private static byte[] MemoryRequest(uint address)
    {
        var request = new byte[64];
        request[0] = 0xAA;
        request[1] = 0x03;
        request[3] = 1;
        request[4] = 4;
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(5, 4), address);
        return request;
    }

    internal static uint? ParseMemory(byte[]? reply) =>
        reply is { Length: 64 } && reply[0] == 0xAA && reply[1] == 0x03 && reply[2] == 4
            ? BinaryPrimitives.ReadUInt32LittleEndian(reply.AsSpan(3, 4)) : null;
}
