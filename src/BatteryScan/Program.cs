using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Scan;

/// <summary>
/// Coletor portatil. Roda numa maquina qualquer, olha tudo o que da' para
/// olhar sem escrever em nada, e cospe dois arquivos: um .txt para quem rodou
/// entender, um .json para eu programar em cima.
///
/// Modo de leitura igual ao do app: sob demanda, uma passada so. A unica coisa
/// que fica aberta por um tempo e' a captura passiva, e mesmo assim so se quem
/// esta rodando aceitar.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* console antigo */ }

        var options = ScanOptions.Parse(args);

        if (options.Help)
        {
            PrintHelp();
            return 0;
        }

        if (options.SelfTest) return ScanSelfTest.Run();

        try
        {
            return RunScan(options);
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"FALHA GERAL: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            WaitIfInteractive(options);
            return 1;
        }
    }

    private static int RunScan(ScanOptions options)
    {
        Log.Silent = true; // nada de joule.log no PC de quem roda: so os dois relatorios ficam
        Console.WriteLine("================================================");
        Console.WriteLine($" JOULESCAN {ScanInfo.Version}");
        Console.WriteLine(" coleta dados de bateria dos perifericos");
        Console.WriteLine();
        Console.WriteLine(" nada e' enviado aos aparelhos sem a sua licenca,");
        Console.WriteLine(" nenhum pareamento e' criado ou desfeito.");
        Console.WriteLine("================================================");
        Console.WriteLine();

        var report = new ScanReport();
        var warnings = report.Warnings;

        Step("sistema");
        report.System = SystemScan.Collect(warnings);

        Step("inventario HID");
        var collections = HidInventory.Scan();
        report.Hid = HidScan.Collect(collections, warnings);
        Console.WriteLine($"   {report.Hid.Count} dispositivos HID, {collections.Count} colecoes");

        Step("devnodes e propriedades do Windows");
        report.DeviceNodes = DeviceNodeScan.Collect(warnings, out int skipped);
        report.DeviceNodesSkipped = skipped;
        Console.WriteLine($"   {report.DeviceNodes.Count} devnodes lidos ({skipped} ignorados)");

        Step("Bluetooth");
        report.Bluetooth = BluetoothScan.Collect(warnings);
        Console.WriteLine(report.Bluetooth.RadioPresent
            ? $"   {report.Bluetooth.Radios.Count} radio(s), {report.Bluetooth.Devices.Count} dispositivo(s) pareado(s)"
            : "   sem radio Bluetooth");

        Step("XInput");
        report.XInput = XInputScan.Collect(warnings);
        Console.WriteLine($"   {report.XInput.Count(s => s.Connected)} controle(s) conectado(s)");

        var allowed = AskBrands(ProvidersScan.BrandsThatAsk(collections, ProviderRegistry.CreateDefault()), options);
        Step("providers atuais do app");
        report.Providers = ProvidersScan.Collect(warnings, allowed);

        int seconds = DecideCaptureSeconds(options);
        if (seconds > 0)
        {
            Console.WriteLine();
            Step($"captura passiva ({seconds}s) -- USE OS PERIFERICOS AGORA");
            report.Capture = PassiveCapture.Run(
                collections, seconds, options.OnlyHardwareIds, warnings,
                (elapsed, _) => Console.Write($"\r   escutando... {elapsed}/{seconds}s   "));
            Console.WriteLine();
            Console.WriteLine($"   {report.Capture.Groups.Sum(g => g.Count)} relatorios em {report.Capture.Groups.Count} formatos");
        }

        report.Candidates = CandidateFinder.Find(report);

        Console.WriteLine();
        PrintCandidates(report);

        var written = WriteReport(report, options);
        Console.WriteLine();

        if (written.Count == 0)
        {
            Console.WriteLine("NAO CONSEGUI GRAVAR OS ARQUIVOS. Copie o texto acima e mande assim mesmo.");
        }
        else
        {
            Console.WriteLine("Arquivos gerados -- mande OS DOIS:");
            foreach (var path in written) Console.WriteLine($"   {path}");
            if (!options.Quiet) OpenInExplorer(written[0]);
        }

        WaitIfInteractive(options);
        return 0;
    }

    private static void PrintCandidates(ScanReport report)
    {
        Console.WriteLine("--- resumo: onde parece haver bateria ---");
        if (report.Candidates.Count == 0)
        {
            Console.WriteLine("   nada encontrado por caminho conhecido (isso tambem e' informacao).");
            return;
        }

        foreach (var c in report.Candidates.Take(12))
        {
            Console.WriteLine($"   [{c.Confidence}] {c.Device}");
            Console.WriteLine($"        {c.What}");
        }

        if (report.Candidates.Count > 12)
            Console.WriteLine($"   ... e mais {report.Candidates.Count - 12} no relatorio.");
    }

    /// <summary>
    /// Licenca por marca, como no Joule (camada de seguranca, 2026-11-12):
    /// marca nao testada so recebe a pergunta de bateria se quem roda disser
    /// sim. Sem resposta (--quiet ou Enter) e' nao.
    /// </summary>
    private static List<string> AskBrands(List<(string Vid, string Label)> brands, ScanOptions options)
    {
        var allowed = new List<string>();
        if (brands.Count == 0 || options.Quiet) return allowed;

        Console.WriteLine();
        Console.WriteLine("--- pergunta de bateria (opcional) ---");
        Console.WriteLine("Estes aparelhos so contam a bateria se o programa perguntar, como o");
        Console.WriteLine("programa da marca faz. Ainda nao foram testados com o Joule. A pergunta");
        Console.WriteLine("e' so a de bateria, uma vez, e nao muda nada no aparelho.");
        foreach (var (vid, label) in brands)
        {
            Console.Write($"Perguntar a bateria de {label} ({vid})? [s/N] ");
            string answer = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
            if (answer is "s" or "sim" or "y" or "yes") allowed.Add(vid);
        }
        return allowed;
    }

    /// <summary>
    /// Quanto tempo escutar. Em modo interativo pergunta, porque a captura so
    /// vale a pena se quem esta na frente do PC mexer nos perifericos.
    /// </summary>
    private static int DecideCaptureSeconds(ScanOptions options)
    {
        if (options.NoListen) return 0;
        if (options.ListenSeconds is int explicitSeconds) return explicitSeconds;
        if (options.Quiet) return 0;

        Console.WriteLine();
        Console.WriteLine("--- captura passiva (opcional, mas e' a parte que mais ajuda) ---");
        Console.WriteLine("Fico 30s escutando o que os perifericos ja mandam sozinhos.");
        Console.WriteLine("Nada e' enviado a eles. Durante a captura: mexa o mouse, role a");
        Console.WriteLine("rodinha, digite, aperte botoes do controle, ligue o headset.");
        Console.Write("Capturar agora? [S/n] ");

        string answer = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
        return answer is "n" or "nao" or "no" ? 0 : 30;
    }

    private static List<string> WriteReport(ScanReport report, ScanOptions options)
    {
        // Sem dados pessoais, como o "Mandar meu aparelho" (decidido em 2026-11-12): nome do arquivo so com data e hora.
        string baseName = $"joulescan_{report.GeneratedLocal:yyyyMMdd-HHmmss}";
        var clean = ScanPrivacy.Redact(report);

        string text = TextReport.Render(clean);
        string json = JsonSerializer.Serialize(clean, JsonOptions);

        var written = new List<string>();
        foreach (string dir in CandidateDirectories(options))
        {
            string? txtPath = TryWrite(dir, baseName + ".txt", text);
            if (txtPath is null) continue;

            written.Add(txtPath);
            string? jsonPath = TryWrite(dir, baseName + ".json", json);
            if (jsonPath is not null) written.Add(jsonPath);
            break;
        }

        return written;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static IEnumerable<string> CandidateDirectories(ScanOptions options)
    {
        if (options.OutDir is { Length: > 0 } requested) yield return requested;

        string? exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? "");
        if (!string.IsNullOrWhiteSpace(exeDir)) yield return exeDir;

        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(desktop)) yield return desktop;

        yield return Path.GetTempPath();
    }

    private static string? TryWrite(string dir, string name, string content)
    {
        try
        {
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return path;
        }
        catch
        {
            return null;
        }
    }

    private static void OpenInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch { /* abrir a pasta e' conveniencia, nao requisito */ }
    }

    private static void WaitIfInteractive(ScanOptions options)
    {
        if (options.Quiet) return;
        Console.WriteLine();
        Console.Write("Pressione Enter para fechar. ");
        try { Console.ReadLine(); } catch { }
    }

    private static void Step(string what) => Console.WriteLine($">> {what}...");

    private static void PrintHelp()
    {
        Console.WriteLine($"JouleScan {ScanInfo.Version} - coletor de dados de bateria de perifericos");
        Console.WriteLine();
        Console.WriteLine("  JouleScan.exe                  interativo (pergunta sobre a captura)");
        Console.WriteLine("  JouleScan.exe --listen 60      captura passiva de 60s, sem perguntar");
        Console.WriteLine("  JouleScan.exe --no-listen      so o inventario, sem captura");
        Console.WriteLine("  JouleScan.exe --only 1D57:FA60 captura so esse VID:PID (repetivel com virgula)");
        Console.WriteLine("  JouleScan.exe --out C:\\pasta    onde gravar os arquivos");
        Console.WriteLine("  JouleScan.exe --quiet          sem perguntas nem pausa no fim");
        Console.WriteLine("  JouleScan.exe --selftest       testa as partes puras, sem tocar em hardware");
        Console.WriteLine();
        Console.WriteLine("Nada e' enviado aos aparelhos sem a sua licenca; com --quiet, nada e' enviado.");
    }
}

internal sealed class ScanOptions
{
    public bool Help { get; private set; }
    public bool SelfTest { get; private set; }
    public bool Quiet { get; private set; }
    public bool NoListen { get; private set; }
    public int? ListenSeconds { get; private set; }
    public string? OutDir { get; private set; }
    public HashSet<string>? OnlyHardwareIds { get; private set; }

    public static ScanOptions Parse(string[] args)
    {
        var options = new ScanOptions();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i].ToLowerInvariant();
            switch (arg)
            {
                case "-h" or "--help" or "/?":
                    options.Help = true;
                    break;

                case "--selftest":
                    options.SelfTest = true;
                    break;

                case "--quiet" or "-q":
                    options.Quiet = true;
                    break;

                case "--no-listen":
                    options.NoListen = true;
                    break;

                case "--listen" or "-l":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int seconds))
                        options.ListenSeconds = Math.Clamp(seconds, 0, 600);
                    break;

                case "--out" or "-o":
                    if (i + 1 < args.Length) options.OutDir = args[++i];
                    break;

                case "--only":
                    if (i + 1 < args.Length) options.OnlyHardwareIds = ParseIds(args[++i]);
                    break;
            }
        }

        return options;
    }

    internal static HashSet<string> ParseIds(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
           .Select(s => s.Replace("-", ":").ToUpperInvariant())
           .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
