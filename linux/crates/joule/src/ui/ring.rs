//! The 32 px ring of the edge widget (ported from `RingArc.cs` and the `Ring` template of
//! `WidgetWindow.xaml`): a track and an arc that fades toward its tip; three arcs for a band, a
//! dashed track for a cable; the device icon in the middle and the ember dot when the value is live.

use super::{
    icons::{self, Icon},
    theme::{Look, Palette, track_color},
};
use gtk::{cairo, prelude::*};
use std::f64::consts::PI;

#[derive(Clone, Copy, Debug, PartialEq)]
pub enum RingKind {
    /// 0.0..=1.0
    Percent(f64),
    /// 0..=3 filled arcs
    Band(i32),
    Cable,
}

#[derive(Clone, Debug)]
pub struct RingSpec {
    pub kind: RingKind,
    pub low: bool,
    pub stale: bool,
    pub live: bool,
    pub icon: Icon,
}

/// Opacity along the arc (0 = start, 1 = tip): full to 30 %, then fading to 15 % (`RingArc.ArcAlpha`).
pub fn arc_alpha(t: f64) -> f64 {
    if t < 0.3 {
        1.0
    } else {
        1.0 - 0.85 * (t - 0.3) / 0.7
    }
}

/// Opacity of the track along the lap: full to 35 %, gone before it closes (`RingArc.TrackAlpha`).
pub fn track_alpha(t: f64) -> f64 {
    if t < 0.35 {
        1.0
    } else {
        (1.0 - (t - 0.35) / 0.6).max(0.0)
    }
}

/// A faded arc as short pieces of decreasing opacity (cairo has no gradient along an arc).
fn faded_arc(
    cr: &cairo::Context,
    c: (f64, f64),
    r: f64,
    color: (f64, f64, f64, f64),
    up_to: f64,
    alpha: fn(f64) -> f64,
    round_start: bool,
) {
    let n = ((up_to * 60.0).ceil() as usize).max(2);
    cr.set_line_width(3.0);
    for i in 0..n {
        let a = alpha((i as f64 + 0.5) / n as f64);
        if a <= 0.01 {
            continue;
        }
        let from = up_to * i as f64 / n as f64;
        let to = up_to * (i as f64 + 1.0) / n as f64;
        cr.set_line_cap(if i == 0 && round_start {
            cairo::LineCap::Round
        } else {
            cairo::LineCap::Butt
        });
        cr.set_source_rgba(color.0, color.1, color.2, color.3 * a);
        cr.new_path();
        cr.arc(
            c.0,
            c.1,
            r,
            from * 2.0 * PI - PI / 2.0,
            to * 2.0 * PI - PI / 2.0,
        );
        cr.stroke().ok();
    }
}

fn rgb(c: (u8, u8, u8)) -> (f64, f64, f64, f64) {
    (
        f64::from(c.0) / 255.0,
        f64::from(c.1) / 255.0,
        f64::from(c.2) / 255.0,
        1.0,
    )
}

pub fn draw_ring(
    cr: &cairo::Context,
    size: f64,
    spec: &RingSpec,
    look: &Look,
    pal: &Palette,
    fg: (f64, f64, f64, f64),
) {
    let c = (size / 2.0, size / 2.0);
    let r = size / 2.0 - 1.5;
    let track = track_color(look);
    let arc_color = if spec.low {
        rgb(pal.alert)
    } else if spec.stale {
        super::theme::stale_rgba(look)
    } else {
        rgb(pal.accent)
    };
    match spec.kind {
        RingKind::Percent(f) => {
            faded_arc(cr, c, r, track, 1.0, track_alpha, false);
            if f > 0.001 {
                faded_arc(cr, c, r, arc_color, f.clamp(0.0, 1.0), arc_alpha, true);
            }
        }
        RingKind::Band(n) => {
            // three arcs of a third each with a gap, the filled ones in the arc colour
            for i in 0..3 {
                let from = f64::from(i) / 3.0 + 0.02;
                let to = f64::from(i + 1) / 3.0 - 0.02;
                let col = if i < n { arc_color } else { track };
                cr.set_line_width(3.0);
                cr.set_line_cap(cairo::LineCap::Butt);
                cr.set_source_rgba(col.0, col.1, col.2, col.3);
                cr.new_path();
                cr.arc(
                    c.0,
                    c.1,
                    r,
                    from * 2.0 * PI - PI / 2.0,
                    to * 2.0 * PI - PI / 2.0,
                );
                cr.stroke().ok();
            }
        }
        RingKind::Cable => {
            cr.set_line_width(3.0);
            cr.set_dash(&[2.0, 2.4], 0.0);
            cr.set_source_rgba(track.0, track.1, track.2, track.3);
            cr.new_path();
            cr.arc(c.0, c.1, r, 0.0, 2.0 * PI);
            cr.stroke().ok();
            cr.set_dash(&[], 0.0);
        }
    }
    // device icon, 14 px, secondary colour
    cr.save().ok();
    cr.translate((size - 14.0) / 2.0, (size - 14.0) / 2.0);
    cr.set_source_rgba(fg.0, fg.1, fg.2, fg.3);
    icons::draw(cr, &spec.icon, 14.0);
    cr.restore().ok();
    // live dot, top right
    if spec.live {
        let (dx, dy) = (size - 3.0, 3.0);
        cr.arc(dx, dy, 5.5, 0.0, 2.0 * PI);
        cr.set_source_rgba(0.0, 0.0, 0.0, 0.45);
        cr.fill().ok();
        let e = rgb(pal.accent);
        cr.arc(dx, dy, 3.5, 0.0, 2.0 * PI);
        cr.set_source_rgba(e.0, e.1, e.2, 1.0);
        cr.fill().ok();
    }
}

/// The ring as a widget, 32 x 32.
pub fn ring_widget(spec: RingSpec, look: Look, pal: Palette) -> gtk::DrawingArea {
    let area = gtk::DrawingArea::new();
    area.set_content_width(32);
    area.set_content_height(32);
    area.set_valign(gtk::Align::Center);
    area.add_css_class("t-secondary");
    area.set_draw_func(move |a, cr, w, h| {
        let c = a.color();
        let fg = (
            f64::from(c.red()),
            f64::from(c.green()),
            f64::from(c.blue()),
            f64::from(c.alpha()),
        );
        cr.translate((f64::from(w) - 32.0) / 2.0, (f64::from(h) - 32.0) / 2.0);
        draw_ring(cr, 32.0, &spec, &look, &pal, fg);
    });
    area
}

#[cfg(test)]
mod tests {
    use super::*;

    fn point(cx: f64, cy: f64, r: f64, f: f64) -> (f64, f64) {
        (cx + r * (f * 2.0 * PI).sin(), cy - r * (f * 2.0 * PI).cos())
    }

    #[test]
    fn the_arc_fades_after_30_percent() {
        assert_eq!(arc_alpha(0.0), 1.0);
        assert_eq!(arc_alpha(0.3), 1.0);
        assert!((arc_alpha(1.0) - 0.15).abs() < 1e-9);
        assert!(arc_alpha(0.65) < arc_alpha(0.4));
    }
    #[test]
    fn the_track_is_gone_before_the_lap_closes() {
        assert_eq!(track_alpha(0.2), 1.0);
        assert!(track_alpha(0.9) < 0.2);
        assert_eq!(track_alpha(1.0), 0.0);
    }
    #[test]
    fn points_start_at_the_top_and_go_clockwise() {
        let (x, y) = point(16.0, 16.0, 10.0, 0.0);
        assert!((x - 16.0).abs() < 1e-9 && (y - 6.0).abs() < 1e-9);
        let (x, y) = point(16.0, 16.0, 10.0, 0.25);
        assert!((x - 26.0).abs() < 1e-9 && (y - 16.0).abs() < 1e-9);
    }
}
