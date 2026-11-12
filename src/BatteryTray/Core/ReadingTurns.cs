namespace BatteryTray.Core;

/// <summary>
/// Quem le e quando, na bandeja (regra "ler sob demanda" do SECURITY.md). Uma leitura completa
/// por vez (clique duplo no Atualizar vira uma so); a ao vivo e a de conexao
/// nunca correm durante a completa nem juntas; o aviso de conexao junta o que
/// entrou e saiu, espera <see cref="Settle"/> e le no maximo uma vez a cada
/// <see cref="Gap"/>. Saiu de TrayController em 2026-11-10 para o --selftest
/// conferir essas regras sem janela e sem aparelho. Chamado so da thread da UI.
/// </summary>
public sealed class ReadingTurns
{
    /// <summary>Um aparelho chega em varias colecoes seguidas: espera assentar.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(1500);

    /// <summary>No maximo uma leitura de conexao a cada Gap.</summary>
    public static readonly TimeSpan Gap = TimeSpan.FromSeconds(10);

    private readonly HashSet<string> _arrived = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastArrivalRead = DateTime.MinValue;
    private int _full;
    private int _live;

    /// <summary>Atualizar. False = ja tem uma leitura completa em curso.</summary>
    public bool TryStartFull() => Interlocked.Exchange(ref _full, 1) == 0;
    public void EndFull() => Volatile.Write(ref _full, 0);

    /// <summary>Ao vivo ou conexao. False = completa ou outra ao vivo em curso.</summary>
    public bool TryStartLive() => Volatile.Read(ref _full) == 0 && Interlocked.Exchange(ref _live, 1) == 0;
    public void EndLive() => Volatile.Write(ref _live, 0);

    /// <summary>O Windows avisou que uma colecao entrou ou saiu.</summary>
    public void Note(string path, bool arrived)
    {
        if (arrived) { _arrived.Add(path); _removed.Remove(path); }
        else { _removed.Add(path); _arrived.Remove(path); }
    }

    /// <summary>Ainda nao houve leitura completa: ela mesma vai ver tudo.</summary>
    public void Forget() { _arrived.Clear(); _removed.Clear(); }

    /// <summary>
    /// A espera venceu. Devolve o que marcar como saido e o que ler; se veio
    /// algo, a vez ao vivo ja esta tomada (chame <see cref="EndLive"/> no fim).
    /// Retry = em quanto tempo tentar de novo o que ficou para depois.
    /// </summary>
    public (IReadOnlyList<string> Removed, IReadOnlyList<string> Arrived, TimeSpan? Retry) Take(DateTime nowUtc)
    {
        if (_arrived.Count == 0 && _removed.Count == 0) return (Array.Empty<string>(), Array.Empty<string>(), null);
        if (!TryStartLive()) return (Array.Empty<string>(), Array.Empty<string>(), Settle);

        var removed = _removed.ToList();
        _removed.Clear();
        var arrived = new List<string>();
        TimeSpan? retry = null;
        var wait = Gap - (nowUtc - _lastArrivalRead);
        if (_arrived.Count > 0 && wait <= TimeSpan.Zero)
        {
            arrived = _arrived.ToList();
            _arrived.Clear();
            _lastArrivalRead = nowUtc;
        }
        else if (_arrived.Count > 0)
        {
            retry = wait;
        }

        if (removed.Count == 0 && arrived.Count == 0) EndLive();
        return (removed, arrived, retry);
    }
}
