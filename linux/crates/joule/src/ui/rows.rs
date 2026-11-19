//! The rows of the device list: a Joule ring (level arc, glyph inside), name, a sub line and the value.
//! Built on libadwaita rows so they sit in a boxed list like any GNOME app.

use super::{
    icons,
    ring::{RingKind, RingSpec, ring_widget},
    theme::{Look, Palette},
};
use adw::prelude::*;
use joule_core::{
    DeviceKind,
    l::t,
    panel::{ConsentRowView, DeviceRowView, OtherRowView, ValueKind},
};

fn origin_label(origin: &str) -> String {
    match origin {
        "Cable" => t("Cabo", "Wired"),
        "Adapter" => t("Adaptador", "Adapter"),
        other => other.to_string(),
    }
}

/// "Dongle · 1 h ago · charging · below 20%": what the sub line says (pure, tested).
pub fn subtitle(v: &DeviceRowView, busy: bool) -> String {
    let reading = busy && !v.is_live;
    let mut parts: Vec<String> = Vec::new();
    if !v.origin.is_empty() {
        parts.push(origin_label(&v.origin));
    }
    if reading {
        parts.push(t("lendo…", "reading…"));
        return parts.join(" · ");
    }
    if v.is_live {
        parts.push(t("ao vivo", "live"));
    } else if let Some(age) = &v.age {
        parts.push(age.clone());
    }
    if let Some(c) = &v.charge_text {
        parts.push(c.clone());
    }
    if let Some(n) = &v.note {
        parts.push(n.clone());
    }
    parts.join(" · ")
}

/// The value at the end of the row: "58%", a band word, or "wired".
pub fn value_text(v: &DeviceRowView) -> (String, Option<String>) {
    match v.value_kind {
        ValueKind::Percent => (v.percent.to_string(), Some("%".into())),
        ValueKind::Band => (v.band_label.clone(), None),
        ValueKind::Cable => (t("no cabo", "wired"), None),
    }
}

pub fn ring_spec(v: &DeviceRowView, busy: bool) -> RingSpec {
    RingSpec {
        kind: match v.value_kind {
            ValueKind::Percent => RingKind::Percent(f64::from(v.percent) / 100.0),
            ValueKind::Band => RingKind::Band(v.band_count),
            ValueKind::Cable => RingKind::Cable,
        },
        low: v.is_low,
        stale: v.is_stale || (busy && !v.is_live),
        live: v.is_live,
        icon: icons::for_kind(v.kind),
    }
}

fn plain_row(title: &str, subtitle: &str) -> adw::ActionRow {
    let row = adw::ActionRow::new();
    row.set_use_markup(false);
    row.set_title(title);
    row.set_subtitle(subtitle);
    row.set_title_lines(1);
    row.set_subtitle_lines(2);
    row
}

pub fn device_row(v: &DeviceRowView, busy: bool, look: Look, pal: Palette) -> adw::ActionRow {
    let row = plain_row(&v.name, &subtitle(v, busy));
    row.add_css_class("joule-device");
    row.set_tooltip_text(Some(&v.tooltip));
    row.add_prefix(&ring_widget(ring_spec(v, busy), 40, look, pal));

    let (number, unit) = value_text(v);
    let value = gtk::Box::new(gtk::Orientation::Horizontal, 1);
    value.set_valign(gtk::Align::Center);
    let flag = |w: &gtk::Label| {
        if v.is_low {
            w.add_css_class("joule-low");
        }
        if v.is_stale || (busy && !v.is_live) {
            w.add_css_class("joule-stale");
        }
    };
    match v.value_kind {
        ValueKind::Band => {
            let chip = gtk::Label::new(Some(&number));
            chip.add_css_class("joule-chip");
            flag(&chip);
            value.append(&chip);
        }
        ValueKind::Cable => {
            let l = gtk::Label::new(Some(&number));
            l.add_css_class("dim-label");
            value.append(&icons::widget(icons::plug(), 14, "dim-label"));
            value.append(&l);
            value.set_spacing(6);
        }
        ValueKind::Percent => {
            let n = gtk::Label::new(Some(&number));
            n.add_css_class("joule-value");
            n.set_valign(gtk::Align::Baseline);
            flag(&n);
            value.append(&n);
            if let Some(u) = unit {
                let u = gtk::Label::new(Some(&u));
                u.add_css_class("joule-unit");
                u.set_valign(gtk::Align::Baseline);
                flag(&u);
                value.append(&u);
            }
        }
    }
    row.add_suffix(&value);
    row
}

fn glyph_prefix(kind: DeviceKind) -> gtk::DrawingArea {
    let g = icons::widget(icons::for_kind(kind), 20, "dim-label");
    g.set_margin_start(10);
    g.set_margin_end(10);
    g
}

pub fn consent_row(c: &ConsentRowView, on_allow: impl Fn() + 'static) -> adw::ActionRow {
    let row = plain_row(
        &c.name,
        &t(
            "Precisa de permissão para ler a bateria",
            "Needs permission to read the battery",
        ),
    );
    row.add_prefix(&glyph_prefix(c.kind));
    let allow = gtk::Button::with_label(&t("Permitir", "Allow"));
    allow.add_css_class("suggested-action");
    allow.add_css_class("pill");
    allow.set_valign(gtk::Align::Center);
    allow.connect_clicked(move |_| on_allow());
    row.add_suffix(&allow);
    row
}

pub fn other_row(o: &OtherRowView) -> adw::ActionRow {
    let row = plain_row(&o.name, &o.reason);
    row.add_prefix(&glyph_prefix(o.kind));
    row.add_css_class("dim-label");
    row
}

#[cfg(test)]
mod tests {
    use super::*;

    fn row() -> DeviceRowView {
        DeviceRowView {
            key: "k".into(),
            name: "G933".into(),
            kind: DeviceKind::Headset,
            origin: "Dongle".into(),
            value_kind: ValueKind::Percent,
            percent: 58,
            band_count: 0,
            band_label: String::new(),
            is_live: false,
            is_stale: true,
            is_low: false,
            age: Some("1 h ago".into()),
            charge_text: None,
            note: Some("headset off".into()),
            tooltip: String::new(),
        }
    }

    #[test]
    fn subtitle_joins_origin_age_and_note() {
        assert_eq!(subtitle(&row(), false), "Dongle · 1 h ago · headset off");
    }
    #[test]
    fn reading_replaces_the_rest() {
        let s = subtitle(&row(), true);
        assert!(s.starts_with("Dongle · "));
        assert!(s.ends_with("reading…") || s.ends_with("lendo…"));
    }
    #[test]
    fn live_shows_instead_of_age_and_charge_follows() {
        let mut r = row();
        r.is_live = true;
        r.note = None;
        r.charge_text = Some("charging".into());
        let s = subtitle(&r, false);
        assert!(s.ends_with("charging"));
        assert!(!s.contains("1 h ago"));
    }
    #[test]
    fn values_by_kind() {
        let mut r = row();
        assert_eq!(value_text(&r), ("58".into(), Some("%".into())));
        r.value_kind = ValueKind::Band;
        r.band_label = "Full".into();
        assert_eq!(value_text(&r), ("Full".into(), None));
    }
    #[test]
    fn busy_dims_the_ring_except_live() {
        let mut r = row();
        r.is_stale = false;
        assert!(ring_spec(&r, true).stale);
        r.is_live = true;
        assert!(!ring_spec(&r, true).stale);
    }
}
