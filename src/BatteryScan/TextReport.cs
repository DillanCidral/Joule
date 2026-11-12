using System.Text;

namespace BatteryTray.Scan;

/// <summary>
/// Renderiza o relatorio em texto. Dois leitores: quem rodou o coletor (quer
/// entender o que esta mandando) e eu (quero achar o caminho de bateria). Por
/// isso o resumo vem primeiro e o despejo cru vem depois.
/// O dump completo das propriedades de devnode fica so no .json.
/// </summary>
public static class TextReport
{
    public static string Render(ScanReport r)
    {
        var sb = new StringBuilder();

        sb.AppendLine("================================================================");
        sb.AppendLine($" BATTERY SCAN {r.Version} - coleta de bateria de perifericos");
        sb.AppendLine("================================================================");
        sb.AppendLine($" maquina : {r.System.MachineName}   usuario: {r.System.UserName}");
        sb.AppendLine($" sistema : {Or(r.System.OsCaption, r.System.OsVersion)}  {r.System.Architecture}");
        sb.AppendLine($" tipo    : {(r.System.HasSystemBattery ? "notebook (tem bateria interna)" : "desktop (sem bateria interna)")}");
        sb.AppendLine($" runtime : {r.System.DotNet}");
        sb.AppendLine($" gerado  : {r.GeneratedLocal:yyyy-MM-dd HH:mm:ss} (local)");
        if (r.ReportedDevices is { Count: > 0 } named)
            sb.AppendLine($" nao aparecem: {string.Join(", ", named)}");
        sb.AppendLine();
        sb.AppendLine(" Este arquivo e' somente leitura de metadados: nada foi escrito em");
        sb.AppendLine(" nenhum dispositivo, nenhum pareamento foi criado ou desfeito.");
        sb.AppendLine();

        RenderCandidates(sb, r);
        RenderVendorSoftware(sb, r);
        RenderHid(sb, r);
        RenderBluetooth(sb, r);
        RenderBatteryDevnodes(sb, r);
        RenderXInput(sb, r);
        RenderProviders(sb, r);
        RenderCapture(sb, r);
        RenderWarnings(sb, r);

        sb.AppendLine();
        sb.AppendLine("fim do relatorio. o .json ao lado tem tudo isto mais o dump completo");
        sb.AppendLine("das propriedades de devnode.");
        return sb.ToString();
    }

    private static void RenderCandidates(StringBuilder sb, ScanReport r)
    {
        Section(sb, "RESUMO: ONDE PARECE HAVER BATERIA");

        if (r.Candidates.Count == 0)
        {
            sb.AppendLine("  Nada encontrado. Isso ja e' informacao: significa que nenhum");
            sb.AppendLine("  periferico desta maquina expoe bateria por caminho conhecido.");
            sb.AppendLine();
            return;
        }

        foreach (var c in r.Candidates)
        {
            sb.AppendLine($"  [{c.Confidence}] {c.Device}");
            sb.AppendLine($"       {c.What}");
            sb.AppendLine($"       onde: {c.Path}");
        }

        sb.AppendLine();
    }

    private static void RenderVendorSoftware(StringBuilder sb, ScanReport r)
    {
        Section(sb, "SOFTWARE DE FABRICANTE RODANDO");

        if (r.System.VendorSoftwareRunning.Count == 0)
        {
            sb.AppendLine("  nenhum reconhecido.");
        }
        else
        {
            foreach (var p in r.System.VendorSoftwareRunning) sb.AppendLine($"  {p}");
            sb.AppendLine();
            sb.AppendLine("  Importa porque ha dispositivo que so responde bateria quando o");
            sb.AppendLine("  software do fabricante pergunta (foi o caso do Rapoo MT760).");
        }

        sb.AppendLine();
    }

    private static void RenderHid(StringBuilder sb, ScanReport r)
    {
        int collections = r.Hid.Sum(d => d.Collections.Count);
        Section(sb, $"DISPOSITIVOS HID ({r.Hid.Count} dispositivos, {collections} colecoes)");

        foreach (var d in r.Hid)
        {
            string name = Or(d.KnownName, Or(d.Product, "(sem nome)"));
            sb.AppendLine($"  {d.HardwareId}  {name}   [{d.Transport}]");

            if (d.Product.Length > 0 || d.Manufacturer.Length > 0)
                sb.AppendLine($"      produto: {Or(d.Product, "-")}   fabricante: {Or(d.Manufacturer, "-")}");
            if (d.Serial.Length > 0)
                sb.AppendLine($"      serie: {d.Serial}");
            sb.AppendLine($"      versao do firmware (bcdDevice): 0x{d.Version:X4}");

            if (d.DeclaredBattery.Count > 0)
            {
                foreach (var label in d.DeclaredBattery)
                    sb.AppendLine($"      *** DECLARA BATERIA: {label}");
            }

            foreach (var c in d.Collections)
            {
                string reserved = c.SystemReserved ? "  [reservada pelo sistema]" : "";
                sb.AppendLine($"      page {c.UsagePage} ({c.UsagePageName}) usage {c.Usage}  in={c.InputLen} out={c.OutputLen} feat={c.FeatureLen}{reserved}");

                foreach (var v in c.ValueCaps.Where(v => v.Battery is not null))
                {
                    sb.AppendLine($"          BATERIA: {v.ReportType} rid={v.ReportId} usage {v.UsagePage}/{v.Usage} " +
                                  $"bits={v.BitSize} count={v.ReportCount} logico={v.LogicalMin}..{v.LogicalMax}");
                    sb.AppendLine($"                   -> {v.Battery}");
                }

                if (c.CapsError is { Length: > 0 })
                    sb.AppendLine($"          (caps: {c.CapsError})");

                sb.AppendLine($"          path: {c.Path}");
            }

            sb.AppendLine();
        }
    }

    private static void RenderBluetooth(StringBuilder sb, ScanReport r)
    {
        Section(sb, "BLUETOOTH");

        if (r.Bluetooth.Error is { Length: > 0 })
        {
            sb.AppendLine($"  {r.Bluetooth.Error}");
            sb.AppendLine();
            return;
        }

        if (!r.Bluetooth.RadioPresent)
        {
            sb.AppendLine("  nenhum radio Bluetooth nesta maquina.");
            sb.AppendLine();
            return;
        }

        foreach (var radio in r.Bluetooth.Radios)
            sb.AppendLine($"  radio: {Or(radio.Name, "(sem nome)")}  {radio.Address}  fabricante=0x{radio.Manufacturer:X4}");

        sb.AppendLine();
        sb.AppendLine($"  dispositivos pareados ({r.Bluetooth.Devices.Count}) -- sem busca ativa:");

        if (r.Bluetooth.Devices.Count == 0)
        {
            sb.AppendLine("    nenhum.");
        }
        else
        {
            foreach (var d in r.Bluetooth.Devices.OrderByDescending(d => d.Connected).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                string state = d.Connected ? "CONECTADO" : d.Paired ? "pareado  " : "conhecido";
                sb.AppendLine($"    [{state}] {Or(d.Name, "(sem nome)"),-32} {d.Address}  {d.DeviceKind}");
                if (d.LastUsedUtc is { } used)
                    sb.AppendLine($"                usado pela ultima vez: {used.ToLocalTime():yyyy-MM-dd HH:mm}");
            }
        }

        sb.AppendLine();
    }

    private static void RenderBatteryDevnodes(StringBuilder sb, ScanReport r)
    {
        var withBattery = r.DeviceNodes
            .Where(n => n.BluetoothBattery is not null || n.BatteryProperties.Count > 0)
            .ToList();

        Section(sb, $"DEVNODES COM PROPRIEDADE DE BATERIA ({withBattery.Count} de {r.DeviceNodes.Count} lidos)");

        if (withBattery.Count == 0)
        {
            sb.AppendLine("  nenhum. O Windows nao guarda bateria de nenhum dispositivo aqui.");
            sb.AppendLine();
            return;
        }

        foreach (var node in withBattery)
        {
            sb.AppendLine($"  {Or(node.Name, "(sem nome)")}");
            sb.AppendLine($"      instancia: {node.InstanceId}");
            sb.AppendLine($"      classe: {Or(node.Class, "-")}   enumerador: {Or(node.Enumerator, "-")}");
            if (node.BluetoothBattery is int pct)
                sb.AppendLine($"      *** DEVPKEY_Bluetooth_Battery = {pct}");
            foreach (var (key, value) in node.BatteryProperties)
                sb.AppendLine($"      {key} = {value}");
            sb.AppendLine();
        }
    }

    private static void RenderXInput(StringBuilder sb, ScanReport r)
    {
        Section(sb, "XINPUT");

        if (r.XInput.Count == 0)
        {
            sb.AppendLine("  XInput indisponivel.");
            sb.AppendLine();
            return;
        }

        foreach (var s in r.XInput)
        {
            if (!s.Connected)
            {
                sb.AppendLine($"  slot {s.Slot}: vazio");
                continue;
            }

            sb.AppendLine($"  slot {s.Slot}: CONECTADO  {Or(s.HardwareId, "vid:pid desconhecido")}  {s.SubType}");
            sb.AppendLine($"           bateria: tipo={s.BatteryType} nivel={s.BatteryLevel}  ->  {s.Interpretation}");
        }

        sb.AppendLine();
    }

    private static void RenderProviders(StringBuilder sb, ScanReport r)
    {
        Section(sb, "PROVIDERS ATUAIS DO APP");

        if (r.Providers.Count == 0)
        {
            sb.AppendLine("  nada.");
            sb.AppendLine();
            return;
        }

        foreach (var p in r.Providers)
        {
            sb.AppendLine($"  [{p.Status,-12}] {p.Value,-11} {p.DisplayName,-34} {p.HardwareId,-12} {p.Provider}");
            if (p.Detail is { Length: > 0 }) sb.AppendLine($"                 -> {p.Detail}");
        }

        sb.AppendLine();
    }

    private static void RenderCapture(StringBuilder sb, ScanReport r)
    {
        if (r.Capture is null)
        {
            Section(sb, "CAPTURA PASSIVA");
            sb.AppendLine("  nao foi feita nesta execucao.");
            sb.AppendLine();
            return;
        }

        var c = r.Capture;
        Section(sb, $"CAPTURA PASSIVA ({c.Seconds}s, {c.CollectionsOpened} colecoes escutadas, {c.CollectionsSkipped} puladas)");

        if (c.Groups.Count == 0)
        {
            sb.AppendLine("  nenhum relatorio chegou. Normal se ninguem mexeu nos dispositivos");
            sb.AppendLine("  durante a captura: mouse parado costuma nao transmitir nada.");
            sb.AppendLine();
            return;
        }

        foreach (var g in c.Groups)
        {
            sb.AppendLine($"  {g.HardwareId}  {Or(g.DeviceName, "(sem nome)")}  page {g.UsagePage}");
            sb.AppendLine($"      relatorio 0x{g.ReportId:X2}  len={g.Length}  {g.Count} recebidos");

            foreach (var sample in g.Samples)
                sb.AppendLine($"      amostra: {sample}");

            var interesting = g.Bytes.Where(b => b.BatteryCandidate).ToList();
            if (interesting.Count > 0)
            {
                sb.AppendLine("      CANDIDATOS a percentual (constantes, entre 1 e 100):");
                foreach (var b in interesting)
                    sb.AppendLine($"          byte[{b.Offset}] = {b.Min}");
            }

            sb.AppendLine("      bytes: " + string.Join("  ", g.Bytes.Select(FormatByteStat)));
            sb.AppendLine();
        }
    }

    private static string FormatByteStat(CaptureByteStat b) =>
        b.Constant ? $"[{b.Offset}]={b.Min:X2}" : $"[{b.Offset}]={b.Min:X2}..{b.Max:X2}({b.Distinct})";

    private static void RenderWarnings(StringBuilder sb, ScanReport r)
    {
        if (r.Warnings.Count == 0) return;

        Section(sb, $"AVISOS ({r.Warnings.Count})");
        foreach (var w in r.Warnings.Take(60)) sb.AppendLine($"  {w}");
        if (r.Warnings.Count > 60) sb.AppendLine($"  ... e mais {r.Warnings.Count - 60}");
        sb.AppendLine();
    }

    private static void Section(StringBuilder sb, string title)
    {
        sb.AppendLine($">>> {title}");
        sb.AppendLine(new string('-', Math.Min(64, title.Length + 4)));
    }

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value!;
}
