using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Provider generico de Bluetooth. SOMENTE LEITURA -- na verdade, nem chega a
/// tocar no dispositivo: le a propriedade DEVPKEY_Bluetooth_Battery do devnode,
/// que e' o mesmo numero que aparece em Configuracoes &gt; Bluetooth. Quem
/// conversou por radio foi a pilha do Windows (GATT 0x180F no BLE, indicador de
/// bateria do HFP no classico); aqui so se le o resultado que ela ja guardou.
///
/// E' o provider de maior alcance do app: nas 8 capturas do notebook com radio
/// BT ele resolveu 5 de 6 familias -- Rapoo BT Mouse, Attack Shark R1 em BT,
/// 8BitDo Ultimate 2C, Moondrop Space Travel e MCHOSE V9 PRO -- sem decifrar
/// protocolo nenhum. Os dois ultimos, alias, sao "sem caminho" no dongle de
/// 2.4GHz e resolvem-se aqui: o que muda nao e' o aparelho, e' o transporte.
///
/// O unico dos testados que NAO aparece por aqui e' o DualSense; para ele
/// existe o <see cref="DualSenseProvider"/>, no cabo.
///
/// Cuidado que define o provider: o percentual CONGELA quando o aparelho
/// desliga -- o Windows guarda o ultimo valor indefinidamente. Por isso a
/// leitura sempre carrega o carimbo de tempo do proprio Windows
/// (ObservedUtc), e o aparelho desligado entra com IsConnected = false, indo
/// para a secao "outros" com a idade a mostra em vez de posar de leitura de
/// agora.
/// </summary>
public sealed class BluetoothPnpProvider : IStandaloneProvider
{
    public string Name => "Bluetooth";

    public IEnumerable<DeviceEntry> Discover(IReadOnlyList<HidCollection> collections)
    {
        var found = new List<DeviceEntry>();

        List<BluetoothBatteryDevice> devices;
        try
        {
            devices = BluetoothBatteryNodes.Collect();
        }
        catch (Exception ex)
        {
            Log.Error("Bluetooth.Collect", ex);
            return found;
        }

        foreach (var device in devices)
        {
            found.Add(new DeviceEntry
            {
                Key = $"bt:{device.Address}",
                DisplayName = device.Name,
                HardwareId = FormatAddress(device.Address),
                ProviderName = Name,
                Reading = ToReading(device),
                IsConnected = device.IsConnected,
                Kind = Known.BluetoothKind(device.ClassOfDevice, device.Name),
                Origin = "Bluetooth",
                // So le o que o Windows ja guardou: pode reler sozinho sem tocar no aparelho.
                IsLive = device.IsConnected,
            });
        }

        return found;
    }

    internal static BatteryReading ToReading(BluetoothBatteryDevice device)
    {
        string? detail = (device.IsConnected, device.UpdatedUtc) switch
        {
            // Desligado: o numero e' o ultimo que o Windows viu, nao o de agora.
            (false, not null) => L.T("desconectado; ultimo valor conhecido", "disconnected; last known value"),
            (false, null) => L.T("desconectado; o Windows nao disse quando leu", "disconnected; Windows didn’t say when it read it"),
            (true, null) => L.T("o Windows nao disse quando leu", "Windows didn’t say when it read it"),
            _ => null,
        };

        return BatteryReading.Percentage(device.Percent, detail, observedUtc: device.UpdatedUtc);
    }

    /// <summary>a1b2c3d4e5f6 -> A1:B2:C3:D4:E5:F6, so para a linha de diagnostico.</summary>
    internal static string FormatAddress(string address)
    {
        if (address.Length != 12) return address.ToUpperInvariant();

        var parts = new string[6];
        for (int i = 0; i < 6; i++) parts[i] = address.Substring(i * 2, 2).ToUpperInvariant();
        return string.Join(':', parts);
    }
}
