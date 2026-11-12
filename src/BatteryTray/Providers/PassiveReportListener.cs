using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Escuta passiva reaproveitavel: um unico handle aberto numa colecao HID,
/// guardando o ultimo relatorio de bateria valido com o instante em que ele
/// chegou. NADA e' escrito no dispositivo e nao ha laco de verificacao: o
/// relatorio ou trafega no barramento por conta propria (Attack Shark), ou
/// trafega porque o software do fabricante perguntou (Rapoo).
///
/// A reabertura so acontece se o handle anterior tiver morrido, e apenas
/// dentro de uma leitura sob demanda.
/// </summary>
internal sealed class PassiveReportListener : IDisposable
{
    private readonly string _label;
    private readonly Func<HidCollection, bool> _matches;
    private readonly Func<byte[], int?> _parsePercent;

    private readonly object _gate = new();
    private readonly List<ManualResetEventSlim> _waiters = new();

    private HidPassiveReader? _reader;
    private int? _lastPercent;
    private DateTime _lastSeenUtc;

    private readonly int _confirmReports;
    private int? _candidate;
    private int _candidateCount;

    /// <param name="confirmReports">
    /// Quantos relatorios seguidos com o mesmo valor novo antes de troca-lo.
    /// O Attack Shark na divisa entre dois niveis manda 80 com picos de 90 que
    /// duram um relatorio so (2 s): sem confirmar, cada pico repintava painel,
    /// widget e icone e enchia o log. 1 = troca na hora (Rapoo, que fala pouco).
    /// </param>
    public PassiveReportListener(string label, Func<HidCollection, bool> matches, Func<byte[], int?> parsePercent, int confirmReports = 1)
    {
        _label = label;
        _matches = matches;
        _parsePercent = parsePercent;
        _confirmReports = Math.Max(1, confirmReports);
    }

    /// <summary>
    /// Percentual mudou -- ou continua chegando o mesmo ha 5 min, para quem
    /// mostra a idade nao achar que o valor envelheceu. Thread do leitor.
    /// </summary>
    public event Action? ValueChanged;

    private static readonly TimeSpan StillAliveEvery = TimeSpan.FromMinutes(5);
    private DateTime _lastNotifiedUtc;

    public bool IsListening
    {
        get { lock (_gate) return _reader is not null && !_reader.DeviceLost; }
    }

    public (int Percent, DateTime SeenUtc)? Last
    {
        get
        {
            lock (_gate) return _lastPercent is int p ? (p, _lastSeenUtc) : null;
        }
    }

    /// <summary>Ultimo relatorio que o parser aceitou (o Attack Shark tira dele o modelo).</summary>
    public byte[]? LastReport { get { lock (_gate) return _lastReport; } }
    private byte[]? _lastReport;

    /// <summary>Abre o listener se ainda nao houver um vivo na colecao certa.</summary>
    public void Ensure(IReadOnlyList<HidCollection> collections)
    {
        var target = collections.FirstOrDefault(c =>
            !HidInventory.IsSystemReserved(c.UsagePage, c.Usage) && c.InputLen > 0 && _matches(c));

        lock (_gate)
        {
            if (target is null)
            {
                // Dispositivo sumiu: parar de tocar nele.
                if (_reader is not null)
                {
                    Log.Write($"{_label}: colecao sumiu, encerrando listener.");
                    _reader.Dispose();
                    _reader = null;
                }
                return;
            }

            if (_reader is not null)
            {
                bool alive = !_reader.DeviceLost &&
                             string.Equals(_reader.Path, target.Path, StringComparison.OrdinalIgnoreCase);
                if (alive) return;

                Log.Write($"{_label}: listener encerrado (err={_reader.LastError}); reabrindo.");
                _reader.Dispose();
                _reader = null;
            }

            _reader = HidPassiveReader.Open(target, OnReport);
            Log.Write(_reader is null
                ? L.T($"{_label}: nao consegui abrir {target.Path} para leitura.", $"{_label}: couldn’t open {target.Path} for reading.")
                : $"{_label}: escutando {target.Path}");
        }
    }

    internal void OnReport(byte[] report)
    {
        int? percent = _parsePercent(report);
        if (percent is null) return;

        ManualResetEventSlim[] waiters;
        bool changed, notify;
        lock (_gate)
        {
            // O primeiro valor entra direto; uma troca precisa se repetir.
            bool accept = _lastPercent is null || _lastPercent == percent;
            if (!accept)
            {
                _candidateCount = _candidate == percent ? _candidateCount + 1 : 1;
                _candidate = percent;
                accept = _candidateCount >= _confirmReports;
            }
            if (accept) { _candidate = null; _candidateCount = 0; }

            changed = accept && _lastPercent != percent;
            if (accept) _lastPercent = percent;
            _lastReport = report;
            _lastSeenUtc = DateTime.UtcNow; // o aparelho esta vivo, mesmo que o valor ainda nao tenha trocado
            notify = changed || _lastSeenUtc - _lastNotifiedUtc > StillAliveEvery;
            if (notify) _lastNotifiedUtc = _lastSeenUtc;
            waiters = _waiters.ToArray();
        }

        // O Attack Shark repete o mesmo relatorio a cada 2s enquanto o mouse
        // esta acordado: registrar todos enche o log de ruido e ainda estoura a
        // rotacao, jogando fora o que interessa. So o primeiro e as mudancas.
        if (changed) Log.Write($"{_label}: bateria {percent}% ({BitConverter.ToString(report)})");
        foreach (var w in waiters)
        {
            try { w.Set(); } catch (ObjectDisposedException) { }
        }

        if (notify)
        {
            try { ValueChanged?.Invoke(); }
            catch (Exception ex) { Log.Error($"{_label}.ValueChanged", ex); }
        }
    }

    /// <summary>Espera ate <c>timeout</c> por um relatorio novo no handle que ja esta aberto.</summary>
    public bool WaitForFreshReport(TimeSpan timeout)
    {
        if (!IsListening) return false;

        var signal = new ManualResetEventSlim(false);
        lock (_gate) _waiters.Add(signal);
        try
        {
            return signal.Wait(timeout);
        }
        finally
        {
            lock (_gate) _waiters.Remove(signal);
            signal.Dispose();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _reader?.Dispose();
            _reader = null;
        }
    }

    public void Dispose() => Stop();
}
