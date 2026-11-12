using System.Windows.Media;
using BatteryTray.Core;

namespace BatteryTray.Ui;

/// <summary>
/// Icones "Silhueta" (pagina aprovada em 2026-11-10,
/// https://claude.ai/artifact/EcDQAWtAJaq6skqmhzCrLp): viewBox 20x20, tudo
/// forma cheia. O que no desenho e' traco (arco do fone, setas, ponteiros) e'
/// alargado aqui com espessura 2, entao quem desenha so pinta o Fill (o estilo
/// Glyph pinta o Fill com a cor do Stroke).
/// </summary>
public static class Icons
{
    public static readonly Geometry Mouse = F("M10 2a5 5 0 0 1 5 5v6a5 5 0 0 1-10 0V7a5 5 0 0 1 5-5z" + Cap(9.1, 4.6, 1.8, 4.8));
    public static readonly Geometry Keyboard = F("M4.5 4.5h11a3 3 0 0 1 3 3v5a3 3 0 0 1-3 3h-11a3 3 0 0 1-3-3v-5a3 3 0 0 1 3-3z"
        + Dot(6.5, 7.6, 0.9) + Dot(10, 7.6, 0.9) + Dot(13.5, 7.6, 0.9) + Cap(6.8, 10.9, 6.4, 1.8));
    public static readonly Geometry Headset = F(Cap(2.25, 10.5, 4.5, 7) + Cap(13.25, 10.5, 4.5, 7), "M3.75 10.6V10a6.25 6.25 0 0 1 12.5 0v0.6");
    public static readonly Geometry Gamepad = F("M6.5 5h7a5 5 0 0 1 0 10h-7a5 5 0 0 1 0-10zM5.3 8.3H6.7V9.3H7.7V10.7H6.7V11.7H5.3V10.7H4.3V9.3H5.3Z"
        + Dot(13.3, 8.9, 0.85) + Dot(14.95, 11.1, 0.85));
    public static readonly Geometry Generic = F("M8 2.5h4a3 3 0 0 1 3 3v9a3 3 0 0 1-3 3H8a3 3 0 0 1-3-3v-9a3 3 0 0 1 3-3z" + Cap(8.3, 13.5, 3.4, 1.6));
    /// <summary>Plugue USB-C: a ponta em pilula com a lingueta recortada, corpo, alivio e cabo.</summary>
    public static readonly Geometry Plug = F(Cap(6.5, 3, 7, 3.2) + Cap(7.9, 4, 4.2, 1.2)
        + "M7.7 6.8h4.6a2.2 2.2 0 0 1 2.2 2.2v1.8a2.2 2.2 0 0 1-2.2 2.2H7.7a2.2 2.2 0 0 1-2.2-2.2V9a2.2 2.2 0 0 1 2.2-2.2zM8.6 13h2.8v1.2a1.4 1.4 0 0 1-2.8 0z",
        "M10 15.4V18");

    public static readonly Geometry Refresh = F(null, "M16.25 10a6.25 6.25 0 1 1-1.83-4.42M14.5 2.75v3h-3");
    public static readonly Geometry Spinner = F(null, "M16 10a6 6 0 1 1-6-6");
    public static readonly Geometry Sliders = F(Cap(4.5, 4.5, 6, 4) + Cap(9.5, 11.5, 6, 4), "M3 6.5h14M3 13.5h14");
    public static readonly Geometry Chevron = F(null, "M5 8l5 5 5-5");
    public static readonly Geometry Back = F(null, "M12.5 4.5 7 10l5.5 5.5");
    public static readonly Geometry Clock = F("M10 3a7 7 0 1 1 0 14a7 7 0 1 1 0-14zM9.25 6.25a0.75 0.75 0 0 1 1.5 0V9.6l2.1 1.3a0.75 0.75 0 0 1-0.8 1.3l-2.45-1.5a0.75 0.75 0 0 1-0.35-0.65z");
    public static readonly Geometry Bolt = F("M11.4 2.2 5.2 11.4h4.6l-1.2 6.4 6.2-9.2h-4.6z");
    public static readonly Geometry Folder = F("M2.75 6.25a1.75 1.75 0 0 1 1.75-1.75h3.25l1.75 2h6a1.75 1.75 0 0 1 1.75 1.75v5.5a1.75 1.75 0 0 1-1.75 1.75h-11a1.75 1.75 0 0 1-1.75-1.75z");
    public static readonly Geometry ChevronRight = F(null, "M7.5 4.5 13 10l-5.5 5.5");
    public static readonly Geometry Info = F("M10 3a7 7 0 1 1 0 14a7 7 0 1 1 0-14zM9.25 9.25h1.5v4.5h-1.5z" + Dot(10, 6.6, 0.9));
    public static readonly Geometry External = F(null, "M11.5 4.5h4v4M15.5 4.5l-6 6M13.5 11.5v2.5a1.5 1.5 0 0 1-1.5 1.5H6a1.5 1.5 0 0 1-1.5-1.5V8A1.5 1.5 0 0 1 6 6.5h2.5");

    public static Geometry For(DeviceKind kind) => kind switch
    {
        DeviceKind.Mouse => Mouse,
        DeviceKind.Keyboard => Keyboard,
        DeviceKind.Headset => Headset,
        DeviceKind.Gamepad => Gamepad,
        _ => Generic,
    };

    /// <summary>Forma cheia (par-impar: os recortes viram furos) unida ao traco alargado.</summary>
    private static Geometry F(string? fill, string? stroke = null)
    {
        Geometry? g = fill is null ? null : Geometry.Parse("F0 " + fill);
        if (stroke is not null)
        {
            var pen = new Pen(Brushes.Black, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            Geometry wide = Geometry.Parse(stroke).GetWidenedPathGeometry(pen);
            g = g is null ? wide : Geometry.Combine(g, wide, GeometryCombineMode.Union, null);
        }
        g!.Freeze();
        return g;
    }

    /// <summary>Capsula (pilula) no retangulo dado, deitada ou em pe.</summary>
    private static string Cap(double x, double y, double w, double h)
    {
        double r = Math.Min(w, h) / 2;
        return w >= h
            ? FormattableString.Invariant($"M{x + r} {y}h{w - 2 * r}a{r} {r} 0 0 1 0 {h}h{-(w - 2 * r)}a{r} {r} 0 0 1 0 {-h}z")
            : FormattableString.Invariant($"M{x} {y + r}a{r} {r} 0 0 1 {w} 0v{h - 2 * r}a{r} {r} 0 0 1 {-w} 0z");
    }

    private static string Dot(double x, double y, double r) =>
        FormattableString.Invariant($"M{x} {y - r}a{r} {r} 0 1 0 0 {2 * r}a{r} {r} 0 1 0 0 {-2 * r}z");
}
