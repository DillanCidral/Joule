//! Settings in an `AdwPreferencesDialog`. Each change saves at once and tells the window.

use super::autostart;
use adw::prelude::*;
use joule_core::{l::t, settings::Settings};
use std::{
    cell::RefCell,
    path::{Path, PathBuf},
    rc::Rc,
};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Event {
    /// Saved; the next reading uses the new values.
    Changed,
    /// Listen only or the allowed brands changed: read again.
    ReadAgain,
    Restart,
    OpenDataFolder,
    SendDevice,
}

const LANGS: [&str; 3] = ["auto", "pt", "en"];

fn lang_index(code: &str) -> u32 {
    LANGS.iter().position(|c| *c == code).unwrap_or(0) as u32
}

fn save(settings: &Rc<RefCell<Settings>>, dir: &Path, apply: impl FnOnce(&mut Settings)) {
    apply(&mut settings.borrow_mut());
    settings.borrow().save_to(dir);
}

fn switch_row(title: &str, subtitle: Option<&str>, active: bool) -> adw::SwitchRow {
    let r = adw::SwitchRow::builder()
        .title(title)
        .active(active)
        .build();
    r.set_use_markup(false);
    if let Some(s) = subtitle {
        r.set_subtitle(s);
    }
    r
}

pub fn show(
    parent: &impl IsA<gtk::Widget>,
    settings: Rc<RefCell<Settings>>,
    dir: PathBuf,
    on: Rc<dyn Fn(Event)>,
) {
    let s = settings.borrow().clone();
    let dialog = adw::PreferencesDialog::new();
    dialog.set_title(&t("Configurações", "Settings"));
    dialog.set_search_enabled(false);
    let page = adw::PreferencesPage::new();
    dialog.add(&page);

    // ---- General ----
    let general = adw::PreferencesGroup::builder()
        .title(t("Geral", "General"))
        .build();
    let login = switch_row(
        &t("Iniciar ao entrar", "Start at login"),
        Some(&t("Só no ícone da barra superior", "In the top bar only")),
        autostart::is_enabled(),
    );
    login.connect_active_notify(|r| autostart::set(r.is_active()));
    general.add(&login);
    let lang = adw::ComboRow::builder()
        .title(t("Idioma", "Language"))
        .subtitle(t(
            "Vale quando o Joule abrir de novo",
            "Takes effect when Joule opens again",
        ))
        .model(&gtk::StringList::new(&[
            &t("Do sistema", "System"),
            "Português",
            "English",
        ]))
        .selected(lang_index(&s.language))
        .build();
    {
        let (settings, dir, on, dialog) =
            (settings.clone(), dir.clone(), on.clone(), dialog.clone());
        lang.connect_selected_notify(move |r| {
            let code = LANGS[(r.selected() as usize).min(2)];
            save(&settings, &dir, |s| s.language = code.into());
            on(Event::Changed);
            let toast = adw::Toast::builder()
                .title(t(
                    "O idioma muda ao reabrir",
                    "The language changes when Joule reopens",
                ))
                .button_label(t("Reabrir agora", "Reopen now"))
                .build();
            let on = on.clone();
            toast.connect_button_clicked(move |_| on(Event::Restart));
            dialog.add_toast(toast);
        });
    }
    general.add(&lang);
    page.add(&general);

    // ---- Notifications ----
    let notices = adw::PreferencesGroup::builder()
        .title(t("Avisos", "Notifications"))
        .build();
    let connect = switch_row(
        &t("Avisar ao conectar", "Notify on connect"),
        Some(&t(
            "Aparelho e bateria quando ele entra",
            "Device and battery when it connects",
        )),
        s.notify_place != "off",
    );
    {
        let (settings, dir, on) = (settings.clone(), dir.clone(), on.clone());
        connect.connect_active_notify(move |r| {
            let on_ = r.is_active();
            save(&settings, &dir, |s| {
                s.notify_place = if on_ { "windows".into() } else { "off".into() }
            });
            on(Event::Changed);
        });
    }
    notices.add(&connect);
    let sound = switch_row(
        &t("Tocar som", "Play sound"),
        Some(&t(
            "Ao conectar e na bateria baixa",
            "On connect and on low battery",
        )),
        s.notify_sound,
    );
    {
        let (settings, dir, on) = (settings.clone(), dir.clone(), on.clone());
        sound.connect_active_notify(move |r| {
            let v = r.is_active();
            save(&settings, &dir, |s| s.notify_sound = v);
            on(Event::Changed);
        });
    }
    notices.add(&sound);
    let low = switch_row(
        &t("Avisar com bateria baixa", "Warn on low battery"),
        Some(&t("Uma vez por aparelho", "Once per device")),
        s.alerts_enabled,
    );
    {
        let (settings, dir, on) = (settings.clone(), dir.clone(), on.clone());
        low.connect_active_notify(move |r| {
            let v = r.is_active();
            save(&settings, &dir, |s| s.alerts_enabled = v);
            on(Event::Changed);
        });
    }
    notices.add(&low);
    let threshold = adw::SpinRow::with_range(1.0, 99.0, 1.0);
    threshold.set_title(&t(
        "Limite de bateria baixa (%)",
        "Low battery threshold (%)",
    ));
    threshold.set_subtitle(&t(
        "Abaixo disso o aparelho fica vermelho",
        "Below this the device turns red",
    ));
    threshold.set_value(f64::from(s.low_battery_threshold));
    {
        let (settings, dir, on) = (settings.clone(), dir.clone(), on.clone());
        threshold.connect_value_notify(move |r| {
            let v = r.value().round() as i32;
            save(&settings, &dir, |s| s.low_battery_threshold = v);
            on(Event::Changed);
        });
    }
    notices.add(&threshold);
    page.add(&notices);

    // ---- Reading ----
    let reading = adw::PreferencesGroup::builder()
        .title(t("Leitura", "Reading"))
        .description(t(
            "O Joule só pergunta a bateria a uma marca depois que você permitir.",
            "Joule asks a brand for its battery only after you allow it.",
        ))
        .build();
    let listen = switch_row(
        &t("Só escutar", "Listen only"),
        Some(&t(
            "Nenhuma pergunta é enviada aos aparelhos. Marcas que dependem de pergunta ficam sem bateria.",
            "No question is sent to devices. Brands that depend on a question show no battery.",
        )),
        s.listen_only,
    );
    {
        let (settings, dir, on) = (settings.clone(), dir.clone(), on.clone());
        listen.connect_active_notify(move |r| {
            let v = r.is_active();
            save(&settings, &dir, |s| s.listen_only = v);
            on(Event::ReadAgain);
        });
    }
    reading.add(&listen);
    let brands = adw::ExpanderRow::builder()
        .title(t("Marcas permitidas", "Allowed brands"))
        .subtitle(if s.ask_allowed_vids.is_empty() {
            t("Nenhuma ainda", "None yet")
        } else {
            s.ask_allowed_vids.join(", ")
        })
        .build();
    brands.set_use_markup(false);
    for vid in s.ask_allowed_vids.clone() {
        let row = adw::ActionRow::builder()
            .title(brand_name(&vid))
            .subtitle(format!("VID {vid}"))
            .build();
        row.set_use_markup(false);
        let remove = gtk::Button::from_icon_name("user-trash-symbolic");
        remove.add_css_class("flat");
        remove.set_valign(gtk::Align::Center);
        remove.set_tooltip_text(Some(&t("Tirar a permissão", "Remove the permission")));
        let (settings, dir, on, brands_, row_) = (
            settings.clone(),
            dir.clone(),
            on.clone(),
            brands.clone(),
            row.clone(),
        );
        remove.connect_clicked(move |_| {
            save(&settings, &dir, |s| {
                s.ask_allowed_vids.retain(|v| !v.eq_ignore_ascii_case(&vid))
            });
            brands_.remove(&row_);
            on(Event::ReadAgain);
        });
        row.add_suffix(&remove);
        brands.add_row(&row);
    }
    reading.add(&brands);
    page.add(&reading);

    // ---- Advanced ----
    let advanced = adw::PreferencesGroup::builder()
        .title(t("Avançado", "Advanced"))
        .build();
    let link = |title: &str, sub: &str, icon: &str, ev: Event| {
        let row = adw::ActionRow::builder()
            .title(title)
            .subtitle(sub)
            .activatable(true)
            .build();
        row.set_use_markup(false);
        row.add_suffix(&gtk::Image::from_icon_name(icon));
        let on = on.clone();
        row.connect_activated(move |_| on(ev));
        row
    };
    advanced.add(&link(
        &t("Mandar meu aparelho", "Send my device"),
        &t(
            "Algum aparelho não aparece? Mande os dados dele pelo GitHub",
            "A device is missing? Send its details on GitHub",
        ),
        "adw-external-link-symbolic",
        Event::SendDevice,
    ));
    advanced.add(&link(
        &t("Abrir pasta de dados", "Open data folder"),
        &t("Log e cache das leituras", "Log and reading cache"),
        "folder-open-symbolic",
        Event::OpenDataFolder,
    ));
    page.add(&advanced);

    dialog.present(Some(parent));
}

/// A friendly name for the VIDs Joule knows; the hex otherwise.
pub fn brand_name(vid: &str) -> String {
    match vid.to_uppercase().as_str() {
        "046D" => "Logitech".into(),
        "1532" => "Razer".into(),
        "1038" => "SteelSeries".into(),
        "0951" | "03F0" => "HyperX".into(),
        "1B1C" => "Corsair".into(),
        "054C" => "Sony".into(),
        other => other.to_string(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn language_index_round_trip() {
        for (i, c) in LANGS.iter().enumerate() {
            assert_eq!(lang_index(c), i as u32);
        }
        assert_eq!(lang_index("xx"), 0);
    }
    #[test]
    fn known_brands_have_names() {
        assert_eq!(brand_name("046d"), "Logitech");
        assert_eq!(brand_name("ABCD"), "ABCD");
    }
}
