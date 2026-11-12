using BatteryTray.Interop;

namespace BatteryTray.Core;

/// <summary>Aparelho que acabou de entrar, antes da leitura: o aviso mostra o nome com "Lendo...".</summary>
public sealed record Arriving(string Key, string Name, DeviceKind Kind);

public sealed class RefreshResult
{
    public required IReadOnlyList<DeviceEntry> Devices { get; init; }
    public DateTime WhenUtc { get; init; } = DateTime.UtcNow;
    public string? FatalError { get; init; }

    /// <summary>Chaves que uma leitura de conexao acabou de ler: o aviso diz "conectou" mesmo se a linha ja existia (controle que foi para o cabo).</summary>
    public IReadOnlyCollection<string> Arrived { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Orquestra uma leitura. SOB DEMANDA apenas: chamado na abertura do app, no
/// login do Windows, no botao Atualizar e (desde 2026-11-10) quando o Windows
/// avisa que um aparelho conectou -- ai so o que entrou, uma vez (ReadArrived).
/// Nao existe timer nem laco de fundo nesta classe -- por decisao de projeto,
/// nao por esquecimento.
/// </summary>
public sealed class ReadingService : IDisposable
{
    private readonly ProviderRegistry _registry;
    private readonly ReadingCache _cache;
    private readonly Settings _settings;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// <summary>Colecoes e reivindicacoes da ultima leitura completa, reusadas pela leitura ao vivo.</summary>
    private List<HidCollection> _lastCollections = new();
    private Dictionary<IBatteryProvider, List<HidCollection>> _lastClaims = new();

    /// <summary>
    /// Uma escuta passiva viu um percentual novo (thread do leitor). Quem
    /// assina chama <see cref="RefreshLive"/>; nada aqui muda de ritmo.
    /// </summary>
    public event Action? LiveValueChanged;

    public ReadingService(ProviderRegistry registry, ReadingCache cache, Settings settings)
    {
        _registry = registry;
        _cache = cache;
        _settings = settings;

        foreach (var p in registry.HidProviders.OfType<IPassiveListenerProvider>())
            p.ValueChanged += () => LiveValueChanged?.Invoke();
    }

    /// <summary>
    /// Atualiza SO as linhas ao vivo sobre o resultado anterior: escutas
    /// passivas (o valor ja chegou no handle aberto) e Bluetooth (metadado do
    /// Windows). Nao varre o HID de novo -- a varredura pede strings ao
    /// dispositivo -- e nao toca em nenhum provider sob demanda. Devolve null
    /// se uma leitura completa estiver em curso.
    /// </summary>
    /// <param name="passive">Reler as escutas passivas (alguma viu valor novo).</param>
    /// <param name="bluetooth">Reler o Bluetooth (timer de 60 s).</param>
    public RefreshResult? RefreshLive(RefreshResult previous, bool passive, bool bluetooth)
    {
        if (!_oneAtATime.Wait(0)) return null;
        try
        {
            var replaced = new Dictionary<string, DeviceEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var (provider, claimed) in passive ? _lastClaims : new())
            {
                // So escuta viva: uma morta nao e' reaberta fora de uma leitura sob demanda.
                if (provider is not IPassiveListenerProvider { IsListening: true }) continue;
                var entry = ReadHid(provider, claimed, _lastCollections);
                if (entry is not null) replaced[entry.Key] = entry;
            }

            var btEntries = new List<DeviceEntry>();
            foreach (var standalone in bluetooth ? _registry.StandaloneProviders.OfType<Providers.BluetoothPnpProvider>() : Enumerable.Empty<Providers.BluetoothPnpProvider>())
            {
                try { btEntries.AddRange(standalone.Discover(_lastCollections).ToList()); }
                catch (Exception ex) { Log.Error("Bluetooth.Live", ex); }
            }
            foreach (var e in btEntries)
            {
                _cache.Update(e.Key, e.DisplayName, e.HardwareId, e.Reading);
                e.Cache = _cache.Get(e.Key);
            }

            var devices = previous.Devices
                .Where(d => !bluetooth || !d.Key.StartsWith("bt:", StringComparison.OrdinalIgnoreCase))
                .Select(d => replaced.TryGetValue(d.Key, out var fresh) ? fresh : d)
                .Concat(btEntries)
                .ToList();
            devices.Sort(CompareForDisplay);

            return new RefreshResult { Devices = devices, WhenUtc = previous.WhenUtc, FatalError = previous.FatalError };
        }
        catch (Exception ex)
        {
            Log.Error("RefreshLive", ex);
            return null;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>
    /// O Windows avisou que colecoes HID entraram (aviso de conexao, decidido
    /// em 2026-11-10). Descreve SO as colecoes novas (acesso 0, como a
    /// varredura) e le UMA vez so o provider delas, sobre o resultado
    /// anterior: nada de varredura completa, nenhum outro aparelho tocado.
    /// <paramref name="planned"/> recebe os aparelhos antes da leitura, para o
    /// aviso mostrar "Lendo...". Null = nada novo, ou outra leitura em curso.
    /// </summary>
    public RefreshResult? ReadArrived(RefreshResult previous, IReadOnlyCollection<string> paths, Action<IReadOnlyList<Arriving>> planned)
    {
        if (!_oneAtATime.Wait(0)) return null;
        try
        {
            var known = new HashSet<string>(_lastCollections.Select(c => c.Path), StringComparer.OrdinalIgnoreCase);
            var fresh = paths.Where(p => !known.Contains(p)).Select(HidInventory.Describe).OfType<HidCollection>().ToList();
            if (fresh.Count == 0) return null;
            HidRequest.NewReading(_settings.ListenOnly, _settings.AskAllowedVids);
            Log.Write($"Conexao: {fresh.Count} colecoes novas ({string.Join(", ", fresh.Select(c => c.HardwareId).Distinct())}).");

            var all = _lastCollections.Concat(fresh).ToList();
            var ids = fresh.Select(c => (c.Vid, c.Pid)).ToHashSet();
            var providers = fresh.Select(c => _registry.Resolve(c.Vid, c.Pid)).OfType<IBatteryProvider>().Distinct().ToList();
            // XInput sempre: o controle ligado pelo dongle ou no cabo entra como HID novo, e o XInput so pergunta a API do Windows.
            var standalone = _registry.StandaloneProviders
                .Where(s => s is Providers.XInputProvider || (s is not Providers.BluetoothPnpProvider && ids.Any(i => s.Handles(i.Vid, i.Pid)))).ToList();

            var plan = new List<Arriving>();
            foreach (var provider in providers)
            {
                var first = fresh.First(c => _registry.Resolve(c.Vid, c.Pid) == provider);
                try
                {
                    string key = provider.LogicalKey(first.Vid, first.Pid);
                    string name = _cache.Get(key)?.DisplayName is { Length: > 0 } dn ? dn
                        : Known.Name(first.Vid, first.Pid) ?? Pick(fresh.Select(c => c.Product)) ?? "Aparelho";
                    plan.Add(new Arriving(key, name, Known.Kind(first.Vid, first.Pid)));
                }
                catch (Exception ex) { Log.Error($"{provider.Name}.Describe", ex); }
            }
            planned(plan);

            var replaced = new Dictionary<string, DeviceEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var provider in providers)
            {
                var claimed = all.Where(c => _registry.Resolve(c.Vid, c.Pid) == provider).ToList();
                _lastClaims[provider] = claimed;
                var entry = ReadHid(provider, claimed, all);
                if (entry is null) continue;
                replaced[entry.Key] = entry;
                Log.Write($"  {entry.DisplayName}: {Describe(entry.Reading)}");
            }
            // So os VID:PID que entraram: os aparelhos da mesma marca que ja estavam ligados nao sao perguntados de novo.
            // O XInput ve tudo (a regra do 8BitDo no cabo olha o dongle ocioso) e so pergunta a API do Windows.
            // shortcut: Razer com cabo E dongle do mesmo modelo le pelo que entrou (o cabo perde a preferencia ate o proximo Atualizar); raro, so mexer se um testador reclamar.
            var arrivedOnly = all.Where(c => ids.Contains((c.Vid, c.Pid))).ToList();
            foreach (var s in standalone)
            {
                try
                {
                    foreach (var entry in s.Discover(s is Providers.XInputProvider ? all : arrivedOnly).ToList())
                    {
                        _cache.Update(entry.Key, entry.DisplayName, entry.HardwareId, entry.Reading);
                        entry.Cache = _cache.Get(entry.Key);
                        replaced[entry.Key] = entry;
                    }
                }
                catch (Exception ex) { Log.Error($"{s.Name}.Discover", ex); }
            }
            // Sem provider (teclado, fone sem protocolo): entra como "sem suporte", para o aviso dizer que conectou.
            foreach (var group in fresh.Where(c => _registry.Resolve(c.Vid, c.Pid) is null).GroupBy(c => c.HardwareId, StringComparer.OrdinalIgnoreCase))
            {
                if (Unsupported(group.Key, all.Where(c => string.Equals(c.HardwareId, group.Key, StringComparison.OrdinalIgnoreCase)).ToList()) is not { } entry) continue;
                if (!replaced.ContainsKey(entry.Key)) replaced[entry.Key] = entry;
            }
            _lastCollections = all;
            _cache.Save();

            var devices = previous.Devices.Where(d => !replaced.ContainsKey(d.Key)).Concat(replaced.Values).ToList();
            devices.Sort(CompareForDisplay);
            // Conectou de verdade: linha nova, que estava desligada ou que mudou de estado (o controle foi para o cabo).
            // O XInput relido sem mudanca nao conta, senao plugar um mouse anunciaria o controle de novo.
            var before = previous.Devices.GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var arrivedKeys = replaced.Values.Where(e => e.IsConnected && (!before.TryGetValue(e.Key, out var o) || !o.IsConnected
                || o.Reading.Status != e.Reading.Status || o.Origin != e.Origin)).Select(e => e.Key).ToList();
            return new RefreshResult { Devices = devices, WhenUtc = DateTime.UtcNow, FatalError = previous.FatalError, Arrived = arrivedKeys };
        }
        catch (Exception ex)
        {
            Log.Error("ReadArrived", ex);
            return null;
        }
        finally { _oneAtATime.Release(); }
    }

    /// <summary>
    /// Colecoes HID sairam: o aparelho que ficou sem nenhuma colecao vira
    /// "desconectado". Nao toca em nada (regra 2: aparelho sumiu, parar de
    /// tocar nele). Null = nada conhecido saiu, ou outra leitura em curso.
    /// </summary>
    public RefreshResult? MarkRemoved(RefreshResult previous, IReadOnlyCollection<string> paths)
    {
        if (!_oneAtATime.Wait(0)) return null;
        try
        {
            var gone = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            var removed = _lastCollections.Where(c => gone.Contains(c.Path)).ToList();
            if (removed.Count == 0) return null;

            _lastCollections = _lastCollections.Where(c => !gone.Contains(c.Path)).ToList();
            foreach (var (provider, claimed) in _lastClaims.ToList())
            {
                var left = claimed.Where(c => !gone.Contains(c.Path)).ToList();
                if (left.Count == 0) _lastClaims.Remove(provider);
                else if (left.Count != claimed.Count) _lastClaims[provider] = left;
            }

            var present = _lastCollections.Select(c => c.HardwareId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var lost = removed.Select(c => c.HardwareId).Where(id => !present.Contains(id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (lost.Count == 0) return null;

            var devices = previous.Devices.Select(d =>
            {
                if (!d.IsConnected || d.Key.StartsWith("bt:", StringComparison.OrdinalIgnoreCase)) return d;
                var mine = d.HardwareId.Split(", ", StringSplitOptions.RemoveEmptyEntries);
                if (!mine.Any(lost.Contains) || mine.Any(present.Contains)) return d;
                Log.Write($"Desconectou: {d.DisplayName}.");
                return new DeviceEntry
                {
                    Key = d.Key, DisplayName = d.DisplayName, HardwareId = d.HardwareId, ProviderName = d.ProviderName,
                    Reading = BatteryReading.Disconnected(L.T("desconectou agora", "just disconnected")), Kind = d.Kind, Origin = d.Origin,
                    Cache = d.Cache, IsConnected = false,
                };
            }).ToList();
            return new RefreshResult { Devices = devices, WhenUtc = previous.WhenUtc, FatalError = previous.FatalError };
        }
        catch (Exception ex)
        {
            Log.Error("MarkRemoved", ex);
            return null;
        }
        finally { _oneAtATime.Release(); }
    }

    /// <summary>So o --selftest: faz de conta que a ultima varredura viu estas colecoes, sem varrer.</summary>
    internal void RememberForTest(IEnumerable<HidCollection> collections) => _lastCollections = collections.ToList();

    /// <summary>Executa uma leitura completa. Pode bloquear alguns segundos: chame fora da UI thread.</summary>
    public RefreshResult Refresh()
    {
        if (!_oneAtATime.Wait(TimeSpan.FromSeconds(15)))
            return new RefreshResult { Devices = Array.Empty<DeviceEntry>(), FatalError = L.T("leitura anterior ainda em curso", "previous reading still running") };

        try
        {
            return RefreshCore();
        }
        catch (Exception ex)
        {
            Log.Error("Refresh", ex);
            return new RefreshResult { Devices = Array.Empty<DeviceEntry>(), FatalError = ex.Message };
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private RefreshResult RefreshCore()
    {
        HidRequest.NewReading(_settings.ListenOnly, _settings.AskAllowedVids);
        List<HidCollection> collections = HidInventory.Scan();
        Log.Write($"Refresh: {collections.Count} colecoes HID presentes.");

        var entries = new List<DeviceEntry>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Agrupa por VID:PID e resolve providers.
        var claimedByProvider = new Dictionary<IBatteryProvider, List<HidCollection>>();
        var unsupportedGroups = new Dictionary<string, List<HidCollection>>(StringComparer.OrdinalIgnoreCase);

        foreach (var col in collections)
        {
            var provider = _registry.Resolve(col.Vid, col.Pid);
            if (provider is not null)
            {
                if (!claimedByProvider.TryGetValue(provider, out var list))
                    claimedByProvider[provider] = list = new List<HidCollection>();
                list.Add(col);
            }
            else
            {
                string id = col.HardwareId;
                if (!unsupportedGroups.TryGetValue(id, out var list))
                    unsupportedGroups[id] = list = new List<HidCollection>();
                list.Add(col);
            }
        }

        // 2) Providers HID: uma leitura por dispositivo logico.
        foreach (var (provider, claimed) in claimedByProvider)
        {
            var entry = ReadHid(provider, claimed, collections);
            if (entry is null) continue;
            seenKeys.Add(entry.Key);
            entries.Add(entry);
            Log.Write($"  {entry.DisplayName}: {Describe(entry.Reading)}");
        }
        _lastCollections = collections;
        _lastClaims = claimedByProvider;

        // 3) Providers autonomos (XInput e afins).
        foreach (var standalone in _registry.StandaloneProviders)
        {
            List<DeviceEntry> discovered;
            try
            {
                // ToList aqui dentro: Discover com yield so roda ao enumerar, e o erro tem de cair neste catch.
                discovered = standalone.Discover(collections).ToList();
            }
            catch (Exception ex)
            {
                Log.Error($"{standalone.Name}.Discover", ex);
                continue;
            }

            foreach (var entry in discovered)
            {
                _cache.Update(entry.Key, entry.DisplayName, entry.HardwareId, entry.Reading);
                entry.Cache = _cache.Get(entry.Key);
                seenKeys.Add(entry.Key);
                entries.Add(entry);
                Log.Write($"  {entry.DisplayName}: {Describe(entry.Reading)}");
            }
        }

        // 4) Desconhecidos: listados como "sem suporte", nunca somem, nunca
        //    mostram valor.
        foreach (var (id, group) in unsupportedGroups)
        {
            if (Unsupported(id, group) is not { } entry || !seenKeys.Add(entry.Key)) continue;
            entries.Add(entry);
        }

        // 5) Dispositivos conhecidos que sumiram: continuam listados com o
        //    ultimo valor conhecido e a idade dele.
        foreach (var provider in _registry.HidProviders)
        {
            foreach (var (vid, pid) in provider.SupportedIds)
            {
                string key;
                try { key = provider.LogicalKey(vid, pid); } catch { continue; }
                if (!seenKeys.Add(key)) continue;

                var cached = _cache.Get(key);
                entries.Add(new DeviceEntry
                {
                    Key = key,
                    DisplayName = cached?.DisplayName is { Length: > 0 } dn ? dn : Known.Name(vid, pid) ?? key,
                    HardwareId = cached?.HardwareId ?? Known.Id(vid, pid),
                    ProviderName = provider.Name,
                    Reading = BatteryReading.Disconnected(L.T("nao esta conectado agora", "not connected right now")),
                    Cache = cached,
                });
            }
        }

        _cache.Save();

        entries.Sort(CompareForDisplay);
        return new RefreshResult { Devices = entries };
    }

    /// <summary>Linha "sem suporte" de um VID:PID sem provider; null se um provider autonomo ja o lista (Logitech, Razer).</summary>
    private DeviceEntry? Unsupported(string id, List<HidCollection> group)
    {
        var first = group[0];
        if (_registry.StandaloneProviders.Any(p => p.Handles(first.Vid, first.Pid))) return null;
        string name = Known.Name(first.Vid, first.Pid)
                      ?? Pick(group.Select(c => c.Product))
                      ?? Pick(group.Select(c => c.Manufacturer))
                      ?? L.T("Dispositivo HID", "HID device");
        string? reason = Known.DeadEndReason(first.Vid, first.Pid);
        return new DeviceEntry
        {
            Key = $"hid:{id}",
            DisplayName = name,
            HardwareId = id,
            ProviderName = "",
            Reading = reason is null
                ? BatteryReading.Unsupported
                : new BatteryReading { Status = ReadingStatus.Unsupported, Detail = reason },
            IsConnected = true,
            Kind = Known.Kind(first.Vid, first.Pid) is var k and not DeviceKind.Generic
                ? k
                : Known.KindFromUsages(group.Select(c => (c.UsagePage, c.Usage))),
        };
    }

    private DeviceEntry? ReadHid(IBatteryProvider provider, List<HidCollection> claimed, List<HidCollection> collections)
    {
        var ctx = new ReadContext { AllCollections = collections, Claimed = claimed, Settings = _settings };

        string key;
        string hardwareId = string.Join(", ", claimed.Select(c => c.HardwareId).Distinct());
        try
        {
            key = provider.LogicalKey(claimed[0].Vid, claimed[0].Pid);
        }
        catch (Exception ex)
        {
            Log.Error($"{provider.Name}.Describe", ex);
            return null;
        }

        BatteryReading reading;
        try
        {
            reading = provider.Read(ctx);
        }
        catch (Exception ex)
        {
            Log.Error($"{provider.Name}.Read", ex);
            reading = BatteryReading.Error(ex.Message);
        }

        // O nome depois da leitura: o Attack Shark so sabe o modelo quando o relatorio chega.
        string name;
        try { name = provider.DisplayName(ctx); }
        catch (Exception ex)
        {
            Log.Error($"{provider.Name}.Describe", ex);
            return null;
        }

        _cache.Update(key, name, hardwareId, reading);
        var first = claimed[0];
        return new DeviceEntry
        {
            Key = key,
            DisplayName = name,
            HardwareId = hardwareId,
            ProviderName = provider.Name,
            Reading = reading,
            Cache = _cache.Get(key),
            IsConnected = true,
            Kind = Known.Kind(first.Vid, first.Pid),
            Origin = reading.Status == ReadingStatus.Charging ? "Cabo" : Known.Origin(first.Vid, first.Pid),
            // Escuta aberta e relatorio recente: o valor chega sozinho. Um
            // mouse que dorme ha horas deixa de ser "ao vivo" e mostra a idade.
            IsLive = provider is IPassiveListenerProvider { IsListening: true }
                     && reading.Status == ReadingStatus.Ok
                     && DateTime.UtcNow - reading.EffectiveObservedUtc < PanelView.StaleAfter,
        };
    }

    private static string? Pick(IEnumerable<string> candidates) =>
        candidates.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

    private static string Describe(BatteryReading r) => r.Status switch
    {
        ReadingStatus.Ok when r.Percent.HasValue => $"{r.Percent}%",
        ReadingStatus.Ok => r.Level ?? "ok",
        ReadingStatus.Charging => L.T("no cabo", "wired"),
        _ => $"{r.Status} ({r.Detail})",
    };

    private static int CompareForDisplay(DeviceEntry a, DeviceEntry b)
    {
        // Suportados primeiro; entre eles, o mais fraco no topo.
        int rank = Rank(a).CompareTo(Rank(b));
        if (rank != 0) return rank;

        int pa = a.SortPercent ?? 1000;
        int pb = b.SortPercent ?? 1000;
        if (pa != pb) return pa.CompareTo(pb);

        return string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
    }

    private static int Rank(DeviceEntry e) => e.Reading.Status switch
    {
        ReadingStatus.Ok => 0,
        ReadingStatus.Charging => 1,
        ReadingStatus.NoData => 2,
        ReadingStatus.Error => 3,
        ReadingStatus.Disconnected => 4,
        ReadingStatus.Unsupported => 5,
        _ => 6,
    };

    /// <summary>
    /// Abre os listeners passivos (ex.: Rapoo). Chamado na inicializacao e em
    /// cada Atualizar; nunca em timer.
    /// </summary>
    public void EnsurePassiveListeners()
    {
        if (!_settings.PassiveListenersEnabled) return;
        try
        {
            var collections = HidInventory.Scan();
            foreach (var provider in _registry.HidProviders.OfType<IPassiveListenerProvider>())
            {
                try { provider.EnsureListening(collections); }
                catch (Exception ex) { Log.Error("EnsureListening", ex); }
            }
        }
        catch (Exception ex) { Log.Error("EnsurePassiveListeners", ex); }
    }

    public void Dispose()
    {
        foreach (var provider in _registry.HidProviders.OfType<IPassiveListenerProvider>())
        {
            try { provider.StopListening(); } catch { }
        }
        _oneAtATime.Dispose();
    }
}
