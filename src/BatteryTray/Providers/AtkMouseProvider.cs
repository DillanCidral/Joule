using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// ATK 1K DONGLE-PAW, protocolo Pixart observado em capturas locais.
/// Uma feature 00 82 00 ... de nove bytes solicita a bateria. A resposta
/// chega na entrada interrupt FF10/1, com o percentual em [9] no Windows.
/// </summary>
public sealed class AtkMouseProvider : IStandaloneProvider
{
    private const ushort Vid = 0x373B, Pid = 0x10F0;
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(3);

    public string Name => "ATK Pixart";
    public bool Handles(ushort vid, ushort pid) => vid == Vid && pid == Pid;

    public IEnumerable<DeviceEntry> Discover(IReadOnlyList<HidCollection> collections)
    {
        var claimed = collections.Where(c => Handles(c.Vid, c.Pid)).ToArray();
        if (claimed.Length == 0) yield break;

        yield return new DeviceEntry
        {
            Key = "atk:1k-paw",
            DisplayName = "ATK 1K DONGLE-PAW",
            HardwareId = Known.Id(Vid, Pid),
            ProviderName = Name,
            Kind = DeviceKind.Mouse,
            Origin = "Dongle",
            IsConnected = true,
            Reading = Read(claimed),
        };
    }

    internal static bool IsTarget(HidCollection c) =>
        c.Vid == Vid && c.Pid == Pid && c.UsagePage == 0xFF10 && c.Usage == 1
        && c.InputLen == 65 && c.FeatureLen == 9;

    private static BatteryReading Read(IReadOnlyList<HidCollection> claimed)
    {
        var targets = claimed.Where(IsTarget).ToArray();
        if (targets.Length != 1)
            return BatteryReading.NoData(L.T("canal FF10/1 do mouse não identificado", "mouse channel FF10/1 could not be identified"));

        using var session = HidRequest.Open(targets);
        if (session is null)
            return BatteryReading.NoData(L.T("canal de bateria do mouse indisponível", "mouse battery channel unavailable"));

        var report = session.AskFeature(targets[0], BatteryRequest(), r => Parse(r) is not null && r[1] == 0x82, ReplyTimeout);
        if (report is null)
            return BatteryReading.NoData(L.T("mouse sem resposta ou consulta desabilitada. Acorde o mouse e atualize",
                "mouse did not respond or queries are disabled. Wake the mouse and refresh"));

        var battery = Parse(report)!.Value;
        return BatteryReading.Percentage(battery.Percent,
            chargeText: battery.Charging ? L.T("carregando", "charging") : null);
    }

    internal static byte[] BatteryRequest()
    {
        var request = new byte[9];
        request[1] = 0x82;
        return request;
    }

    internal static (int Percent, bool Charging)? Parse(byte[] report)
    {
        if (report.Length != 65 || report[0] != 0) return null;
        bool status = report[1] == 0x81 && report[2] == 0x05 && report[3] == 0x01
                      && report[4] == 0 && report[5] == 1 && report[6] == 1;
        bool battery = report[1] == 0x82 && report[2] == 0x08 && report[3] == 0x02
                       && report[4] == 0 && report[5] == 0xFF && report[6] == 3;
        if ((!status && !battery) || report[7] != 0 || report[8] != 0 || report[10] != 1) return null;
        int percent = report[9] & 0x7F;
        return percent <= 100 ? (percent, (report[9] & 0x80) != 0) : null;
    }
}
