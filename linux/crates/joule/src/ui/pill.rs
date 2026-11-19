//! The "Brasa" tray icon of Joule (from `TrayIconFactory.cs`), drawn at run time on a 32 grid.
//! No percentage in the icon (the number lives in the tooltip, the menu and the panel); each state
//! changes shape: fine = the light bar of the logo, lit and fading at the tip; low = a dim pill with a
//! short red stretch; reading = three dots; no value = only the outline. The dark halo keeps it
//! readable on any bar. The GNOME top bar is always dark, so the glyph is white.

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum PillState {
    /// A reading is running: three dots.
    Reading,
    /// No device with a value: only the outline.
    Empty,
    /// The weakest device is below the threshold.
    Low,
    /// Everything is fine.
    Ok,
}

#[derive(Clone, Copy)]
struct Rgba(f32, f32, f32, f32);

const LOW: Rgba = Rgba(
    0xF0 as f32 / 255.0,
    0x45 as f32 / 255.0,
    0x3A as f32 / 255.0,
    1.0,
);
const EMBER: Rgba = Rgba(1.0, 0xC2 as f32 / 255.0, 0x1F as f32 / 255.0, 1.0);
const EMBER_LIGHT: Rgba = Rgba(1.0, 0xE3 as f32 / 255.0, 0x8A as f32 / 255.0, 1.0);
const GLYPH: Rgba = Rgba(1.0, 1.0, 1.0, 1.0);
const HALO: Rgba = Rgba(0.0, 0.0, 0.0, 140.0 / 255.0);
const BOX: Rgba = Rgba(1.0, 1.0, 1.0, 56.0 / 255.0);

/// Signed distance to a horizontal pill (stadium): x, y, w, h in grid units; negative inside.
fn pill_distance(px: f32, py: f32, x: f32, y: f32, w: f32, h: f32) -> f32 {
    let r = h / 2.0;
    let cy = y + r;
    let left = x + r;
    let right = x + w - r;
    let cx = px.clamp(left, right);
    ((px - cx).powi(2) + (py - cy).powi(2)).sqrt() - r
}

struct Canvas {
    size: usize,
    scale: f32,
    px: Vec<Rgba>,
}

impl Canvas {
    fn new(size: usize) -> Self {
        Self {
            size,
            scale: size as f32 / 32.0,
            px: vec![Rgba(0.0, 0.0, 0.0, 0.0); size * size],
        }
    }

    /// Paints `color(t)` where `coverage(grid x, grid y)` says so; `t` is the 0..1 position along x of `span`.
    fn paint(
        &mut self,
        coverage: impl Fn(f32, f32) -> f32,
        color: impl Fn(f32) -> Rgba,
        span: (f32, f32),
    ) {
        for iy in 0..self.size {
            for ix in 0..self.size {
                let gx = (ix as f32 + 0.5) / self.scale;
                let gy = (iy as f32 + 0.5) / self.scale;
                // one grid unit is `scale` pixels: coverage is in pixels of distance
                let cov = coverage(gx, gy).clamp(0.0, 1.0);
                if cov <= 0.0 {
                    continue;
                }
                let t = ((gx - span.0) / (span.1 - span.0)).clamp(0.0, 1.0);
                let c = color(t);
                let a = c.3 * cov;
                let dst = &mut self.px[iy * self.size + ix];
                let out_a = a + dst.3 * (1.0 - a);
                if out_a > 0.0 {
                    let mix = |s: f32, d: f32| (s * a + d * dst.3 * (1.0 - a)) / out_a;
                    *dst = Rgba(mix(c.0, dst.0), mix(c.1, dst.1), mix(c.2, dst.2), out_a);
                }
            }
        }
    }

    fn fill_pill(&mut self, x: f32, y: f32, w: f32, h: f32, color: impl Fn(f32) -> Rgba) {
        let s = self.scale;
        self.paint(
            move |gx, gy| 0.5 - pill_distance(gx, gy, x, y, w, h) * s,
            color,
            (x, x + w),
        );
    }

    fn stroke_pill(&mut self, x: f32, y: f32, w: f32, h: f32, width: f32, color: Rgba) {
        let s = self.scale;
        self.paint(
            move |gx, gy| 0.5 - (pill_distance(gx, gy, x, y, w, h).abs() - width / 2.0) * s,
            move |_| color,
            (x, x + w),
        );
    }

    fn fill_dot(&mut self, cx: f32, cy: f32, r: f32, color: Rgba) {
        let s = self.scale;
        self.paint(
            move |gx, gy| 0.5 - (((gx - cx).powi(2) + (gy - cy).powi(2)).sqrt() - r) * s,
            move |_| color,
            (cx - r, cx + r),
        );
    }

    /// ARGB32, network byte order (the StatusNotifierItem pixmap format).
    fn into_argb(self) -> Vec<u8> {
        let mut out = Vec::with_capacity(self.px.len() * 4);
        for p in self.px {
            let b = |v: f32| (v.clamp(0.0, 1.0) * 255.0).round() as u8;
            out.extend_from_slice(&[b(p.3), b(p.0), b(p.1), b(p.2)]);
        }
        out
    }
}

fn with_alpha(c: Rgba, a: f32) -> Rgba {
    Rgba(c.0, c.1, c.2, c.3 * a)
}

fn lerp(a: Rgba, b: Rgba, t: f32) -> Rgba {
    Rgba(
        a.0 + (b.0 - a.0) * t,
        a.1 + (b.1 - a.1) * t,
        a.2 + (b.2 - a.2) * t,
        a.3 + (b.3 - a.3) * t,
    )
}

/// Light at the start, amber at 30 %, almost gone at the tip, like the logo.
fn ember_fade(t: f32) -> Rgba {
    if t < 0.3 {
        lerp(EMBER_LIGHT, EMBER, t / 0.3)
    } else {
        lerp(EMBER, with_alpha(EMBER, 0.2), (t - 0.3) / 0.7)
    }
}

/// ARGB32 pixels of the icon at `size` x `size`.
pub fn render(state: PillState, size: usize) -> Vec<u8> {
    let mut c = Canvas::new(size);
    match state {
        PillState::Reading => {
            c.fill_pill(2.0, 9.5, 28.0, 13.0, |_| BOX);
            for (x, alpha) in [(9.0, 1.0), (16.0, 0.75), (23.0, 0.5)] {
                c.fill_dot(x, 16.0, 2.2, with_alpha(EMBER, alpha));
            }
        }
        PillState::Empty => c.stroke_pill(3.0, 10.5, 26.0, 11.0, 2.0, GLYPH),
        PillState::Low => {
            c.fill_pill(2.0, 9.5, 28.0, 13.0, |_| BOX);
            c.fill_pill(4.5, 12.0, 11.0, 8.0, |_| LOW);
        }
        PillState::Ok => {
            c.fill_pill(0.75, 8.25, 30.5, 15.5, |_| HALO);
            c.fill_pill(2.0, 9.5, 28.0, 13.0, ember_fade);
        }
    }
    c.into_argb()
}

#[cfg(test)]
mod tests {
    use super::*;

    fn at(data: &[u8], size: usize, x: usize, y: usize) -> [u8; 4] {
        let i = (y * size + x) * 4;
        [data[i], data[i + 1], data[i + 2], data[i + 3]]
    }

    #[test]
    fn buffer_has_the_right_size() {
        assert_eq!(render(PillState::Ok, 32).len(), 32 * 32 * 4);
        assert_eq!(render(PillState::Ok, 64).len(), 64 * 64 * 4);
    }
    #[test]
    fn ok_pill_is_lit_at_the_start_and_fades_at_the_tip() {
        let d = render(PillState::Ok, 32);
        let start = at(&d, 32, 6, 16);
        let tip = at(&d, 32, 28, 16);
        assert_eq!(start[0], 255, "opaque");
        assert!(
            start[1] > 200 && start[2] > 150,
            "light ember at the start: {start:?}"
        );
        assert!(tip[0] < start[0], "the tip is more transparent: {tip:?}");
    }
    #[test]
    fn corners_stay_transparent() {
        for s in [
            PillState::Ok,
            PillState::Low,
            PillState::Empty,
            PillState::Reading,
        ] {
            let d = render(s, 32);
            assert_eq!(at(&d, 32, 0, 0)[0], 0, "{s:?}");
            assert_eq!(at(&d, 32, 31, 31)[0], 0, "{s:?}");
        }
    }
    #[test]
    fn low_has_a_red_stretch_and_a_dim_box() {
        let d = render(PillState::Low, 32);
        let red = at(&d, 32, 9, 16);
        assert!(red[1] > 200 && red[2] < 100 && red[3] < 100, "red: {red:?}");
        let box_part = at(&d, 32, 24, 16);
        assert!(
            box_part[0] > 0 && box_part[0] < 100,
            "dim white box: {box_part:?}"
        );
    }
    #[test]
    fn empty_is_only_an_outline() {
        let d = render(PillState::Empty, 32);
        assert_eq!(at(&d, 32, 16, 16)[0], 0, "hollow inside");
        assert!(at(&d, 32, 16, 10)[0] > 0, "outline on the top edge");
    }
    #[test]
    fn reading_shows_three_dots() {
        let d = render(PillState::Reading, 32);
        let dots: Vec<u8> = [9, 16, 23].iter().map(|x| at(&d, 32, *x, 16)[0]).collect();
        assert!(
            dots[0] > dots[1] && dots[1] > dots[2],
            "fading dots: {dots:?}"
        );
    }

    /// Writes a contact sheet of the four states as a PPM (dark bar background) for a human look.
    /// `JOULE_PILL_PREVIEW=/path/sheet.ppm cargo test -p joule pill_preview -- --ignored`
    #[test]
    #[ignore]
    fn pill_preview() {
        let Ok(path) = std::env::var("JOULE_PILL_PREVIEW") else {
            return;
        };
        let size = 64;
        let states = [
            PillState::Ok,
            PillState::Low,
            PillState::Empty,
            PillState::Reading,
        ];
        let (w, h) = (size * states.len(), size);
        let mut out = format!("P6\n{w} {h}\n255\n").into_bytes();
        for y in 0..h {
            for (i, s) in states.iter().enumerate() {
                let d = render(*s, size);
                for x in 0..size {
                    let p = &d[(y * size + x) * 4..][..4];
                    let a = f32::from(p[0]) / 255.0;
                    let bg = [0x2Au8, 0x2A, 0x2A];
                    for c in 0..3 {
                        out.push((f32::from(p[c + 1]) * a + f32::from(bg[c]) * (1.0 - a)) as u8);
                    }
                }
                let _ = i;
            }
        }
        std::fs::write(path, out).unwrap();
    }
}
