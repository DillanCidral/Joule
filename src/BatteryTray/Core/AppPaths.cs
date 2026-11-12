namespace BatteryTray.Core;

public static class AppPaths
{
    public static string DataDir { get; } = ResolveDataDir();

    /// <summary>
    /// Resolve a pasta de dados garantindo um caminho ABSOLUTO.
    ///
    /// GetFolderPath(LocalApplicationData) pode devolver string vazia em alguns
    /// contextos de lancamento; nesse caso Path.Combine devolveria "Joule",
    /// relativo, e o app gravaria log e cache dentro do diretorio de trabalho de
    /// quem o abriu -- funcionando, mas em silencio, num lugar inesperado.
    /// Nunca foi observado nesta maquina; e' blindagem, e o caminho resolvido
    /// vai para o log na linha "App iniciado" para nunca ficar ambiguo.
    /// </summary>
    private static string ResolveDataDir()
    {
        string? local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(local))
            local = Environment.GetEnvironmentVariable("LOCALAPPDATA");

        if (string.IsNullOrWhiteSpace(local))
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(profile))
                local = Path.Combine(profile, "AppData", "Local");
        }

        if (string.IsNullOrWhiteSpace(local))
            local = Path.GetTempPath();

        string dir = Path.GetFullPath(Path.Combine(local, Ui.AppInfo.Id));

        // Antes do nome Joule a pasta era "BatteryTray": na primeira abertura
        // ela e' movida inteira (configuracao, cache, log antigo). Se o move
        // falhar, segue na pasta antiga para nao perder configuracao.
        string legacy = Path.GetFullPath(Path.Combine(local, "BatteryTray"));
        if (!Ui.AppInfo.IsTest && !Directory.Exists(dir) && Directory.Exists(legacy))
        {
            try { Directory.Move(legacy, dir); }
            catch { return legacy; }
        }
        return dir;
    }

    public static string StateFile => Path.Combine(DataDir, "state.json");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string LogFile => Path.Combine(DataDir, "joule.log");

    public static void EnsureDir()
    {
        try { Directory.CreateDirectory(DataDir); } catch { }
    }

    /// <summary>
    /// Grava num .tmp e troca de uma vez: queda de energia no meio deixa o
    /// arquivo velho inteiro, nunca um JSON pela metade (que o Load trocaria
    /// pelo padrao, perdendo a configuracao em silencio).
    /// </summary>
    public static void WriteAtomic(string path, string text)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, overwrite: true);
    }
}
