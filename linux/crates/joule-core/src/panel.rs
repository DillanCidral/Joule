//! Display rules of the panel in one place (ported from `Core/PanelView.cs`), free of any toolkit:
//! a band is not a percent, an old value looks old, no data goes to "other" with the reason,
//! a device that is not plugged in does not show.

use crate::{
    known,
    l::t,
    model::{BatteryReading, DeviceEntry, DeviceKind, ReadingStatus},
};

/// Above this an on-demand reading becomes "old value".
pub const STALE_AFTER_SECS: i64 = 15 * 60;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ValueKind {
    /// A number and a continuous bar.
    Percent,
    /// A word and three blocks. Never becomes a percent.
    Band,
    /// "wired", no number.
    Cable,
}

#[derive(Clone, Debug, PartialEq)]
pub struct DeviceRowView {
    pub key: String,
    pub name: String,
    pub kind: DeviceKind,
    pub origin: String,
    pub value_kind: ValueKind,
    pub percent: i32,
    pub band_count: i32,
    pub band_label: String,
    pub is_live: bool,
    pub is_stale: bool,
    pub is_low: bool,
    pub age: Option<String>,
    pub charge_text: Option<String>,
    pub note: Option<String>,
    pub tooltip: String,
}

/// A "other" row: plugged in, no value, with the reason.
#[derive(Clone, Debug, PartialEq)]
pub struct OtherRowView {
    pub key: String,
    pub name: String,
    pub kind: DeviceKind,
    pub reason: String,
}

/// Plugged in, no value, brand waiting for consent: stays visible with an "Allow" button.
#[derive(Clone, Debug, PartialEq)]
pub struct ConsentRowView {
    pub key: String,
    pub name: String,
    pub kind: DeviceKind,
    /// VID hex, for example "046D".
    pub vid: String,
}

/// `read_at_unix` is when the reading was made; it stands in for readings without their own time.
pub fn main_rows(
    devices: &[DeviceEntry],
    threshold: i32,
    now_unix: i64,
    read_at_unix: i64,
) -> Vec<DeviceRowView> {
    let mut rows: Vec<DeviceRowView> = devices
        .iter()
        .filter(|d| d.shows_battery())
        .map(|d| row(d, threshold, now_unix, read_at_unix))
        .collect();
    rows.sort_by(|a, b| {
        rank(a)
            .cmp(&rank(b))
            .then_with(|| a.name.to_lowercase().cmp(&b.name.to_lowercase()))
    });
    rows
}

// shortcut: consent is per VID, so a device of the same brand over another transport that has no value also asks "Allow".
pub fn need_consent(
    devices: &[DeviceEntry],
    waiting_vid: &dyn Fn(&str) -> Option<String>,
) -> Vec<ConsentRowView> {
    let mut v: Vec<ConsentRowView> = devices
        .iter()
        .filter(|d| d.shows_as_other())
        .filter_map(|d| {
            waiting_vid(&d.hardware_id).map(|vid| ConsentRowView {
                key: d.key.clone(),
                name: d.display_name.clone(),
                kind: d.kind,
                vid,
            })
        })
        .collect();
    v.sort_by_key(|c| c.name.to_lowercase());
    v
}

/// No value, with the reason. What waits for consent goes to [`need_consent`], not here.
pub fn others(
    devices: &[DeviceEntry],
    waiting_vid: &dyn Fn(&str) -> Option<String>,
) -> Vec<OtherRowView> {
    let mut v: Vec<OtherRowView> = devices
        .iter()
        .filter(|d| d.shows_as_other() && waiting_vid(&d.hardware_id).is_none())
        .map(|d| OtherRowView {
            key: d.key.clone(),
            name: d.display_name.clone(),
            kind: d.kind,
            reason: reason(d),
        })
        .collect();
    v.sort_by_key(|o| o.name.to_lowercase());
    v
}

fn row(d: &DeviceEntry, threshold: i32, now_unix: i64, read_at_unix: i64) -> DeviceRowView {
    let r = &d.reading;
    let from_cache = matches!(
        r.status,
        ReadingStatus::NoData | ReadingStatus::Disconnected
    ) && d
        .cache
        .as_ref()
        .is_some_and(|c| c.percent.is_some() || c.level.is_some());
    let observed = if from_cache {
        d.cache
            .as_ref()
            .map(|c| c.timestamp_unix)
            .unwrap_or(read_at_unix)
    } else {
        r.observed_unix.unwrap_or(read_at_unix)
    };
    let age = (now_unix - observed).max(0);

    let (value_kind, percent, band_count, band_label, low) = if let Some(p) = d.effective_percent()
    {
        (
            ValueKind::Percent,
            i32::from(p),
            0,
            String::new(),
            i32::from(p) < threshold,
        )
    } else if let Some(level) = d.effective_level() {
        let bands = match level {
            "FULL" => 3,
            "MEDIUM" => 2,
            "LOW" => 1,
            _ => 0,
        };
        (
            ValueKind::Band,
            0,
            bands,
            known::level_label(level),
            bands <= 1,
        )
    } else {
        (ValueKind::Cable, 0, 0, String::new(), false)
    };

    let stale = !d.is_live && (from_cache || age > STALE_AFTER_SECS);
    let note = if low {
        Some(if value_kind == ValueKind::Percent {
            t(
                &format!("abaixo de {threshold}%"),
                &format!("below {threshold}%"),
            )
        } else {
            t("bateria baixa", "low battery")
        })
    } else if stale {
        r.detail.clone()
    } else {
        None
    };

    DeviceRowView {
        key: d.key.clone(),
        name: d.display_name.clone(),
        kind: d.kind,
        origin: d.origin.clone(),
        value_kind,
        percent,
        band_count,
        band_label,
        is_live: d.is_live,
        is_stale: stale,
        is_low: low,
        age: if d.is_live { None } else { Some(age_text(age)) },
        charge_text: if from_cache {
            None
        } else {
            r.charge_text.clone()
        },
        note,
        tooltip: tooltip(d),
    }
}

/// Percents from weakest to strongest, then bands, then cable.
fn rank(r: &DeviceRowView) -> i32 {
    match r.value_kind {
        ValueKind::Percent => r.percent,
        ValueKind::Band => 1000 + r.band_count,
        ValueKind::Cable => 2000,
    }
}

pub fn age_text(secs: i64) -> String {
    let minutes = secs / 60;
    let hours = secs / 3600;
    let days = secs / 86400;
    if secs < 45 {
        t("agora", "now")
    } else if minutes < 60 {
        let m = minutes.max(1);
        t(&format!("há {m} min"), &format!("{m} min ago"))
    } else if hours < 24 {
        t(&format!("há {hours} h"), &format!("{hours} h ago"))
    } else {
        t(&format!("há {days} d"), &format!("{days} d ago"))
    }
}

pub fn reason(d: &DeviceEntry) -> String {
    let r: &BatteryReading = &d.reading;
    let detail = r.detail.as_deref().filter(|s| !s.trim().is_empty());
    match (r.status, detail) {
        (ReadingStatus::Unsupported, None) => t(
            "O app ainda não sabe ler a bateria deste aparelho.",
            "The app can’t read this device’s battery yet.",
        ),
        (ReadingStatus::Unsupported, Some(d)) => sentence(d),
        (ReadingStatus::Error, d) => {
            let d = d.unwrap_or_default();
            sentence(&t(
                &format!("erro ao ler: {d}"),
                &format!("error reading: {d}"),
            ))
        }
        (_, None) => t("Sem dado agora.", "No data right now."),
        (_, Some(d)) => sentence(&t(
            &format!("sem dado agora: {d}"),
            &format!("no data right now: {d}"),
        )),
    }
}

fn tooltip(d: &DeviceEntry) -> String {
    let mut lines = vec![d.display_name.clone()];
    if let Some(detail) = d.reading.detail.as_deref().filter(|s| !s.trim().is_empty()) {
        lines.push(sentence(detail));
    }
    if !d.hardware_id.trim().is_empty() {
        lines.push(d.hardware_id.clone());
    }
    lines.join("\n")
}

/// First letter upper case, one full stop at the end.
pub fn sentence(text: &str) -> String {
    let s = text.trim();
    let mut chars = s.chars();
    let Some(first) = chars.next() else {
        return String::new();
    };
    let s: String = first.to_uppercase().chain(chars).collect();
    if s.ends_with('.') { s } else { format!("{s}.") }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::cache::CachedReading;

    const NOW: i64 = 1_000_000;

    fn dev(name: &str, reading: BatteryReading) -> DeviceEntry {
        DeviceEntry {
            key: name.into(),
            display_name: name.into(),
            hardware_id: "046D:0A5B".into(),
            provider_name: "x".into(),
            reading,
            kind: DeviceKind::Headset,
            origin: "Dongle".into(),
            is_live: false,
            is_connected: true,
            cache: None,
        }
    }
    fn cache(p: u8, at: i64) -> CachedReading {
        CachedReading {
            display_name: "x".into(),
            hardware_id: "046D:0A5B".into(),
            percent: Some(p),
            level: None,
            charging: false,
            timestamp_unix: at,
        }
    }
    fn no_wait(_: &str) -> Option<String> {
        None
    }

    #[test]
    fn weakest_percent_first_then_bands_then_cable() {
        let mut band = BatteryReading::coarse("MEDIUM");
        band.percent = None;
        let devices = vec![
            dev("wired", BatteryReading::charging(None)),
            dev("band", band),
            dev("high", BatteryReading::percentage(80, None, None)),
            dev("low", BatteryReading::percentage(9, None, None)),
        ];
        let names: Vec<_> = main_rows(&devices, 20, NOW, NOW)
            .into_iter()
            .map(|r| r.name)
            .collect();
        assert_eq!(names, ["low", "high", "band", "wired"]);
    }
    #[test]
    fn low_flag_and_note_follow_the_threshold() {
        let rows = main_rows(
            &[dev("a", BatteryReading::percentage(19, None, None))],
            20,
            NOW,
            NOW,
        );
        assert!(rows[0].is_low);
        assert!(rows[0].note.is_some());
        let rows = main_rows(
            &[dev("a", BatteryReading::percentage(20, None, None))],
            20,
            NOW,
            NOW,
        );
        assert!(!rows[0].is_low && rows[0].note.is_none());
    }
    #[test]
    fn a_band_is_never_a_percent() {
        let mut band = BatteryReading::coarse("LOW");
        band.percent = None;
        let r = &main_rows(&[dev("pad", band)], 20, NOW, NOW)[0];
        assert_eq!(r.value_kind, ValueKind::Band);
        assert_eq!(r.band_count, 1);
        assert!(r.is_low);
    }
    #[test]
    fn a_cached_value_is_marked_old_and_drops_the_charge_text() {
        let mut d = dev("g933", BatteryReading::no_data("headset off"));
        d.cache = Some(cache(58, NOW - 3600));
        let r = &main_rows(&[d], 20, NOW, NOW)[0];
        assert_eq!(r.percent, 58);
        assert!(r.is_stale);
        assert_eq!(r.note.as_deref(), Some("headset off"));
        assert!(r.age.as_deref().unwrap().contains('1'), "{:?}", r.age);
        assert_eq!(r.charge_text, None);
    }
    #[test]
    fn a_fresh_reading_is_not_stale_and_keeps_its_charge_text() {
        let d = dev(
            "g933",
            BatteryReading::percentage(70, None, Some("charging".into())),
        );
        let r = &main_rows(&[d], 20, NOW, NOW)[0];
        assert!(!r.is_stale);
        assert_eq!(r.charge_text.as_deref(), Some("charging"));
    }
    #[test]
    fn a_reading_older_than_15_minutes_is_stale() {
        let d = dev("a", BatteryReading::percentage(70, None, None));
        assert!(main_rows(std::slice::from_ref(&d), 20, NOW, NOW - 16 * 60)[0].is_stale);
        assert!(!main_rows(&[d], 20, NOW, NOW - 14 * 60)[0].is_stale);
    }
    #[test]
    fn live_rows_show_no_age() {
        let mut d = dev("mouse", BatteryReading::percentage(70, None, None));
        d.is_live = true;
        let r = &main_rows(&[d], 20, NOW, NOW - 3600)[0];
        assert!(r.age.is_none() && !r.is_stale);
    }
    #[test]
    fn disconnected_devices_do_not_show() {
        let mut d = dev("a", BatteryReading::percentage(70, None, None));
        d.is_connected = false;
        assert!(main_rows(&[d.clone()], 20, NOW, NOW).is_empty());
        assert!(others(&[d], &no_wait).is_empty());
    }
    #[test]
    fn age_text_steps() {
        let _g = crate::l::TEST_LOCK
            .lock()
            .unwrap_or_else(|e| e.into_inner());
        crate::l::set_language("en");
        assert_eq!(age_text(10), "now");
        assert_eq!(age_text(44), "now");
        assert_eq!(age_text(45), "1 min ago");
        assert_eq!(age_text(59 * 60), "59 min ago");
        assert_eq!(age_text(3600), "1 h ago");
        assert_eq!(age_text(86400 * 2), "2 d ago");
        crate::l::set_language("auto");
    }
    #[test]
    fn consent_rows_split_from_other_rows() {
        let silent = dev("G933", BatteryReading::no_data("x"));
        let mut other = dev("keyboard", BatteryReading::unsupported());
        other.hardware_id = "1234:0001".into();
        let devices = vec![silent, other];
        let waiting = |hw: &str| {
            if hw.starts_with("046D") {
                Some("046D".to_string())
            } else {
                None
            }
        };
        let c = need_consent(&devices, &waiting);
        assert_eq!(c.len(), 1);
        assert_eq!(c[0].vid, "046D");
        let o = others(&devices, &waiting);
        assert_eq!(o.len(), 1);
        assert_eq!(o[0].name, "keyboard");
    }
    #[test]
    fn reasons_read_as_sentences() {
        let _g = crate::l::TEST_LOCK
            .lock()
            .unwrap_or_else(|e| e.into_inner());
        crate::l::set_language("en");
        let u = dev("kb", BatteryReading::unsupported());
        assert_eq!(reason(&u), "The app can’t read this device’s battery yet.");
        let mut dead = dev("kb", BatteryReading::unsupported_because("wired keyboard"));
        assert_eq!(reason(&dead), "Wired keyboard.");
        dead.reading = BatteryReading::error("boom");
        assert_eq!(reason(&dead), "Error reading: boom.");
        let n = dev("kb", BatteryReading::no_data("headset off"));
        assert_eq!(reason(&n), "No data right now: headset off.");
        crate::l::set_language("auto");
    }
    #[test]
    fn sentence_does_not_double_the_full_stop() {
        assert_eq!(sentence("  ola "), "Ola.");
        assert_eq!(sentence("ola."), "Ola.");
        assert_eq!(sentence(""), "");
    }
}
