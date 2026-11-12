namespace BatteryTray.Core;

/// <summary>
/// Nomes amigaveis e notas para VID:PID conhecidos. Puramente cosmetico: nao
/// decide suporte (quem decide e' o ProviderRegistry).
/// </summary>
public static class Known
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1D57:FA60"] = "Mouse Attack Shark (dongle 2.4GHz)",
        ["1D57:FA61"] = L.T("Mouse Attack Shark (cabo)", "Attack Shark mouse (wired)"),
        ["24AE:1871"] = "Rapoo MT760 Mini",
        ["291D:385D"] = "MCHOSE V9 PRO (headset)",
        ["2DC8:310A"] = "8BitDo Ultimate 2C",
        ["2DC8:301C"] = L.T("8BitDo Ultimate 2C (dongle ocioso)", "8BitDo Ultimate 2C (idle dongle)"),
        ["054C:0CE6"] = "DualSense (PS5)",
        ["045E:0B12"] = "Xbox Wireless Controller",
        ["045E:02E6"] = "Xbox Wireless Adapter",
        ["045E:028E"] = "Xbox 360 Controller",
        // 02FF e' o PID que o XInput informa para o Xbox no cabo, seja qual for o modelo.
        ["045E:02FF"] = L.T("Controle Xbox", "Xbox Controller"),
    };

    /// <summary>
    /// Casos comprovadamente sem caminho de bateria NESTE TRANSPORTE. Aparecem
    /// listados como "sem suporte" com o motivo, para nao gastar tempo de novo
    /// procurando.
    ///
    /// O motivo diz "pelo dongle" de proposito: os dois aparelhos abaixo tem
    /// bateria legivel quando conectados por Bluetooth, onde o Windows entrega
    /// o percentual pelo BluetoothPnpProvider. Nao e' o aparelho que nao sabe a
    /// propria carga -- e' o receptor de 2.4GHz que nao conta.
    /// </summary>
    private static readonly Dictionary<string, string> DeadEnds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["291D:385D"] = L.T("Pelo dongle não há bateria (firmware 0004 do receptor); pelo Bluetooth o Windows informa.", "No battery through the dongle (receiver firmware 0004); over Bluetooth, Windows reports it."),
        ["2DC8:310A"] = L.T("Pelo dongle não há caminho: o XInput informa \"com fio\" falso. Pelo Bluetooth o Windows informa.", "No path through the dongle: XInput falsely reports \"wired\". Over Bluetooth, Windows reports it."),
        ["04CA:00BD"] = L.T("Sem caminho de bateria por este receptor: o teclado só responde a um comando que ninguém documentou.", "No battery path through this receiver: the keyboard only answers a command nobody has documented."),
        ["0951:16B7"] = L.T("Teclado com fio, sem bateria.", "Wired keyboard, no battery."),
    };

    /// <summary>Tipo por VID:PID, para o icone. O que nao estiver aqui cai na inferencia por usage.</summary>
    private static readonly Dictionary<string, DeviceKind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1D57:FA60"] = DeviceKind.Mouse,
        ["1D57:FA61"] = DeviceKind.Mouse,
        ["24AE:1871"] = DeviceKind.Mouse,
        ["291D:385D"] = DeviceKind.Headset,
        ["2DC8:310A"] = DeviceKind.Gamepad,
        ["2DC8:301C"] = DeviceKind.Gamepad,
        ["054C:0CE6"] = DeviceKind.Gamepad,
        ["045E:0B12"] = DeviceKind.Gamepad,
        ["045E:02E6"] = DeviceKind.Gamepad,
        ["045E:028E"] = DeviceKind.Gamepad,
        ["045E:02FF"] = DeviceKind.Gamepad,
        ["0951:16B7"] = DeviceKind.Keyboard,
        ["04CA:00BD"] = DeviceKind.Keyboard,
    };

    /// <summary>Aparelhos que so falam pelo cabo; o resto dos HID conhecidos vem por dongle.</summary>
    private static readonly HashSet<string> CableOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "054C:0CE6", // DualSense: so o cabo entrega bateria
        "1D57:FA61", // Attack Shark plugado no cabo
    };

    public static DeviceKind Kind(ushort vid, ushort pid) =>
        Kinds.TryGetValue(Id(vid, pid), out var k) ? k : DeviceKind.Generic;

    /// <summary>
    /// Tipo de um HID desconhecido pelas colecoes que ele declara. Mouse e
    /// teclado sao as usages reservadas do sistema (0x01/0x02, 0x01/0x06);
    /// joystick/gamepad 0x01/0x04-0x05; telefonia (0x0B) e' o controle de
    /// chamada que so fone tem.
    /// </summary>
    public static DeviceKind KindFromUsages(IEnumerable<(ushort Page, ushort Usage)> usages)
    {
        var set = usages.ToHashSet();
        if (set.Contains((0x01, 0x04)) || set.Contains((0x01, 0x05))) return DeviceKind.Gamepad;
        if (set.Any(u => u.Page == 0x0B)) return DeviceKind.Headset;
        // Muito mouse sem fio declara um "teclado" para macros: mouse ganha.
        if (set.Contains((0x01, 0x02))) return DeviceKind.Mouse;
        if (set.Contains((0x01, 0x06))) return DeviceKind.Keyboard;
        return DeviceKind.Generic;
    }

    /// <summary>
    /// Tipo de um aparelho Bluetooth. No classico vem do Class of Device
    /// (major 0x04 audio, 0x05 periferico com subtipo); no BLE essa propriedade
    /// nao existe e sobra o nome que o aparelho anuncia.
    /// </summary>
    public static DeviceKind BluetoothKind(uint? classOfDevice, string name)
    {
        if (classOfDevice is uint cod and not 0)
        {
            uint major = (cod >> 8) & 0x1F;
            uint minor = (cod >> 2) & 0x3F;
            if (major == 0x04) return DeviceKind.Headset;
            if (major == 0x05)
            {
                if ((minor & 0x0F) is 0x01 or 0x02) return DeviceKind.Gamepad;
                if ((minor & 0x20) != 0) return DeviceKind.Mouse;
                if ((minor & 0x10) != 0) return DeviceKind.Keyboard;
            }
        }

        string n = name.ToLowerInvariant();
        if (n.Contains("mouse")) return DeviceKind.Mouse;
        if (n.Contains("keyboard") || n.Contains("teclado")) return DeviceKind.Keyboard;
        if (n.Contains("controller") || n.Contains("gamepad") || n.Contains("8bitdo") || n.Contains("dualsense")) return DeviceKind.Gamepad;
        if (n.Contains("buds") || n.Contains("headset") || n.Contains("headphone") || n.Contains("earbud") || n.Contains("fone")) return DeviceKind.Headset;
        return DeviceKind.Generic;
    }

    public static string Origin(ushort vid, ushort pid) =>
        CableOnly.Contains(Id(vid, pid)) ? "Cabo" : "Dongle";

    /// <summary>Origem como aparece na tela: o dado fica em portugues (cache, comparacoes), so o rotulo traduz.</summary>
    public static string OriginLabel(string origin) => origin switch { "Cabo" => L.T("Cabo", "Wired"), "Adaptador" => L.T("Adaptador", "Adapter"), _ => origin };

    public static string Id(ushort vid, ushort pid) => $"{vid:X4}:{pid:X4}";

    public static string? Name(ushort vid, ushort pid) =>
        Names.TryGetValue(Id(vid, pid), out var n) ? n : null;

    public static string? DeadEndReason(ushort vid, ushort pid) =>
        DeadEnds.TryGetValue(Id(vid, pid), out var r) ? r : null;

    /// <summary>Percentual aproximado so para cor do icone/ordenacao; nunca exibido.</summary>
    public static int ApproxPercentForLevel(string? level) => level switch
    {
        "EMPTY" => 5,
        "LOW" => 20,
        "MEDIUM" => 60,
        "FULL" => 95,
        _ => 50,
    };

    public static string LevelLabelPtBr(string level) => level switch
    {
        "EMPTY" => L.T("Vazia", "Empty"),
        "LOW" => L.T("Baixa", "Low"),
        "MEDIUM" => L.T("Média", "Medium"),
        "FULL" => L.T("Cheia", "Full"),
        _ => level,
    };
}
