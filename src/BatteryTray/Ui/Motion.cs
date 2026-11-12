using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BatteryTray.Ui;

/// <summary>
/// O movimento do Joule num lugar so (pagina aprovada em 2026-11-10,
/// https://claude.ai/artifact/2N5aV1awJY2sNRJsrQ6JtN): a curva de saida das
/// janelas de telas (QuinticEase) para chegar, CubicEase de entrada para sair,
/// e sair sempre mais rapido que chegar. Com "Mostrar animacoes" desligado no
/// Windows (SystemParameters.ClientAreaAnimation), tudo vira corte seco.
///
/// <see cref="HoverProperty"/>: no lugar do Setter de IsMouseOver (troca de
/// cor de uma vez), a cor do fundo desliza em 150 ms; com
/// <see cref="PressProperty"/>, o clique afunda o elemento um pouco.
/// </summary>
public static class Motion
{
    public static readonly IEasingFunction Out = Frozen(new QuinticEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction In = Frozen(new CubicEase { EasingMode = EasingMode.EaseIn });

    private static IEasingFunction Frozen(EasingFunctionBase e) { e.Freeze(); return e; }

    public static bool Enabled => SystemParameters.ClientAreaAnimation;

    public static Duration Ms(double ms) => new(TimeSpan.FromMilliseconds(Enabled ? ms : 1));

    // ---------------- hover ----------------

    public static readonly DependencyProperty HoverProperty = DependencyProperty.RegisterAttached("Hover", typeof(Brush), typeof(Motion),
        new PropertyMetadata(null, OnHoverChanged));
    public static Brush? GetHover(DependencyObject d) => (Brush?)d.GetValue(HoverProperty);
    public static void SetHover(DependencyObject d, Brush? v) => d.SetValue(HoverProperty, v);

    /// <summary>Escala do clique (0,97 em botao, 0,985 em linha larga). 0 = sem.</summary>
    public static readonly DependencyProperty PressProperty = DependencyProperty.RegisterAttached("Press", typeof(double), typeof(Motion),
        new PropertyMetadata(0.0, OnPressChanged));
    public static double GetPress(DependencyObject d) => (double)d.GetValue(PressProperty);
    public static void SetPress(DependencyObject d, double v) => d.SetValue(PressProperty, v);

    private static void OnHoverChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border b || e.OldValue is not null) return;
        // O Border pode estar num template: quem dispara e' o pai que recebe o mouse (o Button), ou ele mesmo.
        b.Loaded += (_, _) =>
        {
            var host = (b.TemplatedParent as UIElement) ?? b;
            if (host.GetValue(HostedProperty) is true) return; // Loaded dispara de novo a cada vez que a janela reaparece
            host.SetValue(HostedProperty, true);
            host.MouseEnter += (_, _) => Fade(b, true);
            host.MouseLeave += (_, _) => Fade(b, false);
        };
    }

    private static readonly DependencyProperty HostedProperty = DependencyProperty.RegisterAttached("Hosted", typeof(bool), typeof(Motion));

    private static void Fade(Border b, bool on)
    {
        if (GetHover(b) is not SolidColorBrush hover) return;
        var current = (b.Background as SolidColorBrush)?.Color ?? Colors.Transparent;
        var rest = RestColor(b, hover.Color);
        var target = on ? hover.Color : rest;
        if (current.A == 0) current = Color.FromArgb(0, target.R, target.G, target.B); // de transparente: sem passar pelo branco
        var brush = new SolidColorBrush(current);
        b.Background = brush;
        var anim = new ColorAnimation(target, Ms(on ? 150 : 200));
        if (!on) anim.Completed += (_, _) => { if (!b.IsMouseOver && !(b.TemplatedParent is UIElement p && p.IsMouseOver)) b.ClearValue(Border.BackgroundProperty); };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    /// <summary>A cor de descanso: o fundo que o template/estilo daria (lido com o valor local limpo por um instante).</summary>
    private static Color RestColor(Border b, Color hover)
    {
        var local = b.ReadLocalValue(Border.BackgroundProperty);
        b.ClearValue(Border.BackgroundProperty);
        var rest = (b.Background as SolidColorBrush)?.Color ?? Color.FromArgb(0, hover.R, hover.G, hover.B);
        if (local != DependencyProperty.UnsetValue) b.SetValue(Border.BackgroundProperty, local);
        return rest.A == 0 ? Color.FromArgb(0, hover.R, hover.G, hover.B) : rest;
    }

    private static void OnPressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement el || (double)e.OldValue != 0) return;
        var scale = new ScaleTransform(1, 1);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = scale;
        void To(double v, double ms) { scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(v, Ms(ms)) { EasingFunction = Out }); scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(v, Ms(ms)) { EasingFunction = Out }); }
        el.PreviewMouseLeftButtonDown += (_, _) => To(GetPress(el), 90);
        el.PreviewMouseLeftButtonUp += (_, _) => To(1, 220);
        el.MouseLeave += (_, _) => To(1, 220);
    }

    // ---------------- interruptor ----------------

    /// <summary>
    /// Interruptor 40x20: a bolinha ("Knob" no template) desliza 20 px em 260 ms
    /// ao clicar e estica um pouco enquanto o botao esta apertado, como no
    /// Windows 11. Ao carregar ela ja nasce no lugar, sem animar.
    /// </summary>
    public static readonly DependencyProperty SwitchProperty = DependencyProperty.RegisterAttached("Switch", typeof(bool), typeof(Motion),
        new PropertyMetadata(false, OnSwitchChanged));
    public static bool GetSwitch(DependencyObject d) => (bool)d.GetValue(SwitchProperty);
    public static void SetSwitch(DependencyObject d, bool v) => d.SetValue(SwitchProperty, v);

    private static void OnSwitchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.Primitives.ToggleButton tb || e.NewValue is not true) return;
        const double Travel = 20;
        System.Windows.Shapes.Ellipse? Knob() => tb.Template?.FindName("Knob", tb) as System.Windows.Shapes.Ellipse;
        TranslateTransform Move(System.Windows.Shapes.Ellipse k)
        {
            if (k.RenderTransform is TranslateTransform t) return t;
            t = new TranslateTransform();
            k.RenderTransform = t;
            return t;
        }
        void Place(bool animate)
        {
            if (Knob() is not { } k) return;
            var t = Move(k);
            double to = tb.IsChecked == true ? Travel : 0;
            if (animate) Run(t, TranslateTransform.XProperty, null, to, 260, Out);
            else { t.BeginAnimation(TranslateTransform.XProperty, null); t.X = to; }
            Run(k, FrameworkElement.WidthProperty, null, 12, 160, Out);
        }
        tb.Loaded += (_, _) => Place(false);
        tb.Checked += (_, _) => Place(true);
        tb.Unchecked += (_, _) => Place(true);
        tb.PreviewMouseLeftButtonDown += (_, _) =>
        {
            if (Knob() is not { } k) return;
            Run(k, FrameworkElement.WidthProperty, null, 15, 120, Out);
            // esticar para dentro: do lado direito a bolinha cresce para a esquerda
            if (tb.IsChecked == true) Run(Move(k), TranslateTransform.XProperty, null, Travel - 3, 120, Out);
        };
        tb.MouseLeave += (_, _) => Place(true);
    }

    // ---------------- ajudantes ----------------

    /// <summary>Anima <paramref name="p"/> de <paramref name="from"/> (null = de onde esta) ate <paramref name="to"/>.</summary>
    public static void Run(Animatable target, DependencyProperty p, double? from, double to, double ms, IEasingFunction ease, Action? done = null, double delayMs = 0)
    {
        var a = new DoubleAnimation(to, Ms(ms)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(Enabled ? delayMs : 0) };
        if (from is double f) a.From = f;
        if (done is not null) a.Completed += (_, _) => done();
        target.BeginAnimation(p, a);
    }

    public static void Run(UIElement target, DependencyProperty p, double? from, double to, double ms, IEasingFunction ease, Action? done = null, double delayMs = 0)
    {
        var a = new DoubleAnimation(to, Ms(ms)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(Enabled ? delayMs : 0) };
        if (from is double f) a.From = f;
        if (done is not null) a.Completed += (_, _) => done();
        target.BeginAnimation(p, a);
    }

    /// <summary>Entrada em sequencia (lista que aparece como lista): desliza <paramref name="dx"/> e acende, 70 ms entre um e outro.</summary>
    public static void Stagger(IEnumerable<UIElement> items, double dx, double dy = 0, double startMs = 0)
    {
        int i = 0;
        foreach (var el in items)
        {
            var t = new TranslateTransform(dx, dy);
            el.RenderTransform = t;
            double at = startMs + Math.Min(i++, 6) * 70;
            el.Opacity = Enabled ? 0 : 1;
            Run(el, UIElement.OpacityProperty, 0, 1, 360, Out, null, at);
            Run(t, TranslateTransform.XProperty, dx, 0, 520, Out, null, at);
            Run(t, TranslateTransform.YProperty, dy, 0, 520, Out, null, at);
        }
    }

    /// <summary>Tela que entra (painel, Configuracoes, Sobre): do lado da seta, 28 px, com a curva de chegada.</summary>
    public static void EnterView(UIElement view, int dir)
    {
        var t = new TranslateTransform(28 * dir, 0);
        view.RenderTransform = t;
        Run(view, UIElement.OpacityProperty, 0, 1, 300, Out);
        Run(t, TranslateTransform.XProperty, 28 * dir, 0, 420, Out);
    }
}
