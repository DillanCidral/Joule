use crate::{log, paths};
use serde::{Deserialize, Serialize};
use std::path::Path;

/// JSON keys keep the names of the C# app (camelCase).
#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct Settings {
    pub low_battery_threshold: i32,
    pub alerts_enabled: bool,
    /// One handle opened once, listening. Not polling.
    pub passive_listeners_enabled: bool,
    /// Refuses every question, even for allowed brands.
    pub listen_only: bool,
    /// Brands (VID hex, e.g. "046D") the user allowed Joule to ask.
    pub ask_allowed_vids: Vec<String>,
    pub show_unsupported: bool,
    /// "glass" (tinted glass), "system" or "solid".
    pub appearance: String,
    /// Tint opacity of the glass, in percent.
    pub glass_opacity: i32,
    /// Blur of the background behind the glass (needs a compositor that can blur).
    pub glass_blur: bool,
    pub widget_enabled: bool,
    /// "right" or "left".
    pub widget_side: String,
    pub widget_auto_hide: bool,
    pub widget_monitor: String,
    pub widget_offset: f64,
    /// "joule", "windows", "white", "neutral" or "#RRGGBB".
    pub widget_handle_color: String,
    /// Connect and low-battery notices: "widget", "windows" (the desktop) or "off".
    pub notify_place: String,
    pub notify_sound: bool,
    /// "auto", "pt" or "en".
    pub language: String,
    pub last_version: String,
    pub devices_seen: Vec<String>,
    pub low_alerts: u32,
    pub onboarding_done: bool,
}

impl Default for Settings {
    fn default() -> Self {
        Self {
            low_battery_threshold: 20,
            alerts_enabled: true,
            passive_listeners_enabled: true,
            listen_only: false,
            ask_allowed_vids: Vec::new(),
            show_unsupported: true,
            appearance: "glass".into(),
            glass_opacity: 30,
            glass_blur: true,
            // Linux: the widget floats (no edge docking on Wayland), so it is opt-in.
            widget_enabled: false,
            widget_side: "right".into(),
            widget_auto_hide: true,
            widget_monitor: String::new(),
            widget_offset: 0.44,
            widget_handle_color: "joule".into(),
            notify_place: "widget".into(),
            notify_sound: true,
            language: "auto".into(),
            last_version: String::new(),
            devices_seen: Vec::new(),
            low_alerts: 0,
            onboarding_done: false,
        }
    }
}

impl Settings {
    pub fn load_from(dir: &Path) -> Settings {
        let file = paths::settings_file(dir);
        let Ok(text) = std::fs::read_to_string(&file) else {
            return Settings::default();
        };
        match serde_json::from_str::<Settings>(&text) {
            Ok(mut s) => {
                s.low_battery_threshold = s.low_battery_threshold.clamp(1, 99);
                s
            }
            Err(e) => {
                log::error("Settings.load", &e);
                Settings::default()
            }
        }
    }

    pub fn save_to(&self, dir: &Path) {
        match serde_json::to_string_pretty(self) {
            Ok(json) => {
                if let Err(e) = paths::write_atomic(&paths::settings_file(dir), &json) {
                    log::error("Settings.save", &e);
                }
            }
            Err(e) => log::error("Settings.save", &e),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn defaults() {
        let s = Settings::default();
        assert_eq!(s.low_battery_threshold, 20);
        assert!(!s.listen_only);
        assert!(s.ask_allowed_vids.is_empty());
    }
    #[test]
    fn roundtrip_and_clamp() {
        let d = tempfile::tempdir().unwrap();
        let s = Settings {
            low_battery_threshold: 500,
            ask_allowed_vids: vec!["046D".into()],
            ..Settings::default()
        };
        s.save_to(d.path());
        let back = Settings::load_from(d.path());
        assert_eq!(back.low_battery_threshold, 99);
        assert_eq!(back.ask_allowed_vids, vec!["046D".to_string()]);
    }
    #[test]
    fn corrupt_file_gives_defaults() {
        let d = tempfile::tempdir().unwrap();
        std::fs::write(d.path().join("settings.json"), "{not json").unwrap();
        assert_eq!(Settings::load_from(d.path()).low_battery_threshold, 20);
    }
    #[test]
    fn reads_csharp_keys() {
        let d = tempfile::tempdir().unwrap();
        std::fs::write(
            d.path().join("settings.json"),
            r#"{"lowBatteryThreshold":35,"askAllowedVids":["046D"]}"#,
        )
        .unwrap();
        let s = Settings::load_from(d.path());
        assert_eq!(s.low_battery_threshold, 35);
        assert_eq!(s.ask_allowed_vids, vec!["046D".to_string()]);
    }
}
