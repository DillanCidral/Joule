using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BatteryTray.Core;
using BatteryTray.Interop;
using BatteryTray.Ui;

namespace BatteryTray.Scan;

/// <summary>
/// "Enviar um aparelho" (decidido em 2026-11-04; era "Mandar meu aparelho" ate 2026-11-14): a coleta do JouleScan
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
        // Nome com o canal ("Joule Teste") e versao: o arquivo solto ainda diz de onde veio.
        var report = new ScanReport { Tool = AppInfo.Name, AppVersion = AppInfo.Version, ReportedDevices = reportedNames.ToList() };
        var warnings = report.Warnings;
        report.System = SystemScan.Collect(warnings);
        var collections = HidInventory.Scan();
        report.Hid = HidScan.Collect(collections, warnings);
        report.DeviceNodes = DeviceNodeScan.Collect(warnings, out int skipped);
        report.DeviceNodesSkipped = skipped;
        int all = report.DeviceNodes.Count;
        report.DeviceNodes = KeepForBattery(report.DeviceNodes, report.Hid);
        report.DeviceNodesSkipped += all - report.DeviceNodes.Count;
        report.Bluetooth = BluetoothScan.Collect(warnings);
        report.XInput = XInputScan.Collect(warnings);
        report.Providers = ProvidersScan.From(lastReading).ToList();
        report.Candidates = CandidateFinder.Find(report);
        return (report, collections);
    }

    /// <summary>
    /// So devnodes que podem ter a ver com bateria (revisao de 2026-11-14): os
    /// de HID, Bluetooth, audio, controle e bateria, os que guardam bateria e o
    /// pai USB de um aparelho HID. Processador, rede, impressora e sistema
    /// ficam fora: nao ajudam a analise e levavam nomes do PC.
    /// </summary>
    internal static List<DeviceNodeInfo> KeepForBattery(IEnumerable<DeviceNodeInfo> nodes, IEnumerable<HidDeviceInfo> hid)
    {
        var usb = hid.Select(d => d.HardwareId).Where(id => id.Length == 9 && id[4] == ':')
            .Select(id => $"VID_{id[..4]}&PID_{id[5..]}").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return nodes.Where(n => DeviceNodeScan.BatteryRelated(n) || n.BluetoothBattery is not null || n.BatteryProperties.Count > 0
            || usb.Any(u => n.InstanceId.Contains(u, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>A escuta passiva. "Escutar de novo" chama so isto, com as mesmas colecoes, e soma a escuta nova a anterior.</summary>
    public static void Listen(ScanReport report, IReadOnlyList<HidCollection> collections,
        Action<int, int> tick, CancellationToken cancel)
    {
        report.Capture = PassiveCapture.Merge(report.Capture, PassiveCapture.Run(collections, ListenSeconds, null, report.Warnings, tick, cancel));
        report.Candidates = CandidateFinder.Find(report);
    }

    /// <summary>Uma linha da tela "Aparelhos encontrados".</summary>
    public sealed record Found(string Name, string Detail, bool Talked, string HardwareId = "");

    /// <summary>Os que o Joule ja le (com o valor) e os que ele achou sem saber ler.</summary>
    public static (List<Found> Read, List<Found> Unknown) Summarize(ScanReport r)
    {
        var read = r.Providers.Where(p => p.Value != "--")
            .Select(p => new Found(p.DisplayName, p.Value, true, p.HardwareId)).ToList();
        var readIds = r.Providers.Where(p => p.Value != "--").Select(p => p.HardwareId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var talked = (r.Capture?.Groups ?? new()).Where(g => g.Count > 0).Select(g => g.HardwareId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unknown = r.Hid
            .Where(d => d.Transport != "virtual" && !readIds.Contains(d.HardwareId))
            .Select(d => new Found(
                d.KnownName ?? (d.Product.Length > 0 ? d.Product : d.HardwareId),
                HidUsages.IsBluetooth(d.Transport) ? "Bluetooth" : L.T("Receptor USB", "USB receiver"),
                talked.Contains(d.HardwareId), d.HardwareId))
            .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(f => f.Talked).First())
            .OrderByDescending(f => f.Talked).ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return (read, unknown);
    }

    /// <summary>Linha que separa o texto do JSON; o `--verificar` do JouleScan procura por ela.</summary>
    public const string JsonMark = " dados para o programa (JSON)";

    /// <summary>
    /// O arquivo que vai: o resumo para quem envia (na lingua do app), o alvo
    /// para a analise, o texto tecnico e, no fim, o JSON para programar.
    /// </summary>
    public static string Render(ScanReport report)
    {
        var clean = ScanPrivacy.Redact(report);
        return ForSender(clean) + ForAnalysis(clean) + TextReport.Render(clean)
            + "\n\n================================================================\n"
            + JsonMark + "\n"
            + "================================================================\n"
            + JsonSerializer.Serialize(clean, ScanPrivacy.Json);
    }

    private const string Rule = "================================================================";

    /// <summary>Quem envia abre no Bloco de Notas: o que o arquivo e', o que leva e o que saiu.</summary>
    internal static string ForSender(ScanReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Rule);
        sb.AppendLine(L.T($" ENVIO DE APARELHO · {r.Tool} {r.AppVersion} · formato {r.Version}", $" DEVICE SUBMISSION · {r.Tool} {r.AppVersion} · format {r.Version}"));
        sb.AppendLine(Rule);
        sb.AppendLine(L.T(" PARA QUEM ENVIA", " FOR THE SENDER"));
        sb.AppendLine(L.T(" Este arquivo ajuda o Joule a aprender a ler a bateria de um aparelho.", " This file helps Joule learn to read a device’s battery."));
        sb.AppendLine(L.T(" Ele só sai do seu PC se você anexar.", " It only leaves your PC if you attach it."));
        sb.AppendLine();
        foreach (var t in Pointed(r))
            sb.AppendLine(L.T($" Aparelho apontado: {t.Name} ({t.Id}), {t.Link}, {(t.Talked ? "enviou dados" : "não enviou dados")}",
                $" Device pointed out: {t.Name} ({t.Id}), {t.Link}, {(t.Talked ? "sent data" : "sent no data")}"));
        foreach (var (name, pct) in r.ReportedBattery ?? new())
            sb.AppendLine(L.T($" Bateria informada: {name}: {pct}%", $" Battery reported: {name}: {pct}%"));
        int devices = r.Hid.Count(d => d.Transport != "virtual");
        sb.AppendLine(L.T($" Leva: {devices} aparelhos que o Windows lista, {r.Capture?.Seconds ?? 0} s de dados enviados", $" Includes: {devices} devices Windows lists, {r.Capture?.Seconds ?? 0} s of data they sent,"));
        sb.AppendLine(L.T("       por eles, versões do Windows e do Joule.", "       the Windows and Joule versions."));
        sb.AppendLine(L.T(" Removido: número de série, endereço Bluetooth, nome do computador,", " Removed: serial number, Bluetooth address, computer name,"));
        sb.AppendLine(L.T("       nome de usuário, celulares e caixas de som.", "       user name, phones and speakers."));
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>Para quem analisa o envio (sempre em portugues): o aparelho apontado e os bytes que batem com a bateria informada.</summary>
    internal static string ForAnalysis(ScanReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Rule);
        sb.AppendLine(" PARA A ANALISE (tecnico)");
        sb.AppendLine(Rule);
        var pointed = Pointed(r);
        if (pointed.Count == 0) sb.AppendLine(" alvo    : nenhum marcado");
        foreach (var t in pointed) sb.AppendLine($" alvo    : {t.Id}  {t.Name}  [{t.Link}]  falou na escuta: {(t.Talked ? "sim" : "nao")}");
        foreach (var (name, pct) in r.ReportedBattery ?? new())
        {
            var hits = LevelMatches(r, pct);
            sb.AppendLine($" bateria : {name} = {pct}%  ->  {(hits.Count == 0 ? "nenhum byte constante igual a " + pct : "bytes constantes iguais: " + string.Join("; ", hits))}");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    internal sealed record Target(string Id, string Name, string Link, bool Talked);

    internal static List<Target> Pointed(ScanReport r)
    {
        var talked = (r.Capture?.Groups ?? new()).Where(g => g.Count > 0).Select(g => g.HardwareId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (r.PointedDevices ?? new()).Select(id =>
        {
            var d = r.Hid.FirstOrDefault(h => h.HardwareId.Equals(id, StringComparison.OrdinalIgnoreCase));
            string name = d?.KnownName ?? (d is { Product.Length: > 0 } ? d.Product : L.T("(sem nome)", "(no name)"));
            string link = d is not null && HidUsages.IsBluetooth(d.Transport) ? "Bluetooth" : L.T("receptor USB", "USB receiver");
            return new Target(id, name, link, talked.Contains(id));
        }).ToList();
    }

    /// <summary>Bytes que ficaram parados no valor que a pessoa leu no programa da marca, aparelho apontado primeiro.</summary>
    internal static List<string> LevelMatches(ScanReport r, int pct)
    {
        var pointed = (r.PointedDevices ?? new()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (r.Capture?.Groups ?? new())
            .OrderByDescending(g => pointed.Contains(g.HardwareId))
            .SelectMany(g => g.Bytes.Where(b => b.Offset > 0 && b.Constant && b.Min == pct)
                .Select(b => $"{g.HardwareId} rel 0x{g.ReportId:X2} byte[{b.Offset}]"))
            .Take(12).ToList();
    }

    /// <summary>Grava o arquivo. Com `path` (voltar e revisar de novo), regrava o mesmo em vez de criar outro.</summary>
    public static string Save(string content, DateTime when, string? path = null)
    {
        string dir = Path.Combine(AppPaths.DataDir, "envios");
        Directory.CreateDirectory(dir);
        // Calendario fixo: no Windows persa o nome saia "1405-08-26" (#137).
        path ??= Path.Combine(dir, $"joule-aparelho-{when.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)}.txt");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    // Relato na lingua do app (2026-11-16); o nome do arquivo joule-aparelho- nao muda, e e' por ele que um envio e' reconhecido.
    public static string Title(IReadOnlyList<string> names) =>
        names.Count == 0 ? L.T("Aparelho que o Joule não reconhece", "Device Joule doesn’t recognize") : L.T($"Aparelho: {string.Join(", ", names)}", $"Device: {string.Join(", ", names)}");

    /// <summary>
    /// A lista "Achei estes aparelhos" no texto (2026-11-16): nos envios da 0.2.0, metade chegou sem o
    /// arquivo e um terco sem o nome; com isto da para saber o aparelho e a familia sem o arquivo.
    /// shortcut: 12 linhas para caber na URL do GitHub (~8 mil caracteres); o resto fica so no arquivo.
    /// </summary>
    internal static string Listed(ScanReport r)
    {
        var (read, unknown) = Summarize(r);
        return string.Concat(read.Select(f => L.T($"Já lê: {f.HardwareId} {f.Name}, {f.Detail}", $"Already reads: {f.HardwareId} {f.Name}, {f.Detail}") + "\n")
            .Concat(unknown.Select(f => (f.Talked
                ? L.T($"Encontrado: {f.HardwareId} {f.Name}, enviou dados", $"Found: {f.HardwareId} {f.Name}, sent data")
                : L.T($"Encontrado: {f.HardwareId} {f.Name}", $"Found: {f.HardwareId} {f.Name}")) + "\n"))
            .Take(12));
    }

    /// <summary>Texto do relato. Com o relatorio, leva o aparelho apontado e a bateria informada: da para triar sem abrir o arquivo.</summary>
    public static string Body(IReadOnlyList<string> names, string fileName, string version, ScanReport? report = null, bool email = false)
    {
        string devices = names.Count == 0 ? L.T("(não informado)", "(not given)") : string.Join(", ", names);
        var sb = new StringBuilder();
        sb.Append(L.T($"Aparelho(s): {devices}", $"Device(s): {devices}")).Append('\n');
        sb.Append($"Joule {version}\n");
        if (report is not null)
        {
            foreach (var t in Pointed(report))
                sb.Append(t.Talked ? L.T($"Apontado: {t.Id} {t.Name}, {t.Link}, enviou dados", $"Pointed out: {t.Id} {t.Name}, {t.Link}, sent data")
                                   : L.T($"Apontado: {t.Id} {t.Name}, {t.Link}, não enviou dados", $"Pointed out: {t.Id} {t.Name}, {t.Link}, sent no data")).Append('\n');
            foreach (var b in report.ReportedBattery ?? new())
                sb.Append(L.T($"Bateria informada: {b.Key}: {b.Value}%", $"Battery reported: {b.Key}: {b.Value}%")).Append('\n');
            sb.Append(Listed(report));
        }
        sb.Append('\n').Append(L.T($"Arquivo da captura: {fileName}", $"Capture file: {fileName}")).Append('\n');
        sb.Append(email ? L.T("(arraste o arquivo para este e-mail antes de enviar)", "(drag the file into this email before sending)")
                        : L.T("(arraste o arquivo para esta caixa antes de enviar)", "(drag the file into this box before sending)"));
        return sb.ToString();
    }

    // Busca de pedidos abertos pelo nome (2026-11-16): o app so abre o navegador, sem codigo de internet (regra 9).
    // shortcut: busca so pelo primeiro aparelho digitado; com dois, a pessoa troca o texto na propria busca.
    public static string SearchUrl(string issuesUrl, string name) =>
        $"{issuesUrl}?q={Uri.EscapeDataString("is:issue is:open " + name)}";

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
    // Nome de dono que o celular poe no fone: "Fones de Maria", "Maria's AirPods" (revisao de 2026-11-14).
    private static readonly Regex OwnerName = new(@"\b(?:de|do|da|dos|das)\s+(?<n>\p{Lu}[\p{L}\-]+)|(?<n>\p{Lu}[\p{L}\-]+)['’]s\b", RegexOptions.Compiled);

    /// <summary>Os nomes proprios que aparecem como dono em nomes de aparelho.</summary>
    internal static IEnumerable<string> Owners(string? name) =>
        name is null ? Array.Empty<string>() : OwnerName.Matches(name).Select(m => m.Groups["n"].Value).Where(n => n.Length >= 3 && !NotOwners.Contains(n));

    // "Controle do Xbox" nao tem dono: marca e plataforma ficam.
    private static readonly HashSet<string> NotOwners = new(StringComparer.OrdinalIgnoreCase)
        { "Xbox", "Mac", "Apple", "Microsoft", "Windows", "PlayStation", "Logitech", "Razer", "Samsung", "Sony", "JBL", "Bluetooth", "Gaming" };

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
        // Dono: so palavra inteira e com a mesma caixa, para "Ana" nao apagar "Analog" nos dados.
        var owners = r.Bluetooth.Devices.Select(d => d.Name).Concat(r.DeviceNodes.Select(n => n.Name))
            .SelectMany(Owners).Distinct().ToList();
        Regex? ownerWords = owners.Count == 0 ? null : new Regex(@"\b(?:" + string.Join("|", owners.Select(Regex.Escape)) + @")\b");

        r.Bluetooth.Devices.RemoveAll(d => !IsPeripheral(d.ClassOfDevice));
        r.System.MachineName = Mark;
        r.System.UserName = Mark;
        foreach (var d in r.Hid) if (d.Serial.Length > 0) d.Serial = Mark;

        var clean = JsonSerializer.SerializeToNode(r, Json)!;
        Walk(clean, words.OrderByDescending(w => w.Length).ToList(), ownerWords);
        return clean.Deserialize<ScanReport>(Json)!;
    }

    /// <summary>
    /// Conferencia de quem recebe (`JouleScan --verificar`): o que ainda parece
    /// dado pessoal num arquivo que ja deveria ter passado pelo Redact.
    /// </summary>
    public static List<string> Leaks(string text, ScanReport r)
    {
        // Os valores do JSON sem o escape das barras: "C:\\Users\\x" escapado escaparia da regex.
        text += "\n" + string.Join("\n", Strings(JsonSerializer.SerializeToNode(r, Json)));
        var found = new List<string>();
        if (r.System.MachineName is { Length: > 0 } m && m != Mark) found.Add("nome do PC no campo machineName");
        if (r.System.UserName is { Length: > 0 } u && u != Mark) found.Add("nome de usuario no campo userName");
        foreach (var d in r.Hid.Where(d => d.Serial.Length > 0 && d.Serial != Mark)) found.Add($"numero de serie de {d.HardwareId}");
        foreach (var d in r.Bluetooth.Devices.Where(d => !IsPeripheral(d.ClassOfDevice))) found.Add($"Bluetooth que nao e' periferico (classe 0x{d.ClassOfDevice:X6})");
        // So conta o trecho, nunca o valor: o resumo nao pode repetir o dado vazado.
        int addr = ColonAddress.Matches(text).Count + BareAddress.Matches(text).Count;
        if (addr > 0) found.Add($"{addr} trecho(s) com cara de endereco Bluetooth");
        int folders = UserFolder.Matches(text).Count(x => !text.AsSpan(x.Index + x.Groups[1].Length).StartsWith(Mark));
        if (folders > 0) found.Add($"{folders} caminho(s) com pasta de usuario");
        int owned = r.Bluetooth.Devices.Count(d => Owners(d.Name).Any());
        if (owned > 0) found.Add($"{owned} nome(s) Bluetooth com cara de dono (\"Fones de Maria\")");
        return found;
    }

    internal static string Scrub(string s, IReadOnlyList<string> words, Regex? owners = null)
    {
        foreach (var w in words) s = s.Replace(w, Mark, StringComparison.OrdinalIgnoreCase);
        if (owners is not null) s = owners.Replace(s, Mark);
        s = ColonAddress.Replace(s, Mark);
        s = BareAddress.Replace(s, Mark);
        return UserFolder.Replace(s, "$1" + Mark);
    }

    private static IEnumerable<string> Strings(JsonNode? node) => node switch
    {
        JsonObject obj => obj.SelectMany(p => Strings(p.Value)),
        JsonArray arr => arr.SelectMany(Strings),
        JsonValue v when v.TryGetValue(out string? s) => new[] { s },
        _ => Array.Empty<string>(),
    };

    private static void Walk(JsonNode? node, IReadOnlyList<string> words, Regex? owners)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (obj[key] is JsonValue v && v.TryGetValue(out string? s)) obj[key] = Scrub(s, words, owners);
                    else Walk(obj[key], words, owners);
                }
                break;
            case JsonArray arr:
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonValue v && v.TryGetValue(out string? s)) arr[i] = Scrub(s, words, owners);
                    else Walk(arr[i], words, owners);
                }
                break;
        }
    }
}
