using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// ASUS ROG Pelta headset on its 2.4 GHz receiver (0B05:1B84). The receiver's
/// vendor collection (usage page 0xFF00, 64-byte reports) answers report 0xCC
/// command 0x12: sub-command 0x07 returns the level in byte 6, sub-command 0x08
/// returns the charging flag in byte 5. A reply echoes bytes 1-2 of the
/// question; FF AA in bytes 1-2 or 5-6 means the headset did not answer
/// (asleep or off). Only these two read questions are sent; no lighting or
/// configuration command.
/// </summary>
public sealed class RogPeltaProvider : IStandaloneProvider
{
    internal const ushort Vid = 0x0B05, Pid = 0x1B84;
    private const ushort VendorPage = 0xFF00;
    private const int ReportLength = 64;
    private const byte ReportId = 0xCC, Group = 0x12, BatteryCommand = 0x07, ChargeCommand = 0x08;
    private static readonly TimeSpan AskTimeout = TimeSpan.FromMilliseconds(1500);

    public string Name => "ROG Pelta";

    public bool Handles(ushort vid, ushort pid) => (vid, pid) == (Vid, Pid);

    public IEnumerable<DeviceEntry> Discover(IReadOnlyList<HidCollection> collections)
    {
        var claimed = collections.Where(c => Handles(c.Vid, c.Pid)).ToList();
        if (claimed.Count == 0) yield break;

        yield return new DeviceEntry
        {
            Key = "asus:rog-pelta",
            DisplayName = "ROG Pelta",
            HardwareId = string.Join(", ", claimed.Select(c => c.HardwareId).Distinct()),
            ProviderName = Name,
            Reading = Read(claimed),
            IsConnected = true,
            Kind = DeviceKind.Headset,
            Origin = "Dongle",
        };
    }

    private static BatteryReading Read(List<HidCollection> collections)
    {
        var target = collections.FirstOrDefault(c =>
            c.UsagePage == VendorPage && c.OutputLen >= ReportLength && c.InputLen >= ReportLength);
        if (target is null)
            return BatteryReading.NoData(L.T(
                "não achei o canal 0xFF00 do receptor",
                "couldn’t find the receiver’s 0xFF00 channel"));

        using var session = HidRequest.Open(new[] { target });
        if (session is null)
            return BatteryReading.NoData(L.T("o canal de bateria não abriu", "the battery channel didn’t open"));

        var battery = session.Ask(target, BuildRequest(BatteryCommand), r => IsReplyTo(r, BatteryCommand), AskTimeout);
        if (battery is null)
            return BatteryReading.NoData(L.T(
                "o receptor não respondeu; a pergunta pode estar sem permissão, o fone pode estar desligado ou o Armoury Crate pode estar usando o canal",
                "the receiver didn’t answer; the request may not be permitted, the headset may be off, or Armoury Crate may be holding the channel"));

        var level = ParseLevel(battery);
        if (level is null)
            return BatteryReading.NoData(L.T("o fone não informou um nível válido", "the headset didn’t report a valid level"));

        // The charging flag is a nice-to-have: no answer just means no flag.
        var charge = session.Ask(target, BuildRequest(ChargeCommand), r => IsReplyTo(r, ChargeCommand), AskTimeout);
        bool charging = charge is not null && ParseCharging(charge);
        return BatteryReading.Percentage(level.Value, chargeText: charging ? L.T("carregando", "charging") : null);
    }

    internal static byte[] BuildRequest(byte command) => new byte[] { ReportId, Group, command };

    internal static bool IsReplyTo(byte[] r, byte command) =>
        r.Length >= 7 && r[0] == ReportId && r[1] == Group && r[2] == command;

    private static bool IsError(byte[] r) =>
        r.Length >= 7 && ((r[1] == 0xFF && r[2] == 0xAA) || (r[5] == 0xFF && r[6] == 0xAA));

    /// <summary>Level 0-100 from a 0x07 reply; null when the reply is wrong, an error, or out of range.</summary>
    internal static int? ParseLevel(byte[] r) =>
        IsReplyTo(r, BatteryCommand) && !IsError(r) && r[6] <= 100 ? r[6] : null;

    internal static bool ParseCharging(byte[] r) =>
        IsReplyTo(r, ChargeCommand) && !IsError(r) && r[5] == 1;
}
