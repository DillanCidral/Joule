using System.Windows.Threading;
using BatteryTray.Core;
using Forms = System.Windows.Forms;

namespace BatteryTray.Ui;

/// <summary>
/// Dono do icone da bandeja e do ciclo de vida do app.
///
/// Modelo de leitura: SOB DEMANDA. Ha exatamente tres gatilhos -- abertura do
/// app (que inclui o login do Windows, via autostart), clique em Atualizar e o
/// item Atualizar do menu. Nao existe timer de verificacao periodica, e o
/// alerta de bateria baixa e' avaliado apenas no fim de uma leitura.
///
/// O icone continua sendo o NotifyIcon do WinForms: o WPF nao tem um, e este
/// roda bem no laco de mensagens do WPF.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly Settings _settings;
    private readonly ReadingService _service;
    private readonly Forms.NotifyIcon _tray;
    private PanelWindow _panel;
    private readonly WidgetWindow _widget;
    private readonly HashSet<string> _alerted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dispatcher _ui;

    private System.Drawing.Icon? _currentIcon;

    /// <summary>
    /// Releitura do Bluetooth. Nao e' polling de dispositivo: le o percentual
    /// que a pilha do Windows ja guardou no devnode (SetupAPI), nada vai pelo
    /// radio. Aprovado pelo dono em 2026-10-11; o HID segue sob demanda.
    /// </summary>
    private readonly DispatcherTimer _bluetoothTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private RefreshResult? _last;

    /// <summary>Nao nulo em --demo: os dados do design no lugar da leitura de verdade.</summary>
    private readonly string? _demo;
    private string? _supportAfterReading;

    /// <summary>
    /// Atualizador (Velopack). So existe instalado pelo Setup.exe; rodando do
    /// bin ou do dist fica nulo. Procura ao abrir e a cada hora -- e' ler uma
    /// pasta (ou, no lancamento, um arquivo no GitHub), nenhum aparelho.
    /// </summary>
    private readonly Velopack.UpdateManager? _updates;
    // Teste a cada 5 min: a versao nova entra pouco depois de publicada, sem clique.
    private readonly DispatcherTimer _updateTimer = new() { Interval = AppInfo.IsTest ? TimeSpan.FromMinutes(5) : TimeSpan.FromHours(1) };
    private Velopack.UpdateInfo? _pendingUpdate;
    private Forms.ToolStripMenuItem? _updateItem;
    private bool _checkingUpdate;
    private bool _balloonIsUpdate;

    /// <summary>
    /// Aviso de conexao (decidido em 2026-11-10): o Windows avisa que um HID
    /// entrou/saiu ou que a arvore de aparelhos mudou. Nada aqui e' periodico:
    /// cada aviso (re)arma uma espera de uma vez so; vencida, UMA leitura do
    /// que entrou (ReadArrived) ou a marcacao do que saiu (MarkRemoved), e no
    /// maximo uma leitura de conexao a cada <see cref="ReadingTurns.Gap"/>.
    /// Quem le e quando mora em <see cref="ReadingTurns"/> (testado no --selftest).
    /// </summary>
    private DeviceWatch? _watch;
    private readonly ReadingTurns _turns = new();
    private readonly DispatcherTimer _arrivalSettle = new() { Interval = ReadingTurns.Settle };
    private readonly DispatcherTimer _devicesSettle = new() { Interval = TimeSpan.FromMilliseconds(2000) };

    private OnboardingWindow? _onboarding;
    private SendDeviceWindow? _sendDevice;
    private TrayRingWindow? _trayRing;

    public TrayController(Settings settings, ReadingService service, bool startedByWindows, string? demo = null)
    {
        _settings = settings;
        _demo = demo;
        _service = service;
        _ui = Dispatcher.CurrentDispatcher;

        Theme.Apply(Theme.Parse(settings.Appearance), settings.GlassOpacity, settings.GlassBlur);
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _panel = CreatePanel();
        Theme.Changed += OnThemeChanged;

        _widget = new WidgetWindow(settings);
        _widget.OpenPanelRequested += () => _panel.ShowNearTray();
        _widget.RefreshRequested += () => BeginRefresh();
        _widget.PositionChanged += () => _settings.Save();
        _widget.AllowRequested += Allow;
        if (_demo?.Contains("widget") == true) _widget.PinExpanded();

        _currentIcon = TrayIconFactory.Create(null, _settings.LowBatteryThreshold);
        _tray = new Forms.NotifyIcon
        {
            Icon = _currentIcon,
            Text = AppInfo.Name,
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) _panel.Toggle();
        };
        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_balloonIsUpdate && _pendingUpdate is not null) ApplyUpdate();
            else _panel.ShowNearTray();
        };

        if (_demo is null)
        {
            _service.LiveValueChanged += () => _ui.BeginInvoke(() => RefreshLive(passive: true, bluetooth: false));
            _bluetoothTimer.Tick += (_, _) => RefreshLive(passive: false, bluetooth: true, notify: true);
            _bluetoothTimer.Start();

            try
            {
                _watch = new DeviceWatch();
                _watch.HidChanged += OnHidChanged;
                _watch.DevicesChanged += () => { _devicesSettle.Stop(); _devicesSettle.Start(); };
            }
            catch (Exception ex) { Log.Error("DeviceWatch", ex); }
            _arrivalSettle.Tick += (_, _) => { _arrivalSettle.Stop(); OnArrivalSettled(); };
            _devicesSettle.Tick += (_, _) => { _devicesSettle.Stop(); RefreshLive(passive: false, bluetooth: true, notify: true); };
        }

        if (_demo is null)
        {
            try
            {
                // GitHub: so versoes publicadas e estaveis (prerelease: false), sem token (60 consultas/hora por IP, docs do GithubSource).
                var updates = AppInfo.UpdateFeed.StartsWith("https://", StringComparison.Ordinal)
                    ? new Velopack.UpdateManager(new Velopack.Sources.GithubSource(AppInfo.UpdateFeed, null, false))
                    : new Velopack.UpdateManager(AppInfo.UpdateFeed);
                if (updates.IsInstalled)
                {
                    _updates = updates;
                    _updateTimer.Tick += (_, _) => CheckForUpdates(quiet: true);
                    _updateTimer.Start();
                    _ui.BeginInvoke(() => CheckForUpdates(quiet: true));
                }
            }
            catch (Exception ex) { Log.Error("Atualizador", ex); }
            if (_updateItem is not null) _updateItem.Visible = _updates is not null;
            if (_updates is not null) _panel.SetUpdateStatus(L.T("Procurar atualização", "Check for updates"), true); // o painel nasce antes do atualizador
        }

        Log.Write($"App iniciado (startup={startedByWindows}, exe={Autostart.ExecutablePath}, dados={AppPaths.DataDir}).");
        // O --demo roda de qualquer pasta (bin\Debug): nao pode apontar o autostart para si.
        if (_demo is null) Autostart.RefreshPathIfEnabled();

        // Leitura inicial: abertura do app / login do Windows. Na primeira vez
        // abrem as boas-vindas, e o painel so aparece quando elas fecham.
        bool welcome = _demo is null ? !_settings.OnboardingDone : _demo.Contains("boas-vindas");
        // Depois de uma atualizacao (que reabre com --startup), a tela de apoio, uma vez por versao.
        string? updatedFrom = _demo is null ? Changes.UpdatedFrom(_settings.LastVersion, AppInfo.Version, _settings.OnboardingDone)
            : _demo.Contains("apoio") ? "" : null;
        if (_demo is null && _settings.LastVersion != AppInfo.Version) { _settings.LastVersion = AppInfo.Version; _settings.Save(); }
        BeginRefresh(openPanelWhenDone: !startedByWindows && !welcome && updatedFrom is null);
        if (welcome) _ui.BeginInvoke(ShowOnboarding);
        else _supportAfterReading = updatedFrom; // depois da leitura: o contador ja inclui o que esta no painel
    }

    /// <summary>Tela de apoio e "O que mudou", depois de uma atualizacao.</summary>
    private void ShowSupport(string updatedFrom)
    {
        var w = new SupportWindow(_demo is null ? _settings.DevicesSeen.Count : 4, _demo is null ? _settings.LowAlerts : 12, Changes.Since(updatedFrom));
        w.Show();
    }

    /// <summary>Boas-vindas da beta: na primeira abertura e por "Rever a apresentacao".</summary>
    public void ShowOnboarding()
    {
        if (_onboarding is not null) { _onboarding.Activate(); return; }
        _panel.Hide();
        _onboarding = new OnboardingWindow();
        _onboarding.StepChanged += OnOnboardingStep;
        _onboarding.SendDeviceRequested += ShowSendDevice;
        _onboarding.SearchAgainRequested += () => BeginRefresh();
        if (_last is not null) ShowFoundInOnboarding(_last);
        _onboarding.Closed += (_, _) =>
        {
            _onboarding = null;
            EndShowcase();
            if (_demo is null && !_settings.OnboardingDone)
            {
                _settings.OnboardingDone = true;
                _settings.Save();
            }
            _panel.ShowNearTray();
        };
        _onboarding.Show();
    }

    /// <summary>
    /// Tela 2: painel e widget abertos e pulsando, anel no icone. Tela 3:
    /// destaque no Atualizar. Tela 4: no reloginho e em "Mostrar outros".
    /// Sem aparelho com bateria na ultima leitura, painel e widget mostram
    /// exemplos (decidido em 2026-11-05). Nada aqui le aparelho.
    /// </summary>
    private void OnOnboardingStep(int step)
    {
        // Telas 1 e 2 (boas-vindas, "Achei estes aparelhos") nao destacam nada.
        if (step <= OnboardingWindow.FoundPage) { EndShowcase(); return; }
        step -= OnboardingWindow.FoundPage;

        var devices = _last?.Devices ?? Array.Empty<DeviceEntry>();
        var examples = PanelView.MainRows(devices, _settings.LowBatteryThreshold, DateTime.UtcNow).Count == 0 ? Demo.Examples() : null;
        var pulse = Theme.Pulse();
        var spot = step switch { 1 => PanelWindow.Spot.Panel, 2 => PanelWindow.Spot.Refresh, _ => PanelWindow.Spot.Others };

        _panel.SetShowcase(true, examples, spot, pulse);
        if (!_panel.IsVisible) _panel.ShowNearTray();
        _widget.SetShowcase(step == 1, examples, pulse);

        _trayRing?.Close();
        _trayRing = null;
        if (step == 1 && Native.TrayIconRect(_tray) is { } icon)
        {
            _trayRing = new TrayRingWindow(icon, pulse);
            _trayRing.Show();
        }
        else if (step == 1) Log.Write("Boas-vindas: icone da bandeja nao encontrado (escondido na setinha?); sem anel.");

        _onboarding?.Activate(); // o painel abrindo toma o foco; as setas e o Enter continuam na apresentacao
    }

    private void ShowFoundInOnboarding(RefreshResult result) =>
        _onboarding?.ShowFound(PanelView.MainRows(result.Devices, _settings.LowBatteryThreshold, DateTime.UtcNow), PanelView.Others(result.Devices).Count);

    /// <summary>"Mandar meu aparelho": Configuracoes e a apresentacao.</summary>
    public void ShowSendDevice()
    {
        if (_sendDevice is not null) { _sendDevice.Activate(); return; }
        _sendDevice = new SendDeviceWindow(_last?.Devices ?? Array.Empty<DeviceEntry>());
        _sendDevice.Closed += (_, _) => _sendDevice = null;
        _sendDevice.Show();
    }

    private void EndShowcase()
    {
        _panel.SetShowcase(false);
        _widget.SetShowcase(false);
        _trayRing?.Close();
        _trayRing = null;
    }

    private PanelWindow CreatePanel()
    {
        var panel = new PanelWindow(_settings);
        panel.RefreshRequested += () => BeginRefresh();
        panel.OnboardingRequested += ShowOnboarding;
        panel.SendDeviceRequested += ShowSendDevice;
        panel.NotifySampleRequested += ShowNotifySample;
        panel.AllowRequested += Allow;
        panel.UpdateRequested += () => { if (_pendingUpdate is not null) ApplyUpdate(); else CheckForUpdates(quiet: true, fromPanel: true); };
        if (_pendingUpdate is not null) panel.SetUpdateStatus(L.T($"Reiniciar e atualizar para {_pendingUpdate.TargetFullRelease.Version}", $"Restart and update to {_pendingUpdate.TargetFullRelease.Version}"), true);
        else if (_updates is null) panel.SetUpdateStatus(L.T("Só no app instalado", "Only in the installed app"), false);
        panel.RestartRequested += Restart;
        panel.SettingsChanged += () =>
        {
            _alerted.Clear(); // limite novo: reavaliar do zero na proxima leitura
            if (_last is not null) UpdateTrayIcon(_last);
            _widget.ApplySettings();
        };
        return panel;
    }

    /// <summary>Vidro e' janela por pixel; Sistema/Solido, janela com moldura: trocar recria.</summary>
    private void OnThemeChanged() => _ui.BeginInvoke(() =>
    {
        bool wantLayered = Theme.Layered;
        if (_panel.IsLayered == wantLayered) return;

        bool wasVisible = _panel.IsVisible;
        bool inSettings = _panel.IsShowingSettings;
        var old = _panel;
        _panel = CreatePanel();
        if (_last is not null) _panel.ShowResult(_last);
        old.Close();
        if (wasVisible)
        {
            _panel.ShowNearTray();
            if (inSettings) _panel.OpenSettings();
        }
    });

    private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        // Tema claro/escuro, transparencia e cor de destaque chegam como General/Color.
        if (e.Category is Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.Color)
            _ui.BeginInvoke(() =>
            {
                Theme.Apply(Theme.Parse(_settings.Appearance), _settings.GlassOpacity, _settings.GlassBlur);
                if (_last is not null) UpdateTrayIcon(_last); // a barra pode ter trocado de tema
            });
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(L.T("Atualizar agora", "Refresh now"), null, (_, _) => BeginRefresh());
        menu.Items.Add(L.T("Abrir painel", "Open panel"), null, (_, _) => _panel.ShowNearTray());
        menu.Items.Add(new Forms.ToolStripSeparator());

        var autostartItem = new Forms.ToolStripMenuItem(L.T("Iniciar com o Windows", "Start with Windows"))
        {
            CheckOnClick = true,
            Checked = Autostart.IsEnabled(),
        };
        autostartItem.Click += (_, _) =>
        {
            Autostart.Set(autostartItem.Checked);
            autostartItem.Checked = Autostart.IsEnabled();
        };
        menu.Items.Add(autostartItem);

        menu.Items.Add(L.T("Abrir pasta de dados", "Open data folder"), null, (_, _) => OpenDataFolder());

        _updateItem = new Forms.ToolStripMenuItem(L.T("Procurar atualização", "Check for updates")) { Visible = false };
        _updateItem.Click += (_, _) =>
        {
            if (_pendingUpdate is not null) ApplyUpdate();
            else CheckForUpdates(quiet: false);
        };
        menu.Items.Add(_updateItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(L.T("Sair", "Exit"), null, (_, _) => Exit());
        return menu;
    }

    /// <summary>Abre um site ou arquivo no programa padrao do Windows.</summary>
    public static void OpenExternal(string target)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Error("Abrir", ex); }
    }

    public static void OpenDataFolder()
    {
        try
        {
            AppPaths.EnsureDir();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppPaths.DataDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { Log.Error("AbrirPasta", ex); }
    }

    /// <summary>Chamado de outra thread (segunda instancia do exe pedindo o painel).</summary>
    public void ShowPanelFromAnyThread() => _ui.BeginInvoke(() => _panel.ShowNearTray());

    private void BeginRefresh(bool openPanelWhenDone = false)
    {
        if (!_turns.TryStartFull()) return;

        _panel.SetBusy(true);
        SetTrayIcon(TrayIconFactory.Create(null, _settings.LowBatteryThreshold, reading: true));

        Task.Run(async () =>
        {
            if (_demo is not null)
            {
                await Task.Delay(1500); // da tempo de ver o "lendo..."
                return Demo.Result(_demo);
            }
            _service.EnsurePassiveListeners();
            return _service.Refresh();
        }).ContinueWith(task =>
        {
            var result = task.IsFaulted
                ? new RefreshResult { Devices = Array.Empty<DeviceEntry>(), FatalError = task.Exception?.GetBaseException().Message }
                : task.Result;

            _ui.BeginInvoke(() =>
            {
                try
                {
                    _panel.SetBusy(false);
                    ApplyResult(result);
                    if (openPanelWhenDone) _panel.ShowNearTray();
                }
                catch (Exception ex) { Log.Error("BeginRefresh.Continue", ex); }
                finally { _turns.EndFull(); }
            });
        }, TaskScheduler.Default);
    }

    /// <summary>So as linhas ao vivo (escuta passiva e Bluetooth), sobre a ultima leitura.</summary>
    private void RefreshLive(bool passive, bool bluetooth, bool notify = false)
    {
        var previous = _last;
        if (previous is null) return;
        if (!_turns.TryStartLive())
        {
            // O valor da escuta chegou durante outra leitura: repinta quando ela acabar (EndLive), senao fica ate 5 min velho.
            if (passive) _passiveMissed = true;
            return;
        }

        Task.Run(() => _service.RefreshLive(previous, passive, bluetooth)).ContinueWith(task =>
        {
            var result = task.IsFaulted ? null : task.Result;
            _ui.BeginInvoke(() =>
            {
                try
                {
                    // Uma leitura completa que terminou no meio tem precedencia.
                    if (result is not null && ReferenceEquals(_last, previous)) ApplyResult(result, notify);
                }
                catch (Exception ex) { Log.Error("RefreshLive.Continue", ex); }
                finally { EndLive(); }
            });
        }, TaskScheduler.Default);
    }

    private bool _passiveMissed;

    /// <summary>Fim de uma leitura ao vivo ou de conexao; repinta o valor da escuta que chegou no meio dela.</summary>
    private void EndLive()
    {
        _turns.EndLive();
        if (!_passiveMissed) return;
        _passiveMissed = false;
        RefreshLive(passive: true, bluetooth: false);
    }

    private void ApplyResult(RefreshResult result, bool notify = false)
    {
        var before = _last;
        _last = result;
        _panel.ShowResult(result);
        _widget.ShowResult(result);
        UpdateTrayIcon(result);
        ShowFoundInOnboarding(result);
        if (notify && before is not null && _demo is null) AnnounceChanges(before, result);
        if (_settings.AlertsEnabled && _demo is null) EvaluateLowBattery(result);
        if (_demo is null) NoteDevicesSeen(result);
        if (_supportAfterReading is { } from) { _supportAfterReading = null; ShowSupport(from); }
    }

    /// <summary>Contador da tela de apoio: so o aviso que de fato aparece.</summary>
    private void CountLowAlert(NoticeKind what)
    {
        if (_demo is not null || EffectivePlace(what) == "off") return;
        _settings.LowAlerts++;
        _settings.Save();
    }

    /// <summary>Contador da tela de apoio: aparelho que apareceu com bateria pela primeira vez.</summary>
    private void NoteDevicesSeen(RefreshResult result)
    {
        var fresh = result.Devices.Where(d => d.ShowsBattery && !_settings.DevicesSeen.Contains(d.Key)).Select(d => d.Key).ToList();
        if (fresh.Count == 0) return;
        _settings.DevicesSeen.AddRange(fresh);
        _settings.Save();
    }

    private void UpdateTrayIcon(RefreshResult result)
    {
        var rows = PanelView.MainRows(result.Devices, _settings.LowBatteryThreshold, DateTime.UtcNow);

        // O icone mostra o aparelho mais fraco entre os que tem numero.
        int? weakest = rows.Where(r => r.ValueKind == ValueKind.Percent).Select(r => (int?)r.Percent).Min();

        SetTrayIcon(TrayIconFactory.Create(weakest, _settings.LowBatteryThreshold));
        _tray.Text = TooltipText(rows);
    }

    private void SetTrayIcon(System.Drawing.Icon icon)
    {
        var previous = _currentIcon;
        _currentIcon = icon;
        _tray.Icon = icon;
        previous?.Dispose();
    }

    /// <summary>Uma linha por aparelho, mais fracos primeiro; o que nao cabe em 63 vira "+N".</summary>
    internal static string TooltipText(IReadOnlyList<DeviceRowView> rows)
    {
        if (rows.Count == 0) return L.T($"{AppInfo.Name}: nenhum aparelho com bateria", $"{AppInfo.Name}: no device with battery");

        const int Max = 63; // limite do NotifyIcon.Text
        var lines = new List<string>();
        for (int i = 0; i < rows.Count; i++)
        {
            string line = Line(rows[i]);
            int left = rows.Count - i - 1;
            string suffix = left > 0 ? $"\n+{left} outros" : "";
            string candidate = string.Join("\n", lines.Append(line)) + suffix;
            if (candidate.Length > Max)
            {
                lines.Add(L.T($"+{rows.Count - i} outros", $"+{rows.Count - i} more"));
                break;
            }
            lines.Add(line);
        }
        string text = string.Join("\n", lines);
        return text.Length <= Max ? text : text[..Max];

        static string Line(DeviceRowView r)
        {
            string name = r.Name.Length > 16 ? r.Name[..16].TrimEnd() : r.Name;
            return r.ValueKind switch
            {
                ValueKind.Percent => r.IsStale && r.Age is not null ? $"{name} {r.Percent}% ({r.Age.Replace("há ", "").Replace(" ago", "")})" : $"{name} {r.Percent}%",
                ValueKind.Band => $"{name} {r.BandLabel}",
                _ => L.T($"{name} no cabo", $"{name} wired"),
            };
        }
    }

    /// <summary>
    /// Avaliado SOMENTE aqui, no fim de uma leitura. Nao ha watchdog de fundo.
    /// So dispara para valores lidos agora -- nunca para valor de cache.
    /// </summary>
    private void EvaluateLowBattery(RefreshResult result)
    {
        foreach (var device in result.Devices)
        {
            // Aparelho que nao esta conectado nao gera alerta. Importa por
            // causa do Bluetooth: o Windows guarda o ultimo percentual do
            // aparelho desligado, e sem esta guarda um fone na gaveta com 15%
            // avisaria "bateria baixa" a cada leitura.
            if (!device.IsConnected) continue;
            if (device.Reading.Status != ReadingStatus.Ok) continue;

            bool low;
            string value;

            if (device.Reading.Percent is int p)
            {
                low = p < _settings.LowBatteryThreshold;
                value = $"{p}%";
                if (p > _settings.LowBatteryThreshold + 5) _alerted.Remove(device.Key);
            }
            else if (device.Reading.Level is "LOW" or "EMPTY")
            {
                low = true;
                value = Known.LevelLabelPtBr(device.Reading.Level);
            }
            else
            {
                _alerted.Remove(device.Key);
                continue;
            }

            if (!low) continue;
            if (!_alerted.Add(device.Key)) continue;
            CountLowAlert(NoticeKind.Low);

            Announce(new Notice(device.Key, device.DisplayName, device.Kind, NoticeKind.Low, value, Low: true));
        }
    }

    // ---------------- aviso de conexao ----------------

    private readonly HashSet<string> _pendingReading = new(StringComparer.OrdinalIgnoreCase);

    private void OnHidChanged(string path, bool arrived)
    {
        _turns.Note(path, arrived);
        // Um aparelho chega em varias colecoes seguidas: espera assentar e trata tudo junto.
        _arrivalSettle.Stop();
        _arrivalSettle.Interval = ReadingTurns.Settle;
        _arrivalSettle.Start();
    }

    private void OnArrivalSettled()
    {
        var previous = _last;
        if (previous is null) { _turns.Forget(); return; } // a primeira leitura completa ja vai ver tudo

        var (removed, arrived, retry) = _turns.Take(DateTime.UtcNow);
        if (retry is { } later) { _arrivalSettle.Interval = later; _arrivalSettle.Start(); }
        if (removed.Count == 0 && arrived.Count == 0) return;

        Task.Run(() =>
        {
            var r = previous;
            if (removed.Count > 0) r = _service.MarkRemoved(r, removed) ?? r;
            if (arrived.Count > 0) r = _service.ReadArrived(r, arrived, plan => _ui.BeginInvoke(() => BeginNotices(plan))) ?? r;
            return r;
        }).ContinueWith(task =>
        {
            var result = task.IsFaulted ? null : task.Result;
            _ui.BeginInvoke(() =>
            {
                try
                {
                    if (result is not null && !ReferenceEquals(result, previous) && ReferenceEquals(_last, previous)) ApplyResult(result, notify: true);
                    FinishPendingNotices();
                }
                catch (Exception ex) { Log.Error("Conexao.Continue", ex); }
                finally { EndLive(); }
            });
        }, TaskScheduler.Default);
    }

    /// <summary>A leitura de conexao comecou: no widget, o anel ja aparece com "Lendo...".</summary>
    private void BeginNotices(IReadOnlyList<Arriving> plan)
    {
        if (EffectivePlace(NoticeKind.Reading) != "widget") return;
        foreach (var a in plan)
        {
            _pendingReading.Add(a.Key);
            _widget.Notify(new Notice(a.Key, a.Name, a.Kind, NoticeKind.Reading));
        }
    }

    /// <summary>Quem comecou "Lendo..." e nao voltou como conectado termina em "sem dado".</summary>
    private void FinishPendingNotices()
    {
        foreach (var key in _pendingReading.ToList())
        {
            var d = _last?.Devices.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            _widget.Notify(new Notice(key, d?.DisplayName ?? "Aparelho", d?.Kind ?? DeviceKind.Generic, NoticeKind.Connected));
        }
        _pendingReading.Clear();
    }

    /// <summary>Compara a leitura de antes com a de agora: quem entrou e quem saiu.</summary>
    private void AnnounceChanges(RefreshResult before, RefreshResult after)
    {
        var old = before.Devices.GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var now = after.Devices.GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var arrived = new HashSet<string>(after.Arrived, StringComparer.OrdinalIgnoreCase);
        // Todo aparelho que conectou, ate sem suporte: sem aviso parecia que o app nao viu.
        foreach (var d in now.Values.Where(d => d.IsConnected))
        {
            bool wasOn = old.TryGetValue(d.Key, out var o) && o.IsConnected;
            if (wasOn && !_pendingReading.Contains(d.Key) && !arrived.Contains(d.Key)) continue;
            _pendingReading.Remove(d.Key);
            if (!d.ShowsBattery && Interop.HidRequest.WaitingVid(d.HardwareId) is { } vid)
            {
                Announce(new Notice(d.Key, d.DisplayName, d.Kind, NoticeKind.Consent, Vid: vid));
                continue;
            }
            var (value, percent, low) = Fresh(d);
            if (low && _alerted.Add(d.Key)) CountLowAlert(NoticeKind.Connected); // o aviso de conexao ja diz "bateria baixa"
            Announce(new Notice(d.Key, d.DisplayName, d.Kind, NoticeKind.Connected, value, low, percent));
        }
        foreach (var o in old.Values.Where(o => o.IsConnected))
        {
            if ((now.TryGetValue(o.Key, out var n) && n.IsConnected) || arrived.Contains(o.Key)) continue;
            Announce(new Notice(o.Key, o.DisplayName, o.Kind, NoticeKind.Disconnected));
        }
    }

    /// <summary>
    /// So o que foi lido agora: cache nao vira numero de aviso (regra 6). O
    /// Bluetooth guarda o ultimo valor congelado ate o aparelho mandar outro:
    /// valor com carimbo de mais de 5 minutos, na hora de conectar, e' "sem dado".
    /// </summary>
    private (string? Value, int? Percent, bool Low) Fresh(DeviceEntry d) =>
        d.Reading.Status == ReadingStatus.Ok && DateTime.UtcNow - d.Reading.EffectiveObservedUtc > TimeSpan.FromMinutes(5) ? (null, null, false) :
        d.Reading switch
    {
        { Status: ReadingStatus.Charging } => (L.T("no cabo", "wired"), null, false),
        { Status: ReadingStatus.Ok, Percent: int p } => ($"{p}%", p, p < _settings.LowBatteryThreshold),
        { Status: ReadingStatus.Ok, Level: { Length: > 0 } l } => (Known.LevelLabelPtBr(l), null, l is "LOW" or "EMPTY"),
        _ => (null, null, false),
    };

    /// <summary>
    /// Licenca para a marca receber a pergunta de bateria (camada de seguranca,
    /// 2026-11-12): grava e le na hora, como o Atualizar (regra 1: clique da pessoa).
    /// </summary>
    private void Allow(string vid)
    {
        if (!_settings.AskAllowedVids.Contains(vid, StringComparer.OrdinalIgnoreCase)) _settings.AskAllowedVids.Add(vid);
        _settings.Save();
        Log.Write($"Licenca dada para a marca {vid}.");
        BeginRefresh();
    }

    /// <summary>Onde o aviso aparece: "widget", "windows" ou "off". Bateria baixa tem a chave propria.</summary>
    private string EffectivePlace(NoticeKind what)
    {
        string place = _settings.NotifyPlace;
        if (what == NoticeKind.Low) { if (!_settings.AlertsEnabled) return "off"; if (place == "off") place = "windows"; }
        if (place == "widget" && !_widget.CanNotify) place = "windows";
        return place;
    }

    private void Announce(Notice n)
    {
        string place = EffectivePlace(n.What);
        if (place == "off") return;
        Log.Write($"Aviso ({place}): {n.Name} {n.What} {n.Value ?? "sem dado"}.");
        if (place == "widget") { _widget.Notify(n); return; }
        var (title, text) = n.Balloon;
        ShowBalloon(title, text, n.Low ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info, silent: true);
        if (_settings.NotifySound && n.Sound is { } c) Sounds.Play(c);
    }

    /// <summary>
    /// Escolheu "No widget" ou "Do Windows" nas Configuracoes: um aviso de
    /// exemplo na hora, com o som. Usa o ultimo valor de um aparelho de
    /// verdade (ou o exemplo das boas-vindas); nao le nada.
    /// </summary>
    public void ShowNotifySample()
    {
        var rows = PanelView.MainRows(_last?.Devices ?? Array.Empty<DeviceEntry>(), _settings.LowBatteryThreshold, DateTime.UtcNow);
        if (rows.Count == 0) rows = PanelView.MainRows(Demo.Examples().Devices, _settings.LowBatteryThreshold, DateTime.UtcNow);
        var r = rows.FirstOrDefault(x => x.ValueKind == ValueKind.Percent) ?? rows.FirstOrDefault();
        if (r is null) return;
        string? value = r.ValueKind switch { ValueKind.Percent => $"{r.Percent}%", ValueKind.Band => r.BandLabel, _ => L.T("no cabo", "wired") };
        var done = new Notice(r.Key, r.Name, r.Kind, NoticeKind.Connected, value, r.IsLow, r.ValueKind == ValueKind.Percent ? r.Percent : null);

        string place = EffectivePlace(NoticeKind.Connected);
        if (place == "widget")
        {
            _widget.Notify(done with { What = NoticeKind.Reading });
            var later = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
            later.Tick += (_, _) => { later.Stop(); _widget.Notify(done); };
            later.Start();
        }
        else if (place == "windows")
        {
            var (title, text) = done.Balloon;
            ShowBalloon("Exemplo: " + title, text, Forms.ToolTipIcon.Info, silent: true);
            if (_settings.NotifySound && done.Sound is { } c) Sounds.Play(c);
        }
    }

    /// <summary>Procura e ja baixa; aplicar so com o clique (reinicia o app).</summary>
    private async void CheckForUpdates(bool quiet, bool fromPanel = false)
    {
        if (_updates is null || _checkingUpdate || _pendingUpdate is not null) return;
        _checkingUpdate = true;
        _panel.SetUpdateStatus(L.T("Procurando…", "Checking…"), false);
        try
        {
            var info = await _updates.CheckForUpdatesAsync();
            if (info is null)
            {
                _panel.SetUpdateStatus(fromPanel ? L.T("Você já tem a mais nova", "You have the latest") : L.T("Procurar atualização", "Check for updates"), !fromPanel);
                if (!quiet) ShowBalloon(AppInfo.Name, L.T($"Você já está na versão mais nova ({AppInfo.Version}).", $"You’re on the latest version ({AppInfo.Version})."), Forms.ToolTipIcon.Info);
                return;
            }
            await _updates.DownloadUpdatesAsync(info);
            _pendingUpdate = info;
            string version = info.TargetFullRelease.Version.ToString();
            Log.Write($"Atualização {version} baixada.");

            // Canal de teste: troca sozinho, sem esperar o clique (pedido do dono
            // em 2026-11-03). Volta quieto pelo --startup, como no login.
            if (AppInfo.IsTest) { ApplyUpdate(); return; }
            if (_updateItem is not null) _updateItem.Text = L.T($"Reiniciar e atualizar para {version}", $"Restart and update to {version}");
            _panel.SetUpdateStatus(L.T($"Reiniciar e atualizar para {version}", $"Restart and update to {version}"), true);
            ShowBalloon(L.T("Atualização pronta", "Update ready"), L.T($"{AppInfo.Name} {version}: clique aqui para reiniciar e atualizar.", $"{AppInfo.Name} {version}: click here to restart and update."), Forms.ToolTipIcon.Info);
            _balloonIsUpdate = true;
        }
        catch (Exception ex)
        {
            Log.Error("Atualizador", ex);
            _panel.SetUpdateStatus(fromPanel ? L.T("Não deu; tentar de novo", "Didn’t work; try again") : L.T("Procurar atualização", "Check for updates"), true);
            if (!quiet) ShowBalloon(AppInfo.Name, L.T("Não deu para procurar atualização. Detalhes no log.", "Couldn’t check for updates. Details in the log."), Forms.ToolTipIcon.Warning);
        }
        finally { _checkingUpdate = false; }
    }

    private void ApplyUpdate()
    {
        if (_updates is null || _pendingUpdate is null) return;
        Log.Write($"Aplicando atualização {_pendingUpdate.TargetFullRelease.Version}.");
        try { _tray.Visible = false; } catch { }
        // --startup: volta quieto, sem abrir o painel, como no login.
        try { _updates.ApplyUpdatesAndRestart(_pendingUpdate, new[] { "--startup" }); }
        catch (Exception ex)
        {
            Log.Error("Atualizador.Aplicar", ex);
            _tray.Visible = true;
        }
    }

    /// <param name="silent">Sem o som do Windows: o Joule toca o dele (cai para o normal se nao der).</param>
    private void ShowBalloon(string title, string text, Forms.ToolTipIcon icon, bool silent = false)
    {
        _balloonIsUpdate = false;
        try
        {
            if (silent && Native.ShowSilentBalloon(_tray, title, text, icon == Forms.ToolTipIcon.Warning)) return;
            _tray.ShowBalloonTip(8000, title, text, icon);
        }
        catch (Exception ex) { Log.Error("Balloon", ex); }
    }

    /// <summary>Abre outra instancia com --restart (ela espera esta soltar o mutex) e sai.</summary>
    private void Restart()
    {
        Log.Write("Reabrindo (idioma).");
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, _demo is null ? "--restart" : $"--restart --demo {_demo}") { UseShellExecute = false }); }
        catch (Exception ex) { Log.Error("Restart", ex); return; }
        Exit();
    }

    private void Exit()
    {
        Log.Write("Saindo.");
        try { _tray.Visible = false; } catch { }
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        Theme.Changed -= OnThemeChanged;
        _bluetoothTimer.Stop();
        _updateTimer.Stop();
        _arrivalSettle.Stop();
        _devicesSettle.Stop();
        _watch?.Dispose();
        try { _service.Dispose(); } catch { }
        try { _tray.Dispose(); } catch { }
        try { _panel.Close(); } catch { }
        try { _widget.Close(); } catch { }
        _currentIcon?.Dispose();
    }
}
