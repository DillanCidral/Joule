using System.Runtime.InteropServices;

namespace BatteryTray.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct XINPUT_BATTERY_INFORMATION
{
    public byte BatteryType;
    public byte BatteryLevel;
}

[StructLayout(LayoutKind.Sequential)]
public struct XINPUT_GAMEPAD
{
    public ushort wButtons;
    public byte bLeftTrigger;
    public byte bRightTrigger;
    public short sThumbLX, sThumbLY, sThumbRX, sThumbRY;
}

[StructLayout(LayoutKind.Sequential)]
public struct XINPUT_STATE
{
    public uint dwPacketNumber;
    public XINPUT_GAMEPAD Gamepad;
}

[StructLayout(LayoutKind.Sequential)]
public struct XINPUT_VIBRATION
{
    public ushort wLeftMotorSpeed;
    public ushort wRightMotorSpeed;
}

[StructLayout(LayoutKind.Sequential)]
public struct XINPUT_CAPABILITIES
{
    public byte Type;
    public byte SubType;
    public ushort Flags;
    public XINPUT_GAMEPAD Gamepad;
    public XINPUT_VIBRATION Vibration;
}

/// <summary>Estrutura do export nao documentado XInputGetCapabilitiesEx (ordinal 108).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct XINPUT_CAPABILITIES_EX
{
    public XINPUT_CAPABILITIES Capabilities;
    public ushort VendorId;
    public ushort ProductId;
    public ushort RevisionId;
    public uint ProductVersion;
}

/// <summary>
/// XInput somente-leitura. Nada de vibracao, nada de escrita.
/// XInputGetCapabilitiesEx e' resolvido por ordinal porque nao tem nome exportado;
/// e' o unico jeito de saber qual VID:PID esta em cada slot (o sufixo IG_nn do
/// InstanceId NAO corresponde ao indice do slot).
/// </summary>
public static class XInputNative
{
    public const uint ERROR_SUCCESS = 0;
    public const uint ERROR_DEVICE_NOT_CONNECTED = 1167;

    public const byte BATTERY_DEVTYPE_GAMEPAD = 0;
    public const byte BATTERY_DEVTYPE_HEADSET = 1;

    public const byte BATTERY_TYPE_DISCONNECTED = 0;
    public const byte BATTERY_TYPE_WIRED = 1;
    public const byte BATTERY_TYPE_ALKALINE = 2;
    public const byte BATTERY_TYPE_NIMH = 3;
    public const byte BATTERY_TYPE_UNKNOWN = 0xFF;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetCapabilitiesExDelegate(uint reserved, uint userIndex, uint flags, out XINPUT_CAPABILITIES_EX caps);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState14(uint userIndex, out XINPUT_STATE state);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetBatteryInformation")]
    private static extern uint XInputGetBatteryInformation14(uint userIndex, byte devType, out XINPUT_BATTERY_INFORMATION info);

    [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState13(uint userIndex, out XINPUT_STATE state);

    [DllImport("xinput1_3.dll", EntryPoint = "XInputGetBatteryInformation")]
    private static extern uint XInputGetBatteryInformation13(uint userIndex, byte devType, out XINPUT_BATTERY_INFORMATION info);

    private static bool? _use14;
    private static GetCapabilitiesExDelegate? _capsEx;
    private static bool _capsExResolved;

    /// <summary>Nome da DLL efetivamente em uso, ou null se XInput nao esta disponivel.</summary>
    public static string? DllInUse => _use14 switch
    {
        true => "xinput1_4.dll",
        false => "xinput1_3.dll",
        _ => null,
    };

    public static bool IsAvailable()
    {
        if (_use14.HasValue) return true;
        try { XInputGetState14(0, out _); _use14 = true; return true; }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        try { XInputGetState13(0, out _); _use14 = false; return true; }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return false;
    }

    public static uint GetState(uint userIndex, out XINPUT_STATE state)
    {
        if (!IsAvailable()) { state = default; return ERROR_DEVICE_NOT_CONNECTED; }
        return _use14 == true ? XInputGetState14(userIndex, out state) : XInputGetState13(userIndex, out state);
    }

    public static uint GetBatteryInformation(uint userIndex, byte devType, out XINPUT_BATTERY_INFORMATION info)
    {
        if (!IsAvailable()) { info = default; return ERROR_DEVICE_NOT_CONNECTED; }
        return _use14 == true
            ? XInputGetBatteryInformation14(userIndex, devType, out info)
            : XInputGetBatteryInformation13(userIndex, devType, out info);
    }

    /// <summary>VID/PID reais do controle no slot, via ordinal 108. Null se indisponivel.</summary>
    public static (ushort Vid, ushort Pid, byte SubType)? GetCapabilitiesEx(uint userIndex)
    {
        var fn = ResolveCapsEx();
        if (fn is null) return null;
        try
        {
            if (fn(1, userIndex, 0, out XINPUT_CAPABILITIES_EX caps) != ERROR_SUCCESS) return null;
            return (caps.VendorId, caps.ProductId, caps.Capabilities.SubType);
        }
        catch
        {
            return null;
        }
    }

    private static GetCapabilitiesExDelegate? ResolveCapsEx()
    {
        if (_capsExResolved) return _capsEx;
        _capsExResolved = true;
        try
        {
            IntPtr module = LoadLibrary("xinput1_4.dll");
            if (module == IntPtr.Zero) return null;
            IntPtr proc = GetProcAddress(module, new IntPtr(108));
            if (proc == IntPtr.Zero) return null;
            _capsEx = Marshal.GetDelegateForFunctionPointer<GetCapabilitiesExDelegate>(proc);
        }
        catch
        {
            _capsEx = null;
        }
        return _capsEx;
    }

    public static string BatteryTypeName(byte t) => t switch
    {
        BATTERY_TYPE_DISCONNECTED => "DISCONNECTED",
        BATTERY_TYPE_WIRED => "WIRED",
        BATTERY_TYPE_ALKALINE => "ALKALINE",
        BATTERY_TYPE_NIMH => "NIMH",
        BATTERY_TYPE_UNKNOWN => "UNKNOWN",
        _ => $"0x{t:X2}",
    };

    public static string SubTypeName(byte s) => s switch
    {
        1 => "Gamepad",
        2 => "Volante",
        3 => "Arcade stick",
        4 => "Flight stick",
        5 => "Dance pad",
        6 => "Guitarra",
        8 => "Bateria",
        0x13 => "Arcade pad",
        _ => "Controle",
    };
}
