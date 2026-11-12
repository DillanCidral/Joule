namespace BatteryTray.Core;

public enum ValueKind
{
    /// <summary>Numero + barra continua.</summary>
    Percent,
    /// <summary>Palavra num chip + 3 blocos (XInput). Nunca vira percentual.</summary>
    Band,
    /// <summary>"no cabo", sem numero.</summary>
    Cable,
}

/// <summary>Uma linha da lista principal, ja decidida. Sem WPF: testada no diag.</summary>
public sealed record DeviceRowView(
    string Key,
    string Name,
    DeviceKind Kind,
    string Origin,
    ValueKind ValueKind,
    int Percent,
    int BandCount,
    string BandLabel,
    bool IsLive,
    bool IsStale,
    bool IsLow,
    string? Age,
    string? ChargeText,
    string? Note,
    string Tooltip);

/// <summary>Uma linha de "outros": plugado, sem valor, com o motivo.</summary>
public sealed record OtherRowView(string Key, string Name, DeviceKind Kind, string Reason);

/// <summary>Plugado, sem valor, de marca esperando licenca: fica a vista com "Permitir" (2026-11-12).</summary>
public sealed record ConsentRowView(string Key, string Name, DeviceKind Kind, string Vid);

/// <summary>
/// Regras de exibicao do design v1, num lugar so:
/// faixa nao e' percentual, valor antigo parece antigo, sem dado vai para
/// "outros" com o motivo, desconectado nao aparece.
/// </summary>
public static class PanelView
{
    /// <summary>Acima disto uma leitura sob demanda vira "valor antigo".</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    public static List<DeviceRowView> MainRows(IEnumerable<DeviceEntry> devices, int threshold, DateTime nowUtc)
    {
        var rows = devices.Where(d => d.ShowsBattery).Select(d => Row(d, threshold, nowUtc)).ToList();
        rows.Sort(Compare);
        return rows;
    }

    // shortcut: a licenca e' por VID, entao um aparelho Bluetooth sem valor da mesma marca tambem pede "Permitir"; separar por transporte se aparecer na pratica.
    public static List<ConsentRowView> NeedConsent(IEnumerable<DeviceEntry> devices, Func<string, string?> waitingVid) =>
        devices.Where(d => d.ShowsAsOther && waitingVid(d.HardwareId) is not null)
            .Select(d => new ConsentRowView(d.Key, d.DisplayName, d.Kind, waitingVid(d.HardwareId)!))
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Sem valor e com o motivo; quem espera licenca vai para <see cref="NeedConsent"/>, nao para ca.</summary>
    public static List<OtherRowView> Others(IEnumerable<DeviceEntry> devices, Func<string, string?>? waitingVid = null) =>
        devices.Where(d => d.ShowsAsOther && waitingVid?.Invoke(d.HardwareId) is null)
            .Select(d => new OtherRowView(d.Key, d.DisplayName, d.Kind, Reason(d)))
            .OrderBy(o => o.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    internal static DeviceRowView Row(DeviceEntry d, int threshold, DateTime nowUtc)
    {
        var r = d.Reading;
        bool fromCache = r.Status is ReadingStatus.NoData or ReadingStatus.Disconnected && d.Cache?.HasValue == true;
        DateTime observed = fromCache ? d.Cache!.TimestampUtc : r.EffectiveObservedUtc;
        TimeSpan age = nowUtc - observed;

        ValueKind kind;
        int percent = 0, bands = 0;
        string bandLabel = "";
        bool low = false;

        if (d.EffectivePercent is int p)
        {
            kind = ValueKind.Percent;
            percent = p;
            low = p < threshold;
        }
        else if (d.EffectiveLevel is { Length: > 0 } level)
        {
            kind = ValueKind.Band;
            bands = level switch { "FULL" => 3, "MEDIUM" => 2, "LOW" => 1, _ => 0 };
            bandLabel = Known.LevelLabelPtBr(level);
            low = bands <= 1;
        }
        else
        {
            kind = ValueKind.Cable;
        }

        bool stale = !d.IsLive && (fromCache || age > StaleAfter);

        string? note = low
            ? (kind == ValueKind.Percent ? L.T($"abaixo de {threshold}%", $"below {threshold}%") : L.T("bateria baixa", "low battery"))
            : stale ? r.Detail : null;

        return new DeviceRowView(
            Key: d.Key,
            Name: d.DisplayName,
            Kind: d.Kind,
            Origin: d.Origin,
            ValueKind: kind,
            Percent: percent,
            BandCount: bands,
            BandLabel: bandLabel,
            IsLive: d.IsLive,
            IsStale: stale,
            IsLow: low,
            Age: d.IsLive ? null : Age(age),
            ChargeText: fromCache ? null : r.ChargeText,
            Note: note,
            Tooltip: Tooltip(d));
    }

    /// <summary>Percentuais do mais fraco ao mais forte, depois faixas, depois cabo.</summary>
    private static int Compare(DeviceRowView a, DeviceRowView b)
    {
        int c = Rank(a).CompareTo(Rank(b));
        return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
    }

    private static int Rank(DeviceRowView r) => r.ValueKind switch
    {
        ValueKind.Percent => r.Percent,
        ValueKind.Band => 1000 + r.BandCount,
        _ => 2000,
    };

    public static string Age(TimeSpan age)
    {
        if (age.TotalSeconds < 45) return L.T("agora", "now");
        if (age.TotalMinutes < 60) return L.T($"há {Math.Max(1, (int)age.TotalMinutes)} min", $"{Math.Max(1, (int)age.TotalMinutes)} min ago");
        if (age.TotalHours < 24) return L.T($"há {(int)age.TotalHours} h", $"{(int)age.TotalHours} h ago");
        return L.T($"há {(int)age.TotalDays} d", $"{(int)age.TotalDays} d ago");
    }

    internal static string Reason(DeviceEntry d)
    {
        var r = d.Reading;
        return r.Status switch
        {
            ReadingStatus.Unsupported when ReferenceEquals(r, BatteryReading.Unsupported) || string.IsNullOrWhiteSpace(r.Detail)
                => L.T("O app ainda não sabe ler a bateria deste aparelho.", "The app can’t read this device’s battery yet."),
            ReadingStatus.Unsupported => Sentence(r.Detail!),
            ReadingStatus.Error => Sentence(L.T($"erro ao ler: {r.Detail}", $"error reading: {r.Detail}")),
            _ => string.IsNullOrWhiteSpace(r.Detail) ? L.T("Sem dado agora.", "No data right now.") : Sentence(L.T($"sem dado agora: {r.Detail}", $"no data right now: {r.Detail}")),
        };
    }

    private static string Tooltip(DeviceEntry d)
    {
        var lines = new List<string> { d.DisplayName };
        if (!string.IsNullOrWhiteSpace(d.Reading.Detail)) lines.Add(Sentence(d.Reading.Detail));
        if (!string.IsNullOrWhiteSpace(d.HardwareId)) lines.Add(d.HardwareId);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Primeira letra maiuscula e ponto final, sem duplicar o ponto.</summary>
    internal static string Sentence(string text)
    {
        string s = text.Trim();
        if (s.Length == 0) return s;
        s = char.ToUpper(s[0]) + s[1..];
        return s.EndsWith('.') ? s : s + ".";
    }
}
