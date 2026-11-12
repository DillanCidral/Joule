using BatteryTray.Interop;

namespace BatteryTray.Core;

public enum ReadingStatus
{
    /// <summary>Valor lido agora, confiavel.</summary>
    Ok,
    /// <summary>Dispositivo no cabo / carregando: percentual nao se aplica.</summary>
    Charging,
    /// <summary>Nao veio nada nesta leitura; se houver cache, ele e' exibido com a idade.</summary>
    NoData,
    /// <summary>Existe provider, mas o dispositivo nao esta presente agora.</summary>
    Disconnected,
    /// <summary>Nenhum provider conhece este VID:PID.</summary>
    Unsupported,
    /// <summary>Falha ao tentar ler.</summary>
    Error,
}

/// <summary>Resultado de uma tentativa de leitura. Imutavel.</summary>
public sealed class BatteryReading
{
    public ReadingStatus Status { get; init; }

    /// <summary>Percentual 0-100 quando o dispositivo entrega percentual.</summary>
    public int? Percent { get; init; }

    /// <summary>Faixa grosseira (XInput: EMPTY/LOW/MEDIUM/FULL) quando nao ha percentual.</summary>
    public string? Level { get; init; }

    /// <summary>Explicacao curta para a UI (motivo do NoData, tipo de bateria, etc).</summary>
    public string? Detail { get; init; }

    /// <summary>
    /// Quando o relatorio foi realmente capturado. Null = agora. Um listener
    /// passivo pode ter visto o valor ha minutos: e' esse instante que vale,
    /// nao o da chamada do Refresh -- caso contrario a UI diria "lido agora"
    /// para um numero antigo.
    /// </summary>
    public DateTime? ObservedUtc { get; init; }

    public DateTime EffectiveObservedUtc => ObservedUtc ?? DateTime.UtcNow;

    /// <summary>"carregando" / "carga completa" quando o aparelho informa, junto do percentual.</summary>
    public string? ChargeText { get; init; }

    public static BatteryReading Percentage(int percent, string? detail = null, DateTime? observedUtc = null, string? chargeText = null) =>
        new() { Status = ReadingStatus.Ok, Percent = Math.Clamp(percent, 0, 100), Detail = detail, ObservedUtc = observedUtc, ChargeText = chargeText };

    public static BatteryReading Coarse(string level, string? detail = null) =>
        new() { Status = ReadingStatus.Ok, Level = level, Detail = detail };

    public static BatteryReading Charging(string? detail = null) =>
        new() { Status = ReadingStatus.Charging, Detail = detail };

    public static BatteryReading NoData(string? detail = null) =>
        new() { Status = ReadingStatus.NoData, Detail = detail };

    public static BatteryReading Disconnected(string? detail = null) =>
        new() { Status = ReadingStatus.Disconnected, Detail = detail };

    public static BatteryReading Error(string detail) =>
        new() { Status = ReadingStatus.Error, Detail = detail };

    public static readonly BatteryReading Unsupported =
        new() { Status = ReadingStatus.Unsupported, Detail = L.T("sem provider para este VID:PID", "no provider for this VID:PID") };
}

/// <summary>Contexto entregue ao provider no momento da leitura.</summary>
public sealed class ReadContext
{
    public required IReadOnlyList<HidCollection> AllCollections { get; init; }

    /// <summary>Colecoes cujo VID:PID o provider reivindicou.</summary>
    public required IReadOnlyList<HidCollection> Claimed { get; init; }

    /// <summary>Preferencias do usuario (ex.: manter listeners passivos abertos).</summary>
    public required Settings Settings { get; init; }

    public IEnumerable<HidCollection> Find(ushort vid, ushort pid) =>
        AllCollections.Where(c => c.Vid == vid && c.Pid == pid);

    public bool IsPresent(ushort vid, ushort pid) =>
        AllCollections.Any(c => c.Vid == vid && c.Pid == pid);
}

/// <summary>
/// Um provider de bateria para uma familia de hardware.
/// Regra absoluta: provider nao escreve no dispositivo por conta propria. A unica
/// escrita e a pergunta de bateria, pela porta unica Interop/HidRequest.cs.
/// </summary>
public interface IBatteryProvider
{
    /// <summary>Nome tecnico do provider (aparece no diagnostico).</summary>
    string Name { get; }

    /// <summary>VID:PIDs que este provider reivindica.</summary>
    IReadOnlyList<(ushort Vid, ushort Pid)> SupportedIds { get; }

    /// <summary>
    /// Chave logica estavel do dispositivo. Varios VID:PID podem colapsar na
    /// mesma chave (ex.: dongle vs. cabo do mesmo mouse).
    /// </summary>
    string LogicalKey(ushort vid, ushort pid);

    /// <summary>Nome amigavel para a UI.</summary>
    string DisplayName(ReadContext ctx);

    /// <summary>Le a bateria. Chamado apenas sob demanda, nunca em laco de fundo.</summary>
    BatteryReading Read(ReadContext ctx);
}

/// <summary>
/// Provider que nao e' descoberto pela enumeracao HID (ex.: XInput, que expoe
/// slots em vez de colecoes). Ele mesmo descobre e le seus dispositivos.
/// </summary>
public interface IStandaloneProvider
{
    string Name { get; }
    /// <param name="collections">Colecoes HID ja enumeradas neste Refresh, para cruzar presenca sem varrer de novo.</param>
    IEnumerable<DeviceEntry> Discover(IReadOnlyList<HidCollection> collections);

    /// <summary>Este provider ja lista este VID:PID: a varredura HID nao cria a linha "sem suporte".</summary>
    bool Handles(ushort vid, ushort pid) => false;
}

/// <summary>Tipo do aparelho, so para o icone da linha.</summary>
public enum DeviceKind
{
    Generic,
    Mouse,
    Keyboard,
    Headset,
    Gamepad,
}

/// <summary>Um dispositivo mostrado na UI, com o resultado da ultima tentativa.</summary>
public sealed class DeviceEntry
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public string HardwareId { get; init; } = "";
    public string ProviderName { get; init; } = "";
    public required BatteryReading Reading { get; set; }

    public DeviceKind Kind { get; init; }

    /// <summary>Por onde o valor chega: "Dongle", "Bluetooth", "Cabo", "Adaptador".</summary>
    public string Origin { get; init; } = "";

    /// <summary>
    /// O valor se atualiza sozinho, sem tocar no aparelho: escuta passiva
    /// aberta ou Bluetooth lido do que o Windows ja guardou. As outras linhas
    /// so mudam num Atualizar.
    /// </summary>
    public bool IsLive { get; init; }

    /// <summary>Ultimo valor conhecido em disco, usado quando a leitura de agora nao trouxe nada.</summary>
    public CachedReading? Cache { get; set; }

    /// <summary>Ordem de exibicao: suportados primeiro.</summary>
    public bool IsSupported => Reading.Status != ReadingStatus.Unsupported;

    /// <summary>
    /// O dispositivo esta presente no barramento AGORA. Falso apenas para as
    /// linhas-fantasma (provider conhecido cujo hardware nao apareceu na
    /// enumeracao desta leitura).
    /// </summary>
    public bool IsConnected { get; init; }

    /// <summary>Tem algum valor de bateria para mostrar (lido agora ou do cache).</summary>
    public bool HasBatteryValue =>
        Reading.Status == ReadingStatus.Charging ||
        EffectivePercent.HasValue ||
        EffectiveLevel is { Length: > 0 };

    /// <summary>
    /// Criterio da lista principal do painel: plugado agora E com bateria para
    /// mostrar.
    /// </summary>
    public bool ShowsBattery => IsConnected && HasBatteryValue;

    /// <summary>
    /// Criterio da secao "outros": esta plugado agora, mas nao tem valor para
    /// mostrar (sem suporte, ou suportado e sem leitura ainda).
    ///
    /// Quem NAO esta plugado nao aparece no painel de jeito nenhum. O valor
    /// continua no cache e volta assim que o aparelho voltar; o que nao faz
    /// sentido e' encher a lista de aparelho que nao esta ali.
    /// </summary>
    public bool ShowsAsOther => IsConnected && !HasBatteryValue;

    /// <summary>Percentual a exibir agora: o lido, ou o do cache se a leitura veio vazia.</summary>
    public int? EffectivePercent =>
        Reading.Percent ?? (Reading.Status is ReadingStatus.NoData or ReadingStatus.Disconnected ? Cache?.Percent : null);

    public string? EffectiveLevel =>
        Reading.Level ?? (Reading.Status is ReadingStatus.NoData or ReadingStatus.Disconnected ? Cache?.Level : null);

    /// <summary>Valor numerico so para ordenar e colorir o icone.</summary>
    public int? SortPercent =>
        EffectivePercent ?? (EffectiveLevel is { Length: > 0 } lvl ? Known.ApproxPercentForLevel(lvl) : null);
}
