using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BatteryTray.Core;
using BatteryTray.Interop;
using BatteryTray.Scan;

namespace BatteryTray.Ui;

/// <summary>
/// "Mandar meu aparelho" (telas aprovadas no Claude Design em 2026-11-07:
/// https://claude.ai/artifact/UEuwDLdvBMspxkBezFobWJ). Coleta como o
/// JouleScan, escuta 30 s, mostra o que achou, tira os dados pessoais e deixa
/// a pessoa mandar pelo GitHub ou por e-mail. A coleta e' sob demanda (o
/// clique de quem abriu) e a escuta e' passiva: nada e' escrito no aparelho.
/// </summary>
public partial class SendDeviceWindow : WizardWindow
{
    private const int Before = 0, Listening = 1, Found = 2, Review = 3, GitHub = 4, Email = 5;

    private readonly IReadOnlyList<DeviceEntry> _lastReading;
    private ScanReport? _report;
    private IReadOnlyList<HidCollection>? _collections;
    private CancellationTokenSource? _listen;
    private string? _file;

    public SendDeviceWindow(IReadOnlyList<DeviceEntry> lastReading)
    {
        _lastReading = lastReading;
        InitializeComponent();
        InitWizard(Root, Lift, Pages, Dots, 5);

        Title = L.T($"Mandar meu aparelho · {AppInfo.Name}", $"Send my device · {AppInfo.Name}");
        BrandText.Text = AppInfo.Name;
        AddNameRow();

        AddNameButton.Click += (_, _) => AddNameRow().Focus();
        CancelButton.Click += (_, _) => Leave();
        PrimaryButton.Click += (_, _) => OnPrimary();
        SecondaryButton.Click += (_, _) => OnSecondary();
        NotepadButton.Click += (_, _) => Open("notepad.exe", Quote(_file!));
        ToEmailLink.Click += (_, _) => Go(Email);
        GmailButton.Click += (_, _) => Open(DeviceSubmission.GmailUrl(AppInfo.ContactEmail, Subject, MailBody));
        OutlookButton.Click += (_, _) => Open(DeviceSubmission.OutlookUrl(AppInfo.ContactEmail, Subject, MailBody));
        MailAppButton.Click += (_, _) => Open(DeviceSubmission.MailtoUrl(AppInfo.ContactEmail, Subject, MailBody));
        CopyAddressButton.Click += (_, _) => { try { Clipboard.SetText(AppInfo.ContactEmail); } catch { /* area de transferencia ocupada */ } };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Leave(); };
        // Fechar no meio da escuta encerra os handles (um CancelIoEx cada, no PassiveCapture).
        Closed += (_, _) => _listen?.Cancel();

        RepoText.Text = L.T($"{AppInfo.Repository}/issues · público", $"{AppInfo.Repository}/issues · public");
        AddressText.Text = L.T($"Outro serviço? Mande para {AppInfo.ContactEmail}", $"Another service? Send to {AppInfo.ContactEmail}");
        MailAppButton.Visibility = HasMailApp() ? Visibility.Visible : Visibility.Collapsed;
        MailButtons.Columns = MailAppButton.Visibility == Visibility.Visible ? 3 : 2;
        NoAccountNote.Visibility = HasEmail ? Visibility.Visible : Visibility.Collapsed;
        foreach (string removed in new[] { L.T("Número de série", "Serial number"), L.T("Endereço Bluetooth", "Bluetooth address"), L.T("Nome do computador", "Computer name"), L.T("Seu nome de usuário", "Your user name") })
            RemovedGrid.Children.Add(Line(removed, "M5 10h10", "Text.Tertiary", secondary: true));
        RemovedPhones.Content = Line(L.T("Celulares, caixas de som e outros Bluetooth que não são periféricos", "Phones, speakers and other Bluetooth devices that aren’t peripherals"), "M5 10h10", "Text.Tertiary", secondary: true);
        int n = 1;
        foreach (string step in new[] { L.T("Entre na sua conta do GitHub, se ele pedir.", "Sign in to your GitHub account, if it asks."), L.T("Arraste o arquivo abaixo para a caixa de texto da página.", "Drag the file below into the page’s text box."), L.T("Clique no botão verde para enviar.", "Click the green button to send.") })
            StepsList.Children.Add(Step(n++, step));
    }

    private static bool HasEmail => AppInfo.ContactEmail.Length > 0;

    private List<string> Names() => NameRows.Children.OfType<DockPanel>()
        .Select(row => row.Children.OfType<TextBox>().First().Text.Trim())
        .Where(t => t.Length > 0).ToList();

    private string Subject => DeviceSubmission.Title(Names());
    private string MailBody => DeviceSubmission.Body(Names(), Path.GetFileName(_file ?? ""), AppInfo.Version).Replace("esta caixa", "este e-mail");

    private TextBox AddNameRow()
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var box = new TextBox { Style = (Style)FindResource("Field"), Tag = L.T("Marca e modelo, como está na caixa", "Brand and model, as on the box") };
        AutomationName(box, L.T($"Aparelho {NameRows.Children.Count + 1}", $"Device {NameRows.Children.Count + 1}"));
        if (NameRows.Children.Count > 0)
        {
            var remove = new Button { Style = (Style)FindResource("FlatButton"), Width = 34, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(0) };
            remove.Content = new Viewbox { Width = 12, Height = 12, Child = new System.Windows.Shapes.Path { Style = (Style)FindResource("Glyph"), Data = Geometry.Parse("M5 5l10 10M15 5 5 15"), Stroke = (Brush)FindResource("Text.Tertiary"), StrokeThickness = 1.8 } };
            AutomationName(remove, L.T("Tirar este aparelho", "Remove this device"));
            remove.Click += (_, _) => NameRows.Children.Remove(row);
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
        }
        row.Children.Add(box);
        NameRows.Children.Add(row);
        return box;
    }

    private void OnPrimary()
    {
        switch (Page)
        {
            case Before: Go(Listening); break;
            case Found: Go(Review); break;
            case Review:
                Open(DeviceSubmission.GitHubUrl(AppInfo.NewIssueUrl, Subject,
                    DeviceSubmission.Body(Names(), Path.GetFileName(_file!), AppInfo.Version)));
                Go(GitHub);
                break;
            default: Leave(); break; // Concluir
        }
    }

    private void OnSecondary()
    {
        switch (Page)
        {
            case Listening: _listen?.Cancel(); break; // Terminar agora: segue com o que chegou
            case Found: Go(Listening); break;         // Escutar de novo, nas mesmas colecoes
            case Review: Go(Email); break;
            case GitHub:
                Open(DeviceSubmission.GitHubUrl(AppInfo.NewIssueUrl, Subject,
                    DeviceSubmission.Body(Names(), Path.GetFileName(_file!), AppInfo.Version)));
                break;
        }
    }

    protected override int DotFor(int page) => Math.Min(page, GitHub);

    protected override void OnPageShown(int page, int from, TimeSpan start)
    {
        if (page == Listening && from != Listening) StartListening();
        if (page == Found) ShowFound();
        if (page == Review) SaveFile();
        if (page is GitHub or Email)
        {
            GitChipSlot.Content = page == GitHub ? FileChip(L.T("Arraste daqui", "Drag from here")) : null;
            MailChipSlot.Content = page == Email ? FileChip(L.T("Anexe este arquivo, ou arraste daqui", "Attach this file, or drag from here")) : null;
        }
    }

    protected override void UpdateChrome()
    {
        (string? secondary, string? primary) = Page switch
        {
            Before => (null, L.T("Começar a escutar", "Start listening")),
            Listening => (L.T("Terminar agora", "Finish now"), null),
            Found => (L.T("Escutar de novo", "Listen again"), L.T("Próximo", "Next")),
            Review => (HasEmail ? L.T("Por e-mail", "By email") : null, L.T("Mandar pelo GitHub", "Send via GitHub")),
            GitHub => (L.T("Abrir a página de novo", "Open the page again"), L.T("Concluir", "Done")),
            _ => (null, L.T("Concluir", "Done")),
        };
        SecondaryButton.Content = secondary;
        SecondaryButton.Visibility = secondary is null ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.Content = primary;
        PrimaryButton.Visibility = primary is null ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = Page < GitHub ? Visibility.Visible : Visibility.Hidden;
        base.UpdateChrome();
    }

    /// <summary>
    /// Primeira vez: inventario (HidInventory.Scan pede strings ao aparelho,
    /// por isso so aqui, uma vez) e depois a escuta. "Escutar de novo" repete
    /// so a escuta, nas colecoes da primeira varredura.
    /// </summary>
    private void StartListening()
    {
        SecondsText.Text = DeviceSubmission.ListenSeconds.ToString();
        TimeFill.Width = 0;
        LiveText.Text = L.T("Preparando…", "Preparing…");
        _listen = new CancellationTokenSource();
        var cancel = _listen.Token;
        var names = Names();

        Task.Run(() =>
        {
            if (_report is null) (_report, _collections) = DeviceSubmission.Inventory(_lastReading, names);
            Dispatcher.BeginInvoke(() => LiveText.Text = L.T("Escutando… use os aparelhos", "Listening… use your devices"));
            DeviceSubmission.Listen(_report, _collections!, (elapsed, talking) => Dispatcher.BeginInvoke(() =>
            {
                SecondsText.Text = (DeviceSubmission.ListenSeconds - elapsed).ToString();
                TimeFill.Width = TimeTrack.ActualWidth * elapsed / DeviceSubmission.ListenSeconds;
                LiveText.Text = talking switch
                {
                    0 => L.T("Nenhum aparelho mandou dados ainda", "No device has sent data yet"),
                    1 => L.T("Chegando dados de 1 aparelho", "Receiving data from 1 device"),
                    _ => L.T($"Chegando dados de {talking} aparelhos", $"Receiving data from {talking} devices"),
                };
            }), cancel);
        }).ContinueWith(task => Dispatcher.BeginInvoke(() =>
        {
            if (!IsLoaded) return; // janela fechada no meio
            if (task.Exception is { } ex)
            {
                Log.Write($"Mandar meu aparelho: falha na coleta: {ex.GetBaseException().Message}");
                LiveText.Text = L.T("Não consegui escutar. Feche e tente de novo.", "Couldn’t listen. Close and try again.");
                return;
            }
            Log.Write($"Mandar meu aparelho: coleta pronta ({_report!.Hid.Count} HID, {_report.Capture?.Groups.Count ?? 0} formatos na escuta).");
            Go(Found);
        }));
    }

    private void ShowFound()
    {
        var (read, unknown) = DeviceSubmission.Summarize(_report!);
        ReadList.Children.Clear();
        UnknownList.Children.Clear();
        foreach (var f in read) ReadList.Children.Add(FoundRow(f.Name, L.T("Esse já funciona, não precisa mandar", "This one already works, no need to send it"), f.Detail, known: true, talked: true));
        foreach (var f in unknown.Take(5))
            UnknownList.Children.Add(FoundRow($"{f.Name} · {f.Detail}",
                f.Talked ? L.T("Mandou dados na escuta. Ótimo para a gente estudar", "It sent data while listening. Great for us to study") : L.T("Ficou quieto. Se for o seu, escute de novo usando ele", "It stayed quiet. If it’s yours, listen again while using it"), null, known: false, f.Talked));
        if (unknown.Count > 5)
            UnknownList.Children.Add(new TextBlock { Style = (Style)FindResource("Caption"), Margin = new Thickness(28, 4, 0, 0), Text = L.T($"E mais {unknown.Count - 5}. Vão todos no arquivo.", $"And {unknown.Count - 5} more. They all go in the file.") });
        ReadLabel.Visibility = ReadList.Visibility = read.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UnknownLabel.Visibility = UnknownList.Visibility = unknown.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveFile()
    {
        _report!.ReportedDevices = Names();
        _file = DeviceSubmission.Save(DeviceSubmission.Render(_report), DateTime.Now);
        Log.Write($"Mandar meu aparelho: arquivo salvo ({new FileInfo(_file).Length / 1024} KB).");

        int devices = _report.Hid.Count(d => d.Transport != "virtual");
        GoesList.Children.Clear();
        foreach (string goes in new[] { L.T($"Os {devices} aparelhos que o Windows mostra e o que cada um diz sobre bateria", $"The {devices} devices Windows shows and what each says about battery"), L.T($"O que chegou deles nos {DeviceSubmission.ListenSeconds} segundos", $"What they sent in the {DeviceSubmission.ListenSeconds} seconds"), L.T("A versão do Windows e do Joule", "The Windows and Joule versions") })
            GoesList.Children.Add(Line(goes, "m4.5 10.5 3.5 3.5 7.5-8", "Accent", secondary: false));
        WhereNote.Text = HasEmail
            ? L.T("No GitHub o relato fica público, e quem tem o mesmo aparelho pode acompanhar. Por e-mail, só a gente vê.", "On GitHub the report is public, and anyone with the same device can follow it. By email, only we see it.")
            : L.T("No GitHub o relato fica público, e quem tem o mesmo aparelho pode acompanhar.", "On GitHub the report is public, and anyone with the same device can follow it.");
    }

    // ---- pecas das telas ----

    private UIElement Line(string text, string glyph, string brush, bool secondary)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, secondary ? 6 : 8) };
        var icon = new Viewbox
        {
            Width = secondary ? 14 : 16, Height = secondary ? 14 : 16, Margin = new Thickness(0, 2, secondary ? 8 : 10, 0), VerticalAlignment = VerticalAlignment.Top,
            Child = new System.Windows.Shapes.Path { Style = (Style)FindResource("Glyph"), Data = Geometry.Parse(glyph), Stroke = (Brush)FindResource(brush), StrokeThickness = 1.8 },
        };
        dock.Children.Add(icon);
        dock.Children.Add(new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap,
            FontSize = secondary ? 12 : 14, LineHeight = secondary ? 17 : 20,
            Foreground = (Brush)FindResource(secondary ? "Text.Secondary" : "Text.Primary"),
        });
        return dock;
    }

    private UIElement Step(int number, string text)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var badge = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 12, 0), Background = (Brush)FindResource("Tile.Fill") };
        badge.Child = new TextBlock { Text = number.ToString(), FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        dock.Children.Add(badge);
        dock.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        return dock;
    }

    private UIElement FoundRow(string name, string detail, string? value, bool known, bool talked)
    {
        var dock = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        UIElement mark = known
            ? new Viewbox { Width = 16, Height = 16, Child = new System.Windows.Shapes.Path { Style = (Style)FindResource("Glyph"), Data = Geometry.Parse("m4.5 10.5 3.5 3.5 7.5-8"), Stroke = (Brush)FindResource("Accent"), StrokeThickness = 1.8 } }
            : new System.Windows.Shapes.Ellipse
            {
                Width = 7, Height = 7, Margin = new Thickness(4.5, 0, 4.5, 0),
                Fill = talked ? (Brush)FindResource("Accent") : Brushes.Transparent,
                Stroke = talked ? null : (Brush)FindResource("Text.Tertiary"), StrokeThickness = 1.5,
            };
        ((FrameworkElement)mark).Margin = new Thickness(known ? 0 : 4.5, 0, 12, 0);
        ((FrameworkElement)mark).VerticalAlignment = VerticalAlignment.Center;
        dock.Children.Add(mark);
        if (value is not null)
        {
            var v = new TextBlock { Text = value, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(v, Dock.Right);
            dock.Children.Add(v);
        }
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = detail, Style = (Style)FindResource("Caption") });
        dock.Children.Add(text);
        return dock;
    }

    /// <summary>O arquivo, para arrastar ao GitHub ou ao e-mail, com "Mostrar na pasta".</summary>
    private UIElement FileChip(string hint)
    {
        var chip = new Border
        {
            Padding = new Thickness(12, 10, 8, 10), CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand,
            Background = (Brush)FindResource("Card.Fill"), BorderBrush = (Brush)FindResource("Level.Track"), BorderThickness = new Thickness(1),
        };
        var dock = new DockPanel();
        var folder = new Button { Style = (Style)FindResource("FlatButton"), Content = L.T("Mostrar na pasta", "Show in folder") };
        folder.Click += (_, _) => Open("explorer.exe", $"/select,{Quote(_file!)}");
        DockPanel.SetDock(folder, Dock.Right);
        dock.Children.Add(folder);
        dock.Children.Add(new Viewbox
        {
            Width = 20, Height = 20, Margin = new Thickness(0, 0, 12, 0),
            Child = new System.Windows.Shapes.Path { Style = (Style)FindResource("Glyph"), Data = Geometry.Parse("M5.5 2.5h6l3 3v11a1 1 0 0 1-1 1h-8a1 1 0 0 1-1-1v-13a1 1 0 0 1 1-1zM11.5 2.5v3h3M7.5 10h5M7.5 13h5"), Stroke = (Brush)FindResource("Text.Secondary"), StrokeThickness = 1.5 },
        });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = Path.GetFileName(_file), TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = hint, Style = (Style)FindResource("Caption") });
        dock.Children.Add(text);
        chip.Child = dock;
        // Arrastar o arquivo direto para a pagina do GitHub ou para o e-mail.
        chip.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null || _file is null) return;
            e.Handled = true; // sem isto a janela trata o clique como "arrastar a janela"
            DragDrop.DoDragDrop(chip, new DataObject(DataFormats.FileDrop, new[] { _file }), DragDropEffects.Copy);
        };
        return chip;
    }

    private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null and not T) d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as T;
    }

    private static void AutomationName(DependencyObject d, string name) => System.Windows.Automation.AutomationProperties.SetName(d, name);

    private static string Quote(string path) => $"\"{path}\"";

    private static void Open(string target, string? args = null)
    {
        try { Process.Start(new ProcessStartInfo(target) { Arguments = args ?? "", UseShellExecute = true }); }
        catch (Exception ex) { Log.Write($"Mandar meu aparelho: nao abriu {target.Split('?')[0]}: {ex.Message}"); }
    }

    /// <summary>O Windows tem um programa escolhido para mailto:? Sem ele, o clique so abriria "Como deseja abrir".</summary>
    private static bool HasMailApp()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\mailto\UserChoice");
            return key?.GetValue("ProgId") is string { Length: > 0 };
        }
        catch { return false; }
    }
}
