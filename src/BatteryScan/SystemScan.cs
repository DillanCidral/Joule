using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace BatteryTray.Scan;

/// <summary>
/// Contexto da maquina. O campo que mais importa e' o ultimo: software de
/// fabricante rodando.
///
/// Licao do Rapoo MT760: ele so responde quando o RapooOfficeDev.exe pergunta.
/// Se numa maquina alheia aparece um relatorio de bateria "do nada", saber que
/// existe um software do fabricante rodando explica de onde ele veio -- e
/// evita eu escrever um provider que so funciona na maquina de quem testou.
/// </summary>
public static class SystemScan
{
    private static readonly string[] VendorKeywords =
    {
        "rapoo", "logi", "ghub", "lghub", "icue", "corsair", "razer", "synapse",
        "steelseries", "hyperx", "ngenuity", "armoury", "asus", "msi", "glorious",
        "redragon", "attack", "shark", "vxe", "mchose", "8bitdo", "xbox", "wootility",
        "keychron", "pulsar", "endgame", "zowie", "cooler", "dareu", "machenike",
        "bloody", "fantech", "hator", "trust", "hp accessory", "dell peripheral",
    };

    public static SystemInfo Collect(List<string> warnings)
    {
        var info = new SystemInfo
        {
            MachineName = SafeGet(() => Environment.MachineName),
            UserName = SafeGet(() => Environment.UserName),
            OsVersion = SafeGet(() => Environment.OSVersion.VersionString),
            OsCaption = ReadOsCaption(),
            Architecture = SafeGet(() => RuntimeInformation.OSArchitecture.ToString()),
            DotNet = SafeGet(() => RuntimeInformation.FrameworkDescription),
            Culture = SafeGet(() => System.Globalization.CultureInfo.CurrentCulture.Name),
            Is64Bit = Environment.Is64BitOperatingSystem,
        };

        try
        {
            // 255 = NoSystemBattery: desktop. Qualquer outro valor = tem bateria
            // interna, ou seja, notebook.
            info.HasSystemBattery = System.Windows.Forms.SystemInformation.PowerStatus.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery;
        }
        catch (Exception ex)
        {
            warnings.Add($"PowerStatus: {ex.Message}");
        }

        info.VendorSoftwareRunning = FindVendorSoftware(warnings);
        return info;
    }

    private static List<string> FindVendorSoftware(List<string> warnings)
    {
        var found = new List<string>();
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                string name;
                try { name = process.ProcessName; }
                catch { continue; }
                finally { process.Dispose(); }

                if (name.Length == 0) continue;
                if (VendorKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)) &&
                    !found.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    found.Add(name);
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"lista de processos: {ex.Message}");
        }

        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    private static string ReadOsCaption()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key is null) return "";

            string product = key.GetValue("ProductName") as string ?? "";
            string display = key.GetValue("DisplayVersion") as string ?? "";
            string build = key.GetValue("CurrentBuild") as string ?? "";
            string ubr = key.GetValue("UBR")?.ToString() ?? "";

            // O ProductName no registro continua dizendo "Windows 10" no
            // Windows 11 -- a Microsoft nunca atualizou esse valor. Build
            // 22000 e' o corte.
            if (int.TryParse(build, out int buildNumber) && buildNumber >= 22000)
                product = product.Replace("Windows 10", "Windows 11");

            string full = product;
            if (display.Length > 0) full += $" {display}";
            if (build.Length > 0) full += $" (build {build}{(ubr.Length > 0 ? "." + ubr : "")})";
            return full.Trim();
        }
        catch
        {
            return "";
        }
    }

    private static string SafeGet(Func<string> get)
    {
        try { return get() ?? ""; }
        catch { return ""; }
    }
}
