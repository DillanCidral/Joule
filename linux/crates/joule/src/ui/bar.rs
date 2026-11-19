//! The ember bar, the three band blocks and the spinner (cairo), ported from the XAML of the panel
//! (`DeviceRow`) and `RowItem`: the bar fades at its tip and glows, a charging device gets a shimmer.

use super::{
    icons,
    theme::{Look, Palette, stale_rgba, track_color},
};
use gtk::{cairo, glib, prelude::*};
use std::{
    cell::{Cell, RefCell},
    f64::consts::PI,
    rc::Rc,
};

/// Which brush the fill uses (`RowItem.BarBrush`).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum BarKind {
    Ember,
    Alert,
    Stale,
}

#[derive(Clone, Copy, Debug)]
pub struct BarSpec {
    /// 0..=100
    pub percent: i32,
    pub kind: BarKind,
    /// 0.45 while reading, else 1.
    pub opacity: f64,
    /// Glow opacity (0 = none).
    pub glow: f64,
    pub shimmer: bool,
}

fn rounded_rect(cr: &cairo::Context, x: f64, y: f64, w: f64, h: f64, r: f64) {
    let r = r.min(w / 2.0).min(h / 2.0);
    cr.new_sub_path();
    cr.arc(x + w - r, y + r, r, -PI / 2.0, 0.0);
    cr.arc(x + w - r, y + h - r, r, 0.0, PI / 2.0);
    cr.arc(x + r, y + h - r, r, PI / 2.0, PI);
    cr.arc(x + r, y + r, r, PI, 3.0 * PI / 2.0);
    cr.close_path();
}

fn rgb(c: (u8, u8, u8)) -> (f64, f64, f64) {
    (
        f64::from(c.0) / 255.0,
        f64::from(c.1) / 255.0,
        f64::from(c.2) / 255.0,
    )
}

/// Opacity along the tip: full to 30 %, then fading (`Theme.Fade`).
fn fade_stops(g: &cairo::LinearGradient, light: (u8, u8, u8), color: (u8, u8, u8), end_alpha: f64) {
    let (lr, lg, lb) = rgb(light);
    let (r, gr, b) = rgb(color);
    g.add_color_stop_rgba(0.0, lr, lg, lb, 1.0);
    g.add_color_stop_rgba(0.3, r, gr, b, 1.0);
    g.add_color_stop_rgba(1.0, r, gr, b, end_alpha);
}

/// Height of the drawing area: the bar is 4 high, the rest is room for the glow.
pub const BAR_AREA_H: i32 = 12;
const PAD: f64 = 4.0;

pub fn draw_bar(
    cr: &cairo::Context,
    w: f64,
    spec: &BarSpec,
    look: &Look,
    pal: &Palette,
    shimmer_x: f64,
) {
    let h = 4.0;
    let y = PAD;
    // Track: the Level.TrackFade gradient, gone before the end so the limit never shows.
    let (tr, tg, tb, ta) = track_color(look);
    let track = cairo::LinearGradient::new(0.0, 0.0, w, 0.0);
    track.add_color_stop_rgba(0.0, tr, tg, tb, ta);
    track.add_color_stop_rgba(0.35, tr, tg, tb, ta);
    track.add_color_stop_rgba(1.0, tr, tg, tb, 0.0);
    rounded_rect(cr, 0.0, y, w, h, 2.0);
    cr.set_source(&track).ok();
    cr.fill().ok();

    let fw = (w * f64::from(spec.percent.clamp(0, 100)) / 100.0).max(0.0);
    if fw < 0.5 {
        return;
    }

    // Glow around the fill (a drop shadow in WPF): a few growing, fainter outlines.
    if spec.glow > 0.0 {
        let (gr, gg, gb) = rgb(if spec.kind == BarKind::Alert {
            pal.alert
        } else {
            pal.accent
        });
        for i in (1..=3).rev() {
            let grow = f64::from(i) * 1.2;
            rounded_rect(
                cr,
                -grow * 0.0,
                y - grow,
                fw + grow,
                h + 2.0 * grow,
                2.0 + grow,
            );
            cr.set_source_rgba(
                gr,
                gg,
                gb,
                spec.glow * 0.10 / f64::from(i) * opacity_scale(),
            );
            cr.fill().ok();
        }
    }

    cr.push_group();
    rounded_rect(cr, 0.0, y, fw, h, 2.0);
    cr.clip();
    match spec.kind {
        BarKind::Stale => {
            let (r, g, b, a) = stale_rgba(look);
            cr.set_source_rgba(r, g, b, a);
        }
        kind => {
            let grad = cairo::LinearGradient::new(0.0, 0.0, fw, 0.0);
            if kind == BarKind::Alert {
                fade_stops(&grad, pal.alert_light, pal.alert, 0.25);
            } else {
                fade_stops(&grad, pal.ember_light, pal.accent, 0.15);
            }
            cr.set_source(&grad).ok();
        }
    }
    cr.paint().ok();
    if spec.shimmer {
        // A bright band running back and forth: x in -0.7..0.7 of the fill width.
        let off = shimmer_x * fw;
        let g = cairo::LinearGradient::new(off, 0.0, off + fw, 0.0);
        g.add_color_stop_rgba(0.3, 1.0, 0.984, 0.918, 0.0);
        g.add_color_stop_rgba(0.5, 1.0, 0.984, 0.918, 0.9);
        g.add_color_stop_rgba(0.7, 1.0, 0.984, 0.918, 0.0);
        cr.set_source(&g).ok();
        cr.paint().ok();
    }
    cr.pop_group_to_source().ok();
    cr.paint_with_alpha(spec.opacity).ok();
}

fn opacity_scale() -> f64 {
    1.0
}

/// Sine ease in-out ping-pong of 1.6 s, from -0.7 to 0.7 (the XAML storyboard).
pub fn shimmer_position(seconds: f64) -> f64 {
    let period = 3.2; // there and back
    let t = (seconds % period) / period; // 0..1
    let tri = if t < 0.5 { t * 2.0 } else { 2.0 - t * 2.0 }; // 0..1..0
    let eased = -(f64::cos(PI * tri) - 1.0) / 2.0;
    -0.7 + 1.4 * eased
}

/// The bar as a widget. `look` and `pal` are copied; rebuild the card when the theme changes.
pub fn bar_widget(spec: BarSpec, look: Look, pal: Palette) -> gtk::DrawingArea {
    let area = gtk::DrawingArea::new();
    area.set_content_height(BAR_AREA_H);
    area.set_hexpand(true);
    let x = Rc::new(Cell::new(0.0f64));
    {
        let x = x.clone();
        area.set_draw_func(move |_, cr, w, _| {
            draw_bar(cr, f64::from(w), &spec, &look, &pal, x.get())
        });
    }
    if spec.shimmer {
        let start: Rc<RefCell<Option<i64>>> = Rc::new(RefCell::new(None));
        area.add_tick_callback(move |a, clock| {
            let now = clock.frame_time();
            let t0 = *start.borrow_mut().get_or_insert(now);
            x.set(shimmer_position((now - t0) as f64 / 1_000_000.0));
            a.queue_draw();
            glib::ControlFlow::Continue
        });
    }
    area
}

/// The three band blocks: 6 high, radius 1, 4 apart.
pub fn segments_widget(
    count: i32,
    filled: (f64, f64, f64, f64),
    empty: (f64, f64, f64, f64),
) -> gtk::DrawingArea {
    let area = gtk::DrawingArea::new();
    area.set_content_height(6);
    area.set_hexpand(true);
    area.set_draw_func(move |_, cr, w, h| {
        let (w, h) = (f64::from(w), f64::from(h));
        let gap = 4.0;
        let sw = (w - 2.0 * gap) / 3.0;
        for i in 0..3 {
            let c = if i < count { filled } else { empty };
            rounded_rect(cr, f64::from(i) * (sw + gap), 0.0, sw, h, 1.0);
            cr.set_source_rgba(c.0, c.1, c.2, c.3);
            cr.fill().ok();
        }
    });
    area
}

/// The spinner icon turning at 360 degrees per second (header "Reading…" pill).
pub fn spinner_widget(size: i32, color_class: &str) -> gtk::DrawingArea {
    let area = gtk::DrawingArea::new();
    area.set_content_width(size);
    area.set_content_height(size);
    area.add_css_class(color_class);
    let angle = Rc::new(Cell::new(130.0f64));
    {
        let angle = angle.clone();
        area.set_draw_func(move |a, cr, w, h| {
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
            icons::draw(cr, &icons::spinner(), side);
        });
    }
    let start: Rc<RefCell<Option<i64>>> = Rc::new(RefCell::new(None));
    area.add_tick_callback(move |a, clock| {
        if !a.is_mapped() {
            *start.borrow_mut() = None;
            return glib::ControlFlow::Continue;
        }
        let now = clock.frame_time();
        let t0 = *start.borrow_mut().get_or_insert(now);
        angle.set(130.0 + 360.0 * (((now - t0) as f64 / 1_000_000.0) % 1.0));
        a.queue_draw();
        glib::ControlFlow::Continue
    });
    area
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn shimmer_runs_between_minus_and_plus_0_7() {
        assert!((shimmer_position(0.0) + 0.7).abs() < 1e-9);
        assert!((shimmer_position(1.6) - 0.7).abs() < 1e-9);
        assert!(
            (shimmer_position(3.2) + 0.7).abs() < 1e-6,
            "back at the start after one period"
        );
        assert!(shimmer_position(0.8).abs() < 1e-9, "halfway is the middle");
    }
    #[test]
    fn shimmer_stays_in_range() {
        for i in 0..200 {
            let p = shimmer_position(f64::from(i) * 0.037);
            assert!((-0.7001..=0.7001).contains(&p), "{p}");
        }
    }
}
