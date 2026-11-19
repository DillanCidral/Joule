//! The list view of the panel (ported from the "Lista" part of `PanelWindow.xaml` and its code-behind):
//! header with the name and the actions, device cards, the empty state, permission rows, "show others"
//! and the footer.

use super::{
    bar::{self, BarKind, BarSpec},
    icons::{self, Icon},
    kit::{self, clear, hbox, label, vbox},
    theme::{Look, Palette, track_color},
};
use adw::prelude::*;
use gtk::{cairo, glib};
use joule_core::{
    DeviceEntry, DeviceKind,
    l::t,
    panel::{self, ConsentRowView, DeviceRowView, OtherRowView, ValueKind},
};
use std::{cell::Cell, rc::Rc};

/// What the list view asks the window to do.
#[derive(Clone, Debug, PartialEq, Eq)]
pub enum Action {
    Refresh,
    OpenSettings,
    Allow(String),
}

pub struct PanelInput<'a> {
    pub devices: &'a [DeviceEntry],
    pub when_unix: i64,
    pub fatal_error: Option<&'a str>,
    pub busy: bool,
    pub threshold: i32,
    pub now_unix: i64,
    /// VID (hex) of a brand waiting for consent, by hardware id.
    pub waiting_vid: &'a dyn Fn(&str) -> Option<String>,
    pub look: Look,
    pub pal: Palette,
}

pub struct PanelView {
    pub root: gtk::Box,
    on_action: Rc<dyn Fn(Action)>,
    title_sub: gtk::Label,
    reading_pill: gtk::Box,
    refresh_button: gtk::Button,
    empty: gtk::Box,
    empty_icon: gtk::Box,
    empty_title: gtk::Label,
    empty_body: gtk::Label,
    empty_refresh: gtk::Button,
    consent_rows: gtk::Box,
    rows: gtk::Box,
    others_block: gtk::Box,
    others_label: gtk::Label,
    chevron_angle: Rc<Cell<f64>>,
    chevron: gtk::DrawingArea,
    other_rows: gtk::Box,
    footer_text: gtk::Label,
    live_legend: gtk::Box,
    scroller: gtk::ScrolledWindow,
    others_open: Cell<bool>,
    last_input: std::cell::RefCell<Option<LastRender>>,
}

/// What the "show others" toggle needs to repaint without a new result.
#[derive(Clone)]
struct LastRender {
    devices: Vec<DeviceEntry>,
    when_unix: i64,
    fatal_error: Option<String>,
    busy: bool,
    threshold: i32,
    waiting: Vec<(String, String)>,
    look: Look,
    pal: Palette,
}

pub fn kind_icon(kind: DeviceKind) -> Icon {
    icons::for_kind(kind)
}

/// A rounded square tile with an icon.
fn tile(icon: Icon, size: i32, glyph: i32, color: &str) -> gtk::Box {
    let b = gtk::Box::new(gtk::Orientation::Horizontal, 0);
    b.add_css_class("joule-tile");
    b.set_size_request(size, size);
    b.set_valign(gtk::Align::Center);
    b.set_halign(gtk::Align::Center);
    b.append(&icons::widget(icon, glyph, color));
    b.set_hexpand(false);
    b.set_halign(gtk::Align::Start);
    b
}

fn dashed_slot(icon: Icon, glyph: i32, round: bool) -> gtk::Overlay {
    let o = gtk::Overlay::new();
    o.set_size_request(32, 32);
    let ring = gtk::Box::new(gtk::Orientation::Horizontal, 0);
    ring.add_css_class(if round {
        "joule-dashed-round"
    } else {
        "joule-dashed"
    });
    o.set_child(Some(&ring));
    o.add_overlay(&icons::widget(icon, glyph, "t-tertiary"));
    o
}

/// The little ember dot with its glow (6 px).
fn ember_dot(pal: Palette) -> gtk::DrawingArea {
    let a = gtk::DrawingArea::new();
    a.set_content_width(12);
    a.set_content_height(12);
    a.set_valign(gtk::Align::Center);
    a.set_draw_func(move |_, cr, w, h| {
        let (cx, cy) = (f64::from(w) / 2.0, f64::from(h) / 2.0);
        let (r, g, b) = (
            f64::from(pal.accent.0) / 255.0,
            f64::from(pal.accent.1) / 255.0,
            f64::from(pal.accent.2) / 255.0,
        );
        for i in (1..=3).rev() {
            cr.arc(cx, cy, 3.0 + f64::from(i), 0.0, 2.0 * std::f64::consts::PI);
            cr.set_source_rgba(r, g, b, 0.7 * 0.08);
            cr.fill().ok();
        }
        cr.arc(cx, cy, 3.0, 0.0, 2.0 * std::f64::consts::PI);
        cr.set_source_rgb(r, g, b);
        cr.fill().ok();
    });
    a
}

/// A plain accent dot (6 px) for the footer legend.
fn accent_dot(color_class: &str) -> gtk::DrawingArea {
    let a = gtk::DrawingArea::new();
    a.set_content_width(6);
    a.set_content_height(6);
    a.set_valign(gtk::Align::Center);
    a.add_css_class(color_class);
    a.set_draw_func(|a, cr, w, h| {
        let c = a.color();
        cr.set_source_rgba(
            f64::from(c.red()),
            f64::from(c.green()),
            f64::from(c.blue()),
            f64::from(c.alpha()),
        );
        cr.arc(
            f64::from(w) / 2.0,
            f64::from(h) / 2.0,
            3.0,
            0.0,
            2.0 * std::f64::consts::PI,
        );
        cr.fill().ok();
    });
    a
}

fn cap(text: &str, classes: &[&str]) -> gtk::Label {
    let mut all = vec!["joule-caption"];
    all.extend_from_slice(classes);
    label(text, &all)
}

fn with_margin_end<W: IsA<gtk::Widget>>(w: &W, px: i32) -> gtk::Widget {
    w.set_margin_end(px);
    w.clone().upcast()
}

/// The sub line (WrapPanel in the XAML): origin, live, reading, charge, age, note.
fn sub_line(v: &DeviceRowView, reading: bool, stale: bool, pal: Palette) -> gtk::FlowBox {
    let flow = gtk::FlowBox::new();
    flow.set_selection_mode(gtk::SelectionMode::None);
    flow.set_max_children_per_line(12);
    flow.set_homogeneous(false);
    flow.set_column_spacing(0);
    flow.set_row_spacing(0);
    flow.set_margin_top(2);
    flow.set_can_focus(false);
    flow.set_halign(gtk::Align::Fill);
    let add = |w: gtk::Widget| flow.append(&w);

    add(with_margin_end(&cap(&origin_label(&v.origin), &[]), 8));
    if v.is_live {
        let b = hbox(0);
        b.append(&ember_dot(pal));
        b.append(&cap(&t("ao vivo", "live"), &["t-secondary"]));
        add(with_margin_end(&b, 8));
    }
    if reading {
        add(with_margin_end(
            &cap(&t("lendo…", "reading…"), &["t-secondary"]),
            8,
        ));
    }
    if let Some(c) = v.charge_text.as_ref().filter(|_| !reading) {
        let b = hbox(3);
        let bolt = icons::widget(icons::bolt(), 12, "t-accent");
        bolt.set_margin_top(1);
        b.append(&bolt);
        b.append(&cap(c, &[]));
        add(with_margin_end(&b, 8));
    }
    if let Some(age) = v.age.as_ref().filter(|_| !reading) {
        let cls = if stale { "t-secondary" } else { "t-tertiary" };
        let b = hbox(4);
        if stale {
            b.append(&icons::widget(icons::clock(), 12, cls));
        }
        b.append(&cap(age, &[cls]));
        add(with_margin_end(&b, 8));
    }
    if let Some(n) = v.note.as_ref().filter(|_| !reading) {
        let l = cap(n, &[if v.is_low { "t-alert" } else { "t-tertiary" }]);
        l.set_ellipsize(gtk::pango::EllipsizeMode::End);
        l.set_max_width_chars(40);
        add(l.upcast());
    }
    // FlowBoxChild paints its own padding and focus ring: flatten it.
    let mut child = flow.first_child();
    while let Some(c) = child {
        c.add_css_class("joule-flowchild");
        child = c.next_sibling();
    }
    flow
}

fn origin_label(origin: &str) -> String {
    match origin {
        "Cable" => t("Cabo", "Wired"),
        "Adapter" => t("Adaptador", "Adapter"),
        other => other.to_string(),
    }
}

/// One card of the main list (`DeviceRow` in the XAML, `RowItem` for the values).
pub fn device_card(v: &DeviceRowView, busy: bool, look: Look, pal: Palette) -> gtk::Widget {
    let reading = busy && !v.is_live;
    let stale = v.is_stale && !reading;

    let grid = gtk::Grid::new();
    grid.add_css_class("joule-card");
    grid.set_column_spacing(12);
    grid.set_margin_bottom(2);
    grid.set_tooltip_text(Some(&v.tooltip));

    grid.attach(&tile(kind_icon(v.kind), 32, 18, "t-secondary"), 0, 0, 1, 1);

    let texts = vbox(0);
    texts.set_valign(gtk::Align::Center);
    texts.set_hexpand(true);
    let name = label(&v.name, &[]);
    name.set_ellipsize(gtk::pango::EllipsizeMode::End);
    name.set_max_width_chars(26);
    texts.append(&name);
    texts.append(&sub_line(v, reading, stale, pal));
    grid.attach(&texts, 1, 0, 1, 1);

    // Value: number, band or cable.
    let value = hbox(0);
    value.set_valign(gtk::Align::Center);
    value.set_margin_start(12);
    let value_class = if v.is_low { "t-alert" } else { "t-primary" };
    match v.value_kind {
        ValueKind::Percent => {
            let n = label(&v.percent.to_string(), &["joule-value", value_class]);
            n.set_valign(gtk::Align::End);
            let u = label("%", &["joule-unit", value_class]);
            u.set_valign(gtk::Align::End);
            u.set_margin_start(1);
            u.set_margin_bottom(3);
            value.append(&n);
            value.append(&u);
        }
        ValueKind::Band => {
            let chip = label(&v.band_label, &["joule-chip", value_class]);
            value.append(&chip);
        }
        ValueKind::Cable => {
            let plug = icons::widget(icons::plug(), 14, "t-secondary");
            plug.set_margin_end(5);
            value.append(&plug);
            value.append(&label(&t("no cabo", "wired"), &["t-secondary"]));
        }
    }
    value.set_opacity(if stale || reading { 0.5 } else { 1.0 });
    grid.attach(&value, 2, 0, 1, 1);

    // Bar: continuous (percent) or three blocks (band).
    match v.value_kind {
        ValueKind::Percent => {
            let lit = !v.is_low && !stale && !reading;
            let spec = BarSpec {
                percent: v.percent,
                kind: if v.is_low {
                    BarKind::Alert
                } else if stale {
                    BarKind::Stale
                } else {
                    BarKind::Ember
                },
                opacity: if reading { 0.45 } else { 1.0 },
                glow: if lit {
                    if v.charge_text.is_some() && !reading {
                        0.8
                    } else {
                        0.5
                    }
                } else {
                    0.0
                },
                shimmer: lit && v.charge_text.is_some() && !reading,
            };
            let b = bar::bar_widget(spec, look, pal);
            // 4 px bar + room for the glow; the card's own spacing already counts 8 px above the bar.
            b.set_margin_top(4);
            b.set_margin_bottom(-4);
            grid.attach(&b, 1, 1, 2, 1);
        }
        ValueKind::Band => {
            let filled = if reading {
                super::theme::stale_rgba(&look)
            } else if v.is_low {
                rgb_f(pal.alert)
            } else {
                rgb_f(pal.accent)
            };
            let seg = bar::segments_widget(v.band_count, filled, track_color(&look));
            seg.set_margin_top(8);
            grid.attach(&seg, 1, 1, 2, 1);
        }
        ValueKind::Cable => {}
    }
    grid.upcast()
}

fn rgb_f(c: (u8, u8, u8)) -> (f64, f64, f64, f64) {
    (
        f64::from(c.0) / 255.0,
        f64::from(c.1) / 255.0,
        f64::from(c.2) / 255.0,
        1.0,
    )
}

/// A brand that waits for permission (`ConsentRow`).
fn consent_row(c: &ConsentRowView, on_allow: impl Fn() + 'static) -> gtk::Widget {
    let row = hbox(12);
    row.add_css_class("joule-card");
    row.set_margin_bottom(4);
    row.set_tooltip_text(Some(&c.name));
    row.append(&dashed_slot(kind_icon(c.kind), 16, true));
    let texts = vbox(0);
    texts.set_hexpand(true);
    texts.set_valign(gtk::Align::Center);
    let name = label(&c.name, &[]);
    name.set_ellipsize(gtk::pango::EllipsizeMode::End);
    name.set_max_width_chars(22);
    texts.append(&name);
    texts.append(&kit::caption(&t(
        "Precisa de permissão para ler a bateria",
        "Needs permission to read the battery",
    )));
    row.append(&texts);
    let allow = kit::accent_button(&t("Permitir", "Allow"), None);
    allow.set_valign(gtk::Align::Center);
    allow.set_margin_start(10);
    allow.connect_clicked(move |_| on_allow());
    row.append(&allow);
    row.upcast()
}

/// A line of "others" (`OtherRow`): dashed icon, name and the reason in up to two lines.
fn other_row(o: &OtherRowView) -> gtk::Widget {
    let row = hbox(12);
    row.add_css_class("joule-flat-card");
    row.set_tooltip_text(Some(&o.reason));
    let slot = dashed_slot(kind_icon(o.kind), 18, false);
    slot.set_valign(gtk::Align::Start);
    row.append(&slot);
    let texts = vbox(2);
    texts.set_hexpand(true);
    let name = label(&o.name, &["t-secondary"]);
    name.set_ellipsize(gtk::pango::EllipsizeMode::End);
    name.set_max_width_chars(30);
    texts.append(&name);
    let reason = kit::caption(&o.reason);
    reason.set_lines(2);
    reason.set_ellipsize(gtk::pango::EllipsizeMode::End);
    reason.set_max_width_chars(44);
    texts.append(&reason);
    row.append(&texts);
    row.upcast()
}

impl PanelView {
    pub fn new(on_action: Rc<dyn Fn(Action)>, brand_font_ok: bool) -> Rc<Self> {
        let _ = brand_font_ok;
        let root = vbox(0);

        // ---- header: margin 16,12,12,10 ----
        let header = hbox(0);
        header.set_margin_start(16);
        header.set_margin_end(12);
        header.set_margin_top(12);
        header.set_margin_bottom(10);

        let title_box = vbox(0);
        title_box.set_hexpand(true);
        title_box.set_valign(gtk::Align::Center);
        let brand = hbox(0);
        brand.append(&label(crate::ui::APP_NAME, &["joule-brand", "t-primary"]));
        let dot = gtk::DrawingArea::new();
        dot.set_content_width(5);
        dot.set_content_height(5);
        dot.set_valign(gtk::Align::End);
        dot.set_margin_bottom(5);
        dot.set_margin_start(1);
        dot.set_draw_func(|_, cr, w, h| {
            cr.arc(
                f64::from(w) / 2.0,
                f64::from(h) / 2.0,
                1.3,
                0.0,
                2.0 * std::f64::consts::PI,
            );
            cr.set_source_rgb(1.0, 0.761, 0.122);
            cr.fill().ok();
        });
        brand.append(&dot);
        title_box.append(&brand);
        let title_sub = cap("", &["t-tertiary"]);
        title_sub.set_visible(false);
        title_box.append(&title_sub);
        header.append(&title_box);

        let reading_pill = hbox(6);
        reading_pill.add_css_class("joule-pill");
        reading_pill.set_margin_end(4);
        reading_pill.set_valign(gtk::Align::Center);
        reading_pill.append(&bar::spinner_widget(16, "t-accent"));
        reading_pill.append(&label(
            &t("Lendo…", "Reading…"),
            &["t-secondary", "joule-caption"],
        ));
        reading_pill.set_visible(false);
        header.append(&reading_pill);

        let refresh_button = kit::icon_button(icons::refresh(), &t("Atualizar", "Refresh"));
        refresh_button.set_margin_end(4);
        header.append(&refresh_button);
        let settings_button = kit::icon_button(icons::sliders(), &t("Configurações", "Settings"));
        header.append(&settings_button);
        root.append(&header);

        // ---- footer is appended after the scroller (it docks at the bottom) ----
        let content = vbox(0);

        let empty = vbox(0);
        empty.set_margin_start(32);
        empty.set_margin_end(32);
        empty.set_margin_top(20);
        empty.set_margin_bottom(20);
        empty.set_halign(gtk::Align::Center);
        let empty_icon = gtk::Box::new(gtk::Orientation::Horizontal, 0);
        empty_icon.add_css_class("joule-tile");
        empty_icon.set_size_request(48, 48);
        empty_icon.set_halign(gtk::Align::Center);
        empty_icon.set_margin_bottom(12);
        empty.append(&empty_icon);
        let empty_title = label("", &["joule-empty-title"]);
        empty_title.set_halign(gtk::Align::Center);
        empty.append(&empty_title);
        let empty_body = label("", &["joule-empty-body"]);
        empty_body.set_wrap(true);
        empty_body.set_wrap_mode(gtk::pango::WrapMode::WordChar);
        empty_body.set_justify(gtk::Justification::Center);
        empty_body.set_max_width_chars(34);
        empty_body.set_margin_top(6);
        empty_body.set_halign(gtk::Align::Center);
        empty.append(&empty_body);
        let empty_refresh = kit::accent_button(&t("Atualizar", "Refresh"), Some(icons::refresh()));
        empty_refresh.set_halign(gtk::Align::Center);
        empty_refresh.set_margin_top(16);
        empty.append(&empty_refresh);
        empty.set_visible(false);
        content.append(&empty);

        let consent_rows = vbox(0);
        consent_rows.set_margin_start(8);
        consent_rows.set_margin_end(8);
        content.append(&consent_rows);
        let rows = vbox(0);
        rows.set_margin_start(8);
        rows.set_margin_end(8);
        content.append(&rows);

        // "Show others": margin 8,8,8,0; toggle height 36, padding 12,0.
        let others_block = vbox(0);
        others_block.set_margin_start(8);
        others_block.set_margin_end(8);
        others_block.set_margin_top(8);
        let toggle = kit::flat_button();
        toggle.set_size_request(-1, 36);
        let toggle_row = hbox(0);
        toggle_row.set_margin_start(12);
        toggle_row.set_margin_end(12);
        let others_label = label("", &["t-secondary"]);
        others_label.set_hexpand(true);
        others_label.set_css_classes(&["t-secondary"]);
        others_label.add_css_class("joule-size-13");
        toggle_row.append(&others_label);
        let chevron_angle: Rc<Cell<f64>> = Rc::new(Cell::new(0.0));
        let chevron = gtk::DrawingArea::new();
        chevron.set_content_width(14);
        chevron.set_content_height(14);
        chevron.set_valign(gtk::Align::Center);
        chevron.add_css_class("t-secondary");
        {
            let angle = chevron_angle.clone();
            chevron.set_draw_func(move |a, cr, w, h| {
                let c = a.color();
                cr.set_source_rgba(
                    f64::from(c.red()),
                    f64::from(c.green()),
                    f64::from(c.blue()),
                    f64::from(c.alpha()),
                );
                let side = f64::from(w.min(h));
                cr.translate(f64::from(w) / 2.0, f64::from(h) / 2.0);
                cr.rotate(angle.get().to_radians());
                cr.translate(-side / 2.0, -side / 2.0);
                icons::draw(cr, &icons::chevron(), side);
            });
        }
        toggle_row.append(&chevron);
        toggle.set_child(Some(&toggle_row));
        others_block.append(&toggle);
        let other_rows = vbox(0);
        other_rows.set_margin_top(2);
        others_block.append(&other_rows);
        content.append(&others_block);

        let scroller = gtk::ScrolledWindow::builder()
            .child(&content)
            .vscrollbar_policy(gtk::PolicyType::Automatic)
            .hscrollbar_policy(gtk::PolicyType::Never)
            .propagate_natural_height(true)
            .vexpand(true)
            .build();
        scroller.vscrollbar().add_css_class("joule-scroll");
        root.append(&scroller);

        // Footer: top divider, padding 16,10,16,12, margin top 8.
        let footer = hbox(0);
        footer.add_css_class("joule-footer");
        footer.set_margin_top(8);
        let footer_in = hbox(0);
        footer_in.set_margin_start(16);
        footer_in.set_margin_end(16);
        footer_in.set_margin_top(10);
        footer_in.set_margin_bottom(12);
        footer_in.set_hexpand(true);
        let footer_text = cap("", &[]);
        footer_text.set_hexpand(true);
        footer_in.append(&footer_text);
        let live_legend = hbox(5);
        live_legend.append(&accent_dot("t-accent"));
        live_legend.append(&cap(&t("atualiza sozinho", "updates by itself"), &[]));
        live_legend.set_visible(false);
        footer_in.append(&live_legend);
        footer.append(&footer_in);
        root.append(&footer);

        let view = Rc::new(Self {
            root,
            on_action,
            title_sub,
            reading_pill,
            refresh_button: refresh_button.clone(),
            empty,
            empty_icon,
            empty_title,
            empty_body,
            empty_refresh: empty_refresh.clone(),
            consent_rows,
            rows,
            others_block,
            others_label,
            chevron_angle,
            chevron,
            other_rows,
            footer_text,
            live_legend,
            scroller,
            others_open: Cell::new(false),
            last_input: std::cell::RefCell::new(None),
        });

        let v = view.clone();
        refresh_button.connect_clicked(move |_| (v.on_action)(Action::Refresh));
        let v = view.clone();
        empty_refresh.connect_clicked(move |_| (v.on_action)(Action::Refresh));
        let v = view.clone();
        settings_button.connect_clicked(move |_| (v.on_action)(Action::OpenSettings));
        let v = view.clone();
        toggle.connect_clicked(move |_| {
            v.others_open.set(!v.others_open.get());
            v.repaint();
        });
        view
    }

    /// Limit of the list height: the work area minus the header, footer and margins (`Scroller.MaxHeight`).
    pub fn set_max_height(&self, px: i32) {
        self.scroller.set_max_content_height(px);
    }

    /// Repaints with the last input (used by "show others").
    fn repaint(&self) {
        let Some(last) = self.last_input.borrow().clone() else {
            return;
        };
        let waiting = last.waiting.clone();
        let lookup = move |hw: &str| {
            waiting
                .iter()
                .find(|(h, _)| hw.starts_with(h.as_str()))
                .map(|(_, v)| v.clone())
        };
        self.render(&PanelInput {
            devices: &last.devices,
            when_unix: last.when_unix,
            fatal_error: last.fatal_error.as_deref(),
            busy: last.busy,
            threshold: last.threshold,
            now_unix: now_unix(),
            waiting_vid: &lookup,
            look: last.look,
            pal: last.pal,
        });
    }

    pub fn render(&self, input: &PanelInput) {
        let main = panel::main_rows(
            input.devices,
            input.threshold,
            input.now_unix,
            input.when_unix,
        );
        let consent = panel::need_consent(input.devices, input.waiting_vid);
        let others = panel::others(input.devices, input.waiting_vid);

        // Remember enough to repaint on "show others" (the closure borrows are not 'static).
        let mut waiting: Vec<(String, String)> = Vec::new();
        for d in input.devices {
            if let Some(vid) = (input.waiting_vid)(&d.hardware_id) {
                let prefix = d.hardware_id.split(':').next().unwrap_or("").to_string();
                if !waiting.iter().any(|(p, _)| *p == prefix) {
                    waiting.push((prefix, vid));
                }
            }
        }
        *self.last_input.borrow_mut() = Some(LastRender {
            devices: input.devices.to_vec(),
            when_unix: input.when_unix,
            fatal_error: input.fatal_error.map(String::from),
            busy: input.busy,
            threshold: input.threshold,
            waiting,
            look: input.look,
            pal: input.pal,
        });

        clear(&self.rows);
        for r in &main {
            self.rows
                .append(&device_card(r, input.busy, input.look, input.pal));
        }
        clear(&self.consent_rows);
        for c in &consent {
            let on = self.on_action.clone();
            let vid = c.vid.clone();
            self.consent_rows
                .append(&consent_row(c, move || on(Action::Allow(vid.clone()))));
        }
        clear(&self.other_rows);
        for o in &others {
            self.other_rows.append(&other_row(o));
        }

        // Header
        let low: Vec<&DeviceRowView> = main.iter().filter(|r| r.is_low).collect();
        let subtitle: Option<String> = if input.busy {
            Some(t(
                "Atualizando os aparelhos sob demanda",
                "Refreshing devices on demand",
            ))
        } else if low.len() == 1 {
            Some(t(
                &format!("{} abaixo de {}%", low[0].name, input.threshold),
                &format!("{} below {}%", low[0].name, input.threshold),
            ))
        } else if low.len() > 1 {
            Some(t(
                &format!("{} aparelhos abaixo de {}%", low.len(), input.threshold),
                &format!("{} devices below {}%", low.len(), input.threshold),
            ))
        } else {
            None
        };
        self.title_sub.set_text(subtitle.as_deref().unwrap_or(""));
        self.title_sub.set_visible(subtitle.is_some());
        self.title_sub.remove_css_class("t-alert");
        self.title_sub.remove_css_class("t-tertiary");
        self.title_sub
            .add_css_class(if !input.busy && !low.is_empty() {
                "t-alert"
            } else {
                "t-tertiary"
            });
        self.reading_pill.set_visible(input.busy);
        self.refresh_button.set_visible(!input.busy);

        // Empty state: with "others" it is "no device with battery" and the list opens by itself.
        let empty = main.is_empty() && consent.is_empty() && input.when_unix > 0;
        self.empty.set_visible(empty);
        if empty {
            let has_others = !others.is_empty();
            while let Some(c) = self.empty_icon.first_child() {
                self.empty_icon.remove(&c);
            }
            let icon = icons::widget(
                if has_others {
                    icons::generic()
                } else {
                    icons::plug()
                },
                26,
                "t-secondary",
            );
            icon.set_hexpand(true);
            self.empty_icon.append(&icon);
            self.empty_title.set_text(&if has_others {
                t("Nenhum aparelho com bateria", "No device with battery")
            } else {
                t("Nenhum aparelho conectado", "No device connected")
            });
            self.empty_body.set_text(&if has_others {
                t(
                    "Há aparelhos plugados, mas nenhum informou bateria. Veja o motivo de cada um abaixo.",
                    "Devices are plugged in, but none reported a battery. See each one’s reason below.",
                )
            } else {
                t(
                    "Ligue o dongle ou conecte por Bluetooth. Valores guardados voltam quando o aparelho reconectar.",
                    "Plug in the dongle or connect over Bluetooth. Saved values come back when the device reconnects.",
                )
            });
            self.empty_refresh.set_visible(!has_others);
        }

        let open = self.others_open.get() || (empty && !others.is_empty());
        self.others_block.set_visible(!others.is_empty());
        self.others_label.set_text(&format!(
            "{} ({})",
            if open {
                t("Ocultar outros", "Hide others")
            } else {
                t("Mostrar outros", "Show others")
            },
            others.len()
        ));
        self.chevron_angle.set(if open { 180.0 } else { 0.0 });
        self.chevron.queue_draw();
        self.other_rows.set_visible(open);

        // Footer
        let text = if input.busy {
            t("Lendo… leva até 3 s", "Reading… takes up to 3 s")
        } else if let Some(err) = input.fatal_error.filter(|e| !e.is_empty()) {
            t(
                &format!("Erro na leitura: {err}"),
                &format!("Reading error: {err}"),
            )
        } else if input.when_unix <= 0 {
            t("Ainda sem leitura", "No reading yet")
        } else {
            let hm = clock_text(input.when_unix);
            t(
                &format!("Última leitura às {hm}"),
                &format!("Last reading at {hm}"),
            )
        };
        self.footer_text.set_text(&text);
        self.live_legend.set_visible(main.iter().any(|r| r.is_live));
    }
}

fn now_unix() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
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

#[allow(dead_code)]
fn _unused(_: &cairo::Context) {}
