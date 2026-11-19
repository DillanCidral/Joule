use crate::{cache::CachedReading, known};
use serde::Serialize;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum ReadingStatus {
    Ok,
    Charging,
    NoData,
    Disconnected,
    Unsupported,
    Error,
}

#[derive(Clone, Debug, PartialEq, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct BatteryReading {
    pub status: ReadingStatus,
    pub percent: Option<u8>,
    pub level: Option<String>,
    pub detail: Option<String>,
    pub observed_unix: Option<i64>,
    pub charge_text: Option<String>,
}

impl BatteryReading {
    fn base(status: ReadingStatus, detail: Option<String>) -> Self {
        Self {
            status,
            percent: None,
            level: None,
            detail,
            observed_unix: None,
            charge_text: None,
        }
    }
    pub fn percentage(percent: i32, detail: Option<String>, charge_text: Option<String>) -> Self {
        Self {
            percent: Some(percent.clamp(0, 100) as u8),
            charge_text,
            ..Self::base(ReadingStatus::Ok, detail)
        }
    }
    /// Only a coarse level word ("EMPTY", "LOW", "MEDIUM", "FULL"), never turned into a percent.
    pub fn coarse(level: &str) -> Self {
        Self {
            level: Some(level.to_string()),
            ..Self::base(ReadingStatus::Ok, None)
        }
    }
    pub fn charging(detail: Option<String>) -> Self {
        Self::base(ReadingStatus::Charging, detail)
    }
    pub fn no_data(detail: impl Into<String>) -> Self {
        Self::base(ReadingStatus::NoData, Some(detail.into()))
    }
    pub fn disconnected(detail: impl Into<String>) -> Self {
        Self::base(ReadingStatus::Disconnected, Some(detail.into()))
    }
    pub fn error(detail: impl Into<String>) -> Self {
        Self::base(ReadingStatus::Error, Some(detail.into()))
    }
    pub fn unsupported() -> Self {
        Self::base(ReadingStatus::Unsupported, None)
    }
    pub fn unsupported_because(reason: impl Into<String>) -> Self {
        Self::base(ReadingStatus::Unsupported, Some(reason.into()))
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize)]
#[serde(rename_all = "camelCase")]
pub enum DeviceKind {
    Generic,
    Mouse,
    Keyboard,
    Headset,
    Gamepad,
}

#[derive(Clone, Debug, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct DeviceEntry {
    pub key: String,
    pub display_name: String,
    pub hardware_id: String,
    pub provider_name: String,
    pub reading: BatteryReading,
    pub kind: DeviceKind,
    /// "Dongle", "Bluetooth", "Cable".
    pub origin: String,
    pub is_live: bool,
    pub is_connected: bool,
    /// Last known value on disk, used when this reading came back empty.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub cache: Option<CachedReading>,
}

impl DeviceEntry {
    fn uses_cache(&self) -> bool {
        matches!(
            self.reading.status,
            ReadingStatus::NoData | ReadingStatus::Disconnected
        )
    }

    /// Percent to show now: the reading, or the cached one when the reading came back empty.
    pub fn effective_percent(&self) -> Option<u8> {
        self.reading.percent.or(if self.uses_cache() {
            self.cache.as_ref().and_then(|c| c.percent)
        } else {
            None
        })
    }

    pub fn effective_level(&self) -> Option<&str> {
        self.reading
            .level
            .as_deref()
            .or(if self.uses_cache() {
                self.cache.as_ref().and_then(|c| c.level.as_deref())
            } else {
                None
            })
            .filter(|l| !l.is_empty())
    }

    pub fn has_battery_value(&self) -> bool {
        self.reading.status == ReadingStatus::Charging
            || self.effective_percent().is_some()
            || self.effective_level().is_some()
    }

    /// Main list: plugged in now and something to show.
    pub fn shows_battery(&self) -> bool {
        self.is_connected && self.has_battery_value()
    }

    /// "Other devices": plugged in now but nothing to show.
    pub fn shows_as_other(&self) -> bool {
        self.is_connected && !self.has_battery_value()
    }

    /// Number only for ordering and icon colour.
    pub fn sort_percent(&self) -> Option<i32> {
        self.effective_percent().map(i32::from).or_else(|| {
            self.effective_level()
                .map(|l| known::approx_percent_for_level(Some(l)))
        })
    }
}

fn rank(e: &DeviceEntry) -> u8 {
    match e.reading.status {
        ReadingStatus::Ok => 0,
        ReadingStatus::Charging => 1,
        ReadingStatus::NoData => 2,
        ReadingStatus::Error => 3,
        ReadingStatus::Disconnected => 4,
        ReadingStatus::Unsupported => 5,
    }
}

/// Supported first; among them the weakest on top, then by name.
pub fn compare_for_display(a: &DeviceEntry, b: &DeviceEntry) -> std::cmp::Ordering {
    rank(a)
        .cmp(&rank(b))
        .then_with(|| {
            a.sort_percent()
                .unwrap_or(1000)
                .cmp(&b.sort_percent().unwrap_or(1000))
        })
        .then_with(|| {
            a.display_name
                .to_lowercase()
                .cmp(&b.display_name.to_lowercase())
        })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn percentage_clamps() {
        assert_eq!(
            BatteryReading::percentage(140, None, None).percent,
            Some(100)
        );
        assert_eq!(BatteryReading::percentage(-5, None, None).percent, Some(0));
    }
    #[test]
    fn no_data_has_no_percent() {
        let r = BatteryReading::no_data("x");
        assert_eq!(r.status, ReadingStatus::NoData);
        assert_eq!(r.percent, None);
    }
    fn entry(name: &str, reading: BatteryReading) -> DeviceEntry {
        DeviceEntry {
            key: name.into(),
            display_name: name.into(),
            hardware_id: "0000:0000".into(),
            provider_name: String::new(),
            reading,
            kind: DeviceKind::Generic,
            origin: String::new(),
            is_live: false,
            is_connected: true,
            cache: None,
        }
    }
    fn cached(percent: u8) -> CachedReading {
        CachedReading {
            display_name: "x".into(),
            hardware_id: "0000:0000".into(),
            percent: Some(percent),
            level: None,
            charging: false,
            timestamp_unix: 1,
        }
    }

    #[test]
    fn display_order_is_status_then_weakest_then_name() {
        let mut v = [
            entry("zeta", BatteryReading::unsupported()),
            entry("b", BatteryReading::percentage(80, None, None)),
            entry("a", BatteryReading::percentage(80, None, None)),
            entry("weak", BatteryReading::percentage(10, None, None)),
            entry("wired", BatteryReading::charging(None)),
            entry("silent", BatteryReading::no_data("x")),
        ];
        v.sort_by(compare_for_display);
        let names: Vec<_> = v.iter().map(|e| e.display_name.as_str()).collect();
        assert_eq!(names, ["weak", "a", "b", "wired", "silent", "zeta"]);
    }
    #[test]
    fn empty_reading_falls_back_to_the_cache() {
        let mut e = entry("g933", BatteryReading::no_data("off"));
        assert!(!e.has_battery_value());
        assert!(e.shows_as_other());
        e.cache = Some(cached(55));
        assert_eq!(e.effective_percent(), Some(55));
        assert!(e.shows_battery());
        // A real reading never borrows the cache.
        e.reading = BatteryReading::error("boom");
        assert_eq!(e.effective_percent(), None);
    }
    #[test]
    fn disconnected_row_never_shows() {
        let mut e = entry("g933", BatteryReading::percentage(50, None, None));
        e.is_connected = false;
        assert!(!e.shows_battery() && !e.shows_as_other());
    }
    #[test]
    fn serializes_camel_case() {
        let json = serde_json::to_string(&BatteryReading::percentage(
            70,
            None,
            Some("charging".into()),
        ))
        .unwrap();
        assert!(json.contains("\"chargeText\":\"charging\""), "{json}");
        assert!(json.contains("\"status\":\"ok\""), "{json}");
    }
}
