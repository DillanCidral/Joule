using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Scan;

/// <summary>
/// Roda os providers que o app ja tem, exatamente como o app roda: uma leitura
/// sob demanda, sem laco. Numa maquina alheia isso quase sempre nao acha nada
/// -- e e' esse o ponto: mostra preto no branco que o suporte atual e'
/// especifico do hardware daqui.
///
/// Desde 2026-11-12 nao usa as configuracoes nem o cache do Joule instalado:
/// so pergunta as marcas que quem roda permitiu no console, e nao grava nada.
/// </summary>
public static class ProvidersScan
{
    public static List<ProviderReadInfo> Collect(List<string> warnings, IEnumerable<string> allowedVids)
    {
        var result = new List<ProviderReadInfo>();

        try
        {
            var registry = ProviderRegistry.CreateDefault();
            var settings = new Settings { AskAllowedVids = allowedVids.ToList() };
            using var service = new ReadingService(registry, new ReadingCache { Persist = false }, settings);
            var refresh = service.Refresh();

            if (refresh.FatalError is { } fatal) warnings.Add($"providers: {fatal}");

            result.AddRange(From(refresh.Devices));
        }
        catch (Exception ex)
        {
            warnings.Add($"providers: {ex.GetType().Name}: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Marcas presentes que so dao bateria com pergunta e ainda nao foram
    /// testadas: as que pedem licenca antes da leitura. VID em hex e um nome
    /// para mostrar (o produto que o aparelho informa, ou o provider).
    /// </summary>
    public static List<(string Vid, string Label)> BrandsThatAsk(IReadOnlyList<HidCollection> collections, ProviderRegistry registry) =>
        collections
            .Where(c => !HidRequest.Tested.Contains(c.Vid) && registry.StandaloneProviders.Any(p => p.Handles(c.Vid, c.Pid)))
            .GroupBy(c => c.Vid)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                string product = g.Select(c => Known.Name(c.Vid, c.Pid) ?? c.Product).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                    ?? registry.StandaloneProviders.First(p => p.Handles(g.Key, g.First().Pid)).Name;
                return ($"{g.Key:X4}", product.Trim());
            })
            .ToList();

    /// <summary>
    /// Os aparelhos de uma leitura que ja aconteceu. Dentro do Joule e' isto que
    /// entra no relatorio: ler de novo so para o arquivo repetiria perguntas.
    /// </summary>
    public static IEnumerable<ProviderReadInfo> From(IEnumerable<DeviceEntry> devices) =>
        devices.Select(device => new ProviderReadInfo
        {
            DisplayName = device.DisplayName,
            HardwareId = device.HardwareId,
            Provider = device.ProviderName,
            Status = device.Reading.Status.ToString(),
            Value = device.Reading.Status == ReadingStatus.Charging ? "carregando"
                : device.EffectivePercent is int p ? $"{p}%"
                : device.EffectiveLevel is { Length: > 0 } lvl ? lvl
                : "--",
            Detail = device.Reading.Detail,
        });
}
