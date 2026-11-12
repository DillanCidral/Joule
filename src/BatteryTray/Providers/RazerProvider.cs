using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Mouses Razer pelo dongle (HyperSpeed) e no cabo. Pergunta a bateria por
/// relatorio de feature de 90 bytes, pela porta do <see cref="HidRequest.Feature"/>.
///
/// Formato (do openrazer, conferido no razer-battery-report; so o formato):
///   [0] status (00 novo, 01 ocupado, 02 ok, 03 falha, 04 sem resposta, 05 nao suportado)
///   [1] transaction id (por modelo)   [2..3] pacotes restantes   [4] 00
///   [5] tamanho dos dados   [6] classe   [7] comando   [8..87] dados
///   [88] XOR dos bytes 2..87   [89] 00
/// Bateria = classe 07, comando 80, dado [9] em 0..255. Carga = 07/84, [9] != 0.
/// No Windows vai com report id 0 na frente (91 bytes), na colecao do
/// mouse da interface certa, com handle de acesso 0.
///
/// Por leitura: uma pergunta de bateria por mouse (e uma de carga, se o
/// modelo tiver). Mouse dormindo responde "sem resposta" pelo dongle e some
/// da lista, como aparelho desligado.
///
/// Tabela de modelos: razer-battery-report (github.com/xzeldon/razer-battery-report,
/// MIT, Copyright 2024 Timofey Gelazoniya), gerada por ele a partir do openrazer. Sem Razer nesta casa: quem confirma
/// sao os testadores da beta.
/// </summary>
public sealed class RazerProvider : IStandaloneProvider
{
    private const ushort Vid = 0x1532;
    private const int ReportLen = 90;
    private const byte StatusBusy = 0x01, StatusOk = 0x02, StatusNoResponse = 0x04;

    internal sealed record Model(ushort Pid, string Name, byte Tid, int Interface, int WaitUs, bool Charging, bool Wired, DeviceKind Kind = DeviceKind.Mouse)
    {
        public string BaseName { get; } = StripLink(Name);
    }

    private static readonly string[] LinkSuffixes = { " (Wired)", " (Wireless)", " (Receiver)", " Receiver" };

    internal static string StripLink(string name) =>
        LinkSuffixes.Where(name.EndsWith).Select(s => name[..^s.Length]).FirstOrDefault() ?? name;

    private static readonly Model[] Models =
    {
        new(0x001F, "Razer Naga Epic", 0xFF, 0, 600, true, true),
        new(0x0024, "Razer Mamba 2012 (Wired)", 0xFF, 0, 600, true, true),
        new(0x0025, "Razer Mamba 2012 (Wireless)", 0xFF, 0, 600, true, false),
        new(0x0032, "Razer Ouroboros", 0xFF, 0, 600, true, true),
        new(0x003E, "Razer Naga Epic Chroma (Wired)", 0xFF, 0, 600, true, true),
        new(0x003F, "Razer Naga Epic Chroma (Wireless)", 0xFF, 0, 600, true, false),
        new(0x0044, "Razer Mamba Chroma (Wired)", 0xFF, 0, 600, true, true),
        new(0x0045, "Razer Mamba Chroma (Wireless)", 0xFF, 0, 600, true, false),
        new(0x0059, "Razer Lancehead (Wired)", 0x3F, 0, 600, true, true),
        new(0x005A, "Razer Lancehead (Wireless)", 0x3F, 0, 600, true, false),
        new(0x0062, "Razer Atheris (Receiver)", 0x1F, 0, 400_000, false, false),
        new(0x006F, "Razer Lancehead Wireless (Receiver)", 0x1F, 0, 31_000, true, false),
        new(0x0070, "Razer Lancehead Wireless (Wired)", 0x1F, 0, 31_000, true, true),
        new(0x0072, "Razer Mamba Wireless (Receiver)", 0x3F, 0, 31_000, true, false),
        new(0x0073, "Razer Mamba Wireless (Wired)", 0x3F, 0, 31_000, true, true),
        new(0x0077, "Razer Pro Click (Receiver)", 0x1F, 0, 31_000, true, false),
        new(0x007A, "Razer Viper Ultimate (Wired)", 0xFF, 0, 59_900, true, true),
        new(0x007B, "Razer Viper Ultimate (Wireless)", 0xFF, 0, 59_900, true, false),
        new(0x007C, "Razer DeathAdder V2 Pro (Wired)", 0x3F, 0, 59_900, true, true),
        new(0x007D, "Razer DeathAdder V2 Pro (Wireless)", 0x3F, 0, 59_900, true, false),
        new(0x0080, "Razer Pro Click (Wired)", 0x1F, 0, 31_000, true, true),
        new(0x0083, "Razer Basilisk X HyperSpeed", 0xFF, 0, 31_000, false, false),
        new(0x0086, "Razer Basilisk Ultimate", 0x1F, 0, 31_000, true, true),
        new(0x0088, "Razer Basilisk Ultimate Receiver", 0x1F, 0, 31_000, true, false),
        new(0x008F, "Razer Naga Pro (Wired)", 0x1F, 0, 600, true, true),
        new(0x0090, "Razer Naga Pro (Wireless)", 0x1F, 0, 31_000, true, false),
        new(0x0094, "Razer Orochi V2 (Receiver)", 0x1F, 0, 400_000, false, false),
        new(0x009A, "Razer Pro Click Mini (Receiver)", 0x1F, 0, 31_000, true, false),
        new(0x009C, "Razer DeathAdder V2 X HyperSpeed", 0x1F, 0, 31_000, false, false),
        new(0x009E, "Razer Viper Mini SE (Wired)", 0x1F, 0, 59_900, true, true),
        new(0x009F, "Razer Viper Mini SE (Wireless)", 0x1F, 0, 59_900, true, false),
        new(0x00A5, "Razer Viper V2 Pro (Wired)", 0x1F, 0, 31_000, true, true),
        new(0x00A6, "Razer Viper V2 Pro (Wireless)", 0x1F, 0, 31_000, true, false),
        new(0x00A7, "Razer Naga V2 Pro (Wired)", 0x1F, 0, 31_000, true, true),
        new(0x00A8, "Razer Naga V2 Pro (Wireless)", 0x1F, 0, 31_000, true, false),
        new(0x00AA, "Razer Basilisk V3 Pro (Wired)", 0x1F, 0, 31_000, true, true),
        new(0x00AB, "Razer Basilisk V3 Pro (Wireless)", 0x1F, 0, 31_000, true, false),
        new(0x00AF, "Razer Cobra Pro (Wired)", 0x1F, 0, 31_000, true, true),
        new(0x00B0, "Razer Cobra Pro (Wireless)", 0x1F, 0, 31_000, true, false),
        new(0x00B3, "Razer HyperPolling Wireless Dongle", 0x1F, 0, 59_900, true, false),
        new(0x00B4, "Razer Naga V2 HyperSpeed (Receiver)", 0x1F, 0, 31_000, false, false),
        new(0x00B6, "Razer DeathAdder V3 Pro (Wired)", 0x1F, 0, 31_000, true, true),
        new(0x00B7, "Razer DeathAdder V3 Pro (Wireless)", 0x1F, 0, 31_000, true, false),
        new(0x00B8, "Razer Viper V3 HyperSpeed", 0x1F, 0, 59_900, false, false),
        new(0x00B9, "Razer Basilisk V3 X HyperSpeed", 0x1F, 0, 31_000, false, false),
        new(0x00BE, "Razer DeathAdder V4 Pro (Wired)", 0x1F, 0, 31_000, true, true),
        new(0x00BF, "Razer DeathAdder V4 Pro (Wireless)", 0x1F, 0, 31_000, true, false),
        new(0x00C0, "Razer Viper V3 Pro (Wired)", 0x1F, 0, 31_000, true, true),
        new(0x00C1, "Razer Viper V3 Pro (Wireless)", 0x1F, 0, 59_900, true, false),
        new(0x00C2, "Razer DeathAdder V3 Pro (Wired)", 0x1F, 0, 31_000, true, true),
        new(0x00C3, "Razer DeathAdder V3 Pro (Wireless)", 0x1F, 0, 31_000, true, false),
        new(0x00C4, "Razer DeathAdder V3 HyperSpeed (Wired)", 0x1F, 0, 31_000, true, true),
        new(0x00C5, "Razer DeathAdder V3 HyperSpeed (Wireless)", 0x1F, 0, 31_000, true, false),
        new(0x00C7, "Razer Pro Click V2 Vertical Edition (Wired)", 0x1F, 0, 31_000, true, true),
        new(0x00C8, "Razer Pro Click V2 Vertical Edition (Wireless)", 0x1F, 0, 31_000, true, false),
        new(0x00CC, "Razer Basilisk V3 Pro 35K (Wired)", 0x1F, 0, 31_000, true, true),
        new(0x00CD, "Razer Basilisk V3 Pro 35K (Wireless)", 0x1F, 0, 31_000, true, false),
        new(0x00D0, "Razer Pro Click V2 (Wired)", 0x1F, 0, 31_000, true, true),
        new(0x00D1, "Razer Pro Click V2 (Wireless)", 0x1F, 0, 31_000, true, false),
        new(0x00D3, "Razer Basilisk Mobile (Wired)", 0x1F, 0, 31_000, false, true),
        new(0x00D4, "Razer Basilisk Mobile (Receiver)", 0x1F, 0, 31_000, false, false),
        new(0x00D6, "Razer Basilisk V3 Pro 35K Phantom Green Edition (Wired)", 0x1F, 0, 31_000, true, true),
        new(0x00D7, "Razer Basilisk V3 Pro 35K Phantom Green Edition (Wireless)", 0x1F, 0, 31_000, true, false),
        new(0x00DA, "Razer Cobra HyperSpeed", 0x1F, 0, 31_000, true, true),
        new(0x00DB, "Razer Cobra HyperSpeed Receiver", 0x1F, 0, 31_000, true, false),
        new(0x00DE, "Razer Viper V3 Pro SE (Wired)", 0x1F, 0, 31_000, true, true),
        new(0x00DF, "Razer Viper V3 Pro SE (Wireless)", 0x1F, 0, 59_900, true, false),
        new(0x00E2, "Razer Basilisk V4 Pro (Wired)", 0x1F, 3, 59_900, true, true),
        new(0x00E3, "Razer Basilisk V4 Pro (Wireless)", 0x1F, 3, 59_900, true, false),
        new(0x00E5, "Razer Viper V4 Pro (Wired)", 0x1F, 3, 59_900, true, true),
        new(0x00E6, "Razer Viper V4 Pro (Wireless)", 0x1F, 3, 59_900, true, false),

        // Teclados (openrazer, driver/razerkbd_driver.c: transaction id, interface e espera por modelo)
        new(0x0258, "Razer BlackWidow V3 Mini HyperSpeed (Wired)", 0x1F, 3, 600, true, true, DeviceKind.Keyboard),
        new(0x0271, "Razer BlackWidow V3 Mini HyperSpeed (Wireless)", 0x9F, 3, 4_900, true, false, DeviceKind.Keyboard),
        new(0x025A, "Razer BlackWidow V3 Pro (Wired)", 0x3F, 2, 600, true, true, DeviceKind.Keyboard),
        new(0x025C, "Razer BlackWidow V3 Pro (Wireless)", 0x9F, 2, 4_900, true, false, DeviceKind.Keyboard),
        new(0x02B9, "Razer BlackWidow V4 Mini HyperSpeed (Wired)", 0x1F, 3, 600, true, true, DeviceKind.Keyboard),
        new(0x02BA, "Razer BlackWidow V4 Mini HyperSpeed (Wireless)", 0x9F, 3, 4_900, true, false, DeviceKind.Keyboard),
        new(0x02D7, "Razer BlackWidow V4 Tenkeyless HyperSpeed (Wired)", 0x1F, 3, 600, true, true, DeviceKind.Keyboard),
        new(0x02D5, "Razer BlackWidow V4 Tenkeyless HyperSpeed (Wireless)", 0x9F, 2, 4_900, true, false, DeviceKind.Keyboard),
        new(0x0292, "Razer DeathStalker V2 Pro (Wired)", 0x1F, 3, 600, true, true, DeviceKind.Keyboard),
        new(0x0290, "Razer DeathStalker V2 Pro (Wireless)", 0x9F, 2, 4_900, true, false, DeviceKind.Keyboard),
        new(0x0298, "Razer DeathStalker V2 Pro TKL (Wired)", 0x1F, 3, 600, true, true, DeviceKind.Keyboard),
        new(0x0296, "Razer DeathStalker V2 Pro TKL (Wireless)", 0x9F, 2, 4_900, true, false, DeviceKind.Keyboard),
        new(0x02CD, "Razer Joro", 0x1F, 3, 600, true, false, DeviceKind.Keyboard),
    };

    // --- fone Razer Nari (RazerNariBatteryLevel, captura do Synapse; so o formato) ---
    // Feature 0xFF de 64 bytes, pergunta ff 0a 00 fd 04 12 f1 02 05; a resposta
    // traz a tensao em mV nos bytes 12..13 (big-endian). As primeiras leituras
    // podem vir zeradas enquanto o fone prepara a resposta.
    private const ushort PidNari = 0x051C;
    private static readonly byte[] NariAsk = { 0xFF, 0x0A, 0x00, 0xFD, 0x04, 0x12, 0xF1, 0x02, 0x05 };

    internal static BatteryReading? ParseNari(byte[] r)
    {
        int mv = (r[12] << 8) | r[13];
        if (mv is < 3000 or > 4500) return null;
        return BatteryReading.Percentage(LogitechProvider.PercentFromMillivolts(mv), L.T("estimado pela tensão", "estimated from voltage"));
    }

    private DeviceEntry ReadNari(List<HidCollection> cols)
    {
        var target = cols.FirstOrDefault(c => c.FeatureLen == 64 && c.Path.Contains("&mi_05", StringComparison.OrdinalIgnoreCase));
        BatteryReading reading;
        if (target is null)
        {
            reading = BatteryReading.NoData(L.T("não achei o canal de bateria do fone", "couldn’t find the headset’s battery channel"));
        }
        else
        {
            var wire = new byte[64];
            NariAsk.CopyTo(wire, 0);
            var reply = HidRequest.Feature(target, wire, TimeSpan.FromMilliseconds(100), r => ((r[12] << 8) | r[13]) == 0);
            reading = (reply is null ? null : ParseNari(reply)) ?? BatteryReading.NoData(L.T("fone desligado ou longe do receptor; ligue e atualize", "headset off or far from the receiver; turn it on and refresh"));
        }
        return new DeviceEntry
        {
            Key = "razer:razer nari",
            DisplayName = "Razer Nari",
            HardwareId = Known.Id(Vid, PidNari),
            ProviderName = Name,
            Reading = reading,
            IsConnected = true,
            Kind = DeviceKind.Headset,
            Origin = "Dongle",
        };
    }

    private static readonly Dictionary<ushort, Model> ByPid = Models.ToDictionary(m => m.Pid);

    public string Name => "Razer";

    public bool Handles(ushort vid, ushort pid) => vid == Vid && (ByPid.ContainsKey(pid) || pid == PidNari);

    public IEnumerable<DeviceEntry> Discover(IReadOnlyList<HidCollection> collections)
    {
        var found = new List<DeviceEntry>();
        var nari = collections.Where(c => c.Vid == Vid && c.Pid == PidNari).ToList();
        if (nari.Count > 0) found.Add(ReadNari(nari));
        var present = collections.Where(c => c.Vid == Vid && ByPid.ContainsKey(c.Pid)).GroupBy(c => c.Pid).ToList();

        // O mesmo mouse no cabo e no dongle vira uma linha so; o cabo responde melhor.
        foreach (var byModel in present.GroupBy(g => ByPid[g.Key].BaseName))
        {
            DeviceEntry? entry = null;
            string why = L.T("não respondeu; mexa no aparelho e atualize", "didn’t answer; move the device and refresh");
            foreach (var group in byModel.OrderByDescending(g => ByPid[g.Key].Wired))
            {
                entry = ReadModel(ByPid[group.Key], group.ToList(), ref why);
                if (entry is not null) break;
            }
            // Presente sem resposta vai para "outros", com o motivo.
            var first = ByPid[byModel.First().Key];
            found.Add(entry ?? MakeEntry(first, BatteryReading.NoData(why)));
        }
        return found;
    }

    private DeviceEntry? ReadModel(Model model, List<HidCollection> cols, ref string why)
    {
        // Colecoes da interface que responde, a do mouse primeiro.
        string mi = $"&mi_{model.Interface:D2}";
        var targets = cols
            .Where(c => c.FeatureLen == ReportLen + 1)
            .Where(c => c.Path.Contains(mi, StringComparison.OrdinalIgnoreCase)
                        || (model.Interface == 0 && !c.Path.Contains("&mi_", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(c => (c.UsagePage, c.Usage) != (0x01, 0x02))
            .ToList();
        if (targets.Count == 0) return null;

        var wait = TimeSpan.FromMilliseconds(Math.Max(1, model.WaitUs / 1000.0));
        foreach (var target in targets)
        {
            var battery = Ask(target, model.Tid, 0x80, wait);
            if (battery is null) continue; // esta colecao nao atende; tenta a proxima
            if (battery[0] == StatusNoResponse)
            {
                why = L.T("dormindo ou desligado; mexa no aparelho e atualize", "asleep or off; move the device and refresh");
                return null;
            }

            BatteryReading reading;
            if (ParseLevel(battery) is not int percent)
            {
                reading = BatteryReading.NoData(L.T($"o aparelho respondeu status {battery[0]:X2}", $"the device answered status {battery[0]:X2}"));
            }
            else
            {
                string? charge = null;
                if (model.Charging && Ask(target, model.Tid, 0x84, wait) is { } c && c[0] == StatusOk)
                    charge = c[9] != 0 ? L.T("carregando", "charging") : null;
                reading = BatteryReading.Percentage(percent, chargeText: charge);
            }

            return MakeEntry(model, reading);
        }
        Log.Write($"Razer: {model.Name} nao atendeu em nenhuma colecao.");
        return null;
    }

    private DeviceEntry MakeEntry(Model model, BatteryReading reading) => new()
    {
        Key = $"razer:{model.BaseName.ToLowerInvariant()}",
        DisplayName = model.BaseName,
        HardwareId = Known.Id(Vid, model.Pid),
        ProviderName = Name,
        Reading = reading,
        IsConnected = true,
        Kind = model.Kind,
        Origin = model.Wired ? "Cabo" : "Dongle",
    };

    private static byte[]? Ask(HidCollection target, byte tid, byte command, TimeSpan wait)
    {
        var wire = new byte[ReportLen + 1];
        BuildRequest(tid, command).CopyTo(wire, 1);
        var reply = HidRequest.Feature(target, wire, wait, r => r.Length > 1 && r[1] == StatusBusy);
        if (reply is null) return null;
        var report = reply[1..];
        return IsReplyTo(report, command) ? report : null;
    }

    internal static byte[] BuildRequest(byte tid, byte command)
    {
        var r = new byte[ReportLen];
        r[1] = tid;
        r[5] = 0x02;
        r[6] = 0x07;
        r[7] = command;
        r[88] = Crc(r);
        return r;
    }

    internal static byte Crc(byte[] r)
    {
        byte crc = 0;
        for (int i = 2; i < 88; i++) crc ^= r[i];
        return crc;
    }

    /// <summary>Resposta inteira, com CRC certo e da mesma pergunta (classe 07, comando igual).</summary>
    internal static bool IsReplyTo(byte[] r, byte command) =>
        r.Length == ReportLen && r[88] == Crc(r) && r[6] == 0x07 && r[7] == command && r[0] <= 0x05;

    /// <summary>Percentual de uma resposta de bateria com status ok; null se nao for.</summary>
    internal static int? ParseLevel(byte[] r) =>
        r[0] == StatusOk ? (r[9] * 100 + 127) / 255 : null;
}
