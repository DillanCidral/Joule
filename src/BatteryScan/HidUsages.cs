namespace BatteryTray.Scan;

/// <summary>
/// Conhecimento puro sobre o descritor HID: quais usages significam bateria e
/// como classificar o transporte pelo caminho da interface. Sem I/O nenhum --
/// e' o que o ScanSelfTest consegue verificar sem hardware.
/// </summary>
public static class HidUsages
{
    /// <summary>
    /// Rotulo quando (page, usage) e' reconhecidamente de bateria; null se nao e'.
    ///
    /// O caminho que mais interessa e' o primeiro: pagina 0x06 (Generic Device
    /// Controls), usage 0x20 (Battery Strength). E' o percentual padronizado
    /// que o proprio Windows le em mouse/teclado Bluetooth -- se um dispositivo
    /// declara isso, da' para escrever um provider generico em vez de decifrar
    /// protocolo de fabricante.
    /// </summary>
    public static string? BatteryUsage(ushort page, ushort usage)
    {
        if (page == 0x0006 && usage == 0x0020)
            return "Battery Strength (padrao HID: page 0x06 usage 0x20)";

        if (page == 0x0085)
        {
            string? known = usage switch
            {
                0x0044 => "Charging",
                0x0045 => "Discharging",
                0x0046 => "FullyCharged",
                0x0047 => "FullyDischarged",
                0x0065 => "AbsoluteStateOfCharge",
                0x0066 => "RemainingCapacity",
                0x0067 => "FullChargeCapacity",
                0x0068 => "RunTimeToEmpty",
                0x0083 => "DesignCapacity",
                0x00D0 => "ACPresent",
                _ => null,
            };
            return known is null
                ? $"Battery System (page 0x85, usage 0x{usage:X2})"
                : $"Battery System: {known}";
        }

        if (page == 0x0084)
        {
            string? known = usage switch
            {
                0x0002 => "PresentStatus",
                0x0016 => "BatterySystem",
                0x0030 => "Voltage",
                0x0031 => "Current",
                0x0035 => "PercentLoad",
                _ => null,
            };
            return known is null
                ? $"Power Device (page 0x84, usage 0x{usage:X2})"
                : $"Power Device: {known}";
        }

        return null;
    }

    public static string UsagePageName(ushort page) => page switch
    {
        0x0001 => "Generic Desktop",
        0x0002 => "Simulation",
        0x0005 => "Game",
        0x0006 => "Generic Device Controls",
        0x0007 => "Keyboard",
        0x0008 => "LEDs",
        0x0009 => "Button",
        0x000A => "Ordinal",
        0x000B => "Telephony",
        0x000C => "Consumer",
        0x000D => "Digitizer",
        0x000F => "PID",
        0x0014 => "Alphanumeric Display",
        0x0020 => "Sensors",
        0x0084 => "Power Device",
        0x0085 => "Battery System",
        _ => page >= 0xFF00 ? "vendor-defined" : $"page 0x{page:X4}",
    };

    /// <summary>
    /// Transporte deduzido do caminho da interface HID. Os GUIDs sao os UUIDs
    /// de servico Bluetooth embutidos no caminho: 0x1124 = HID classico
    /// (HIDP), 0x1812 = HID sobre GATT (BLE).
    /// </summary>
    public static string Transport(string path)
    {
        string p = (path ?? "").ToLowerInvariant();

        if (p.Contains("00001812")) return "Bluetooth LE (HID over GATT)";
        if (p.Contains("00001124")) return "Bluetooth classico (HIDP)";
        if (p.Contains("bthledevice") || p.Contains("bthle")) return "Bluetooth LE";
        if (p.Contains("bthenum") || p.Contains("bthhfenum")) return "Bluetooth classico";
        if (p.Contains("virtual") || p.Contains("vhf") || p.Contains("root#")) return "virtual";
        if (p.Contains("hid#vid_") || p.Contains("usb#")) return "USB";
        return "desconhecido";
    }

    public static bool IsBluetooth(string transport) =>
        transport.StartsWith("Bluetooth", StringComparison.OrdinalIgnoreCase);

    /// <summary>Classe do dispositivo Bluetooth a partir do Class of Device.</summary>
    public static string DeviceKindFromCod(uint cod)
    {
        uint major = (cod >> 8) & 0x1F;
        uint minor = (cod >> 2) & 0x3F;

        if (major == 0x05)
        {
            return minor switch
            {
                0x10 or 0x11 or 0x12 or 0x13 or 0x14 or 0x15 or 0x16 or 0x17 => "teclado",
                0x20 or 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 => "mouse",
                0x30 or 0x31 or 0x32 or 0x33 or 0x34 or 0x35 or 0x36 or 0x37 => "teclado+mouse",
                0x08 => "joystick",
                0x0C => "gamepad",
                _ => "periferico (HID)",
            };
        }

        return major switch
        {
            0x00 => "diverso",
            0x01 => "computador",
            0x02 => "telefone",
            0x03 => "rede",
            0x04 => "audio/video",
            0x06 => "imagem",
            0x07 => "vestivel",
            0x08 => "brinquedo",
            0x09 => "saude",
            _ => $"major 0x{major:X2}",
        };
    }
}
