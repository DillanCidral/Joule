//! The small amount of CSS on top of libadwaita: the Joule yellow as the accent (a darker text tone on
//! the light theme, like `Accent.Text` in `Theme.cs`), the Geist name and the tabular values.

pub const CSS: &str = r#"
:root {
  --accent-bg-color: #FFC21F;
  --accent-fg-color: rgb(0 0 0 / 85%);
  --accent-color: #826313;
}
@media (prefers-color-scheme: dark) {
  :root { --accent-color: #FFC21F; }
}
.joule-brand { font-family: "Geist", sans-serif; font-weight: 600; font-size: 15px; }
.joule-value { font-size: 20px; font-weight: 600; font-feature-settings: "tnum"; }
.joule-unit { font-size: 13px; font-weight: 600; }
.joule-low { color: var(--error-color); }
.joule-stale { opacity: 0.55; }
.joule-chip {
  font-size: 13px; font-weight: 600; padding: 3px 10px; border-radius: 999px;
  background: color-mix(in srgb, currentColor 10%, transparent);
}
row.joule-device { min-height: 64px; }
"#;

pub fn install() {
    let provider = gtk::CssProvider::new();
    provider.load_from_string(CSS);
    if let Some(display) = gtk::gdk::Display::default() {
        gtk::style_context_add_provider_for_display(
            &display,
            &provider,
            gtk::STYLE_PROVIDER_PRIORITY_APPLICATION,
        );
    }
}
