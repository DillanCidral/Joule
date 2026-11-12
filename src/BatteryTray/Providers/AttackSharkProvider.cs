using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Mouses Attack Shark (e o Delux M600 Pro) pelo receptor 1D57:FA60.
/// SOMENTE LEITURA: nao ha comando algum a enviar, o mouse transmite sozinho na
/// colecao 0x000A do dongle.
///
/// Formato do relatorio (5 bytes):  03 &lt;modelo&gt; 40 &lt;flag&gt; &lt;n&gt;
/// O byte 1 e' o modelo pareado: o receptor e os PIDs sao os mesmos em toda a
/// linha, e so ele diz qual mouse e'. A escala de n muda por modelo (R1 e X6
/// em dezenas, X11 e Delux em %). Tabela de modelos e escalas: OpenMouse
/// (AGPL-3.0, src/drivers/attackshark/hid.ts), que cita os drivers abertos do
/// R1 e do X11; o R1 foi conferido aqui (03 10 40 .. n, n x 10 = %).
///
/// A assinatura completa e' verificada porque VID 0x1D57 e' um chip Beken
/// generico usado por varias marcas: sem isso o app poderia inventar um
/// percentual para um dispositivo que so parece o mesmo.
///
/// Sobre a estrategia de leitura: medido nesta maquina, o mouse parado NAO
/// transmite (75s de escuta, zero relatorios) -- a transmissao a cada ~2s so
/// acontece com o mouse acordado. Por isso o provider mantem o mesmo listener
/// passivo de risco zero usado no Rapoo (um handle, aberto uma vez, um unico
/// CancelIoEx ao sair) e, no Refresh, espera ate 3s por um relatorio novo nesse
/// handle que ja esta aberto. Assim o valor aparece assim que o mouse acorda
/// -- inclusive no movimento que leva o cursor ate o icone da bandeja -- e
/// nunca se abre/fecha handle repetidamente.
/// Com PassiveListenersEnabled = false volta ao modelo estrito de abrir,
/// esperar 3s e fechar.
/// </summary>
public sealed class AttackSharkProvider : IBatteryProvider, IPassiveListenerProvider, IDisposable
{
    private const ushort Vid = 0x1D57;
    private const ushort PidDongle = 0xFA60;
    private const ushort PidCable = 0xFA61;
    private const ushort PidCableX11 = 0xFA55;

    /// <summary>Modelo pelo byte 1; Tenths = n x 10 (R1, X6), senao n ja e' %.</summary>
    private static readonly Dictionary<byte, (string Name, bool Tenths)> Models = new()
    {
        [0x10] = ("Attack Shark R1", true),
        [0x85] = ("Attack Shark X6", true),
        [0x55] = ("Attack Shark X11", false),
        [0x07] = ("Attack Shark X11 SE", false),
        [0x20] = ("Delux M600 Pro", false),
    };


    private const ushort BatteryUsagePage = 0x000A;
    private const int BatteryReportLength = 5;

    private static readonly TimeSpan WaitForReport = TimeSpan.FromSeconds(3);

    private readonly PassiveReportListener _listener = new(
        "AttackShark",
        c => c.Vid == Vid && c.Pid == PidDongle && c.UsagePage == BatteryUsagePage && c.InputLen == BatteryReportLength,
        ParsePercent,
        confirmReports: 2);

    public string Name => "AttackShark";

    public IReadOnlyList<(ushort Vid, ushort Pid)> SupportedIds { get; } = new[]
    {
        (Vid, PidDongle),
        (Vid, PidCable),
        (Vid, PidCableX11),
    };

    // Dongle e cabo sao o mesmo mouse: uma unica linha na UI.
    public string LogicalKey(ushort vid, ushort pid) => "attackshark:r1";

    public string DisplayName(ReadContext ctx) =>
        _listener.LastReport is { Length: > 1 } r && Models.TryGetValue(r[1], out var m) ? m.Name : "Mouse Attack Shark";

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
        // Mouse no cabo: o dongle 2.4GHz para de transmitir neste estado.
        if (ctx.IsPresent(Vid, PidCable) || ctx.IsPresent(Vid, PidCableX11))
            return BatteryReading.Charging(L.T("carregando; no cabo o dongle 2.4 GHz não transmite", "charging; when wired the 2.4 GHz dongle doesn’t transmit"));

        var collection = ctx.Find(Vid, PidDongle)
            .FirstOrDefault(c => c.UsagePage == BatteryUsagePage
                                 && c.InputLen == BatteryReportLength
                                 && !HidInventory.IsSystemReserved(c.UsagePage, c.Usage));

        if (collection is null)
            return BatteryReading.Disconnected(L.T("colecao 0x000A do dongle nao encontrada", "dongle collection 0x000A not found"));

        return ctx.Settings.PassiveListenersEnabled
            ? ReadViaListener(ctx)
            : ReadOneShot(collection);
    }

    private BatteryReading ReadViaListener(ReadContext ctx)
    {
        try
        {
            _listener.Ensure(ctx.AllCollections);

            if (!_listener.IsListening)
                return BatteryReading.NoData(L.T("nao consegui abrir a colecao 0x000A para escuta", "couldn’t open collection 0x000A to listen"));

            // Se o ultimo valor ja e' recente, nem espera.
            var current = _listener.Last;
            bool recent = current is not null && DateTime.UtcNow - current.Value.SeenUtc < TimeSpan.FromSeconds(10);
            if (!recent) _listener.WaitForFreshReport(WaitForReport);

            var last = _listener.Last;
            if (last is null)
                return BatteryReading.NoData(L.T("mouse parado não transmite; mexa nele e atualize", "an idle mouse doesn’t transmit; move it and refresh"));

            return BatteryReading.Percentage(last.Value.Percent, observedUtc: last.Value.SeenUtc);
        }
        catch (Exception ex)
        {
            Log.Error("AttackShark.ReadViaListener", ex);
            return BatteryReading.Error(ex.Message);
        }
    }

    /// <summary>Modelo estrito: abre, espera ate 3s, fecha (um unico CancelIoEx).</summary>
    private static BatteryReading ReadOneShot(HidCollection collection)
    {
        try
        {
            var report = HidOneShot.WaitForReport(collection, r => ParsePercent(r) is not null, WaitForReport);
            if (report is null)
                return BatteryReading.NoData(L.T("nenhum relatorio de bateria em 3s (mouse parado?)", "no battery report in 3 s (idle mouse?)"));

            return BatteryReading.Percentage(ParsePercent(report)!.Value);
        }
        catch (Exception ex)
        {
            Log.Error("AttackShark.ReadOneShot", ex);
            return BatteryReading.Error(ex.Message);
        }
    }

    /// <summary>
    /// Assinatura 03 &lt;modelo conhecido&gt; 40 .. n. O R1 vale com qualquer flag
    /// (conferido aqui); os outros so com flag 01, como no OpenMouse.
    /// </summary>
    internal static int? ParsePercent(byte[] r)
    {
        if (r.Length < BatteryReportLength) return null;
        if (r[0] != 0x03 || r[2] != 0x40) return null;
        if (!Models.TryGetValue(r[1], out var model)) return null;
        if (r[1] != 0x10 && r[3] != 0x01) return null;
        if (model.Tenths) return r[4] <= 10 ? r[4] * 10 : null;
        return r[4] <= 100 ? r[4] : null;
    }
}
