namespace BatteryTray.Core;

/// <summary>Log simples em arquivo, best-effort. Nunca lanca.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 512 * 1024;

    /// <summary>O --selftest liga para nao sujar o joule.log de verdade com aparelhos de mentira.</summary>
    internal static bool Silent { get; set; }

    public static void Write(string message)
    {
        if (Silent) return;
        try
        {
            AppPaths.EnsureDir();
            lock (Gate)
            {
                var file = new FileInfo(AppPaths.LogFile);
                if (file.Exists && file.Length > MaxBytes)
                {
                    // Guarda o anterior: o erro que interessa costuma estar logo antes do corte.
                    try { File.Move(AppPaths.LogFile, AppPaths.LogFile + ".old", overwrite: true); } catch { }
                }
                File.AppendAllText(AppPaths.LogFile,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch { /* log nunca derruba o app */ }
    }

    /// <summary>Com a pilha: so a mensagem nao diz de onde veio (o OutOfMemory de 2026-10-22 ficou sem origem).</summary>
    public static void Error(string context, Exception ex) =>
        Write($"ERRO {context}: {ex}");
}
