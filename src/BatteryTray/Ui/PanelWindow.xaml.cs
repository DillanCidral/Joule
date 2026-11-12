using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BatteryTray.Core;

namespace BatteryTray.Ui;

/// <summary>
/// Flyout da bandeja (design v1, secoes 01, 02 e 04). Nao tem timer: so
/// redesenha quando recebe um resultado, muda de "lendo" ou o tema muda.
/// </summary>
public partial class PanelWindow : Window
{
    private readonly Settings _settings;
    private RefreshResult? _last;
    private bool _busy;
    private bool _othersOpen;
    private DateTime _hiddenByDeactivateAt = DateTime.MinValue;

    private static readonly TimeSpan AutoCloseAfter = TimeSpan.FromSeconds(15);
    private readonly System.Windows.Threading.DispatcherTimer _outsideCheck = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private DateTime _lastInsideAt;
    private bool _wasDown;

    private bool _loadingSettings;
    private bool _closing;

    // Boas-vindas: painel fixo na tela, exemplos no lugar de uma lista vazia, destaque pulsando.
    private bool _showcase;
    private RefreshResult? _examples;
    private Spot _spot;
    private Brush? _pulse;

    public event Action? RefreshRequested;

    /// <summary>Alguma configuracao mudou e ja foi salva.</summary>
    public event Action? SettingsChanged;

    /// <summary>"Rever a apresentacao" na tela Sobre.</summary>
    public event Action? OnboardingRequested;

    /// <summary>"Mandar meu aparelho" nas Configuracoes.</summary>
    public event Action? SendDeviceRequested;

    /// <summary>"Procurar atualizacao" (ou "Reiniciar e atualizar") na tela Sobre.</summary>
    public event Action? UpdateRequested;

    /// <summary>Escolheu "No widget" ou "Do Windows": mostrar um aviso de exemplo.</summary>
    public event Action? NotifySampleRequested;

    /// <summary>"Permitir" num aparelho de marca nao testada: o VID em hex.</summary>
    public event Action<string>? AllowRequested;

    /// <summary>"Reabrir agora", depois de trocar o idioma.</summary>
    public event Action? RestartRequested;

    private void OnAllowClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string vid) AllowRequested?.Invoke(vid);
    }

    /// <summary>O que as boas-vindas destacam no painel.</summary>
    public enum Spot { None, Panel, Refresh, Others }

    /// <summary>
    /// Janela com transparencia por pixel (vidro tingido). Decidido na criacao:
    /// o WPF nao deixa trocar depois de aberta, entao mudar entre vidro e
    /// Sistema/Solido recria o painel (TrayController).
    /// </summary>
    public bool IsLayered { get; }

    public PanelWindow(Settings settings)
    {
        _settings = settings;
        IsLayered = Theme.Layered;
        InitializeComponent();

        if (IsLayered)
        {
            AllowsTransparency = true;
            Root.CornerRadius = new CornerRadius(8);
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            // Moldura de verdade (o DWM so pinta Mica em janela com moldura), escondida.
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(-1),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
            });
        }

        TitleText.Text = AppInfo.Name;
        RefreshButton.Click += (_, _) => RefreshRequested?.Invoke();
        EmptyRefresh.Click += (_, _) => RefreshRequested?.Invoke();
        SettingsButton.Click += (_, _) => ShowSettings(true);
        BackButton.Click += (_, _) => ShowSettings(false);
        WireSettings();
        OthersToggle.Click += (_, _) => { _othersOpen = !_othersOpen; Render(); };

        // Fechar (recriacao ao trocar de aparencia) tambem desativa: esconder
        // uma janela que esta fechando lanca InvalidOperationException.
        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) =>
        {
            if (_closing || !IsVisible || _showcase) return;
            _hiddenByDeactivateAt = DateTime.UtcNow;
            Dismiss();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) { ShowSettings(false); _outsideCheck.Stop(); return; }
            _lastInsideAt = DateTime.UtcNow;
            _wasDown = Native.AnyMouseButtonDown();
            _outsideCheck.Start();
        };
        _outsideCheck.Tick += (_, _) => CheckOutside();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Dismiss(); };
        SizeChanged += (_, _) => PlaceNearTray();

        SourceInitialized += (_, _) =>
        {
            Native.MakeToolWindow(this);
            if (!IsLayered) Native.KeepBackdropActive(this);
            else _blur = BlurUnderlay.Attach(this);
            ApplyTheme();
        };
        Theme.Changed += OnThemeChanged;
        Closed += (_, _) => Theme.Changed -= OnThemeChanged;
    }

    private void OnThemeChanged() => Dispatcher.BeginInvoke(() => { ApplyTheme(); Render(); });

    /// <summary>
    /// O Deactivated so fecha o painel se ele chegou a ter o foco, e o Windows
    /// nao da o foco a um app em segundo plano: aberto no inicio, depois de uma
    /// leitura ou pelo widget, o painel ficava na tela para sempre. Enquanto
    /// visivel, olha o mouse: clique que comeca fora fecha; mouse longe por
    /// AutoCloseAfter fecha. So le cursor e botoes, nenhum aparelho.
    /// </summary>
    private void CheckOutside()
    {
        var (x, y) = Native.CursorPos();
        var r = Native.WindowRect(this);
        bool inside = x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom;
        bool down = Native.AnyMouseButtonDown();
        bool pressedNow = down && !_wasDown; // arrastar um slider para fora nao conta
        _wasDown = down;

        if (inside || _showcase) { _lastInsideAt = DateTime.UtcNow; return; }
        if (pressedNow || DateTime.UtcNow - _lastInsideAt > AutoCloseAfter)
        {
            _hiddenByDeactivateAt = DateTime.UtcNow; // o mesmo clique no icone nao reabre
            Dismiss();
        }
    }

    public bool IsShowingSettings => SettingsView.Visibility == Visibility.Visible;

    public void OpenSettings() => ShowSettings(true);

    /// <summary>
    /// Modo das boas-vindas: o painel nao se esconde ao perder o foco, mostra
    /// os exemplos (se houver) no lugar da leitura e pulsa o que a tela explica.
    /// Desligar volta tudo ao normal. Nao le aparelho nenhum.
    /// </summary>
    public void SetShowcase(bool on, RefreshResult? examples = null, Spot spot = Spot.None, Brush? pulse = null)
    {
        _showcase = on;
        _examples = on ? examples : null;
        _spot = on ? spot : Spot.None;
        _pulse = on ? pulse : null;
        Root.BorderBrush = _spot == Spot.Panel ? _pulse : null;
        Root.BorderThickness = new Thickness(_spot == Spot.Panel ? 2 : 0);
        RefreshRing.BorderBrush = _pulse;
        RefreshRing.Visibility = _spot == Spot.Refresh ? Visibility.Visible : Visibility.Collapsed;
        OthersRing.BorderBrush = _pulse;
        OthersRing.Visibility = _spot == Spot.Others ? Visibility.Visible : Visibility.Collapsed;
        // O destaque que chega aparece devagar, em vez de surgir de uma vez.
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        RefreshRing.BeginAnimation(OpacityProperty, fadeIn);
        OthersRing.BeginAnimation(OpacityProperty, fadeIn);
        Render();
    }

    public void ShowResult(RefreshResult result)
    {
        _last = result;
        Render();
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        Render();
    }

    /// <summary>Repinta com o mesmo resultado (o tempo passou, ou um valor ao vivo mudou).</summary>
    public void Rerender() => Render();

    public void Toggle()
    {
        if (IsVisible && !_dismissing) { Dismiss(); return; }
        // O clique no icone tira o foco do painel antes de chegar aqui: sem
        // esta janela, o painel fecharia e reabriria no mesmo clique.
        if (DateTime.UtcNow - _hiddenByDeactivateAt < TimeSpan.FromMilliseconds(300)) return;
        ShowNearTray();
    }

    public void ShowNearTray()
    {
        _dismissing = false;
        Render();
        if (!IsVisible) Show();
        PlaceNearTray();
        Activate();

        // Sobe 16 px com a curva das janelas de telas (antes: CubicEase de 250 ms, que parava seco).
        Motion.Run(Slide, TranslateTransform.YProperty, 16, 0, 420, Motion.Out);
        Motion.Run(Body, OpacityProperty, 0, 1, 260, Motion.Out);
    }

    private bool _dismissing;

    /// <summary>Fecha descendo e esmaecendo (150 ms, mais rapido do que abre) e so entao esconde.</summary>
    public void Dismiss()
    {
        if (!IsVisible || _dismissing) return;
        if (!Motion.Enabled || _closing) { Hide(); return; }
        _dismissing = true;
        Motion.Run(Slide, TranslateTransform.YProperty, null, 10, 150, Motion.In);
        Motion.Run(Body, OpacityProperty, null, 0, 150, Motion.In, () =>
        {
            if (!_dismissing) return; // reabriu no meio
            _dismissing = false;
            Hide();
        });
    }

    /// <summary>
    /// Canto inferior direito do monitor onde esta o mouse (o da bandeja, ou o
    /// do widget), em pixels. SystemParameters.WorkArea vem na escala do login:
    /// no Avell (escala trocada depois do login, ou monitor externo) o painel
    /// abria no meio da tela.
    /// </summary>
    private void PlaceNearTray()
    {
        var (cx, cy) = Native.CursorPos();
        var work = Native.MonitorAt(cx, cy).Work;
        var dpi = VisualTreeHelper.GetDpi(this);
        double workH = work.Height / dpi.DpiScaleY;
        Scroller.MaxHeight = Math.Max(200, workH - 160);
        SettingsScroller.MaxHeight = Math.Max(200, workH - 140);
        AboutScroller.MaxHeight = SettingsScroller.MaxHeight;

        // Tamanho pelo WPF, nao pelo GetWindowRect: no SizeChanged a janela ainda tem o velho.
        int w = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        int h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        int gap = (int)Math.Round(12 * dpi.DpiScaleX);
        Native.MoveWindowTo(this, work.Right - w - gap, work.Bottom - h - gap);
    }

    private void ApplyTheme()
    {
        // Vidro: a pelicula e' o fundo, por cima do que estiver atras.
        // Sistema: o Mica aparece sozinho. Solido: a tinta opaca e' o fundo.
        Root.Background = Theme.Current == Appearance.System
            ? Brushes.Transparent
            : (Brush)FindResource("Window.Tint");
        if (!IsLayered) Native.ApplyBackdrop(this, Theme.Current, Theme.IsDark);
        _blur?.Update(null, 8 * VisualTreeHelper.GetDpi(this).DpiScaleX, Theme.BlurOpacity);
    }

    private BlurUnderlay? _blur;

    private void ShowSettings(bool on)
    {
        if (on) LoadSettings();
        bool wasAbout = AboutView.Visibility == Visibility.Visible;
        bool changed = (SettingsView.Visibility == Visibility.Visible) != on || wasAbout;
        MainView.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        SettingsView.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        AboutView.Visibility = Visibility.Collapsed;
        // A tela nova entra do lado da seta: para frente da direita, voltando da esquerda.
        if (changed && IsVisible) Motion.EnterView(on ? SettingsView : MainView, on ? 1 : -1);
    }

    private void ShowAbout(bool on)
    {
        SettingsView.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        AboutView.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (IsVisible) Motion.EnterView(on ? AboutView : SettingsView, on ? 1 : -1);
    }

    /// <summary>O texto e o estado do botao de atualizacao, ditos pelo atualizador.</summary>
    public void SetUpdateStatus(string text, bool enabled)
    {
        UpdateText.Text = text;
        UpdateButton.IsEnabled = enabled;
    }

    private static bool WouldBeEnglish(string choice) =>
        choice switch { "en" => true, "pt" => false, _ => System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "pt" };

    private void OnCreditClick(object sender, RoutedEventArgs e) => TrayController.OpenExternal((string)((FrameworkElement)sender).Tag);

    private void LoadSettings()
    {
        _loadingSettings = true;
        try
        {
            AlertsSwitch.IsChecked = _settings.AlertsEnabled;
            PlaceWidget.IsChecked = _settings.NotifyPlace == "widget";
            PlaceWindows.IsChecked = _settings.NotifyPlace == "windows";
            PlaceOff.IsChecked = _settings.NotifyPlace == "off";
            SoundSwitch.IsChecked = _settings.NotifySound;
            ListenOnlySwitch.IsChecked = _settings.ListenOnly;
            ThresholdSlider.Value = _settings.LowBatteryThreshold;
            ThresholdValue.Text = $"{_settings.LowBatteryThreshold}%";
            AutostartSwitch.IsChecked = Autostart.IsEnabled();
            LanguageAuto.IsChecked = _settings.Language is not ("pt" or "en");
            LanguagePt.IsChecked = _settings.Language == "pt";
            LanguageEn.IsChecked = _settings.Language == "en";
            LanguageRestart.Visibility = WouldBeEnglish(_settings.Language) != L.En ? Visibility.Visible : Visibility.Collapsed;

            var requested = Theme.Parse(_settings.Appearance);
            AppearanceGlass.IsChecked = requested == Appearance.Glass;
            AppearanceSystem.IsChecked = requested == Appearance.System;
            AppearanceSolid.IsChecked = requested == Appearance.Solid;
            OpacityBlock.Visibility = requested == Appearance.Glass ? Visibility.Visible : Visibility.Collapsed;
            BlurBlock.Visibility = OpacityBlock.Visibility;
            AppearanceSystem.IsEnabled = Theme.TransparencyEffects || requested == Appearance.System;
            BlurSwitch.IsChecked = _settings.GlassBlur && Theme.TransparencyEffects;
            BlurSwitch.IsEnabled = Theme.TransparencyEffects;
            BlurTitle.Opacity = Theme.TransparencyEffects ? 1 : 0.5;
            BlurCaption.Text = Theme.TransparencyEffects
                ? L.T("O que fica atrás do painel aparece borrado", "What’s behind the panel shows blurred")
                : L.T("Precisa de “Efeitos de transparência” ligado no Windows", "Needs “Transparency effects” turned on in Windows");
            ColorsButton.Visibility = Theme.TransparencyEffects ? Visibility.Collapsed : Visibility.Visible;
            OpacitySlider.Value = _settings.GlassOpacity;
            OpacityValue.Text = $"{_settings.GlassOpacity}%";

            AppearanceNote.Text = requested != Theme.Current
                ? L.T("“Efeitos de transparência” estão desligados no Windows, e o Sistema depende deles: usando Sólido.", "“Transparency effects” are off in Windows, and System depends on them: using Solid.")
                : requested != Appearance.Glass
                    ? L.T("O vidro tingido funciona mesmo com “Efeitos de transparência” desligados; o Sistema precisa deles.", "Tinted glass works even with “Transparency effects” off; System needs them.")
                    : Theme.Blur
                        ? L.T("Com desfoque dá para baixar bem a película sem o texto de trás atrapalhar.", "With blur you can lower the tint a lot without the text behind getting in the way.")
                        : L.T("Sem desfoque o vidro só escurece o fundo. Se atrapalhar a leitura, suba a película.", "Without blur the glass only darkens the background. If it gets hard to read, raise the tint.");

            WidgetSwitch.IsChecked = _settings.WidgetEnabled;
            SideLeft.IsChecked = _settings.WidgetSide == "left";
            SideRight.IsChecked = _settings.WidgetSide != "left";
            ShowAlways.IsChecked = !_settings.WidgetAutoHide;
            ShowAutoHide.IsChecked = _settings.WidgetAutoHide;

            // A alca so existe no "Esconder sozinho".
            string handle = _settings.WidgetHandleColor;
            bool custom = handle.StartsWith('#');
            HandleColorBlock.IsEnabled = _settings.WidgetAutoHide;
            HandleColorBlock.Opacity = _settings.WidgetAutoHide ? 1 : 0.4;
            HandleJoule.IsChecked = handle == "joule";
            HandleWindows.IsChecked = handle == "windows";
            HandleWhite.IsChecked = handle == "white";
            HandleNeutral.IsChecked = handle == "neutral";
            HandleCustom.IsChecked = custom;
            HandlePalette.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
            foreach (System.Windows.Controls.RadioButton b in HandlePalette.Children)
                b.IsChecked = Hex(b).Equals(handle, StringComparison.OrdinalIgnoreCase);
            HandleColorName.Text = handle switch
            {
                "windows" => L.T("Tema do Windows", "Windows theme"),
                "white" => L.T("Branco", "White"),
                "neutral" => L.T("Neutro", "Neutral"),
                _ when custom => L.T("Cor própria", "Custom color"),
                _ => L.T("Joule (padrão)", "Joule (default)"),
            };

            AboutText.Text = $"{AppInfo.Name} {AppInfo.Version}";
        }
        finally { _loadingSettings = false; }
    }

    private void WireSettings()
    {
        void Changed(Action apply)
        {
            if (_loadingSettings) return;
            apply();
            _settings.Save();
            SettingsChanged?.Invoke();
            LoadSettings();
        }

        AlertsSwitch.Click += (_, _) => Changed(() => _settings.AlertsEnabled = AlertsSwitch.IsChecked == true);
        void SetPlace(string place)
        {
            if (_loadingSettings) return;
            Changed(() => _settings.NotifyPlace = place);
            if (place != "off") NotifySampleRequested?.Invoke();
        }
        PlaceWidget.Checked += (_, _) => SetPlace("widget");
        PlaceWindows.Checked += (_, _) => SetPlace("windows");
        PlaceOff.Checked += (_, _) => SetPlace("off");
        SoundSwitch.Click += (_, _) => Changed(() => _settings.NotifySound = SoundSwitch.IsChecked == true);
        ListenOnlySwitch.Click += (_, _) => Changed(() => _settings.ListenOnly = ListenOnlySwitch.IsChecked == true); // vale na proxima leitura
        BlurSwitch.Click += (_, _) => Changed(() =>
        {
            _settings.GlassBlur = BlurSwitch.IsChecked == true;
            if (_settings.GlassBlur) _settings.GlassOpacity = Math.Min(_settings.GlassOpacity, 30); // pelicula alta esconde o borrado
            Theme.Apply(Theme.Parse(_settings.Appearance), _settings.GlassOpacity, _settings.GlassBlur);
        });
        ColorsButton.Click += (_, _) => TrayController.OpenExternal("ms-settings:colors");
        ThresholdSlider.ValueChanged += (_, e) => Changed(() => _settings.LowBatteryThreshold = (int)Math.Round(e.NewValue));
        AutostartSwitch.Click += (_, _) => Changed(() => Autostart.Set(AutostartSwitch.IsChecked == true));
        LanguageAuto.Checked += (_, _) => Changed(() => _settings.Language = "auto");
        LanguagePt.Checked += (_, _) => Changed(() => _settings.Language = "pt");
        LanguageEn.Checked += (_, _) => Changed(() => _settings.Language = "en");
        RestartButton.Click += (_, _) => RestartRequested?.Invoke();

        void SetAppearance(Appearance a) => Changed(() =>
        {
            _settings.Appearance = Theme.ToSetting(a);
            Theme.Apply(a, _settings.GlassOpacity, _settings.GlassBlur);
        });
        // ponytail: salva a cada passo do arrasto; settings.json e' pequeno.
        OpacitySlider.ValueChanged += (_, e) => Changed(() =>
        {
            _settings.GlassOpacity = (int)Math.Round(e.NewValue);
            Theme.Apply(Theme.Parse(_settings.Appearance), _settings.GlassOpacity, _settings.GlassBlur);
        });
        AppearanceGlass.Checked += (_, _) => SetAppearance(Appearance.Glass);
        AppearanceSystem.Checked += (_, _) => SetAppearance(Appearance.System);
        AppearanceSolid.Checked += (_, _) => SetAppearance(Appearance.Solid);

        WidgetSwitch.Click += (_, _) => Changed(() => _settings.WidgetEnabled = WidgetSwitch.IsChecked == true);
        SideLeft.Checked += (_, _) => Changed(() => _settings.WidgetSide = "left");
        SideRight.Checked += (_, _) => Changed(() => _settings.WidgetSide = "right");
        ShowAlways.Checked += (_, _) => Changed(() => _settings.WidgetAutoHide = false);
        ShowAutoHide.Checked += (_, _) => Changed(() => _settings.WidgetAutoHide = true);
        HandleJoule.Checked += (_, _) => Changed(() => _settings.WidgetHandleColor = "joule");
        HandleWindows.Checked += (_, _) => Changed(() => _settings.WidgetHandleColor = "windows");
        HandleWhite.Checked += (_, _) => Changed(() => _settings.WidgetHandleColor = "white");
        HandleNeutral.Checked += (_, _) => Changed(() => _settings.WidgetHandleColor = "neutral");
        HandleCustom.Checked += (_, _) => Changed(() => _settings.WidgetHandleColor = "#2DD4BF");
        foreach (System.Windows.Controls.RadioButton b in HandlePalette.Children)
            b.Checked += (_, _) => Changed(() => _settings.WidgetHandleColor = Hex(b));

        DataFolderButton.Click += (_, _) => TrayController.OpenDataFolder();
        OnboardingButton.Click += (_, _) => OnboardingRequested?.Invoke();
        SendDeviceButton.Click += (_, _) => SendDeviceRequested?.Invoke();

        AboutButton.Click += (_, _) => ShowAbout(true);
        AboutBackButton.Click += (_, _) => ShowAbout(false);
        AboutVersion.Text = $"{AppInfo.Name} {AppInfo.Version}";
        SourceRow.Content = new Credit(L.T("Ver o código no GitHub", "View the code on GitHub"), "", "", "https://" + AppInfo.Repository);
        ComponentsList.ItemsSource = Credit.Components;
        TablesList.ItemsSource = Credit.Tables;
        ProtocolsList.ItemsSource = Credit.Protocols;
        UpdateButton.Click += (_, _) => UpdateRequested?.Invoke();
        LicensesButton.Click += (_, _) => TrayController.OpenExternal(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt"));
    }

    private static string Hex(System.Windows.Controls.RadioButton b) => "#" + ((SolidColorBrush)b.Background).Color.ToString()[3..]; // #AARRGGBB -> #RRGGBB

    private void Render()
    {
        var shown = _examples ?? _last;
        var devices = shown?.Devices ?? Array.Empty<DeviceEntry>();
        int threshold = _settings.LowBatteryThreshold;
        var main = PanelView.MainRows(devices, threshold, DateTime.UtcNow);
        var consent = PanelView.NeedConsent(devices, BatteryTray.Interop.HidRequest.WaitingVid);
        var others = PanelView.Others(devices, BatteryTray.Interop.HidRequest.WaitingVid);

        Rows.ItemsSource = main.Select(r => new RowItem(r, _busy && _examples is null, _spot == Spot.Others && r.IsStale ? _pulse : null)).ToList();
        ConsentRows.ItemsSource = consent.Select(c => new ConsentItem(c)).ToList();
        OtherRows.ItemsSource = others.Select(o => new OtherItem(o)).ToList();

        // Cabecalho
        var low = main.Where(r => r.IsLow).ToList();
        string? subtitle = _busy ? L.T("Atualizando os aparelhos sob demanda", "Refreshing devices on demand")
            : low.Count == 1 ? L.T($"{low[0].Name} abaixo de {threshold}%", $"{low[0].Name} below {threshold}%")
            : low.Count > 1 ? L.T($"{low.Count} aparelhos abaixo de {threshold}%", $"{low.Count} devices below {threshold}%")
            : null;
        SubtitleText.Text = subtitle ?? "";
        SubtitleText.Visibility = subtitle is null ? Visibility.Collapsed : Visibility.Visible;
        SubtitleText.Foreground = (Brush)FindResource(!_busy && low.Count > 0 ? "Level.Alert" : "Text.Tertiary");

        ReadingPill.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
        RefreshButton.Visibility = _busy ? Visibility.Collapsed : Visibility.Visible;
        if (_busy)
            Spin.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(130, 490, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
        else
            Spin.BeginAnimation(RotateTransform.AngleProperty, null);

        // Vazio: com "outros", e' "nenhum com bateria" e a lista ja abre.
        bool empty = main.Count == 0 && consent.Count == 0 && shown is not null;
        EmptyView.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (empty)
        {
            bool hasOthers = others.Count > 0;
            EmptyIcon.Data = hasOthers ? Icons.Generic : Icons.Plug;
            EmptyTitle.Text = hasOthers ? L.T("Nenhum aparelho com bateria", "No device with battery") : L.T("Nenhum aparelho conectado", "No device connected");
            EmptyBody.Text = hasOthers
                ? L.T("Há aparelhos plugados, mas nenhum informou bateria. Veja o motivo de cada um abaixo.", "Devices are plugged in, but none reported a battery. See each one’s reason below.")
                : L.T("Ligue o dongle ou conecte por Bluetooth. Valores guardados voltam quando o aparelho reconectar.", "Plug in the dongle or connect over Bluetooth. Saved values come back when the device reconnects.");
            EmptyRefresh.Visibility = hasOthers ? Visibility.Collapsed : Visibility.Visible;
        }

        bool othersOpen = _othersOpen || (empty && others.Count > 0);
        OthersBlock.Visibility = others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        OthersLabel.Text = $"{(othersOpen ? L.T("Ocultar outros", "Hide others") : L.T("Mostrar outros", "Show others"))} ({others.Count})";
        ChevronTurn.Angle = othersOpen ? 180 : 0;
        OtherRows.Visibility = othersOpen ? Visibility.Visible : Visibility.Collapsed;

        // Rodape
        FooterText.Text = _busy ? L.T("Lendo… leva até 3 s", "Reading… takes up to 3 s")
            : _last?.FatalError is { Length: > 0 } err ? L.T($"Erro na leitura: {err}", $"Reading error: {err}")
            : _last is null ? L.T("Ainda sem leitura", "No reading yet")
            : L.T($"Última leitura às {_last.WhenUtc.ToLocalTime():HH:mm}", $"Last reading at {_last.WhenUtc.ToLocalTime():HH:mm}");
        LiveLegend.Visibility = main.Any(r => r.IsLive) ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>Linha da lista principal com as cores ja resolvidas para o tema atual.</summary>
public sealed class RowItem
{
    /// <param name="highlight">Contorno pulsando das boas-vindas; nulo = o normal do tema.</param>
    public RowItem(DeviceRowView v, bool busy, Brush? highlight = null)
    {
        CardStroke = highlight ?? R("Card.Stroke");
        bool reading = busy && !v.IsLive; // "lendo" so nas linhas sob demanda
        bool stale = v.IsStale && !reading;

        Name = v.Name;
        Origin = Known.OriginLabel(v.Origin);
        Icon = Icons.For(v.Kind);
        Tooltip = v.Tooltip;

        ShowLive = v.IsLive;
        IsReading = reading;
        IsStale = stale;
        ChargeText = v.ChargeText;
        ShowCharge = v.ChargeText is not null && !reading;
        Age = v.Age;
        ShowAge = v.Age is not null && !reading;
        AgeBrush = R(stale ? "Text.Secondary" : "Text.Tertiary");
        Note = v.Note;
        ShowNote = v.Note is not null && !reading;
        NoteBrush = R(v.IsLow ? "Level.Alert" : "Text.Tertiary");

        IsPercent = v.ValueKind == ValueKind.Percent;
        IsBand = v.ValueKind == ValueKind.Band;
        IsCable = v.ValueKind == ValueKind.Cable;
        Percent = v.Percent;
        BandLabel = v.BandLabel;
        FillWidth = new GridLength(v.Percent, GridUnitType.Star);
        RestWidth = new GridLength(100 - v.Percent, GridUnitType.Star);

        BarBrush = R(v.IsLow ? "Ember.Alert" : stale ? "Level.Stale" : "Ember");
        BarOpacity = reading ? 0.45 : 1;
        bool lit = !v.IsLow && !stale && !reading;
        GlowOpacity = lit ? (ShowCharge ? 0.8 : 0.5) : 0;
        ShowShimmer = lit && ShowCharge && SystemParameters.ClientAreaAnimation;
        ValueBrush = R(v.IsLow ? "Level.Alert" : "Text.Primary");
        ValueOpacity = stale || reading ? 0.5 : 1;

        Brush filled = R(reading ? "Level.Stale" : v.IsLow ? "Level.Alert" : "Accent.Text");
        Brush track = R("Level.Track");
        Seg0 = v.BandCount > 0 ? filled : track;
        Seg1 = v.BandCount > 1 ? filled : track;
        Seg2 = v.BandCount > 2 ? filled : track;
    }

    public string Name { get; }
    public string Origin { get; }
    public Geometry Icon { get; }
    public string Tooltip { get; }
    public bool ShowLive { get; }
    public bool IsReading { get; }
    public bool IsStale { get; }
    public string? ChargeText { get; }
    public bool ShowCharge { get; }
    public string? Age { get; }
    public bool ShowAge { get; }
    public Brush AgeBrush { get; }
    public Brush CardStroke { get; }
    public string? Note { get; }
    public bool ShowNote { get; }
    public Brush NoteBrush { get; }
    public bool IsPercent { get; }
    public bool IsBand { get; }
    public bool IsCable { get; }
    public int Percent { get; }
    public string BandLabel { get; }
    public GridLength FillWidth { get; }
    public GridLength RestWidth { get; }
    public Brush BarBrush { get; }
    public double GlowOpacity { get; }
    public bool ShowShimmer { get; }
    public double BarOpacity { get; }
    public Brush ValueBrush { get; }
    public double ValueOpacity { get; }
    public Brush Seg0 { get; }
    public Brush Seg1 { get; }
    public Brush Seg2 { get; }

    internal static Brush R(string key) => (Brush)System.Windows.Application.Current.Resources[key];
}

public sealed class ConsentItem(ConsentRowView v)
{
    public string Name { get; } = v.Name;
    public string Vid { get; } = v.Vid;
    public Geometry Icon { get; } = Icons.For(v.Kind);
}

public sealed class OtherItem
{
    public OtherItem(OtherRowView v)
    {
        Name = v.Name;
        Reason = v.Reason;
        Icon = Icons.For(v.Kind);
    }

    public string Name { get; }
    public string Reason { get; }
    public Geometry Icon { get; }
}

/// <summary>Um projeto de terceiros citado na tela Sobre (o texto legal fica em THIRD-PARTY-NOTICES).</summary>
public sealed record Credit(string Name, string Use, string License, string Url)
{
    private static string Ver(Type t) =>
        (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(t.Assembly)?.InformationalVersion ?? "").Split('+')[0];

    public static readonly Credit[] Components =
    {
        new($".NET {Environment.Version}", L.T("Base do app, vai instalada junto", "The app’s foundation, installed with it"), "MIT", "https://github.com/dotnet/runtime"),
        new($"Velopack {Ver(typeof(Velopack.UpdateManager))}", L.T("Instalador e atualizações", "Installer and updates"), "MIT", "https://github.com/velopack/velopack"),
        new($"Geist {AppInfo.GeistVersion}", L.T("Fonte do nome Joule", "Font for the Joule name"), "SIL OFL 1.1", "https://github.com/vercel/geist-font"),
    };

    public static readonly Credit[] Tables =
    {
        new("razer-battery-report", L.T("Mouses Razer", "Razer mice"), "MIT", "https://github.com/xzeldon/razer-battery-report"),
        new("OpenRazer", L.T("Teclados Razer", "Razer keyboards"), "GPL-2.0+", "https://github.com/openrazer/openrazer"),
        new("OpenMouse", L.T("Attack Shark, ATK, Keychron e outras marcas", "Attack Shark, ATK, Keychron and other brands"), "AGPL-3.0", "https://github.com/OpenMouse-Project/mouse-protocol"),
    };

    public static readonly Credit[] Protocols =
    {
        new("Solaar", L.T("Receptores Logitech", "Logitech receivers"), "", "https://github.com/pwr-Solaar/Solaar"),
        new("HeadsetControl", L.T("Fones Logitech, SteelSeries, HyperX e Corsair", "Logitech, SteelSeries, HyperX and Corsair headsets"), "", "https://github.com/Sapd/HeadsetControl"),
        new("OpenLinkHub", L.T("Receptor Corsair Slipstream", "Corsair Slipstream receiver"), "", "https://github.com/jurkovic-nikola/OpenLinkHub"),
        new("rivalcfg", L.T("Mouses SteelSeries", "SteelSeries mice"), "", "https://github.com/flozz/rivalcfg"),
        new("Pulsefire Dart RE", "HyperX Pulsefire Dart", "", "https://github.com/santeri3700/hyperx_pulsefire_dart_reverse_engineering"),
        new("RazerNariBatteryLevel", L.T("Fone Razer Nari", "Razer Nari headset"), "", "https://github.com/Modzeleczek/RazerNariBatteryLevel"),
    };
}
