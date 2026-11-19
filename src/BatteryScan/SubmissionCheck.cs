using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BatteryTray.Scan;

/// <summary>
/// `JouleScan --verificar arquivo [--origem texto]` (2026-11-14): confere um
/// envio do "Mandar meu aparelho" que chegou por e-mail ou pelo GitHub e
/// imprime o resumo com as linhas prontas para o catalogo de aparelhos. Nao
/// toca em hardware. O arquivo vem de
/// estranhos: so e' lido como JSON, e todo texto dele sai limpo e curto.
/// Saida: 0 = ok, 2 = nao e' um envio legivel, 3 = sobrou dado pessoal.
/// </summary>
internal static class SubmissionCheck
{
    public static int Run(string path, string origin)
    {
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) { Console.WriteLine($"ERRO: nao li o arquivo: {ex.Message}"); return 2; }

        var (report, readable) = Parse(text);
        if (report is null) { Console.WriteLine("ERRO: nao achei o JSON de um envio do Joule nesse arquivo."); return 2; }

        var leaks = ScanPrivacy.Leaks(readable, report);
        Console.Write(Summary(report, leaks, Clean(origin), Clean(Path.GetFileName(path))));
        return leaks.Count > 0 ? 3 : 0;
    }

    /// <summary>O relatorio e a parte de texto. Aceita o .txt do Joule (texto + JSON) e o .json do JouleScan.</summary>
    internal static (ScanReport? Report, string Readable) Parse(string text)
    {
        int mark = text.IndexOf(DeviceSubmission.JsonMark, StringComparison.Ordinal);
        string readable = mark >= 0 ? text[..mark] : "";
        string json = mark >= 0 ? text[mark..] : text;
        int start = json.IndexOf('{');
        if (start < 0) return (null, readable);
        try
        {
            var r = JsonSerializer.Deserialize<ScanReport>(json[start..], ScanPrivacy.Json);
            return (r is { Hid: not null, System: not null } ? r : null, readable);
        }
        catch (JsonException) { return (null, readable); }
    }

    internal static string Summary(ScanReport r, IReadOnlyList<string> leaks, string origin, string file)
    {
        var sb = new StringBuilder();
        string date = $"{r.GeneratedLocal:yyyy-MM-dd}";
        sb.AppendLine($"# Envio {file}");
        sb.AppendLine($"- origem: {origin}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- gerado: {r.GeneratedLocal:yyyy-MM-dd HH:mm} por {Clean($"{r.Tool} {r.AppVersion}")} (formato {Clean(r.Version)})");
        sb.AppendLine($"- Windows: {Clean(r.System.OsCaption.Length > 0 ? r.System.OsCaption : r.System.OsVersion)} {Clean(r.System.Architecture)}");
        sb.AppendLine(leaks.Count == 0
            ? "- privacidade: OK"
            : $"- privacidade: SOBROU DADO PESSOAL ({string.Join("; ", leaks)})");
        var named = r.ReportedDevices ?? new();
        sb.AppendLine($"- aparelhos que a pessoa citou: {(named.Count == 0 ? "nenhum" : string.Join(", ", named.Select(Clean)))}");
        var pointed = DeviceSubmission.Pointed(r);
        sb.AppendLine($"- aparelho apontado: {(pointed.Count == 0 ? "nenhum" : string.Join(", ", pointed.Select(t => $"{Clean(t.Id)} {Clean(t.Name)} ({(t.Talked ? "falou" : "quieto")})")))}");
        foreach (var (name, pct) in r.ReportedBattery ?? new())
        {
            var hits = DeviceSubmission.LevelMatches(r, pct);
            sb.AppendLine($"- bateria informada: {Clean(name)} = {pct}% ({(hits.Count == 0 ? "nenhum byte constante igual" : Clean(string.Join("; ", hits)))})");
        }
        var read = r.Providers.Where(p => p.Value is { Length: > 0 } v && v != "--").ToList();
        sb.AppendLine($"- o Joule ja le: {(read.Count == 0 ? "nenhum" : string.Join(", ", read.Select(p => $"{Clean(p.DisplayName)} ({Clean(p.Value)})")))}");
        sb.AppendLine($"- escuta: {r.Capture?.Seconds ?? 0} s, {r.Capture?.Groups.Count(g => g.Count > 0) ?? 0} formato(s) chegaram");
        foreach (var c in r.Candidates) sb.AppendLine($"- pista [{Clean(c.Confidence)}]: {Clean(c.Device)}: {Clean(c.What)}");
        if (r.Warnings.Count > 0) sb.AppendLine($"- avisos da coleta: {r.Warnings.Count}");

        // Com dado pessoal sobrando, nada vai para o catalogo: o arquivo e' conferido a mao.
        if (leaks.Count > 0) return sb.AppendLine().AppendLine("Catalogo pulado: conferir o arquivo a mao.").ToString();

        var readIds = read.Select(p => p.HardwareId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var talked = (r.Capture?.Groups ?? new()).Where(g => g.Count > 0).Select(g => g.HardwareId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        sb.AppendLine();
        sb.AppendLine("## Linhas do catalogo");
        var pointedIds = (r.PointedDevices ?? new()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        sb.AppendLine("| data | origem | VID:PID | nome | ligacao | declara bateria | falou na escuta | Joule ja le | apontado |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var g in r.Hid.Where(d => d.Transport != "virtual").GroupBy(d => d.HardwareId, StringComparer.OrdinalIgnoreCase))
        {
            var d = g.First();
            string name = d.KnownName ?? (d.Product.Length > 0 ? d.Product : "(sem nome)");
            bool battery = g.Any(x => x.DeclaredBattery.Count > 0);
            sb.AppendLine($"| {date} | {origin} | {Clean(g.Key)} | {Clean(name)} | {(HidUsages.IsBluetooth(d.Transport) ? "Bluetooth" : "USB")} | {(battery ? "sim" : "nao")} | {(talked.Contains(g.Key) ? "sim" : "nao")} | {(readIds.Contains(g.Key) ? "sim" : "nao")} | {(pointedIds.Contains(g.Key) ? "sim" : "nao")} |");
        }
        return sb.ToString();
    }

    /// <summary>Uma linha, sem controle nem '|', no maximo 80 caracteres: o texto veio de fora e vira tabela.</summary>
    internal static string Clean(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var t = new string(s.Select(c => char.IsControl(c) || c == '|' ? ' ' : c).ToArray()).Trim();
        return t.Length > 80 ? t[..80] + "…" : t;
    }
}
