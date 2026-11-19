//! The widget (ported from `WidgetWindow.xaml`): a column of rings, one per device, that opens on
//! hover into a list with name, pace and value, a Refresh button and "Open panel". On GNOME/Wayland
//! an app cannot pin a window to a screen edge, so it floats and can be dragged anywhere.

use super::{
    icons,
    kit::{self, clear, hbox, label, vbox},
    panel_view::{clock_text, kind_icon},
    ring::{RingKind, RingSpec, ring_widget},
    theme::{Look, Palette},
};
use gtk::prelude::*;
use joule_core::{
    DeviceEntry,
    l::t,
    panel::{self, DeviceRowView, ValueKind},
};
use std::{cell::Cell, rc::Rc};

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Action {
    Refresh,
    OpenPanel,
}

/// The text under (collapsed) or beside (expanded) a ring.
pub fn value_text(r: &DeviceRowView) -> String {
    match r.value_kind {
        ValueKind::Percent => format!("{}%", r.percent),
        ValueKind::Band => r.band_label.clone(),
        ValueKind::Cable => t("no cabo", "wired"),
    }
}

/// "Dongle · 2 min ago" style meta line of an expanded row.
pub fn meta_text(r: &DeviceRowView) -> String {
    let mut parts: Vec<String> = Vec::new();
    if r.is_live {
        parts.push(t("ao vivo", "live"));
    } else if let Some(age) = &r.age {
        parts.push(age.clone());
    }
    if let Some(c) = &r.charge_text {
        parts.push(c.clone());
    }
    if parts.is_empty() && !r.origin.is_empty() {
        parts.push(r.origin.clone());
    }
    parts.join(" · ")
}

fn ring_spec(r: &DeviceRowView) -> RingSpec {
    RingSpec {
        kind: match r.value_kind {
            ValueKind::Percent => RingKind::Percent(f64::from(r.percent) / 100.0),
            ValueKind::Band => RingKind::Band(r.band_count),
            ValueKind::Cable => RingKind::Cable,
        },
        low: r.is_low,
        stale: r.is_stale,
        live: r.is_live,
        icon: kind_icon(r.kind),
    }
}

pub struct WidgetView {
    pub root: gtk::Box,
    collapsed: gtk::Box,
    expanded: gtk::Box,
    rings: gtk::Box,
    rows: gtk::Box,
    read_age: gtk::Label,
    expanded_now: Cell<bool>,
}

impl WidgetView {
    pub fn new(on: Rc<dyn Fn(Action)>) -> Rc<Self> {
        let root = vbox(0);
        root.add_css_class("joule-widget");

        // Collapsed: one ring per device, value under it. Margin 8,10.
        let collapsed = vbox(0);
        collapsed.set_margin_start(8);
        collapsed.set_margin_end(8);
        collapsed.set_margin_top(10);
        collapsed.set_margin_bottom(10);
        let rings = vbox(8);
        collapsed.append(&rings);
        root.append(&collapsed);

        // Expanded: 268 wide.
        let expanded = vbox(0);
        expanded.set_size_request(268, -1);
        expanded.set_visible(false);
        let head = hbox(0);
        head.set_margin_start(10);
        head.set_margin_end(8);
        head.set_margin_top(8);
        head.set_margin_bottom(6);
        let title = label(crate::ui::APP_NAME, &["t-secondary"]);
        title.add_css_class("joule-widget-title");
        title.set_hexpand(true);
        title.set_valign(gtk::Align::Center);
        head.append(&title);
        let read_age = label("", &["joule-caption", "joule-size-11"]);
        read_age.set_margin_end(8);
        read_age.set_valign(gtk::Align::Center);
        head.append(&read_age);
        let refresh = gtk::Button::new();
        refresh.add_css_class("joule-icon");
        refresh.add_css_class("joule-small-icon");
        refresh.set_child(Some(&icons::widget(icons::refresh(), 14, "t-primary")));
        refresh.set_tooltip_text(Some(&t(
            "Atualizar os aparelhos sob demanda",
            "Refresh devices on demand",
        )));
        refresh.set_focusable(false);
        head.append(&refresh);
        expanded.append(&head);
        let rows = vbox(0);
        rows.set_margin_start(8);
        rows.set_margin_end(8);
        expanded.append(&rows);
        let open = gtk::Button::new();
        open.add_css_class("joule-outline");
        open.set_child(Some(&label(
            &t("Abrir painel", "Open panel"),
            &["t-secondary"],
        )));
        open.set_margin_start(8);
        open.set_margin_end(8);
        open.set_margin_top(4);
        open.set_margin_bottom(8);
        open.set_focusable(false);
        expanded.append(&open);
        root.append(&expanded);

        let on2 = on.clone();
        refresh.connect_clicked(move |_| on2(Action::Refresh));
        open.connect_clicked(move |_| on(Action::OpenPanel));

        let view = Rc::new(Self {
            root,
            collapsed,
            expanded,
            rings,
            rows,
            read_age,
            expanded_now: Cell::new(false),
        });

        // Hover: the mouse resting on the widget opens it, leaving closes it.
        let motion = gtk::EventControllerMotion::new();
        let v = view.clone();
        motion.connect_enter(move |_, _, _| v.set_expanded(true));
        let v = view.clone();
        motion.connect_leave(move |_| v.set_expanded(false));
        view.root.add_controller(motion);
        view
    }

    pub fn set_expanded(&self, on: bool) {
        self.expanded_now.set(on);
        self.expanded.set_visible(on);
        self.collapsed.set_visible(!on);
    }

    pub fn render(
        &self,
        devices: &[DeviceEntry],
        when_unix: i64,
        threshold: i32,
        now_unix: i64,
        look: Look,
        pal: Palette,
    ) {
        let rows = panel::main_rows(devices, threshold, now_unix, when_unix);
        clear(&self.rings);
        clear(&self.rows);
        for r in &rows {
            // collapsed
            let cell = vbox(2);
            cell.set_tooltip_text(Some(&r.tooltip));
            cell.append(&ring_widget(ring_spec(r), look, pal));
            let v = label(
                &value_text(r),
                &[
                    "joule-size-10",
                    if r.is_low { "t-alert" } else { "t-primary" },
                ],
            );
            v.set_halign(gtk::Align::Center);
            cell.append(&v);
            self.rings.append(&cell);

            // expanded row: ring, name + meta, value
            let row = hbox(10);
            row.add_css_class("joule-widget-row");
            row.set_tooltip_text(Some(&r.tooltip));
            row.append(&ring_widget(ring_spec(r), look, pal));
            let texts = vbox(0);
            texts.set_hexpand(true);
            texts.set_valign(gtk::Align::Center);
            let name = label(&r.name, &[]);
            name.set_ellipsize(gtk::pango::EllipsizeMode::End);
            name.set_max_width_chars(20);
            texts.append(&name);
            let meta = label(
                &meta_text(r),
                &[
                    "joule-size-11",
                    if r.is_stale {
                        "t-secondary"
                    } else {
                        "t-tertiary"
                    },
                ],
            );
            meta.set_ellipsize(gtk::pango::EllipsizeMode::End);
            meta.set_max_width_chars(26);
            texts.append(&meta);
            row.append(&texts);
            let value = label(
                &value_text(r),
                &[
                    "joule-size-14",
                    if r.is_low { "t-alert" } else { "t-primary" },
                ],
            );
            value.set_margin_start(8);
            value.set_valign(gtk::Align::Center);
            value.set_opacity(if r.is_stale { 0.5 } else { 1.0 });
            row.append(&value);
            self.rows.append(&row);
        }
        if rows.is_empty() {
            let none = kit::caption(&t("Nenhum aparelho com bateria", "No device with battery"));
            none.set_margin_start(10);
            none.set_margin_end(10);
            none.set_margin_top(4);
            none.set_margin_bottom(4);
            self.rows.append(&none);
            let dot = icons::widget(icons::plug(), 20, "t-tertiary");
            dot.set_margin_start(6);
            dot.set_margin_end(6);
            self.rings.append(&dot);
        }
        self.read_age.set_text(&if when_unix > 0 {
            clock_text(when_unix)
        } else {
            String::new()
        });
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn row() -> DeviceRowView {
        DeviceRowView {
            key: "k".into(),
            name: "G933".into(),
            kind: joule_core::DeviceKind::Headset,
            origin: "Dongle".into(),
            value_kind: ValueKind::Percent,
            percent: 58,
            band_count: 0,
            band_label: String::new(),
            is_live: false,
            is_stale: false,
            is_low: false,
            age: Some("2 min ago".into()),
            charge_text: None,
            note: None,
            tooltip: String::new(),
        }
    }

    #[test]
    fn value_text_by_kind() {
        let mut r = row();
        assert_eq!(value_text(&r), "58%");
        r.value_kind = ValueKind::Band;
        r.band_label = "Full".into();
        assert_eq!(value_text(&r), "Full");
    }
    #[test]
    fn meta_shows_live_or_age_and_charge() {
        let mut r = row();
        assert_eq!(meta_text(&r), "2 min ago");
        r.charge_text = Some("charging".into());
        assert_eq!(meta_text(&r), "2 min ago · charging");
        r.is_live = true;
        assert!(meta_text(&r).ends_with("charging"));
        r.age = None;
        r.is_live = false;
        r.charge_text = None;
        assert_eq!(meta_text(&r), "Dongle");
    }
    #[test]
    fn ring_kinds_follow_the_value() {
        let mut r = row();
        assert_eq!(ring_spec(&r).kind, RingKind::Percent(0.58));
        r.value_kind = ValueKind::Band;
        r.band_count = 2;
        assert_eq!(ring_spec(&r).kind, RingKind::Band(2));
        r.value_kind = ValueKind::Cable;
        assert_eq!(ring_spec(&r).kind, RingKind::Cable);
    }
}
