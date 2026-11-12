using Microsoft.Win32;

namespace BatteryTray.Core;

/// <summary>
/// Autostart via HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// Nao exige administrador e nao toca em HKLM.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = Ui.AppInfo.Id;
    /// <summary>Nome da entrada antes do Joule; migrada em RefreshPathIfEnabled.</summary>
    private const string LegacyValueName = "BatteryTray";

    public static string ExecutablePath => Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath;

    private static string CommandLine => $"\"{ExecutablePath}\" --startup";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string s && s.Length > 0;
        }
        catch (Exception ex) { Log.Error("Autostart.IsEnabled", ex); return false; }
    }

    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return false;
            if (enabled)
                key.SetValue(ValueName, CommandLine, RegistryValueKind.String);
            else if (key.GetValue(ValueName) is not null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) { Log.Error("Autostart.Set", ex); return false; }
    }

    /// <summary>Se o exe foi movido, atualiza o caminho registrado.</summary>
    public static void RefreshPathIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);

            // Antes do Run, o autostart era um atalho na pasta Inicializar, que
            // abria o BatteryTray.exe velho junto com o Joule a cada login.
            // O canal de teste nunca herda o autostart antigo: ele e' do main.
            string legacyLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), LegacyValueName + ".lnk");
            if (!Ui.AppInfo.IsTest && File.Exists(legacyLink))
            {
                File.Delete(legacyLink);
                key?.SetValue(ValueName, CommandLine, RegistryValueKind.String);
                Log.Write($"Autostart: atalho antigo removido ({legacyLink}).");
            }

            if (!Ui.AppInfo.IsTest && key?.GetValue(LegacyValueName) is string legacy && legacy.Length > 0)
            {
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
                key.SetValue(ValueName, CommandLine, RegistryValueKind.String);
            }
            if (key?.GetValue(ValueName) is string current &&
                !string.Equals(current, CommandLine, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, CommandLine, RegistryValueKind.String);
            }
        }
        catch (Exception ex) { Log.Error("Autostart.Refresh", ex); }
    }
}
