using BatteryTray.Providers;

namespace BatteryTray.Core;

/// <summary>
/// Registro plugavel de providers, indexado por VID:PID. Para dar suporte a um
/// hardware novo em outra maquina basta escrever o provider e registra-lo aqui:
/// nada mais no app precisa saber que ele existe.
/// </summary>
public sealed class ProviderRegistry
{
    private readonly Dictionary<string, IBatteryProvider> _byHardwareId = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IBatteryProvider> HidProviders { get; }
    public IReadOnlyList<IStandaloneProvider> StandaloneProviders { get; }

    private ProviderRegistry(IReadOnlyList<IBatteryProvider> hid, IReadOnlyList<IStandaloneProvider> standalone)
    {
        HidProviders = hid;
        StandaloneProviders = standalone;

        foreach (var provider in hid)
        {
            foreach (var (vid, pid) in provider.SupportedIds)
            {
                string id = Known.Id(vid, pid);
                if (_byHardwareId.TryGetValue(id, out var existing))
                {
                    Log.Write($"Conflito de registro: {id} reivindicado por {existing.Name} e {provider.Name}; mantendo {existing.Name}.");
                    continue;
                }
                _byHardwareId[id] = provider;
            }
        }
    }

    public static ProviderRegistry CreateDefault() => new(
        hid: new IBatteryProvider[]
        {
            new AttackSharkProvider(),
            new RapooProvider(),
            new IncottProvider(),
            new DualSenseProvider(),
        },
        standalone: new IStandaloneProvider[]
        {
            new XInputProvider(),
            new BluetoothPnpProvider(),
            new LogitechProvider(),
            new RazerProvider(),
            new SteelSeriesProvider(),
            new HyperXProvider(),
            new CorsairProvider(),
            new CompxProvider(),
            new ChineseBrandsProvider(),
        });

    public IBatteryProvider? Resolve(ushort vid, ushort pid) =>
        _byHardwareId.TryGetValue(Known.Id(vid, pid), out var p) ? p : null;
}
