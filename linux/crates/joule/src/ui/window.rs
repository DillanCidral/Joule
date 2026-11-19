//! The Joule window and the application around it: reading on demand in a worker thread, hardware
//! arrival through kernel uevents, notifications, the top bar item and single instance (the D-Bus
//! application id). The window is a normal libadwaita window; the Joule look lives in the rows.

use super::{
    about, fonts, icons,
    pill::PillState,
    prefs, rows,
    sound::{self, Tone},
    style,
    theme::{self, Look, Palette},
    tray::{self, Command, JouleTray, MenuDevice},
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

/// Local clock time "HH:MM" of a unix time.
pub fn clock_text(unix: i64) -> String {
    glib::DateTime::from_unix_local(unix)
        .ok()
        .and_then(|d| d.format("%H:%M").ok())
        .map(|s| s.to_string())
        .unwrap_or_default()
}

/// Everything one reading hands back to the main thread.
struct Outcome {
    result: RefreshResult,
    alerts: Vec<LowBatteryAlert>,
    waiting: Vec<u16>,
}

struct Ui {
    app: adw::Application,
    window: adw::ApplicationWindow,
    dir: PathBuf,
    service: Arc<Mutex<ReadingService>>,
    settings: Rc<RefCell<Settings>>,
    result: RefCell<RefreshResult>,
    waiting: RefCell<Vec<u16>>,
    turns: RefCell<ReadingTurns>,
    busy: Cell<bool>,
    content: gtk::Box,
    subtitle: gtk::Label,
    refresh_stack: gtk::Stack,
    others_open: Cell<bool>,
    tray: RefCell<Option<ksni::blocking::Handle<JouleTray>>>,
}

fn clear(b: &gtk::Box) {
    while let Some(c) = b.first_child() {
        b.remove(&c);
    }
}

impl Ui {
    fn threshold(&self) -> i32 {
        self.settings.borrow().low_battery_threshold
    }

    fn look(&self) -> (Look, Palette) {
        let dark = adw::StyleManager::default().is_dark();
        let look = Look { dark };
        (look, theme::palette(&look))
    }

    // ---------------- rendering ----------------

    fn render(self: &Rc<Self>) {
        let result = self.result.borrow().clone();
        let busy = self.busy.get();
        let waiting = self.waiting.borrow().clone();
        let lookup = move |hw: &str| {
            let vid = hw.split(':').next()?;
            let n = u16::from_str_radix(vid, 16).ok()?;
            waiting.contains(&n).then(|| vid.to_uppercase())
        };
        let main = panel::main_rows(
            &result.devices,
            self.threshold(),
            now_unix(),
            result.when_unix,
        );
        let consent = panel::need_consent(&result.devices, &lookup);
        let others = panel::others(&result.devices, &lookup);
        let (look, pal) = self.look();

        self.subtitle.set_text(&if busy {
            t("Lendo…", "Reading…")
        } else if let Some(err) = result.fatal_error.as_deref().filter(|e| !e.is_empty()) {
            t(
                &format!("Erro na leitura: {err}"),
                &format!("Reading error: {err}"),
            )
        } else if result.when_unix <= 0 {
            t("Ainda sem leitura", "No reading yet")
        } else {
            let hm = clock_text(result.when_unix);
            t(
                &format!("Última leitura às {hm}"),
                &format!("Last reading at {hm}"),
            )
        });
        self.refresh_stack
            .set_visible_child_name(if busy { "spinner" } else { "button" });

        clear(&self.content);
        let empty = main.is_empty() && consent.is_empty() && result.when_unix > 0;
        if empty {
            let has_others = !others.is_empty();
            let page = adw::StatusPage::builder()
                .title(if has_others {
                    t("Nenhum aparelho com bateria", "No device with battery")
                } else {
                    t("Nenhum aparelho conectado", "No device connected")
                })
                .description(if has_others {
                    t(
                        "Há aparelhos plugados, mas nenhum informou bateria. Veja o motivo de cada um abaixo.",
                        "Devices are plugged in, but none reported a battery. See each one’s reason below.",
                    )
                } else {
                    t(
                        "Ligue o dongle ou conecte por Bluetooth. Valores guardados voltam quando o aparelho reconectar.",
                        "Plug in the dongle or connect over Bluetooth. Saved values come back when the device reconnects.",
                    )
                })
                .build();
            page.add_css_class("compact");
            let glyph = icons::widget(
                if has_others {
                    icons::generic()
                } else {
                    icons::plug()
                },
                64,
                "dim-label",
            );
            page.set_child(Some(&{
                let b = gtk::Box::new(gtk::Orientation::Vertical, 18);
                b.prepend(&glyph);
                if !has_others {
                    let refresh = gtk::Button::with_label(&t("Atualizar", "Refresh"));
                    refresh.add_css_class("pill");
                    refresh.add_css_class("suggested-action");
                    refresh.set_halign(gtk::Align::Center);
                    refresh.set_action_name(Some("app.refresh"));
                    b.append(&refresh);
                }
                b
            }));
            self.content.append(&page);
        }

        if !consent.is_empty() {
            let group = adw::PreferencesGroup::builder()
                .title(t("Precisa de permissão", "Needs permission"))
                .build();
            for c in &consent {
                let ui = self.clone();
                let vid = c.vid.clone();
                group.add(&rows::consent_row(c, move || ui.allow(&vid)));
            }
            self.content.append(&group);
        }

        if !main.is_empty() {
            let group = adw::PreferencesGroup::new();
            for r in &main {
                group.add(&rows::device_row(r, busy, look, pal));
            }
            self.content.append(&group);
        }

        if !others.is_empty() {
            let group = adw::PreferencesGroup::new();
            let exp = adw::ExpanderRow::builder()
                .title(t(
                    &format!("Outros aparelhos ({})", others.len()),
                    &format!("Other devices ({})", others.len()),
                ))
                .subtitle(t(
                    "Plugados, sem valor de bateria",
                    "Plugged in, no battery value",
                ))
                .expanded(self.others_open.get() || empty)
                .build();
            exp.set_use_markup(false);
            for o in &others {
                exp.add_row(&rows::other_row(o));
            }
            let ui = self.clone();
            exp.connect_expanded_notify(move |e| ui.others_open.set(e.is_expanded()));
            group.add(&exp);
            self.content.append(&group);
        }

        self.update_tray(&main, busy);
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
            clock_text(when)
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
        let (notify, sound_on) = {
            let s = self.settings.borrow();
            (s.notify_place != "off", s.notify_sound)
        };
        if announce_arrivals && notify {
            for key in &o.result.arrived {
                if let Some(d) = o.result.devices.iter().find(|d| &d.key == key) {
                    self.notify_connected(d);
                    sound::play(
                        sound_on,
                        if d.has_battery_value() {
                            Tone::Connected
                        } else {
                            Tone::NotFound
                        },
                    );
                }
            }
        }
        for a in &o.alerts {
            if notify {
                sound::play(sound_on, Tone::Low);
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
            _ => t("sem valor de bateria", "no battery value"),
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
        let removed_any = !taken.removed.is_empty();
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
                if removed_any {
                    let s = ui.settings.borrow();
                    sound::play(
                        s.notify_sound && s.notify_place != "off",
                        Tone::Disconnected,
                    );
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

    // ---------------- window ----------------

    fn show(self: &Rc<Self>) {
        self.render();
        self.window.present();
    }

    fn toggle(self: &Rc<Self>) {
        if self.window.is_visible() && self.window.is_active() {
            self.window.set_visible(false);
        } else {
            self.show();
        }
    }

    fn on_prefs(self: &Rc<Self>, e: prefs::Event) {
        match e {
            prefs::Event::Changed => self.render(),
            prefs::Event::ReadAgain => self.refresh(),
            prefs::Event::Restart => self.restart(),
            prefs::Event::OpenDataFolder => {
                let file = gio::File::for_path(&self.dir);
                gtk::FileLauncher::new(Some(&file)).launch(
                    Some(&self.window),
                    gio::Cancellable::NONE,
                    |_| {},
                );
            }
            prefs::Event::SendDevice => self.send_device(),
        }
    }

    fn send_device(&self) {
        let url = super::submission::url(&joule_hid::inventory::scan(), env!("CARGO_PKG_VERSION"));
        gtk::UriLauncher::new(&url).launch(Some(&self.window), gio::Cancellable::NONE, |_| {});
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

/// The header title: "Joule" in Geist with the ember dot, the last reading under it.
fn title_widget() -> (gtk::Box, gtk::Label) {
    let v = gtk::Box::new(gtk::Orientation::Vertical, 0);
    v.set_valign(gtk::Align::Center);
    let brand = gtk::Box::new(gtk::Orientation::Horizontal, 0);
    brand.set_halign(gtk::Align::Center);
    let name = gtk::Label::new(Some(super::APP_NAME));
    name.add_css_class("joule-brand");
    brand.append(&name);
    let dot = gtk::DrawingArea::new();
    dot.set_content_width(5);
    dot.set_content_height(5);
    dot.set_valign(gtk::Align::End);
    dot.set_margin_bottom(4);
    dot.set_margin_start(1);
    dot.set_draw_func(|_, cr, w, h| {
        cr.arc(
            f64::from(w) / 2.0,
            f64::from(h) / 2.0,
            1.4,
            0.0,
            2.0 * std::f64::consts::PI,
        );
        cr.set_source_rgb(1.0, 0.761, 0.122);
        cr.fill().ok();
    });
    brand.append(&dot);
    v.append(&brand);
    let sub = gtk::Label::new(None);
    sub.add_css_class("caption");
    sub.add_css_class("dim-label");
    v.append(&sub);
    (v, sub)
}

/// Saves what a window shows as a PNG (developer aid: `JOULE_SNAPSHOT=/dir joule --panel` writes
/// main.png, settings.png and about.png of the real window, then quits).
fn snapshot_png(widget: &gtk::Widget, native: &gtk::Widget, path: &std::path::Path) {
    let paintable = gtk::WidgetPaintable::new(Some(widget));
    let (w, h) = (
        paintable.intrinsic_width().max(1),
        paintable.intrinsic_height().max(1),
    );
    let snapshot = gtk::Snapshot::new();
    paintable.snapshot(&snapshot, f64::from(w), f64::from(h));
    let (Some(node), Some(renderer)) = (
        snapshot.to_node(),
        native.native().and_then(|n| n.renderer()),
    ) else {
        log::write(&format!(
            "snapshot: nothing to render for {}",
            path.display()
        ));
        return;
    };
    if let Err(e) = renderer.render_texture(&node, None).save_to_png(path) {
        log::error("snapshot", &e);
    }
}

fn run_snapshots(ui: &Rc<Ui>, dir: PathBuf) {
    // Dialogs are separate surfaces in the window; render the whole window so they show too.
    let shot = |ui: &Rc<Ui>, name: &'static str, dir: PathBuf| {
        let w: gtk::Widget = ui.window.clone().upcast();
        let ui = ui.clone();
        glib::timeout_add_local_once(Duration::from_millis(1200), move || {
            let child = ui
                .window
                .content()
                .map(|c| c.upcast::<gtk::Widget>())
                .unwrap_or(w.clone());
            // A dialog is a child of the window: when one is open, render the window itself.
            let _ = child;
            let target = w.clone();
            snapshot_png(&target, &w, &dir.join(format!("{name}.png")));
            if name == "about" {
                ui.app.quit();
            }
        });
    };
    let (u, d) = (ui.clone(), dir.clone());
    glib::timeout_add_local_once(Duration::from_millis(5000), move || shot(&u, "main", d));
    let (u, d) = (ui.clone(), dir.clone());
    glib::timeout_add_local_once(Duration::from_millis(8000), move || {
        u.app.activate_action("preferences", None);
        shot(&u, "settings", d);
    });
    let (u, d) = (ui.clone(), dir);
    glib::timeout_add_local_once(Duration::from_millis(11000), move || {
        if let Some(dialog) = u.window.visible_dialog() {
            dialog.close();
        }
        u.app.activate_action("about", None);
        shot(&u, "about", d);
    });
}

fn build_ui(app: &adw::Application, hidden: bool) -> Rc<Ui> {
    fonts::register();
    style::install();
    let dir = paths::data_dir();
    let settings = Rc::new(RefCell::new(Settings::load_from(&dir)));
    joule_core::l::set_language(&settings.borrow().language);
    let service = Arc::new(Mutex::new(ReadingService::new(
        Registry::create_default(),
        settings.borrow().clone(),
        dir.clone(),
    )));

    // Header bar: refresh (a spinner while reading), the title, the main menu.
    let refresh_button = gtk::Button::from_icon_name("view-refresh-symbolic");
    refresh_button.set_tooltip_text(Some(&t("Atualizar", "Refresh")));
    refresh_button.set_action_name(Some("app.refresh"));
    let spinner = adw::Spinner::new();
    let refresh_stack = gtk::Stack::new();
    refresh_stack.add_named(&refresh_button, Some("button"));
    refresh_stack.add_named(&spinner, Some("spinner"));
    let menu = gio::Menu::new();
    let first = gio::Menu::new();
    first.append(
        Some(&t("Configurações", "Settings")),
        Some("app.preferences"),
    );
    first.append(
        Some(&t("Mandar meu aparelho", "Send my device")),
        Some("app.send-device"),
    );
    menu.append_section(None, &first);
    let second = gio::Menu::new();
    second.append(Some(&t("Sobre o Joule", "About Joule")), Some("app.about"));
    second.append(Some(&t("Sair", "Quit")), Some("app.quit"));
    menu.append_section(None, &second);
    let menu_button = gtk::MenuButton::builder()
        .icon_name("open-menu-symbolic")
        .menu_model(&menu)
        .tooltip_text(t("Menu principal", "Main menu"))
        .primary(true)
        .build();
    let (title, subtitle) = title_widget();
    let header = adw::HeaderBar::new();
    header.set_title_widget(Some(&title));
    header.pack_start(&refresh_stack);
    header.pack_end(&menu_button);

    let content = gtk::Box::new(gtk::Orientation::Vertical, 18);
    content.set_margin_top(12);
    content.set_margin_bottom(24);
    content.set_margin_start(12);
    content.set_margin_end(12);
    let clamp = adw::Clamp::builder()
        .maximum_size(560)
        .child(&content)
        .build();
    let scroll = gtk::ScrolledWindow::builder()
        .child(&clamp)
        .hscrollbar_policy(gtk::PolicyType::Never)
        .vexpand(true)
        .build();
    let toolbar = adw::ToolbarView::new();
    toolbar.add_top_bar(&header);
    toolbar.set_content(Some(&scroll));

    let window = adw::ApplicationWindow::builder()
        .application(app)
        .title(super::APP_NAME)
        .icon_name(APP_ID)
        .default_width(440)
        .default_height(600)
        .width_request(360)
        .height_request(300)
        .content(&toolbar)
        .build();

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
        content,
        subtitle,
        refresh_stack,
        others_open: Cell::new(false),
        tray: RefCell::new(None),
    });
    {
        let u = ui.clone();
        adw::StyleManager::default().connect_dark_notify(move |_| u.render());
    }

    // Closing hides the window while the top bar item lives (Joule keeps watching); without it, quits.
    let u = ui.clone();
    window.connect_close_request(move |w| {
        if u.tray.borrow().is_some() {
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
    let u = ui.clone();
    action("refresh", Box::new(move || u.refresh()));
    let u = ui.clone();
    action(
        "preferences",
        Box::new(move || {
            let u2 = u.clone();
            prefs::show(
                &u.window,
                u.settings.clone(),
                u.dir.clone(),
                Rc::new(move |e| u2.on_prefs(e)),
            );
        }),
    );
    let u = ui.clone();
    action("send-device", Box::new(move || u.send_device()));
    let u = ui.clone();
    action("about", Box::new(move || about::show(&u.window)));
    let a = app.clone();
    action("quit", Box::new(move || a.quit()));
    app.set_accels_for_action("app.refresh", &["F5", "<primary>r"]);
    app.set_accels_for_action("app.preferences", &["<primary>comma"]);
    app.set_accels_for_action("app.quit", &["<primary>q"]);
    app.set_accels_for_action("window.close", &["<primary>w", "Escape"]);

    // The top bar item and its commands.
    let (tx, rx) = async_channel::unbounded::<Command>();
    *ui.tray.borrow_mut() = tray::spawn(tx, ui.settings.borrow().listen_only);
    let u = ui.clone();
    glib::spawn_future_local(async move {
        while let Ok(cmd) = rx.recv().await {
            match cmd {
                Command::Toggle => u.toggle(),
                Command::Show => u.show(),
                Command::Refresh => u.refresh(),
                Command::ToggleListenOnly => {
                    {
                        let mut s = u.settings.borrow_mut();
                        s.listen_only = !s.listen_only;
                        s.save_to(&u.dir);
                    }
                    u.refresh();
                }
                Command::Quit => u.app.quit(),
            }
        }
    });

    ui.start_hotplug();
    ui.refresh();
    if !hidden || ui.tray.borrow().is_none() {
        ui.show();
    }
    if let Some(dir) = std::env::var_os("JOULE_SNAPSHOT") {
        run_snapshots(&ui, PathBuf::from(dir));
    }
    let _ = gdk::Display::default();
    ui
}

/// Runs the application. `--hidden` starts in the top bar without a window (autostart).
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
