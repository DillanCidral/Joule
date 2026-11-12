using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Fones sem fio e o mouse Pulsefire Dart da HyperX (VIDs 0951 Kingston e
/// 03F0 HP). De uma a tres perguntas por aparelho e por leitura, pela porta do
/// <see cref="HidRequest"/>, uma de cada vez.
///
/// Formatos (so o formato; nada de codigo copiado):
///   fones -> HeadsetControl (GPL-3.0), lib/devices/hyperx_*.hpp
///   Dart  -> hyperx_pulsefire_dart_reverse_engineering (CC0), protocol/index.md
/// Os bytes sao os do hidapi: o primeiro e' o report id. O Dart nao usa report
/// id, entao vai um 00 na frente e sai da resposta.
///
/// Fora por ora: Cloud Alpha S e Cloud 3 (com fio, sem bateria) e os outros
/// mouses Pulsefire, que ninguem documentou ainda. Sem HyperX sem fio nesta
/// casa: quem confirma sao os testadores.
/// </summary>
public sealed class HyperXProvider : IStandaloneProvider
{
    private const ushort VidKingston = 0x0951, VidHp = 0x03F0;
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Le o aparelho; null = desligado ou longe da base.</summary>
    private delegate BatteryReading? Reader(Func<byte[], Func<byte[], bool>, byte[]?> ask);

    private sealed record Spec(string Name, ushort Vid, ushort[] Pids, int Interface, ushort UsagePage, ushort Usage, DeviceKind Kind, bool Wired, Reader Read);

    private static readonly Spec[] Specs =
    {
        new("HyperX Cloud II Wireless", VidHp, new ushort[] { 0x0696, 0x018B }, 0, 0xFF90, 0x0303, DeviceKind.Headset, false, CloudII),
        new("HyperX Cloud II Wireless", VidKingston, new ushort[] { 0x1718 }, 0, 0, 0, DeviceKind.Headset, false, CloudIIKingston),
        new("HyperX Cloud Alpha Wireless", VidHp, new ushort[] { 0x098D }, 0, 0, 0, DeviceKind.Headset, false, AlphaWireless),
        new("HyperX Cloud Alpha 2 Wireless", VidHp, new ushort[] { 0x08BE }, 2, 0xFF13, 0xFF00, DeviceKind.Headset, false, Alpha2Wireless),
        new("HyperX Cloud Flight Wireless", VidKingston, new ushort[] { 0x16C4, 0x1723 }, 0, 0, 0, DeviceKind.Headset, false, CloudFlight),
        new("HyperX Pulsefire Dart", VidKingston, new ushort[] { 0x16E1 }, 2, 0xFF00, 0, DeviceKind.Mouse, false, Dart),
        new("HyperX Pulsefire Dart", VidKingston, new ushort[] { 0x16E2 }, 1, 0xFF13, 0, DeviceKind.Mouse, true, Dart),
    };

    private static readonly Dictionary<string, Spec> ById =
        Specs.SelectMany(s => s.Pids.Select(p => (Id: BatteryTray.Core.Known.Id(s.Vid, p), s))).ToDictionary(x => x.Id, x => x.s);

    public string Name => "HyperX";

    public bool Handles(ushort vid, ushort pid) => ById.ContainsKey(BatteryTray.Core.Known.Id(vid, pid));

    public IEnumerable<DeviceEntry> Discover(IReadOnlyList<HidCollection> collections)
    {
        var found = new List<DeviceEntry>();
        var byId = collections.Where(c => Handles(c.Vid, c.Pid)).GroupBy(c => c.HardwareId);
        // Mesmo aparelho no cabo e no dongle: uma linha, a do cabo primeiro.
        foreach (var byModel in byId.GroupBy(g => ById[g.Key].Name))
        {
            DeviceEntry? entry = null;
            foreach (var group in byModel.OrderByDescending(g => ById[g.Key].Wired))
            {
                var spec = ById[group.Key];
                entry = Entry(spec, group.Key, Read(spec, group.ToList()));
                if (entry.Reading.Status == ReadingStatus.Ok) break;
            }
            found.Add(entry!);
        }
        return found;
    }

    private static BatteryReading Read(Spec spec, List<HidCollection> cols)
    {
        string mi = $"&mi_{spec.Interface:D2}";
        var target = cols.FirstOrDefault(c => HidRequest.CanAsk(c) && c.OutputLen > 0 && c.InputLen > 0
            && (spec.UsagePage == 0 || c.UsagePage == spec.UsagePage)
            && (spec.Usage == 0 || c.Usage == spec.Usage)
            && (c.Path.Contains(mi, StringComparison.OrdinalIgnoreCase)
                || (spec.Interface == 0 && !c.Path.Contains("&mi_", StringComparison.OrdinalIgnoreCase))));
        if (target is null) return BatteryReading.NoData(L.T("não achei o canal de bateria deste aparelho", "couldn’t find this device’s battery channel"));

        using var session = HidRequest.Open(new[] { target });
        if (session is null) return BatteryReading.NoData(L.T("o canal de bateria não abriu", "the battery channel didn’t open"));

        bool silent = false;
        byte[]? Ask(byte[] request, Func<byte[], bool> isReply)
        {
            var reply = session.Ask(target, request, isReply, AskTimeout);
            if (reply is null) silent = true;
            return reply;
        }

        BatteryReading? reading;
        try { reading = spec.Read(Ask); }
        catch (IndexOutOfRangeException) { return BatteryReading.NoData(L.T("resposta curta demais", "reply too short")); }
        if (reading is not null) return reading;
        return silent
            ? BatteryReading.NoData(L.T("não respondeu; ligue o aparelho e atualize", "didn’t answer; turn the device on and refresh"))
            : BatteryReading.NoData(L.T("desligado ou longe da base; ligue e atualize", "off or far from the base; turn it on and refresh"));
    }

    private DeviceEntry Entry(Spec spec, string hardwareId, BatteryReading reading) => new()
    {
        Key = $"hyperx:{hardwareId}",
        DisplayName = spec.Name,
        HardwareId = hardwareId,
        ProviderName = Name,
        Reading = reading,
        IsConnected = true,
        Kind = spec.Kind,
        Origin = spec.Wired ? "Cabo" : "Dongle",
    };

    private static BatteryReading Pct(int pct, bool charging) =>
        BatteryReading.Percentage(Math.Clamp(pct, 0, 100), chargeText: charging ? L.T("carregando", "charging") : null);

    // --- leitores: cada um faz as suas perguntas e entrega aos parsers puros ---

    private static BatteryReading? CloudII(Func<byte[], Func<byte[], bool>, byte[]?> ask)
    {
        static Func<byte[], bool> Is(byte cmd) => r => r.Length > 7 && r[0] == 0x06 && r[1] == 0xFF && r[2] == 0xBB && r[3] == cmd;
        var level = ask(new byte[] { 0x06, 0xFF, 0xBB, 0x02 }, Is(0x02));
        if (level is null) return null;
        var charge = ask(new byte[] { 0x06, 0xFF, 0xBB, 0x03 }, Is(0x03));
        return ParseCloudII(level, charge);
    }

    internal static BatteryReading ParseCloudII(byte[] level, byte[]? charge) => Pct(level[7], charge is { Length: > 4 } c && c[4] == 1);

    internal static byte[] KingstonRequest(byte cmd) =>
        new byte[] { 0x06, 0x00, 0x02, 0x00, 0x9A, 0x00, 0x00, 0x68, 0x4A, 0x8E, 0x0A, 0x00, 0x00, 0x00, 0xBB, cmd, 0x00 };

    private static BatteryReading? CloudIIKingston(Func<byte[], Func<byte[], bool>, byte[]?> ask)
    {
        static Func<byte[], bool> Is(byte cmd) => r => r.Length > 7 && r[0] == 0x0B && r[2] == 0xBB && r[3] == cmd;
        var level = ask(KingstonRequest(0x02), Is(0x02));
        if (level is null) return null;
        var charge = ask(KingstonRequest(0x03), Is(0x03));
        return ParseCloudII(level, charge);
    }

    // ponytail: o HeadsetControl aceita o primeiro relatorio 0x21 sem conferir o eco
    // do comando; aqui tambem. Se o fone mandar outro 0x21 no meio, a leitura erra.
    private static BatteryReading? AlphaWireless(Func<byte[], Func<byte[], bool>, byte[]?> ask)
    {
        static bool Is(byte[] r) => r.Length > 3 && r[0] == 0x21;
        var conn = ask(new byte[] { 0x21, 0xBB, 0x03 }, Is);
        if (conn is null || conn[3] == 0x01) return null;
        var charge = ask(new byte[] { 0x21, 0xBB, 0x0C }, Is);
        var level = ask(new byte[] { 0x21, 0xBB, 0x0B }, Is);
        return level is null ? null : Pct(level[3], charge is { Length: > 3 } c && c[3] == 0x01);
    }

    private static BatteryReading? Alpha2Wireless(Func<byte[], Func<byte[], bool>, byte[]?> ask)
    {
        var conn = ask(new byte[] { 0x52, 0x01 }, r => r.Length > 2 && r[0] == 0x53 && r[1] == 0x01);
        if (conn is null || conn[2] == 0x00) return null;
        var battery = ask(new byte[] { 0x50, 0x02 }, r => r.Length > 3 && r[0] == 0x51 && r[1] == 0x02);
        return battery is null ? null : ParseAlpha2(battery);
    }

    internal static BatteryReading ParseAlpha2(byte[] r) =>
        r[2] > 100 ? BatteryReading.NoData(L.T($"nível fora da faixa ({r[2]})", $"level out of range ({r[2]})")) : Pct(r[2], r[3] == 0x01);

    private static BatteryReading? CloudFlight(Func<byte[], Func<byte[], bool>, byte[]?> ask)
    {
        var r = ask(new byte[] { 0x21, 0xFF, 0x05 }, r => r.Length > 4);
        return r is null ? null : ParseFlight(r);
    }

    /// <summary>Tensao em [3..4]; acima de 4107 mV o fone esta no carregador e nao da nivel.</summary>
    internal static BatteryReading ParseFlight(byte[] r)
    {
        int mv = (r[3] << 8) | r[4];
        if (mv > 0x100B) return BatteryReading.Charging(L.T("carregando; o fone não informa o nível no carregador", "charging; the headset doesn’t report its level on the charger"));
        return BatteryReading.Percentage(FlightPercent(mv), L.T("estimado pela tensão", "estimated from voltage"));
    }

    // ponytail: curva amostrada da estimativa do HeadsetControl para o Flight; se os
    // testadores virem valor torto, e' esta tabela que se calibra.
    private static readonly (int Mv, int Pct)[] FlightCurve =
    {
        (3975, 100), (3950, 93), (3925, 87), (3900, 82), (3875, 77), (3850, 71), (3825, 64),
        (3800, 55), (3775, 46), (3750, 36), (3725, 26), (3700, 16), (3675, 9), (3650, 5), (3500, 0),
    };

    internal static int FlightPercent(int mv)
    {
        if (mv >= FlightCurve[0].Mv) return 100;
        for (int i = 1; i < FlightCurve.Length; i++)
        {
            var (hiMv, hiPct) = FlightCurve[i - 1];
            var (loMv, loPct) = FlightCurve[i];
            if (mv >= loMv) return (int)Math.Round(loPct + (double)(mv - loMv) * (hiPct - loPct) / (hiMv - loMv));
        }
        return 0;
    }

    /// <summary>Dart: pergunta 51 (sem report id, por isso o 00 na frente); resposta 51 .. .. .. nivel carga.</summary>
    private static BatteryReading? Dart(Func<byte[], Func<byte[], bool>, byte[]?> ask)
    {
        var r = ask(new byte[] { 0x00, 0x51 }, r => r.Length > 6 && r[0] == 0x00 && r[1] == 0x51);
        return r is null ? null : ParseDart(r[1..]);
    }

    internal static BatteryReading ParseDart(byte[] d) =>
        d[4] > 100 ? BatteryReading.NoData(L.T($"nível fora da faixa ({d[4]})", $"level out of range ({d[4]})")) : Pct(d[4], d[5] == 0x01);
}
