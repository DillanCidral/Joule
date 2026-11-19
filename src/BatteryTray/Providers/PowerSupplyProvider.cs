using System.Text.RegularExpressions;
using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Linux only. Reads the batteries the kernel already exposes in
/// /sys/class/power_supply: Logitech (hid-logitech-hidpp), DualSense and
/// DualShock 4 (hid-playstation, hid-sony), Nintendo and Steam controllers,
/// Xbox over Bluetooth (xpadneo) and Bluetooth LE mice and keyboards
/// (hid-input). The Linux counterpart of <see cref="BluetoothPnpProvider"/>:
/// Joule talks to nothing, it reads what the driver stored.
/// </summary>
public sealed partial class PowerSupplyProvider : IStandaloneProvider
{
    private const string Root = "/sys/class/power_supply";

    /// <summary>VID:PID of the devices listed in the last Discover, and of the receivers they sit behind.</summary>
    private HashSet<(ushort, ushort)> _seen = new();

    public string Name => "Linux power_supply";

    public bool Handles(ushort vid, ushort pid) => _seen.Contains((vid, pid));

    // "0003:046D:4082.0004": bus, vendor, product of a HID device directory.
    [GeneratedRegex(@"([0-9A-Fa-f]{4}):([0-9A-Fa-f]{4}):([0-9A-Fa-f]{4})\.[0-9A-Fa-f]{4}")]
    private static partial Regex HidDir();

    public IEnumerable<DeviceEntry> Discover(IReadOnlyList<HidCollection> collections)
    {
        var found = new List<DeviceEntry>();
        var seen = new HashSet<(ushort, ushort)>();
        if (!Directory.Exists(Root)) { _seen = seen; return found; }

        foreach (var dir in Directory.GetFileSystemEntries(Root).OrderBy(d => d, StringComparer.Ordinal))
        {
            // Laptop battery and charger have scope System; a USB-C port has no HID device behind it.
            if (Read(dir, "scope") != "Device") continue;
            string? device = HidLinux.RealPath(Path.Combine(dir, "device"));
            var hids = device is null ? new List<Match>() : HidDir().Matches(device).ToList();
            if (hids.Count == 0) continue;

            foreach (var m in hids) seen.Add((Hex(m.Groups[2].Value), Hex(m.Groups[3].Value)));
            var last = hids[^1];
            ushort vid = Hex(last.Groups[2].Value), pid = Hex(last.Groups[3].Value);
            bool bluetooth = last.Groups[1].Value == "0005";

            string name = Read(dir, "model_name") is { Length: > 0 } model ? model
                : HidName(device!) ?? Known.Name(vid, pid) ?? Path.GetFileName(dir);
            bool online = Read(dir, "online") != "0";
            found.Add(new DeviceEntry
            {
                Key = $"ps:{Path.GetFileName(dir)}",
                DisplayName = name,
                HardwareId = Known.Id(vid, pid),
                ProviderName = Name,
                Reading = online ? ToReading(Read(dir, "capacity"), Read(dir, "capacity_level"), Read(dir, "status"))
                                 : BatteryReading.Disconnected(L.T("desligado", "turned off")),
                IsConnected = online,
                Kind = Known.Kind(vid, pid) is var k and not DeviceKind.Generic ? k : Known.BluetoothKind(null, name),
                Origin = bluetooth ? "Bluetooth" : Known.Origin(vid, pid),
                IsLive = false,
            });
        }
        _seen = seen;
        return found;
    }

    /// <summary>sysfs values to a reading. Never invents a number: no capacity and no level is "no data".</summary>
    internal static BatteryReading ToReading(string? capacity, string? level, string? status)
    {
        string? charge = status switch
        {
            "Charging" => L.T("carregando", "charging"),
            "Full" => L.T("carga completa", "fully charged"),
            _ => null,
        };
        if (int.TryParse(capacity, out int pct) && pct is >= 0 and <= 100)
            return BatteryReading.Percentage(pct, chargeText: charge);
        // capacity_level uses the kernel's words; Joule's coarse levels are XInput's.
        string? coarse = level switch
        {
            "Critical" => "EMPTY",
            "Low" => "LOW",
            "Normal" or "High" => "MEDIUM",
            "Full" => "FULL",
            _ => null,
        };
        if (coarse is not null) return BatteryReading.Coarse(coarse);
        if (status == "Charging") return BatteryReading.Charging();
        return BatteryReading.NoData(L.T("o driver ainda nao informou a bateria", "the driver has not reported the battery yet"));
    }

    private static string? HidName(string device)
    {
        string? line = Read(device, "uevent")?.Split('\n').FirstOrDefault(l => l.StartsWith("HID_NAME="));
        return line?["HID_NAME=".Length..] is { Length: > 0 } n ? n : null;
    }

    private static ushort Hex(string s) => Convert.ToUInt16(s, 16);

    private static string? Read(string dir, string file)
    {
        try { return File.ReadAllText(Path.Combine(dir, file)).Trim(); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
