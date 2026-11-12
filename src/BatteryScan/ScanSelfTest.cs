using System.Runtime.InteropServices;
using System.Text;

namespace BatteryTray.Scan;

/// <summary>
/// Testes das partes puras do coletor. Rodam sem hardware nenhum plugado, e
/// existem por dois motivos:
///  - o tamanho das structs de interop tem de bater com o das APIs do Windows,
///    senao HidP_GetValueCaps e as funcoes de Bluetooth devolvem lixo em
///    silencio -- erro que nao aparece como excecao, so como dado errado;
///  - a classificacao de usage/transporte e' o que decide se um dispositivo
///    entra no resumo como candidato. Errar ali me faz procurar protocolo em
///    dispositivo que ja tinha caminho padrao.
/// </summary>
internal static class ScanSelfTest
{
    private static int _passed;
    private static int _failed;

    public static int Run()
    {
        Console.WriteLine("=== self-test do coletor ===");

        // --- tamanho das structs de interop ---
        Eq("HIDP_VALUE_CAPS tem 72 bytes", 72, Marshal.SizeOf<ScanNative.HIDP_VALUE_CAPS>());
        Eq("BLUETOOTH_RADIO_INFO tem 520 bytes", 520, Marshal.SizeOf<ScanNative.BLUETOOTH_RADIO_INFO>());
        Eq("BLUETOOTH_DEVICE_INFO tem 560 bytes", 560, Marshal.SizeOf<ScanNative.BLUETOOTH_DEVICE_INFO>());
        Eq("BLUETOOTH_DEVICE_SEARCH_PARAMS tem 40 bytes", 40, Marshal.SizeOf<ScanNative.BLUETOOTH_DEVICE_SEARCH_PARAMS>());
        Eq("SYSTEMTIME tem 16 bytes", 16, Marshal.SizeOf<ScanNative.SYSTEMTIME>());

        // --- usages de bateria ---
        Check("page 0x06 usage 0x20 e' bateria", HidUsages.BatteryUsage(0x0006, 0x0020) is not null);
        Check("page 0x06 usage 0x20 cita o padrao", HidUsages.BatteryUsage(0x0006, 0x0020)!.Contains("padrao HID"));
        Check("AbsoluteStateOfCharge reconhecido", HidUsages.BatteryUsage(0x0085, 0x0065)!.Contains("AbsoluteStateOfCharge"));
        Check("Charging reconhecido", HidUsages.BatteryUsage(0x0085, 0x0044)!.Contains("Charging"));
        Check("usage nova da page 0x85 ainda conta", HidUsages.BatteryUsage(0x0085, 0x00FE) is not null);
        Check("page 0x84 conta como energia", HidUsages.BatteryUsage(0x0084, 0x0002) is not null);
        Null("mouse X/Y nao e' bateria", HidUsages.BatteryUsage(0x0001, 0x0030));
        Null("botao nao e' bateria", HidUsages.BatteryUsage(0x0009, 0x0001));
        Null("vendor-defined nao e' bateria", HidUsages.BatteryUsage(0xFF00, 0x0020));
        Null("page 0x06 com outra usage nao e' bateria", HidUsages.BatteryUsage(0x0006, 0x0021));

        // --- transporte pelo caminho da interface ---
        Eq("USB pelo hid#vid_", "USB", HidUsages.Transport(@"\\?\HID#VID_1D57&PID_FA60&MI_01&Col02#7&abc#{4d1e55b2}"));
        Eq("BLE pelo UUID 0x1812", "Bluetooth LE (HID over GATT)",
            HidUsages.Transport(@"\\?\HID#{00001812-0000-1000-8000-00805f9b34fb}_DEV_VID&02046D_PID&B02A#8&1a2b#{4d1e55b2}"));
        Eq("BT classico pelo UUID 0x1124", "Bluetooth classico (HIDP)",
            HidUsages.Transport(@"\\?\HID#{00001124-0000-1000-8000-00805f9b34fb}_VID&0002046d_PID&b34d#9&x#{4d1e55b2}"));
        Eq("BTHENUM e' Bluetooth classico", "Bluetooth classico", HidUsages.Transport(@"BTHENUM\Dev_AABBCCDDEEFF"));
        Eq("caminho vazio e' desconhecido", "desconhecido", HidUsages.Transport(""));
        Check("IsBluetooth reconhece BLE", HidUsages.IsBluetooth("Bluetooth LE (HID over GATT)"));
        Check("IsBluetooth rejeita USB", !HidUsages.IsBluetooth("USB"));

        // --- class of device ---
        Eq("COD de mouse", "mouse", HidUsages.DeviceKindFromCod(0x2580));
        Eq("COD de teclado", "teclado", HidUsages.DeviceKindFromCod(0x2540));
        Eq("COD de audio", "audio/video", HidUsages.DeviceKindFromCod(0x240404));

        // --- endereco Bluetooth: rgBytes[0] e' o byte baixo ---
        Eq("endereco formatado do mais alto para o mais baixo", "AA:BB:CC:DD:EE:FF", BluetoothScan.FormatAddress(0xAABBCCDDEEFF));
        Eq("endereco com zeros", "00:11:22:33:44:55", BluetoothScan.FormatAddress(0x001122334455));

        // --- render de propriedades de devnode ---
        Eq("DEVPROP string", "Mouse BT", DeviceNodeScan.RenderValue(ScanNative.DEVPROP_TYPE_STRING, Utf16("Mouse BT\0")));
        Eq("DEVPROP string list", "a | b", DeviceNodeScan.RenderValue(ScanNative.DEVPROP_TYPE_STRING_LIST, Utf16("a\0b\0\0")));
        Eq("DEVPROP byte (bateria BT)", "85", DeviceNodeScan.RenderValue(ScanNative.DEVPROP_TYPE_BYTE, new byte[] { 85 }));
        Eq("DEVPROP boolean", "true", DeviceNodeScan.RenderValue(ScanNative.DEVPROP_TYPE_BOOLEAN, new byte[] { 0xFF }));
        Eq("DEVPROP uint32", "300", DeviceNodeScan.RenderValue(ScanNative.DEVPROP_TYPE_UINT32, BitConverter.GetBytes(300u)));
        Eq("DEVPROP desconhecido vira hex", "01 02 03", DeviceNodeScan.RenderValue(0x9999, new byte[] { 1, 2, 3 }));

        // --- candidatos ---
        RunCandidateTests();

        // --- Mandar meu aparelho: nada pessoal sai do PC ---
        RunPrivacyTests();

        // --- linha de comando ---
        var listen = ScanOptions.Parse(new[] { "--listen", "45" });
        Eq("--listen 45", 45, listen.ListenSeconds);
        Check("--no-listen desliga captura", ScanOptions.Parse(new[] { "--no-listen" }).NoListen);
        Eq("--listen fora da faixa e' limitado", 600, ScanOptions.Parse(new[] { "--listen", "99999" }).ListenSeconds);
        Check("--only normaliza para maiusculo", ScanOptions.ParseIds("1d57:fa60,24ae:1871").Contains("1D57:FA60"));
        Check("--only aceita hifen", ScanOptions.ParseIds("1d57-fa60").Contains("1D57:FA60"));

        Console.WriteLine();
        Console.WriteLine($"{_passed} passaram, {_failed} falharam.");
        return _failed == 0 ? 0 : 1;
    }

    // Dados ficticios: nenhum endereco, nome ou serie real (regra de privacidade: nada de dado pessoal no codigo).
    private static void RunPrivacyTests()
    {
        var report = new ScanReport
        {
            System = new SystemInfo { MachineName = "PC-FICTICIO-01", UserName = "fulana" },
            Hid =
            {
                new HidDeviceInfo
                {
                    HardwareId = "1D57:FA60", Product = "Mouse", Serial = "SN9X7Y5Z",
                    Collections = { new HidCollectionInfo { Path = @"\\?\hid#vid_1d57&pid_fa60#7&abc&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}" } },
                },
                new HidDeviceInfo { HardwareId = "0000:0001", Product = "Teclado", Serial = "0000" },
            },
            DeviceNodes =
            {
                new DeviceNodeInfo
                {
                    InstanceId = @"BTHLE\DEV_A1B2C3D4E5F6\8&1A2B&0&A1B2C3D4E5F6",
                    Name = "Celular da Fulana",
                    Properties = { ["Pai"] = @"USB\VID_1D57&PID_FA60\SN9X7Y5Z", ["Pasta"] = @"C:\Users\fulana\AppData" },
                },
            },
            Bluetooth =
            {
                Radios = { new BluetoothRadioInfo { Name = "PC-FICTICIO-01", Address = "11:22:33:44:55:66" } },
                Devices =
                {
                    new BluetoothDeviceInfoDto { Name = "Fone Teste", Address = "AA:BB:CC:DD:EE:01", ClassOfDevice = 0x240404 },
                    new BluetoothDeviceInfoDto { Name = "Celular da Fulana", Address = "A1:B2:C3:D4:E5:F6", ClassOfDevice = 0x5A020C },
                },
            },
            Capture = new CaptureInfo { Groups = { new CaptureGroup { HardwareId = "1D57:FA60", Samples = { "00 00 00 00 00 00 00 00 00 00 00 00" } } } },
            ReportedDevices = new() { "Fone Redragon Zeus" },
        };

        string text = DeviceSubmission.Render(report);
        foreach (var secret in new[] { "PC-FICTICIO-01", "fulana", "SN9X7Y5Z", "Celular da Fulana", "A1B2C3D4E5F6", "11:22:33:44:55:66", "AA:BB:CC:DD:EE:01" })
            Check($"arquivo publico sem \"{secret}\"", !text.Contains(secret, StringComparison.OrdinalIgnoreCase));
        Check("fone Bluetooth continua no arquivo", text.Contains("Fone Teste"));
        Check("celular sai da lista Bluetooth", ScanPrivacy.Redact(report).Bluetooth.Devices.Count == 1);
        Check("GUID da interface HID continua inteiro", text.Contains("001111000030"));
        Check("bytes da captura continuam", text.Contains("00 00 00 00 00 00 00 00 00 00 00 00"));
        Check("aparelho que a pessoa citou vai no arquivo", text.Contains("Fone Redragon Zeus"));
        Check("relatorio original fica intacto", report.System.MachineName == "PC-FICTICIO-01");
        var named = new ScanReport
        {
            System = new SystemInfo { UserName = "fulana" },
            Bluetooth = { Devices = { new BluetoothDeviceInfoDto { Name = "Fone de fulana", Address = "AA:BB:CC:DD:EE:02", ClassOfDevice = 0x240404 } } },
        };
        Check("nome de usuario sai tambem do nome do aparelho", !DeviceSubmission.Render(named).Contains("fulana", StringComparison.OrdinalIgnoreCase));

        // JouleScan (revisao de 2026-11-12): so marca que pergunta e nao foi testada pede licenca no console.
        var registry = BatteryTray.Core.ProviderRegistry.CreateDefault();
        var present = new List<BatteryTray.Interop.HidCollection>
        {
            new() { Path = "a", Vid = 0x1532, Pid = 0x00B7, Product = "Mouse Razer" },
            new() { Path = "b", Vid = 0x1532, Pid = 0x00B7, Product = "Mouse Razer" },
            new() { Path = "c", Vid = 0x1D57, Pid = 0xFA60, Product = "Receptor" },
        };
        var brands = ProvidersScan.BrandsThatAsk(present, registry);
        Check("jouleScan: Razer pede licenca uma vez, Attack Shark (so escuta) nao", brands.Count == 1 && brands[0].Vid == "1532");

        Check("PC nao e' periferico", !ScanPrivacy.IsPeripheral(0x10010C));
        Check("gamepad e' periferico", ScanPrivacy.IsPeripheral(0x002508));

        string url = DeviceSubmission.GitHubUrl("https://github.com/o/r/issues/new", "Aparelho: A & B", "linha 1\nlinha 2");
        Check("URL do GitHub codifica & e espaco", url.Contains("title=Aparelho%3A%20A%20%26%20B"));
        Check("URL do GitHub sem labels (404 sem permissao)", !url.Contains("labels="));
        Check("mailto codifica assunto", DeviceSubmission.MailtoUrl("x@y.z", "a b", "c").StartsWith("mailto:x@y.z?subject=a%20b"));
    }

    private static void RunCandidateTests()
    {
        var report = new ScanReport
        {
            DeviceNodes =
            {
                new DeviceNodeInfo { Name = "Mouse BT", InstanceId = @"BTHLEDEVICE\X", Enumerator = "BTHLEDEVICE", BluetoothBattery = 77 },
                new DeviceNodeInfo { Name = "Teclado sem nada", InstanceId = @"HID\Y", Enumerator = "HID" },
            },
            Hid =
            {
                new HidDeviceInfo
                {
                    HardwareId = "046D:B02A",
                    Product = "MX Master",
                    Transport = "Bluetooth LE (HID over GATT)",
                    DeclaredBattery = { "Battery Strength (padrao HID: page 0x06 usage 0x20)" },
                    Collections =
                    {
                        new HidCollectionInfo
                        {
                            UsagePage = "0xFF00", Usage = "0x0001",
                            ValueCaps = { new HidValueCapInfo { Battery = "Battery Strength (padrao HID: page 0x06 usage 0x20)" } },
                        },
                    },
                },
            },
            Providers =
            {
                new ProviderReadInfo { DisplayName = "Attack Shark R1", HardwareId = "1D57:FA60", Provider = "AttackShark", Status = "Ok", Value = "70%" },
                new ProviderReadInfo { DisplayName = "Sumido", HardwareId = "24AE:1871", Provider = "Rapoo", Status = "Disconnected", Value = "--" },
            },
        };

        var candidates = CandidateFinder.Find(report);

        Eq("tres candidatos", 3, candidates.Count);
        Eq("leitura confirmada vem primeiro", CandidateFinder.Confirmed, candidates[0].Confidence);
        Check("provider desconectado nao vira candidato", candidates.All(c => !c.Device.Contains("Sumido")));
        Check("bateria BT do Windows entra como alta",
            candidates.Any(c => c.Confidence == CandidateFinder.High && c.What.Contains("77%")));
        Check("descritor declarado entra como alta",
            candidates.Any(c => c.Confidence == CandidateFinder.High && c.Device.Contains("046D:B02A")));
        Check("devnode sem nada nao vira candidato", candidates.All(c => !c.Device.Contains("Teclado sem nada")));

        // Captura: byte constante em 1..100 e' pista fraca, e byte 0 nunca conta.
        var stat = new CaptureByteStat { Offset = 4, Min = 7, Max = 7 };
        Check("byte constante e' detectado como constante", stat.Constant);
        Check("byte que varia nao e' constante", !new CaptureByteStat { Offset = 1, Min = 0, Max = 9 }.Constant);
    }

    private static byte[] Utf16(string s) => Encoding.Unicode.GetBytes(s);

    private static void Eq<T>(string name, T expected, T actual)
    {
        if (Equals(expected, actual)) Pass(name);
        else Fail($"{name}: esperado <{expected}>, veio <{actual}>");
    }

    private static void Null(string name, object? value)
    {
        if (value is null) Pass(name);
        else Fail($"{name}: esperado null, veio <{value}>");
    }

    private static void Check(string name, bool condition)
    {
        if (condition) Pass(name);
        else Fail(name);
    }

    private static void Pass(string name)
    {
        _passed++;
        Console.WriteLine($"  ok   {name}");
    }

    private static void Fail(string name)
    {
        _failed++;
        Console.WriteLine($"  FALHOU  {name}");
    }
}
