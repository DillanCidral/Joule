using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BatteryTray.Core;

namespace BatteryTray.Ui;

/// <summary>
/// Widget de borda (design v1, secao 03). Janela propria, grudada na borda,
/// que nunca rouba o foco. Recolhido: um anel por aparelho. Mouse em cima:
/// expande -- e SO expande; passar o mouse nao dispara leitura (hover
/// repetido viraria leitura em loop). Reler os aparelhos sob demanda e' o
/// botao de atualizar do expandido, ou abrir o painel.
/// </summary>
public partial class WidgetWindow : Window
{
    private readonly Settings _settings;
    private RefreshResult? _last;
    private List<RingItem> _items = new();

    private bool _expanded;
    private bool _revealed;      // esconder sozinho: alca virou widget enquanto o mouse esta perto
    private bool _fullscreen;    // um app em tela cheia esta na frente
    private bool _showcase;      // boas-vindas: aberto, fixo e com destaque, mesmo desligado
    private RefreshResult? _examples;

    /// <summary>Passar o mouse mostra os aneis com o valor; so parar em cima expande (passar rapido abria tudo).</summary>
    private readonly DispatcherTimer _expandDelay = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private readonly DispatcherTimer _collapseDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };

    /// <summary>Nao le dispositivo nenhum: so pergunta ao Windows qual janela esta na frente.</summary>
    private readonly DispatcherTimer _fullscreenCheck = new() { Interval = TimeSpan.FromSeconds(2) };

    // Aviso de conexao (pagina aprovada em 2026-11-10): um por vez, na fila.
    private Notice? _notice;
    private readonly Queue<Notice> _queue = new();
    private bool _noticeOnly; // widget estava escondido: sai da borda so o anel deste aparelho
    private readonly DispatcherTimer _noticeHold = new();
    private string? _chargeKey, _pulseKey;
    /// <summary>Ultimo arco mostrado por aparelho: o valor novo corre a partir dele.</summary>
    private readonly Dictionary<string, double> _fractions = new(StringComparer.OrdinalIgnoreCase);

    private readonly ScaleTransform _cardScale = new(1, 1);
    private readonly TranslateTransform _cardMove = new();
    private int _gen; // cada transicao nova invalida o "terminou" das anteriores
    private Size _collapsedSize;

    /// <summary>Pode mostrar aviso: ligado e nenhum app em tela cheia na frente.</summary>
    public bool CanNotify => _settings.WidgetEnabled && !_fullscreen;

    private (int X, int Y)? _press;
    private Native.PixelRect _pressRect;
    private bool _dragging;

    public event Action? OpenPanelRequested;
    public event Action? RefreshRequested;

    /// <summary>Monitor, lado e altura novos escolhidos arrastando; quem assina salva.</summary>
    public event Action? PositionChanged;

    /// <summary>"Permitir" no aviso de licenca: o VID em hex.</summary>
    public event Action<string>? AllowRequested;

    private void CloseNotice()
    {
        _noticeHold.Stop();
        EndNotice();
    }

    /// <summary>So o aviso de licenca tem botoes e texto em varias linhas.</summary>
    private void ShowActions(Notice n)
    {
        bool consent = n.What == NoticeKind.Consent;
        NoticeActions.Visibility = consent ? Visibility.Visible : Visibility.Collapsed;
        NoticeState.TextWrapping = consent ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    public WidgetWindow(Settings settings)
    {
        _settings = settings;
        InitializeComponent();
        TitleText.Text = AppInfo.Name;
        Card.RenderTransform = new TransformGroup { Children = { _cardScale, _cardMove } };
        LayoutUpdated += (_, _) => UpdateBlur();
        _noticeHold.Tick += (_, _) =>
        {
            if (IsMouseOver) { _noticeHold.Interval = TimeSpan.FromSeconds(1); return; } // mouse em cima segura o aviso
            _noticeHold.Stop();
            EndNotice();
        };
        UpdateView(); // lado, cantos e alca valem desde o primeiro quadro
        NoticeAllow.Click += (_, _) => { if (_notice?.Vid is { } vid) AllowRequested?.Invoke(vid); CloseNotice(); };
        NoticeLater.Click += (_, _) => CloseNotice(); // sem memoria: o aviso volta na proxima conexao (corte do ponytail, 2026-11-12)

        SourceInitialized += (_, _) =>
        {
            Native.MakeToolWindow(this, noActivate: true);
            Place();
        };
        SizeChanged += (_, _) => Place();
        // Escala diferente muda o tamanho em pixels sem mudar o em DIPs: o
        // SizeChanged nao dispara. Monitor plugado/desplugado/rearranjado idem.
        DpiChanged += (_, _) => Place();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Theme.Changed += OnThemeChanged;
        Closed += (_, _) =>
        {
            Theme.Changed -= OnThemeChanged;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _fullscreenCheck.Stop();
        };

        MouseEnter += (_, _) =>
        {
            _collapseDelay.Stop();
            if (_settings.WidgetAutoHide && !_revealed) { _revealed = true; UpdateView(); SlideCardIn(); }
            else if (_cardMove.X != 0 || Card.Opacity < 1) SlideCardIn(); // voltou no meio do esconder
            if (_notice is null) _expandDelay.Start();
        };
        MouseLeave += (_, _) =>
        {
            _expandDelay.Stop();
            if (!_dragging) _collapseDelay.Start();
        };
        _expandDelay.Tick += (_, _) =>
        {
            _expandDelay.Stop();
            if (_dragging || _notice is not null || _expanded) return;
            _collapsedSize = new Size(Card.ActualWidth, Card.ActualHeight);
            _expanded = true;
            UpdateView();
            AnimateExpand();
        };
        _collapseDelay.Tick += (_, _) =>
        {
            _collapseDelay.Stop();
            if (_dragging || _showcase || _notice is not null) return;
            void Hide()
            {
                if (_settings.WidgetAutoHide && _revealed) SlideCardOut(() => { _revealed = false; UpdateView(); });
            }
            if (_expanded) AnimateCollapse(() => { _expanded = false; UpdateView(); Hide(); });
            else Hide();
        };

        OpenPanelButton.Click += (_, _) => OpenPanelRequested?.Invoke();
        RefreshButton.Click += (_, _) => RefreshRequested?.Invoke();

        // Arrastar o widget inteiro, para qualquer tela. Botoes tratam o
        // proprio clique e nao chegam aqui; clique sem arrasto abre o painel.
        MouseLeftButtonDown += (_, _) =>
        {
            _press = Native.CursorPos();
            _pressRect = Native.WindowRect(this);
            _expandDelay.Stop();
            CaptureMouse();
            Squeeze(0.96, 90);
        };
        MouseMove += (_, e) =>
        {
            if (_press is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var (x, y) = Native.CursorPos();
            int dx = x - start.X, dy = y - start.Y;
            if (!_dragging && Math.Abs(dx) < 5 && Math.Abs(dy) < 5) return;
            _dragging = true;
            Native.MoveWindowTo(this, _pressRect.Left + dx, _pressRect.Top + dy);
        };
        MouseLeftButtonUp += (_, _) =>
        {
            ReleaseMouseCapture();
            Squeeze(1, 220);
            bool dragged = _dragging;
            _press = null;
            _dragging = false;
            if (dragged) Snap();
            else OpenPanelRequested?.Invoke();
            if (!IsMouseOver) _collapseDelay.Start();
        };
        LostMouseCapture += (_, _) =>
        {
            if (!_dragging) return;
            _press = null;
            _dragging = false;
            Snap();
        };

        _fullscreenCheck.Tick += (_, _) =>
        {
            bool fs = Native.ForegroundIsFullscreen(this);
            if (fs == _fullscreen) return;
            _fullscreen = fs;
            ShowIfAllowed();
        };
    }

    private void OnThemeChanged() => Dispatcher.BeginInvoke(Render);
    private void OnDisplaySettingsChanged(object? s, EventArgs e) => Dispatcher.BeginInvoke(Place);

    public void ShowResult(RefreshResult result)
    {
        _last = result;
        Render();
    }

    /// <summary>
    /// Boas-vindas: expandido e fixo, com os exemplos (se houver) e contorno
    /// pulsando; aparece mesmo com o widget desligado. Desligar volta ao normal.
    /// </summary>
    public void SetShowcase(bool on, RefreshResult? examples = null, Brush? pulse = null)
    {
        _showcase = on;
        _examples = on ? examples : null;
        _expanded = on;
        _revealed = false;
        Card.BorderBrush = on ? pulse : null;
        Card.BorderThickness = new Thickness(on && pulse is not null ? 2 : 0);
        UpdateView();
        Render();
        if (on)
            Card.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            });
    }

    /// <summary>So para o --demo: abre expandido e fica, para conferir o layout sem mouse.</summary>
    public void PinExpanded()
    {
        _expanded = true;
        _collapseDelay.Interval = TimeSpan.FromDays(1);
        UpdateView();
    }

    /// <summary>Liga/desliga, lado, esconder sozinho: reaplica tudo.</summary>
    public void ApplySettings()
    {
        _revealed = false;
        UpdateView();
        Render();
    }

    private void Render()
    {
        var devices = (_examples ?? _last)?.Devices ?? Array.Empty<DeviceEntry>();
        var rows = PanelView.MainRows(devices, _settings.LowBatteryThreshold, DateTime.UtcNow);
        _items = rows.Select(r => new RingItem(r)).ToList();

        // Aviso: o anel do aparelho entra na coluna (provisorio enquanto le ou
        // depois que saiu); com o widget escondido, sai da borda so ele.
        RingItem? mine = null;
        if (_notice is { } n)
        {
            mine = _items.FirstOrDefault(i => Same(i.Key, n.Key));
            if (mine is null && n.What != NoticeKind.Low)
            {
                mine = new RingItem(n, _fractions.GetValueOrDefault(n.Key));
                _items.Add(mine);
            }
            if (mine is not null)
            {
                if (n.What == NoticeKind.Reading) mine.IsReading = true;
                mine.Pulse = Same(_pulseKey, n.Key);
                if (_noticeOnly) _items = new List<RingItem> { mine };
            }
            _pulseKey = null; // pulsa uma vez so
        }
        foreach (var item in _items)
        {
            if (item.From is null && item.IsPercent)
                item.From = _fractions.TryGetValue(item.Key, out double f) ? f : Same(item.Key, _chargeKey) ? 0 : null;
            if (item.IsPercent && !item.IsReading) _fractions[item.Key] = item.Fraction;
        }
        Rings.ItemsSource = _items;
        Rows.ItemsSource = _items;

        // O widget fica sobre o conteudo o tempo todo (o painel so quando aberto):
        // pelicula de no minimo 80% para o texto de tras nao brigar com o dele.
        var tint = ((SolidColorBrush)FindResource("Window.Tint")).Color;
        // Com desfoque o fundo ja vem borrado: vale a opacidade escolhida, com piso de 40%.
        byte floor = Theme.Blur ? (byte)102 : (byte)204;
        Card.Background = new SolidColorBrush(Color.FromArgb(Math.Max(tint.A, floor), tint.R, tint.G, tint.B));
        NoticeTab.Background = Card.Background;
        if (_notice is not null) Dispatcher.BeginInvoke(AlignTab, DispatcherPriority.Loaded);

        ReadAge.Text = _last is null ? "" : L.T($"lido {PanelView.Age(DateTime.UtcNow - _last.WhenUtc)}", $"read {PanelView.Age(DateTime.UtcNow - _last.WhenUtc)}");
        HandleAlert.Visibility = rows.Any(r => r.IsLow) ? Visibility.Visible : Visibility.Collapsed;

        ShowIfAllowed();
        Place();
    }

    private void ShowIfAllowed()
    {
        bool show = (_settings.WidgetEnabled || _showcase) && _items.Count > 0 && !_fullscreen;
        if (_settings.WidgetEnabled) _fullscreenCheck.Start(); else _fullscreenCheck.Stop();

        if (show && !IsVisible) Show();
        else if (!show && IsVisible) Hide();
    }

    private void UpdateView()
    {
        bool right = _settings.WidgetSide != "left";
        bool handle = _settings.WidgetAutoHide && !_revealed && !_expanded;

        HandleView.Visibility = handle ? Visibility.Visible : Visibility.Collapsed;
        Strip.Visibility = handle ? Visibility.Collapsed : Visibility.Visible;
        Rings.Visibility = _expanded ? Visibility.Collapsed : Visibility.Visible;
        Expanded.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;

        // O Windows impoe largura minima de janela (~136 px) maior que o widget
        // recolhido (48): o conteudo encosta no lado da borda, o resto da
        // janela e' transparente e deixa o clique passar.
        RootGrid.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        // Cantos de 8 px so do lado de dentro; o lado da borda fica reto.
        Card.CornerRadius = right ? new CornerRadius(8, 0, 0, 8) : new CornerRadius(0, 8, 8, 0);
        Card.RenderTransformOrigin = new Point(right ? 1 : 0, 0.5);
        // A lingueta fica do lado de dentro do widget e sai de tras dele.
        System.Windows.Controls.Grid.SetColumn(NoticeClip, right ? 0 : 1);
        System.Windows.Controls.Grid.SetColumn(Card, right ? 1 : 0);
        NoticeTab.CornerRadius = right ? new CornerRadius(8, 0, 0, 8) : new CornerRadius(0, 8, 8, 0);
        NoticeClip.Padding = right ? new Thickness(10, 8, 0, 8) : new Thickness(0, 8, 10, 8);
        HandleBar.CornerRadius = right ? new CornerRadius(3, 0, 0, 3) : new CornerRadius(0, 3, 3, 0);
        HandleBar.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        HandleAlert.HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        HandleAlert.Margin = right ? new Thickness(0, 0, 7, 0) : new Thickness(7, 0, 0, 0);

        string color = _settings.WidgetHandleColor;
        HandleBar.BorderThickness = new Thickness(color == "white" ? 1 : 0); // branco some em fundo claro sem contorno
        if (color is "joule" or "windows")
            HandleBar.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, color == "joule" ? "Accent" : "Accent.Windows");
        else
            HandleBar.Background = (Brush)new BrushConverter().ConvertFromString(color switch { "white" => "#FFFFFF", "neutral" => "#8A8A8A", _ => color })!;

        Place();
    }

    private string _blurShape = "";
    private BlurUnderlay? _blur;

    /// <summary>O desfoque so atras do cartao e da lingueta (o resto da janela e' transparente e deixa o clique passar).</summary>
    private void UpdateBlur()
    {
        var shapes = new List<Int32Rect>();
        if (Theme.Blur && Strip.Visibility == Visibility.Visible && Card.ActualWidth > 0)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            Int32Rect Px(FrameworkElement el, double grow)
            {
                // Posicao de layout (sem as animacoes de deslizar): a regiao vale para o lugar final.
                var off = new Vector();
                for (DependencyObject v = el; v is Visual vis && !ReferenceEquals(v, Strip); v = VisualTreeHelper.GetParent(v)) off += VisualTreeHelper.GetOffset(vis);
                var p = Strip.TranslatePoint(new Point(off.X, off.Y), this);
                // O lado da borda passa do limite da janela: canto reto ali, redondo so do lado de dentro.
                double x = p.X - (Edge < 0 ? grow : 0), w = el.ActualWidth + grow;
                return new Int32Rect((int)Math.Round(x * dpi.DpiScaleX), (int)Math.Round(p.Y * dpi.DpiScaleY),
                    (int)Math.Round(w * dpi.DpiScaleX), (int)Math.Round(el.ActualHeight * dpi.DpiScaleY));
            }
            shapes.Add(Px(Card, 10));
            if (NoticeClip.Visibility == Visibility.Visible && NoticeTab.ActualWidth > 0) shapes.Add(Px(NoticeTab, 0));
        }
        string key = string.Join(";", shapes) + Theme.BlurOpacity;
        if (key == _blurShape) return;
        _blurShape = key;
        _blur ??= BlurUnderlay.Attach(this);
        _blur?.Update(shapes, 8 * VisualTreeHelper.GetDpi(this).DpiScaleX, Theme.BlurOpacity);
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private double Edge => _settings.WidgetSide != "left" ? 1 : -1; // para onde fica a borda

    // ---------------- aviso ----------------

    /// <summary>Aviso de conectar/desconectar/bateria baixa. O mesmo aparelho atualiza o aviso aberto; outro entra na fila.</summary>
    public void Notify(Notice n)
    {
        if (_notice is { } cur && Same(cur.Key, n.Key)) { UpdateNotice(n); return; }
        if (_notice is not null)
        {
            // Na fila, o resultado substitui o "lendo" do mesmo aparelho.
            var waiting = _queue.ToList();
            _queue.Clear();
            foreach (var q in waiting.Where(q => !Same(q.Key, n.Key))) _queue.Enqueue(q);
            _queue.Enqueue(n);
            return;
        }
        StartNotice(n);
    }

    private void StartNotice(Notice n)
    {
        _gen++;
        _notice = n;
        _expandDelay.Stop();
        _collapseDelay.Stop();
        if (_expanded) { _expanded = false; Card.Clip = null; }
        bool fromHandle = _settings.WidgetAutoHide && !_revealed;
        _noticeOnly = fromHandle;
        if (fromHandle) _revealed = true;
        if (n.What is NoticeKind.Reading or NoticeKind.Connected) { _chargeKey = n.Key; _fractions.Remove(n.Key); } // conectou: o anel enche do zero
        if (n.Low && n.What != NoticeKind.Reading) _pulseKey = n.Key;
        Render();
        UpdateView();
        if (fromHandle) SlideCardIn();

        NoticeName.Text = n.Name;
        NoticeState.Text = n.State;
        NoticeStateOld.Text = "";
        ShowActions(n);
        NoticeClip.Visibility = Visibility.Visible;
        NoticeMove.X = 400 * Edge; // fora do recorte ate medir
        Dispatcher.BeginInvoke(() =>
        {
            AlignTab();
            double from = (NoticeTab.ActualWidth + 12) * Edge;
            Motion.Run(NoticeMove, TranslateTransform.XProperty, from, 0, 460, Motion.Out, delayMs: fromHandle ? 160 : 60);
        }, DispatcherPriority.Loaded);
        Chime(n);
        ArmHold(n);
    }

    private void UpdateNotice(Notice n)
    {
        _notice = n;
        if (n.Low && n.What != NoticeKind.Reading) _pulseKey = n.Key;
        Render();
        // A linha de baixo troca subindo: a velha sai, a nova entra por baixo.
        NoticeStateOld.Text = NoticeState.Text;
        NoticeState.Text = n.State;
        ShowActions(n);
        Motion.Run(NoticeStateOld, OpacityProperty, 1, 0, 180, Motion.In);
        Motion.Run(NoticeState, OpacityProperty, 0, 1, 260, Motion.Out, delayMs: 60);
        Motion.Run(NoticeStateMove, TranslateTransform.YProperty, 6, 0, 320, Motion.Out, delayMs: 60);
        Chime(n);
        ArmHold(n);
    }

    private void Chime(Notice n)
    {
        if (_settings.NotifySound && n.Sound is { } c) Sounds.Play(c);
    }

    private void ArmHold(Notice n)
    {
        _noticeHold.Stop();
        _noticeHold.Interval = n.What switch
        {
            NoticeKind.Reading => TimeSpan.FromSeconds(15), // espera o resultado; quem chama manda o fim
            NoticeKind.Disconnected => TimeSpan.FromMilliseconds(2600),
            NoticeKind.Consent => TimeSpan.FromSeconds(20), // tempo de ler e decidir; sumir sem resposta nao e' recusa
            _ => TimeSpan.FromSeconds(4),
        };
        _noticeHold.Start();
    }

    private void EndNotice()
    {
        int gen = ++_gen;
        double away = (NoticeTab.ActualWidth + 12) * Edge;
        Motion.Run(NoticeMove, TranslateTransform.XProperty, null, away, 200, Motion.In, () =>
        {
            if (gen != _gen) return;
            NoticeClip.Visibility = Visibility.Collapsed;
            _notice = null;
            _chargeKey = null;
            _noticeOnly = false;
            void Next()
            {
                if (_queue.Count == 0) return;
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                t.Tick += (_, _) => { t.Stop(); if (_notice is null && _queue.Count > 0) StartNotice(_queue.Dequeue()); };
                t.Start();
            }
            if (_settings.WidgetAutoHide && _revealed && !IsMouseOver)
                SlideCardOut(() => { _revealed = false; Render(); UpdateView(); Next(); });
            else { Render(); UpdateView(); Next(); }
        });
    }

    /// <summary>A lingueta na altura do anel do aparelho, sem passar do fim do cartao.</summary>
    private void AlignTab()
    {
        if (_notice is null || NoticeClip.Visibility != Visibility.Visible) return;
        var item = _items.FirstOrDefault(i => Same(i.Key, _notice.Key));
        if (item is null || Rings.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement box) return;
        double ringMid = box.TranslatePoint(new Point(0, 16), Strip).Y;
        double h = NoticeClip.ActualHeight;
        double top = Math.Clamp(ringMid - h / 2, 0, Math.Max(0, Card.ActualHeight - h));
        NoticeClip.Margin = new Thickness(0, top, 0, 0);
    }

    // ---------------- transicoes ----------------

    private void Squeeze(double to, double ms)
    {
        Motion.Run(_cardScale, ScaleTransform.ScaleXProperty, null, to, ms, Motion.Out);
        Motion.Run(_cardScale, ScaleTransform.ScaleYProperty, null, to, ms, Motion.Out);
    }

    /// <summary>Alca vira widget: o cartao sai da borda.</summary>
    private void SlideCardIn()
    {
        _gen++;
        double w = Card.ActualWidth > 0 ? Card.ActualWidth : 56;
        Motion.Run(_cardMove, TranslateTransform.XProperty, _cardMove.X != 0 ? null : w * Edge, 0, 300, Motion.Out);
        Motion.Run(Card, OpacityProperty, null, 1, 200, Motion.Out);
    }

    /// <summary>Widget volta a ser alca: entra na borda, mais rapido do que saiu.</summary>
    private void SlideCardOut(Action done)
    {
        int gen = ++_gen;
        double w = Card.ActualWidth > 0 ? Card.ActualWidth : 56;
        Motion.Run(Card, OpacityProperty, null, 0, 180, Motion.In);
        Motion.Run(_cardMove, TranslateTransform.XProperty, null, w * Edge, 200, Motion.In, () =>
        {
            if (gen != _gen) return;
            done();
            _cardMove.BeginAnimation(TranslateTransform.XProperty, null);
            _cardMove.X = 0;
            Card.BeginAnimation(OpacityProperty, null);
            Card.Opacity = 1;
        });
    }

    /// <summary>Aneis viram lista: o cartao abre a partir da borda e as linhas entram em sequencia.</summary>
    private void AnimateExpand()
    {
        if (!Motion.Enabled) return;
        _gen++;
        const double W = 268;
        double w0 = Math.Max(1, _collapsedSize.Width), h0 = Math.Max(1, _collapsedSize.Height);
        var clip = new RectangleGeometry(new Rect(Edge > 0 ? W - w0 : 0, 0, w0, h0), 8, 8);
        Card.Clip = clip;
        Dispatcher.BeginInvoke(() =>
        {
            var full = new Rect(0, 0, Card.ActualWidth, Card.ActualHeight);
            var a = new RectAnimation(full, Motion.Ms(420)) { EasingFunction = Motion.Out };
            a.Completed += (_, _) => { if (ReferenceEquals(Card.Clip, clip)) Card.Clip = null; };
            clip.BeginAnimation(RectangleGeometry.RectProperty, a);
            var parts = new List<UIElement> { DragArea };
            for (int i = 0; i < Rows.Items.Count; i++)
                if (Rows.ItemContainerGenerator.ContainerFromIndex(i) is UIElement c) parts.Add(c);
            parts.Add(OpenPanelButton);
            Motion.Stagger(parts, 14 * Edge, 0, 40);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Lista volta a aneis: fecha para a borda (220 ms) e so entao troca.</summary>
    private void AnimateCollapse(Action done)
    {
        if (!Motion.Enabled || Card.ActualWidth <= 0) { done(); return; }
        int gen = ++_gen;
        double w0 = Math.Max(1, _collapsedSize.Width), h0 = Math.Max(1, _collapsedSize.Height);
        var clip = new RectangleGeometry(new Rect(0, 0, Card.ActualWidth, Card.ActualHeight), 8, 8);
        Card.Clip = clip;
        var a = new RectAnimation(new Rect(Edge > 0 ? Card.ActualWidth - w0 : 0, 0, w0, h0), Motion.Ms(220)) { EasingFunction = Motion.In };
        a.Completed += (_, _) =>
        {
            Card.Clip = null;
            if (gen == _gen) done();
        };
        clip.BeginAnimation(RectangleGeometry.RectProperty, a);
        Motion.Run(Expanded, OpacityProperty, 1, 0.2, 200, Motion.In, () => { Expanded.BeginAnimation(OpacityProperty, null); Expanded.Opacity = 1; });
    }

    /// <summary>Gruda na borda guardada (monitor, lado, altura), em pixels.</summary>
    private void Place()
    {
        if (_dragging) return;
        if (Native.WindowRect(this).Width == 0) return; // sem handle ainda; SourceInitialized chama de novo

        // Tamanho pelo que o WPF ja decidiu, nao pelo GetWindowRect: no
        // SizeChanged a janela ainda tem o tamanho velho, e do lado direito o
        // widget expandia para fora da tela (x calculado com a largura antiga).
        var dpi = VisualTreeHelper.GetDpi(this);
        int w = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        int h = (int)Math.Round(ActualHeight * dpi.DpiScaleY);

        var work = Native.WorkAreaOf(_settings.WidgetMonitor);
        int center = work.Top + (int)Math.Round(_settings.WidgetOffset * work.Height);
        int y = Math.Clamp(center - h / 2, work.Top, Math.Max(work.Top, work.Bottom - h));
        int x = _settings.WidgetSide == "left" ? work.Left : work.Right - w;
        Native.MoveWindowTo(this, x, y);
    }

    /// <summary>
    /// Soltou: o widget vai para a borda esquerda ou direita mais perto, no
    /// monitor onde caiu, na altura onde caiu -- o mesmo comportamento do dock
    /// do Token Monitor. Fica sempre numa borda porque o desenho (cantos so do
    /// lado de dentro) e' de widget de borda.
    /// </summary>
    private void Snap()
    {
        var rect = Native.WindowRect(this);
        int cx = rect.Left + rect.Width / 2, cy = rect.Top + rect.Height / 2;
        var (device, work) = Native.MonitorAt(cx, cy);
        if (work.Height <= 0) { Place(); return; }

        _settings.WidgetMonitor = device;
        _settings.WidgetSide = cx < work.Left + work.Width / 2 ? "left" : "right";
        _settings.WidgetOffset = Math.Clamp((double)(cy - work.Top) / work.Height, 0, 1);
        PositionChanged?.Invoke();
        UpdateView();
    }
}

/// <summary>Um aparelho no widget: o anel e a linha do expandido.</summary>
public sealed class RingItem
{
    /// <summary>Circunferencia do anel: raio 13 (Ellipse 29 com traco 3).</summary>
    private const double C = 2 * Math.PI * 13;
    private const double T = 3; // StrokeDashArray do WPF conta em multiplos da espessura

    public RingItem(DeviceRowView v)
    {
        Key = v.Key;
        Name = v.Name;
        Icon = Icons.For(v.Kind);
        IsLive = v.IsLive;
        IsStale = v.IsStale;
        RingOpacity = v.IsStale ? 0.55 : 1;
        DotRing = new SolidColorBrush(Theme.IsDark ? Color.FromRgb(0x17, 0x18, 0x1B) : Color.FromRgb(0xF3, 0xF3, 0xF3));

        ArcBrush = RowItem.R(v.IsLow ? "Level.Alert" : v.IsStale ? "Level.Stale" : "Accent.Text");
        ArcCap = PenLineCap.Flat;

        switch (v.ValueKind)
        {
            case ValueKind.Percent:
                IsPercent = true;
                Fraction = v.Percent / 100.0;
                HasArc = v.Percent > 0;
                ArcDash = new DoubleCollection { C * v.Percent / 100 / T, C / T };
                if (v.Percent < 100) ArcCap = PenLineCap.Round;
                ValueText = $"{v.Percent}%";
                break;

            case ValueKind.Band:
                double seg = C / 3;
                TrackDash = new DoubleCollection { (seg - 3) / T, 3 / T };
                HasArc = v.BandCount > 0;
                ArcDash = new DoubleCollection();
                for (int i = 0; i < v.BandCount; i++)
                {
                    ArcDash.Add((seg - 3) / T);
                    ArcDash.Add(i < v.BandCount - 1 ? 3 / T : (C - (v.BandCount * seg) + 3) / T);
                }
                ValueText = v.BandLabel;
                break;

            default:
                TrackDash = new DoubleCollection { 2 / T, 3.1 / T };
                ValueText = L.T("no cabo", "wired");
                break;
        }

        ValueBrush = RowItem.R(v.IsLow ? "Level.Alert" : "Text.Primary");
        MetaBrush = RowItem.R("Text.Tertiary");

        if (v.IsLive)
        {
            Meta = v.IsLow ? (v.Note is { } n ? L.T($"ao vivo · {n}", $"live · {n}") : L.T("ao vivo", "live")) : L.T("ao vivo", "live");
        }
        else
        {
            var parts = new List<string>();
            if (v.Origin == "Cabo") parts.Add(L.T("cabo", "wired"));
            if (v.ValueKind == ValueKind.Band) parts.Add(L.T("faixa", "range"));
            if (v.ChargeText is { } c) parts.Add(c);
            else if (v.Age is { } a) parts.Add(a);
            if (v.IsLow && v.Note is { } note) parts.Add(note);
            Meta = string.Join(" · ", parts);
        }

        Tooltip = $"{v.Name}: {ValueText}";
    }

    /// <summary>Anel provisorio do aviso: aparelho lendo (ainda sem linha) ou que acabou de sair (o arco esvazia).</summary>
    public RingItem(Notice n, double lastFraction)
    {
        Key = n.Key;
        Name = n.Name;
        Icon = Icons.For(n.Kind);
        RingOpacity = 1;
        DotRing = Brushes.Transparent;
        ArcBrush = RowItem.R("Accent.Text");
        ArcCap = PenLineCap.Round;
        IsPercent = true;
        Fraction = 0;
        From = n.What == NoticeKind.Disconnected ? lastFraction : null;
        IsReading = n.What == NoticeKind.Reading;
        ValueBrush = RowItem.R("Text.Primary");
        MetaBrush = RowItem.R("Text.Tertiary");
        Tooltip = n.Name;
    }

    public string Key { get; } = "";
    public bool IsPercent { get; }
    /// <summary>No percentual o trilho e' do RingArc, que o apaga antes do fim.</summary>
    public bool ShowTrackEllipse => !IsPercent;
    public double Fraction { get; }
    /// <summary>De onde o arco parte (o valor mostrado antes); null = sem animar.</summary>
    public double? From { get; set; }
    public bool IsReading { get; set; }
    public bool Pulse { get; set; }
    public bool HasDashArc => HasArc && !IsPercent;
    public bool RoundCap => ArcCap == PenLineCap.Round;
    public string Name { get; }
    public Geometry Icon { get; }
    public bool IsLive { get; }
    public bool IsStale { get; }
    public double RingOpacity { get; }
    public Brush DotRing { get; }
    public bool HasArc { get; }
    public DoubleCollection? ArcDash { get; }
    public DoubleCollection? TrackDash { get; }
    public PenLineCap ArcCap { get; }
    public Brush ArcBrush { get; }
    public string ValueText { get; } = "";
    public Brush ValueBrush { get; }
    public string Meta { get; } = "";
    public Brush MetaBrush { get; }
    public string Tooltip { get; }
}
