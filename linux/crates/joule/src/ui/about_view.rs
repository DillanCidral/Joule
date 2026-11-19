//! The About page (ported from the "Sobre" part of `PanelWindow.xaml`): version, source link,
//! components, model tables and protocol references.

use super::{
    icons,
    kit::{self, caption, group, group_label, hbox, label, rule, vbox},
};
use gtk::prelude::*;
use joule_core::l::t;
use std::rc::Rc;

pub const REPOSITORY: &str = "github.com/DillanCidral/Joule";

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Credit {
    pub name: String,
    pub usage: String,
    pub license: String,
    pub url: String,
}

fn credit(name: &str, usage: &str, license: &str, url: &str) -> Credit {
    Credit {
        name: name.into(),
        usage: usage.into(),
        license: license.into(),
        url: url.into(),
    }
}

/// What the Linux build stands on.
pub fn components() -> Vec<Credit> {
    vec![
        credit(
            "Rust",
            &t(
                "Base do app, vai compilada junto",
                "The app’s foundation, compiled in",
            ),
            "MIT / Apache-2.0",
            "https://www.rust-lang.org",
        ),
        credit(
            "GTK 4 + libadwaita",
            &t("A janela e os controles", "The window and the controls"),
            "LGPL-2.1+",
            "https://gitlab.gnome.org/GNOME/libadwaita",
        ),
        credit(
            "ksni",
            &t("O item da barra superior", "The top bar item"),
            "Unlicense",
            "https://github.com/iovxw/ksni",
        ),
        credit(
            "Geist 1.900",
            &t("Fonte do nome Joule", "Font for the Joule name"),
            "SIL OFL 1.1",
            "https://github.com/vercel/geist-font",
        ),
    ]
}

pub fn tables() -> Vec<Credit> {
    vec![
        credit(
            "razer-battery-report",
            &t("Mouses Razer", "Razer mice"),
            "MIT",
            "https://github.com/xzeldon/razer-battery-report",
        ),
        credit(
            "OpenRazer",
            &t("Teclados Razer", "Razer keyboards"),
            "GPL-2.0+",
            "https://github.com/openrazer/openrazer",
        ),
        credit(
            "OpenMouse",
            &t(
                "Attack Shark, ATK, Keychron e outras marcas",
                "Attack Shark, ATK, Keychron and other brands",
            ),
            "AGPL-3.0",
            "https://github.com/OpenMouse-Project/mouse-protocol",
        ),
    ]
}

pub fn protocols() -> Vec<Credit> {
    vec![
        credit(
            "Solaar",
            &t("Receptores Logitech", "Logitech receivers"),
            "",
            "https://github.com/pwr-Solaar/Solaar",
        ),
        credit(
            "HeadsetControl",
            &t(
                "Fones Logitech, SteelSeries, HyperX e Corsair",
                "Logitech, SteelSeries, HyperX and Corsair headsets",
            ),
            "",
            "https://github.com/Sapd/HeadsetControl",
        ),
        credit(
            "OpenLinkHub",
            &t("Receptor Corsair Slipstream", "Corsair Slipstream receiver"),
            "",
            "https://github.com/jurkovic-nikola/OpenLinkHub",
        ),
        credit(
            "rivalcfg",
            &t("Mouses SteelSeries", "SteelSeries mice"),
            "",
            "https://github.com/flozz/rivalcfg",
        ),
        credit(
            "Pulsefire Dart RE",
            "HyperX Pulsefire Dart",
            "",
            "https://github.com/santeri3700/hyperx_pulsefire_dart_reverse_engineering",
        ),
        credit(
            "RazerNariBatteryLevel",
            &t("Fone Razer Nari", "Razer Nari headset"),
            "",
            "https://github.com/Modzeleczek/RazerNariBatteryLevel",
        ),
    ]
}

/// One line of credits: name, use, license and the link icon. Rows of a group get a rule between them.
fn credit_row(c: &Credit, open: Rc<dyn Fn(&str)>) -> gtk::Widget {
    let b = kit::flat_button();
    let row = hbox(10);
    row.set_margin_start(14);
    row.set_margin_end(14);
    row.set_margin_top(12);
    row.set_margin_bottom(12);
    let texts = vbox(0);
    texts.set_hexpand(true);
    texts.append(&label(&c.name, &[]));
    if !c.usage.is_empty() {
        texts.append(&caption(&c.usage));
    }
    row.append(&texts);
    if !c.license.is_empty() {
        let l = label(&c.license, &["t-secondary"]);
        l.set_valign(gtk::Align::Center);
        l.add_css_class("joule-size-12");
        row.append(&l);
    }
    row.append(&icons::widget(icons::external(), 14, "t-tertiary"));
    b.set_child(Some(&row));
    let url = c.url.clone();
    b.connect_clicked(move |_| open(&url));
    b.upcast()
}

fn credit_group(list: &[Credit], open: &Rc<dyn Fn(&str)>) -> gtk::Box {
    let rows: Vec<gtk::Widget> = list.iter().map(|c| credit_row(c, open.clone())).collect();
    group(&rows)
}

pub struct AboutHandlers {
    pub open: Rc<dyn Fn(&str)>,
    pub licenses: Rc<dyn Fn()>,
}

pub fn build(back: &gtk::Button, h: AboutHandlers) -> gtk::Box {
    let root = vbox(0);
    root.append(&kit::page_header(&t("Sobre", "About"), back));
    let page = vbox(0);
    page.set_margin_start(8);
    page.set_margin_end(8);
    page.set_margin_bottom(8);

    // Version + update + source
    let version_row = hbox(10);
    version_row.set_margin_start(14);
    version_row.set_margin_end(14);
    version_row.set_margin_top(12);
    version_row.set_margin_bottom(12);
    let texts = vbox(0);
    texts.set_hexpand(true);
    texts.set_valign(gtk::Align::Center);
    texts.append(&label(
        &format!("Joule {}", env!("CARGO_PKG_VERSION")),
        &["joule-title"],
    ));
    texts.append(&caption(&t(
        "Código aberto, licença AGPL-3.0",
        "Open source, AGPL-3.0 license",
    )));
    version_row.append(&texts);
    // Linux has no auto-updater: the button opens the releases page.
    let update = kit::outline_button(&t("Ver versões no GitHub", "See releases on GitHub"));
    update.set_valign(gtk::Align::Center);
    let open = h.open.clone();
    update.connect_clicked(move |_| open(&format!("https://{REPOSITORY}/releases")));
    version_row.append(&update);
    let source = credit_row(
        &credit(
            &t("Ver o código no GitHub", "View the code on GitHub"),
            "",
            "",
            &format!("https://{REPOSITORY}"),
        ),
        h.open.clone(),
    );
    let first = vbox(0);
    first.add_css_class("joule-group");
    first.append(&version_row);
    first.append(&rule());
    first.append(&source);
    page.append(&first);

    let link = |title: &str, sub: &str, on: Rc<dyn Fn()>| {
        let b = kit::flat_button();
        let g = hbox(0);
        g.add_css_class("joule-group");
        g.set_margin_top(2);
        let inner = hbox(10);
        inner.set_margin_start(14);
        inner.set_margin_end(14);
        inner.set_margin_top(12);
        inner.set_margin_bottom(12);
        inner.set_hexpand(true);
        let texts = vbox(0);
        texts.set_hexpand(true);
        texts.append(&label(title, &[]));
        texts.append(&caption(sub));
        inner.append(&texts);
        inner.append(&icons::widget(icons::external(), 14, "t-tertiary"));
        g.append(&inner);
        b.set_child(Some(&g));
        b.connect_clicked(move |_| on());
        b
    };
    page.append(&group_label(&t("Componentes", "Components"), false));
    page.append(&credit_group(&components(), &h.open));
    page.append(&group_label(
        &t("Tabelas de modelos", "Model tables"),
        false,
    ));
    page.append(&credit_group(&tables(), &h.open));
    page.append(&group_label(
        &t("Referências de protocolo", "Protocol references"),
        false,
    ));
    page.append(&credit_group(&protocols(), &h.open));

    let licenses = link(
        &t("Ler as licenças completas", "Read the full licenses"),
        &t(
            "Abre o arquivo que vem instalado com o app",
            "Opens the file installed with the app",
        ),
        h.licenses.clone(),
    );
    licenses.set_margin_top(16);
    page.append(&licenses);

    let scroll = gtk::ScrolledWindow::builder()
        .child(&page)
        .hscrollbar_policy(gtk::PolicyType::Never)
        .propagate_natural_height(true)
        .max_content_height(kit::max_page_height())
        .vexpand(true)
        .build();
    scroll.vscrollbar().add_css_class("joule-scroll");
    root.append(&scroll);
    root
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn credits_are_complete_and_linked() {
        for list in [components(), tables(), protocols()] {
            assert!(!list.is_empty());
            for c in list {
                assert!(c.url.starts_with("https://"), "{c:?}");
                assert!(!c.name.is_empty());
            }
        }
        assert_eq!(tables().len(), 3);
        assert_eq!(protocols().len(), 6);
    }
    #[test]
    fn licenses_match_the_windows_page_for_the_tables() {
        let t = tables();
        assert_eq!(t[0].license, "MIT");
        assert_eq!(t[1].license, "GPL-2.0+");
        assert_eq!(t[2].license, "AGPL-3.0");
    }
}
