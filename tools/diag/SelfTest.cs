using BatteryTray.Core;
using BatteryTray.Interop;
using BatteryTray.Providers;

namespace BatteryTray.Diag;

/// <summary>
/// Testes das partes puras: parsing dos relatorios e traducao do XInput.
/// Existem porque essas sao exatamente as regras que impedem o app de mostrar
/// um valor errado, e nao dependem de o hardware estar plugado.
/// </summary>
internal static class SelfTest
{
    private static int _passed;
    private static int _failed;

    /// <summary>Os aparelhos de mentira da porta tem VID 0000: com licenca, para os testes do teto nao esbarrarem na camada de seguranca.</summary>
    private static readonly string[] SelfTestVid = { "0000" };

    public static int Run()
    {
        Console.WriteLine("=== self-test ===");
        Log.Silent = true; // aparelhos de mentira abaixo nao vao para o joule.log de verdade

        // --- Cor da alca do widget: valor editado a mao no settings.json ---
        Check("alca: opcoes conhecidas e #RRGGBB passam", Settings.HandleColor("neutral") == "neutral" && Settings.HandleColor("#2dd4bf") == "#2dd4bf");
        Check("alca: lixo volta para a cor do app", Settings.HandleColor("#12345G") == "joule" && Settings.HandleColor("vidro") == "joule" && Settings.HandleColor(null) == "joule");

        // --- Attack Shark: 03 10 40 <flag> <n>, bateria = n * 10 ---
        Eq("shark 70%", 70, AttackSharkProvider.ParsePercent(B(0x03, 0x10, 0x40, 0x00, 0x07)));
        Eq("shark 100%", 100, AttackSharkProvider.ParsePercent(B(0x03, 0x10, 0x40, 0x01, 0x0A)));
        Eq("shark 0%", 0, AttackSharkProvider.ParsePercent(B(0x03, 0x10, 0x40, 0x00, 0x00)));
        Null("shark n>10 rejeitado", AttackSharkProvider.ParsePercent(B(0x03, 0x10, 0x40, 0x00, 0x0B)));
        Null("shark byte0 errado", AttackSharkProvider.ParsePercent(B(0x02, 0x10, 0x40, 0x00, 0x05)));
        Null("shark byte1 errado", AttackSharkProvider.ParsePercent(B(0x03, 0x11, 0x40, 0x00, 0x05)));
        Null("shark byte2 errado", AttackSharkProvider.ParsePercent(B(0x03, 0x10, 0x41, 0x00, 0x05)));
        Null("shark curto demais", AttackSharkProvider.ParsePercent(B(0x03, 0x10, 0x40)));
        Null("shark relatorio de mouse", AttackSharkProvider.ParsePercent(B(0x01, 0x00, 0xFF, 0x00, 0x02)));
        Eq("shark X11 em %", 64, AttackSharkProvider.ParsePercent(B(0x03, 0x55, 0x40, 0x01, 64)));
        Null("shark X11 sem flag 01 nao vale", AttackSharkProvider.ParsePercent(B(0x03, 0x55, 0x40, 0x00, 64)));
        Eq("shark X6 em dezenas", 80, AttackSharkProvider.ParsePercent(B(0x03, 0x85, 0x40, 0x01, 8)));
        Null("shark X3 sem escala conhecida", AttackSharkProvider.ParsePercent(B(0x03, 0x4D, 0x40, 0x01, 8)));

        // --- AJAZZ, Rapoo VT9, Lamzu ---
        var ajz = new byte[63]; ajz[0] = 0x30; ajz[7] = 58; ajz[8] = 1;
        Eq("ajazz percentual em [7]", 58, ChineseBrandsProvider.ParseAjazz(ajz).Percent);
        Check("ajazz carregando em [8]", ChineseBrandsProvider.ParseAjazz(ajz).ChargeText == "carregando");
        ajz[7] = 0xFF;
        Check("ajazz valor fora da faixa vira sem dado, nao 100%", ChineseBrandsProvider.ParseAjazz(ajz) is { Status: ReadingStatus.NoData, Percent: null });
        var rp = new byte[12]; rp[0] = 0x2A; rp[9] = 81;
        Eq("rapoo vt9 % depois do id", 81, ChineseBrandsProvider.ParseRapoo(rp)?.Percent);
        var lz = ChineseBrandsProvider.LamzuRequest(0x02);
        Check("lamzu pergunta 00 00 02 02 00 83", lz.Length == 64 && lz[2] == 0x02 && lz[3] == 0x02 && lz[5] == 0x83);
        var lzr = new byte[64]; lzr[0] = 0xA1; lzr[5] = 0x83; lzr[6] = 1; lzr[7] = 45;
        Eq("lamzu percentual em [7]", 45, ChineseBrandsProvider.ParseLamzu(lzr)?.Percent);
        lzr[0] = 0xA0;
        Check("lamzu pendente nao vale", ChineseBrandsProvider.ParseLamzu(lzr) is null);
        Check("chinesas handles", new ChineseBrandsProvider().Handles(0xA8A5, 0x2157) && new ChineseBrandsProvider().Handles(0x373E, 0x0047) && new ChineseBrandsProvider().Handles(0x24AE, 0x1205) && !new ChineseBrandsProvider().Handles(0x24AE, 0x1871));
        var lzShift = new byte[64]; Array.Copy(new byte[] { 0xA1, 0, 0, 0, 0, 0x83, 0, 61 }, 0, lzShift, 1, 8);
        Eq("wlmouse quadro deslocado um byte", 61, ChineseBrandsProvider.ParseLamzu(lzShift)?.Percent);

        // --- lote 3: GearHub, Incott, Bytech, K-Snake, G-Wolves xvi, Realtek 8K ---
        var gh = ChineseBrandsProvider.ParseGearHub(B(0x01, 0x00, 0x2D, 0x01, 0x00, 0x01)); // captura do OpenMouse
        Check("gearhub mouse 45%, teclado desligado", gh.Mouse?.Percent == 45 && gh.Keyboard is null);
        var gh2 = ChineseBrandsProvider.ParseGearHub(B(0x01, 0x50, 0x2D, 0x00, 0x00, 0x01));
        Check("gearhub teclado ligado tambem vira linha", gh2.Keyboard?.Percent == 80);
        Check("incott 09 5F = 95%", IncottProvider.Parse(B(0x09, 0x5F, 0x10, 0x04, 0x00, 0x0F, 0x0F, 0x10)) == (95, false));
        Check("incott 09 E1 = carregando 97%", IncottProvider.Parse(B(0x09, 0xE1, 0x10, 0x04, 0x00, 0x0F, 0x0F, 0x10)) == (97, true));
        Check("incott outro relatorio", IncottProvider.Parse(B(0x01, 0x5F, 0x10)) is null);
        var bt = ChineseBrandsProvider.BytechRequest();
        Check("bytech pergunta 03 [soma] 50 00 02 4F 81", bt.Length == 64 && bt[0] == 0x03 && bt[1] == 0x22 && bt[4] == 0x02 && bt[6] == 0x81);
        var btr = new byte[63]; btr[3] = 0x02; btr[5] = 0xE4;
        Eq("bytech E4 = cheio", 100, ChineseBrandsProvider.ParseBytech(btr, wired: false)?.Percent);
        btr[5] = 0xC8;
        Check("bytech valor estranho nao vale", ChineseBrandsProvider.ParseBytech(btr, wired: false) is null);
        var ks = new byte[64]; ks[0] = 0x55; ks[1] = 0x30; ks[8] = 66; ks[9] = 1;
        Check("ksnake [8] % e [9] carregando", ChineseBrandsProvider.ParseKsnake(ks) is { Percent: 66, ChargeText: "carregando" });
        var gx = ChineseBrandsProvider.GwolvesXviRequest(wireless: true);
        Check("g-wolves xvi pergunta 00 02 8F 01", gx.Length == 65 && gx[2] == 0x02 && gx[3] == 0x8F && gx[4] == 0x01);
        var gxr = new byte[64]; gxr[0] = 0xA1; gxr[2] = 0x8F; gxr[4] = 1; gxr[5] = 73;
        Check("g-wolves xvi [5] % e [4] carregando", ChineseBrandsProvider.ParseGwolvesXvi(gxr) is { Percent: 73, ChargeText: "carregando" });
        var rt = new byte[63]; rt[0] = 0x06; rt[19] = 0x80 | 55;
        Check("realtek 8k [19] com bit 7 = carregando", ChineseBrandsProvider.ParseRealtek8k(rt) is { Percent: 55, ChargeText: "carregando" });
        rt[0] = 0x05;
        Check("realtek 8k outro comando nao vale", ChineseBrandsProvider.ParseRealtek8k(rt) is null);
        var cn = new ChineseBrandsProvider(); var cx = new CompxProvider();
        Check("lote 3 handles", cn.Handles(0x3151, 0x402D) && cn.Handles(0x372E, 0x1014) && cn.Handles(0xA8A5, 0x2255) && cn.Handles(0x0BDA, 0xFFE0)
                                && cn.Handles(0x3434, 0xD060) && cn.Handles(0x36A7, 0xA883) && !cn.Handles(0x36A7, 0xA887) && !cn.Handles(0x0BDA, 0xFFF1)
                                && cx.Handles(0x33E4, 0x3854) && cx.Handles(0x3770, 0x0200));
        bool gwolvesOverlap = Enumerable.Range(0, 0x10000).Any(p => cn.Handles(0x33E4, (ushort)p) && cx.Handles(0x33E4, (ushort)p));
        Check("g-wolves: nenhum PID em dois providers", !gwolvesOverlap && cn.Handles(0x33E4, 0x3517) && cn.Handles(0x33E4, 0x3817));

        // --- ATK/VXE: relatorio 0x08, checksum 0x55 - soma ---
        var atk = CompxProvider.BuildPayload(0x04);
        Check("atk pergunta de bateria", atk.Length == 16 && atk[0] == 0x04 && atk[15] == ((0x55 - 0x08 - 0x04) & 0xFF));
        var atkReply = new byte[16]; atkReply[0] = 0x04; atkReply[4] = 4; atkReply[5] = 77; atkReply[6] = 1; atkReply[15] = CompxProvider.Checksum(atkReply);
        Check("atk resposta valida", CompxProvider.IsBatteryFrame(atkReply));
        Eq("atk percentual", 77, CompxProvider.ParseBattery(atkReply).Percent);
        Check("atk carregando", CompxProvider.ParseBattery(atkReply).ChargeText == "carregando");
        var atkBad = (byte[])atkReply.Clone(); atkBad[5] = 78;
        Check("atk checksum errado rejeitado", !CompxProvider.IsBatteryFrame(atkBad));
        var atkHigh = (byte[])atkReply.Clone(); atkHigh[5] = 0xC8; atkHigh[15] = CompxProvider.Checksum(atkHigh);
        Check("atk valor fora da faixa vira sem dado, nao 100%", CompxProvider.ParseBattery(atkHigh) is { Status: ReadingStatus.NoData, Percent: null });
        Check("compx handles", new CompxProvider().Handles(0x373B, 0x1085) && new CompxProvider().Handles(0x3554, 0xFB56) && new CompxProvider().Handles(0x3710, 0x1234) && !new CompxProvider().Handles(0x3710, 0x5405) && new CompxProvider().Handles(0x260D, 0x1114));

        // Confirmacao: o pico de um relatorio so (80, 90, 80 -- visto no log de 2026-10-22) nao troca o valor.
        var conf = new PassiveReportListener("t", _ => false, r => r[0], confirmReports: 2);
        conf.OnReport(B(80));
        Check("confirmar: primeiro valor entra direto", conf.Last?.Percent == 80);
        conf.OnReport(B(90));
        Check("confirmar: pico de um relatorio nao troca", conf.Last?.Percent == 80);
        conf.OnReport(B(80)); conf.OnReport(B(90));
        Check("confirmar: pico desfeito zera a contagem", conf.Last?.Percent == 80);
        conf.OnReport(B(90));
        Check("confirmar: dois seguidos trocam", conf.Last?.Percent == 90);
        var direct = new PassiveReportListener("t", _ => false, r => r[0]);
        direct.OnReport(B(80)); direct.OnReport(B(90));
        Check("sem confirmar troca na hora (Rapoo)", direct.Last?.Percent == 90);

        // --- Rapoo: BB B0 01 40 06 01 <bat>, percentual direto ---
        Eq("rapoo 75%", 75, RapooProvider.ParsePercent(B(0xBB, 0xB0, 0x01, 0x40, 0x06, 0x01, 0x4B)));
        Eq("rapoo 100%", 100, RapooProvider.ParsePercent(B(0xBB, 0xB0, 0x01, 0x40, 0x06, 0x01, 0x64)));
        Eq("rapoo 1%", 1, RapooProvider.ParsePercent(B(0xBB, 0xB0, 0x01, 0x40, 0x06, 0x01, 0x01)));
        Null("rapoo >100 rejeitado", RapooProvider.ParsePercent(B(0xBB, 0xB0, 0x01, 0x40, 0x06, 0x01, 0x65)));
        Null("rapoo byte0 errado", RapooProvider.ParsePercent(B(0xBA, 0xB0, 0x01, 0x40, 0x06, 0x01, 0x32)));
        Null("rapoo byte1 errado", RapooProvider.ParsePercent(B(0xBB, 0xB1, 0x01, 0x40, 0x06, 0x01, 0x32)));
        Null("rapoo byte3 errado", RapooProvider.ParsePercent(B(0xBB, 0xB0, 0x01, 0x41, 0x06, 0x01, 0x32)));
        Null("rapoo curto demais", RapooProvider.ParsePercent(B(0xBB, 0xB0, 0x01, 0x40, 0x06, 0x01)));

        // --- DualSense: relatorio 0x01 de 64 bytes (so o cabo), byte 53 = nivel + estado ---
        // O caso do controle real medido aqui: 0x29 = estado 2 (cheio), nivel 9.
        Eq("dualsense cheio no cabo", 100, DualSenseProvider.ParseStatus(Ds(0x29))?.Percent);
        Check("dualsense cheio marca estado", DualSenseProvider.ParseStatus(Ds(0x29))?.State == DualSenseProvider.ChargeState.Full);
        Eq("dualsense carregando 95%", 95, DualSenseProvider.ParseStatus(Ds(0x19))?.Percent);
        Check("dualsense carregando marca estado", DualSenseProvider.ParseStatus(Ds(0x19))?.State == DualSenseProvider.ChargeState.Charging);
        Eq("dualsense descarregando 95%", 95, DualSenseProvider.ParseStatus(Ds(0x09))?.Percent);
        Eq("dualsense nivel 0 vira 5%", 5, DualSenseProvider.ParseStatus(Ds(0x00))?.Percent);
        Eq("dualsense nivel 10 nao passa de 100", 100, DualSenseProvider.ParseStatus(Ds(0x0A))?.Percent);
        Null("dualsense nivel 11 rejeitado", DualSenseProvider.ParseStatus(Ds(0x0B))?.Percent);
        Null("dualsense estado anormal rejeitado", DualSenseProvider.ParseStatus(Ds(0xA5))?.Percent);
        Null("dualsense report id errado", DualSenseProvider.ParseStatus(WithId(Ds(0x29), 0x31))?.Percent);
        Null("dualsense curto demais", DualSenseProvider.ParseStatus(B(0x01, 0x00))?.Percent);
        // A checagem que impede o pior erro: por Bluetooth vem 78 bytes com o
        // byte 53 zerado -- sem o teste de comprimento isso viraria "5%".
        Null("dualsense relatorio de bluetooth rejeitado", DualSenseProvider.ParseStatus(BluetoothReport())?.Percent);

        // --- XInput: WIRED tem de ser descartado (8BitDo reporta WIRED/FULL falso) ---
        var wired = XInputProvider.MapBattery(XInputNative.BATTERY_TYPE_WIRED, 3, 0x2DC8, 0x310A);
        Check("xinput WIRED nao vira leitura", wired.Status == ReadingStatus.NoData);
        Check("xinput WIRED sem percentual", wired.Percent is null && wired.Level is null);

        // Cabo provado pelo dongle ocioso (2DC8:301C presente): ai WIRED e' verdade.
        var wiredCable = XInputProvider.MapBattery(XInputNative.BATTERY_TYPE_WIRED, 3, 0x2DC8, 0x310A, idleDongle: true);
        Check("xinput 8BitDo WIRED + dongle ocioso = no cabo", wiredCable.Status == ReadingStatus.Charging);
        var wiredOther = XInputProvider.MapBattery(XInputNative.BATTERY_TYPE_WIRED, 3, 0x1234, 0x5678, idleDongle: true);
        Check("xinput WIRED de outro VID:PID segue descartado", wiredOther.Status == ReadingStatus.NoData);

        // Xbox no cabo: 045E:02FF, DISCONNECTED logo ao plugar e WIRED depois (medido 2026-10-11).
        var xboxPlug = XInputProvider.MapBattery(XInputNative.BATTERY_TYPE_DISCONNECTED, 0, 0x045E, 0x02FF);
        Check("xinput Xbox recem-plugado = no cabo", xboxPlug.Status == ReadingStatus.Charging && xboxPlug.Level is null);
        var xboxCable = XInputProvider.MapBattery(XInputNative.BATTERY_TYPE_WIRED, 3, 0x045E, 0x02FF);
        Check("xinput Xbox WIRED = no cabo", xboxCable.Status == ReadingStatus.Charging && xboxCable.Level is null);
        // Dongle generico se passa por Xbox 360 com fio: VID Microsoft nao basta.
        var fake360 = XInputProvider.MapBattery(XInputNative.BATTERY_TYPE_WIRED, 3, 0x045E, 0x028E);
        Check("xinput WIRED de 045E:028E segue descartado", fake360.Status == ReadingStatus.NoData);

        var alkalineFull = XInputProvider.MapBattery(XInputNative.BATTERY_TYPE_ALKALINE, 3, 0x045E, 0x0B12);
        Check("xinput alcalina FULL", alkalineFull.Status == ReadingStatus.Ok && alkalineFull.Level == "FULL");

        var nimhLow = XInputProvider.MapBattery(XInputNative.BATTERY_TYPE_NIMH, 1, 0x045E, 0x0B12);
        Check("xinput NiMH LOW", nimhLow.Status == ReadingStatus.Ok && nimhLow.Level == "LOW");

        var disconnected = XInputProvider.MapBattery(XInputNative.BATTERY_TYPE_DISCONNECTED, 0, 0, 0);
        Check("xinput DISCONNECTED sem valor", disconnected.Status == ReadingStatus.NoData && disconnected.Level is null);

        var unknown = XInputProvider.MapBattery(0xFF, 0, 0x2DC8, 0x310A);
        Check("xinput UNKNOWN sem valor", unknown.Status == ReadingStatus.NoData && unknown.Level is null);

        var badLevel = XInputProvider.MapBattery(XInputNative.BATTERY_TYPE_ALKALINE, 9, 0, 0);
        Check("xinput nivel invalido rejeitado", badLevel.Status == ReadingStatus.NoData);

        // --- Bluetooth generico: casar devnode filho com devnode raiz ---
        // O caso real do Moondrop: a bateria mora no filho do servico
        // Hands-Free AG, que se declara desconectado mesmo com o fone tocando;
        // quem sabe da conexao e' a raiz BTHENUM\DEV_<mac>. Sem casar os dois
        // o app diria "desconectado" para um aparelho ligado.
        var stampBt = new DateTime(2026, 9, 7, 16, 34, 43, DateTimeKind.Utc);
        var classico = BluetoothBatteryNodes.Merge(new[]
        {
            Bt("BTHENUM", "0A1B2C3D4E5F", "Moondrop Space Travel", null, null, connected: true, root: true),
            Bt("BTHENUM", "0A1B2C3D4E5F", "Moondrop Space Travel Hands-Free AG", 100, stampBt, connected: false, root: false),
        });
        Check("bt classico vira um dispositivo so", classico.Count == 1);
        Check("bt classico usa a conexao da raiz", classico[0].IsConnected);
        Eq("bt classico le a bateria do filho", 100, classico[0].Percent);
        Check("bt classico usa o nome da raiz", classico[0].Name == "Moondrop Space Travel");
        Check("bt classico guarda o carimbo do Windows", classico[0].UpdatedUtc == stampBt);

        var ble = BluetoothBatteryNodes.Merge(new[]
        {
            Bt("BTHLE", "a0b1c2d3e4f5", "8BitDo Ultimate 2C Wireless", 99, stampBt, connected: true, root: true),
        });
        Check("bt LE le do proprio devnode", ble.Count == 1 && ble[0].Percent == 99);
        Check("bt LE marca low energy", ble[0].IsLowEnergy);

        // Enderecos vem em caixas diferentes conforme o transporte.
        Check("endereco normalizado por caixa", BluetoothBatteryNodes.NormalizeAddress("0A1B2C3D4E5F") == "0a1b2c3d4e5f");
        Check("endereco sem separadores", BluetoothBatteryNodes.NormalizeAddress("0a:1b:2c:3d:4e:5f") == "0a1b2c3d4e5f");
        Check("nome sem sufixo de perfil", BluetoothBatteryNodes.CleanName("MCHOSE V9 PRO Hands-Free AG") == "MCHOSE V9 PRO");

        var doisValores = BluetoothBatteryNodes.Merge(new[]
        {
            Bt("BTHENUM", "aabbccddeeff", "Fone", 40, stampBt, connected: false, root: false),
            Bt("BTHENUM", "aabbccddeeff", "Fone", 55, stampBt.AddMinutes(5), connected: false, root: false),
        });
        Eq("bt fica com o carimbo mais novo", 55, doisValores[0].Percent);

        Check("bt sem bateria nao entra na lista", BluetoothBatteryNodes.Merge(new[]
        {
            Bt("BTHENUM", "aabbccddeeff", "Teclado", null, null, connected: true, root: true),
        }).Count == 0);

        // O valor congela quando o aparelho desliga: a leitura tem de sair com
        // o carimbo do Windows, nunca com a hora do Refresh.
        var congelado = BluetoothPnpProvider.ToReading(new BluetoothBatteryDevice
        {
            Address = "a0b1c2d3e4f5", Name = "8BitDo", Percent = 99,
            UpdatedUtc = stampBt, IsConnected = false,
        });
        Check("bt desligado mantem o percentual", congelado.Percent == 99);
        Check("bt desligado usa o carimbo antigo", congelado.ObservedUtc == stampBt);
        Check("bt desligado se explica", congelado.Detail is { Length: > 0 });

        var vivo = BluetoothPnpProvider.ToReading(new BluetoothBatteryDevice
        {
            Address = "a0b1c2d3e4f5", Name = "8BitDo", Percent = 99,
            UpdatedUtc = stampBt, IsConnected = true,
        });
        Check("bt conectado nao inventa observacao", vivo.Detail is null && vivo.ObservedUtc == stampBt);
        Check("endereco formatado para diagnostico", BluetoothPnpProvider.FormatAddress("a0b1c2d3e4f5") == "A0:B1:C2:D3:E4:F5");

        // --- guarda das colecoes reservadas pelo Windows ---
        Check("mouse de sistema e' reservado", HidInventory.IsSystemReserved(0x0001, 0x0002));
        Check("teclado de sistema e' reservado", HidInventory.IsSystemReserved(0x0001, 0x0006));
        Check("colecao 0x000A liberada", !HidInventory.IsSystemReserved(0x000A, 0x0000));
        Check("colecao 0xFF00 liberada", !HidInventory.IsSystemReserved(0xFF00, 0x0002));
        Check("joystick nao e' reservado", !HidInventory.IsSystemReserved(0x0001, 0x0004));

        // --- registry: cada VID:PID vai para o provider certo ---
        var registry = ProviderRegistry.CreateDefault();
        Check("1D57:FA60 -> AttackShark", registry.Resolve(0x1D57, 0xFA60)?.Name == "AttackShark");
        Check("1D57:FA61 -> AttackShark", registry.Resolve(0x1D57, 0xFA61)?.Name == "AttackShark");
        Check("24AE:1871 -> Rapoo", registry.Resolve(0x24AE, 0x1871)?.Name == "Rapoo");
        Check("054C:0CE6 -> DualSense", registry.Resolve(0x054C, 0x0CE6)?.Name == "DualSense");
        Check("desconhecido nao resolve", registry.Resolve(0x0951, 0x16B7) is null);
        Check("headset sem provider", registry.Resolve(0x291D, 0x385D) is null);

        // --- cache: leitura sem valor nao sobrescreve o ultimo valor bom ---
        var cache = new ReadingCache();
        var observed = DateTime.UtcNow.AddMinutes(-30);
        cache.Update("k", "Mouse", "1D57:FA60", BatteryReading.Percentage(60, observedUtc: observed));
        cache.Update("k", "Mouse", "1D57:FA60", BatteryReading.NoData("sem relatorio"));
        Check("NoData nao apaga o cache", cache.Get("k")?.Percent == 60);
        Check("cache guarda o instante do relatorio", cache.Get("k") is { } c && Math.Abs((c.TimestampUtc - observed).TotalSeconds) < 2);
        Check("idade do cache em minutos", ReadingCache.DescribeAge(TimeSpan.FromMinutes(30)) == "ha 30 min");
        Check("idade recente vira agora", ReadingCache.DescribeAge(TimeSpan.FromSeconds(3)) == "agora");

        // --- entrada da UI: valor do cache so aparece quando a leitura veio vazia ---
        var stale = new DeviceEntry
        {
            Key = "k",
            DisplayName = "Mouse",
            Reading = BatteryReading.NoData("mouse parado"),
            Cache = cache.Get("k"),
        };
        Check("NoData mostra valor do cache", stale.EffectivePercent == 60);

        var unsupported = new DeviceEntry
        {
            Key = "u",
            DisplayName = "Teclado",
            Reading = BatteryReading.Unsupported,
            Cache = cache.Get("k"),
        };
        Check("sem suporte nunca mostra valor", unsupported.EffectivePercent is null && unsupported.SortPercent is null);

        // --- lista principal do painel: plugado agora E com bateria ---
        // A regra existe para o painel mostrar so o que interessa; tudo que
        // falha aqui continua visivel, mas atras do link "mostrar outros".
        Check("entrada sem IsConnected fica fora por padrao", !stale.ShowsBattery);

        var live = new DeviceEntry
        {
            Key = "live", DisplayName = "Mouse", IsConnected = true,
            Reading = BatteryReading.Percentage(80),
        };
        Check("plugado com leitura entra na lista", live.ShowsBattery);

        var staleConnected = new DeviceEntry
        {
            Key = "k", DisplayName = "Mouse", IsConnected = true,
            Reading = BatteryReading.NoData("mouse parado"),
            Cache = cache.Get("k"),
        };
        Check("plugado com valor de cache entra na lista", staleConnected.ShowsBattery);

        var charging = new DeviceEntry
        {
            Key = "c", DisplayName = "Mouse", IsConnected = true,
            Reading = BatteryReading.Charging("no cabo"),
        };
        Check("carregando entra na lista", charging.ShowsBattery);

        var connectedNoValue = new DeviceEntry
        {
            Key = "n", DisplayName = "Mouse", IsConnected = true,
            Reading = BatteryReading.NoData("sem relatorio ainda"),
        };
        Check("plugado sem valor nenhum fica fora", !connectedNoValue.ShowsBattery);

        var ghost = new DeviceEntry
        {
            Key = "g", DisplayName = "Mouse sumido", IsConnected = false,
            Reading = BatteryReading.Disconnected("nao esta conectado agora"),
            Cache = cache.Get("k"),
        };
        Check("desconectado com cache fica fora", !ghost.ShowsBattery);
        Check("desconectado nao entra nem em outros", !ghost.ShowsAsOther);
        Check("desconectado mantem o valor no cache", ghost.EffectivePercent == 60);

        var unsupportedConnected = new DeviceEntry
        {
            Key = "u2", DisplayName = "Teclado", IsConnected = true,
            Reading = BatteryReading.Unsupported,
        };
        Check("sem suporte fica fora da lista principal", !unsupportedConnected.ShowsBattery);
        Check("sem suporte plugado aparece em outros", unsupportedConnected.ShowsAsOther);
        Check("plugado sem valor aparece em outros", connectedNoValue.ShowsAsOther);
        Check("plugado com valor nao repete em outros", !live.ShowsAsOther);

        // Motivo de "sem caminho" fala do transporte, nao do aparelho: os dois
        // resolvem-se por Bluetooth.
        Check("beco do 8BitDo cita o dongle", Known.DeadEndReason(0x2DC8, 0x310A)?.Contains("dongle") == true);
        Check("beco do 8BitDo cita o Bluetooth", Known.DeadEndReason(0x2DC8, 0x310A)?.Contains("Bluetooth") == true);
        Check("beco do V9 PRO cita o Bluetooth", Known.DeadEndReason(0x291D, 0x385D)?.Contains("Bluetooth") == true);
        Check("beco do teclado Dell registrado", Known.DeadEndReason(0x04CA, 0x00BD)?.Contains("comando") == true);

        // --- Tipo do aparelho (icone da linha) ---
        Check("bt classico de audio e' fone", Known.BluetoothKind(0x240404, "Moondrop Space Travel") == DeviceKind.Headset);
        Check("bt classico do DualSense e' controle", Known.BluetoothKind(0x2508, "DualSense Wireless Controller") == DeviceKind.Gamepad);
        Check("ble sem classe cai no nome", Known.BluetoothKind(null, "Rapoo BT Mouse") == DeviceKind.Mouse);
        Check("ble R1mouse1 e' mouse", Known.BluetoothKind(null, "R1mouse1") == DeviceKind.Mouse);
        Check("ble 8BitDo e' controle", Known.BluetoothKind(null, "8BitDo Ultimate 2C Wireless") == DeviceKind.Gamepad);
        Check("nome desconhecido e' generico", Known.BluetoothKind(null, "Desview") == DeviceKind.Generic);
        Check("mouse com teclado de macro e' mouse", Known.KindFromUsages(new (ushort, ushort)[] { (0x01, 0x06), (0x01, 0x02) }) == DeviceKind.Mouse);
        Check("telefonia e' fone", Known.KindFromUsages(new (ushort, ushort)[] { (0x0C, 0x01), (0x0B, 0x01) }) == DeviceKind.Headset);

        // --- Linhas do painel (design v1) ---
        var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
        DeviceEntry Dev(string name, BatteryReading r, bool live = false, CachedReading? cache = null) =>
            new() { Key = name, DisplayName = name, Reading = r, IsConnected = true, IsLive = live, Cache = cache };

        var faixa = PanelView.Row(Dev("xbox", BatteryReading.Coarse("FULL")), 20, now);
        Check("faixa nao vira percentual", faixa.ValueKind == ValueKind.Band && faixa.BandCount == 3 && faixa.BandLabel == "Cheia");
        var baixo = PanelView.Row(Dev("shark", BatteryReading.Percentage(15), live: true), 20, now);
        Check("abaixo do limite e' alerta", baixo.IsLow && baixo.Note == "abaixo de 20%");
        Check("ao vivo nao mostra idade", baixo.Age is null && !baixo.IsStale);
        Check("no limite exato nao e' alerta", !PanelView.Row(Dev("x", BatteryReading.Percentage(20)), 20, now).IsLow);
        var velho = PanelView.Row(Dev("rapoo", BatteryReading.Percentage(94, "software da Rapoo fechado", observedUtc: now.AddHours(-2))), 20, now);
        Check("sob demanda velho e' antigo", velho.IsStale && velho.Age == "há 2 h" && velho.Note == "software da Rapoo fechado");
        var recente = PanelView.Row(Dev("ds", BatteryReading.Percentage(100, observedUtc: now.AddMinutes(-3), chargeText: "carga completa")), 20, now);
        Check("sob demanda recente nao e' antigo", !recente.IsStale && recente.Age == "há 3 min" && recente.Note is null);
        Check("carga completa chega na linha", recente.ChargeText == "carga completa");
        var guardado = new CachedReading { Percent = 70, TimestampUtc = now.AddMinutes(-1) };
        var doCache = PanelView.Row(Dev("c", BatteryReading.NoData("mouse parado"), cache: guardado), 20, now);
        Check("valor do cache e' antigo mesmo recente", doCache.IsStale && doCache.Percent == 70);
        Check("cabo sem numero e' cabo", PanelView.Row(Dev("8bitdo", BatteryReading.Charging()), 20, now).ValueKind == ValueKind.Cable);

        var ordem = PanelView.MainRows(new[]
        {
            Dev("cabo", BatteryReading.Charging()),
            Dev("faixa", BatteryReading.Coarse("MEDIUM")),
            Dev("cem", BatteryReading.Percentage(100)),
            Dev("oitenta", BatteryReading.Percentage(80)),
        }, 20, now).Select(r => r.Key).ToArray();
        Check("ordem: percentual, faixa, cabo", ordem.SequenceEqual(new[] { "oitenta", "cem", "faixa", "cabo" }));

        Check("motivo sem provider e' frase", PanelView.Reason(Dev("hid", BatteryReading.Unsupported)) == "O app ainda não sabe ler a bateria deste aparelho.");
        Check("motivo sem dado vira frase", PanelView.Reason(Dev("s", BatteryReading.NoData("mouse parado não transmite"))) == "Sem dado agora: mouse parado não transmite.");
        Check("idade abaixo de 45 s e' agora", PanelView.Age(TimeSpan.FromSeconds(30)) == "agora");

        // --- Logitech HID++ 2.0 ---
        var ask = LogitechProvider.BuildRequest(2, 0x00, 0, 0x10, 0x04);
        Check("logi pergunta curta", ask.SequenceEqual(B(0x10, 0x02, 0x00, 0x0D, 0x10, 0x04, 0x00)));
        Check("logi pergunta com funcao 1", LogitechProvider.BuildRequest(1, 0x08, 1)[3] == 0x1D);
        var ok = LogitechProvider.MatchReply(ask, B(0x11, 0x02, 0x00, 0x0D, 0x06, 0x00, 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        Check("logi resposta casa e traz dados", ok?.Data?[0] == 0x06);
        Check("logi erro 1.0 do receptor", LogitechProvider.MatchReply(ask, B(0x10, 0x02, 0x8F, 0x00, 0x0D, 0x09, 0x00))?.Error10 == 0x09);
        Check("logi erro 2.0 do aparelho", LogitechProvider.MatchReply(ask, B(0x11, 0x02, 0xFF, 0x00, 0x0D, 0x05, 0x00))?.Error20 == 0x05);
        Check("logi outro slot nao casa", LogitechProvider.MatchReply(ask, B(0x11, 0x03, 0x00, 0x0D, 0x06, 0x00, 0x03)) is null);
        Check("logi notificacao (swid 0) nao casa", LogitechProvider.MatchReply(ask, B(0x11, 0x02, 0x00, 0x00, 0x06, 0x00, 0x03)) is null);
        Eq("logi 1004 percentual", 64, LogitechProvider.ParseUnified(B(64, 0x04, 0x00, 0x00)).Percent);
        Check("logi 1004 carregando", LogitechProvider.ParseUnified(B(64, 0x04, 0x01, 0x00)).ChargeText == "carregando");
        Check("logi 1004 so faixa", LogitechProvider.ParseUnified(B(0, 0x02, 0x00, 0x00)).Level == "LOW");
        Check("logi 1004 sem nada", LogitechProvider.ParseUnified(B(0, 0, 0, 0)).Status == ReadingStatus.NoData);
        Eq("logi 1000 percentual", 50, LogitechProvider.ParseStatus(B(50, 30, 0x00)).Percent);
        Check("logi 1000 zero e' desconhecido", LogitechProvider.ParseStatus(B(0, 0, 0x00)).Status == ReadingStatus.NoData);
        Check("logi 1000 cheia", LogitechProvider.ParseStatus(B(100, 0, 0x03)).ChargeText == "carga completa");
        Eq("logi 1001 4.2 V = 100", 100, LogitechProvider.ParseVoltage(B(0x10, 0x68, 0x00)).Percent);
        Eq("logi 1001 3.8 V = 50", 50, LogitechProvider.ParseVoltage(B(0x0E, 0xD8, 0x00)).Percent);
        Eq("logi 1001 abaixo de 3.5 V = 0", 0, LogitechProvider.PercentFromMillivolts(3400));
        Check("logi 1001 carregando", LogitechProvider.ParseVoltage(B(0x0E, 0xD8, 0x80)).ChargeText == "carregando");
        Check("logi 1001 tensao absurda", LogitechProvider.ParseVoltage(B(0x00, 0x05, 0x00)).Status == ReadingStatus.NoData);
        Check("logi nome ganha a marca", LogitechProvider.CleanName("G305") == "Logitech G305");
        Check("logi nome nao repete a marca", LogitechProvider.CleanName("Logitech G502") == "Logitech G502");

        // --- Razer: relatorio de feature de 90 bytes ---
        var rz = RazerProvider.BuildRequest(0x1F, 0x80);
        Check("razer pergunta de bateria", rz.Length == 90 && rz[1] == 0x1F && rz[5] == 0x02 && rz[6] == 0x07 && rz[7] == 0x80);
        Check("razer crc = 02^07^80", rz[88] == (0x02 ^ 0x07 ^ 0x80));
        var rzOk = (byte[])rz.Clone(); rzOk[0] = 0x02; rzOk[9] = 0xFF; rzOk[88] = RazerProvider.Crc(rzOk);
        Check("razer resposta valida", RazerProvider.IsReplyTo(rzOk, 0x80));
        Eq("razer 255 = 100%", 100, RazerProvider.ParseLevel(rzOk));
        rzOk[9] = 128; rzOk[88] = RazerProvider.Crc(rzOk);
        Eq("razer 128 = 50%", 50, RazerProvider.ParseLevel(rzOk));
        var rzBad = (byte[])rzOk.Clone(); rzBad[10] ^= 1;
        Check("razer crc errado rejeitado", !RazerProvider.IsReplyTo(rzBad, 0x80));
        Check("razer resposta de outro comando", !RazerProvider.IsReplyTo(rzOk, 0x84));
        var rzBusy = (byte[])rz.Clone(); rzBusy[0] = 0x01;
        Null("razer ocupado nao vira valor", RazerProvider.ParseLevel(rzBusy));
        Check("razer nome sem o tipo de conexao", RazerProvider.StripLink("Razer Viper V2 Pro (Wireless)") == "Razer Viper V2 Pro");
        Check("razer nome do receptor", RazerProvider.StripLink("Razer Basilisk Ultimate Receiver") == "Razer Basilisk Ultimate");
        // --- SteelSeries: indices como os do hidapi (sem o byte do report id 0) ---
        Check("ss normaliza report id 0", SteelSeriesProvider.Normalize(B(0x00, 0xB0), B(0x00, 0xB0, 0x01, 0x40)).SequenceEqual(B(0xB0, 0x01, 0x40)));
        Check("ss mantem report id 06", SteelSeriesProvider.Normalize(B(0x06, 0x18), B(0x06, 0x18, 0x50)).Length == 3);
        Eq("ss arctis 7 percentual", 80, SteelSeriesProvider.Arctis7(B(0x06, 0x18, 80), 0x1260)?.Percent);
        Check("ss arctis 1 desligado", SteelSeriesProvider.Arctis1(B(0x06, 0x12, 0x01, 50), 0x12B3) is null);
        Eq("ss arctis 1 percentual", 50, SteelSeriesProvider.Arctis1(B(0x06, 0x12, 0x03, 50), 0x12B3)?.Percent);
        Eq("ss 7+ niveis 0..4", 75, SteelSeriesProvider.Arctis7Plus(B(0xB0, 0x03, 0x03, 0x00), 0x220E)?.Percent);
        Check("ss 7+ desligado", SteelSeriesProvider.Arctis7Plus(B(0xB0, 0x01, 0x03, 0x00), 0x220E) is null);
        Eq("ss nova 7 antigo em niveis", 50, SteelSeriesProvider.Nova7(B(0xB0, 0x00, 0x02, 0x03), 0x2202)?.Percent);
        Eq("ss nova 7 novo em percentual", 62, SteelSeriesProvider.Nova7(B(0xB0, 0x00, 62, 0x03), 0x22A1)?.Percent);
        Check("ss nova 7 carregando", SteelSeriesProvider.Nova7(B(0xB0, 0x00, 62, 0x01), 0x22A1)?.ChargeText == "carregando");
        Check("ss nova 7 desligado", SteelSeriesProvider.Nova7(B(0xB0, 0x00, 62, 0x00), 0x22A1) is null);
        Eq("ss nova 5 percentual", 44, SteelSeriesProvider.Nova5(B(0xB0, 0x03, 0x00, 44, 0x00), 0x2232)?.Percent);
        Check("ss nova 5 desligado", SteelSeriesProvider.Nova5(B(0xB0, 0x02, 0x00, 44, 0x00), 0x2232) is null);
        Eq("ss gamebuds o mais fraco", 40, SteelSeriesProvider.GameBuds(B(0xB0, 0, 0, 0x03, 0x03, 70, 40), 0x230A)?.Percent);
        Check("ss gamebuds no estojo", SteelSeriesProvider.GameBuds(B(0xB0, 0, 0, 0x02, 0x02, 70, 40), 0x230A) is null);
        var pro = new byte[16]; pro[0] = 0x06; pro[6] = 4; pro[15] = 0x08;
        Eq("ss nova pro 4 de 8", 50, SteelSeriesProvider.NovaPro(pro, 0x12E0)?.Percent);
        Eq("ss aerox nivel 15 = 70%", 70, SteelSeriesProvider.Aerox(B(0xD2, 15), 0x1838)?.Percent);
        Check("ss aerox carregando", SteelSeriesProvider.Aerox(B(0xD2, 0x80 | 15), 0x1838)?.ChargeText == "carregando");
        Eq("ss rival 3 percentual", 90, SteelSeriesProvider.Rival(B(90, 0, 1), 0x1830)?.Percent);
        Check("ss handles", new SteelSeriesProvider().Handles(0x1038, 0x12E0) && !new SteelSeriesProvider().Handles(0x1038, 0x0001));

        // --- HyperX ---
        Eq("hx cloud II nivel em [7]", 66, HyperXProvider.ParseCloudII(B(0x06, 0xFF, 0xBB, 0x02, 0, 0, 0, 66), B(0x06, 0xFF, 0xBB, 0x03, 0x00)).Percent);
        Check("hx cloud II carregando em [4]", HyperXProvider.ParseCloudII(B(0x06, 0xFF, 0xBB, 0x02, 0, 0, 0, 66), B(0x06, 0xFF, 0xBB, 0x03, 0x01)).ChargeText == "carregando");
        Check("hx kingston pede com BB e o comando", HyperXProvider.KingstonRequest(0x02)[14] == 0xBB && HyperXProvider.KingstonRequest(0x02)[15] == 0x02);
        Eq("hx alpha 2 percentual", 73, HyperXProvider.ParseAlpha2(B(0x51, 0x02, 73, 0x00)).Percent);
        Check("hx alpha 2 fora da faixa", HyperXProvider.ParseAlpha2(B(0x51, 0x02, 150, 0x00)).Status == ReadingStatus.NoData);
        Eq("hx flight 3800 mV = 55%", 55, HyperXProvider.ParseFlight(B(0x21, 0xFF, 0x05, 0x0E, 0xD8)).Percent);
        Check("hx flight acima de 4107 mV = no carregador", HyperXProvider.ParseFlight(B(0x21, 0xFF, 0x05, 0x10, 0x20)).Status == ReadingStatus.Charging);
        Eq("hx flight abaixo da curva = 0", 0, HyperXProvider.FlightPercent(3400));
        Eq("hx dart nivel em [4]", 57, HyperXProvider.ParseDart(B(0x51, 0, 0, 0x03, 57, 0x00)).Percent);
        Check("hx dart carregando", HyperXProvider.ParseDart(B(0x51, 0, 0, 0x03, 57, 0x01)).ChargeText == "carregando");
        Check("hx handles por vid e pid", new HyperXProvider().Handles(0x03F0, 0x0696) && !new HyperXProvider().Handles(0x0951, 0x0696) && !new HyperXProvider().Handles(0x0951, 0x16B7));

        // --- Corsair ---
        // Lista do Slipstream (sem report id): [5] = 1 aparelho, vid 1B1C, pid 7040 (Dark Core RGB Pro), tipo 1, id de 2 bytes.
        var lista = B(0x00, 0x08, 0x00, 0x00, 0x00, 0x01, 0x1C, 0x1B, 0x00, 0x00, 0x80, 0x1B, 0x01, 0x02, 0x41, 0x42);
        var pares = CorsairProvider.ParsePairedList(lista);
        Check("corsair lista um aparelho no endpoint 09", pares.Count == 1 && pares[0].Pid == 7040 && pares[0].Endpoint == 0x09);
        var dois = B(0x00, 0x08, 0x00, 0x00, 0x00, 0x02, 0x1C, 0x1B, 0, 0, 0x80, 0x1B, 0x01, 0x00, 0x1C, 0x1B, 0, 0, 0xAB, 0x1B, 0x02, 0x00);
        var pares2 = CorsairProvider.ParsePairedList(dois);
        Check("corsair lista dois: endpoint 08 + tipo", pares2.Count == 2 && pares2[0].Endpoint == 0x09 && pares2[1].Endpoint == 0x0A && pares2[1].Pid == 7083);
        Eq("corsair bragi 845 = 84%", 84, CorsairProvider.ParseBragi(B(0x09, 0x02, 0x00, 0x4D, 0x03), 2, 3)?.Percent);
        Check("corsair bragi status ruim = sem aparelho", CorsairProvider.ParseBragi(B(0x09, 0x02, 0x05, 0x4D, 0x03), 2, 3) is null);
        Check("corsair bragi zero = sem aparelho", CorsairProvider.ParseBragi(B(0x09, 0x02, 0x00, 0x00, 0x00), 2, 3) is null);
        Eq("corsair v2 em [4..5]", 61, CorsairProvider.ParseV2(B(0x02, 0x09, 0x02, 0x00, 0x62, 0x02))?.Percent);
        Eq("corsair void nivel sem o bit do microfone", 55, CorsairProvider.ParseVoid(B(0x64, 0x00, 0x80 | 55, 0x00, 0x01))?.Percent);
        Check("corsair void carregando", CorsairProvider.ParseVoid(B(0x64, 0x00, 55, 0x00, 0x05))?.ChargeText == "carregando");
        Check("corsair void sem fone", CorsairProvider.ParseVoid(B(0x64, 0x00, 55, 0x00, 0x00)) is null);
        Check("corsair void valor fora da faixa vira sem dado, nao 100%", CorsairProvider.ParseVoid(B(0x64, 0x00, 0x7F, 0x00, 0x01)) is { Status: ReadingStatus.NoData, Percent: null });
        Check("corsair handles", new CorsairProvider().Handles(0x1B1C, 7132) && new CorsairProvider().Handles(0x1B1C, 0x0A64) && !new CorsairProvider().Handles(0x1B1C, 0x0001));

        // --- fones Logitech e Razer, teclados Razer ---
        var g633 = new (int, int)[] { (4100, 100), (3950, 80), (3850, 60), (3750, 40), (3650, 20), (3500, 10), (3300, 5), (3150, 0) };
        Eq("logi fone 3850 mV = 60%", 60, LogitechProvider.ParseHeadset(B(0x11, 0xFF, 0x08, 0x0A, 0x0F, 0x0A, 0x01), g633)?.Percent);
        Check("logi fone carregando", LogitechProvider.ParseHeadset(B(0x11, 0xFF, 0x08, 0x0A, 0x0F, 0x0A, 0x03), g633)?.ChargeText == "carregando");
        Check("logi fone desligado (FF)", LogitechProvider.ParseHeadset(B(0x11, 0xFF, 0xFF, 0x08, 0x0A, 0x09, 0x00), g633) is null);
        Eq("logi fone meio da reta", 70, LogitechProvider.Interpolate(3900, g633));
        Check("logi handles fone e receptor", new LogitechProvider().Handles(0x046D, 0x0A87) && new LogitechProvider().Handles(0x046D, 0xC52B));
        var nari = new byte[64]; nari[0] = 0xFF; nari[12] = 0x0E; nari[13] = 0xD8;
        Eq("razer nari 3800 mV", 50, RazerProvider.ParseNari(nari)?.Percent);
        nari[12] = 0; nari[13] = 0;
        Check("razer nari zerado = sem dado", RazerProvider.ParseNari(nari) is null);
        Check("razer handles teclado e nari", new RazerProvider().Handles(0x1532, 0x0290) && new RazerProvider().Handles(0x1532, 0x051C));

        Check("razer handles so 1532", new RazerProvider().Handles(0x1532, 0x00B7) && !new RazerProvider().Handles(0x046D, 0x00B7));

        // --- regra 4: porta unica de escrita (sem aparelho: caminho que nao existe) ---
        Check("porta: so o HidRequest.cs escreve no aparelho", WritersOutsideTheGate() is { Count: 0 });
        var vendor = new HidCollection { Path = @"\?\joule-selftest-nao-existe", UsagePage = 0xFF00, Usage = 0x01, InputLen = 20, OutputLen = 20 };
        var sysMouse = new HidCollection { Path = vendor.Path, UsagePage = 0x01, Usage = 0x02, InputLen = 20, OutputLen = 20 };
        var sysKeyboard = new HidCollection { Path = vendor.Path, UsagePage = 0x01, Usage = 0x06, InputLen = 20, OutputLen = 20 };
        var hello = new byte[] { 0x10, 0x01, 0x02 };
        var tiny = new HidCollection { Path = vendor.Path, UsagePage = 0xFF00, Usage = 0x01, InputLen = 20, OutputLen = 2, FeatureLen = 2 };
        using (var gate = new HidRequest())
        {
            HidRequest.NewReading(false, SelfTestVid);
            int sent = HidRequest.Sent;
            gate.Ask(sysMouse, hello, _ => true, TimeSpan.Zero);
            gate.Ask(sysKeyboard, hello, _ => true, TimeSpan.Zero);
            Check("porta: mouse e teclado de sistema recusados sem tentar escrever", HidRequest.Sent == sent && gate.Asks == 0);
            gate.Ask(tiny, hello, _ => true, TimeSpan.Zero);
            Check("porta: pergunta maior que o relatorio declarado recusada", HidRequest.Sent == sent && gate.Asks == 0);
            Check("porta: feature de tamanho errado recusada", HidRequest.Feature(tiny, hello, TimeSpan.Zero, _ => false) is null && HidRequest.Sent == sent);
            HidRequest.NewReading(false, SelfTestVid);
            for (int i = 0; i < HidRequest.MaxAsks + 12; i++) gate.Ask(vendor, hello, _ => true, TimeSpan.Zero);
            Eq("porta: no maximo 48 perguntas por leitura", HidRequest.MaxAsks, HidRequest.Sent - sent);
            var feature = new HidCollection { Path = vendor.Path, UsagePage = 0xFF00, Usage = 0x01, FeatureLen = hello.Length };
            int atCap = HidRequest.Sent;
            HidRequest.Feature(feature, hello, TimeSpan.Zero, _ => false);
            using (var other = new HidRequest()) other.Ask(vendor, hello, _ => true, TimeSpan.Zero);
            Check("porta: o teto soma outros aparelhos e a feature da Razer", HidRequest.Sent == atCap);
            HidRequest.NewReading(false, SelfTestVid);
            gate.Ask(vendor, hello, _ => true, TimeSpan.Zero);
            Check("porta: a leitura seguinte volta a ter perguntas", HidRequest.Sent == atCap + 1);

            // --- camada de seguranca (2026-11-12): so escutar, licenca por marca, modelo bloqueado ---
            int s0 = HidRequest.Sent;
            HidRequest.NewReading(true, SelfTestVid);
            gate.Ask(vendor, hello, _ => true, TimeSpan.Zero);
            HidRequest.Feature(feature, hello, TimeSpan.Zero, _ => false);
            Check("so escutar: nenhuma pergunta, nem por feature, mesmo com licenca", HidRequest.Sent == s0);

            var razer = new HidCollection { Path = vendor.Path, Vid = 0x1532, Pid = 0x00B7, UsagePage = 0xFF00, Usage = 0x01, InputLen = 20, OutputLen = 20, FeatureLen = 90, Product = "teste" };
            HidRequest.NewReading(false, SelfTestVid);
            gate.Ask(razer, hello, _ => true, TimeSpan.Zero);
            HidRequest.Feature(razer, new byte[90], TimeSpan.Zero, _ => false);
            Check("marca nao testada sem licenca: nada enviado", HidRequest.Sent == s0);
            Check("marca nao testada sem licenca: entra na espera do aviso", HidRequest.WaitingConsent.ContainsKey(0x1532));
            var rapooVt9 = new HidCollection { Path = vendor.Path, Vid = 0x24AE, Pid = 0x1205, UsagePage = 0xFF00, Usage = 0x01, FeatureLen = 8 };
            HidRequest.ReadFeature(rapooVt9, 0x2A);
            Check("leitura de feature tambem passa pela licenca", HidRequest.WaitingConsent.ContainsKey(0x24AE));

            HidRequest.NewReading(false, new[] { "0000", "1532" });
            gate.Ask(razer, hello, _ => true, TimeSpan.Zero);
            Check("marca permitida: pergunta sai e sai da espera", HidRequest.Sent == s0 + 1 && !HidRequest.WaitingConsent.ContainsKey(0x1532));
            HidRequest.NewReading(false, new[] { "lixo", "0000" });
            gate.Ask(vendor, hello, _ => true, TimeSpan.Zero);
            Check("licenca com valor estranho no settings.json e' ignorada, o resto vale", HidRequest.Sent == s0 + 2);

            HidRequest.Blocked.Add(razer.HardwareId);
            HidRequest.NewReading(false, new[] { "0000", "1532" });
            gate.Ask(razer, hello, _ => true, TimeSpan.Zero);
            HidRequest.Blocked.Remove(razer.HardwareId);
            Check("modelo bloqueado: nada enviado, mesmo com licenca", HidRequest.Sent == s0 + 2);
            Check("nenhum modelo bloqueado e nenhuma marca testada que pergunta, em 2026-11-12", HidRequest.Blocked.Count == 0 && HidRequest.Tested.Count == 0);
            HidRequest.NewReading(false, SelfTestVid);
        }
        Check("so escutar desligado e nenhuma licenca no comeco", new Settings() is { ListenOnly: false, AskAllowedVids.Count: 0 });

        // Painel: quem espera licenca sai de "outros" e ganha o "Permitir" (2026-11-12).
        var semValor = new[]
        {
            new DeviceEntry { Key = "rz", DisplayName = "Razer", HardwareId = "1532:00B7", Reading = BatteryReading.NoData("sem resposta"), IsConnected = true },
            new DeviceEntry { Key = "kb", DisplayName = "Teclado", HardwareId = "0000:0001", Reading = BatteryReading.Unsupported, IsConnected = true },
        };
        string? Espera(string id) => id.StartsWith("1532") ? "1532" : null;
        var pedem = PanelView.NeedConsent(semValor, Espera);
        Check("painel: marca esperando licenca vira linha com Permitir", pedem.Count == 1 && pedem[0].Vid == "1532");
        Check("painel: e sai de outros, o resto fica", PanelView.Others(semValor, Espera) is { Count: 1 } o && o[0].Key == "kb" && PanelView.Others(semValor).Count == 2);

        // --- quem baixa o Joule (camada de seguranca, 2026-11-12) ---
        var netLines = NetworkCode();
        Report("sem internet: nenhum codigo de rede no app e no JouleScan fora do atualizador", netLines is { Count: 0 },
            netLines is null ? "codigo-fonte nao achado" : string.Join(" | ", netLines));
        // --- idioma (2026-11-12): portugues e ingles lado a lado em L.T ---
        var holes = TranslationMismatches();
        Report("idioma: cada texto em ingles leva os mesmos {valores} do portugues", holes is { Count: 0 },
            holes is null ? "codigo-fonte nao achado" : string.Join(" | ", holes));
        L.Set("en");
        bool english = PanelView.Age(TimeSpan.FromMinutes(5)) == "5 min ago" && PanelView.Age(TimeSpan.FromSeconds(1)) == "now";
        L.Set("pt");
        Check("idioma: modo ingles troca os textos e o portugues volta", english && PanelView.Age(TimeSpan.FromMinutes(5)) == "há 5 min");

        // --- tela depois de atualizar (2026-11-12) ---
        Check("apoio: nao abre na primeira instalacao", Changes.UpdatedFrom("", "0.2.0", onboardingDone: false) is null);
        Check("apoio: nao abre de novo na mesma versao", Changes.UpdatedFrom("0.2.0", "0.2.0", onboardingDone: true) is null);
        Check("apoio: abre vindo de versao sem a tela", Changes.UpdatedFrom("", "0.2.0", onboardingDone: true) == "");
        Check("apoio: abre a cada versao nova", Changes.UpdatedFrom("0.2.0", "0.2.1", onboardingDone: true) == "0.2.0");
        Check("novidades: so as mais novas que a versao de antes",
            Changes.Since("0.1.22").Count == Changes.All.Count(c => Version.Parse(c.Version) > new Version(0, 1, 22))
            && Changes.Since("99.0.0").Count == 0 && Changes.Since("").Count == Changes.All.Length);
        Check("novidades: toda novidade tem versao valida e as duas linguas",
            Changes.All.All(c => Version.TryParse(c.Version, out _) && c.TitleEn.Length > 0 && c.TextEn.Length > 0));
        Check("apoio: link so do Stripe (ou vazio, sem botao)",
            BatteryTray.Ui.AppInfo.SupportUrl.Length == 0 || BatteryTray.Ui.AppInfo.SupportUrl.StartsWith("https://buy.stripe.com/", StringComparison.Ordinal));

        // Item 4 (2026-11-12): o main so do repositorio oficial; o Teste so da pasta deste PC (decisao do Dillan).
        Check("atualizacao so do repositorio oficial DillanCidral/Joule",
            BatteryTray.Ui.AppInfo.UpdateFeed == "https://github.com/DillanCidral/Joule");
        Check(@"atualizacao do Joule Teste so de pasta local releases\teste",
            BatteryTray.Ui.AppInfo.TestFeedOrEmpty(@"D:\repo\releases\teste") == @"D:\repo\releases\teste"
            && BatteryTray.Ui.AppInfo.TestFeedOrEmpty("https://github.com/DillanCidral/Joule") == ""
            && BatteryTray.Ui.AppInfo.TestFeedOrEmpty(@"\\servidor\releases\teste") == ""
            && BatteryTray.Ui.AppInfo.TestFeedOrEmpty(@"D:\repo\releases\main") == ""
            && BatteryTray.Ui.AppInfo.TestFeedOrEmpty(null) == "");
        var testeFeed = TestChannelFeed();
        Report("o Joule Teste tira a pasta so da compilacao, pelo filtro", testeFeed is not null && testeFeed.StartsWith("TestFeedOrEmpty(", StringComparison.Ordinal),
            testeFeed ?? "AppInfo.cs sem o bloco JOULE_TESTE");

        // --- regra 1: quem le e quando (ReadingTurns, a fila da bandeja) ---
        Check("conexao espera 1,5 s para assentar", ReadingTurns.Settle == TimeSpan.FromMilliseconds(1500));
        var turns = new ReadingTurns();
        Check("atualizar: primeira leitura completa sai", turns.TryStartFull());
        Check("atualizar: clique duplo nao abre segunda leitura", !turns.TryStartFull());
        Check("ao vivo nao corre durante a completa", !turns.TryStartLive());
        var t0 = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        turns.Note("mouse", arrived: true);
        var busy = turns.Take(t0);
        Check("conexao durante o atualizar espera", busy.Arrived.Count == 0 && busy.Retry == ReadingTurns.Settle);
        turns.EndFull();
        var first = turns.Take(t0);
        Check("conexao depois do atualizar le uma vez", first.Arrived.SequenceEqual(new[] { "mouse" }) && first.Retry is null);
        Check("leitura de conexao nao corre junto com a ao vivo", !turns.TryStartLive());
        turns.EndLive();
        Check("clique depois da leitura volta a valer", turns.TryStartFull()); turns.EndFull();
        turns.Note("fone", arrived: true);
        turns.Note("teclado", arrived: false);
        var soon = turns.Take(t0.AddSeconds(3));
        Check("segunda conexao em menos de 10 s espera o restante", soon.Arrived.Count == 0 && soon.Retry == TimeSpan.FromSeconds(7));
        Check("saida nao espera a fila de conexao", soon.Removed.SequenceEqual(new[] { "teclado" }));
        turns.EndLive();
        var later = turns.Take(t0.AddSeconds(10));
        Check("passados 10 s a segunda conexao le", later.Arrived.SequenceEqual(new[] { "fone" }));
        turns.EndLive();
        turns.Note("pendrive", arrived: true);
        turns.Note("pendrive", arrived: false);
        var blink = turns.Take(t0.AddMinutes(1));
        Check("entrou e saiu antes de assentar: nao le", blink.Arrived.Count == 0 && blink.Removed.SequenceEqual(new[] { "pendrive" }));
        turns.EndLive();
        Check("sem aviso nenhum nao ha o que fazer", turns.Take(t0.AddMinutes(2)) is { Arrived.Count: 0, Removed.Count: 0, Retry: null } && turns.TryStartLive());
        turns.EndLive();

        // --- regra 2: aparelho sumiu, parar de tocar nele ---
        var service = new ReadingService(ProviderRegistry.CreateDefault(), new ReadingCache(), new Settings());
        var shark = new HidCollection { Path = "shark", Vid = 0x1D57, Pid = 0xFA60, UsagePage = 0xFF00, Usage = 1, InputLen = 20 };
        var rapoo = new HidCollection { Path = "rapoo", Vid = 0x24AE, Pid = 0x1871, UsagePage = 0xFF00, Usage = 1, InputLen = 20 };
        var before = new RefreshResult
        {
            Devices = new[]
            {
                new DeviceEntry { Key = "hid:shark", DisplayName = "Mouse", HardwareId = "1D57:FA60", Reading = BatteryReading.Percentage(80), IsConnected = true },
                new DeviceEntry { Key = "hid:rapoo", DisplayName = "Outro", HardwareId = "24AE:1871", Reading = BatteryReading.Percentage(50), IsConnected = true },
            },
        };
        service.RememberForTest(new[] { shark, rapoo });
        Check("saiu algo desconhecido: lista nao muda", service.MarkRemoved(before, new[] { "pendrive" }) is null);
        int sentBefore = HidRequest.Sent;
        var after = service.MarkRemoved(before, new[] { "shark" });
        var gone = after?.Devices.FirstOrDefault(d => d.Key == "hid:shark");
        Check("aparelho que saiu vira desconectado", gone is { IsConnected: false } && gone.Reading.Status == ReadingStatus.Disconnected);
        Check("aparelho que saiu nao recebe pergunta", HidRequest.Sent == sentBefore);
        Check("o outro aparelho segue conectado", after?.Devices.First(d => d.Key == "hid:rapoo").IsConnected == true);

        Console.WriteLine();
        Console.WriteLine($"  {_passed} passou, {_failed} falhou");
        return _failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// Ida e volta no HKCU\...\Run. Mexe no registro de verdade, por isso fica
    /// atras de uma flag propria; restaura o estado anterior ao terminar.
    /// </summary>
    public static int RunAutostartRoundTrip()
    {
        Console.WriteLine("=== autostart (HKCU\\...\\Run) ===");
        bool before = Autostart.IsEnabled();
        Console.WriteLine($"  estado inicial: {(before ? "ligado" : "desligado")}");

        try
        {
            Check("liga sem erro", Autostart.Set(true));
            Check("aparece como ligado", Autostart.IsEnabled());
            Check("desliga sem erro", Autostart.Set(false));
            Check("aparece como desligado", !Autostart.IsEnabled());
        }
        finally
        {
            Autostart.Set(before);
            Console.WriteLine($"  estado restaurado: {(Autostart.IsEnabled() ? "ligado" : "desligado")}");
        }

        Console.WriteLine();
        Console.WriteLine($"  {_passed} passou, {_failed} falhou");
        return _failed == 0 ? 0 : 1;
    }

    private static BluetoothBatteryNodes.RawNode Bt(
        string enumerator, string address, string? name, int? percent,
        DateTime? updatedUtc, bool connected, bool root) =>
        new(enumerator, BluetoothBatteryNodes.NormalizeAddress(address), name, percent, updatedUtc, connected, root);

    private static byte[] B(params int[] bytes) => bytes.Select(b => (byte)b).ToArray();

    /// <summary>Relatorio 0x01 do DualSense no cabo: 64 bytes, status no byte 53.</summary>
    private static byte[] Ds(byte status)
    {
        var r = new byte[64];
        r[0] = 0x01;
        r[53] = status;
        return r;
    }

    /// <summary>Relatorio curto do DualSense por Bluetooth: 78 bytes, quase todos zero.</summary>
    private static byte[] BluetoothReport()
    {
        var r = new byte[78];
        r[0] = 0x01;
        return r;
    }

    private static byte[] WithId(byte[] report, byte id)
    {
        var copy = (byte[])report.Clone();
        copy[0] = id;
        return copy;
    }

    private static void Eq(string name, int expected, int? actual) =>
        Report(name, actual == expected, $"esperado {expected}, veio {actual?.ToString() ?? "null"}");

    private static void Null(string name, int? actual) =>
        Report(name, actual is null, $"esperado null, veio {actual}");

    private static void Check(string name, bool condition) => Report(name, condition, "condicao falsa");

    /// <summary>
    /// Arquivos do repositorio, fora o HidRequest.cs, que chamam escrita no
    /// aparelho (a declaracao extern do HidNative nao conta). Null = codigo-fonte
    /// nao achado, e o teste falha: rodar da pasta do repositorio.
    /// </summary>
    private static List<string>? WritersOutsideTheGate()
    {
        var call = new System.Text.RegularExpressions.Regex(@"\b(WriteFile\w*|HidD_SetFeature|HidD_SetOutputReport)\s*\(");
        return SourceFiles("src", "tools")?
            .Where(f => Path.GetFileName(f) != "HidRequest.cs")
            .Where(f => File.ReadLines(f).Any(l => call.IsMatch(l) && !l.Contains("extern ")))
            .ToList();
    }

    /// <summary>
    /// Linhas do app e do JouleScan com codigo de internet (camada de seguranca,
    /// 2026-11-12). O atualizador e' a excecao: o Velopack faz a rede por dentro,
    /// e o nosso codigo so pode cria-lo com o AppInfo.UpdateFeed. Null = codigo-fonte nao achado.
    /// </summary>
    private static List<string>? NetworkCode()
    {
        var net = new System.Text.RegularExpressions.Regex(
            @"\b(HttpClient|HttpMessageHandler|WebClient|WebRequest|HttpWebRequest|WebSocket|ClientWebSocket|TcpClient|TcpListener|UdpClient|Socket|SmtpClient|Dns|NetworkStream)\b|\bSystem\.Net\b(?!\.NetworkInformation)");
        var updater = new System.Text.RegularExpressions.Regex(@"\bnew\s+(Velopack\.)?UpdateManager\s*\(");
        return SourceFiles("src")?
            .SelectMany(f => File.ReadLines(f).Select(l => (f, l: l.Trim())))
            .Where(x => !x.l.StartsWith("//") && !x.l.StartsWith("///"))
            .Where(x => net.IsMatch(x.l)
                        || (updater.IsMatch(x.l) && !AllowedUpdater.Any(x.l.Contains))
                        || (x.l.Contains("Velopack.Sources") && !x.l.Contains(AllowedUpdater[1])))
            .Select(x => $"{Path.GetFileName(x.f)}: {x.l}")
            .ToList();
    }

    /// <summary>As duas formas aceitas de criar o atualizador, sempre com o AppInfo.UpdateFeed (qualquer outra fonte do Velopack falha).</summary>
    private static readonly string[] AllowedUpdater =
    {
        "UpdateManager(AppInfo.UpdateFeed)",
        "UpdateManager(new Velopack.Sources.GithubSource(AppInfo.UpdateFeed, null, false))",
    };

    /// <summary>O UpdateFeed do Joule Teste como escrito no AppInfo.cs (o diag compila o canal main). Null = nao achado.</summary>
    private static string? TestChannelFeed()
    {
        var file = SourceFiles("src")?.FirstOrDefault(f => Path.GetFileName(f) == "AppInfo.cs");
        if (file is null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(file),
            @"#if JOULE_TESTE\s+public static readonly string UpdateFeed = ([^;]+);\s+#else");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    /// <summary>L.T cujo ingles nao leva os mesmos buracos {..} do portugues (um numero ou nome sumiria numa das linguas).</summary>
    private static List<string>? TranslationMismatches()
    {
        var pair = new System.Text.RegularExpressions.Regex(@"L\.T\((\$?)""((?:[^""\\]|\\.)*)"",\s*(\$?)""((?:[^""\\]|\\.)*)""\)");
        var hole = new System.Text.RegularExpressions.Regex(@"\{[^{}]*\}");
        string Holes(string s) => string.Join(",", hole.Matches(s).Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal));
        return SourceFiles("src")?
            .SelectMany(f => pair.Matches(File.ReadAllText(f)).Select(m => (f, m)))
            .Where(x => x.m.Groups[1].Value != x.m.Groups[3].Value || Holes(x.m.Groups[2].Value) != Holes(x.m.Groups[4].Value)
                        || x.m.Groups[4].Value.Trim().Length == 0)
            .Select(x => $"{Path.GetFileName(x.f)}: {x.m.Groups[2].Value}")
            .ToList();
    }

    /// <summary>Os .cs do repositorio nas pastas dadas, sem bin/obj. Null = rodou fora do repositorio.</summary>
    private static List<string>? SourceFiles(params string[] dirs)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "BatteryTray"))) root = root.Parent;
        if (root is null) return null;
        return dirs
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(root.FullName, d), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
    }

    private static void Report(string name, bool ok, string detail)
    {
        if (ok) { _passed++; Console.WriteLine($"  ok    {name}"); }
        else { _failed++; Console.WriteLine($"  FALHA {name}: {detail}"); }
    }
}
