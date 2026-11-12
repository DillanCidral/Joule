using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Mouse Rapoo MT760 Mini. SOMENTE LEITURA.
///
/// Formato (7 bytes):  BB B0 01 40 06 01 &lt;bat&gt;   =&gt;  bateria = &lt;bat&gt; (% direto)
///
/// Este mouse NAO e' autonomo: so responde quando o software do fabricante
/// (RapooOfficeDev.exe) pergunta, o que acontece a cada ~90-105s. A estrategia
/// e' a de risco zero: um listener passivo fica aberto enquanto o app roda e
/// cacheia o ultimo valor visto com o instante em que ele chegou. Nada e'
/// enviado ao mouse. Sem o RapooOfficeDev.exe rodando nao ha leitura nova --
/// nesse caso a UI mostra o valor em cache com a idade dele, nunca um valor
/// inventado.
/// </summary>
public sealed class RapooProvider : IBatteryProvider, IPassiveListenerProvider, IDisposable
{
    private const ushort Vid = 0x24AE;
    private const ushort Pid = 0x1871;

    private const ushort VendorUsagePage = 0xFF00;
    private const ushort VendorUsage = 0x0002;
    private const int ReportLength = 7;

    private readonly PassiveReportListener _listener = new(
        "Rapoo",
        c => c.Vid == Vid && c.Pid == Pid && c.UsagePage == VendorUsagePage
             && c.Usage == VendorUsage && c.InputLen == ReportLength,
        ParsePercent);

    public string Name => "Rapoo";

    public IReadOnlyList<(ushort Vid, ushort Pid)> SupportedIds { get; } = new[] { (Vid, Pid) };

    public string LogicalKey(ushort vid, ushort pid) => "rapoo:mt760";

    public string DisplayName(ReadContext ctx) => "Rapoo MT760 Mini";

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
            return BatteryReading.Disconnected(L.T("dongle do MT760 nao esta presente", "MT760 dongle not present"));

        if (!ctx.Settings.PassiveListenersEnabled)
            return BatteryReading.NoData(L.T("escuta passiva desligada; este mouse so responde ao software do fabricante", "passive listening off; this mouse only answers the maker’s software"));

        try
        {
            _listener.Ensure(ctx.AllCollections);
        }
        catch (Exception ex)
        {
            Log.Error("Rapoo.Ensure", ex);
            return BatteryReading.Error(ex.Message);
        }

        var last = _listener.Last;
        if (last is not null)
            return BatteryReading.Percentage(last.Value.Percent,
                ManufacturerAppRunning() ? null : L.T("software da Rapoo fechado", "Rapoo software closed"),
                observedUtc: last.Value.SeenUtc);

        return BatteryReading.NoData(_listener.IsListening
            ? L.T("o valor chega quando o software da Rapoo consultar o mouse (a cada ~90 s)", "the value arrives when the Rapoo software queries the mouse (every ~90 s)")
            : L.T("nao foi possivel abrir a colecao 0xFF00 para escuta", "couldn’t open collection 0xFF00 to listen"));
    }

    /// <summary>Sem o RapooOfficeDev.exe o mouse nao fala: o valor so envelhece.</summary>
    private static bool ManufacturerAppRunning()
    {
        try
        {
            var found = System.Diagnostics.Process.GetProcessesByName("RapooOfficeDev");
            foreach (var p in found) p.Dispose();
            return found.Length > 0;
        }
        catch { return true; } // na duvida, nao culpa o software
    }

    /// <summary>Assinatura BB B0 .. 40 .. .. bat, com bat &lt;= 100. Devolve o percentual ou null.</summary>
    internal static int? ParsePercent(byte[] r)
    {
        if (r.Length < ReportLength) return null;
        if (r[0] != 0xBB || r[1] != 0xB0 || r[3] != 0x40) return null;
        if (r[6] > 100) return null;
        return r[6];
    }
}
