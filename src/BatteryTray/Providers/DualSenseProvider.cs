using BatteryTray.Core;
using BatteryTray.Interop;

namespace BatteryTray.Providers;

/// <summary>
/// Controle DualSense (PS5). SOMENTE LEITURA, sem pergunta: so le o relatorio
/// que o controle ja manda sozinho.
///
/// Cabo: relatorio de entrada 0x01 com 64 bytes, status no byte 53.
/// Bluetooth: relatorio 0x31 com 78 bytes, status no byte 54 (um byte de
/// sequencia a mais depois do id; formato do hid-playstation do Linux, so o
/// formato). Nos dois:
///     nibble baixo = nivel bruto 0..10   =&gt;  bateria = min(n * 10 + 5, 100) %
///     nibble alto  = estado: 0 descarregando, 1 carregando, 2 carga completa
///
/// Bluetooth: o controle nasce no modo basico e manda um 0x01 curto, sem
/// bateria -- medido, 15579 relatorios seguidos com os bytes 10..77 zerados.
/// O 0x31 so comeca a sair depois de algum programa ESCREVER o feature report
/// 0x05, e escrever em dispositivo e' invariante do projeto. Entao o Joule le
/// o 0x31 so quando outro programa (a Steam, por exemplo) ja ligou o modo
/// completo -- visto num envio de 2026-11-16, byte 54 = 0x09 -- e, sem ele,
/// segue "sem dado".
///
/// A leitura e' pontual (abre, le um relatorio, fecha): o controle manda a
/// ~250 relatorios por segundo, o valor chega em milissegundos, e manter um
/// handle aberto num fluxo desses seria pagar caro por um dado que o Refresh
/// consegue na hora.
///
/// O comprimento junto com o id separa cabo de Bluetooth: sem isso o provider
/// leria o byte 53 zerado de um relatorio de Bluetooth e anunciaria 5% para
/// um controle cheio.
///
/// Nota de calibracao: o cabo foi conferido contra um unico controle, cheio
/// (byte 53 = 0x29 = estado 2, nivel 9; uma segunda captura deu 0x2A). O
/// Bluetooth nao foi conferido aqui. Estados fora de 0..2 (o firmware usa
/// 0xA/0xB/0xF para tensao e temperatura anormais) sao descartados em vez de
/// virar percentual.
/// </summary>
public sealed class DualSenseProvider : IBatteryProvider
{
    private const ushort Vid = 0x054C;
    private const ushort Pid = 0x0CE6;

    internal const ushort GamepadUsagePage = 0x0001;
    internal const ushort GamepadUsage = 0x0005;

    private const int UsbLen = 64;
    private const int BtLen = 78;

    private static readonly TimeSpan WaitForReport = TimeSpan.FromSeconds(2);

    public string Name => "DualSense";

    public IReadOnlyList<(ushort Vid, ushort Pid)> SupportedIds { get; } = new[] { (Vid, Pid) };

    public string LogicalKey(ushort vid, ushort pid) => "dualsense:ps5";

    public string DisplayName(ReadContext ctx) => "DualSense (PS5)";

    public BatteryReading Read(ReadContext ctx) =>
        ReadPad(ctx, Vid, new[] { Pid }, ParseStatus, "DualSense.Read");

    /// <summary>
    /// Leitura comum aos controles PlayStation: prefere a colecao do cabo
    /// (64 bytes) e cai na do Bluetooth, le um relatorio valido em ate 2 s.
    /// </summary>
    internal static BatteryReading ReadPad(ReadContext ctx, ushort vid, ushort[] pids,
        Func<byte[], PadStatus?> parse, string logName)
    {
        var pads = pids.SelectMany(pid => ctx.Find(vid, pid))
            .Where(c => c.UsagePage == GamepadUsagePage
                        && c.Usage == GamepadUsage
                        && !HidInventory.IsSystemReserved(c.UsagePage, c.Usage))
            .OrderBy(c => c.InputLen == UsbLen ? 0 : 1)
            .ToList();

        if (pads.Count == 0)
            return BatteryReading.Disconnected(L.T("controle nao esta presente", "controller not present"));

        try
        {
            var report = HidOneShot.WaitForReport(pads[0], r => parse(r) is not null, WaitForReport);
            if (report is null)
                return pads[0].InputLen == UsbLen
                    ? BatteryReading.NoData(L.T("nenhum relatorio valido em 2s", "no valid report in 2 s"))
                    : BatteryReading.NoData(L.T(
                        "por Bluetooth o controle só informa a bateria quando outro programa (como a Steam) está aberto; ligue no cabo",
                        "over Bluetooth the controller only reports battery while another app (like Steam) is open; plug in the cable"));

            var status = parse(report)!.Value;

            // Dois controles no mesmo VID:PID colapsam numa linha so; melhor
            // dizer qual foi lido do que deixar o numero pairando sem dono.
            string? extra = pads.Count > 1 ? L.T($"{pads.Count} controles; mostrando o primeiro", $"{pads.Count} controllers; showing the first") : null;
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
            Log.Error(logName, ex);
            return BatteryReading.Error(ex.Message);
        }
    }

    internal enum ChargeState
    {
        Discharging = 0,
        Charging = 1,
        Full = 2,
    }

    internal readonly record struct PadStatus(int Percent, ChargeState State);

    /// <summary>
    /// 0x01 de 64 bytes (cabo, byte 53) ou 0x31 de 78 bytes (Bluetooth no modo
    /// completo, byte 54). Devolve null para qualquer outro relatorio ou para
    /// nivel/estado fora do conhecido.
    /// </summary>
    internal static PadStatus? ParseStatus(byte[] r)
    {
        int at;
        if (r.Length == UsbLen && r[0] == 0x01) at = 53;
        else if (r.Length == BtLen && r[0] == 0x31) at = 54;
        else return null;

        byte status = r[at];
        int level = status & 0x0F;
        int state = (status & 0xF0) >> 4;

        if (level > 10) return null;
        if (state > (int)ChargeState.Full) return null;

        int percent = state == (int)ChargeState.Full ? 100 : Math.Min(level * 10 + 5, 100);
        return new PadStatus(percent, (ChargeState)state);
    }
}

/// <summary>
/// Controle DualShock 4 (PS4), versoes 1 e 2. SOMENTE LEITURA, sem pergunta.
///
/// Cabo: relatorio 0x01 de 64 bytes, status no byte 30. Bluetooth: relatorio
/// 0x11, status no byte 32 (o Windows entrega com o tamanho da colecao, 547).
/// Como no DualSense, o 0x11 so sai quando outro programa ja ligou o modo
/// completo. Formato do hid-playstation do Linux, so o formato:
///     nibble baixo = nivel 0..10; bit 0x10 = no cabo
///     no cabo: nivel &lt; 10 =&gt; n*10+5 % carregando; 10 =&gt; 100% completa; acima, descartado
///     fora do cabo: min(n*10+5, 100) %
/// Visto nos envios de 2026-11-17 (#142 no cabo: 0x18; #143 no Bluetooth: 0x08).
/// Nao testado aqui. O adaptador sem fio 054C:0BA0 fica de fora: nao ha envio dele.
/// </summary>
public sealed class DualShock4Provider : IBatteryProvider
{
    private const ushort Vid = 0x054C;
    private static readonly ushort[] Pids = { 0x05C4, 0x09CC };

    public string Name => "DualShock 4";

    public IReadOnlyList<(ushort Vid, ushort Pid)> SupportedIds { get; } = Pids.Select(p => (Vid, p)).ToArray();

    public string LogicalKey(ushort vid, ushort pid) => "dualshock4:ps4";

    public string DisplayName(ReadContext ctx) => "DualShock 4 (PS4)";

    public BatteryReading Read(ReadContext ctx) =>
        DualSenseProvider.ReadPad(ctx, Vid, Pids, ParseStatus, "DualShock4.Read");

    internal static DualSenseProvider.PadStatus? ParseStatus(byte[] r)
    {
        int at;
        if (r.Length == 64 && r[0] == 0x01) at = 30;
        else if (r.Length >= 78 && r[0] == 0x11) at = 32;
        else return null;

        byte status = r[at];
        int level = status & 0x0F;
        bool cable = (status & 0x10) != 0;

        if (level > 10) return null;
        if (!cable) return new(Math.Min(level * 10 + 5, 100), DualSenseProvider.ChargeState.Discharging);
        return level == 10
            ? new(100, DualSenseProvider.ChargeState.Full)
            : new(level * 10 + 5, DualSenseProvider.ChargeState.Charging);
    }
}
