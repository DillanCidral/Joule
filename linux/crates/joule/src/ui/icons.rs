//! The "Silhueta" icons of Joule (ported from `Ui/Icons.cs`): viewBox 20x20, filled shapes plus a few
//! 2-wide round strokes. Drawn with cairo from the original path data, so they match the Windows app
//! exactly and take their colour from the widget's CSS `color`.

use gtk::{cairo, prelude::*};
use joule_core::DeviceKind;

/// One icon: a filled path (even-odd, cut-outs become holes) and/or a stroked path (width 2, round).
#[derive(Clone, Debug)]
pub struct Icon {
    pub fill: Option<String>,
    pub stroke: Option<String>,
}

fn f(fill: &str) -> Icon {
    Icon {
        fill: Some(fill.into()),
        stroke: None,
    }
}
fn fs(fill: String, stroke: &str) -> Icon {
    Icon {
        fill: Some(fill),
        stroke: Some(stroke.into()),
    }
}

/// A pill (capsule) in the given rectangle, lying or standing.
fn cap(x: f64, y: f64, w: f64, h: f64) -> String {
    let r = w.min(h) / 2.0;
    if w >= h {
        format!(
            "M{} {}h{}a{r} {r} 0 0 1 0 {h}h{}a{r} {r} 0 0 1 0 {}z",
            x + r,
            y,
            w - 2.0 * r,
            -(w - 2.0 * r),
            -h
        )
    } else {
        format!(
            "M{x} {}a{r} {r} 0 0 1 {w} 0v{}a{r} {r} 0 0 1 {} 0z",
            y + r,
            h - 2.0 * r,
            -w
        )
    }
}

fn dot(x: f64, y: f64, r: f64) -> String {
    format!(
        "M{x} {}a{r} {r} 0 1 0 0 {}a{r} {r} 0 1 0 0 {}z",
        y - r,
        2.0 * r,
        -2.0 * r
    )
}

pub fn mouse() -> Icon {
    f(&format!(
        "M10 2a5 5 0 0 1 5 5v6a5 5 0 0 1-10 0V7a5 5 0 0 1 5-5z{}",
        cap(9.1, 4.6, 1.8, 4.8)
    ))
}
pub fn keyboard() -> Icon {
    f(&format!(
        "M4.5 4.5h11a3 3 0 0 1 3 3v5a3 3 0 0 1-3 3h-11a3 3 0 0 1-3-3v-5a3 3 0 0 1 3-3z{}{}{}{}",
        dot(6.5, 7.6, 0.9),
        dot(10.0, 7.6, 0.9),
        dot(13.5, 7.6, 0.9),
        cap(6.8, 10.9, 6.4, 1.8)
    ))
}
pub fn headset() -> Icon {
    fs(
        format!(
            "{}{}",
            cap(2.25, 10.5, 4.5, 7.0),
            cap(13.25, 10.5, 4.5, 7.0)
        ),
        "M3.75 10.6V10a6.25 6.25 0 0 1 12.5 0v0.6",
    )
}
pub fn gamepad() -> Icon {
    f(&format!(
        "M6.5 5h7a5 5 0 0 1 0 10h-7a5 5 0 0 1 0-10zM5.3 8.3H6.7V9.3H7.7V10.7H6.7V11.7H5.3V10.7H4.3V9.3H5.3Z{}{}",
        dot(13.3, 8.9, 0.85),
        dot(14.95, 11.1, 0.85)
    ))
}
pub fn generic() -> Icon {
    f(&format!(
        "M8 2.5h4a3 3 0 0 1 3 3v9a3 3 0 0 1-3 3H8a3 3 0 0 1-3-3v-9a3 3 0 0 1 3-3z{}",
        cap(8.3, 13.5, 3.4, 1.6)
    ))
}
/// USB-C plug: the pill tip with the cut-out tongue, body, relief and cable.
pub fn plug() -> Icon {
    fs(
        format!(
            "{}{}M7.7 6.8h4.6a2.2 2.2 0 0 1 2.2 2.2v1.8a2.2 2.2 0 0 1-2.2 2.2H7.7a2.2 2.2 0 0 1-2.2-2.2V9a2.2 2.2 0 0 1 2.2-2.2zM8.6 13h2.8v1.2a1.4 1.4 0 0 1-2.8 0z",
            cap(6.5, 3.0, 7.0, 3.2),
            cap(7.9, 4.0, 4.2, 1.2)
        ),
        "M10 15.4V18",
    )
}

pub fn for_kind(kind: DeviceKind) -> Icon {
    match kind {
        DeviceKind::Mouse => mouse(),
        DeviceKind::Keyboard => keyboard(),
        DeviceKind::Headset => headset(),
        DeviceKind::Gamepad => gamepad(),
        DeviceKind::Generic => generic(),
    }
}

// ---------------- path data ----------------

#[derive(Clone, Copy, Debug, PartialEq)]
pub enum Seg {
    Move(f64, f64),
    Line(f64, f64),
    Cubic(f64, f64, f64, f64, f64, f64),
    Close,
}

struct Tokens<'a> {
    b: &'a [u8],
    i: usize,
}

impl Tokens<'_> {
    fn skip(&mut self) {
        while self.i < self.b.len() && (self.b[self.i] == b' ' || self.b[self.i] == b',') {
            self.i += 1;
        }
    }
    fn command(&mut self) -> Option<u8> {
        self.skip();
        match self.b.get(self.i) {
            Some(c) if c.is_ascii_alphabetic() => {
                self.i += 1;
                Some(*c)
            }
            _ => None,
        }
    }
    fn has_number(&mut self) -> bool {
        self.skip();
        matches!(self.b.get(self.i), Some(c) if c.is_ascii_digit() || *c == b'-' || *c == b'+' || *c == b'.')
    }
    fn number(&mut self) -> f64 {
        self.skip();
        let start = self.i;
        if matches!(self.b.get(self.i), Some(b'-') | Some(b'+')) {
            self.i += 1;
        }
        let mut seen_dot = false;
        while let Some(c) = self.b.get(self.i) {
            if c.is_ascii_digit() {
                self.i += 1;
            } else if *c == b'.' && !seen_dot {
                seen_dot = true;
                self.i += 1;
            } else {
                break;
            }
        }
        std::str::from_utf8(&self.b[start..self.i])
            .ok()
            .and_then(|t| t.parse().ok())
            .unwrap_or(0.0)
    }
    /// Arc flags are single characters and may be glued to the next number ("0 0 1 0 5").
    fn flag(&mut self) -> bool {
        self.skip();
        let c = self.b.get(self.i).copied().unwrap_or(b'0');
        self.i += 1;
        c == b'1'
    }
}

/// An SVG arc converted to cubic Béziers (endpoint to centre parameterisation).
#[allow(clippy::too_many_arguments)]
fn arc_to_cubics(
    x0: f64,
    y0: f64,
    mut rx: f64,
    mut ry: f64,
    phi_deg: f64,
    large: bool,
    sweep: bool,
    x1: f64,
    y1: f64,
) -> Vec<Seg> {
    if (x0 - x1).abs() < 1e-12 && (y0 - y1).abs() < 1e-12 {
        return vec![];
    }
    rx = rx.abs();
    ry = ry.abs();
    if rx < 1e-12 || ry < 1e-12 {
        return vec![Seg::Line(x1, y1)];
    }
    let phi = phi_deg.to_radians();
    let (sp, cp) = phi.sin_cos();
    let dx = (x0 - x1) / 2.0;
    let dy = (y0 - y1) / 2.0;
    let x1p = cp * dx + sp * dy;
    let y1p = -sp * dx + cp * dy;
    let lambda = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
    if lambda > 1.0 {
        let k = lambda.sqrt();
        rx *= k;
        ry *= k;
    }
    let num = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
    let den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
    let mut coef = (num / den).max(0.0).sqrt();
    if large == sweep {
        coef = -coef;
    }
    let cxp = coef * (rx * y1p / ry);
    let cyp = coef * -(ry * x1p / rx);
    let cx = cp * cxp - sp * cyp + (x0 + x1) / 2.0;
    let cy = sp * cxp + cp * cyp + (y0 + y1) / 2.0;
    let angle = |ux: f64, uy: f64, vx: f64, vy: f64| (ux * vy - uy * vx).atan2(ux * vx + uy * vy);
    let theta1 = angle(1.0, 0.0, (x1p - cxp) / rx, (y1p - cyp) / ry);
    let mut dtheta = angle(
        (x1p - cxp) / rx,
        (y1p - cyp) / ry,
        (-x1p - cxp) / rx,
        (-y1p - cyp) / ry,
    );
    if !sweep && dtheta > 0.0 {
        dtheta -= 2.0 * std::f64::consts::PI;
    } else if sweep && dtheta < 0.0 {
        dtheta += 2.0 * std::f64::consts::PI;
    }
    let n = (dtheta.abs() / (std::f64::consts::PI / 2.0) - 1e-6)
        .ceil()
        .max(1.0) as usize;
    let step = dtheta / n as f64;
    let t = 4.0 / 3.0 * (step / 4.0).tan();
    let mut out = Vec::new();
    let mut a = theta1;
    for _ in 0..n {
        let (s1, c1) = a.sin_cos();
        let (s2, c2) = (a + step).sin_cos();
        let pt = |ca: f64, sa: f64| {
            (
                cx + rx * ca * cp - ry * sa * sp,
                cy + rx * ca * sp + ry * sa * cp,
            )
        };
        let dpt = |ca: f64, sa: f64| (-rx * sa * cp - ry * ca * sp, -rx * sa * sp + ry * ca * cp);
        let (p1x, p1y) = pt(c1, s1);
        let (d1x, d1y) = dpt(c1, s1);
        let (p2x, p2y) = pt(c2, s2);
        let (d2x, d2y) = dpt(c2, s2);
        out.push(Seg::Cubic(
            p1x + t * d1x,
            p1y + t * d1y,
            p2x - t * d2x,
            p2y - t * d2y,
            p2x,
            p2y,
        ));
        a += step;
    }
    // land exactly on the endpoint
    if let Some(Seg::Cubic(_, _, _, _, ex, ey)) = out.last_mut() {
        *ex = x1;
        *ey = y1;
    }
    out
}

/// Parses SVG path data (M L H V C A Z, absolute and relative) into segments.
pub fn parse_path(d: &str) -> Vec<Seg> {
    let mut t = Tokens {
        b: d.as_bytes(),
        i: 0,
    };
    let mut segs = Vec::new();
    let (mut x, mut y) = (0.0f64, 0.0f64);
    let (mut sx, mut sy) = (0.0f64, 0.0f64);
    let mut cmd = b'M';
    loop {
        if let Some(c) = t.command() {
            cmd = c;
        } else if !t.has_number() {
            break;
        }
        let rel = cmd.is_ascii_lowercase();
        match cmd.to_ascii_uppercase() {
            b'Z' => {
                segs.push(Seg::Close);
                x = sx;
                y = sy;
                // a number after Z would loop forever: stop
                if t.has_number() {
                    break;
                }
            }
            b'M' => {
                let (nx, ny) = (t.number(), t.number());
                x = if rel { x + nx } else { nx };
                y = if rel { y + ny } else { ny };
                sx = x;
                sy = y;
                segs.push(Seg::Move(x, y));
                cmd = if rel { b'l' } else { b'L' };
            }
            b'L' => {
                let (nx, ny) = (t.number(), t.number());
                x = if rel { x + nx } else { nx };
                y = if rel { y + ny } else { ny };
                segs.push(Seg::Line(x, y));
            }
            b'H' => {
                let nx = t.number();
                x = if rel { x + nx } else { nx };
                segs.push(Seg::Line(x, y));
            }
            b'V' => {
                let ny = t.number();
                y = if rel { y + ny } else { ny };
                segs.push(Seg::Line(x, y));
            }
            b'C' => {
                let v: Vec<f64> = (0..6).map(|_| t.number()).collect();
                let (ox, oy) = if rel { (x, y) } else { (0.0, 0.0) };
                segs.push(Seg::Cubic(
                    ox + v[0],
                    oy + v[1],
                    ox + v[2],
                    oy + v[3],
                    ox + v[4],
                    oy + v[5],
                ));
                x = ox + v[4];
                y = oy + v[5];
            }
            b'A' => {
                let (rx, ry, rot) = (t.number(), t.number(), t.number());
                let (large, sweep) = (t.flag(), t.flag());
                let (nx, ny) = (t.number(), t.number());
                let (ex, ey) = if rel { (x + nx, y + ny) } else { (nx, ny) };
                segs.extend(arc_to_cubics(x, y, rx, ry, rot, large, sweep, ex, ey));
                x = ex;
                y = ey;
            }
            _ => break,
        }
    }
    segs
}

fn trace(cr: &cairo::Context, segs: &[Seg]) {
    for seg in segs {
        match *seg {
            Seg::Move(x, y) => cr.move_to(x, y),
            Seg::Line(x, y) => cr.line_to(x, y),
            Seg::Cubic(a, b, c, d, e, g) => cr.curve_to(a, b, c, d, e, g),
            Seg::Close => cr.close_path(),
        }
    }
}

/// Draws the icon scaled to `size` pixels with the current source colour.
pub fn draw(cr: &cairo::Context, icon: &Icon, size: f64) {
    cr.save().ok();
    cr.scale(size / 20.0, size / 20.0);
    if let Some(fill) = &icon.fill {
        trace(cr, &parse_path(fill));
        cr.set_fill_rule(cairo::FillRule::EvenOdd);
        cr.fill().ok();
    }
    if let Some(stroke) = &icon.stroke {
        cr.set_line_width(2.0);
        cr.set_line_cap(cairo::LineCap::Round);
        cr.set_line_join(cairo::LineJoin::Round);
        trace(cr, &parse_path(stroke));
        cr.stroke().ok();
    }
    cr.restore().ok();
}

/// An icon widget of `size` x `size` pixels. The colour comes from the CSS `color` of the widget,
/// so a style class decides it (`ic-primary`, `ic-secondary`, `ic-tertiary`, `ic-ember`...).
pub fn widget(icon: Icon, size: i32, color_class: &str) -> gtk::DrawingArea {
    let area = gtk::DrawingArea::new();
    area.set_content_width(size);
    area.set_content_height(size);
    area.set_halign(gtk::Align::Center);
    area.set_valign(gtk::Align::Center);
    area.add_css_class(color_class);
    area.set_draw_func(move |a, cr, w, h| {
        let c = a.color();
        cr.set_source_rgba(
            f64::from(c.red()),
            f64::from(c.green()),
            f64::from(c.blue()),
            f64::from(c.alpha()),
        );
        // centre inside the allocation
        let side = f64::from(w.min(h));
        cr.translate((f64::from(w) - side) / 2.0, (f64::from(h) - side) / 2.0);
        draw(cr, &icon, side);
    });
    area
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn move_line_close() {
        let p = parse_path("M1 2L3 4z");
        assert_eq!(p, [Seg::Move(1.0, 2.0), Seg::Line(3.0, 4.0), Seg::Close]);
    }
    #[test]
    fn relative_and_implicit_repeats() {
        let p = parse_path("M0 0l1 1 2 2h3v-1");
        assert_eq!(
            p,
            [
                Seg::Move(0.0, 0.0),
                Seg::Line(1.0, 1.0),
                Seg::Line(3.0, 3.0),
                Seg::Line(6.0, 3.0),
                Seg::Line(6.0, 2.0)
            ]
        );
    }
    #[test]
    fn numbers_glued_by_minus_signs() {
        let p = parse_path("M10 2a5 5 0 0 1 5 5v6a5 5 0 0 1-10 0");
        // M, arc (cubics), v, arc (cubics): the glued "1-10" must read flag 1 then -10
        let last = p.last().unwrap();
        match last {
            Seg::Cubic(.., x, y) => {
                assert!((x - 5.0).abs() < 1e-9 && (y - 13.0).abs() < 1e-9, "{x},{y}");
            }
            other => panic!("{other:?}"),
        }
    }
    #[test]
    fn a_half_circle_arc_passes_through_its_top() {
        // From (0,0) to (10,0) with radius 5, sweep=1: goes over the top (y negative in svg coordinates).
        let p = parse_path("M0 0a5 5 0 0 1 10 0");
        let ys: Vec<f64> = p
            .iter()
            .filter_map(|s| match s {
                Seg::Cubic(_, _, _, _, x, y) if (*x - 5.0).abs() < 1e-6 => Some(*y),
                _ => None,
            })
            .collect();
        assert_eq!(ys.len(), 1);
        assert!((ys[0] + 5.0).abs() < 1e-6, "{ys:?}");
    }
    #[test]
    fn all_icons_parse_to_something() {
        for icon in [mouse(), keyboard(), headset(), gamepad(), generic(), plug()] {
            let n = icon
                .fill
                .iter()
                .chain(icon.stroke.iter())
                .map(|d| parse_path(d).len())
                .sum::<usize>();
            assert!(n > 2, "{icon:?}");
        }
    }
    /// Bounding box of the curve control polygon of a path (good enough for pills and dots).
    fn bbox(d: &str) -> (f64, f64, f64, f64) {
        let (mut x0, mut y0, mut x1, mut y1) = (f64::MAX, f64::MAX, f64::MIN, f64::MIN);
        let mut add = |x: f64, y: f64| {
            x0 = x0.min(x);
            y0 = y0.min(y);
            x1 = x1.max(x);
            y1 = y1.max(y);
        };
        for seg in parse_path(d) {
            match seg {
                Seg::Move(x, y) | Seg::Line(x, y) => add(x, y),
                Seg::Cubic(_, _, _, _, x, y) => add(x, y),
                Seg::Close => {}
            }
        }
        (x0, y0, x1, y1)
    }

    #[test]
    fn a_standing_pill_fills_its_rectangle() {
        // Cap(9.1, 4.6, 1.8, 4.8): the quarter-arc end points reach x 9.1..10.9 and y 4.6..9.4
        let (x0, y0, x1, y1) = bbox(&cap(9.1, 4.6, 1.8, 4.8));
        assert!(
            (x0 - 9.1).abs() < 1e-9 && (x1 - 10.9).abs() < 1e-9,
            "{x0} {x1}"
        );
        assert!(
            (y0 - 4.6).abs() < 1e-9 && (y1 - 9.4).abs() < 1e-9,
            "{y0} {y1}"
        );
    }
    #[test]
    fn a_lying_pill_and_a_dot_close_their_paths() {
        let p = parse_path(&cap(6.8, 10.9, 6.4, 1.8));
        assert_eq!(p.last(), Some(&Seg::Close));
        let d = parse_path(&dot(10.0, 6.6, 0.9));
        assert_eq!(
            d.iter().filter(|s| matches!(s, Seg::Cubic(..))).count(),
            4,
            "two half circles of two cubics each"
        );
    }
}
