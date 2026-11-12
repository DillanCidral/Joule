using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// O "protocolo do relatorio 8" dos chips Compx/Beken, comum nos mouses
/// chineses: ATK, VXE, VGN, Dareu, Pulsar, G-Wolves (geracao HTX Ultra), Hitscan (e GravaStar, Scyrox, Lamzu Atlantis
/// no VID 3554). Uma pergunta por leitura, pela porta do <see cref="HidRequest"/>,
/// na colecao vendor que tem relatorio 0x08 de 16 bytes nos dois sentidos.
///
/// Formato (OpenMouse, AGPL-3.0: drivers atk, vgn, dareu e pulsar; so o
/// formato, nada de codigo copiado):
///   relatorio 0x08 de 16 bytes: [0] comando, [1..14] dados, [15] checksum
///   checksum = 0x55 - (0x08 + soma dos bytes 0..14), em 8 bits
///   bateria = comando 0x04; resposta [4] tamanho, [5] %, [6] carregando, [7..8] mV
///
/// A tabela de nomes vem do catalogo do OpenMouse (gerado do app oficial ATK
/// HUB). PIDs compartilhados por varios modelos ganham o nome em comum. Sem
/// ATK/VXE nesta casa: quem confirma sao os testadores.
/// </summary>
public sealed class CompxProvider : IStandaloneProvider
{
    private const ushort VidAtk = 0x373B, VidCompx = 0x3554, VidPulsar = 0x3710, VidDareu = 0x260D, VidGwolves = 0x33E4, VidHitscan = 0x3770;
    private const byte ReportId = 0x08, CmdBattery = 0x04;
    private static readonly TimeSpan AskTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>Pulsar com outro protocolo (Pro e XS1): ficam de fora.</summary>
    private static readonly HashSet<ushort> PulsarOther = new() { 0x5405, 0x3404, 0x3409, 0x3410, 0x5402, 0x5403 };

    private static readonly Dictionary<(ushort Vid, ushort Pid), string> Names = new()
    {
        [(0x3554, 0xF502)] = "ATK Dragonfly F1 PRO",
        [(0x3554, 0xF506)] = "ATK Dragonfly F1",
        [(0x3554, 0xF50A)] = "ATK Dragonfly",
        [(0x3554, 0xF58C)] = "VXE R1 PRO",
        [(0x3554, 0xF58F)] = "VXE (mouse)",
        [(0x373B, 0x1014)] = "ATK F1 PRO",
        [(0x373B, 0x1015)] = "ATK F1 PRO MAX",
        [(0x373B, 0x1016)] = "ATK X1 PRO",
        [(0x373B, 0x1017)] = "ATK X1 PRO MAX",
        [(0x373B, 0x1018)] = "ATK Z1 PRO",
        [(0x373B, 0x1019)] = "ATK Z1 PRO MAX",
        [(0x373B, 0x102E)] = "ATK F1 Ultimate",
        [(0x373B, 0x102F)] = "ATK X1 Ultra",
        [(0x373B, 0x1030)] = "ATK FIERCE X",
        [(0x373B, 0x103C)] = "ATK F1 Ultra",
        [(0x373B, 0x103D)] = "ATK X1 Ultimate",
        [(0x373B, 0x103E)] = "MAD R MAJOR",
        [(0x373B, 0x103F)] = "MAD R",
        [(0x373B, 0x1045)] = "ATK F1 EXTREME",
        [(0x373B, 0x104A)] = "ATK Z1 Ultra",
        [(0x373B, 0x104B)] = "MAD R +",
        [(0x373B, 0x104C)] = "MAD R MAJOR+",
        [(0x373B, 0x105B)] = "ATK F1 LEVIATAN",
        [(0x373B, 0x1065)] = "VXE R1 NK",
        [(0x373B, 0x1066)] = "VXE R1 NK",
        [(0x373B, 0x1084)] = "VXE R1S+",
        [(0x373B, 0x1086)] = "ATK U2 MINI",
        [(0x373B, 0x1087)] = "ATK U2 Ultimate",
        [(0x373B, 0x10AC)] = "ATK U2 Ultra",
        [(0x373B, 0x10C7)] = "VXE R1 NK",
        [(0x373B, 0x10C8)] = "VXE R1 NK",
        [(0x373B, 0x10D8)] = "ATK X1",
        [(0x373B, 0x10E7)] = "VXE V3 NK",
        [(0x373B, 0x10E8)] = "VXE V3 PRO NK",
        [(0x373B, 0x10E9)] = "ATK X1 EXTREME",
        [(0x373B, 0x10EC)] = "ATK U2 Pro",
        [(0x373B, 0x10FD)] = "ATK Y9 PRO MAX",
        [(0x373B, 0x1106)] = "ATK X1 S NK",
        [(0x373B, 0x1107)] = "ATK X1 SE NK",
        [(0x373B, 0x110D)] = "ATK U2 Plus NK",
        [(0x373B, 0x110E)] = "ATK U2 NK",
        [(0x373B, 0x1115)] = "ATK A9 Plus NK",
        [(0x373B, 0x1116)] = "ATK A9 NK",
        [(0x373B, 0x1117)] = "ATK A9 Pro Max",
        [(0x373B, 0x1119)] = "ATK A9 Ultra",
        [(0x373B, 0x112D)] = "ATK A9 Pro",
        [(0x373B, 0x112E)] = "ATK Y9 Pro",
        [(0x373B, 0x1135)] = "ATK A9 SE NK",
        [(0x373B, 0x113E)] = "ATK X1 Lite",
        [(0x373B, 0x113F)] = "ATK X1E",
        [(0x373B, 0x1147)] = "ATK A9 PRO MAX",
        [(0x373B, 0x1149)] = "ATK U2 SE NK",
        [(0x373B, 0x114A)] = "DRAGONFLY F1 V2 Plus NK",
        [(0x373B, 0x114B)] = "DRAGONFLY F1 V2 NK",
        [(0x373B, 0x114C)] = "DRAGONFLY F1 V2 SE NK",
        [(0x373B, 0x1154)] = "ATK ZERO",
        [(0x373B, 0x1157)] = "ATK A9",
        [(0x373B, 0x1158)] = "ATK U2 Pro Max",
        [(0x373B, 0x115B)] = "ATK Y9 Plus NK",
        [(0x373B, 0x115C)] = "ATK Y9 NK",
        [(0x373B, 0x115D)] = "ATK Y9 SE NK",
        [(0x373B, 0x1175)] = "DRAGONFLY F1 V2 Pro",
        [(0x373B, 0x11A0)] = "ATK A9 X",
        [(0x373B, 0x11A6)] = "ATK Duckbill Ultra",
        [(0x373B, 0x11AA)] = "ATK U2",
        [(0x373B, 0x11AB)] = "DRAGONFLY F1 V2 PRO MAX",
        [(0x373B, 0x11B6)] = "ATK A9 Ultimate",
        [(0x373B, 0x11CC)] = "ATK ZERO EX",
        [(0x373B, 0x11CF)] = "ATK Y9 Ultimate",
        [(0x373B, 0x11D2)] = "VXE R1 UlTRA",
        [(0x373B, 0x11DB)] = "ATK A9 Pro Max 2.0 NK",
        [(0x373B, 0x11DD)] = "ATK A9 Ultra Max 2.0",
        [(0x373B, 0x11E3)] = "ATK F1 EXTREME 2.0",
        [(0x373B, 0x11E4)] = "ATK F1 Ultimate 2.0",
        [(0x373B, 0x11E5)] = "ATK X1 EXTREME 2.0",
        [(0x373B, 0x11E7)] = "ATK X1 Ultimate 2.0",
        [(0x373B, 0x11EC)] = "ATK X1 Ultra Max 2.0",
        [(0x373B, 0x11ED)] = "ATK F1 Ultra Max 2.0",
        [(0x373B, 0x11F0)] = "ATK F1 LEVIATAN 2.0",
        [(0x373B, 0x11F2)] = "ATK A9 Ultra Max 2.0 Tri-Mode",
        [(0x373B, 0x11F3)] = "ATK A9 NB ULTRA",
        [(0x373B, 0x11F5)] = "ATK U2 Ultimate 2.0",
        [(0x373B, 0x11F6)] = "ATK U2 Mini 2.0",
        [(0x373B, 0x11F7)] = "ATK U2 Ultra Max 2.0",
        [(0x373B, 0x11F9)] = "VXE R1 Ultra",
        [(0x373B, 0x1201)] = "ATK Z1 Ultimate 2.0",
        [(0x373B, 0x120A)] = "ATK A9 Air",
        [(0x373B, 0x120B)] = "ATK X1 EXTREME GT",
        [(0x373B, 0x120D)] = "ATK Ghost EXTREME",
        [(0x373B, 0x120E)] = "ATK Ghost Ultimate",
        [(0x373B, 0x1211)] = "ATK A9 Ultimate 3.0",
        [(0x373B, 0x1213)] = "VXE V3 NK",
        [(0x373B, 0x1214)] = "VXE V3 PRO NK",
        [(0x373B, 0x1215)] = "VXE V3 Plus",
        [(0x373B, 0x121F)] = "VXE R1 S NK",
        [(0x373B, 0x1222)] = "ATK A9 SE NK",
        [(0x373B, 0x1223)] = "ATK A9 NK",
        [(0x373B, 0x1224)] = "ATK A9 Plus NK",
        [(0x373B, 0x1230)] = "ATK Duckbill EXTREME",
        [(0x373B, 0x123A)] = "ATK Ghost Ultimate",
        [(0x373B, 0x1245)] = "ATK A9 NB",
        [(0x373B, 0x1248)] = "VXE X3",
        [(0x373B, 0x1249)] = "VXE X3 Pro",
        [(0x373B, 0x124A)] = "VXE X3 Turbo",
        [(0x373B, 0x124C)] = "VXE R1 NK",
        [(0x373B, 0x124D)] = "VXE R1 S NK",
        [(0x373B, 0x124E)] = "ATK ZERO",
        [(0x373B, 0x1251)] = "ATK A9 Mini +",
        [(0x373B, 0x1256)] = "ATK A9 Mini",
        [(0x373B, 0x125A)] = "ATK A9 Mini",
        [(0x373B, 0x1263)] = "ATK A9 Plus NK",
        [(0x373B, 0x1264)] = "ATK A9 Pro Max 2.0 NK",
        [(0x373B, 0x1266)] = "ATK X EXTREME",
        [(0x373B, 0x1268)] = "ATK A9 Mini",
        [(0x373B, 0x1269)] = "ATK A9 Mini +",
        [(0x373B, 0x126C)] = "ATK U2 Plus NK",
        [(0x373B, 0x126D)] = "ATK U2 Pro Max",
        [(0x373B, 0x126E)] = "ATK F1 Ultra Max 2.0",
        [(0x373B, 0x126F)] = "ATK A9 Ultra Max 2.0",
        [(0x373B, 0x1270)] = "ATK A9 Ultra Max 2.0 Tri-Mode",
        [(0x373B, 0x1271)] = "VXE R1",
        [(0x373B, 0x1272)] = "VXE R1 PRO",
        [(0x373B, 0x1273)] = "VXE R1 PRO MAX",
        [(0x373B, 0x1274)] = "ATK X1 Ultra Max 2.0",
        [(0x373B, 0x127B)] = "VXE V3 PRO Max",
        [(0x373B, 0x127C)] = "ATK X1 Air +",
        [(0x373B, 0x127E)] = "ATK A9 Ultimate +",
        [(0x373B, 0x127F)] = "ATK A9 Air +",
        [(0x373B, 0x1282)] = "ATK Duckbill ELITE",
        [(0x373B, 0x1285)] = "ATK X1 EXTREME 2.0 +",
        [(0x373B, 0x1286)] = "ATK X1 Ultimate 2.0 +",
        [(0x373B, 0x1287)] = "ATK A9 Ultimate +",
        [(0x373B, 0x1288)] = "ATK A9 Air +",
        [(0x373B, 0x1289)] = "ATK ZERO EX +",
        [(0x373B, 0x128A)] = "ATK Ghost EXTREME +",
        [(0x373B, 0x128B)] = "ATK Ghost Ultimate +",
        [(0x373B, 0x128C)] = "ATK U2 Ultimate 2.0 +",
        [(0x373B, 0x128D)] = "ATK U2 Mini 2.0 +",
        [(0x373B, 0x128E)] = "ATK A9 Mini +",
        [(0x373B, 0x128F)] = "ATK A9 Ultra Max 2.0 +",
        [(0x373B, 0x1292)] = "ATK A9 Ultimate +",
        [(0x373B, 0x1293)] = "ATK A9 Air +",
        [(0x373B, 0x1296)] = "ATK PHANTOM ULTIMATE",
        [(0x373B, 0x1299)] = "ATK X1 SE Nearlink",
        [(0x373B, 0x129A)] = "VXE R1 PRO MAX 2.0 NK",
        [(0x373B, 0x129B)] = "ATK F1 V3 EXTREME +",
        [(0x373B, 0x129C)] = "ATK F1 Ultimate 2.0 +",
        [(0x373B, 0x129D)] = "ATK F1 V3 LEVIATAN +",
        [(0x373B, 0x129E)] = "ATK ZERO +",
        [(0x373B, 0x12A2)] = "ATK X1 Air +",
        [(0x373B, 0x12A7)] = "ATK F1 V3 ULTIMATE +",
        [(0x373B, 0x12A8)] = "ATK F1 V3 ULTIMATE +",
        [(0x373B, 0x12A9)] = "ATK F1 V3 ULTIMATE +",
        [(0x373B, 0x12AB)] = "ATK A9 NB ULTRA +",
        [(0x373B, 0x12AF)] = "ATK Y9 Ultimate +",
        [(0x373B, 0x12B0)] = "ATK Z1 Ultimate 2.0 +",
        [(0x373B, 0x12B1)] = "VXE R2 SE NK",
        [(0x373B, 0x12B3)] = "ATK A9 Ultimate Silent",
        [(0x373B, 0x12B4)] = "ATK X1 Ultimate 2.0 Silent",
        [(0x373B, 0x12BF)] = "VXE R1SE+ Silent",
        [(0x373B, 0x12C6)] = "ATK A9 Wireless Charging",
        [(0x373B, 0x12CE)] = "ATK PHANTOM ULTIMATE",
        [(0x373B, 0x12D0)] = "ATK A9 NB ULTRA",
        [(0x373B, 0x12D2)] = "VXE V3 NK",
        [(0x373B, 0x12D7)] = "Flow 7",
        [(0x373B, 0x12D9)] = "ATK X1 ULTIMATE 2.0+",
        [(0x373B, 0x12DC)] = "VXE R1 SE+",
        [(0x373B, 0x12DE)] = "ATK X1 Ultra Max 2.0 + Tri-Mode",
        [(0x373B, 0x12E1)] = "VXE T1",
        [(0x373B, 0x12E3)] = "ATK U2 Plus NK",
        [(0x373B, 0x12EC)] = "ATK X1 Air+",
        [(0x373B, 0x12F1)] = "ATK A9 Ultimate +",
        [(0x373B, 0x12FA)] = "ATK F1 V3 ULTIMATE",
        [(0x373B, 0x12FF)] = "ATK X1 Ultra Max 2.0",
        [(0x373B, 0x1300)] = "ATK A9 Plus NK",
        [(0x373B, 0x1301)] = "ATK A9 Pro Max 2.0 NK",
        [(0x373B, 0x1304)] = "ATK F1 V3 ULTIMATE+",
        [(0x373B, 0x1305)] = "ATK A9 3.0 NK",
        [(0x373B, 0x1306)] = "VXE V3 PRO NK",
        [(0x373B, 0x1307)] = "ATK F1 V3 Ultra Max",
        [(0x373B, 0x1308)] = "ATK F1 V3 ULTIMATE+",
        [(0x373B, 0x130E)] = "ATK X1 SE NK",
        [(0x373B, 0x130F)] = "ATK A9 SE 3.0 NK",
        [(0x373B, 0x1310)] = "VXE R1 S NK",
        [(0x373B, 0x1311)] = "VXE R1N NK",
        [(0x373B, 0x1312)] = "ATK PHANTOM ULTIMATE",
        [(0x373B, 0x1314)] = "ATK A9 Ultimate",
        [(0x373B, 0x1319)] = "VXE R1 SE+",
        [(0x373B, 0x131A)] = "ATK X1 S NK",
        [(0x373B, 0x132B)] = "MAD R",
        [(0x373B, 0x1337)] = "ATK A9 Plus 3.0 NK",
        [(0x373B, 0x1338)] = "ATK A9 Pro Max 3.0 NK",
        [(0x373B, 0x1085)] = "VXE R1 SE (receptor)",
        [(0x373B, 0x10C9)] = "Receptor ATK NK",
        [(0x3554, 0xF58A)] = "VXE R1 PRO MAX (receptor)",
        [(0x3554, 0xF58E)] = "VXE R1 (receptor)",
        [(0x3554, 0xFB56)] = "VGN Dragonfly F2 Master+ (receptor)",
        [(0x3554, 0xFB57)] = "VGN Dragonfly F2 Master+",
        [(0x260D, 0x1114)] = "Dareu A950 PRO Mg (receptor)",
        [(0x260D, 0x1117)] = "Dareu A950 PRO Mg",
        // G-Wolves XVI 0 (o receptor 3854 serve a geracao toda; os outros sao no cabo).
        [(0x33E4, 0x3854)] = "G-Wolves (receptor)",
        [(0x33E4, 0x5618)] = "G-Wolves HTX Ultra", [(0x33E4, 0x5619)] = "G-Wolves HTX Ultra",
        [(0x33E4, 0x5219)] = "G-Wolves HTS Plus Pro", [(0x33E4, 0x5418)] = "G-Wolves HTS Plus", [(0x33E4, 0x5419)] = "G-Wolves HTS Plus",
        [(0x33E4, 0x4718)] = "G-Wolves Lycan", [(0x33E4, 0x4719)] = "G-Wolves Lycan", [(0x33E4, 0x3619)] = "G-Wolves Fenrir Pro",
        [(0x33E4, 0x2719)] = "G-Wolves HTX Mini", [(0x33E4, 0x4219)] = "G-Wolves WARG", [(0x33E4, 0x2619)] = "G-Wolves HTX Mini Asym",
        [(0x33E4, 0x3519)] = "G-Wolves Fenrir Asym", [(0x33E4, 0x001A)] = "G-Wolves HT-S ACE", [(0x33E4, 0x00AA)] = "G-Wolves HT-S ACE",
        [(0x3770, 0x0100)] = "Hitscan", [(0x3770, 0x0200)] = "Hitscan (receptor)", [(0x3770, 0x0300)] = "Hitscan (receptor 8K)",
    };

    public string Name => "Compx";

    public bool Handles(ushort vid, ushort pid) => vid switch
    {
        VidAtk => Names.ContainsKey((vid, pid)),
        VidCompx => true,
        VidPulsar => !PulsarOther.Contains(pid),
        VidDareu => pid is 0x1114 or 0x1117,
        VidGwolves or VidHitscan => Names.ContainsKey((vid, pid)),
        _ => false,
    };

    private static string Brand(ushort vid) => vid switch
    {
        VidAtk => "ATK",
        VidPulsar => "Pulsar",
        VidDareu => "Dareu",
        VidGwolves => "G-Wolves",
        VidHitscan => "Hitscan",
        _ => "VGN/VXE",
    };

    public IEnumerable<DeviceEntry> Discover(IReadOnlyList<HidCollection> collections)
    {
        foreach (var group in collections.Where(c => Handles(c.Vid, c.Pid)).GroupBy(c => (c.Vid, c.Pid)))
        {
            var (vid, pid) = group.Key;
            string? product = group.Select(c => c.Product).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
            string name = Names.TryGetValue((vid, pid), out var n) ? n
                : product is not null ? (product.Contains(Brand(vid), StringComparison.OrdinalIgnoreCase) ? product : $"{Brand(vid)} {product}")
                : $"Mouse {Brand(vid)}";
            BatteryReading reading;
            try { reading = Read(group.ToList()); }
            catch (Exception ex)
            {
                // Um mouse com problema nao derruba os outros.
                Log.Error($"{Name} {vid:X4}:{pid:X4}", ex);
                reading = BatteryReading.Error(ex.Message);
            }
            yield return new DeviceEntry
            {
                Key = $"compx:{vid:X4}:{pid:X4}",
                DisplayName = name,
                HardwareId = BatteryTray.Core.Known.Id(vid, pid),
                ProviderName = Name,
                Reading = reading,
                IsConnected = true,
                Kind = DeviceKind.Mouse,
                Origin = "Dongle",
            };
        }
    }

    private static BatteryReading Read(List<HidCollection> cols)
    {
        // O canal: relatorio 0x08 de 16 bytes (17 com o id) nos dois sentidos; o 0xFF02/0x0002 primeiro.
        var target = cols.Where(c => c.UsagePage >= 0xFF00 && c.OutputLen == 17 && c.InputLen == 17)
                         .OrderBy(c => (c.UsagePage, c.Usage) != (0xFF02, 0x0002))
                         .FirstOrDefault();
        if (target is null) return BatteryReading.NoData(L.T("não achei o canal de bateria deste mouse", "couldn’t find this mouse’s battery channel"));

        using var session = HidRequest.Open(new[] { target });
        if (session is null) return BatteryReading.NoData(L.T("o canal de bateria não abriu", "the battery channel didn’t open"));

        var request = new byte[17];
        request[0] = ReportId;
        BuildPayload(CmdBattery).CopyTo(request, 1);
        var reply = session.Ask(target, request, r => r.Length >= 17 && r[0] == ReportId && IsBatteryFrame(r[1..17]), AskTimeout);
        if (reply is null) return BatteryReading.NoData(L.T("o mouse não respondeu; pode estar dormindo, mexa nele e atualize", "the mouse didn’t answer; it may be asleep, move it and refresh"));
        return ParseBattery(reply[1..17]);
    }

    internal static byte Checksum(byte[] frame)
    {
        int sum = ReportId;
        for (int i = 0; i < 15; i++) sum += frame[i];
        return (byte)((0x55 - (sum & 0xFF)) & 0xFF);
    }

    internal static byte[] BuildPayload(byte cmd)
    {
        var p = new byte[16];
        p[0] = cmd;
        p[15] = Checksum(p);
        return p;
    }

    internal static bool IsBatteryFrame(byte[] f) => f.Length >= 16 && f[0] == CmdBattery && f[15] == Checksum(f);

    internal static BatteryReading ParseBattery(byte[] f)
    {
        int length = Math.Min((int)f[4], 10);
        if (length < 1) return BatteryReading.NoData(L.T("o mouse não informou o nível", "the mouse didn’t report its level"));
        bool charging = length >= 2 && f[6] != 0;
        if (f[5] > 100) return ChineseBrandsProvider.OutOfRange; // regra 6: valor sem sentido nao vira 100%
        return BatteryReading.Percentage(f[5], chargeText: charging ? L.T("carregando", "charging") : null);
    }
}
