using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// ASUS ROG Pelta headset on its 2.4 GHz receiver (0B05:1B84). The receiver's
/// vendor collection (usage page 0xFF00, 64-byte reports) uses report 0xCC,
/// group 0x12:
///   - 0x07: answer to the question carries the level in byte 6;
///   - 0x08: charging flag in byte 5 (1 = charging). It is the answer to the
///     0x08 question AND the headset sends it ON ITS OWN when the cable is
///     plugged or unplugged (captured: 01 / 00 / 01 as the cable went in, out, in);
///   - 0x09: sent by the headset ON ITS OWN whenever the level changes
///     (captured: CC 12 09 00 00 4A / 4B / 4C while charging 74 -> 76%), level in byte 5.
/// A reply echoes bytes 1-2 of the question; FF AA in bytes 1-2 or 5-6 means the
/// headset did not answer (asleep or off).
///
/// Between readings Joule only listens for 0x08 and 0x09 (one handle, nothing
/// sent), so level and charging update live. Each on-demand reading asks 0x07
/// and 0x08 once through HidRequest. No lighting or configuration command.
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
    private readonly PeltaState _state = new();

    /// <summary>Charging flipped on the open handle; the level listener only notifies on a new percentage.</summary>
    private event Action? ChargingChanged;

    public RogPeltaProvider() =>
        _listener = new PassiveReportListener("ROG Pelta", IsTarget, OnReport);

    /// <summary>Runs on the listener's thread for every report on the open handle.</summary>
    private int? OnReport(byte[] r)
    {
        if (ParseChargePush(r) is bool charging)
        {
            if (_state.SetCharging(charging))
            {
                Log.Write($"ROG Pelta: {(charging ? "carregando" : "fora do carregador")}");
                try { ChargingChanged?.Invoke(); }
                catch (Exception ex) { Log.Error("RogPelta.ChargingChanged", ex); }
            }
            return null;
        }
        return _state.Observe(ParsePush(r));
    }

    public string Name => "ROG Pelta";

    public IReadOnlyList<(ushort Vid, ushort Pid)> SupportedIds { get; } = new[] { (Vid, Pid) };

    public string LogicalKey(ushort vid, ushort pid) => "asus:rog-pelta";

    public string DisplayName(ReadContext ctx) => "ROG Pelta";

    public void EnsureListening(IReadOnlyList<HidCollection> collections) => _listener.Ensure(collections);

    public void StopListening() => _listener.Stop();

    public bool IsListening => _listener.IsListening;

    public event Action? ValueChanged
    {
        add { _listener.ValueChanged += value; ChargingChanged += value; }
        remove { _listener.ValueChanged -= value; ChargingChanged -= value; }
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
        // permission / in "Listen only") Ask returns null and what was heard counts.
        using (var session = HidRequest.Open(new[] { target }))
        {
            if (session?.Ask(target, BuildRequest(BatteryCommand), r => IsReplyTo(r, BatteryCommand), AskTimeout) is { } battery
                && ParseLevel(battery) is int level)
            {
                var charge = session.Ask(target, BuildRequest(ChargeCommand), r => IsReplyTo(r, ChargeCommand), AskTimeout);
                bool? charging = charge is null ? null : ParseCharging(charge);
                _state.Reset(level, charging);
                return BatteryReading.Percentage(level, observedUtc: DateTime.UtcNow, chargeText: charging == true ? ChargingText : null);
            }
        }

        if (_state.Snapshot() is { Level: int known } snap)
            return BatteryReading.Percentage(known, observedUtc: snap.SeenUtc, chargeText: snap.Charging == true ? ChargingText : null);

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

    /// <summary>Charging flag from a 0x08 report (sent on its own on plug/unplug); null unless byte 5 is 0 or 1.</summary>
    internal static bool? ParseChargePush(byte[] r) =>
        IsReplyTo(r, ChargeCommand) && !IsError(r) && r[5] <= 1 ? r[5] == 1 : null;

    /// <summary>Level 0-100 from the 0x09 report the headset sends on its own; null otherwise.</summary>
    internal static int? ParsePush(byte[] r) =>
        IsReplyTo(r, LevelPush) && !IsError(r) && r[5] <= 100 ? r[5] : null;

    /// <summary>
    /// Last known level and charging state, from questions and from what the headset
    /// sends on its own. A 0x08 report sets charging directly. Without one, a level
    /// that rises means charging and one that falls means not. Unknown stays unknown.
    /// </summary>
    internal sealed class PeltaState
    {
        private readonly object _gate = new();
        private int? _level;
        private bool? _charging;
        private DateTime _seenUtc;

        public bool? Charging { get { lock (_gate) return _charging; } }

        public (int? Level, bool? Charging, DateTime SeenUtc) Snapshot()
        {
            lock (_gate) return (_level, _charging, _seenUtc);
        }

        public void Reset(int level, bool? charging)
        {
            lock (_gate) { _level = level; _charging = charging; _seenUtc = DateTime.UtcNow; }
        }

        /// <summary>True when the state changed.</summary>
        public bool SetCharging(bool charging)
        {
            lock (_gate)
            {
                bool changed = _charging != charging;
                _charging = charging;
                if (_level is not null) _seenUtc = DateTime.UtcNow;
                return changed;
            }
        }

        public int? Observe(int? level)
        {
            if (level is not int now) return null;
            lock (_gate)
            {
                if (_level is int before && now != before) _charging = now > before;
                _level = now;
                _seenUtc = DateTime.UtcNow;
            }
            return now;
        }
    }
}
