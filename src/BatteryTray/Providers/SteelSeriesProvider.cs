using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Fones Arctis e mouses sem fio da SteelSeries (VID 1038). Uma pergunta por
/// aparelho e por leitura, pela porta do <see cref="HidRequest"/>: escreve o
/// pedido na colecao vendor da interface certa e pega o primeiro relatorio
/// que voltar nela.
///
/// Formatos (so o formato; nada de codigo copiado):
///   fones  -> HeadsetControl (GPL-3.0), lib/devices/steelseries_*.hpp
///   mouses -> rivalcfg (WTFPL), rivalcfg/devices/*.py
/// Os indices abaixo sao os desses projetos, que usam hidapi: com report id 0
/// o hidapi tira o byte do id da resposta, entao aqui ele tambem sai antes de
/// ler (ver <see cref="Normalize"/>).
///
/// Fora por ora: Arctis 9 e Arctis Pro Wireless (colecao sem usage vendor
/// conhecida e, no Pro, duas perguntas por leitura). Sem SteelSeries nesta
/// casa: quem confirma sao os testadores.
/// </summary>
public sealed class SteelSeriesProvider : IStandaloneProvider
{
    private const ushort Vid = 0x1038;
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Null = o aparelho esta desligado ou fora do alcance da base.</summary>
    internal delegate BatteryReading? Parser(byte[] d, ushort pid);

    internal sealed record Spec(string Name, ushort[] Pids, int Interface, ushort UsagePage, byte[] Request, Parser Parse, DeviceKind Kind, bool Wired = false);

    private static readonly byte[] NovaStatus = { 0x00, 0xB0 };

    private static readonly Spec[] Specs =
    {
        // --- fones (HeadsetControl) ---
        new("SteelSeries Arctis 1/7X/7P", new ushort[] { 0x12B3, 0x12B6, 0x12D7, 0x12D5 }, 3, 0xFF43, new byte[] { 0x06, 0x12 }, Arctis1, DeviceKind.Headset),
        new("SteelSeries Arctis 7/Pro", new ushort[] { 0x1260, 0x12AD, 0x1252, 0x1280 }, 5, 0xFF43, new byte[] { 0x06, 0x18 }, Arctis7, DeviceKind.Headset),
        new("SteelSeries Arctis 7+", new ushort[] { 0x220E, 0x2212, 0x2216, 0x2236 }, 3, 0xFFC0, NovaStatus, Arctis7Plus, DeviceKind.Headset),
        new("SteelSeries Arctis Nova 7", new ushort[] { 0x2202, 0x22A1, 0x227E, 0x2206, 0x2258, 0x229E, 0x22AD, 0x223A, 0x22A9, 0x227A, 0x22A4, 0x22A5, 0x220A, 0x22A7, 0x2298 }, 3, 0xFFC0, NovaStatus, Nova7, DeviceKind.Headset),
        new("SteelSeries Arctis Nova 5", new ushort[] { 0x2232, 0x2253 }, 3, 0xFFC0, NovaStatus, Nova5, DeviceKind.Headset),
        new("SteelSeries Arctis Nova 3P/3X Wireless", new ushort[] { 0x2269, 0x226D }, 3, 0xFFC0, NovaStatus, Nova5, DeviceKind.Headset),
        new("SteelSeries Arctis GameBuds", new ushort[] { 0x230A }, 3, 0xFFC0, NovaStatus, GameBuds, DeviceKind.Headset),
        new("SteelSeries Arctis Nova Pro Wireless", new ushort[] { 0x12E0, 0x12E5 }, 4, 0, new byte[] { 0x06, 0xB0 }, NovaPro, DeviceKind.Headset),

        // --- mouses (rivalcfg) ---
        new("SteelSeries Aerox 3 Wireless", new ushort[] { 0x1838, 0x1878 }, 3, 0, new byte[] { 0x00, 0xD2 }, Aerox, DeviceKind.Mouse),
        new("SteelSeries Aerox 3 Wireless", new ushort[] { 0x183A, 0x187A }, 3, 0, new byte[] { 0x00, 0x92 }, Aerox, DeviceKind.Mouse, Wired: true),
        new("SteelSeries Aerox 5 Wireless", new ushort[] { 0x1852, 0x185C, 0x1860 }, 3, 0, new byte[] { 0x00, 0xD2 }, Aerox, DeviceKind.Mouse),
        new("SteelSeries Aerox 5 Wireless", new ushort[] { 0x1854, 0x185E, 0x1862 }, 3, 0, new byte[] { 0x00, 0x92 }, Aerox, DeviceKind.Mouse, Wired: true),
        new("SteelSeries Aerox 9 Wireless", new ushort[] { 0x1858, 0x1874 }, 3, 0, new byte[] { 0x00, 0xD2 }, Aerox, DeviceKind.Mouse),
        new("SteelSeries Aerox 9 Wireless", new ushort[] { 0x185A, 0x1876 }, 3, 0, new byte[] { 0x00, 0x92 }, Aerox, DeviceKind.Mouse, Wired: true),
        new("SteelSeries Prime Wireless", new ushort[] { 0x1840, 0x1848 }, 3, 0, new byte[] { 0x00, 0xD2 }, Aerox, DeviceKind.Mouse),
        new("SteelSeries Prime Wireless", new ushort[] { 0x1842, 0x184A }, 3, 0, new byte[] { 0x00, 0x92 }, Aerox, DeviceKind.Mouse, Wired: true),
        new("SteelSeries Rival 3 Wireless", new ushort[] { 0x1830, 0x1872 }, 3, 0, new byte[] { 0x00, 0xAA, 0x01 }, Rival, DeviceKind.Mouse),
        new("SteelSeries Rival 650 Wireless", new ushort[] { 0x1726 }, 0, 0, new byte[] { 0x00, 0xAA, 0x01 }, Rival, DeviceKind.Mouse),
        new("SteelSeries Rival 650 Wireless", new ushort[] { 0x172B }, 0, 0, new byte[] { 0x00, 0xAA, 0x01 }, Rival, DeviceKind.Mouse, Wired: true),
    };

    private static readonly Dictionary<ushort, Spec> ByPid = Specs.SelectMany(s => s.Pids.Select(p => (p, s))).ToDictionary(x => x.p, x => x.s);

    public string Name => "SteelSeries";

    public bool Handles(ushort vid, ushort pid) => vid == Vid && ByPid.ContainsKey(pid);

    public IEnumerable<DeviceEntry> Discover(IReadOnlyList<HidCollection> collections)
    {
        var found = new List<DeviceEntry>();
        var byPid = collections.Where(c => Handles(c.Vid, c.Pid)).GroupBy(c => c.Pid);
        // O mesmo mouse no cabo e no dongle vira uma linha so: le o cabo, e o dongle so se o cabo nao responder.
        foreach (var byModel in byPid.GroupBy(g => ByPid[g.Key].Name))
        {
            DeviceEntry? entry = null;
            foreach (var group in byModel.OrderByDescending(g => ByPid[g.Key].Wired))
            {
                var spec = ByPid[group.Key];
                entry = Entry(spec, group.Key, Read(spec, group.Key, group.ToList()));
                if (entry.Reading.Status == ReadingStatus.Ok) break;
            }
            found.Add(entry!);
        }
        return found;
    }

    private static BatteryReading Read(Spec spec, ushort pid, List<HidCollection> cols)
    {
        string mi = $"&mi_{spec.Interface:D2}";
        var target = cols.FirstOrDefault(c => HidRequest.IsVendor(c)
                                              && c.OutputLen >= spec.Request.Length && c.InputLen > 0
                                              && (spec.UsagePage == 0 || c.UsagePage == spec.UsagePage)
                                              && c.Path.Contains(mi, StringComparison.OrdinalIgnoreCase));
        if (target is null) return BatteryReading.NoData(L.T("não achei o canal de bateria deste aparelho", "couldn’t find this device’s battery channel"));

        using var session = HidRequest.Open(new[] { target });
        if (session is null) return BatteryReading.NoData(L.T("o canal de bateria não abriu", "the battery channel didn’t open"));

        var reply = session.Ask(target, spec.Request, r => r.Length >= 2, AskTimeout);
        if (reply is null) return BatteryReading.NoData(L.T("não respondeu; ligue o aparelho e atualize", "didn’t answer; turn the device on and refresh"));

        BatteryReading? reading;
        try { reading = spec.Parse(Normalize(spec.Request, reply), pid); }
        catch (IndexOutOfRangeException) { reading = BatteryReading.NoData(L.T("resposta curta demais", "reply too short")); }
        return reading ?? BatteryReading.NoData(L.T("desligado ou longe da base; ligue e atualize", "off or far from the base; turn it on and refresh"));
    }

    /// <summary>Pedido com report id 0: tira o byte do id da resposta, como o hidapi faz.</summary>
    internal static byte[] Normalize(byte[] request, byte[] reply) =>
        request[0] == 0x00 && reply.Length > 0 && reply[0] == 0x00 ? reply[1..] : reply;

    private DeviceEntry Entry(Spec spec, ushort pid, BatteryReading reading) => new()
    {
        Key = $"steelseries:{pid:X4}",
        DisplayName = spec.Name,
        HardwareId = BatteryTray.Core.Known.Id(Vid, pid),
        ProviderName = Name,
        Reading = reading,
        IsConnected = true,
        Kind = spec.Kind,
        Origin = spec.Wired ? "Cabo" : "Dongle",
    };

    private static BatteryReading Pct(int pct, bool charging) =>
        BatteryReading.Percentage(Math.Clamp(pct, 0, 100), chargeText: charging ? L.T("carregando", "charging") : null);

    /// <summary>Niveis 0..4 viram 0/25/50/75/100.</summary>
    private static int Steps(int level, int max) => Math.Clamp(level, 0, max) * 100 / max;

    internal static BatteryReading? Arctis1(byte[] d, ushort _) => d[2] == 0x01 ? null : Pct(d[3], false);

    internal static BatteryReading? Arctis7(byte[] d, ushort _) => Pct(d[2], false);

    internal static BatteryReading? Arctis7Plus(byte[] d, ushort _) => d[1] == 0x01 ? null : Pct(Steps(d[2], 4), d[3] == 0x01);

    private static readonly HashSet<ushort> Nova7Steps = new() { 0x2202, 0x2206, 0x220A, 0x223A, 0x227A, 0x22A4 };

    internal static BatteryReading? Nova7(byte[] d, ushort pid) =>
        d[3] == 0x00 ? null : Pct(Nova7Steps.Contains(pid) ? Steps(d[2], 4) : d[2], d[3] is 0x01 or 0x02);

    internal static BatteryReading? Nova5(byte[] d, ushort _) => d[1] == 0x02 ? null : Pct(d[3], d[4] == 0x01);

    /// <summary>Fone ativo (fora do estojo) = 3; mostra o mais fraco dos dois.</summary>
    internal static BatteryReading? GameBuds(byte[] d, ushort _)
    {
        var active = new List<int>();
        if (d[3] == 0x03) active.Add(d[5]);
        if (d[4] == 0x03) active.Add(d[6]);
        return active.Count == 0 ? null : Pct(active.Min(), false);
    }

    internal static BatteryReading? NovaPro(byte[] d, ushort _) => d[15] == 0x01 ? null : Pct(Steps(d[6], 8), d[15] == 0x02);

    /// <summary>Aerox/Prime: [1] bit 7 = carregando, resto = nivel em passos de 5% a partir de 1.</summary>
    internal static BatteryReading? Aerox(byte[] d, ushort _) => Pct(((d[1] & 0x7F) - 1) * 5, (d[1] & 0x80) != 0);

    internal static BatteryReading? Rival(byte[] d, ushort _) => Pct(d[0], d[2] != 0);
}
