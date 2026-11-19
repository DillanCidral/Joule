//! The colours of the Joule drawings (ported from the "solid" column of `Ui/Theme.cs`): the ring track,
//! the colour of an old value, the ember accent and the alert red. The window follows libadwaita.

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Look {
    pub dark: bool,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Palette {
    pub accent: (u8, u8, u8),
    pub alert: (u8, u8, u8),
}

/// The Joule yellow of the icon.
pub const ACCENT: (u8, u8, u8) = (0xFF, 0xC2, 0x1F);

pub fn palette(look: &Look) -> Palette {
    Palette {
        // On the light theme a deeper yellow, so the arc reads on white.
        accent: if look.dark {
            ACCENT
        } else {
            (0xE0, 0xA2, 0x00)
        },
        alert: if look.dark {
            (0xFF, 0x99, 0xA4)
        } else {
            (0xC4, 0x2B, 0x1C)
        },
    }
}

fn argb(v: u32) -> (f64, f64, f64, f64) {
    let c = |s: u32| f64::from((v >> s) & 255) / 255.0;
    (c(16), c(8), c(0), c(24))
}

/// `Level.Track` of the solid look.
pub fn track_color(look: &Look) -> (f64, f64, f64, f64) {
    argb(if look.dark { 0xFF3A3A3A } else { 0xFFDCDCDC })
}

/// `Level.Stale` of the solid look: an old value.
pub fn stale_rgba(look: &Look) -> (f64, f64, f64, f64) {
    argb(if look.dark { 0xFF6B6B6B } else { 0xFFA3A3A3 })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn colours_follow_the_theme() {
        assert_eq!(palette(&Look { dark: true }).accent, ACCENT);
        assert_ne!(palette(&Look { dark: false }).accent, ACCENT);
        let (r, _, _, a) = track_color(&Look { dark: true });
        assert!((r - 58.0 / 255.0).abs() < 1e-9 && a == 1.0);
        assert!(stale_rgba(&Look { dark: false }).0 > stale_rgba(&Look { dark: true }).0);
    }
}
