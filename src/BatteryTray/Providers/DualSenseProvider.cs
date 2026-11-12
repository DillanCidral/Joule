using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Controle DualSense (PS5), SOMENTE NO CABO. SOMENTE LEITURA.
///
/// Formato: relatorio de entrada 0x01 com 64 bytes, do qual o byte 53 e' o
/// status da bateria:
///     nibble baixo = nivel bruto 0..10   =&gt;  bateria = min(n * 10 + 5, 100) %
///     nibble alto  = estado: 0 descarregando, 1 carregando, 2 carga completa
///
/// Por que so no cabo: por Bluetooth o controle nasce no modo basico e manda
/// um 0x01 curto, sem bateria -- medido, 15579 relatorios seguidos com os
/// bytes 10..77 zerados. O relatorio 0x31, que carrega a bateria, so comeca a
/// sair depois de o host ESCREVER o feature report 0x05, e escrever em
/// dispositivo e' invariante do projeto. No cabo o controle ja manda os 64
/// bytes completos sozinho, a ~250 relatorios por segundo, sem ninguem pedir
/// nada. Por isso aqui nao ha listener passivo: o valor chega em milissegundos,
/// entao a leitura pontual (abre, le um relatorio, fecha) e' a mais barata e a
/// mais estrita das duas -- manter um handle aberto num fluxo de 250 Hz seria
/// pagar caro por um dado que o Refresh consegue na hora.
///
/// O comprimento do relatorio E' a checagem que separa cabo de Bluetooth: o
/// mesmo VID:PID aparece com 78 bytes quando vem pelo radio. Sem isso o
/// provider leria o byte 53 zerado de um relatorio de Bluetooth e anunciaria
/// 5% para um controle cheio.
///
/// Nota de calibracao: a leitura foi conferida contra um unico controle, cheio
/// e no cabo (byte 53 = 0x29 = estado 2, nivel 9). O mapa de nibbles e' o
/// documentado do DualSense; os estados 0 e 1 ainda nao foram vistos nesta
/// casa (uma segunda captura deu 0x2A, estado 2 nivel 10, tambem 100%).
/// Estados fora de 0..2 (o firmware usa 0xA/0xB/0xF para tensao e
/// temperatura anormais) sao descartados em vez de virar percentual.
/// </summary>
public sealed class DualSenseProvider : IBatteryProvider
{
    private const ushort Vid = 0x054C;
    private const ushort Pid = 0x0CE6;

    private const ushort GamepadUsagePage = 0x0001;
    private const ushort GamepadUsage = 0x0005;

    /// <summary>Relatorio 0x01 completo, que so sai pelo cabo.</summary>
    private const int UsbInputLen = 64;

    private const int StatusByte = 53;

    private static readonly TimeSpan WaitForReport = TimeSpan.FromSeconds(2);

    public string Name => "DualSense";

    public IReadOnlyList<(ushort Vid, ushort Pid)> SupportedIds { get; } = new[] { (Vid, Pid) };

    public string LogicalKey(ushort vid, ushort pid) => "dualsense:ps5";

    public string DisplayName(ReadContext ctx) => "DualSense (PS5)";

    public BatteryReading Read(ReadContext ctx)
    {
        if (!ctx.IsPresent(Vid, Pid))
            return BatteryReading.Disconnected(L.T("controle nao esta presente", "controller not present"));

        var usb = ctx.Find(Vid, Pid)
            .Where(c => c.UsagePage == GamepadUsagePage
                        && c.Usage == GamepadUsage
                        && c.InputLen == UsbInputLen
                        && !HidInventory.IsSystemReserved(c.UsagePage, c.Usage))
            .ToList();

        if (usb.Count == 0)
            return BatteryReading.NoData(L.T("por Bluetooth o controle não informa bateria; ligue no cabo", "over Bluetooth the controller doesn’t report battery; plug in the cable"));

        try
        {
            var report = HidOneShot.WaitForReport(usb[0], r => ParseStatus(r) is not null, WaitForReport);
            if (report is null)
                return BatteryReading.NoData(L.T("nenhum relatorio valido em 2s", "no valid report in 2 s"));

            var status = ParseStatus(report)!.Value;

            // Dois controles no mesmo VID:PID colapsam numa linha so; melhor
            // dizer qual foi lido do que deixar o numero pairando sem dono.
            string? extra = usb.Count > 1 ? L.T($"{usb.Count} controles no cabo; mostrando o primeiro", $"{usb.Count} controllers wired; showing the first") : null;
            string? charge = status.State switch
            {
                ChargeState.Charging => L.T("carregando", "charging"),
                ChargeState.Full => L.T("carga completa", "fully charged"),
                _ => null,
            };

            return BatteryReading.Percentage(status.Percent, extra, chargeText: charge);
        }
        catch (Exception ex)
        {
            Log.Error("DualSense.Read", ex);
            return BatteryReading.Error(ex.Message);
        }
    }

    internal enum ChargeState
    {
        Discharging = 0,
        Charging = 1,
        Full = 2,
    }

    internal readonly record struct DualSenseStatus(int Percent, ChargeState State);

    /// <summary>
    /// Relatorio 0x01 de 64 bytes (so o cabo entrega esse tamanho), byte 53
    /// dividido em nivel e estado. Devolve null se nao for esse relatorio ou
    /// se o estado nao for um dos tres conhecidos.
    /// </summary>
    internal static DualSenseStatus? ParseStatus(byte[] r)
    {
        if (r.Length != UsbInputLen) return null;
        if (r[0] != 0x01) return null;

        byte status = r[StatusByte];
        int level = status & 0x0F;
        int state = (status & 0xF0) >> 4;

        if (level > 10) return null;
        if (state > (int)ChargeState.Full) return null;

        int percent = state == (int)ChargeState.Full ? 100 : Math.Min(level * 10 + 5, 100);
        return new DualSenseStatus(percent, (ChargeState)state);
    }
}
