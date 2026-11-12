using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BatteryTray.Core;

namespace BatteryTray.Ui;

/// <summary>
/// Boas-vindas da beta: abre sozinha na primeira vez e pelas Configuracoes.
/// Explica o que e' beta, mostra o que o Joule achou (ou o que fazer sem
/// nada), onde o app mora, como le e o "feche e abra de novo".
/// So texto e animacao: nao toca em aparelho nenhum.
/// </summary>
public partial class OnboardingWindow : WizardWindow
{
    public const int FoundPage = 1;
    private bool _empty = true;

    /// <summary>Tela nova (0 a 4): quem assina abre e destaca painel, widget e bandeja.</summary>
    public event Action<int>? StepChanged;

    /// <summary>"Meu aparelho nao apareceu" ou "Mandar meu aparelho".</summary>
    public event Action? SendDeviceRequested;

    /// <summary>"Procurar de novo" na tela sem aparelho: uma leitura sob demanda.</summary>
    public event Action? SearchAgainRequested;

    public OnboardingWindow()
    {
        InitializeComponent();
        InitWizard(Root, Lift, Pages, Dots, 5);

        Title = L.T($"Boas-vindas ao {AppInfo.Name}", $"Welcome to {AppInfo.Name}");
        BrandText.Text = AppInfo.Name;
        WelcomeTitle.Text = L.T($"Boas-vindas ao {AppInfo.Name}", $"Welcome to {AppInfo.Name}");

        NextButton.Click += (_, _) => { if (Page == PageList.Length - 1) Leave(); else Go(Page + 1); };
        BackButton.Click += (_, _) => Go(Page - 1);
        SkipButton.Click += (_, _) => Leave();
        MissingButton.Click += (_, _) => SendDeviceRequested?.Invoke();
        SendButton.Click += (_, _) => SendDeviceRequested?.Invoke();
        SearchAgainButton.Click += (_, _) => SearchAgainRequested?.Invoke();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Leave();
            else if (e.Key == Key.Right && Page < PageList.Length - 1) Go(Page + 1);
            else if (e.Key == Key.Left && Page > 0) Go(Page - 1);
        };
        ShowFound(Array.Empty<DeviceRowView>(), 0);
    }

    /// <summary>A tela "Achei estes aparelhos", com a ultima leitura (vazia: o que fazer).</summary>
    public void ShowFound(IReadOnlyList<DeviceRowView> rows, int others)
    {
        _empty = rows.Count == 0;
        FoundTitle.Text = _empty ? L.T("Ainda não achei nenhum aparelho", "I haven’t found any device yet") : L.T("Achei estes aparelhos", "I found these devices");
        FoundLead.Text = _empty ? L.T("Pode ser só que ele esteja desligado. Antes de tudo:", "It may just be turned off. First of all:") : L.T("É o que o Joule já sabe ler neste computador.", "This is what Joule can already read on this computer.");
        FoundList.Visibility = MissingCard.Visibility = _empty ? Visibility.Collapsed : Visibility.Visible;
        EmptyPoints.Visibility = _empty ? Visibility.Visible : Visibility.Collapsed;
        OthersNote.Visibility = !_empty && others > 0 ? Visibility.Visible : Visibility.Collapsed;
        OthersNote.Text = others == 1
            ? L.T("E mais 1 que ele achou, mas ainda não sabe ler. Fica em “Mostrar outros”, no fim do painel.", "Plus 1 it found but can’t read yet. It’s under “Show others” at the bottom of the panel.")
            : L.T($"E mais {others} que ele achou, mas ainda não sabe ler. Ficam em “Mostrar outros”, no fim do painel.", $"Plus {others} it found but can’t read yet. They’re under “Show others” at the bottom of the panel.");

        FoundList.Children.Clear();
        foreach (var row in rows.Take(4)) FoundList.Children.Add(FoundRow(row));
        UpdateChrome();
    }

    private UIElement FoundRow(DeviceRowView v)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        var tile = new Border { Style = (Style)FindResource("TileBox"), VerticalAlignment = VerticalAlignment.Center };
        tile.Child = new Viewbox
        {
            Width = 18, Height = 18,
            Child = new System.Windows.Shapes.Path { Style = (Style)FindResource("IconGlyph"), Data = Icons.For(v.Kind), Stroke = (Brush)FindResource("Text.Secondary") },
        };
        DockPanel.SetDock(tile, Dock.Left);
        dock.Children.Add(tile);

        var value = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        string text = v.ValueKind == ValueKind.Percent ? $"{v.Percent}%" : v.BandLabel;
        value.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });
        if (v.ValueKind == ValueKind.Percent)
        {
            var track = new Border { Width = 56, Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 5, 0, 0), Background = (Brush)FindResource("Level.TrackFade") };
            track.Child = new Border { Width = 56 * v.Percent / 100.0, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Background = (Brush)FindResource("Ember") };
            value.Children.Add(track);
        }
        DockPanel.SetDock(value, Dock.Right);
        dock.Children.Add(value);

        var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock { Text = v.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        name.Children.Add(new TextBlock { Text = Known.OriginLabel(v.Origin), Style = (Style)FindResource("Caption") });
        dock.Children.Add(name);
        return dock;
    }

    protected override void OnPageShown(int page, int from, TimeSpan start)
    {
        if (page != from || page == 0) StepChanged?.Invoke(page);
        if (page == 0)
            FillBar.BeginAnimation(WidthProperty, new DoubleAnimation(0, 176, TimeSpan.FromMilliseconds(1100))
            {
                BeginTime = start + Stagger * 3 + TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            });
    }

    protected override void UpdateChrome()
    {
        bool last = Page == PageList.Length - 1;
        NextButton.Content = last ? L.T("Começar", "Get started") : Page == FoundPage && _empty ? L.T("Continuar sem aparelho", "Continue without a device") : L.T("Próximo", "Next");
        BackButton.Visibility = Page == 0 ? Visibility.Hidden : Visibility.Visible;
        SkipButton.Visibility = last ? Visibility.Hidden : Visibility.Visible;
        base.UpdateChrome();
    }
}
