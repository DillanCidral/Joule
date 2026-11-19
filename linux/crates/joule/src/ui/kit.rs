//! Small widget builders with the Joule styles (see `theme.rs` for the CSS).

use super::icons::{self, Icon};
use gtk::prelude::*;
use joule_core::l::t;

pub fn vbox(spacing: i32) -> gtk::Box {
    gtk::Box::new(gtk::Orientation::Vertical, spacing)
}

pub fn hbox(spacing: i32) -> gtk::Box {
    gtk::Box::new(gtk::Orientation::Horizontal, spacing)
}

pub fn clear(b: &gtk::Box) {
    while let Some(c) = b.first_child() {
        b.remove(&c);
    }
}

/// A left-aligned label with style classes.
pub fn label(text: &str, classes: &[&str]) -> gtk::Label {
    let l = gtk::Label::new(Some(text));
    l.set_xalign(0.0);
    for c in classes {
        l.add_css_class(c);
    }
    l
}

/// A wrapping caption (12 px, tertiary).
pub fn caption(text: &str) -> gtk::Label {
    let l = label(text, &["joule-caption"]);
    l.set_wrap(true);
    l.set_wrap_mode(gtk::pango::WrapMode::WordChar);
    l
}

pub fn rule() -> gtk::Box {
    let b = gtk::Box::new(gtk::Orientation::Horizontal, 0);
    b.add_css_class("joule-rule");
    b
}

/// A 32 px icon button (16 px glyph).
pub fn icon_button(icon: Icon, tooltip: &str) -> gtk::Button {
    let b = gtk::Button::new();
    b.add_css_class("joule-icon");
    b.set_child(Some(&icons::widget(icon, 16, "t-primary")));
    b.set_tooltip_text(Some(tooltip));
    b.set_focusable(false);
    b.set_valign(gtk::Align::Center);
    b
}

/// The flat row button ("show others", settings links).
pub fn flat_button() -> gtk::Button {
    let b = gtk::Button::new();
    b.add_css_class("joule-flat");
    b.set_focusable(false);
    b
}

/// An accent button with a label (13 px semibold) and an optional glyph.
pub fn accent_button(text: &str, icon: Option<Icon>) -> gtk::Button {
    let b = gtk::Button::new();
    b.add_css_class("joule-accent");
    b.set_focusable(false);
    let row = hbox(6);
    row.set_halign(gtk::Align::Center);
    if let Some(i) = icon {
        row.append(&icons::widget(i, 14, "t-accent-fg"));
    }
    row.append(&gtk::Label::new(Some(text)));
    b.set_child(Some(&row));
    b
}

pub fn outline_button(text: &str) -> gtk::Button {
    let b = gtk::Button::with_label(text);
    b.add_css_class("joule-outline");
    b.set_focusable(false);
    b
}

/// The 40x20 switch of the design.
pub fn switch(active: bool) -> gtk::Switch {
    let s = gtk::Switch::new();
    s.add_css_class("joule-switch");
    s.set_active(active);
    s.set_valign(gtk::Align::Center);
    s.set_focusable(false);
    s
}

/// A segmented control: one pill track with toggle buttons; `equal` = same width (UniformGrid).
pub fn segmented(
    options: &[&str],
    equal: bool,
    padding: (i32, i32),
) -> (gtk::Box, Vec<gtk::ToggleButton>) {
    let track = hbox(0);
    track.add_css_class("joule-segtrack");
    track.set_homogeneous(equal);
    let mut buttons: Vec<gtk::ToggleButton> = Vec::new();
    for text in options {
        let b = gtk::ToggleButton::with_label(text);
        b.set_focusable(false);
        b.set_hexpand(equal);
        // The XAML pads each segment (4,6 on the full-width ones; 12,5 or 10,5 on the compact ones).
        b.add_css_class(&format!("seg-{}-{}", padding.0, padding.1));
        if let Some(first) = buttons.first() {
            b.set_group(Some(first));
        }
        track.append(&b);
        buttons.push(b);
    }
    (track, buttons)
}

/// A settings row: texts on the left, a control on the right (the XAML DockPanel), margin 14,12.
pub fn setting_row(
    title: &str,
    caption_text: Option<&str>,
    control: &impl IsA<gtk::Widget>,
) -> gtk::Box {
    let row = hbox(10);
    row.set_margin_start(14);
    row.set_margin_end(14);
    row.set_margin_top(12);
    row.set_margin_bottom(12);
    let texts = vbox(0);
    texts.set_hexpand(true);
    texts.set_valign(gtk::Align::Center);
    texts.append(&label(title, &[]));
    if let Some(c) = caption_text {
        texts.append(&caption(c));
    }
    row.append(&texts);
    control.set_valign(gtk::Align::Center);
    row.append(control);
    row
}

/// A group box (rounded card) with rules between the rows.
pub fn group(rows: &[gtk::Widget]) -> gtk::Box {
    let g = vbox(0);
    g.add_css_class("joule-group");
    for (i, r) in rows.iter().enumerate() {
        if i > 0 {
            g.append(&rule());
        }
        g.append(r);
    }
    g
}

pub fn group_label(text: &str, first: bool) -> gtk::Label {
    let l = label(text, &["joule-group-label"]);
    if !first {
        l.set_margin_top(16);
    }
    l
}

/// "Back" header of the sub pages: margin 8,12,12,12, back button and a semibold title.
pub fn page_header(title: &str, back: &gtk::Button) -> gtk::Box {
    let row = hbox(8);
    row.set_margin_start(8);
    row.set_margin_end(12);
    row.set_margin_top(12);
    row.set_margin_bottom(12);
    row.append(back);
    row.append(&label(title, &["joule-title"]));
    row
}

pub fn back_button() -> gtk::Button {
    icon_button(icons::back(), &t("Voltar", "Back"))
}

/// Largest height of a settings or about page: the work area minus the header and footer
/// (`SettingsScroller.MaxHeight = workH - 140`).
pub fn max_page_height() -> i32 {
    use gtk::gdk;
    let h = gdk::Display::default()
        .and_then(|d| d.monitors().item(0))
        .and_then(|m| m.downcast::<gdk::Monitor>().ok())
        .map(|m| m.geometry().height())
        .unwrap_or(1080);
    (h - 140).max(200)
}
