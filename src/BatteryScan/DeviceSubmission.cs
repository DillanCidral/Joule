using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Scan;

/// <summary>
/// "Mandar meu aparelho" (decidido em 2026-11-04): a coleta do JouleScan
/// rodando dentro do Joule, sem dados pessoais, num arquivo que vira relato
/// no GitHub ou anexo de e-mail. Sem servidor: quem manda e' a pessoa.
/// </summary>
public static class DeviceSubmission
{
    public const int ListenSeconds = 30;

    /// <summary>
    /// Tudo menos a escuta. Os providers vem da leitura que o Joule ja fez:
    /// ler de novo so para o arquivo repetiria perguntas ao aparelho.
    /// </summary>
    public static (ScanReport Report, IReadOnlyList<HidCollection> Collections) Inventory(
        IEnumerable<DeviceEntry> lastReading, IReadOnlyList<string> reportedNames)
    {
        var report = new ScanReport { Tool = "Joule", ReportedDevices = reportedNames.ToList() };
        var warnings = report.Warnings;
        report.System = SystemScan.Collect(warnings);
        var collections = HidInventory.Scan();
        report.Hid = HidScan.Collect(collections, warnings);
        report.DeviceNodes = DeviceNodeScan.Collect(warnings, out int skipped);
        report.DeviceNodesSkipped = skipped;
        report.Bluetooth = BluetoothScan.Collect(warnings);
        report.XInput = XInputScan.Collect(warnings);
        report.Providers = ProvidersScan.From(lastReading).ToList();
        report.Candidates = CandidateFinder.Find(report);
        return (report, collections);
    }

    /// <summary>A escuta passiva. "Escutar de novo" chama so isto, com as mesmas colecoes.</summary>
    public static void Listen(ScanReport report, IReadOnlyList<HidCollection> collections,
        Action<int, int> tick, CancellationToken cancel)
    {
        report.Capture = PassiveCapture.Run(collections, ListenSeconds, null, report.Warnings, tick, cancel);
        report.Candidates = CandidateFinder.Find(report);
    }

    /// <summary>Uma linha da tela "O que achei".</summary>
    public sealed record Found(string Name, string Detail, bool Talked);

    /// <summary>Os que o Joule ja le (com o valor) e os que ele achou sem saber ler.</summary>
    public static (List<Found> Read, List<Found> Unknown) Summarize(ScanReport r)
    {
        var read = r.Providers.Where(p => p.Value != "--")
            .Select(p => new Found(p.DisplayName, p.Value, true)).ToList();
        var readIds = r.Providers.Where(p => p.Value != "--").Select(p => p.HardwareId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var talked = (r.Capture?.Groups ?? new()).Where(g => g.Count > 0).Select(g => g.HardwareId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unknown = r.Hid
            .Where(d => d.Transport != "virtual" && !readIds.Contains(d.HardwareId))
            .Select(d => new Found(
                d.KnownName ?? (d.Product.Length > 0 ? d.Product : d.HardwareId),
                HidUsages.IsBluetooth(d.Transport) ? "Bluetooth" : "Receptor USB ou cabo",
                talked.Contains(d.HardwareId)))
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(f => f.Talked).First())
            .OrderByDescending(f => f.Talked).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return (read, unknown);
    }

    /// <summary>O arquivo que vai: o texto para ler e, no fim, o JSON para programar.</summary>
    public static string Render(ScanReport report)
    {
        var clean = ScanPrivacy.Redact(report);
        return TextReport.Render(clean)
            + "\n\n================================================================\n"
            + " dados para o programa (JSON)\n"
            + "================================================================\n"
            + JsonSerializer.Serialize(clean, ScanPrivacy.Json);
    }

    public static string Save(string content, DateTime when)
    {
        string dir = Path.Combine(AppPaths.DataDir, "envios");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"joule-aparelho-{when:yyyy-MM-dd-HHmmss}.txt");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    public static string Title(IReadOnlyList<string> names) =>
        names.Count == 0 ? "Aparelho que o Joule não reconhece" : $"Aparelho: {string.Join(", ", names)}";

    public static string Body(IReadOnlyList<string> names, string fileName, string version) =>
        $"Aparelho(s): {(names.Count == 0 ? "(não informado)" : string.Join(", ", names))}\n" +
        $"Joule {version}\n\n" +
        $"Arquivo da captura: {fileName}\n(arraste o arquivo para esta caixa antes de enviar)";

    // Sem labels na URL: quem nao tem permissao de etiqueta recebe 404 do GitHub (docs, 2026-11-07).
    public static string GitHubUrl(string newIssueUrl, string title, string body) =>
        $"{newIssueUrl}?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";

    // shortcut: formatos de Gmail e Outlook sem pagina oficial (uso comum, conferido em foruns em 2026-11-07); se pararem, o "Copiar endereco" segue valendo.
    public static string GmailUrl(string to, string subject, string body) =>
        $"https://mail.google.com/mail/?view=cm&to={Uri.EscapeDataString(to)}&su={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";

    public static string OutlookUrl(string to, string subject, string body) =>
        $"https://outlook.live.com/mail/0/deeplink/compose?to={Uri.EscapeDataString(to)}&subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";

    public static string MailtoUrl(string to, string subject, string body) =>
        $"mailto:{to}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
}

/// <summary>
/// Tira o que identifica a pessoa antes de o arquivo sair do PC (decidido em
/// 2026-11-04): numero de serie, endereco Bluetooth, nome do PC, nome de
/// usuario e os Bluetooth que nao sao periferico (celular, caixa de som, carro).
/// Troca em todo texto do relatorio, porque esses dados tambem aparecem
/// dentro de caminhos e ids do Windows.
/// </summary>
public static class ScanPrivacy
{
    public const string Mark = "[tirado]";

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Regex ColonAddress = new(@"(?<![0-9A-Fa-f])(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}(?![0-9A-Fa-f])", RegexOptions.Compiled);
    // 12 hex soltos: endereco Bluetooth dentro de ids (BTHLE\Dev_..., ..._a0b1c2d3e4f5). O '-' fora: ultimo bloco de GUID.
    private static readonly Regex BareAddress = new(@"(?<![0-9A-Fa-f\-])[0-9A-Fa-f]{12}(?![0-9A-Fa-f])", RegexOptions.Compiled);
    private static readonly Regex UserFolder = new(@"(?i)(\\Users\\)[^\\""\s]+", RegexOptions.Compiled);

    /// <summary>Headset, viva-voz e fone ficam; celular, PC, caixa de som e carro saem.</summary>
    internal static bool IsPeripheral(uint cod)
    {
        uint major = (cod >> 8) & 0x1F, minor = (cod >> 2) & 0x3F;
        return major == 0x05 || (major == 0x04 && minor is 0x01 or 0x02 or 0x06);
    }

    public static ScanReport Redact(ScanReport original)
    {
        // Copia pelo JSON: o relatorio de quem chamou continua inteiro.
        var node = JsonSerializer.SerializeToNode(original, Json)!;
        var r = node.Deserialize<ScanReport>(Json)!;

        var words = new List<string>();
        void Word(string? s, int min) { if (s is { } t && t.Trim().Length >= min) words.Add(t.Trim()); }

        Word(r.System.MachineName, 4);
        Word(r.System.UserName, 4); // tambem fora de caminhos: "Fone de <usuario>" (revisao de 2026-11-12)
        foreach (var radio in r.Bluetooth.Radios) Word(radio.Name, 4); // o radio costuma ter o nome do PC
        foreach (var d in r.Hid)
            // Serie curta ou repetida ("0000") apagaria bytes da captura: so o campo sai.
            if (d.Serial.Trim().Length >= 6 && d.Serial.Distinct().Count() > 2) Word(d.Serial, 6);
        foreach (var d in r.Bluetooth.Devices.Where(d => !IsPeripheral(d.ClassOfDevice))) Word(d.Name, 4);

        r.Bluetooth.Devices.RemoveAll(d => !IsPeripheral(d.ClassOfDevice));
        r.System.MachineName = Mark;
        r.System.UserName = Mark;
        foreach (var d in r.Hid) if (d.Serial.Length > 0) d.Serial = Mark;

        var clean = JsonSerializer.SerializeToNode(r, Json)!;
        Walk(clean, words.OrderByDescending(w => w.Length).ToList());
        return clean.Deserialize<ScanReport>(Json)!;
    }

    internal static string Scrub(string s, IReadOnlyList<string> words)
    {
        foreach (var w in words) s = s.Replace(w, Mark, StringComparison.OrdinalIgnoreCase);
        s = ColonAddress.Replace(s, Mark);
        s = BareAddress.Replace(s, Mark);
        return UserFolder.Replace(s, "$1" + Mark);
    }

    private static void Walk(JsonNode? node, IReadOnlyList<string> words)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (obj[key] is JsonValue v && v.TryGetValue(out string? s)) obj[key] = Scrub(s, words);
                    else Walk(obj[key], words);
                }
                break;
            case JsonArray arr:
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonValue v && v.TryGetValue(out string? s)) arr[i] = Scrub(s, words);
                    else Walk(arr[i], words);
                }
                break;
        }
    }
}
