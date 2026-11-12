using BatteryTray.Core;

namespace BatteryTray.Ui;

/// <summary>
/// `Joule.exe --demo [alerta|vazio|sem-bateria][-widget]` (widget = expandido fixo): o painel com os dados
/// do design v1 (secao 6), sem tocar em hardware nenhum. Serve para conferir
/// os estados que o PC de desenvolvimento nao consegue produzir -- Bluetooth,
/// valor antigo, alerta. Nao le, nao grava cache, nao dispara notificacao.
/// </summary>
public static class Demo
{
    public static RefreshResult Result(string? variant)
    {
        variant = variant?.Replace("widget", "").Replace("boas-vindas", "").Trim('-', ' ');
        var now = DateTime.UtcNow;
        var list = new List<DeviceEntry>();

        DeviceEntry Dev(string name, DeviceKind kind, string origin, BatteryReading r, bool live = false) => new()
        {
            Key = name, DisplayName = name, Kind = kind, Origin = origin, Reading = r, IsLive = live, IsConnected = true,
        };

        DeviceEntry Other(string name, DeviceKind kind, string reason) =>
            Dev(name, kind, "", new BatteryReading { Status = ReadingStatus.Unsupported, Detail = reason });

        if (variant == "vazio") return new RefreshResult { Devices = list };

        if (variant == "sem-bateria")
        {
            list.Add(Other("HyperX Alloy FPS", DeviceKind.Keyboard, L.T("Teclado com fio, sem bateria.", "Wired keyboard, no battery.")));
            list.Add(Dev("Mouse Attack Shark", DeviceKind.Mouse, "Dongle", BatteryReading.NoData(L.T("mouse parado não transmite; mexa nele e atualize", "an idle mouse doesn’t transmit; move it and refresh"))));
            return new RefreshResult { Devices = list };
        }

        list.Add(Dev("MCHOSE V9 PRO", DeviceKind.Headset, "Bluetooth", BatteryReading.Percentage(80), live: true));
        list.Add(Dev("Mouse Attack Shark", DeviceKind.Mouse, "Dongle", BatteryReading.Percentage(variant == "alerta" ? 15 : 90), live: true));
        list.Add(Dev("Rapoo MT760 Mini", DeviceKind.Mouse, "Dongle",
            BatteryReading.Percentage(94, L.T("software da Rapoo fechado", "Rapoo software closed"), observedUtc: now.AddHours(-2))));
        list.Add(Dev("Moondrop Space Travel", DeviceKind.Headset, "Bluetooth", BatteryReading.Percentage(100), live: true));
        list.Add(Dev("DualSense (PS5)", DeviceKind.Gamepad, "Cabo",
            BatteryReading.Percentage(100, observedUtc: now.AddMinutes(-3), chargeText: L.T("carga completa", "fully charged"))));
        list.Add(Dev(L.T("Controle Xbox", "Xbox Controller"), DeviceKind.Gamepad, "Adaptador",
            new BatteryReading { Status = ReadingStatus.Ok, Level = "FULL", ObservedUtc = now.AddMinutes(-3) }));
        list.Add(Dev("8BitDo Ultimate 2C", DeviceKind.Gamepad, "Cabo",
            new BatteryReading { Status = ReadingStatus.Charging, ObservedUtc = now.AddMinutes(-3) }));

        list.Add(Other("HyperX Alloy FPS", DeviceKind.Keyboard, L.T("Teclado com fio, sem bateria.", "Wired keyboard, no battery.")));
        list.Add(Other("Dell Wireless Keyboard", DeviceKind.Keyboard,
            L.T("Sem caminho de bateria por este receptor: o teclado só responde depois de um comando, e o app não envia comandos.", "No battery path through this receiver: the keyboard only answers after a command, and the app sends no commands.")));
        list.Add(Other("MCHOSE V9 PRO (dongle)", DeviceKind.Headset,
            L.T("Pelo dongle não há bateria; a leitura vem pelo Bluetooth, na lista acima.", "No battery through the dongle; the reading comes over Bluetooth, in the list above.")));

        return new RefreshResult { Devices = list };
    }

    /// <summary>
    /// Os exemplos das boas-vindas, quando a leitura nao achou nenhum aparelho
    /// com bateria: modelos de topo que existem, para a vitrine (pedido do dono
    /// em 2026-11-05), sem etiqueta de "exemplo" (decisao dele: obvio pelo
    /// contexto). Numero inventado nunca passa por leitura de verdade: so o
    /// painel e o widget recebem, nunca o icone, o cache ou o alerta.
    /// </summary>
    public static RefreshResult Examples()
    {
        var now = DateTime.UtcNow;
        DeviceEntry Dev(string name, DeviceKind kind, string origin, BatteryReading r) => new()
        {
            Key = "exemplo:" + name, DisplayName = name, Kind = kind, Origin = origin, Reading = r, IsConnected = true,
        };
        return new RefreshResult
        {
            Devices = new List<DeviceEntry>
            {
                Dev("Razer Viper V3 Pro", DeviceKind.Mouse, "Dongle", BatteryReading.Percentage(82, observedUtc: now)),
                Dev("SteelSeries Arctis Nova Pro", DeviceKind.Headset, "Bluetooth", BatteryReading.Percentage(45, observedUtc: now.AddHours(-2))),
                Dev("DualSense Edge", DeviceKind.Gamepad, "Bluetooth", BatteryReading.Percentage(18, observedUtc: now)),
                Dev("Logitech MX Keys S", DeviceKind.Keyboard, "Bluetooth", BatteryReading.Percentage(64, observedUtc: now)),
                Dev("Wooting 80HE", DeviceKind.Keyboard, "", new BatteryReading
                {
                    Status = ReadingStatus.Unsupported,
                    Detail = L.T("Teclado com fio, sem bateria.", "Wired keyboard, no battery."),
                }),
            },
        };
    }
}
