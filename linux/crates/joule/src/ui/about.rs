//! About, in an `AdwAboutDialog`, with the credits of the Windows About page.

use adw::prelude::*;
use joule_core::l::t;

pub const REPOSITORY: &str = "https://github.com/DillanCidral/Joule";

/// (name, url) of the model tables and the protocol references the providers are built from.
pub fn tables() -> Vec<(&'static str, &'static str)> {
    vec![
        (
            "razer-battery-report",
            "https://github.com/xzeldon/razer-battery-report",
        ),
        ("OpenRazer", "https://github.com/openrazer/openrazer"),
        (
            "OpenMouse",
            "https://github.com/OpenMouse-Project/mouse-protocol",
        ),
    ]
}

pub fn protocols() -> Vec<(&'static str, &'static str)> {
    vec![
        ("Solaar", "https://github.com/pwr-Solaar/Solaar"),
        ("HeadsetControl", "https://github.com/Sapd/HeadsetControl"),
        (
            "OpenLinkHub",
            "https://github.com/jurkovic-nikola/OpenLinkHub",
        ),
        ("rivalcfg", "https://github.com/flozz/rivalcfg"),
        (
            "Pulsefire Dart RE",
            "https://github.com/santeri3700/hyperx_pulsefire_dart_reverse_engineering",
        ),
        (
            "RazerNariBatteryLevel",
            "https://github.com/Modzeleczek/RazerNariBatteryLevel",
        ),
    ]
}

fn entries(list: &[(&str, &str)]) -> Vec<String> {
    list.iter().map(|(n, u)| format!("{n} {u}")).collect()
}

pub fn show(parent: &impl IsA<gtk::Widget>) {
    let about = adw::AboutDialog::builder()
        .application_name("Joule")
        .application_icon("io.github.dillancidral.Joule")
        .version(env!("CARGO_PKG_VERSION"))
        .developer_name("Dillan Cidral")
        .website(REPOSITORY)
        .issue_url(format!("{REPOSITORY}/issues"))
        .license_type(gtk::License::Agpl30)
        .comments(t(
            "Bateria dos seus mouses, fones, controles e teclados sem fio. Lê sob demanda, nunca em laço, e só pergunta a uma marca depois que você permitir.",
            "Battery of your wireless mice, headsets, controllers and keyboards. Reads on demand, never in a loop, and asks a brand only after you allow it.",
        ))
        .build();
    let tables = entries(&tables());
    let protocols = entries(&protocols());
    about.add_credit_section(
        Some(&t("Tabelas de modelos", "Model tables")),
        &tables.iter().map(String::as_str).collect::<Vec<_>>(),
    );
    about.add_credit_section(
        Some(&t("Referências de protocolo", "Protocol references")),
        &protocols.iter().map(String::as_str).collect::<Vec<_>>(),
    );
    about.add_legal_section(
        "Geist",
        Some("© Vercel"),
        gtk::License::Custom,
        Some("SIL Open Font License 1.1: https://github.com/vercel/geist-font"),
    );
    about.add_legal_section("GTK, libadwaita", None, gtk::License::Lgpl21, None);
    about.add_legal_section(
        "ksni",
        None,
        gtk::License::Custom,
        Some("Unlicense: https://github.com/iovxw/ksni"),
    );
    about.present(Some(parent));
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn credits_match_the_windows_page() {
        assert_eq!(tables().len(), 3);
        assert_eq!(protocols().len(), 6);
        assert!(
            tables()
                .iter()
                .chain(protocols().iter())
                .all(|(_, u)| u.starts_with("https://"))
        );
        assert_eq!(entries(&[("A", "https://a")]), ["A https://a"]);
    }
}
