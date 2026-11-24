using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Nintendo Switch Pro Controller (057E:2009). LISTEN ONLY, nothing is sent:
/// it reads one report the controller already streams, like the DualSense.
///
/// The full input report 0x30 (and the subcommand reply 0x21) carries the
/// battery in byte 2: bits 7..5 = level 0..4 (empty, critical, low, medium,
/// full), bit 4 = charging. Format of Linux hid-nintendo, format only.
///
/// Over Bluetooth the controller starts in the simple mode (report 0x3F), which
/// has no battery. The 0x30 stream only starts after a program such as Steam
/// writes the "full report" subcommand, and writing to a device is not Joule's
/// job, so without such a program the reading is "no data". Checked on a real
/// controller over Bluetooth with Steam open: 0x30 reports, byte 2 = 0x40 (low).
///
/// The controller only gives five bands, so the reading is coarse, never a
/// made-up percentage.
/// </summary>
public sealed class SwitchProProvider : IBatteryProvider
{
    private const ushort Vid = 0x057E, Pid = 0x2009;
    private static readonly TimeSpan WaitForReport = TimeSpan.FromSeconds(2);

    public string Name => "Switch Pro Controller";

    public IReadOnlyList<(ushort Vid, ushort Pid)> SupportedIds { get; } = new[] { (Vid, Pid) };

    public string LogicalKey(ushort vid, ushort pid) => "nintendo:switch-pro";

    public string DisplayName(ReadContext ctx) => "Switch Pro Controller";

    public BatteryReading Read(ReadContext ctx)
    {
        var pads = ctx.Find(Vid, Pid)
            .Where(c => c.UsagePage == DualSenseProvider.GamepadUsagePage
                        && c.Usage == DualSenseProvider.GamepadUsage
                        && !HidInventory.IsSystemReserved(c.UsagePage, c.Usage))
            .ToList();

        if (pads.Count == 0)
            return BatteryReading.Disconnected(L.T("controle nao esta presente", "controller not present"));

        try
        {
            var report = HidOneShot.WaitForReport(pads[0], r => ParseStatus(r) is not null, WaitForReport);
            if (report is null)
                return BatteryReading.NoData(L.T(
                    "o controle só informa a bateria quando outro programa (como a Steam) está aberto",
                    "the controller only reports battery while another app (like Steam) is open"));

            var status = ParseStatus(report)!.Value;
            var details = new List<string>();
            if (status.Critical) details.Add(L.T("crítica", "critical"));
            if (status.Charging) details.Add(L.T("carregando", "charging"));
            if (pads.Count > 1) details.Add(L.T($"{pads.Count} controles; mostrando o primeiro", $"{pads.Count} controllers; showing the first"));

            return BatteryReading.Coarse(status.Level, details.Count > 0 ? string.Join(", ", details) : null);
        }
        catch (Exception ex)
        {
            Log.Error("SwitchPro.Read", ex);
            return BatteryReading.Error(ex.Message);
        }
    }

    internal readonly record struct ProStatus(string Level, bool Charging, bool Critical);

    /// <summary>
    /// Report 0x30 or 0x21 with at least 13 bytes. Null for the simple 0x3F
    /// report, anything shorter or a level above 4.
    /// </summary>
    internal static ProStatus? ParseStatus(byte[] r)
    {
        if (r.Length < 13 || (r[0] != 0x30 && r[0] != 0x21)) return null;

        int level = (r[2] & 0xE0) >> 5;
        bool charging = (r[2] & 0x10) != 0;
        string? band = level switch
        {
            4 => "FULL",
            3 => "MEDIUM",
            2 => "LOW",
            1 => "LOW",
            0 => "EMPTY",
            _ => null,
        };
        return band is null ? null : new ProStatus(band, charging, level == 1);
    }
}
