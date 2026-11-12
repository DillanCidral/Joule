using System.Runtime.InteropServices;
using BatteryTray.Interop;

namespace BatteryTray.Diag;

/// <summary>
/// Sonda de relatorio de FEATURE. Existe SO no harness de diagnostico.
///
/// Por que nao fica em Interop/ junto com o resto: `HidD_GetFeature` e' a unica
/// operacao do projeto que poe um pacote no barramento por iniciativa nossa (um
/// GET_REPORT no canal de controle). Nao e' escrita -- nao altera nada no
/// dispositivo -- mas tambem nao e' a escuta passiva que o app promete. Deixar
/// a declaracao aqui garante que nenhum exe entregue (Joule, JouleScan)
/// carregue esse import nem por acidente.
///
/// Disciplina desta sonda:
///   - UMA chamada por execucao, sem laco e sem repeticao;
///   - nunca em colecao reservada pelo sistema (mouse/teclado);
///   - o report id vem do descritor, lido antes, sem tocar no aparelho -- nao
///     se varre id nenhum na tentativa e erro.
///
/// Caso que motivou: teclado Dell Wireless `04CA:00BD`, cuja unica coleção
/// vendor (`0xFF00`/`0x0001`) tem `in=0 out=0 feat=42`, e cujo descritor
/// declara um unico relatorio de feature, id 3, com 41 bytes opacos.
/// </summary>
internal static class FeatureProbe
{
    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(IntPtr handle, byte[] buffer, int bufferLength);

    public static int Run(string hardwareId, int reportId)
    {
        Console.WriteLine("=== sonda de feature report ===");
        Console.WriteLine($"  alvo: {hardwareId}  report id {reportId}");
        Console.WriteLine("  UMA chamada HidD_GetFeature, sem laco. Nada e' escrito no aparelho.");
        Console.WriteLine();

        var parts = hardwareId.Split(':');
        if (parts.Length != 2
            || !ushort.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out ushort vid)
            || !ushort.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out ushort pid))
        {
            Console.WriteLine("  uso: --feature <VID:PID em hex> <report id>");
            return 1;
        }

        var targets = HidInventory.Scan()
            .Where(c => c.Vid == vid && c.Pid == pid && c.FeatureLen > 0)
            .Where(c => !HidInventory.IsSystemReserved(c.UsagePage, c.Usage))
            .ToList();

        if (targets.Count == 0)
        {
            Console.WriteLine("  nenhuma colecao com relatorio de feature para este VID:PID");
            Console.WriteLine("  (o aparelho esta plugado? a colecao pode ser reservada pelo sistema)");
            return 1;
        }

        foreach (var collection in targets)
        {
            Console.WriteLine($"  colecao page=0x{collection.UsagePage:X4} usage=0x{collection.Usage:X4} feat={collection.FeatureLen}");
            Probe(collection, reportId);
            Console.WriteLine();
        }

        return 0;
    }

    private static void Probe(HidCollection collection, int reportId)
    {
        IntPtr handle = HidNative.CreateFile(
            collection.Path,
            HidNative.GENERIC_READ,
            HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
            IntPtr.Zero,
            HidNative.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle == HidNative.INVALID_HANDLE_VALUE)
        {
            Console.WriteLine($"    nao abriu para leitura (err {Marshal.GetLastWin32Error()})");
            return;
        }

        try
        {
            var buffer = new byte[collection.FeatureLen];
            buffer[0] = (byte)reportId;

            if (!HidD_GetFeature(handle, buffer, buffer.Length))
            {
                Console.WriteLine($"    HidD_GetFeature recusou (err {Marshal.GetLastWin32Error()})");
                return;
            }

            Console.WriteLine($"    resposta ({buffer.Length} bytes):");
            Console.WriteLine("    " + BitConverter.ToString(buffer).Replace('-', ' '));

            var plausible = new List<string>();
            for (int i = 1; i < buffer.Length; i++)
            {
                if (buffer[i] is >= 1 and <= 100) plausible.Add($"[{i}]={buffer[i]}");
            }

            Console.WriteLine(plausible.Count > 0
                ? "    bytes em 1..100 (candidatos a percentual): " + string.Join("  ", plausible)
                : "    nenhum byte em 1..100");
            Console.WriteLine("    um retrato so nao identifica bateria: e' preciso comparar com");
            Console.WriteLine("    outra carga mais tarde para ver qual byte acompanha.");
        }
        finally
        {
            HidNative.CloseHandle(handle);
        }
    }
}
