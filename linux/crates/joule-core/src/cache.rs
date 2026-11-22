use crate::{
    log,
    model::{BatteryReading, ChargeState, ReadingStatus},
    paths,
};
use serde::{Deserialize, Serialize};
use std::{collections::BTreeMap, path::Path};

#[derive(Clone, Debug, Default, PartialEq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CachedReading {
    pub display_name: String,
    pub hardware_id: String,
    pub percent: Option<u8>,
    pub level: Option<String>,
    pub charging: bool,
    pub timestamp_unix: i64,
    /// Last percent read while not charging, and when: where a charge estimate starts.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub discharge_percent: Option<u8>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub discharge_unix: Option<i64>,
    /// The running charge estimate: the percent and the time it started from.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub charge_start_percent: Option<u8>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub charge_start_unix: Option<i64>,
}

impl CachedReading {
    pub fn age_secs(&self, now_unix: i64) -> i64 {
        now_unix - self.timestamp_unix
    }
}

/// Last value seen per device, kept on disk so the first screen is useful before a reading ends.
#[derive(Default, Debug)]
pub struct ReadingCache {
    items: BTreeMap<String, CachedReading>,
}

impl ReadingCache {
    pub fn get(&self, key: &str) -> Option<CachedReading> {
        self.items.get(key).cloned()
    }

    pub fn update(
        &mut self,
        key: &str,
        display_name: &str,
        hardware_id: &str,
        reading: &BatteryReading,
        now_unix: i64,
    ) {
        if !matches!(reading.status, ReadingStatus::Ok | ReadingStatus::Charging) {
            return;
        }
        let previous = self.items.get(key).cloned().unwrap_or_default();
        let charging = reading.status == ReadingStatus::Charging
            || reading.charge_state == Some(ChargeState::Charging)
            || (reading.charge_text.is_some() && reading.charge_state != Some(ChargeState::Full));
        let at = reading.observed_unix.unwrap_or(now_unix);
        let mut entry = CachedReading {
            display_name: display_name.to_string(),
            hardware_id: hardware_id.to_string(),
            percent: reading.percent,
            level: reading.level.clone(),
            charging,
            timestamp_unix: at,
            ..previous.clone()
        };
        if charging {
            // keep where the charge started; the reading service sets it (set_charge_start)
        } else {
            entry.discharge_percent = reading.percent.or(previous.discharge_percent);
            entry.discharge_unix = if reading.percent.is_some() {
                Some(at)
            } else {
                previous.discharge_unix
            };
            entry.charge_start_percent = None;
            entry.charge_start_unix = None;
        }
        self.items.insert(key.to_string(), entry);
    }

    /// Remembers where the running charge estimate started (`None` clears it).
    pub fn set_charge_start(&mut self, key: &str, start: Option<(u8, i64)>) {
        if let Some(e) = self.items.get_mut(key) {
            e.charge_start_percent = start.map(|s| s.0);
            e.charge_start_unix = start.map(|s| s.1);
        }
    }

    pub fn load_from(dir: &Path) -> ReadingCache {
        let Ok(text) = std::fs::read_to_string(paths::state_file(dir)) else {
            return Self::default();
        };
        match serde_json::from_str(&text) {
            Ok(items) => Self { items },
            Err(e) => {
                log::error("ReadingCache.load", &e);
                Self::default()
            }
        }
    }

    pub fn save_to(&self, dir: &Path) {
        match serde_json::to_string_pretty(&self.items) {
            Ok(json) => {
                if let Err(e) = paths::write_atomic(&paths::state_file(dir), &json) {
                    log::error("ReadingCache.save", &e);
                }
            }
            Err(e) => log::error("ReadingCache.save", &e),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn ignores_readings_without_a_value() {
        let mut c = ReadingCache::default();
        c.update("k", "Dev", "046D:0A5B", &BatteryReading::no_data("x"), 100);
        assert!(c.get("k").is_none());
    }
    #[test]
    fn stores_percent_and_charging() {
        let mut c = ReadingCache::default();
        c.update(
            "k",
            "Dev",
            "046D:0A5B",
            &BatteryReading::percentage(70, None, Some("charging".into())),
            100,
        );
        let v = c.get("k").unwrap();
        assert_eq!(v.percent, Some(70));
        assert!(v.charging);
        assert_eq!(v.age_secs(160), 60);
    }
    #[test]
    fn roundtrip() {
        let d = tempfile::tempdir().unwrap();
        let mut c = ReadingCache::default();
        c.update(
            "k",
            "Dev",
            "046D:0A5B",
            &BatteryReading::percentage(42, None, None),
            7,
        );
        c.save_to(d.path());
        assert_eq!(
            ReadingCache::load_from(d.path()).get("k").unwrap().percent,
            Some(42)
        );
    }
}
