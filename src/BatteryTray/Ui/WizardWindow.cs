using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BatteryTray.Ui;

/// <summary>
/// Moldura das janelas de telas em sequencia (boas-vindas, "Mandar meu
/// aparelho"): vidro ou sistema, entrada e saida, pontinhos e a troca de tela
/// bloco a bloco. Quem herda monta as telas no XAML e chama InitWizard.
/// </summary>
public class WizardWindow : Window
{
    private Border _root = null!;
    private TranslateTransform _lift = null!;
    private Panel _dots = null!;
    private bool _leaving;

    protected FrameworkElement[] PageList = Array.Empty<FrameworkElement>();
    protected int Page { get; private set; }

    // Curvas e tempos das animacoes: entrada desacelera, saida acelera.
    protected static readonly IEasingFunction EaseOut = new QuinticEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction EaseIn = new CubicEase { EasingMode = EasingMode.EaseIn };
    private static readonly TimeSpan OutTime = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan InTime = TimeSpan.FromMilliseconds(520);
    protected static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(70);

    protected void InitWizard(Border root, TranslateTransform lift, Panel pages, Panel dots, int dotCount)
    {
        _root = root;
        _lift = lift;
        _dots = dots;
        PageList = pages.Children.Cast<FrameworkElement>().ToArray();
        // Cada bloco da tela entra por conta propria (titulo, texto, cada item, em sequencia).
        foreach (var page in PageList)
        {
            page.RenderTransform = new TranslateTransform();
            foreach (var block in ((Panel)page).Children.Cast<UIElement>())
                block.RenderTransform = new TranslateTransform();
        }

        // shortcut: aparencia decidida na abertura; trocar vidro/solido com ela aberta so vale na proxima.
        bool layered = Theme.Layered;
        if (layered)
        {
            AllowsTransparency = true;
            root.CornerRadius = new CornerRadius(8);
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(-1),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
            });
        }

        for (int i = 0; i < dotCount; i++)
            dots.Children.Add(new Border
            {
                Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 6, 0),
                Background = new SolidColorBrush(Colors.Transparent),
            });

        // Sem barra de titulo: arrastar por qualquer parte vazia move a janela.
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not TextBox) DragMove(); };

        SourceInitialized += (_, _) => { if (!layered) Native.KeepBackdropActive(this); else _blur = BlurUnderlay.Attach(this); ApplyTheme(); };
        Loaded += (_, _) => { PlaceBesidePanel(); Enter(); Go(0); Activate(); };
        Theme.Changed += OnThemeChanged;
        Closed += (_, _) => Theme.Changed -= OnThemeChanged;
    }

    private void OnThemeChanged() => Dispatcher.BeginInvoke(() => { ApplyTheme(); UpdateChrome(); });

    private void ApplyTheme()
    {
        if (Theme.Current == Appearance.System) _root.Background = Brushes.Transparent;
        else
        {
            // Como o widget: fica sobre a area de trabalho, entao pelicula de no minimo 80%
            // (com desfoque o fundo ja nao briga com o texto: vale a opacidade escolhida).
            var tint = ((SolidColorBrush)FindResource("Window.Tint")).Color;
            _root.Background = new SolidColorBrush(Color.FromArgb(Theme.Blur ? tint.A : Math.Max(tint.A, (byte)204), tint.R, tint.G, tint.B));
        }
        if (!AllowsTransparency) Native.ApplyBackdrop(this, Theme.Current, Theme.IsDark);
        _blur?.Update(null, 8 * VisualTreeHelper.GetDpi(this).DpiScaleX, Theme.BlurOpacity);
    }

    private BlurUnderlay? _blur;

    /// <summary>A janela surge subindo de leve.</summary>
    private void Enter()
    {
        _root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = EaseOut });
        _lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(560)) { EasingFunction = EaseOut });
    }

    /// <summary>Desce e some, depois fecha.</summary>
    protected void Leave()
    {
        if (_leaving) return;
        _leaving = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = EaseIn };
        fade.Completed += (_, _) => Close();
        _root.BeginAnimation(OpacityProperty, fade);
        _lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, TimeSpan.FromMilliseconds(200)) { EasingFunction = EaseIn });
    }

    /// <summary>Tela nova visivel. start = quando o primeiro bloco comeca a entrar.</summary>
    protected virtual void OnPageShown(int page, int from, TimeSpan start) { }

    /// <summary>Qual pontinho acende nesta tela (telas alternativas dividem um).</summary>
    protected virtual int DotFor(int page) => page;

    /// <summary>
    /// A tela que sai desliza um pouco e some; a que entra vem bloco a bloco,
    /// do lado para onde se esta indo. Clicar rapido nao embaralha: cada
    /// passo recomeca as animacoes das duas telas.
    /// </summary>
    protected void Go(int page)
    {
        int from = Page;
        Page = Math.Clamp(page, 0, PageList.Length - 1);
        UpdateChrome();

        int dir = Page == from ? 0 : Page > from ? 1 : -1;
        var shown = PageList[Page];

        if (Page != from)
        {
            var old = PageList[from];
            var fade = new DoubleAnimation(0, OutTime) { EasingFunction = EaseIn };
            fade.Completed += (_, _) => { if (old != PageList[Page]) old.Visibility = Visibility.Hidden; };
            old.BeginAnimation(OpacityProperty, fade);
            ((TranslateTransform)old.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(-16 * dir, OutTime) { EasingFunction = EaseIn });
        }

        foreach (var other in PageList)
            if (other != shown && other != PageList[from]) other.Visibility = Visibility.Hidden;
        shown.Visibility = Visibility.Visible;
        shown.BeginAnimation(OpacityProperty, null);
        shown.Opacity = 1;
        ((TranslateTransform)shown.RenderTransform).BeginAnimation(TranslateTransform.XProperty, null);

        var start = Page == from ? TimeSpan.FromMilliseconds(120) : OutTime;
        OnPageShown(Page, from, start);
        int i = 0;
        foreach (var block in ((Panel)shown).Children.Cast<UIElement>())
        {
            var at = start + Stagger * i++;
            block.Opacity = 0;
            block.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, InTime) { BeginTime = at, EasingFunction = EaseOut });
            var t = (TranslateTransform)block.RenderTransform;
            // Primeira abertura: sobe; trocando de tela: vem do lado.
            t.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(28 * dir, 0, InTime) { BeginTime = at, EasingFunction = EaseOut });
            t.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dir == 0 ? 12 : 0, 0, InTime) { BeginTime = at, EasingFunction = EaseOut });
        }
    }

    /// <summary>
    /// No meio da tela principal, mas sem cobrir o painel (380 px no canto
    /// direito, 12 de folga): em tela estreita, desliza para a esquerda.
    /// Em pixels, no monitor do mouse, como o painel (SystemParameters vem na escala do login).
    /// </summary>
    private void PlaceBesidePanel()
    {
        var (cx, cy) = Native.CursorPos();
        var work = Native.MonitorAt(cx, cy).Work;
        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)Math.Round(ActualWidth * dpi.DpiScaleX), h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        int gap = (int)Math.Round(12 * dpi.DpiScaleX), panel = (int)Math.Round((380 + 12 + 24) * dpi.DpiScaleX);
        int left = Math.Max(work.Left + gap, Math.Min(work.Left + (work.Width - w) / 2, work.Right - panel - w));
        Native.MoveWindowTo(this, left, work.Top + Math.Max(gap, (work.Height - h) / 2));
    }

    /// <summary>Pontinhos; quem herda acrescenta os botoes da tela.</summary>
    protected virtual void UpdateChrome()
    {
        var on = ((SolidColorBrush)FindResource("Accent")).Color;
        var off = ((SolidColorBrush)FindResource("Level.Track")).Color;
        var time = TimeSpan.FromMilliseconds(360);
        int lit = DotFor(Page);
        for (int i = 0; i < _dots.Children.Count; i++)
        {
            var dot = (Border)_dots.Children[i];
            dot.BeginAnimation(WidthProperty, new DoubleAnimation(i == lit ? 18 : 6, time) { EasingFunction = EaseOut });
            dot.Background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(i == lit ? on : off, time) { EasingFunction = EaseOut });
        }
    }
}
