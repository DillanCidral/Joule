//! The panel window and the application around it: reading on demand in a worker thread, hardware
//! arrival through kernel uevents, notifications, single instance (the D-Bus application id).
//! The look follows `PanelWindow.xaml`: a borderless flyout, 380 wide, with three views (list,
//! settings, about) that slide in the direction of the arrow.

use super::{
    about_view, fonts, icons, kit,
    panel_view::{self, Action, PanelInput, PanelView},
    pill::PillState,
    settings_view::{self, SettingsView},
    sound::{self, Tone},
    theme::{self, Appearance, Look, Palette},
    tray::{self, Command, JouleTray, MenuDevice},
    widget_view::{self, WidgetView},
};
use crate::hotplug::{self, HidrawEvent};
use adw::prelude::*;
use gtk::{gdk, gio, glib};
use joule_core::{
    DeviceEntry, ReadingStatus,
    alerts::LowBatteryAlert,
    l::t,
    log,
    panel::{self, DeviceRowView, ValueKind},
    paths,
    settings::Settings,
    turns::{ReadingTurns, SETTLE},
};
use joule_providers::{
    registry::Registry,
    service::{ReadingService, RefreshResult},
};
use std::{
    cell::{Cell, RefCell},
    path::PathBuf,
    rc::Rc,
    sync::{Arc, Mutex},
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};

pub const APP_ID: &str = "io.github.dillancidral.Joule";

fn now_unix() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0)
}

/// Everything one reading hands back to the main thread.
struct Outcome {
    result: RefreshResult,
    alerts: Vec<LowBatteryAlert>,
    waiting: Vec<u16>,
}

struct Ui {
    app: adw::Application,
    window: gtk::ApplicationWindow,
    dir: PathBuf,
    service: Arc<Mutex<ReadingService>>,
    settings: Rc<RefCell<Settings>>,
    result: RefCell<RefreshResult>,
    waiting: RefCell<Vec<u16>>,
    turns: RefCell<ReadingTurns>,
    busy: Cell<bool>,
    stack: gtk::Stack,
    panel: Rc<PanelView>,
    widget: Rc<WidgetView>,
    widget_window: gtk::Window,
    settings_view: Rc<SettingsView>,
    css: gtk::CssProvider,
    look: Cell<Look>,
    palette: Cell<Palette>,
    hidden_by_deactivate_at: Cell<Option<Instant>>,
    was_active: Cell<bool>,
    /// When the window got the focus: a focus loss right after opening is the desktop moving the
    /// focus (the app grid closing), not the person clicking elsewhere.
    active_since: Cell<Option<Instant>>,
    /// Hide on focus loss (the Windows flyout behaviour). Only when it was opened from the top bar.
    auto_dismiss: Cell<bool>,
    dismissing: Cell<bool>,
    tray: RefCell<Option<ksni::blocking::Handle<JouleTray>>>,
}

impl Ui {
    fn threshold(&self) -> i32 {
        self.settings.borrow().low_battery_threshold
    }

    // ---------------- theme ----------------

    /// Rebuilds the stylesheet from the settings and the system dark/light switch.
    fn apply_theme(&self) {
        let sm = adw::StyleManager::default();
        let s = self.settings.borrow();
        let look = Look {
            appearance: theme::parse(&s.appearance),
            dark: sm.is_dark(),
            glass_opacity: s.glass_opacity,
        };
        let accent = sm.accent_color_rgba();
        let sys = (
            (accent.red() * 255.0).round() as u8,
            (accent.green() * 255.0).round() as u8,
            (accent.blue() * 255.0).round() as u8,
        );
        self.css.load_from_string(&theme::css(&look, Some(sys)));
        self.look.set(look);
        self.palette.set(theme::palette(&look, Some(sys)));
    }

    // ---------------- rendering ----------------

    fn render(self: &Rc<Self>) {
        let result = self.result.borrow().clone();
        let waiting = self.waiting.borrow().clone();
        let lookup = move |hw: &str| {
            let vid = hw.split(':').next()?;
            let n = u16::from_str_radix(vid, 16).ok()?;
            waiting.contains(&n).then(|| vid.to_uppercase())
        };
        self.panel.render(&PanelInput {
            devices: &result.devices,
            when_unix: result.when_unix,
            fatal_error: result.fatal_error.as_deref(),
            busy: self.busy.get(),
            threshold: self.threshold(),
            now_unix: now_unix(),
            waiting_vid: &lookup,
            look: self.look.get(),
            pal: self.palette.get(),
        });
        let rows = panel::main_rows(
            &result.devices,
            self.threshold(),
            now_unix(),
            result.when_unix,
        );
        self.widget.render(
            &result.devices,
            result.when_unix,
            self.threshold(),
            now_unix(),
            self.look.get(),
            self.palette.get(),
        );
        self.update_tray(&rows, self.busy.get());
    }

    fn update_tray(&self, rows: &[DeviceRowView], reading: bool) {
        let state = if reading {
            PillState::Reading
        } else if rows.is_empty() {
            PillState::Empty
        } else if rows.iter().any(|r| r.is_low) {
            PillState::Low
        } else {
            PillState::Ok
        };
        let devices: Vec<MenuDevice> = rows
            .iter()
            .map(|r| {
                let value = match r.value_kind {
                    ValueKind::Percent => format!("{}%", r.percent),
                    ValueKind::Band => r.band_label.clone(),
                    ValueKind::Cable => t("no cabo", "wired"),
                };
                let name: String = if r.name.chars().count() > 28 {
                    format!(
                        "{}…",
                        r.name.chars().take(27).collect::<String>().trim_end()
                    )
                } else {
                    r.name.clone()
                };
                let age = match (&r.age, r.is_stale) {
                    (Some(a), true) => format!(" ({a})"),
                    _ => String::new(),
                };
                MenuDevice {
                    icon: tray_icon_name(r.kind),
                    label: format!("{name}   {value}{age}"),
                }
            })
            .collect();
        let listen_only = self.settings.borrow().listen_only;
        let when = self.result.borrow().when_unix;
        let last = if when > 0 {
            panel_view::clock_text(when)
        } else {
            String::new()
        };
        if let Some(h) = self.tray.borrow().as_ref() {
            h.update(move |tray: &mut JouleTray| {
                tray.state = state;
                tray.devices = devices;
                tray.listen_only = listen_only;
                tray.last_reading = last;
            });
        }
    }

    // ---------------- views ----------------

    fn show_view(&self, name: &str, forward: bool) {
        self.stack.set_transition_type(if forward {
            gtk::StackTransitionType::SlideLeft
        } else {
            gtk::StackTransitionType::SlideRight
        });
        if name == "settings" {
            self.settings_view.load(true_blur());
        }
        self.stack.set_visible_child_name(name);
    }

    // ---------------- readings ----------------

    /// Full reading (start, Refresh, a new permission). One at a time: a second click joins the first.
    fn refresh(self: &Rc<Self>) {
        if !self.turns.borrow_mut().try_start_full() {
            return;
        }
        self.busy.set(true);
        self.render();
        let settings = self.settings.borrow().clone();
        let service = self.service.clone();
        let ui = self.clone();
        glib::spawn_future_local(async move {
            let outcome = gio::spawn_blocking(move || {
                let mut svc = service.lock().unwrap_or_else(|e| e.into_inner());
                if *svc.settings() != settings {
                    svc.set_settings(settings);
                }
                let result = svc.refresh();
                let alerts = svc.low_battery_alerts(&result);
                let waiting = svc
                    .gate()
                    .waiting_consent()
                    .into_iter()
                    .map(|(v, _)| v)
                    .collect();
                Outcome {
                    result,
                    alerts,
                    waiting,
                }
            })
            .await;
            ui.turns.borrow_mut().end_full();
            ui.busy.set(false);
            match outcome {
                Ok(o) => ui.apply(o, false),
                Err(_) => {
                    log::write("refresh: worker thread panicked");
                    ui.render();
                }
            }
        });
    }

    fn apply(self: &Rc<Self>, o: Outcome, announce_arrivals: bool) {
        let place = self.settings.borrow().notify_place.clone();
        if announce_arrivals && place != "off" {
            for key in &o.result.arrived {
                if let Some(d) = o.result.devices.iter().find(|d| &d.key == key) {
                    self.notify_connected(d);
                    let tone = if d.has_battery_value() {
                        Tone::Connected
                    } else {
                        Tone::NotFound
                    };
                    sound::play(self.settings.borrow().notify_sound, tone);
                }
            }
        }
        for a in &o.alerts {
            if place != "off" {
                sound::play(self.settings.borrow().notify_sound, Tone::Low);
                let n = gio::Notification::new(&t("Bateria baixa", "Low battery"));
                n.set_body(Some(&format!("{} {}", a.name, a.value)));
                n.set_priority(gio::NotificationPriority::High);
                self.app
                    .send_notification(Some(&format!("low-{}", a.key)), &n);
            }
            let mut s = self.settings.borrow_mut();
            s.low_alerts += 1;
            s.save_to(&self.dir);
        }
        *self.result.borrow_mut() = o.result;
        *self.waiting.borrow_mut() = o.waiting;
        self.render();
    }

    fn notify_connected(&self, d: &DeviceEntry) {
        let value = match (d.effective_percent(), d.reading.status) {
            (Some(p), _) => format!("{p}%"),
            (None, ReadingStatus::Charging) => t("no cabo", "wired"),
            _ => t("lendo…", "reading…"),
        };
        let n = gio::Notification::new(&t(
            &format!("{} conectou", d.display_name),
            &format!("{} connected", d.display_name),
        ));
        n.set_body(Some(&value));
        self.app
            .send_notification(Some(&format!("conn-{}", d.key)), &n);
    }

    fn allow(self: &Rc<Self>, vid: &str) {
        {
            let mut s = self.settings.borrow_mut();
            s.ask_allowed_vids.retain(|v| !v.eq_ignore_ascii_case(vid));
            s.ask_allowed_vids.push(vid.to_uppercase());
            s.save_to(&self.dir);
        }
        self.refresh();
    }

    // ---------------- hardware arrival ----------------

    /// The settle wait is over: read what came, mark what left, at most once per 10 s.
    fn process_turns(self: &Rc<Self>) {
        let taken = self.turns.borrow_mut().take(Instant::now());
        if let Some(retry) = taken.retry {
            let ui = self.clone();
            glib::timeout_add_local_once(retry.max(Duration::from_millis(50)), move || {
                ui.process_turns()
            });
        }
        if taken.removed.is_empty() && taken.arrived.is_empty() {
            return;
        }
        let previous = self.result.borrow().clone();
        let service = self.service.clone();
        let settings = self.settings.borrow().clone();
        let ui = self.clone();
        glib::spawn_future_local(async move {
            let out = gio::spawn_blocking(move || {
                let mut svc = service.lock().unwrap_or_else(|e| e.into_inner());
                if *svc.settings() != settings {
                    svc.set_settings(settings);
                }
                let mut current = previous;
                if !taken.removed.is_empty()
                    && let Some(next) = svc.mark_removed(&current, &taken.removed)
                {
                    current = next;
                }
                if !taken.arrived.is_empty()
                    && let Some(next) = svc.read_arrived(&current, &taken.arrived)
                {
                    current = next;
                }
                let alerts = svc.low_battery_alerts(&current);
                let waiting = svc
                    .gate()
                    .waiting_consent()
                    .into_iter()
                    .map(|(v, _)| v)
                    .collect();
                Outcome {
                    result: current,
                    alerts,
                    waiting,
                }
            })
            .await;
            ui.turns.borrow_mut().end_live();
            if let Ok(o) = out {
                if o.result.devices.iter().any(|d| {
                    !d.is_connected
                        && d.reading.status == ReadingStatus::Disconnected
                        && ui
                            .result
                            .borrow()
                            .devices
                            .iter()
                            .any(|p| p.key == d.key && p.is_connected)
                }) {
                    sound::play(ui.settings.borrow().notify_sound, Tone::Disconnected);
                }
                ui.apply(o, true);
            }
        });
    }

    fn start_hotplug(self: &Rc<Self>) {
        let (tx, rx) = async_channel::unbounded::<HidrawEvent>();
        if let Err(e) = hotplug::spawn(move |ev| tx.send_blocking(ev).is_ok()) {
            log::error("hotplug", &e);
            return;
        }
        let ui = self.clone();
        glib::spawn_future_local(async move {
            while let Ok(ev) = rx.recv().await {
                ui.turns.borrow_mut().note(&ev.path, ev.arrived);
                // A device shows up as several nodes in a row: wait until the burst ends.
                while let Ok(Ok(next)) = glib::future_with_timeout(SETTLE, rx.recv()).await {
                    ui.turns.borrow_mut().note(&next.path, next.arrived);
                }
                ui.process_turns();
            }
        });
    }

    /// Shows or hides the floating widget as the setting says.
    fn sync_widget(&self) {
        if self.settings.borrow().widget_enabled {
            self.widget_window.present();
        } else {
            self.widget_window.set_visible(false);
        }
    }

    // ---------------- show / hide ----------------

    /// Shows the flyout: back on the list, fading in (260 ms, like the slide of the Windows panel).
    fn show(self: &Rc<Self>) {
        self.auto_dismiss.set(false);
        self.show_flyout();
    }

    /// Opened from the top bar item: it closes again when the person clicks elsewhere.
    fn show_from_tray(self: &Rc<Self>) {
        self.auto_dismiss.set(true);
        self.show_flyout();
    }

    fn show_flyout(self: &Rc<Self>) {
        self.dismissing.set(false);
        self.render();
        self.window.set_opacity(0.0);
        self.window.present();
        let target = adw::CallbackAnimationTarget::new({
            let w = self.window.clone();
            move |v| w.set_opacity(v)
        });
        let anim = adw::TimedAnimation::builder()
            .widget(&self.window)
            .value_from(0.0)
            .value_to(1.0)
            .duration(260)
            .easing(adw::Easing::EaseOutQuint)
            .target(&target)
            .build();
        let w = self.window.clone();
        anim.connect_done(move |_| w.set_opacity(1.0));
        anim.play();
        // An animation that never runs (window not drawn yet, no frame clock) must not leave the
        // flyout invisible.
        let ui = self.clone();
        glib::timeout_add_local_once(Duration::from_millis(600), move || {
            if !ui.dismissing.get() {
                ui.window.set_opacity(1.0);
            }
        });
    }

    fn toggle(self: &Rc<Self>) {
        if self.window.is_visible() && !self.dismissing.get() {
            self.dismiss();
            return;
        }
        // The click on the tray item takes the focus from the panel first: without this guard the
        // panel would close and open again in the same click.
        if let Some(at) = self.hidden_by_deactivate_at.get()
            && at.elapsed() < Duration::from_millis(300)
        {
            return;
        }
        self.show_from_tray();
    }

    /// Closes sinking and fading (150 ms, faster than it opens), then hides.
    fn dismiss(self: &Rc<Self>) {
        if !self.window.is_visible() || self.dismissing.get() {
            return;
        }
        self.dismissing.set(true);
        let target = adw::CallbackAnimationTarget::new({
            let w = self.window.clone();
            move |v| w.set_opacity(v)
        });
        let anim = adw::TimedAnimation::builder()
            .widget(&self.window)
            .value_from(1.0)
            .value_to(0.0)
            .duration(150)
            .easing(adw::Easing::EaseInCubic)
            .target(&target)
            .build();
        let ui = self.clone();
        anim.connect_done(move |_| {
            if !ui.dismissing.get() {
                return; // reopened in between
            }
            ui.dismissing.set(false);
            // Without a top bar item there is nothing to bring the window back: leaving ends the app.
            if ui.tray.borrow().is_none() {
                ui.app.quit();
                return;
            }
            ui.window.set_visible(false);
            ui.show_view("main", false);
        });
        anim.play();
    }

    // ---------------- events from the views ----------------

    fn on_panel_action(self: &Rc<Self>, a: Action) {
        match a {
            Action::Refresh => self.refresh(),
            Action::OpenSettings => self.show_view("settings", true),
            Action::Allow(vid) => self.allow(&vid),
        }
    }

    fn on_settings_event(self: &Rc<Self>, e: settings_view::Event) {
        use settings_view::Event;
        match e {
            Event::Changed => {
                self.render();
            }
            Event::LookChanged => {
                self.apply_theme();
                self.render();
            }
            Event::NotifySample => self.notify_sample(),
            Event::Restart => self.restart(),
            Event::SendDevice => {
                let url = super::submission::url(
                    &joule_hid::inventory::scan(),
                    env!("CARGO_PKG_VERSION"),
                );
                open_uri(&self.window, &url);
            }
            Event::OpenDataFolder => {
                let file = gio::File::for_path(&self.dir);
                gtk::FileLauncher::new(Some(&file)).launch(
                    Some(&self.window),
                    gio::Cancellable::NONE,
                    |_| {},
                );
            }
            Event::OpenAbout => self.show_view("about", true),
            Event::WidgetChanged => self.sync_widget(),
        }
    }

    /// A sample notice, so choosing a place shows what it looks like.
    fn notify_sample(&self) {
        let n = gio::Notification::new(&t("Exemplo de aviso", "Sample notice"));
        n.set_body(Some(&t(
            "Assim o Joule avisa quando um aparelho conecta.",
            "This is how Joule tells you a device connected.",
        )));
        self.app.send_notification(Some("sample"), &n);
    }

    /// "Reopen now" after a language change: start a new copy a moment after this one quits.
    fn restart(&self) {
        if let Ok(exe) = std::env::current_exe() {
            let _ = std::process::Command::new("sh")
                .arg("-c")
                .arg("sleep 1; exec \"$0\" --panel")
                .arg(exe)
                .stdin(std::process::Stdio::null())
                .spawn();
        }
        self.app.quit();
    }
}

fn tray_icon_name(kind: joule_core::DeviceKind) -> String {
    use joule_core::DeviceKind::*;
    match kind {
        Headset => "audio-headset-symbolic",
        Mouse => "input-mouse-symbolic",
        Keyboard => "input-keyboard-symbolic",
        Gamepad => "input-gaming-symbolic",
        Generic => "drive-removable-media-symbolic",
    }
    .into()
}

/// Blur behind an app window is not available on GNOME (no protocol for it).
fn true_blur() -> bool {
    false
}

/// Saves what the window shows as a PNG (a developer aid for checking the look without a screen
/// grabber: `JOULE_SNAPSHOT=/dir joule --panel` writes main.png, settings.png and about.png, then quits).
fn snapshot_png(window: &gtk::Window, paintable: &gtk::WidgetPaintable, path: &std::path::Path) {
    let (w, h) = (
        paintable.intrinsic_width().max(1),
        paintable.intrinsic_height().max(1),
    );
    let snapshot = gtk::Snapshot::new();
    paintable.snapshot(&snapshot, f64::from(w), f64::from(h));
    let Some(node) = snapshot.to_node() else {
        log::write(&format!(
            "snapshot: empty render node ({w}x{h}); visible={} mapped={} opacity={} size={}x{}",
            window.is_visible(),
            window.is_mapped(),
            window.opacity(),
            window.width(),
            window.height()
        ));
        return;
    };
    let Some(renderer) = window.native().and_then(|n| n.renderer()) else {
        log::write("snapshot: the window has no renderer");
        return;
    };
    let texture = renderer.render_texture(&node, None);
    if let Err(e) = texture.save_to_png(path) {
        log::error("snapshot", &e);
    }
}

fn run_snapshots(ui: &Rc<Ui>, dir: PathBuf) {
    // (delay in ms, name). "widget" shows the floating widget closed, "widget-open" hovered.
    let steps: [(u32, &'static str); 5] = [
        (5000, "main"),
        (8000, "settings"),
        (11000, "about"),
        (14000, "widget"),
        (17000, "widget-open"),
    ];
    for (ms, name) in steps {
        let ui = ui.clone();
        let dir = dir.clone();
        glib::timeout_add_local_once(Duration::from_millis(u64::from(ms)), move || {
            let target: gtk::Window = if name.starts_with("widget") {
                ui.widget_window.present();
                ui.widget.set_expanded(name == "widget-open");
                ui.widget_window.clone()
            } else {
                if name != "main" {
                    ui.show_view(name, true);
                }
                ui.window.clone().upcast()
            };
            // The paintable must exist before the frame it copies is drawn.
            let paintable = gtk::WidgetPaintable::new(target.child().as_ref());
            glib::timeout_add_local_once(Duration::from_millis(1200), move || {
                snapshot_png(&target, &paintable, &dir.join(format!("{name}.png")));
                if name == "widget-open" {
                    ui.app.quit();
                }
            });
        });
    }
}

fn open_uri(window: &gtk::ApplicationWindow, uri: &str) {
    gtk::UriLauncher::new(uri).launch(Some(window), gio::Cancellable::NONE, |_| {});
}

fn work_area_height() -> i32 {
    gdk::Display::default()
        .and_then(|d| d.monitors().item(0))
        .and_then(|m| m.downcast::<gdk::Monitor>().ok())
        .map(|m| m.geometry().height())
        .unwrap_or(1080)
}

fn build_ui(app: &adw::Application, hidden: bool) -> Rc<Ui> {
    fonts::register();
    let dir = paths::data_dir();
    let settings = Rc::new(RefCell::new(Settings::load_from(&dir)));
    joule_core::l::set_language(&settings.borrow().language);
    let running_en = t("pt", "en") == "en";
    let service = Arc::new(Mutex::new(ReadingService::new(
        Registry::create_default(),
        settings.borrow().clone(),
        dir.clone(),
    )));

    // Style sheet: one provider, rebuilt when the look changes.
    let css = gtk::CssProvider::new();
    if let Some(display) = gdk::Display::default() {
        gtk::style_context_add_provider_for_display(
            &display,
            &css,
            gtk::STYLE_PROVIDER_PRIORITY_APPLICATION,
        );
        // Small fixed rules that do not depend on the look.
        let fixed = gtk::CssProvider::new();
        fixed.load_from_string(
            "flowboxchild.joule-flowchild { padding: 0; margin: 0; min-height: 0; min-width: 0; outline: none; background: none; }
             flowbox { background: none; }
             .joule-flat-card { background: transparent; border-radius: 6px; padding: 8px 12px; transition: background 150ms ease-out; }
             .joule-flat-card:hover { background: alpha(@window_fg_color, 0.06); }",
        );
        gtk::style_context_add_provider_for_display(
            &display,
            &fixed,
            gtk::STYLE_PROVIDER_PRIORITY_APPLICATION,
        );
    }
    adw::StyleManager::default().set_color_scheme(adw::ColorScheme::Default);

    // Window: borderless, 380 wide, height by content (`SizeToContent="Height"`).
    let window = gtk::ApplicationWindow::builder()
        .application(app)
        .title(super::APP_NAME)
        .default_width(380)
        .resizable(false)
        .decorated(false)
        .build();
    window.add_css_class("joule");

    let back = kit::back_button();
    let about_back = kit::back_button();

    let ui_cell: Rc<RefCell<Option<Rc<Ui>>>> = Rc::new(RefCell::new(None));
    let with_ui = {
        let ui_cell = ui_cell.clone();
        move |f: &dyn Fn(&Rc<Ui>)| {
            let ui = ui_cell.borrow().clone();
            if let Some(ui) = ui {
                f(&ui);
            }
        }
    };

    let panel = PanelView::new(
        {
            let w = with_ui.clone();
            Rc::new(move |a| w(&|ui| ui.on_panel_action(a.clone())))
        },
        true,
    );
    panel.set_max_height((work_area_height() - 160).max(200));

    let widget = WidgetView::new({
        let w = with_ui.clone();
        Rc::new(move |a| {
            w(&|ui| match a {
                widget_view::Action::Refresh => ui.refresh(),
                widget_view::Action::OpenPanel => ui.show(),
            })
        })
    });
    let widget_window = gtk::Window::builder()
        .application(app)
        .title(super::APP_NAME)
        .decorated(false)
        .resizable(false)
        .build();
    widget_window.add_css_class("joule");
    let widget_handle = gtk::WindowHandle::new();
    widget_handle.set_child(Some(&widget.root));
    widget_window.set_child(Some(&widget_handle));

    let settings_view = SettingsView::new(
        settings.clone(),
        dir.clone(),
        {
            let w = with_ui.clone();
            Rc::new(move |e| w(&|ui| ui.on_settings_event(e)))
        },
        &back,
        running_en,
        true_blur(),
    );
    let w = with_ui.clone();
    back.connect_clicked(move |_| w(&|ui| ui.show_view("main", false)));

    let about = about_view::build(
        &about_back,
        about_view::AboutHandlers {
            open: {
                let w = with_ui.clone();
                Rc::new(move |url: &str| {
                    let url = url.to_string();
                    w(&|ui| open_uri(&ui.window, &url));
                })
            },
            licenses: {
                let w = with_ui.clone();
                Rc::new(move || {
                    w(&|ui| {
                        open_uri(
                            &ui.window,
                            "https://github.com/DillanCidral/Joule/blob/main/THIRD-PARTY-NOTICES.md",
                        );
                    })
                })
            },
        },
    );
    let w = with_ui.clone();
    about_back.connect_clicked(move |_| w(&|ui| ui.show_view("settings", false)));

    let stack = gtk::Stack::new();
    stack.set_transition_duration(300);
    stack.set_vhomogeneous(false);
    stack.set_hhomogeneous(true);
    stack.set_interpolate_size(true);
    stack.add_named(&panel.root, Some("main"));
    stack.add_named(&settings_view.root, Some("settings"));
    stack.add_named(&about, Some("about"));

    let root = kit::vbox(0);
    root.add_css_class("joule-root");
    root.append(&stack);
    let handle = gtk::WindowHandle::new();
    handle.set_child(Some(&root));
    window.set_child(Some(&handle));

    let ui = Rc::new(Ui {
        app: app.clone(),
        window: window.clone(),
        dir,
        service,
        settings,
        result: RefCell::new(RefreshResult::default()),
        waiting: RefCell::new(Vec::new()),
        turns: RefCell::new(ReadingTurns::default()),
        busy: Cell::new(false),
        stack,
        panel,
        widget,
        widget_window,
        settings_view,
        css,
        look: Cell::new(Look {
            appearance: Appearance::Glass,
            dark: true,
            glass_opacity: 30,
        }),
        palette: Cell::new(theme::palette(
            &Look {
                appearance: Appearance::Glass,
                dark: true,
                glass_opacity: 30,
            },
            None,
        )),
        hidden_by_deactivate_at: Cell::new(None),
        was_active: Cell::new(false),
        active_since: Cell::new(None),
        auto_dismiss: Cell::new(false),
        dismissing: Cell::new(false),
        tray: RefCell::new(None),
    });
    *ui_cell.borrow_mut() = Some(ui.clone());
    ui.apply_theme();
    {
        let u = ui.clone();
        adw::StyleManager::default().connect_dark_notify(move |_| {
            u.apply_theme();
            u.render();
        });
        let u = ui.clone();
        adw::StyleManager::default().connect_accent_color_rgba_notify(move |_| {
            u.apply_theme();
            u.render();
        });
    }

    // Deactivating (clicking elsewhere) hides the flyout; Esc too.
    {
        let u = ui.clone();
        window.connect_is_active_notify(move |w| {
            if w.is_active() {
                u.was_active.set(true);
                u.active_since.set(Some(Instant::now()));
            } else if u.auto_dismiss.get()
                && u.was_active.get()
                && u.active_since
                    .get()
                    .is_some_and(|t| t.elapsed() > Duration::from_millis(400))
                && w.is_visible()
                && !u.dismissing.get()
            {
                u.was_active.set(false);
                u.hidden_by_deactivate_at.set(Some(Instant::now()));
                u.dismiss();
            }
        });
        let u = ui.clone();
        let keys = gtk::EventControllerKey::new();
        keys.connect_key_pressed(move |_, key, _, _| {
            if key == gdk::Key::Escape {
                if u.stack.visible_child_name().as_deref() != Some("main") {
                    u.show_view("main", false);
                } else {
                    u.dismiss();
                }
                return glib::Propagation::Stop;
            }
            glib::Propagation::Proceed
        });
        window.add_controller(keys);
    }
    // A window that is closed some other way keeps the process alive while the tray lives.
    let ui2 = ui.clone();
    window.connect_close_request(move |w| {
        if ui2.tray.borrow().is_some() {
            w.set_visible(false);
            glib::Propagation::Stop
        } else {
            glib::Propagation::Proceed
        }
    });

    let action = |name: &str, f: Box<dyn Fn()>| {
        let a = gio::SimpleAction::new(name, None);
        a.connect_activate(move |_, _| f());
        app.add_action(&a);
    };
    let ui2 = ui.clone();
    action("refresh", Box::new(move || ui2.refresh()));
    let app2 = app.clone();
    action("quit", Box::new(move || app2.quit()));
    app.set_accels_for_action("app.refresh", &["F5", "<primary>r"]);
    app.set_accels_for_action("app.quit", &["<primary>q"]);

    // The top bar item and its commands.
    let (tx, rx) = async_channel::unbounded::<Command>();
    *ui.tray.borrow_mut() = tray::spawn(tx, ui.settings.borrow().listen_only);
    let ui2 = ui.clone();
    glib::spawn_future_local(async move {
        while let Ok(cmd) = rx.recv().await {
            match cmd {
                Command::Toggle => ui2.toggle(),
                Command::Show => ui2.show_from_tray(),
                Command::Refresh => ui2.refresh(),
                Command::ToggleListenOnly => {
                    {
                        let mut s = ui2.settings.borrow_mut();
                        s.listen_only = !s.listen_only;
                        s.save_to(&ui2.dir);
                    }
                    ui2.settings_view.load(true_blur());
                    ui2.refresh();
                }
                Command::Quit => ui2.app.quit(),
            }
        }
    });

    ui.settings_view.load(true_blur());
    ui.sync_widget();
    ui.start_hotplug();
    ui.refresh();
    if !hidden || ui.tray.borrow().is_none() {
        ui.show();
    }
    let _ = icons::spinner();
    if let Some(dir) = std::env::var_os("JOULE_SNAPSHOT") {
        run_snapshots(&ui, PathBuf::from(dir));
    }
    ui
}

/// Runs the application. `--hidden` starts in the tray without a window (autostart).
pub fn run(hidden: bool) -> i32 {
    let app = adw::Application::builder().application_id(APP_ID).build();
    let ui: Rc<RefCell<Option<Rc<Ui>>>> = Rc::new(RefCell::new(None));
    let first_hidden = Rc::new(RefCell::new(hidden));
    // Keeps the process alive while the window is hidden.
    let guard = Rc::new(RefCell::new(None));
    app.connect_activate(move |app| {
        let existing = ui.borrow().clone();
        match existing {
            Some(u) => u.show(), // a second launch lands here: show the first window
            None => {
                let hide = std::mem::replace(&mut *first_hidden.borrow_mut(), false);
                let u = build_ui(app, hide);
                *guard.borrow_mut() = Some(app.hold());
                *ui.borrow_mut() = Some(u);
            }
        }
    });
    // GTK would reject our own flags; hand it only the program name.
    let code = app.run_with_args(&[std::env::args().next().unwrap_or_else(|| "joule".into())]);
    i32::from(code)
}
