namespace BatteryTray.Scan;

/// <summary>
/// Tudo o que o coletor descobre numa maquina. Serializado como JSON (para eu
/// programar em cima) e renderizado como texto (para quem rodou entender o que
/// mandou). Nenhuma classe aqui toca em hardware: sao so dados.
/// </summary>
public sealed class ScanReport
{
    public string Tool { get; set; } = "JouleScan";
    public string Version { get; set; } = ScanInfo.Version;
    public DateTime GeneratedLocal { get; set; } = DateTime.Now;
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;

    public SystemInfo System { get; set; } = new();
    public List<HidDeviceInfo> Hid { get; set; } = new();
    public List<DeviceNodeInfo> DeviceNodes { get; set; } = new();
    public int DeviceNodesSkipped { get; set; }
    public BluetoothInfo Bluetooth { get; set; } = new();
    public List<XInputSlotInfo> XInput { get; set; } = new();
    public List<ProviderReadInfo> Providers { get; set; } = new();
    public CaptureInfo? Capture { get; set; }

    /// <summary>Aparelhos que a pessoa disse que nao aparecem ("Mandar meu aparelho").</summary>
    public List<string>? ReportedDevices { get; set; }

    /// <summary>Linhas do resumo: onde ha caminho de bateria plausivel.</summary>
    public List<CandidateInfo> Candidates { get; set; } = new();

    /// <summary>O que falhou durante a coleta (nunca derruba o scan).</summary>
    public List<string> Warnings { get; set; } = new();
}

public static class ScanInfo
{
    public const string Version = "1.0";
}

public sealed class SystemInfo
{
    public string MachineName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string OsCaption { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string DotNet { get; set; } = "";
    public string Culture { get; set; } = "";
    public bool Is64Bit { get; set; }
    public bool HasSystemBattery { get; set; }
    public List<string> VendorSoftwareRunning { get; set; } = new();
}

public sealed class HidDeviceInfo
{
    public string HardwareId { get; set; } = "";
    public string? KnownName { get; set; }
    public string Product { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Transport { get; set; } = "";
    public ushort Version { get; set; }
    public List<HidCollectionInfo> Collections { get; set; } = new();

    /// <summary>Rotulos de bateria declarados no descritor, agregados.</summary>
    public List<string> DeclaredBattery { get; set; } = new();
}

public sealed class HidCollectionInfo
{
    public string Path { get; set; } = "";
    public string UsagePage { get; set; } = "";
    public string Usage { get; set; } = "";
    public string UsagePageName { get; set; } = "";
    public int InputLen { get; set; }
    public int OutputLen { get; set; }
    public int FeatureLen { get; set; }
    public bool SystemReserved { get; set; }
    public string? CapsError { get; set; }
    public List<HidValueCapInfo> ValueCaps { get; set; } = new();
}

public sealed class HidValueCapInfo
{
    public string ReportType { get; set; } = "";
    public int ReportId { get; set; }
    public string UsagePage { get; set; } = "";
    public string Usage { get; set; } = "";
    public int BitSize { get; set; }
    public int ReportCount { get; set; }
    public int LogicalMin { get; set; }
    public int LogicalMax { get; set; }

    /// <summary>Preenchido quando a usage e' reconhecidamente de bateria.</summary>
    public string? Battery { get; set; }
}

public sealed class DeviceNodeInfo
{
    public string InstanceId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Class { get; set; } = "";
    public string Enumerator { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public List<string> HardwareIds { get; set; } = new();

    /// <summary>Valor de DEVPKEY_Bluetooth_Battery, se o Windows expuser.</summary>
    public int? BluetoothBattery { get; set; }

    /// <summary>Todas as propriedades lidas do devnode: chave -> valor renderizado.</summary>
    public Dictionary<string, string> Properties { get; set; } = new();

    /// <summary>Propriedades cujo nome ou chave cheira a bateria.</summary>
    public Dictionary<string, string> BatteryProperties { get; set; } = new();
}

public sealed class BluetoothInfo
{
    public bool RadioPresent { get; set; }
    public List<BluetoothRadioInfo> Radios { get; set; } = new();
    public List<BluetoothDeviceInfoDto> Devices { get; set; } = new();
    public string? Error { get; set; }
}

public sealed class BluetoothRadioInfo
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public uint ClassOfDevice { get; set; }
    public ushort Manufacturer { get; set; }
}

public sealed class BluetoothDeviceInfoDto
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public bool Connected { get; set; }
    public bool Paired { get; set; }
    public bool Authenticated { get; set; }
    public string DeviceKind { get; set; } = "";
    public uint ClassOfDevice { get; set; }
    public DateTime? LastSeenUtc { get; set; }
    public DateTime? LastUsedUtc { get; set; }
}

public sealed class XInputSlotInfo
{
    public int Slot { get; set; }
    public bool Connected { get; set; }
    public string HardwareId { get; set; } = "";
    public string SubType { get; set; } = "";
    public string BatteryType { get; set; } = "";
    public int BatteryLevel { get; set; }
    public string Interpretation { get; set; } = "";
}

public sealed class ProviderReadInfo
{
    public string DisplayName { get; set; } = "";
    public string HardwareId { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Status { get; set; } = "";
    public string Value { get; set; } = "";
    public string? Detail { get; set; }
}

public sealed class CaptureInfo
{
    public int Seconds { get; set; }
    public int CollectionsOpened { get; set; }
    public int CollectionsSkipped { get; set; }
    public List<string> SkippedReasons { get; set; } = new();
    public List<CaptureGroup> Groups { get; set; } = new();
}

/// <summary>Relatorios capturados de uma colecao, agrupados por report id.</summary>
public sealed class CaptureGroup
{
    public string HardwareId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Path { get; set; } = "";
    public string UsagePage { get; set; } = "";
    public int ReportId { get; set; }
    public int Length { get; set; }
    public int Count { get; set; }
    public List<string> Samples { get; set; } = new();
    public List<CaptureByteStat> Bytes { get; set; } = new();
}

public sealed class CaptureByteStat
{
    public int Offset { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }
    public int Distinct { get; set; }
    public bool Constant => Min == Max;

    /// <summary>Constante, no intervalo 1..100 e fora do byte 0: cara de percentual.</summary>
    public bool BatteryCandidate { get; set; }
}

public sealed class CandidateInfo
{
    public string Device { get; set; } = "";
    public string Path { get; set; } = "";
    public string Confidence { get; set; } = "";
    public string What { get; set; } = "";
}
