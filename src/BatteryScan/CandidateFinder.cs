namespace BatteryTray.Scan;

/// <summary>
/// Le o relatorio ja coletado e responde a unica pergunta que importa: onde,
/// nesta maquina, parece existir um caminho de bateria que da' para programar?
///
/// Funcao pura sobre o modelo -- nao toca em hardware -- entao o ScanSelfTest
/// consegue verificar a classificacao sem nenhum periferico plugado.
/// </summary>
public static class CandidateFinder
{
    public const string Confirmed = "confirmada";
    public const string High = "alta";
    public const string Medium = "media";
    public const string Low = "a investigar";

    public static List<CandidateInfo> Find(ScanReport report)
    {
        var found = new List<CandidateInfo>();

        // 1) Provider atual que leu de verdade: caminho ja resolvido.
        foreach (var p in report.Providers.Where(p => p.Status is "Ok" or "Charging"))
        {
            found.Add(new CandidateInfo
            {
                Device = $"{p.DisplayName} ({p.HardwareId})",
                Path = $"provider {p.Provider}",
                Confidence = Confirmed,
                What = $"o app ja le: {p.Value}",
            });
        }

        // 2) O Windows ja sabe a bateria do dispositivo Bluetooth. E' o melhor
        //    caso possivel: nao precisa decifrar protocolo, so ler a propriedade.
        foreach (var node in report.DeviceNodes.Where(n => n.BluetoothBattery is not null))
        {
            found.Add(new CandidateInfo
            {
                Device = Describe(node),
                Path = "DEVPKEY_Bluetooth_Battery {104EA319-6EE2-4701-BD47-8DDBF425BBE5},2",
                Confidence = High,
                What = $"Windows ja expoe {node.BluetoothBattery}% -- da' para um provider generico de Bluetooth",
            });
        }

        // 3) Outras propriedades de devnode com "battery" no nome.
        foreach (var node in report.DeviceNodes.Where(n => n.BluetoothBattery is null && n.BatteryProperties.Count > 0))
        {
            foreach (var (key, value) in node.BatteryProperties)
            {
                found.Add(new CandidateInfo
                {
                    Device = Describe(node),
                    Path = key,
                    Confidence = Medium,
                    What = $"propriedade de devnode com valor \"{value}\"",
                });
            }
        }

        // 4) Descritor HID declara bateria: caminho padronizado, sem engenharia
        //    reversa -- basta ler o relatorio certo.
        foreach (var device in report.Hid.Where(d => d.DeclaredBattery.Count > 0))
        {
            var where = device.Collections
                .Where(c => c.ValueCaps.Any(v => v.Battery is not null))
                .Select(c => $"page {c.UsagePage}/{c.Usage}")
                .Distinct()
                .ToList();

            found.Add(new CandidateInfo
            {
                Device = $"{device.KnownName ?? device.Product} ({device.HardwareId}) [{device.Transport}]",
                Path = string.Join(", ", where),
                Confidence = High,
                What = "descritor declara " + string.Join("; ", device.DeclaredBattery),
            });
        }

        // 5) XInput fora do WIRED falso.
        foreach (var slot in report.XInput.Where(s => s.Connected && s.Interpretation.StartsWith("OK", StringComparison.Ordinal)))
        {
            found.Add(new CandidateInfo
            {
                Device = $"XInput slot {slot.Slot} {slot.HardwareId} ({slot.SubType})",
                Path = "XInputGetBatteryInformation",
                Confidence = High,
                What = $"{slot.BatteryType} nivel {slot.BatteryLevel} -> {slot.Interpretation}",
            });
        }

        // 6) Bytes que ficaram parados dentro de 1..100 durante a captura. E'
        //    fraco de proposito: e' pista, nao conclusao.
        foreach (var group in report.Capture?.Groups ?? new List<CaptureGroup>())
        {
            var candidates = group.Bytes.Where(b => b.BatteryCandidate).ToList();
            if (candidates.Count == 0) continue;

            found.Add(new CandidateInfo
            {
                Device = $"{group.DeviceName} ({group.HardwareId})",
                Path = $"page {group.UsagePage}, relatorio 0x{group.ReportId:X2} len={group.Length}",
                Confidence = Low,
                What = "bytes constantes em 1..100: " +
                       string.Join(", ", candidates.Select(b => $"[{b.Offset}]={b.Min}")),
            });
        }

        return found.OrderBy(c => Rank(c.Confidence)).ThenBy(c => c.Device, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Describe(DeviceNodeInfo node)
    {
        string name = node.Name.Length > 0 ? node.Name : node.InstanceId;
        return node.Enumerator.Length > 0 ? $"{name} [{node.Enumerator}]" : name;
    }

    internal static int Rank(string confidence) => confidence switch
    {
        Confirmed => 0,
        High => 1,
        Medium => 2,
        Low => 3,
        _ => 4,
    };
}
