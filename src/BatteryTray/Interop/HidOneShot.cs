namespace BatteryTray.Interop;

/// <summary>
/// Leitura pontual: abre a colecao UMA vez, espera ate <c>timeout</c> pelo
/// primeiro relatorio que passe no validador, e fecha (um unico CancelIoEx,
/// feito pelo Dispose do reader). Nao ha reabertura nem laco de cancelamento.
/// So faz sentido em colecoes que comprovadamente transmitem sozinhas.
/// </summary>
public static class HidOneShot
{
    public static byte[]? WaitForReport(HidCollection collection, Func<byte[], bool> isValid, TimeSpan timeout)
    {
        byte[]? captured = null;
        using var signal = new ManualResetEventSlim(false);

        using var reader = HidPassiveReader.Open(collection, report =>
        {
            if (Volatile.Read(ref captured) is not null) return;
            if (!isValid(report)) return;
            Volatile.Write(ref captured, report);
            try { signal.Set(); } catch (ObjectDisposedException) { }
        });

        if (reader is null) return null;

        signal.Wait(timeout);
        return Volatile.Read(ref captured);
    }
}
