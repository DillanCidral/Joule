using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Incott (receptor 8K 093A:522C, cabo 093A:622C). SOMENTE LEITURA: nenhuma
/// pergunta. Enquanto e' usado, o mouse manda sozinho o relatorio 0x09 na
/// colecao vendor 0xFF05:  09 &lt;bateria&gt; &lt;dpi/polling&gt; ...
/// bateria &lt;= 100 e' o %; acima de 100 esta carregando e o % e' bateria - 128.
///
/// Formato do OpenMouse (AGPL-3.0, src/incott/index.ts, conferido por eles num
/// ciclo de carga inteiro: 09 5F .. = 95%, 09 E1 .. = carregando 97%). A
/// pergunta 0x8E que o app oficial usa devolve um valor congelado, por isso
/// nao e' usada. Mesmo modelo do Attack Shark: um handle aberto, esperando ate
/// 3 s por um relatorio novo no Refresh. Sem Incott nesta casa.
/// </summary>
public sealed class IncottProvider : IBatteryProvider, IPassiveListenerProvider, IDisposable
{
    private const ushort Vid = 0x093A, PidDongle = 0x522C, PidCable = 0x622C;
    private const ushort VendorPage = 0xFF05;
    private const byte ReportId = 0x09;
    private static readonly TimeSpan WaitForReport = TimeSpan.FromSeconds(3);

    private readonly PassiveReportListener _listener = new(
        "Incott",
        c => c.Vid == Vid && (c.Pid == PidDongle || c.Pid == PidCable) && c.UsagePage == VendorPage,
        r => Parse(r)?.Percent);

    public string Name => "Incott";

    public IReadOnlyList<(ushort Vid, ushort Pid)> SupportedIds { get; } = new[] { (Vid, PidDongle), (Vid, PidCable) };

    // Receptor e cabo sao o mesmo mouse: uma unica linha na UI.
    public string LogicalKey(ushort vid, ushort pid) => "incott:8k";

    public string DisplayName(ReadContext ctx) => "Mouse Incott";

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
        bool cable = ctx.IsPresent(Vid, PidCable);
        if (!ctx.Find(Vid, cable ? PidCable : PidDongle).Any(c => c.UsagePage == VendorPage))
            return cable ? BatteryReading.Charging(L.T("no cabo", "wired")) : BatteryReading.Disconnected(L.T("colecao 0xFF05 nao encontrada", "collection 0xFF05 not found"));

        if (!ctx.Settings.PassiveListenersEnabled)
            return ReadOneShot(ctx.Find(Vid, cable ? PidCable : PidDongle).First(c => c.UsagePage == VendorPage), cable);

        try
        {
            _listener.Ensure(ctx.AllCollections);
            if (!_listener.IsListening)
                return BatteryReading.NoData(L.T("nao consegui abrir a colecao 0xFF05 para escuta", "couldn’t open collection 0xFF05 to listen"));

            var current = _listener.Last;
            if (current is null || DateTime.UtcNow - current.Value.SeenUtc > TimeSpan.FromSeconds(10))
                _listener.WaitForFreshReport(WaitForReport);

            var last = _listener.Last;
            var parsed = _listener.LastReport is { } raw ? Parse(raw) : null;
            if (last is null || parsed is null)
                return cable ? BatteryReading.Charging(L.T("no cabo", "wired")) : BatteryReading.NoData(L.T("mouse parado não transmite; mexa nele e atualize", "an idle mouse doesn’t transmit; move it and refresh"));

            return BatteryReading.Percentage(last.Value.Percent, observedUtc: last.Value.SeenUtc,
                chargeText: parsed.Value.Charging || cable ? L.T("carregando", "charging") : null);
        }
        catch (Exception ex)
        {
            Log.Error("Incott.Read", ex);
            return BatteryReading.Error(ex.Message);
        }
    }

    private static BatteryReading ReadOneShot(HidCollection collection, bool cable)
    {
        try
        {
            var report = HidOneShot.WaitForReport(collection, r => Parse(r) is not null, WaitForReport);
            if (report is null)
                return cable ? BatteryReading.Charging(L.T("no cabo", "wired")) : BatteryReading.NoData(L.T("nenhum relatorio de bateria em 3s (mouse parado?)", "no battery report in 3 s (idle mouse?)"));
            var p = Parse(report)!.Value;
            return BatteryReading.Percentage(p.Percent, chargeText: p.Charging || cable ? L.T("carregando", "charging") : null);
        }
        catch (Exception ex)
        {
            Log.Error("Incott.ReadOneShot", ex);
            return BatteryReading.Error(ex.Message);
        }
    }

    /// <summary>Relatorio com o id em [0]: 09 &lt;bateria&gt; ...</summary>
    internal static (int Percent, bool Charging)? Parse(byte[] r)
    {
        if (r.Length < 3 || r[0] != ReportId) return null;
        bool charging = r[1] > 100;
        int pct = charging ? r[1] - 128 : r[1];
        return pct is >= 0 and <= 100 ? (pct, charging) : null;
    }
}
