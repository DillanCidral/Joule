using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// ASUS ROG Pelta headset on its 2.4 GHz receiver (0B05:1B84). The receiver's
/// vendor collection (usage page 0xFF00, 64-byte reports) uses report 0xCC,
/// group 0x12:
///   - question 0x07: reply carries the level in byte 6;
///   - question 0x08: reply carries the charging flag in byte 5;
///   - 0x09, sent by the headset ON ITS OWN whenever the level changes
///     (captured passively on real hardware: CC 12 09 00 00 4A / 4B / 4C while
///     charging 74 -> 76%), level in byte 5.
/// A reply echoes bytes 1-2 of the question; FF AA in bytes 1-2 or 5-6 means the
/// headset did not answer (asleep or off).
///
/// Between readings Joule only listens for the 0x09 report (one handle, nothing
/// sent). Each on-demand reading asks 0x07 and 0x08 once through HidRequest. No
/// lighting or configuration command is used.
/// </summary>
public sealed class RogPeltaProvider : IBatteryProvider, IPassiveListenerProvider, IDisposable
{
    internal const ushort Vid = 0x0B05, Pid = 0x1B84;
    private const ushort VendorPage = 0xFF00;
    private const int ReportLength = 64;
    private const byte ReportId = 0xCC, Group = 0x12, BatteryCommand = 0x07, ChargeCommand = 0x08, LevelPush = 0x09;
    private static readonly TimeSpan AskTimeout = TimeSpan.FromMilliseconds(1500);

    private static bool IsTarget(HidCollection c) =>
        c.Vid == Vid && c.Pid == Pid && c.UsagePage == VendorPage && c.InputLen >= ReportLength && c.OutputLen >= ReportLength;

    private readonly PassiveReportListener _listener;
    private readonly ChargeTrend _trend = new();

    public RogPeltaProvider() =>
        _listener = new PassiveReportListener("ROG Pelta", IsTarget, r => _trend.Observe(ParsePush(r)));

    public string Name => "ROG Pelta";

    public IReadOnlyList<(ushort Vid, ushort Pid)> SupportedIds { get; } = new[] { (Vid, Pid) };

    public string LogicalKey(ushort vid, ushort pid) => "asus:rog-pelta";

    public string DisplayName(ReadContext ctx) => "ROG Pelta";

    public void EnsureListening(IReadOnlyList<HidCollection> collections) => _listener.Ensure(collections);

    public void StopListening() => _listener.Stop();

    public bool IsListening => _listener.IsListening;

    public event Action? ValueChanged
    {
        add => _listener.ValueChanged += value;
        remove => _listener.ValueChanged -= value;
    }

    public void Dispose() => _listener.Dispose();

    public BatteryReading Read(ReadContext ctx)
    {
        if (!ctx.IsPresent(Vid, Pid))
            return BatteryReading.Disconnected(L.T("receptor do ROG Pelta não está presente", "ROG Pelta receiver not present"));

        var target = ctx.AllCollections.FirstOrDefault(IsTarget);
        if (target is null)
            return BatteryReading.NoData(L.T("não achei o canal 0xFF00 do receptor", "couldn’t find the receiver’s 0xFF00 channel"));

        if (ctx.Settings.PassiveListenersEnabled)
        {
            try { _listener.Ensure(ctx.AllCollections); }
            catch (Exception ex) { Log.Error("RogPelta.Ensure", ex); }
        }

        // One question per on-demand reading. During a live refresh (or without
        // permission / in "Listen only") Ask returns null and the listener's value counts.
        using (var session = HidRequest.Open(new[] { target }))
        {
            if (session?.Ask(target, BuildRequest(BatteryCommand), r => IsReplyTo(r, BatteryCommand), AskTimeout) is { } battery
                && ParseLevel(battery) is int level)
            {
                var charge = session.Ask(target, BuildRequest(ChargeCommand), r => IsReplyTo(r, ChargeCommand), AskTimeout);
                bool? charging = charge is null ? null : ParseCharging(charge);
                _trend.Reset(level, charging);
                return BatteryReading.Percentage(level, observedUtc: DateTime.UtcNow, chargeText: charging == true ? ChargingText : null);
            }
        }

        if (_listener.Last is { } last)
            return BatteryReading.Percentage(last.Percent, observedUtc: last.SeenUtc, chargeText: _trend.Charging == true ? ChargingText : null);

        return BatteryReading.NoData(L.T(
            "o receptor não respondeu; a pergunta pode estar sem permissão, o fone pode estar desligado ou o Armoury Crate pode estar usando o canal",
            "the receiver didn’t answer; the request may not be permitted, the headset may be off, or Armoury Crate may be holding the channel"));
    }

    private static string ChargingText => L.T("carregando", "charging");

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

    /// <summary>Level 0-100 from the 0x09 report the headset sends on its own; null otherwise.</summary>
    internal static int? ParsePush(byte[] r) =>
        IsReplyTo(r, LevelPush) && !IsError(r) && r[5] <= 100 ? r[5] : null;

    /// <summary>
    /// Charging state between questions. The headset sends no charging event, only
    /// level changes, so: the asked flag holds until the level moves; a rising level
    /// means charging, a falling one means not. Unknown stays unknown.
    /// </summary>
    internal sealed class ChargeTrend
    {
        private readonly object _gate = new();
        private int? _level;
        private bool? _charging;

        public bool? Charging { get { lock (_gate) return _charging; } }

        public void Reset(int level, bool? charging)
        {
            lock (_gate) { _level = level; _charging = charging; }
        }

        public int? Observe(int? level)
        {
            if (level is not int now) return null;
            lock (_gate)
            {
                if (_level is int before && now != before) _charging = now > before;
                _level = now;
            }
            return now;
        }
    }
}
