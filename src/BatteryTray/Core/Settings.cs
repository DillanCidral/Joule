using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatteryTray.Core;

public sealed class Settings
{
    [JsonPropertyName("lowBatteryThreshold")]
    public int LowBatteryThreshold { get; set; } = 20;

    [JsonPropertyName("alertsEnabled")]
    public bool AlertsEnabled { get; set; } = true;

    /// <summary>
    /// Listener passivo permanente para dispositivos que so respondem quando o
    /// software do fabricante pergunta (ex.: Rapoo). Nao e' polling: e' um unico
    /// handle aberto, escutando.
    /// </summary>
    [JsonPropertyName("passiveListenersEnabled")]
    public bool PassiveListenersEnabled { get; set; } = true;

    /// <summary>"So escutar": a porta de escrita recusa toda pergunta, ate nas marcas permitidas (camada de seguranca, 2026-11-12).</summary>
    [JsonPropertyName("listenOnly")]
    public bool ListenOnly { get; set; }

    /// <summary>Marcas (VID em hex, ex. "046D") que a pessoa deixou o Joule perguntar a bateria. Marca nao testada fora daqui nao recebe pergunta.</summary>
    [JsonPropertyName("askAllowedVids")]
    public List<string> AskAllowedVids { get; set; } = new();

    [JsonPropertyName("showUnsupported")]
    public bool ShowUnsupported { get; set; } = true;

    /// <summary>"glass" (vidro tingido), "system" (Mica) ou "solid".</summary>
    [JsonPropertyName("appearance")]
    public string Appearance { get; set; } = "glass";

    /// <summary>Opacidade da pelicula do vidro tingido, em %. 50 = TintedGlass do Windhawk.</summary>
    [JsonPropertyName("glassOpacity")]
    public int GlassOpacity { get; set; } = 30; // desfoque ligado por padrao: o borrado ja separa o texto do fundo

    [JsonPropertyName("widgetEnabled")]
    public bool WidgetEnabled { get; set; } = true;

    /// <summary>"right" ou "left".</summary>
    [JsonPropertyName("widgetSide")]
    public string WidgetSide { get; set; } = "right";

    [JsonPropertyName("widgetAutoHide")]
    public bool WidgetAutoHide { get; set; } = true;

    /// <summary>Monitor do widget (nome do dispositivo, ex. \\.\DISPLAY2). Vazio = o principal.</summary>
    [JsonPropertyName("widgetMonitor")]
    public string WidgetMonitor { get; set; } = "";

    /// <summary>Altura do widget como fracao da area de trabalho (0..1), a partir do topo.</summary>
    [JsonPropertyName("widgetOffset")]
    public double WidgetOffset { get; set; } = 0.44;

    /// <summary>Cor da alca do widget: "joule" (a do app), "windows", "white", "neutral" ou "#RRGGBB".</summary>
    [JsonPropertyName("widgetHandleColor")]
    public string WidgetHandleColor { get; set; } = "joule";

    /// <summary>Aviso de conectar/desconectar (e de bateria baixa): "widget", "windows" ou "off".</summary>
    [JsonPropertyName("notifyPlace")]
    public string NotifyPlace { get; set; } = "widget";

    /// <summary>Os cinco toques dos avisos (conectou, achei, nao achei, bateria baixa, desconectou).</summary>
    [JsonPropertyName("notifySound")]
    public bool NotifySound { get; set; } = true;

    /// <summary>Vidro tingido com desfoque do fundo (so com "Efeitos de transparencia" ligado).</summary>
    [JsonPropertyName("glassBlur")]
    public bool GlassBlur { get; set; } = true;

    /// <summary>Idioma da interface: "auto" (o do Windows), "pt" ou "en". Vale ao abrir o app (Core.L).</summary>
    [JsonPropertyName("language")]
    public string Language { get; set; } = "auto";

    /// <summary>Versao da ultima abertura: muda depois de uma atualizacao e abre a tela de apoio (Core.Changes).</summary>
    [JsonPropertyName("lastVersion")]
    public string LastVersion { get; set; } = "";

    /// <summary>Contador da tela de apoio: aparelhos que ja apareceram com bateria (chaves). So neste PC, nunca enviado.</summary>
    [JsonPropertyName("devicesSeen")]
    public List<string> DevicesSeen { get; set; } = new();

    /// <summary>Contador da tela de apoio: avisos de bateria baixa mostrados. So neste PC, nunca enviado.</summary>
    [JsonPropertyName("lowAlerts")]
    public int LowAlerts { get; set; }

    /// <summary>A pessoa ja viu as boas-vindas da beta (fechou por Comecar, Pular ou Esc).</summary>
    [JsonPropertyName("onboardingDone")]
    public bool OnboardingDone { get; set; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.SettingsFile));
                if (s is not null)
                {
                    s.LowBatteryThreshold = Math.Clamp(s.LowBatteryThreshold, 1, 99);
                    s.GlassOpacity = Math.Clamp(s.GlassOpacity, 20, 95);
                    s.WidgetOffset = Math.Clamp(double.IsFinite(s.WidgetOffset) ? s.WidgetOffset : 0.44, 0, 1);
                    s.WidgetHandleColor = HandleColor(s.WidgetHandleColor);
                    return s;
                }
            }
        }
        catch (Exception ex) { Log.Error("Settings.Load", ex); }
        return new Settings();
    }

    /// <summary>O settings.json pode ser editado a mao: valor estranho volta para a cor do app.</summary>
    public static string HandleColor(string? value) =>
        value is "joule" or "windows" or "white" or "neutral"
        || (value is { Length: 7 } && value[0] == '#' && value[1..].All(Uri.IsHexDigit))
            ? value : "joule";

    public void Save()
    {
        try
        {
            AppPaths.EnsureDir();
            AppPaths.WriteAtomic(AppPaths.SettingsFile, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) { Log.Error("Settings.Save", ex); }
    }
}
