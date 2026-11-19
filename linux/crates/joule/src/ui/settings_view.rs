//! The settings page (ported from the "Configurações" part of `PanelWindow.xaml` and `LoadSettings` /
//! `WireSettings`). Changes save at once and tell the window.

use super::{
    autostart,
    icons::{self},
    kit::{self, caption, group, group_label, hbox, label, rule, setting_row, vbox},
    theme,
};
use adw::prelude::*;
use gtk::glib;
use joule_core::{l::t, settings::Settings};
use std::{
    cell::{Cell, RefCell},
    rc::Rc,
};

/// What the settings page asks the window to do.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Event {
    /// Something was saved; the theme or the readings may need a refresh.
    Changed,
    /// The appearance (look) changed.
    LookChanged,
    NotifySample,
    Restart,
    SendDevice,
    OpenDataFolder,
    OpenAbout,
    WidgetChanged,
}

pub struct SettingsView {
    pub root: gtk::Box,
    settings: Rc<RefCell<Settings>>,
    dir: std::path::PathBuf,
    on: Rc<dyn Fn(Event)>,
    loading: Cell<bool>,
    // controls
    place: Vec<gtk::ToggleButton>,
    sound: gtk::Switch,
    alerts: gtk::Switch,
    threshold: gtk::Scale,
    threshold_value: gtk::Label,
    autostart: gtk::Switch,
    lang: Vec<gtk::ToggleButton>,
    lang_restart: gtk::Box,
    appearance: Vec<gtk::ToggleButton>,
    blur_block: gtk::Box,
    blur: gtk::Switch,
    blur_title: gtk::Label,
    blur_caption: gtk::Label,
    opacity_block: gtk::Box,
    opacity: gtk::Scale,
    opacity_value: gtk::Label,
    appearance_note: gtk::Label,
    widget: gtk::Switch,
    listen: gtk::Switch,
    about_text: gtk::Label,
    /// Language the running UI was built with, to offer "Reopen now".
    running_en: bool,
}

fn slider(min: f64, max: f64) -> gtk::Scale {
    let s = gtk::Scale::with_range(gtk::Orientation::Horizontal, min, max, 1.0);
    s.add_css_class("joule-scale");
    s.set_draw_value(false);
    s.set_round_digits(0);
    s.set_hexpand(true);
    s.set_focusable(false);
    s
}

fn value_label() -> gtk::Label {
    let l = label("", &["joule-title"]);
    l.set_xalign(1.0);
    l
}

/// "Windows" in the Joule texts becomes "System" on the desktop.
fn lang_is_en(choice: &str) -> bool {
    match choice {
        "en" => true,
        "pt" => false,
        _ => !std::env::var("LANG")
            .map(|v| v.starts_with("pt"))
            .unwrap_or(false),
    }
}

impl SettingsView {
    pub fn new(
        settings: Rc<RefCell<Settings>>,
        dir: std::path::PathBuf,
        on: Rc<dyn Fn(Event)>,
        back: &gtk::Button,
        running_en: bool,
        blur_supported: bool,
    ) -> Rc<Self> {
        let root = vbox(0);
        root.append(&kit::page_header(&t("Configurações", "Settings"), back));

        let page = vbox(0);
        page.set_margin_start(8);
        page.set_margin_end(8);
        page.set_margin_bottom(8);

        // ---- Notifications ----
        page.append(&group_label(&t("Avisos", "Notifications"), true));
        let place_box = vbox(0);
        place_box.set_margin_start(14);
        place_box.set_margin_end(14);
        place_box.set_margin_top(12);
        place_box.set_margin_bottom(12);
        place_box.append(&label(
            &t("Conectar e desconectar", "Connect and disconnect"),
            &[],
        ));
        place_box.append(&caption(&t(
            "Aparelho e bateria quando ele entra ou sai",
            "Device and battery when it connects or disconnects",
        )));
        let (place_track, place) = kit::segmented(
            &[
                &t("No widget", "On the widget"),
                &t("Do sistema", "Desktop"),
                &t("Desligado", "Off"),
            ],
            true,
            (4, 6),
        );
        place_track.set_margin_top(10);
        place_box.append(&place_track);

        let sound = kit::switch(false);
        let alerts = kit::switch(false);
        let threshold = slider(1.0, 99.0);
        let threshold_value = value_label();
        let thr_box = vbox(0);
        thr_box.set_margin_start(14);
        thr_box.set_margin_end(14);
        thr_box.set_margin_top(12);
        thr_box.set_margin_bottom(16);
        let thr_head = hbox(0);
        let thr_texts = vbox(0);
        thr_texts.set_hexpand(true);
        thr_texts.append(&label(
            &t("Limite de bateria baixa", "Low battery threshold"),
            &[],
        ));
        thr_texts.append(&caption(&t(
            "Abaixo disso a linha fica na cor de alerta",
            "Below this, the row turns to the alert color",
        )));
        thr_head.append(&thr_texts);
        threshold_value.set_valign(gtk::Align::Center);
        thr_head.append(&threshold_value);
        thr_box.append(&thr_head);
        threshold.set_margin_top(12);
        thr_box.append(&threshold);

        page.append(&group(&[
            place_box.upcast(),
            setting_row(
                &t("Tocar som", "Play sound"),
                Some(&t(
                    "Só quando a bateria é lida, e na bateria baixa",
                    "Only when the battery is read, and on low battery",
                )),
                &sound,
            )
            .upcast(),
            setting_row(
                &t("Avisar com bateria baixa", "Warn on low battery"),
                Some(&t(
                    "No mesmo lugar dos avisos, uma vez por aparelho",
                    "In the same place as notifications, once per device",
                )),
                &alerts,
            )
            .upcast(),
            thr_box.upcast(),
        ]));

        // ---- General ----
        page.append(&group_label(&t("Geral", "General"), false));
        let autostart_switch = kit::switch(false);
        let lang_box = vbox(0);
        lang_box.set_margin_start(14);
        lang_box.set_margin_end(14);
        lang_box.set_margin_top(12);
        lang_box.set_margin_bottom(12);
        lang_box.append(&label(&t("Idioma", "Language"), &[]));
        let (lang_track, lang) = kit::segmented(
            &[&t("Do sistema", "System"), "Português", "English"],
            true,
            (4, 6),
        );
        lang_track.set_margin_top(10);
        lang_box.append(&lang_track);
        let lang_restart = hbox(10);
        lang_restart.set_margin_top(10);
        let restart_text = caption(&t(
            "Vale quando o Joule abrir de novo.",
            "Takes effect when Joule opens again.",
        ));
        restart_text.set_hexpand(true);
        restart_text.set_valign(gtk::Align::Center);
        lang_restart.append(&restart_text);
        let restart_button = kit::outline_button(&t("Reabrir agora", "Reopen now"));
        restart_button.set_valign(gtk::Align::Center);
        lang_restart.append(&restart_button);
        lang_restart.set_visible(false);
        lang_box.append(&lang_restart);
        page.append(&group(&[
            setting_row(
                &t("Iniciar ao entrar", "Start at login"),
                None,
                &autostart_switch,
            )
            .upcast(),
            lang_box.upcast(),
        ]));

        // ---- Appearance ----
        page.append(&group_label(&t("Aparência", "Appearance"), false));
        let look_box = vbox(0);
        look_box.set_margin_start(14);
        look_box.set_margin_end(14);
        look_box.set_margin_top(12);
        look_box.set_margin_bottom(12);
        let (look_track, appearance) = kit::segmented(
            &[
                &t("Vidro tingido", "Tinted glass"),
                &t("Sistema", "System"),
                &t("Sólido", "Solid"),
            ],
            true,
            (4, 6),
        );
        look_box.append(&look_track);
        let blur_block = vbox(0);
        blur_block.set_margin_top(12);
        let blur = kit::switch(false);
        let blur_head = hbox(10);
        let blur_texts = vbox(0);
        blur_texts.set_hexpand(true);
        let blur_title = label(&t("Desfocar o fundo", "Blur the background"), &[]);
        blur_texts.append(&blur_title);
        let blur_caption = caption("");
        blur_texts.append(&blur_caption);
        blur_head.append(&blur_texts);
        blur.set_valign(gtk::Align::Center);
        blur_head.append(&blur);
        blur_block.append(&blur_head);
        look_box.append(&blur_block);
        let opacity_block = vbox(0);
        opacity_block.set_margin_top(12);
        let opacity_head = hbox(0);
        let opacity_title = label(&t("Opacidade da película", "Tint opacity"), &[]);
        opacity_title.set_hexpand(true);
        opacity_head.append(&opacity_title);
        let opacity_value = value_label();
        opacity_head.append(&opacity_value);
        opacity_block.append(&opacity_head);
        let opacity = slider(20.0, 95.0);
        opacity.set_margin_top(8);
        opacity_block.append(&opacity);
        look_box.append(&opacity_block);
        let appearance_note = caption("");
        appearance_note.set_margin_top(10);
        look_box.append(&appearance_note);
        page.append(&group(&[look_box.upcast()]));

        // ---- Widget ----
        page.append(&group_label(&t("Widget", "Widget"), false));
        let widget = kit::switch(false);
        page.append(&group(&[setting_row(
            &t("Mostrar widget", "Show widget"),
            Some(&t(
                "Uma janela pequena com um anel por aparelho. O Wayland não deixa fixar numa borda: arraste onde quiser",
                "A small window with a ring per device. Wayland can’t pin it to an edge: drag it anywhere",
            )),
            &widget,
        )
        .upcast()]));

        // ---- Reading ----
        page.append(&group_label(&t("Leitura", "Reading"), false));
        let listen = kit::switch(false);
        page.append(&group(&[setting_row(
            &t("Só escutar", "Listen only"),
            Some(&t(
                "Nenhuma pergunta é enviada aos aparelhos. Marcas que dependem de pergunta ficam sem bateria.",
                "No question is sent to devices. Brands that depend on a question show no battery.",
            )),
            &listen,
        )
        .upcast()]));

        // ---- Advanced ----
        page.append(&group_label(&t("Avançado", "Advanced"), false));
        let link = |title: &str,
                    sub: &str,
                    left: Option<icons::Icon>,
                    right: icons::Icon,
                    ev: Event,
                    on: Rc<dyn Fn(Event)>| {
            let b = kit::flat_button();
            let g = hbox(12);
            g.add_css_class("joule-group");
            g.set_margin_top(2);
            let inner = hbox(12);
            inner.set_margin_start(14);
            inner.set_margin_end(14);
            inner.set_margin_top(12);
            inner.set_margin_bottom(12);
            inner.set_hexpand(true);
            if let Some(l) = left {
                inner.append(&icons::widget(l, 18, "t-secondary"));
            }
            let texts = vbox(0);
            texts.set_hexpand(true);
            texts.append(&label(title, &[]));
            texts.append(&caption(sub));
            inner.append(&texts);
            inner.append(&icons::widget(right, 14, "t-tertiary"));
            g.append(&inner);
            b.set_child(Some(&g));
            b.connect_clicked(move |_| on(ev));
            b
        };
        let first = link(
            &t("Mandar meu aparelho", "Send my device"),
            &t(
                "Algum aparelho não aparece? Mande os dados dele",
                "A device is missing? Send its details",
            ),
            None,
            icons::external(),
            Event::SendDevice,
            on.clone(),
        );
        if let Some(c) = first.child() {
            c.set_margin_top(0);
        }
        page.append(&first);
        page.append(&link(
            &t("Abrir pasta de dados", "Open data folder"),
            &t("Log e cache das leituras", "Log and reading cache"),
            Some(icons::folder()),
            icons::external(),
            Event::OpenDataFolder,
            on.clone(),
        ));
        page.append(&link(
            &t("Sobre e licenças", "About and licenses"),
            &t(
                "Código aberto e os projetos de que o Joule depende",
                "Open source and the projects Joule relies on",
            ),
            Some(icons::info()),
            icons::chevron_right(),
            Event::OpenAbout,
            on.clone(),
        ));

        let scroll = gtk::ScrolledWindow::builder()
            .child(&page)
            .hscrollbar_policy(gtk::PolicyType::Never)
            .propagate_natural_height(true)
            .max_content_height(kit::max_page_height())
            .vexpand(true)
            .build();
        scroll.vscrollbar().add_css_class("joule-scroll");
        root.append(&scroll);

        let footer = hbox(0);
        footer.add_css_class("joule-footer");
        let about_text = caption("");
        about_text.set_margin_start(16);
        about_text.set_margin_end(16);
        about_text.set_margin_top(10);
        about_text.set_margin_bottom(12);
        footer.append(&about_text);
        root.append(&footer);

        let view = Rc::new(Self {
            root,
            settings,
            dir,
            on,
            loading: Cell::new(false),
            place,
            sound,
            alerts,
            threshold,
            threshold_value,
            autostart: autostart_switch,
            lang,
            lang_restart,
            appearance,
            blur_block,
            blur,
            blur_title,
            blur_caption,
            opacity_block,
            opacity,
            opacity_value,
            appearance_note,
            widget,
            listen,
            about_text,
            running_en,
        });
        view.wire(&restart_button, blur_supported);
        view
    }

    /// Applies a change, saves, tells the window and reloads the controls.
    fn changed(self: &Rc<Self>, apply: impl FnOnce(&mut Settings), event: Event) {
        if self.loading.get() {
            return;
        }
        apply(&mut self.settings.borrow_mut());
        self.settings.borrow().save_to(&self.dir);
        (self.on)(event);
        self.load(self.blur_supported_cell());
    }

    fn blur_supported_cell(&self) -> bool {
        self.blur.is_sensitive()
    }

    fn wire(self: &Rc<Self>, restart: &gtk::Button, blur_supported: bool) {
        let v = self.clone();
        self.alerts.connect_state_set(move |_, on| {
            v.changed(move |s| s.alerts_enabled = on, Event::Changed);
            glib::Propagation::Proceed
        });
        for (i, b) in self.place.iter().enumerate() {
            let v = self.clone();
            b.connect_toggled(move |b| {
                if !b.is_active() || v.loading.get() {
                    return;
                }
                let place = ["widget", "windows", "off"][i];
                v.changed(move |s| s.notify_place = place.into(), Event::Changed);
                if place != "off" {
                    (v.on)(Event::NotifySample);
                }
            });
        }
        let v = self.clone();
        self.sound.connect_state_set(move |_, on| {
            v.changed(move |s| s.notify_sound = on, Event::Changed);
            glib::Propagation::Proceed
        });
        let v = self.clone();
        self.listen.connect_state_set(move |_, on| {
            v.changed(move |s| s.listen_only = on, Event::Changed);
            glib::Propagation::Proceed
        });
        let v = self.clone();
        self.blur.connect_state_set(move |_, on| {
            v.changed(
                move |s| {
                    s.glass_blur = on;
                    if on {
                        s.glass_opacity = s.glass_opacity.min(30); // a high tint hides the blur
                    }
                },
                Event::LookChanged,
            );
            glib::Propagation::Proceed
        });
        let v = self.clone();
        self.threshold.connect_value_changed(move |s| {
            let value = s.value().round() as i32;
            v.changed(move |st| st.low_battery_threshold = value, Event::Changed);
        });
        let v = self.clone();
        self.autostart.connect_state_set(move |_, on| {
            if !v.loading.get() {
                autostart::set(on);
            }
            glib::Propagation::Proceed
        });
        for (i, b) in self.lang.iter().enumerate() {
            let v = self.clone();
            b.connect_toggled(move |b| {
                if !b.is_active() || v.loading.get() {
                    return;
                }
                let code = ["auto", "pt", "en"][i];
                v.changed(move |s| s.language = code.into(), Event::Changed);
            });
        }
        let v = self.clone();
        restart.connect_clicked(move |_| (v.on)(Event::Restart));
        for (i, b) in self.appearance.iter().enumerate() {
            let v = self.clone();
            b.connect_toggled(move |b| {
                if !b.is_active() || v.loading.get() {
                    return;
                }
                let a = [
                    theme::Appearance::Glass,
                    theme::Appearance::System,
                    theme::Appearance::Solid,
                ][i];
                v.changed(
                    move |s| s.appearance = theme::to_setting(a).into(),
                    Event::LookChanged,
                );
            });
        }
        let v = self.clone();
        self.opacity.connect_value_changed(move |s| {
            let value = s.value().round() as i32;
            v.changed(move |st| st.glass_opacity = value, Event::LookChanged);
        });
        let v = self.clone();
        self.widget.connect_state_set(move |_, on| {
            v.changed(move |s| s.widget_enabled = on, Event::WidgetChanged);
            glib::Propagation::Proceed
        });
        self.blur.set_sensitive(blur_supported);
    }

    /// Puts the saved values on the controls (`LoadSettings`).
    pub fn load(&self, blur_supported: bool) {
        self.loading.set(true);
        let s = self.settings.borrow().clone();
        self.alerts.set_active(s.alerts_enabled);
        for (b, p) in self.place.iter().zip(["widget", "windows", "off"]) {
            b.set_active(s.notify_place == p);
        }
        self.sound.set_active(s.notify_sound);
        self.listen.set_active(s.listen_only);
        self.threshold.set_value(f64::from(s.low_battery_threshold));
        self.threshold_value
            .set_text(&format!("{}%", s.low_battery_threshold));
        self.autostart.set_active(autostart::is_enabled());
        for (b, c) in self.lang.iter().zip(["auto", "pt", "en"]) {
            b.set_active(if c == "auto" {
                !matches!(s.language.as_str(), "pt" | "en")
            } else {
                s.language == c
            });
        }
        self.lang_restart
            .set_visible(lang_is_en(&s.language) != self.running_en);

        let requested = theme::parse(&s.appearance);
        for (b, a) in self.appearance.iter().zip([
            theme::Appearance::Glass,
            theme::Appearance::System,
            theme::Appearance::Solid,
        ]) {
            b.set_active(requested == a);
        }
        let glass = requested == theme::Appearance::Glass;
        self.opacity_block.set_visible(glass);
        self.blur_block.set_visible(glass);
        self.blur.set_sensitive(blur_supported);
        self.blur.set_active(s.glass_blur && blur_supported);
        self.blur_title
            .set_opacity(if blur_supported { 1.0 } else { 0.5 });
        self.blur_caption.set_text(&if blur_supported {
            t(
                "O que fica atrás do painel aparece borrado",
                "What’s behind the panel shows blurred",
            )
        } else {
            t(
                "O GNOME não borra o que fica atrás de uma janela de app",
                "GNOME can’t blur what is behind an app window",
            )
        });
        self.opacity.set_value(f64::from(s.glass_opacity));
        self.opacity_value
            .set_text(&format!("{}%", s.glass_opacity));
        self.appearance_note.set_text(&if !glass {
            t(
                "O vidro tingido funciona em qualquer desktop; o Sistema usa a cor de destaque do GNOME.",
                "Tinted glass works on any desktop; System uses the GNOME accent color.",
            )
        } else if blur_supported && s.glass_blur {
            t(
                "Com desfoque dá para baixar bem a película sem o texto de trás atrapalhar.",
                "With blur you can lower the tint a lot without the text behind getting in the way.",
            )
        } else {
            t(
                "Sem desfoque o vidro só escurece o fundo. Se atrapalhar a leitura, suba a película.",
                "Without blur the glass only darkens the background. If it gets hard to read, raise the tint.",
            )
        });
        self.widget.set_active(s.widget_enabled);
        self.about_text
            .set_text(&format!("Joule {}", env!("CARGO_PKG_VERSION")));
        self.loading.set(false);
    }
}

#[allow(dead_code)]
fn _keep(_: &gtk::Box) {
    let _ = rule();
}
