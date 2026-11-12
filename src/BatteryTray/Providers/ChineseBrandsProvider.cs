using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Marcas chinesas de protocolo proprio, cada uma com uma pergunta (ou nenhuma)
/// por leitura. Formatos do OpenMouse (AGPL-3.0; so o formato, nada de codigo
/// copiado):
///
///   AJAZZ NJ07/NJ08 (A8A4/A8A5) -> saida 0xF0: 30 01; a resposta que comeca em
///     30 traz [7] % e [8] carregando. (src/ajazz, src/drivers/ajazz/hid.ts)
///   K-Snake X11 / Noir M2-NEX (A8A4/A8A5:2255) -> saida 0: 55 30 A5 0B 2E 01 01;
///     resposta [8] %, [9] carregando. (src/ksnake)
///   Rapoo VT9 (24AE:1205/4405)  -> NENHUMA pergunta: um GET_REPORT da feature
///     0x2A na colecao 0xFF0B/0x0104, % no byte 8 depois do id. (src/rapoo)
///   Lamzu e parentes (373E, 37B0, WLmouse 36A7, G-Wolves "xvi-new") -> feature 0
///     de 64 bytes: 00 00 &lt;alvo&gt; 02 00 83; a resposta (status A1, pagina 00,
///     comando 83) traz [6] carregando e [7] %. Pendente/ocupado (A0/A3): le de
///     novo, sem repetir a pergunta. (src/compx/codec.ts, drivers lamzu, wlmouse,
///     gwolves/xvi-new-hid.ts)
///   G-Wolves "xvi" (33E4) -> feature 0 de 64 bytes: 00 02 8F &lt;sem fio&gt;;
///     resposta A1 .. 8F .. [4] carregando, [5] %. (drivers/gwolves/xvi-hid.ts)
///   GearHub-V5 (3151:402D, Attack Shark R2, Lingbao M5 Pro) -> feature 0 de 64
///     bytes: F7; o RECEPTOR responde [0] pronto, [1] % do teclado, [2] % do
///     mouse, [3]/[4] = 0 se teclado/mouse ligados. (src/gearhub)
///   Bytech (372E:1014/1015) -> feature 3: [soma] 50 00 02 4F 81; resposta com
///     [3] = 02 traz o % em [5] (E4 = cheio). (src/bytech)
///   Realtek 8K: Motospeed X6 (0BDA:FFE0) e mouses Keychron (3434) -> saida B3: 06;
///     a resposta B4 06 .. traz em [19] o % (bit 7 = carregando).
///     (src/motospeed, src/keychron, drivers/keychron/mouse-8k-hid.ts)
///
/// Sem esses aparelhos nesta casa: quem confirma sao os testadores.
/// </summary>
public sealed class ChineseBrandsProvider : IStandaloneProvider
{
    private const ushort VidAjazzUsb = 0xA8A4, VidAjazzDongle = 0xA8A5, VidRapoo = 0x24AE,
        VidGearHub = 0x3151, VidBytech = 0x372E, VidMotospeed = 0x0BDA, VidKeychron = 0x3434, VidGwolves = 0x33E4;
    private const ushort PidKsnake = 0x2255, PidGearHubDongle = 0x402D, PidMotospeedDongle = 0xFFE0, PidBytechWireless = 0x1014;
    private static readonly TimeSpan AskTimeout = TimeSpan.FromMilliseconds(800);

    private static readonly Dictionary<ushort, string> Ajazz = new()
    {
        [0x2157] = "AJAZZ NJ07", [0x2158] = "AJAZZ NJ08", [0x2167] = "AJAZZ NJ07 MC",
        [0x2168] = "AJAZZ NJ08 MC", [0x2177] = "AJAZZ NJ07 MC PRO", [0x2178] = "AJAZZ NJ08 MC PRO",
    };

    private static readonly Dictionary<ushort, string> RapooVt9 = new() { [0x1205] = "Rapoo VT9 Pro", [0x4405] = "Rapoo VT9 Pro" };

    /// <summary>(nome, sem fio, alvo da pergunta): o alvo e' o do OpenMouse por produto.</summary>
    private static readonly Dictionary<(ushort Vid, ushort Pid), (string Name, bool Wireless, byte Target)> Lamzu = new()
    {
        [(0x373E, 0x0098)] = ("RAWM Leviathan V4 GT", false, 0x02),
        [(0x373E, 0x0099)] = ("RAWM Leviathan V4 GT", true, 0x02),
        [(0x373E, 0x001C)] = ("Lamzu Maya X", false, 0x02),
        [(0x373E, 0x001D)] = ("Lamzu Maya X", true, 0x02),
        [(0x373E, 0x001E)] = ("Lamzu Maya X", true, 0x02),
        [(0x373E, 0x006A)] = ("CRDRAKO KO-ONE", false, 0x00),
        [(0x373E, 0x006B)] = ("CRDRAKO KO-ONE", true, 0x02),
        [(0x373E, 0x0032)] = ("LunaFury LUNA33", false, 0x00),
        [(0x373E, 0x0033)] = ("LunaFury LUNA33", true, 0x02),
        [(0x373E, 0x0054)] = ("LunaFury TYPE33", false, 0x00),
        [(0x373E, 0x0084)] = ("LunaFury TYPE33", true, 0x02),
        [(0x373E, 0x0046)] = ("Attack Shark R5 Ultra", false, 0x02),
        [(0x373E, 0x0047)] = ("Attack Shark R5 Ultra", true, 0x02),
        [(0x373E, 0x0021)] = ("Attack Shark R6", false, 0x02),
        [(0x373E, 0x0022)] = ("Attack Shark R6", true, 0x02),
        [(0x373E, 0x003A)] = ("Attack Shark R8", false, 0x02),
        [(0x373E, 0x003B)] = ("Attack Shark R8", true, 0x02),
        [(0x373E, 0x0066)] = ("Vancer Akitsu Medium 3950", false, 0x02),
        [(0x373E, 0x0067)] = ("Vancer Akitsu Medium 3950", true, 0x02),
        [(0x37B0, 0x0001)] = ("Lamzu Aurora 8K Receiver", true, 0x02),
        [(0x37B0, 0x0003)] = ("Lamzu Aurora 1K Receiver", true, 0x02),
        [(0x37B0, 0x0009)] = ("Lamzu Inca 8K", false, 0x02),
        [(0x37B0, 0x000F)] = ("Lamzu Inca 8K", true, 0x02),
        [(0x37B0, 0x0010)] = ("Lamzu Inca 8K", true, 0x02),
        [(0x37B0, 0x0007)] = ("Lamzu Paro Aurora", false, 0x02),
        [(0x37B0, 0x000D)] = ("Lamzu Paro Aurora", true, 0x02),
        [(0x37B0, 0x000E)] = ("Lamzu Paro Aurora", true, 0x02),
        // WLmouse (36A7). O Beast X 4K (A887) fala outro protocolo e fica de fora.
        [(0x36A7, 0xA860)] = ("WLmouse Beast G", true, 0x02),
        [(0x36A7, 0xA861)] = ("WLmouse Beast G", false, 0x02),
        [(0x36A7, 0xA863)] = ("WLmouse Huan", true, 0x02),
        [(0x36A7, 0xA864)] = ("WLmouse Huan", false, 0x02),
        [(0x36A7, 0xA866)] = ("WLmouse Beast Miao", true, 0x02),
        [(0x36A7, 0xA867)] = ("WLmouse Beast Miao", false, 0x02),
        [(0x36A7, 0xA868)] = ("WLmouse Beast Mini Pro", true, 0x02),
        [(0x36A7, 0xA869)] = ("WLmouse Beast Mini Pro", false, 0x02),
        [(0x36A7, 0xA870)] = ("WLmouse Beast X Pro", true, 0x02),
        [(0x36A7, 0xA871)] = ("WLmouse Beast X Pro", false, 0x02),
        [(0x36A7, 0xA872)] = ("WLmouse Strider", true, 0x02),
        [(0x36A7, 0xA873)] = ("WLmouse Strider", false, 0x02),
        [(0x36A7, 0xA874)] = ("WLmouse Ying", true, 0x02),
        [(0x36A7, 0xA875)] = ("WLmouse Ying", false, 0x02),
        [(0x36A7, 0xA878)] = ("WLmouse Sword X", true, 0x02),
        [(0x36A7, 0xA879)] = ("WLmouse Sword X", false, 0x02),
        [(0x36A7, 0xA880)] = ("WLmouse Beast Max", true, 0x02),
        [(0x36A7, 0xA881)] = ("WLmouse Beast Max", false, 0x02),
        [(0x36A7, 0xA882)] = ("WLmouse (receptor 1K)", true, 0x02),
        [(0x36A7, 0xA883)] = ("WLmouse Beast X", true, 0x02),
        [(0x36A7, 0xA884)] = ("WLmouse Beast X", false, 0x02),
        [(0x36A7, 0xA885)] = ("WLmouse Beast Mini", true, 0x02),
        [(0x36A7, 0xA886)] = ("WLmouse Beast Mini", false, 0x02),
        // G-Wolves "xvi-new": a mesma pergunta do Lamzu.
        [(0x33E4, 0x3808)] = ("G-Wolves HTM Plus", false, 0x02),
        [(0x33E4, 0x3817)] = ("G-Wolves HTM Plus", true, 0x02),
        [(0x33E4, 0x6808)] = ("G-Wolves HSK Pro 2.0", false, 0x02),
        [(0x33E4, 0x6817)] = ("G-Wolves HSK Pro 2.0", true, 0x02),
        [(0x33E4, 0x5608)] = ("G-Wolves HTXU", false, 0x02),
        [(0x33E4, 0x5617)] = ("G-Wolves HTXU", true, 0x02),
        [(0x33E4, 0x3608)] = ("G-Wolves Fenrir Pro", false, 0x02),
        [(0x33E4, 0x3617)] = ("G-Wolves Fenrir Pro", true, 0x02),
    };

    /// <summary>G-Wolves "xvi" (protocolo antigo de 64 bytes): nome e se e' sem fio.</summary>
    private static readonly Dictionary<ushort, (string Name, bool Wireless)> GwolvesXvi = new()
    {
        [0x3908] = ("VUK", false), [0x3917] = ("VUK", true), [0x7904] = ("HT-S2", false), [0x7913] = ("HT-S2", true),
        [0x3708] = ("Fenir Max", false), [0x3717] = ("Fenir Max", true), [0x5308] = ("HTS Ultra", false), [0x5317] = ("HTS Ultra", true),
        [0x7704] = ("HTR", false), [0x7713] = ("HTR", true), [0x3508] = ("Fenrir", false), [0x3517] = ("Fenrir", true),
        [0x7908] = ("HT-S2 Pro", false), [0x7917] = ("HT-S2 Pro", true), [0x5808] = ("HSK Pro", false), [0x5817] = ("HSK Pro", true),
        [0x2708] = ("HTX Mini", false), [0x2717] = ("HTX Mini", true), [0x7204] = ("HSK Lite", false), [0x7203] = ("HSK Lite", true),
        [0x5807] = ("HSK Pro", true), [0x5407] = ("HTS Plus", true), [0x5707] = ("HTX", true), [0x5907] = ("HSK Plus", true),
        [0x5408] = ("HTS Plus", false), [0x5417] = ("HTS Plus", true), [0x5708] = ("HTX", false), [0x5717] = ("HTX", true),
        [0x5908] = ("HSK Plus", false), [0x5917] = ("HSK Plus", true), [0x7708] = ("HTR Pro", false), [0x7717] = ("HTR Pro", true),
        [0x5804] = ("HSK Pro ACE", false), [0x5803] = ("HSK Pro ACE", true), [0x5404] = ("HTS Plus ACE", false), [0x5403] = ("HTS Plus ACE", true),
        [0x5704] = ("HTX ACE", false), [0x5703] = ("HTX ACE", true), [0x5904] = ("HSK Plus ACE", false), [0x5903] = ("HSK Plus ACE", true),
    };

    /// <summary>Mouses e receptores Keychron (lista do Keychron Launcher, via OpenMouse).</summary>
    private static readonly Dictionary<ushort, string> Keychron = new()
    {
        [0xD033] = "Keychron M3", [0xD035] = "Keychron M1", [0xD036] = "Keychron M3 Mini", [0xD03B] = "Keychron M2",
        [0xD03D] = "Keychron M2 Mini", [0xD03F] = "Keychron M6", [0xD043] = "Keychron M4", [0xD044] = "Keychron M7",
        [0xD048] = "Keychron M5 8K", [0xD049] = "Keychron M6 8K", [0xD04A] = "Keychron M3 KM", [0xD04C] = "Keychron M3 Combo",
        [0xD04D] = "Keychron M2 Mini Combo", [0xD04E] = "Keychron M3 Combo", [0xD04F] = "Keychron M3 Mini", [0xD050] = "Keychron M3 8K",
        [0xD051] = "Keychron M3 Mini 8K", [0xD052] = "Keychron M2 8K", [0xD053] = "Keychron M4 8K", [0xD054] = "Keychron M1 8K",
        [0xD055] = "Keychron M2 Mini 8K", [0xD056] = "Keychron M7 8K", [0xD058] = "Keychron BM22", [0xD059] = "Keychron M1",
        [0xD060] = "Keychron M6", [0xD061] = "Keychron BM24", [0xD062] = "Keychron BM25", [0xD063] = "Keychron BM26",
        [0xD064] = "Keychron M6", [0xD067] = "Keychron M6 SE", [0xD068] = "Keychron M5", [0xD069] = "Keychron M7",
        [0xD06B] = "Keychron LM7", [0xD06C] = "Keychron LM7 Ultra", [0xD06D] = "Keychron G4", [0xD06E] = "Keychron G3",
        [0xD06F] = "Keychron G5", [0xD070] = "Keychron BM27", [0xD073] = "Keychron M5 SE", [0xD074] = "Keychron G3 HE",
        [0xD075] = "Keychron M8", [0xD076] = "Keychron M3 V2", [0xD079] = "Keychron BM28", [0xD080] = "Keychron G4 HE",
        [0xD082] = "Keychron BM28 8K", [0xD083] = "Keychron G3 HE", [0xD084] = "Keychron T1 HE", [0xD086] = "Keychron G6 HE 8K",
        [0xD087] = "Keychron G5 HE", [0xD08A] = "Keychron M6 HE 8K", [0xD08C] = "Keychron G10 HE", [0xD091] = "Keychron M8 HE",
        [0xD092] = "Keychron G9 HE 8K", [0xD09D] = "Keychron G6 HE 8K", [0xD0A2] = "Keychron G6 HE 8K", [0xD0A3] = "Keychron G3 HE",
        [0xD085] = "JamesDonkey RSR7 Ultra+",
        [0xD024] = "CANDYSIGN Link (receptor)", [0xD026] = "Keychron Link-KM (receptor)", [0xD027] = "Keychron (receptor)",
        [0xD028] = "Keychron Ultra-Link 8K (receptor)", [0xD029] = "Keychron Link-KM Type C (receptor)",
        [0xD030] = "Keychron Link Type A (receptor)", [0xD031] = "Keychron Link Type C (receptor)", [0xD05A] = "Keychron TurboLink 8K (receptor)",
    };

    public string Name => "Chinesas";

    public bool Handles(ushort vid, ushort pid) => vid switch
    {
        VidAjazzUsb or VidAjazzDongle => Ajazz.ContainsKey(pid) || pid == PidKsnake,
        VidRapoo => RapooVt9.ContainsKey(pid),
        VidGearHub => pid == PidGearHubDongle,
        VidBytech => pid is PidBytechWireless or 0x1015,
        VidMotospeed => pid == PidMotospeedDongle,
        VidKeychron => Keychron.ContainsKey(pid),
        VidGwolves => GwolvesXvi.ContainsKey(pid) || Lamzu.ContainsKey((vid, pid)),
        _ => Lamzu.ContainsKey((vid, pid)),
    };

    public IEnumerable<DeviceEntry> Discover(IReadOnlyList<HidCollection> collections)
    {
        foreach (var group in collections.Where(c => Handles(c.Vid, c.Pid)).GroupBy(c => (c.Vid, c.Pid)))
        {
            var (vid, pid) = group.Key;
            var cols = group.ToList();
            List<(string Name, bool Wired, BatteryReading Reading, DeviceKind Kind)> found;
            try
            {
                found = Read(vid, pid, cols);
            }
            catch (IndexOutOfRangeException)
            {
                found = new() { ($"Mouse {vid:X4}:{pid:X4}", false, BatteryReading.NoData(L.T("resposta curta demais", "reply too short")), DeviceKind.Mouse) };
            }
            catch (Exception ex)
            {
                // Um mouse com problema nao derruba os outros.
                Log.Error($"{Name} {vid:X4}:{pid:X4}", ex);
                found = new() { ($"Mouse {vid:X4}:{pid:X4}", false, BatteryReading.Error(ex.Message), DeviceKind.Mouse) };
            }

            foreach (var (name, wired, reading, kind) in found)
            {
                yield return new DeviceEntry
                {
                    Key = kind == DeviceKind.Mouse ? $"cn:{vid:X4}:{pid:X4}" : $"cn:{vid:X4}:{pid:X4}:{kind}",
                    DisplayName = name,
                    HardwareId = BatteryTray.Core.Known.Id(vid, pid),
                    ProviderName = Name,
                    Reading = reading,
                    IsConnected = true,
                    Kind = kind,
                    Origin = wired ? "Cabo" : "Dongle",
                };
            }
        }
    }

    private static List<(string, bool, BatteryReading, DeviceKind)> Read(ushort vid, ushort pid, List<HidCollection> cols)
    {
        string product = cols.Select(c => c.Product).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? "";
        (string, bool, BatteryReading, DeviceKind) Mouse(string name, bool wired, BatteryReading r) => (name, wired, r, DeviceKind.Mouse);

        if (vid is VidAjazzUsb or VidAjazzDongle && pid == PidKsnake)
            return new() { Mouse(product.Contains("M2-NEX", StringComparison.OrdinalIgnoreCase) ? "Noir Gear M2-NEX" : "K-Snake X11",
                                 vid == VidAjazzUsb, ReadKsnake(cols)) };
        if (vid is VidAjazzUsb or VidAjazzDongle) return new() { Mouse(Ajazz[pid], vid == VidAjazzUsb, ReadAjazz(cols)) };
        if (vid == VidRapoo) return new() { Mouse(RapooVt9[pid], pid == 0x4405, ReadRapoo(cols)) };
        if (vid == VidGearHub) return ReadGearHub(cols, product);
        if (vid == VidBytech)
            return new() { Mouse(string.IsNullOrWhiteSpace(product) ? "Mouse Bytech" : product, pid != PidBytechWireless, ReadBytech(cols, pid != PidBytechWireless)) };
        if (vid == VidMotospeed) return new() { Mouse("Motospeed X6", false, ReadRealtek8k(cols)) };
        if (vid == VidKeychron) return new() { Mouse(Keychron[pid], false, ReadRealtek8k(cols)) };
        if (vid == VidGwolves && GwolvesXvi.TryGetValue(pid, out var g))
            return new() { Mouse($"G-Wolves {g.Name}", !g.Wireless, ReadGwolvesXvi(cols, g.Wireless)) };

        var l = Lamzu[(vid, pid)];
        return new() { Mouse(l.Name, !l.Wireless, ReadLamzu(cols, l.Target)) };
    }

    /// <summary>A colecao com feature do tamanho certo; vendor primeiro.</summary>
    private static HidCollection? FeatureChannel(List<HidCollection> cols, int featureLen) =>
        cols.Where(c => c.FeatureLen == featureLen).OrderBy(c => c.UsagePage < 0xFF00).FirstOrDefault();

    private static readonly BatteryReading NoChannel = BatteryReading.NoData(L.T("não achei o canal de bateria deste mouse", "couldn’t find this mouse’s battery channel"));
    private static readonly BatteryReading Asleep = BatteryReading.NoData(L.T("o mouse não respondeu; pode estar dormindo, mexa nele e atualize", "the mouse didn’t answer; it may be asleep, move it and refresh"));

    // ---------------- AJAZZ ----------------

    private static BatteryReading ReadAjazz(List<HidCollection> cols)
    {
        var target = cols.FirstOrDefault(c => c.UsagePage == 0xFF01 && c.Usage == 0x0010 && c.OutputLen >= 3 && c.InputLen > 9);
        if (target is null) return NoChannel;
        using var session = HidRequest.Open(new[] { target });
        if (session is null) return BatteryReading.NoData(L.T("o canal de bateria não abriu", "the battery channel didn’t open"));
        var reply = session.Ask(target, new byte[] { 0xF0, 0x30, 0x01 }, r => r.Length > 9 && r[1] == 0x30, AskTimeout);
        return reply is null ? BatteryReading.NoData(L.T("o mouse não respondeu; mexa nele e atualize", "the mouse didn’t answer; move it and refresh")) : ParseAjazz(reply[1..]);
    }

    /// <summary>Resposta sem o report id: [0] = 30, [7] %, [8] carregando.</summary>
    internal static BatteryReading ParseAjazz(byte[] d) =>
        d[7] > 100 ? OutOfRange // regra 6: valor sem sentido nao vira 100%
            : BatteryReading.Percentage(d[7], chargeText: d[8] != 0 ? L.T("carregando", "charging") : null);

    internal static readonly BatteryReading OutOfRange = BatteryReading.NoData(L.T("o aparelho mandou um valor fora da faixa", "the device sent an out-of-range value"));

    // ---------------- K-Snake X11 / Noir M2-NEX ----------------

    internal static readonly byte[] KsnakeRequest = { 0x55, 0x30, 0xA5, 0x0B, 0x2E, 0x01, 0x01 };

    private static BatteryReading ReadKsnake(List<HidCollection> cols)
    {
        var target = cols.FirstOrDefault(c => c.UsagePage == 0xFF01 && c.Usage == 0x0010 && c.OutputLen == 65 && c.InputLen > 10);
        if (target is null) return NoChannel;
        using var session = HidRequest.Open(new[] { target });
        if (session is null) return BatteryReading.NoData(L.T("o canal de bateria não abriu", "the battery channel didn’t open"));
        var wire = new byte[65];
        KsnakeRequest.CopyTo(wire, 1);
        // ponytail: o OpenMouse aceita o primeiro relatorio que chegar; aqui exijo o
        // comando 30 ecoado no byte 1 (palpite, nao conferido em aparelho).
        var reply = session.Ask(target, wire, r => r.Length > 10 && r[2] == 0x30, AskTimeout);
        return reply is null ? Asleep : ParseKsnake(reply[1..]) ?? BatteryReading.NoData(L.T("o mouse não informou a bateria", "the mouse didn’t report its battery"));
    }

    /// <summary>Resposta sem o report id: [8] %, [9] carregando.</summary>
    internal static BatteryReading? ParseKsnake(byte[] d) =>
        d[8] > 100 ? null : BatteryReading.Percentage(d[8], chargeText: d[9] != 0 ? L.T("carregando", "charging") : null);

    // ---------------- Rapoo VT9 ----------------

    private static BatteryReading ReadRapoo(List<HidCollection> cols)
    {
        var target = cols.FirstOrDefault(c => c.UsagePage == 0xFF0B && c.Usage == 0x0104 && c.FeatureLen > 9);
        if (target is null) return NoChannel;
        var report = HidRequest.ReadFeature(target, 0x2A);
        if (report is null) return BatteryReading.NoData(L.T("o mouse não informou a bateria", "the mouse didn’t report its battery"));
        return ParseRapoo(report) ?? BatteryReading.NoData(L.T("o mouse não informou a bateria", "the mouse didn’t report its battery"));
    }

    /// <summary>Feature 0x2A com o id no byte 0: % no byte 9 (8 depois do id).</summary>
    internal static BatteryReading? ParseRapoo(byte[] r)
    {
        int offset = r[0] == 0x2A ? 1 : 0;
        if (r.Length < offset + 9) return null;
        int pct = r[offset + 8];
        return pct <= 100 ? BatteryReading.Percentage(pct) : null;
    }

    // ---------------- Lamzu, WLmouse, G-Wolves xvi-new ----------------

    private static BatteryReading ReadLamzu(List<HidCollection> cols, byte target)
    {
        var col = FeatureChannel(cols, 65);
        if (col is null) return NoChannel;
        var wire = new byte[65];
        LamzuRequest(target).CopyTo(wire, 1);
        // Pendente (A0) ou ocupado (A3): le de novo, sem repetir a pergunta. O WLmouse
        // as vezes devolve o quadro deslocado um byte, dai olhar [1] e [2].
        var reply = HidRequest.Feature(col, wire, TimeSpan.FromMilliseconds(100),
            r => r[1] is 0xA0 or 0xA3 || r[2] is 0xA0 or 0xA3, reads: 8);
        if (reply is null) return Asleep;
        return ParseLamzu(reply[1..]) ?? BatteryReading.NoData(L.T("o mouse não informou a bateria", "the mouse didn’t report its battery"));
    }

    internal static byte[] LamzuRequest(byte target)
    {
        var p = new byte[64];
        p[2] = target;
        p[3] = 0x02;
        p[4] = 0x00;
        p[5] = 0x83;
        return p;
    }

    /// <summary>Pacote sem o report id: [0] A1 ok, [4] pagina 00, [5] comando 83, [6] carregando, [7] %.
    /// Aceita o quadro deslocado um byte (WLmouse).</summary>
    internal static BatteryReading? ParseLamzu(byte[] d)
    {
        foreach (int o in new[] { 0, 1 })
        {
            if (d.Length < o + 8 || d[o] != 0xA1 || d[o + 4] != 0x00 || d[o + 5] != 0x83 || d[o + 7] > 100) continue;
            return BatteryReading.Percentage(d[o + 7], chargeText: d[o + 6] == 0x01 ? L.T("carregando", "charging") : null);
        }
        return null;
    }

    // ---------------- G-Wolves xvi ----------------

    internal static byte[] GwolvesXviRequest(bool wireless)
    {
        var wire = new byte[65]; // [0] report id 0, depois o pacote de 64
        wire[2] = 0x02;
        wire[3] = 0x8F;
        wire[4] = wireless ? (byte)1 : (byte)0;
        return wire;
    }

    private static BatteryReading ReadGwolvesXvi(List<HidCollection> cols, bool wireless)
    {
        var col = FeatureChannel(cols, 65);
        if (col is null) return NoChannel;
        // Ocupado enquanto nao ecoar o 8F com status final (A1 ok, 02 dormindo).
        var reply = HidRequest.Feature(col, GwolvesXviRequest(wireless), TimeSpan.FromMilliseconds(60),
            r => r[3] != 0x8F || r[1] is not (0xA1 or 0x02), reads: 10);
        if (reply is null || reply[1] == 0x02) return Asleep;
        return ParseGwolvesXvi(reply[1..]) ?? BatteryReading.NoData(L.T("o mouse não informou a bateria", "the mouse didn’t report its battery"));
    }

    /// <summary>Pacote sem o report id: [0] A1, [2] 8F, [4] carregando, [5] %.</summary>
    internal static BatteryReading? ParseGwolvesXvi(byte[] d)
    {
        if (d[0] != 0xA1 || d[2] != 0x8F || d[5] > 100) return null;
        return BatteryReading.Percentage(d[5], chargeText: d[4] == 0x01 ? L.T("carregando", "charging") : null);
    }

    // ---------------- GearHub-V5 (receptor 3151:402D) ----------------

    private static List<(string, bool, BatteryReading, DeviceKind)> ReadGearHub(List<HidCollection> cols, string product)
    {
        string mouseName = string.IsNullOrWhiteSpace(product) ? "Mouse GearHub" : product;
        var col = FeatureChannel(cols, 65);
        if (col is null) return new() { (mouseName, false, NoChannel, DeviceKind.Mouse) };
        var wire = new byte[65];
        wire[1] = 0xF7;
        // [0] = 1 quando o receptor tem a resposta pronta: le de novo sem repetir.
        var reply = HidRequest.Feature(col, wire, TimeSpan.FromMilliseconds(30), r => r[1] != 0x01, reads: 4);
        if (reply is null) return new() { (mouseName, false, BatteryReading.NoData(L.T("o receptor não respondeu", "the receiver didn’t answer")), DeviceKind.Mouse) };
        var (mouse, keyboard) = ParseGearHub(reply[1..]);
        var list = new List<(string, bool, BatteryReading, DeviceKind)>();
        if (mouse is not null) list.Add((mouseName, false, mouse, DeviceKind.Mouse));
        if (keyboard is not null) list.Add(("Teclado (receptor GearHub)", false, keyboard, DeviceKind.Keyboard));
        if (list.Count == 0) list.Add((mouseName, false, BatteryReading.Disconnected(L.T("o receptor diz que o mouse está desligado", "the receiver says the mouse is off")), DeviceKind.Mouse));
        return list;
    }

    /// <summary>Resposta sem o id: [1] % teclado, [2] % mouse, [3]/[4] = 0 quando ligado.</summary>
    internal static (BatteryReading? Mouse, BatteryReading? Keyboard) ParseGearHub(byte[] d)
    {
        BatteryReading? mouse = d[4] == 0 && d[2] <= 100 ? BatteryReading.Percentage(d[2]) : null;
        BatteryReading? keyboard = d[3] == 0 && d[1] <= 100 ? BatteryReading.Percentage(d[1]) : null;
        return (mouse, keyboard);
    }

    // ---------------- Bytech ----------------

    internal static byte[] BytechRequest()
    {
        var wire = new byte[64];
        wire[0] = 0x03; // report id
        wire[2] = 0x50; wire[3] = 0x00; wire[4] = 0x02; wire[5] = 0x4F; wire[6] = 0x81;
        int sum = 0;
        for (int i = 2; i < wire.Length; i++) sum += wire[i];
        wire[1] = (byte)sum;
        return wire;
    }

    private static BatteryReading ReadBytech(List<HidCollection> cols, bool wired)
    {
        var col = cols.Where(c => c.FeatureLen == 64).OrderBy(c => (c.UsagePage, c.Usage) != (0xFF00, 0x0001)).FirstOrDefault();
        if (col is null) return NoChannel;
        // A resposta certa ecoa 02 no byte 3 do pacote; outra coisa: le de novo.
        var reply = HidRequest.Feature(col, BytechRequest(), TimeSpan.FromMilliseconds(40), r => r[4] != 0x02, reads: 5);
        if (reply is null) return Asleep;
        return ParseBytech(reply[1..], wired) ?? BatteryReading.NoData(L.T("o mouse não informou a bateria", "the mouse didn’t report its battery"));
    }

    /// <summary>Pacote sem o id: [5] % (E4 = cheio).</summary>
    internal static BatteryReading? ParseBytech(byte[] d, bool wired)
    {
        int? pct = d[5] <= 100 ? d[5] : d[5] == 0xE4 ? 100 : null;
        return pct is null ? null : BatteryReading.Percentage(pct.Value, chargeText: wired ? L.T("carregando", "charging") : null);
    }

    // ---------------- Realtek 8K: Motospeed X6, Keychron ----------------

    private static BatteryReading ReadRealtek8k(List<HidCollection> cols)
    {
        var target = cols.FirstOrDefault(c => c.UsagePage == 0xFFC1 && c.Usage == 0x0001 && c.OutputLen >= 61 && c.InputLen > 20);
        if (target is null) return NoChannel;
        using var session = HidRequest.Open(new[] { target });
        if (session is null) return BatteryReading.NoData(L.T("o canal de bateria não abriu", "the battery channel didn’t open"));
        var wire = new byte[target.OutputLen];
        wire[0] = 0xB3;
        wire[1] = 0x06;
        var reply = session.Ask(target, wire, r => r.Length > 20 && r[0] == 0xB4 && r[1] == 0x06, AskTimeout);
        return reply is null ? Asleep : ParseRealtek8k(reply[1..]) ?? BatteryReading.NoData(L.T("o mouse não informou a bateria", "the mouse didn’t report its battery"));
    }

    /// <summary>Status sem o id (B4): [0] = 06, [19] % com o bit 7 = carregando.</summary>
    internal static BatteryReading? ParseRealtek8k(byte[] d)
    {
        int pct = d[19] & 0x7F;
        if (d[0] != 0x06 || pct > 100) return null;
        return BatteryReading.Percentage(pct, chargeText: (d[19] & 0x80) != 0 ? L.T("carregando", "charging") : null);
    }
}
