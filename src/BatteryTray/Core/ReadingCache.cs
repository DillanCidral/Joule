using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatteryTray.Core;

public sealed class CachedReading
{
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("hardwareId")] public string HardwareId { get; set; } = "";
    [JsonPropertyName("percent")] public int? Percent { get; set; }
    [JsonPropertyName("level")] public string? Level { get; set; }
    [JsonPropertyName("charging")] public bool Charging { get; set; }
    [JsonPropertyName("timestampUtc")] public DateTime TimestampUtc { get; set; }

    [JsonIgnore] public TimeSpan Age => DateTime.UtcNow - TimestampUtc;
    [JsonIgnore] public bool HasValue => Percent.HasValue || !string.IsNullOrEmpty(Level) || Charging;
}

/// <summary>
/// Ultimo valor visto por dispositivo, com carimbo de tempo. Persistido para que
/// o app mostre algo util ja na primeira abertura, antes da leitura terminar.
/// </summary>
public sealed class ReadingCache
{
    private readonly Dictionary<string, CachedReading> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public CachedReading? Get(string key)
    {
        lock (_gate) return _items.TryGetValue(key, out var v) ? v : null;
    }

    public void Update(string key, string displayName, string hardwareId, BatteryReading reading)
    {
        if (reading.Status is not (ReadingStatus.Ok or ReadingStatus.Charging)) return;

        lock (_gate)
        {
            _items[key] = new CachedReading
            {
                DisplayName = displayName,
                HardwareId = hardwareId,
                Percent = reading.Percent,
                Level = reading.Level,
                Charging = reading.Status == ReadingStatus.Charging,
                TimestampUtc = reading.EffectiveObservedUtc,
            };
        }
    }

    public static ReadingCache Load()
    {
        var cache = new ReadingCache();
        try
        {
            if (File.Exists(AppPaths.StateFile))
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, CachedReading>>(
                    File.ReadAllText(AppPaths.StateFile));
                if (raw is not null)
                {
                    foreach (var kv in raw) cache._items[kv.Key] = kv.Value;
                }
            }
        }
        catch (Exception ex) { Log.Error("ReadingCache.Load", ex); }
        return cache;
    }

    /// <summary>False no JouleScan: o PC do amigo nao ganha pasta do Joule nem cache (revisao de 2026-11-12).</summary>
    public bool Persist { get; init; } = true;

    public void Save()
    {
        if (!Persist) return;
        try
        {
            AppPaths.EnsureDir();
            Dictionary<string, CachedReading> snapshot;
            lock (_gate) snapshot = new Dictionary<string, CachedReading>(_items);
            AppPaths.WriteAtomic(AppPaths.StateFile, JsonSerializer.Serialize(snapshot, Options));
        }
        catch (Exception ex) { Log.Error("ReadingCache.Save", ex); }
    }

    public static string DescribeAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero) return L.T("agora", "now");
        if (age.TotalSeconds < 45) return L.T("agora", "now");
        if (age.TotalMinutes < 60) return L.T($"ha {(int)age.TotalMinutes} min", $"{(int)age.TotalMinutes} min ago");
        if (age.TotalHours < 24) return L.T($"ha {(int)age.TotalHours} h", $"{(int)age.TotalHours} h ago");
        return L.T($"ha {(int)age.TotalDays} d", $"{(int)age.TotalDays} d ago");
    }
}
