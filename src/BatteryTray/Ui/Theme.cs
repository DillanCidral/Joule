using BatteryTray.Core;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace BatteryTray.Ui;

public enum Appearance { Glass, System, Solid }

/// <summary>
/// Tokens do design v1 (secao 06 da entrega de design) e o estado do Windows que
/// decide qual coluna vale: tema claro/escuro, transparencia ligada, cor de
/// destaque. Tudo vira SolidColorBrush em Application.Resources, consumido
/// por DynamicResource -- trocar o tema do Windows repinta sem reabrir nada.
/// </summary>
public static class Theme
{
    // Colunas: vidro escuro, vidro claro, sistema escuro, sistema claro, solido escuro, solido claro.
    private static readonly Dictionary<string, string[]> Tokens = new()
    {
        ["Window.Tint"] = new[] { "#80000000", "#73FFFFFF", "#F0202020", "#F2F3F3F3", "#FF1C1C1C", "#FFF3F3F3" },
        ["Window.Stroke"] = new[] { "#00000000", "#00000000", "#19FFFFFF", "#0F000000", "#FF2E2E2E", "#FFE0E0E0" },
        ["Card.Fill"] = new[] { "#0FFFFFFF", "#73FFFFFF", "#0DFFFFFF", "#B3FFFFFF", "#FF262626", "#FFFFFFFF" },
        ["Card.Stroke"] = new[] { "#00000000", "#00000000", "#1A000000", "#0F000000", "#00000000", "#FFE8E8E8" },
        ["Card.FillHover"] = new[] { "#1CFFFFFF", "#A6FFFFFF", "#15FFFFFF", "#80F9F9F9", "#FF2D2D2D", "#FFFAFAFA" },
        ["Tile.Fill"] = new[] { "#14FFFFFF", "#0F000000", "#0FFFFFFF", "#08000000", "#FF2F2F2F", "#FFF0F0F0" },
        ["Text.Primary"] = new[] { "#FFFFFFFF", "#E6000000", "#FFFFFFFF", "#E4000000", "#FFFFFFFF", "#FF1A1A1A" },
        ["Text.Secondary"] = new[] { "#C7FFFFFF", "#B3000000", "#C5FFFFFF", "#9E000000", "#FFC8C8C8", "#FF4D4D4D" },
        ["Text.Tertiary"] = new[] { "#9EFFFFFF", "#94000000", "#8BFFFFFF", "#8A000000", "#FF9D9D9D", "#FF6B6B6B" },
        ["Divider"] = new[] { "#14FFFFFF", "#12000000", "#15FFFFFF", "#0F000000", "#FF2E2E2E", "#FFE8E8E8" },
        ["Level.Track"] = new[] { "#26FFFFFF", "#1F000000", "#2BFFFFFF", "#24000000", "#FF3A3A3A", "#FFDCDCDC" },
        ["Level.Stale"] = new[] { "#66FFFFFF", "#57000000", "#5DFFFFFF", "#52000000", "#FF6B6B6B", "#FFA3A3A3" },
        ["Button.Fill"] = new[] { "#0FFFFFFF", "#80FFFFFF", "#0FFFFFFF", "#B3FFFFFF", "#FF2A2A2A", "#FFFFFFFF" },
        ["Segment.Selected"] = new[] { "#26FFFFFF", "#FFFFFFFF", "#1FFFFFFF", "#FFFFFFFF", "#FF3A3A3A", "#FFFFFFFF" },
        ["Slider.Thumb"] = new[] { "#FF454545", "#FFFFFFFF", "#FF454545", "#FFFFFFFF", "#FF454545", "#FFFFFFFF" },
    };

    /// <summary>Modo efetivamente em uso (o pedido pode cair para Solido).</summary>
    public static Appearance Current { get; private set; }
    public static bool IsDark { get; private set; } = true;
    public static bool TransparencyEffects { get; private set; } = true;

    /// <summary>Vidro com desfoque do fundo valendo agora: pedido E "Efeitos de transparencia" ligado.</summary>
    public static bool Blur { get; private set; }

    /// <summary>Camada borrada inteira ou nenhuma: misturar nitido e borrado deixa o texto de tras legivel (teste de 2026-11-10).</summary>
    public static double BlurOpacity => Blur ? 1 : 0;

    /// <summary>Janela por pixel: o vidro, com ou sem desfoque (o desfoque e' uma janela por baixo: BlurUnderlay).</summary>
    public static bool Layered => Current == Appearance.Glass;

    public static event Action? Changed;

    public static Appearance Parse(string? value) => value?.ToLowerInvariant() switch
    {
        "system" => Appearance.System,
        "solid" => Appearance.Solid,
        _ => Appearance.Glass,
    };

    public static string ToSetting(Appearance a) => a switch
    {
        Appearance.System => "system",
        Appearance.Solid => "solid",
        _ => "glass",
    };

    /// <param name="glassOpacity">Opacidade da pelicula do vidro, em % (so no vidro tingido).</param>
    /// <param name="blur">Desfocar o fundo no vidro tingido (so vale com a transparencia do Windows ligada).</param>
    public static void Apply(Appearance requested, int glassOpacity = 50, bool blur = false)
    {
        IsDark = ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) != 1;

        // So o Sistema (Mica) depende de "Efeitos de transparencia": desligado,
        // o Windows nao pinta Mica e o app usa Solido. O vidro tingido e' pelicula
        // desenhada pelo app, sem blur (ver Native.cs), e funciona com a opcao
        // desligada -- e' assim que o Windhawk do dono roda (transparencia off).
        TransparencyEffects = ReadDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1) == 1;
        Current = requested == Appearance.System && !TransparencyEffects ? Appearance.Solid : requested;
        Blur = Current == Appearance.Glass && blur && TransparencyEffects;

        int column = (int)Current * 2 + (IsDark ? 0 : 1);
        var res = System.Windows.Application.Current.Resources;
        foreach (var (key, values) in Tokens)
            res[key] = Brush(values[column]);

        // Sem blur por baixo, a pelicula e' o que separa o painel do fundo:
        // a opacidade e' o ajuste fino que o design nao tinha como prever.
        if (Current == Appearance.Glass)
        {
            byte alpha = (byte)Math.Round(Math.Clamp(glassOpacity, 0, 100) * 2.55);
            var tint = Color.FromArgb(alpha, (byte)(IsDark ? 0 : 255), (byte)(IsDark ? 0 : 255), (byte)(IsDark ? 0 : 255));
            var b = new SolidColorBrush(tint);
            b.Freeze();
            res["Window.Tint"] = b;
        }

        // Pele Joule (identidade-visual/finais/joule/2026-11-10_joule-tokens_v2.css):
        // amarelo da brasa (tokens v2, 2026-11-10) no vidro e no solido; o Sistema segue a cor de destaque do Windows.
        // Barra e anel eram neutros (amarelo em barra le como "atencao"); desde 2026-11-10 usam a Brasa, abaixo.
        if (AppInfo.IsTest)
        {
            // Canal de teste: violeta em toda aparencia, para nunca confundir com o main.
            res["Accent"] = Brush("#FFB57BFF");
            res["Accent.Foreground"] = Brush("#FF161616");
        }
        else if (Current == Appearance.System)
        {
            res["Accent"] = new SolidColorBrush(AccentColor(IsDark));
            res["Accent.Foreground"] = Brush(IsDark ? "#FF000000" : "#FFFFFFFF");
        }
        else
        {
            res["Accent"] = Brush("#FFFFC21F"); // amarelo do icone final (2026-11-10); era #FFD34D
            res["Accent.Foreground"] = Brush("#FF161616");
        }
        // Destaque como TEXTO: o amarelo some no tema claro (tokens v3: #826313, 5,1:1 no #F3F3F3; era #84660B ate 2026-11-12).
        res["Accent.Text"] = !AppInfo.IsTest && Current != Appearance.System && !IsDark ? Brush("#FF826313") : res["Accent"];
        res["Accent.Windows"] = new SolidColorBrush(AccentColor(IsDark)); // alca do widget em "Tema do Windows"
        res["Brand.Dot"] = Brush(AppInfo.IsTest ? "#FFB57BFF" : "#FFFFC21F");
        res["Level.Fill"] = Brush(IsDark ? "#FFE6E6E6" : "#FF1F1F1F");
        res["Level.Alert"] = Brush(IsDark ? "#FFFF99A4" : "#FFC42B1C");

        // Brasa (pagina de 2026-11-10): acende clara no comeco e some na ponta, como a logo.
        // Barra do painel, anel do widget e os icones de carregando e ao vivo. Muda a regra
        // do nivel neutro dos tokens v2, a pedido do Dillan; o vermelho segue so na bateria baixa.
        Color ember = ((SolidColorBrush)res["Accent.Text"]).Color;
        Color alert = ((SolidColorBrush)res["Level.Alert"]).Color;
        Color track = ((SolidColorBrush)res["Level.Track"]).Color;
        res["Ember.Color"] = ember;
        res["Ember"] = Fade(IsDark ? Mix(ember, Colors.White, 0.5) : Mix(ember, Colors.Black, 0.2), ember, 0.15); // no escuro, o miolo #FFE38A do icone
        res["Ember.Alert"] = Fade(IsDark ? Mix(alert, Colors.White, 0.35) : Mix(alert, Colors.Black, 0.2), alert, 0.25);
        // Trilho que se apaga antes do fim: o limite da carga nao aparece.
        res["Level.TrackFade"] = Gradient((track, 0), (track, 0.35), (WithAlpha(track, 0), 1));

        Changed?.Invoke();
    }

    private static Brush Fade(Color start, Color c, double endAlpha) =>
        Gradient((start, 0), (c, 0.3), (WithAlpha(c, endAlpha), 1));

    private static Brush Gradient(params (Color Color, double At)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        foreach (var (color, at) in stops) b.GradientStops.Add(new GradientStop(color, at));
        b.Freeze();
        return b;
    }

    private static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)Math.Round(c.A * a), c.R, c.G, c.B);

    private static Color Mix(Color a, Color b, double t) => Color.FromArgb(a.A,
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    /// <summary>
    /// A cor de destaque do usuario no tom que o Windows 11 usa para preencher
    /// controles: Light2 no escuro, Dark1 no claro (AccentPalette tem 8 cores
    /// RGBA, de Light3 a Dark3). Sem a chave, o azul padrao do design.
    /// </summary>
    private static Color AccentColor(bool dark)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (key?.GetValue("AccentPalette") is byte[] p && p.Length >= 32)
            {
                int i = (dark ? 1 : 4) * 4;
                return Color.FromRgb(p[i], p[i + 1], p[i + 2]);
            }
        }
        catch (Exception ex) { Core.Log.Error("Theme.Accent", ex); }
        return (Color)ColorConverter.ConvertFromString(dark ? "#4CC2FF" : "#005FB8");
    }

    /// <summary>Destaque das boas-vindas: a cor de destaque pulsando devagar; nasce fraca e cresce (animacao, nenhuma leitura).</summary>
    public static SolidColorBrush Pulse()
    {
        var b = new SolidColorBrush(((SolidColorBrush)System.Windows.Application.Current.Resources["Accent"]).Color);
        b.BeginAnimation(System.Windows.Media.Brush.OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(900))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
        return b;
    }

    private static int ReadDword(string path, string name, int fallback)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            return key?.GetValue(name) is int v ? v : fallback;
        }
        catch { return fallback; }
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
