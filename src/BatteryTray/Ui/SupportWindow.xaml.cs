using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BatteryTray.Core;

namespace BatteryTray.Ui;

/// <summary>
/// Depois de cada atualizacao: o que o Joule fez (contadores so deste PC),
/// o pedido de apoio e, depois de "Agora nao" ou "Apoiar", "O que mudou".
/// Linha com zero some; sem nada para contar, o resumo inteiro some.
/// Sem AppInfo.SupportUrl, sem botao de apoio nem frase do cafe.
/// </summary>
public partial class SupportWindow : WizardWindow
{
    // A frase do cafe sorteia um comeco e um final (pedido do Dillan, 2026-11-12).
    private static readonly (string Pt, string En)[] Saves =
    {
        ("de um mouse apagado no meio da partida", "a dead mouse mid-match"),
        ("de um fone mudo no meio da reunião", "a silent headset mid-meeting"),
        ("de um controle apagado no último round", "a dead controller in the final round"),
        ("de um teclado sem bateria na hora de entregar o trabalho", "a flat keyboard right before a deadline"),
        ("de um fone apagado no meio da aula", "a dead headset mid-class"),
        ("de um mouse parado bem na hora do clutch", "a frozen mouse right at the clutch"),
    };
    private static readonly (string Pt, string En)[] Coffees =
    {
        ("um café ajuda a mantê-lo assim", "a coffee helps keep it that way"),
        ("um café ajuda a testar mais periféricos", "a coffee helps test more peripherals"),
        ("um café ajuda a pagar o Claude que escreve o código", "a coffee helps pay for the Claude that writes the code"),
        ("um café ajuda a sair a próxima versão", "a coffee helps the next version ship"),
        ("um café ajuda a ensinar o Joule a ler mais marcas", "a coffee helps teach Joule more brands"),
        ("um café mantém o desenvolvimento aceso", "a coffee keeps development going"),
    };

    private readonly bool _hasLink = AppInfo.SupportUrl.Length > 0;
    private readonly bool _hasChanges;

    public SupportWindow(int devices, int lowAlerts, IReadOnlyList<Change> changes)
    {
        InitializeComponent();
        _hasChanges = changes.Count > 0;
        InitWizard(Root, Lift, Pages, Dots, _hasChanges ? 2 : 0);

        Title = AppInfo.Name;
        BrandText.Text = AppInfo.Name;
        VersionText.Text = L.T($"Versão {AppInfo.Version}", $"Version {AppInfo.Version}");

        DevicesRow.Visibility = devices > 0 ? Visibility.Visible : Visibility.Collapsed;
        DevicesCount.Text = devices.ToString();
        DevicesLabel.Text = L.N(devices, "aparelho acompanhado", "aparelhos acompanhados", "device tracked", "devices tracked");
        LowRow.Visibility = lowAlerts > 0 ? Visibility.Visible : Visibility.Collapsed;
        LowCount.Text = lowAlerts.ToString();
        LowLabel.Text = L.N(lowAlerts, "aviso de bateria baixa", "avisos de bateria baixa", "low-battery warning", "low-battery warnings");
        LowCaption.Text = lowAlerts == 1
            ? L.T("1 vez que você soube antes de o aparelho apagar.", "1 time you knew before a device died.")
            : L.T($"{lowAlerts} vezes que você soube antes de o aparelho apagar.", $"{lowAlerts} times you knew before a device died.");
        SummaryRule.Visibility = devices > 0 && lowAlerts > 0 ? Visibility.Visible : Visibility.Collapsed;
        Summary.Visibility = SummaryLead.Visibility = devices > 0 || lowAlerts > 0 ? Visibility.Visible : Visibility.Collapsed;
        PrivacyText.Visibility = Summary.Visibility;

        var (savePt, saveEn) = Saves[Random.Shared.Next(Saves.Length)];
        var (coffeePt, coffeeEn) = Coffees[Random.Shared.Next(Coffees.Length)];
        string save = L.T(savePt, saveEn), coffee = L.T(coffeePt, coffeeEn);
        Phrase.Text = L.T($"O Joule é grátis e de código aberto. Se ele já te salvou {save}, {coffee}.",
                          $"Joule is free and open source. If it ever saved you from {save}, {coffee}.");
        Phrase.Visibility = _hasLink ? Visibility.Visible : Visibility.Collapsed;

        foreach (var c in changes) ChangesList.Children.Add(ChangeRow(c));

        LaterButton.Click += (_, _) => Next();
        NextButton.Click += (_, _) => Next();
        SupportButton.Click += (_, _) => { TrayController.OpenExternal(AppInfo.SupportUrl); Next(); };
        OkButton.Click += (_, _) => Leave();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Leave(); };

        // Clicar numa bolinha vai para a tela dela; a faixa inteira recebe o clique, não só os 6 px.
        Dots.Background = Brushes.Transparent;
        Dots.Cursor = Cursors.Hand;
        Dots.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true; // sem isso a janela começa a arrastar
            double x = e.GetPosition(Dots).X;
            var near = Dots.Children.Cast<FrameworkElement>()
                .MinBy(d => Math.Abs(d.TranslatePoint(new Point(d.ActualWidth / 2, 0), Dots).X - x));
            int page = near is null ? Page : Dots.Children.IndexOf(near);
            if (page != Page) Go(page);
        };
    }

    private void Next() { if (Page == 0 && _hasChanges) Go(1); else Leave(); }

    private UIElement ChangeRow(Change c)
    {
        var dock = new DockPanel { Style = (Style)FindResource("Point") };
        var tile = new Border { Style = (Style)FindResource("TileBox") };
        tile.Child = new Viewbox
        {
            Width = 18, Height = 18,
            Child = new System.Windows.Shapes.Path { Style = (Style)FindResource("Glyph"), Data = Geometry.Parse(c.Glyph), Stroke = (Brush)FindResource("Text.Secondary") },
        };
        DockPanel.SetDock(tile, Dock.Left);
        dock.Children.Add(tile);
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Style = (Style)FindResource("PointTitle"), Text = c.Title });
        text.Children.Add(new TextBlock { Style = (Style)FindResource("Caption"), Text = c.Text });
        dock.Children.Add(text);
        return dock;
    }

    protected override void UpdateChrome()
    {
        bool first = Page == 0;
        LaterButton.Visibility = SupportButton.Visibility = first && _hasLink ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = first && !_hasLink ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = _hasChanges ? L.T("Próximo", "Next") : "OK";
        OkButton.Visibility = first ? Visibility.Collapsed : Visibility.Visible;
        PrivacyText.Opacity = first ? 1 : 0;
        Dots.Visibility = first ? Visibility.Hidden : Visibility.Visible;
        base.UpdateChrome();
    }
}
