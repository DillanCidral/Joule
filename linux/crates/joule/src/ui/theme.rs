//! The tokens of the Joule design (ported from `Ui/Theme.cs`) as one CSS sheet. Columns: tinted
//! glass, system, solid, each dark and light. The sheet is rebuilt whenever the appearance, the glass
//! opacity or the system dark/light switch changes.

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Appearance {
    Glass,
    System,
    Solid,
}

pub fn parse(value: &str) -> Appearance {
    match value.to_lowercase().as_str() {
        "system" => Appearance::System,
        "solid" => Appearance::Solid,
        _ => Appearance::Glass,
    }
}

pub fn to_setting(a: Appearance) -> &'static str {
    match a {
        Appearance::System => "system",
        Appearance::Solid => "solid",
        Appearance::Glass => "glass",
    }
}

/// `#AARRGGBB` (WPF) to a CSS `rgba()`.
fn rgba(argb: &str) -> String {
    let h = argb.trim_start_matches('#');
    let v = u32::from_str_radix(h, 16).unwrap_or(0);
    let (a, r, g, b) = ((v >> 24) & 255, (v >> 16) & 255, (v >> 8) & 255, v & 255);
    format!("rgba({r},{g},{b},{:.3})", f64::from(a) / 255.0)
}

/// Token table: glass dark, glass light, system dark, system light, solid dark, solid light.
const TOKENS: &[(&str, [&str; 6])] = &[
    (
        "window-tint",
        [
            "#80000000",
            "#73FFFFFF",
            "#F0202020",
            "#F2F3F3F3",
            "#FF1C1C1C",
            "#FFF3F3F3",
        ],
    ),
    (
        "window-stroke",
        [
            "#00000000",
            "#00000000",
            "#19FFFFFF",
            "#0F000000",
            "#FF2E2E2E",
            "#FFE0E0E0",
        ],
    ),
    (
        "card-fill",
        [
            "#0FFFFFFF",
            "#73FFFFFF",
            "#0DFFFFFF",
            "#B3FFFFFF",
            "#FF262626",
            "#FFFFFFFF",
        ],
    ),
    (
        "card-stroke",
        [
            "#00000000",
            "#00000000",
            "#1A000000",
            "#0F000000",
            "#00000000",
            "#FFE8E8E8",
        ],
    ),
    (
        "card-hover",
        [
            "#1CFFFFFF",
            "#A6FFFFFF",
            "#15FFFFFF",
            "#80F9F9F9",
            "#FF2D2D2D",
            "#FFFAFAFA",
        ],
    ),
    (
        "tile-fill",
        [
            "#14FFFFFF",
            "#0F000000",
            "#0FFFFFFF",
            "#08000000",
            "#FF2F2F2F",
            "#FFF0F0F0",
        ],
    ),
    (
        "text-primary",
        [
            "#FFFFFFFF",
            "#E6000000",
            "#FFFFFFFF",
            "#E4000000",
            "#FFFFFFFF",
            "#FF1A1A1A",
        ],
    ),
    (
        "text-secondary",
        [
            "#C7FFFFFF",
            "#B3000000",
            "#C5FFFFFF",
            "#9E000000",
            "#FFC8C8C8",
            "#FF4D4D4D",
        ],
    ),
    (
        "text-tertiary",
        [
            "#9EFFFFFF",
            "#94000000",
            "#8BFFFFFF",
            "#8A000000",
            "#FF9D9D9D",
            "#FF6B6B6B",
        ],
    ),
    (
        "divider",
        [
            "#14FFFFFF",
            "#12000000",
            "#15FFFFFF",
            "#0F000000",
            "#FF2E2E2E",
            "#FFE8E8E8",
        ],
    ),
    (
        "level-track",
        [
            "#26FFFFFF",
            "#1F000000",
            "#2BFFFFFF",
            "#24000000",
            "#FF3A3A3A",
            "#FFDCDCDC",
        ],
    ),
    (
        "level-stale",
        [
            "#66FFFFFF",
            "#57000000",
            "#5DFFFFFF",
            "#52000000",
            "#FF6B6B6B",
            "#FFA3A3A3",
        ],
    ),
    (
        "button-fill",
        [
            "#0FFFFFFF",
            "#80FFFFFF",
            "#0FFFFFFF",
            "#B3FFFFFF",
            "#FF2A2A2A",
            "#FFFFFFFF",
        ],
    ),
    (
        "segment-selected",
        [
            "#26FFFFFF",
            "#FFFFFFFF",
            "#1FFFFFFF",
            "#FFFFFFFF",
            "#FF3A3A3A",
            "#FFFFFFFF",
        ],
    ),
    (
        "slider-thumb",
        [
            "#FF454545",
            "#FFFFFFFF",
            "#FF454545",
            "#FFFFFFFF",
            "#FF454545",
            "#FFFFFFFF",
        ],
    ),
];

/// Everything the sheet needs to know.
#[derive(Clone, Copy, Debug)]
pub struct Look {
    pub appearance: Appearance,
    pub dark: bool,
    /// Tint opacity of the glass, in percent.
    pub glass_opacity: i32,
}

fn token(look: &Look, name: &str) -> String {
    let col = match look.appearance {
        Appearance::Glass => 0,
        Appearance::System => 2,
        Appearance::Solid => 4,
    } + usize::from(!look.dark);
    let raw = TOKENS
        .iter()
        .find(|(n, _)| *n == name)
        .map(|(_, v)| v[col])
        .unwrap_or("#00000000");
    rgba(raw)
}

fn mix(a: (u8, u8, u8), b: (u8, u8, u8), t: f64) -> (u8, u8, u8) {
    let m = |x: u8, y: u8| (f64::from(x) + (f64::from(y) - f64::from(x)) * t).round() as u8;
    (m(a.0, b.0), m(a.1, b.1), m(a.2, b.2))
}

fn hex((r, g, b): (u8, u8, u8)) -> String {
    format!("#{r:02X}{g:02X}{b:02X}")
}

pub const ACCENT: (u8, u8, u8) = (0xFF, 0xC2, 0x1F);

/// Colours the panel code needs outside CSS (cairo drawing).
#[derive(Clone, Copy, Debug)]
pub struct Palette {
    pub accent: (u8, u8, u8),
    pub ember_light: (u8, u8, u8),
    pub alert: (u8, u8, u8),
    pub alert_light: (u8, u8, u8),
}

pub fn palette(look: &Look, system_accent: Option<(u8, u8, u8)>) -> Palette {
    let accent = match (look.appearance, system_accent) {
        (Appearance::System, Some(c)) => c,
        _ => ACCENT,
    };
    // Accent as text: yellow vanishes on the light theme (tokens v3: #826313).
    let accent_text = if look.appearance != Appearance::System && !look.dark {
        (0x82, 0x63, 0x13)
    } else {
        accent
    };
    let alert = if look.dark {
        (0xFF, 0x99, 0xA4)
    } else {
        (0xC4, 0x2B, 0x1C)
    };
    let white = (255, 255, 255);
    let black = (0, 0, 0);
    Palette {
        accent: accent_text,
        // Dark: the core #FFE38A of the icon; light: darker.
        ember_light: if look.dark {
            mix(accent_text, white, 0.5)
        } else {
            mix(accent_text, black, 0.2)
        },
        alert,
        alert_light: if look.dark {
            mix(alert, white, 0.35)
        } else {
            mix(alert, black, 0.2)
        },
    }
}

/// The whole stylesheet.
pub fn css(look: &Look, system_accent: Option<(u8, u8, u8)>) -> String {
    let t = |n: &str| token(look, n);
    let p = palette(look, system_accent);
    let accent = match (look.appearance, system_accent) {
        (Appearance::System, Some(c)) => c,
        _ => ACCENT,
    };
    let accent_fg = if look.appearance == Appearance::System && !look.dark {
        "#FFFFFF"
    } else {
        "#161616"
    };
    let accent_fg = if look.appearance == Appearance::System && look.dark {
        "#000000"
    } else {
        accent_fg
    };
    let window_bg = if look.appearance == Appearance::Glass {
        let alpha = (f64::from(look.glass_opacity.clamp(0, 100)) * 2.55).round() / 255.0;
        let c = if look.dark { 0 } else { 255 };
        format!("rgba({c},{c},{c},{alpha:.3})")
    } else {
        t("window-tint")
    };
    let radius = 8;
    let accent_css = hex(accent);
    let alert = hex(p.alert);

    format!(
        r#"
window.joule {{ background: transparent; color: {primary}; }}
.joule-root {{
  background: {window_bg};
  border: 1px solid {window_stroke};
  border-radius: {radius}px;
}}
.joule-root label {{ color: inherit; }}

.t-primary {{ color: {primary}; }}
.t-secondary {{ color: {secondary}; }}
.t-tertiary {{ color: {tertiary}; }}
.t-accent {{ color: {accent_css}; }}
.t-alert {{ color: {alert}; }}
.t-accent-fg {{ color: {accent_fg}; }}

.joule-brand {{ font-family: "Geist", sans-serif; font-weight: 600; font-size: 15px; }}
.joule-caption {{ font-size: 12px; color: {tertiary}; }}
.joule-group-label {{ font-size: 12px; font-weight: 600; color: {secondary}; margin: 0 0 6px 8px; }}
.joule-value {{ font-size: 20px; font-weight: 600; font-feature-settings: "tnum"; }}
.joule-unit {{ font-size: 13px; font-weight: 600; }}
.joule-chip {{ font-size: 13px; font-weight: 600; background: {tile}; border-radius: 999px; padding: 3px 10px; }}
.joule-title {{ font-size: 14px; font-weight: 600; }}
.joule-empty-title {{ font-size: 16px; font-weight: 600; }}
.joule-empty-body {{ font-size: 13px; color: {secondary}; }}

.joule-card {{
  background: {card_fill};
  border: 1px solid {card_stroke};
  border-radius: 6px;
  padding: 10px 12px 12px 12px;
  transition: background 150ms ease-out;
}}
.joule-card:hover {{ background: {card_hover}; }}
.joule-tile {{ background: {tile}; border-radius: 6px; min-width: 32px; min-height: 32px; }}
.joule-dashed {{ border: 1px dashed {track}; border-radius: 6px; min-width: 32px; min-height: 32px; }}
.joule-dashed-round {{ border: 1.5px dashed {track}; border-radius: 16px; min-width: 32px; min-height: 32px; }}

.joule-group {{ background: {card_fill}; border: 1px solid {card_stroke}; border-radius: 6px; }}
.joule-rule {{ background: {divider}; min-height: 1px; }}
.joule-footer {{ border-top: 1px solid {divider}; }}

button.joule-flat {{
  background: transparent; border: none; box-shadow: none; outline: none; padding: 0; min-height: 0; min-width: 0;
  border-radius: 6px; color: {primary}; font-weight: 400; transition: background 150ms ease-out;
}}
button.joule-flat:hover {{ background: {card_hover}; }}
button.joule-icon {{
  background: transparent; border: none; box-shadow: none; padding: 0; border-radius: 4px;
  min-width: 32px; min-height: 32px; transition: background 150ms ease-out;
}}
button.joule-icon:hover {{ background: {card_hover}; }}
button.joule-icon:active {{ opacity: 0.7; }}
button.joule-accent {{
  background: {accent_css}; color: {accent_fg}; border: none; box-shadow: none; border-radius: 4px;
  min-height: 32px; padding: 0 16px; font-size: 13px; font-weight: 600;
}}
button.joule-accent:hover {{ opacity: 0.9; }}
button.joule-outline {{
  background: {button_fill}; color: {primary}; border: 1px solid {track}; box-shadow: none; border-radius: 4px;
  min-height: 32px; padding: 0 12px; font-size: 13px; transition: background 150ms ease-out;
}}
button.joule-outline:hover {{ background: {card_hover}; }}
button.joule-outline:disabled {{ color: {tertiary}; }}
.joule-pill {{ background: {button_fill}; border-radius: 4px; min-height: 32px; padding: 0 12px; }}

switch.joule-switch {{
  min-width: 40px; min-height: 20px; border-radius: 10px; border: 1px solid {secondary};
  background: transparent; box-shadow: none; padding: 0; outline: none;
}}
switch.joule-switch slider {{
  min-width: 12px; min-height: 12px; margin: 3px; border-radius: 6px; background: {secondary};
  border: none; box-shadow: none; transition: margin 150ms ease-out;
}}
switch.joule-switch:checked {{ background: {accent_css}; border-color: {accent_css}; }}
switch.joule-switch:checked slider {{ background: {accent_fg}; }}
switch.joule-switch image, switch.joule-switch label {{ color: transparent; }}

.joule-segtrack {{ background: {track}; border-radius: 6px; padding: 2px; }}
.joule-segtrack button {{
  background: transparent; border: none; box-shadow: none; border-radius: 4px; min-height: 0; min-width: 0;
  padding: 6px 4px; font-size: 13px; color: {secondary};
}}
.joule-segtrack button:hover {{ color: {primary}; }}
.joule-segtrack button:checked {{ background: {segment_selected}; color: {primary}; font-weight: 600; }}
.joule-segtrack button:disabled {{ opacity: 0.4; }}
.joule-segtrack button.seg-4-6 {{ padding: 6px 4px; }}
.joule-segtrack button.seg-12-5 {{ padding: 5px 12px; }}
.joule-segtrack button.seg-10-5 {{ padding: 5px 10px; }}
.joule-size-12 {{ font-size: 12px; }}
.joule-size-13 {{ font-size: 13px; }}
.joule-size-10 {{ font-size: 10px; font-feature-settings: "tnum"; }}
.joule-size-11 {{ font-size: 11px; }}
.joule-size-14 {{ font-size: 14px; font-weight: 600; font-feature-settings: "tnum"; }}
.joule-widget {{ background: {window_bg}; border: 1px solid {window_stroke}; border-radius: 8px; }}
.joule-widget-title {{ font-size: 12px; font-weight: 600; }}
.joule-widget-row {{ border-radius: 6px; padding: 6px; transition: background 150ms ease-out; }}
.joule-widget-row:hover {{ background: {card_hover}; }}
button.joule-small-icon {{ min-width: 28px; min-height: 28px; }}

scale.joule-scale {{ min-height: 20px; padding: 0; }}
scale.joule-scale trough {{ min-height: 4px; border-radius: 2px; background: {track}; border: none; }}
scale.joule-scale highlight {{ min-height: 4px; border-radius: 2px; background: {accent_css}; border: none; }}
scale.joule-scale slider {{
  min-width: 20px; min-height: 20px; border-radius: 10px; border: none; margin: -8px;
  background-image: radial-gradient(circle at center, {accent_css} 0, {accent_css} 6px, {thumb} 6.5px);
  box-shadow: 0 1px 3px rgba(0,0,0,0.3);
}}

scrollbar.joule-scroll {{ background: transparent; }}
scrollbar.joule-scroll slider {{ min-width: 4px; border-radius: 2px; background: {tertiary}; opacity: 0.45; }}
scrollbar.joule-scroll slider:hover {{ min-width: 6px; opacity: 0.75; }}
scrollbar.joule-scroll trough {{ background: transparent; border: none; }}
"#,
        primary = t("text-primary"),
        secondary = t("text-secondary"),
        tertiary = t("text-tertiary"),
        window_bg = window_bg,
        window_stroke = t("window-stroke"),
        radius = radius,
        accent_css = accent_css,
        accent_fg = accent_fg,
        alert = alert,
        tile = t("tile-fill"),
        track = t("level-track"),
        card_fill = t("card-fill"),
        card_stroke = t("card-stroke"),
        card_hover = t("card-hover"),
        divider = t("divider"),
        button_fill = t("button-fill"),
        segment_selected = t("segment-selected"),
        thumb = t("slider-thumb"),
    )
}

/// `level-stale` as `rgba()`, for the cairo bar.
pub fn stale_color(look: &Look) -> String {
    token(look, "level-stale")
}

/// An `rgba(r,g,b,a)` string to cairo components.
pub fn parse_rgba(css: &str) -> (f64, f64, f64, f64) {
    let inner = css.trim().trim_start_matches("rgba(").trim_end_matches(')');
    let v: Vec<f64> = inner
        .split(',')
        .filter_map(|p| p.trim().parse().ok())
        .collect();
    if v.len() == 4 {
        (v[0] / 255.0, v[1] / 255.0, v[2] / 255.0, v[3])
    } else {
        (0.0, 0.0, 0.0, 0.0)
    }
}

/// Track colour (`level-track`) as cairo components.
pub fn track_color(look: &Look) -> (f64, f64, f64, f64) {
    parse_rgba(&token(look, "level-track"))
}

pub fn stale_rgba(look: &Look) -> (f64, f64, f64, f64) {
    parse_rgba(&stale_color(look))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn look(a: Appearance, dark: bool) -> Look {
        Look {
            appearance: a,
            dark,
            glass_opacity: 30,
        }
    }

    #[test]
    fn rgba_converts_argb() {
        assert_eq!(rgba("#80000000"), "rgba(0,0,0,0.502)");
        assert_eq!(rgba("#FFFFC21F"), "rgba(255,194,31,1.000)");
    }
    #[test]
    fn the_columns_follow_appearance_and_theme() {
        assert_eq!(
            token(&look(Appearance::Solid, true), "window-tint"),
            "rgba(28,28,28,1.000)"
        );
        assert_eq!(
            token(&look(Appearance::Solid, false), "window-tint"),
            "rgba(243,243,243,1.000)"
        );
        assert_eq!(
            token(&look(Appearance::System, true), "window-tint"),
            rgba("#F0202020")
        );
        assert_eq!(
            token(&look(Appearance::Glass, true), "text-primary"),
            "rgba(255,255,255,1.000)"
        );
    }
    #[test]
    fn glass_opacity_drives_the_tint() {
        let mut l = look(Appearance::Glass, true);
        l.glass_opacity = 50;
        // Same arithmetic as the C# app: round(50 * 2.55) = 127 (floating point), so 127/255.
        assert!(css(&l, None).contains("rgba(0,0,0,0.498)"));
        l.dark = false;
        assert!(css(&l, None).contains("rgba(255,255,255,0.498)"));
    }
    #[test]
    fn accent_is_the_ember_yellow_except_in_system() {
        let l = look(Appearance::Glass, true);
        assert!(css(&l, Some((10, 20, 30))).contains("#FFC21F"));
        let s = look(Appearance::System, true);
        assert!(css(&s, Some((10, 20, 30))).contains("#0A141E"));
    }
    #[test]
    fn light_theme_accent_text_is_darker() {
        assert_eq!(
            palette(&look(Appearance::Glass, false), None).accent,
            (0x82, 0x63, 0x13)
        );
        assert_eq!(palette(&look(Appearance::Glass, true), None).accent, ACCENT);
    }
    #[test]
    fn parse_and_back() {
        assert_eq!(parse("SYSTEM"), Appearance::System);
        assert_eq!(parse("whatever"), Appearance::Glass);
        assert_eq!(to_setting(Appearance::Solid), "solid");
    }
    #[test]
    fn rgba_roundtrip_for_cairo() {
        let (r, g, b, a) = parse_rgba("rgba(255,0,51,0.500)");
        assert!(
            (r - 1.0).abs() < 1e-9 && g == 0.0 && (b - 0.2).abs() < 1e-9 && (a - 0.5).abs() < 1e-9
        );
    }
}
