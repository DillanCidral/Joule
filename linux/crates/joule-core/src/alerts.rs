//! Low-battery alert edge detection (ported from `TrayController.EvaluateLowBattery`).
//! Evaluated only at the end of a reading. Only values read now count, never cache values.

use crate::{
    known,
    model::{DeviceEntry, DeviceKind, ReadingStatus},
};
use std::collections::HashSet;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct LowBatteryAlert {
    pub key: String,
    pub name: String,
    pub kind: DeviceKind,
    /// "14%" or a level word.
    pub value: String,
}

#[derive(Default, Debug)]
pub struct LowBatteryTracker {
    alerted: HashSet<String>,
}

impl LowBatteryTracker {
    /// A new threshold: judge everything again on the next reading.
    pub fn clear(&mut self) {
        self.alerted.clear();
    }

    /// Returns one alert per device that crossed below the threshold since it last was healthy.
    /// A device re-arms once it climbs more than 5 points above the threshold.
    pub fn evaluate(&mut self, devices: &[DeviceEntry], threshold: i32) -> Vec<LowBatteryAlert> {
        let mut out = Vec::new();
        for d in devices {
            // A device that is not connected never alerts: the OS may remember the last percent of a
            // switched-off headset, and it would warn on every reading.
            if !d.is_connected || d.reading.status != ReadingStatus::Ok {
                continue;
            }
            let (low, value) = if let Some(p) = d.reading.percent {
                if i32::from(p) > threshold + 5 {
                    self.alerted.remove(&d.key);
                }
                (i32::from(p) < threshold, format!("{p}%"))
            } else if matches!(d.reading.level.as_deref(), Some("LOW") | Some("EMPTY")) {
                (
                    true,
                    known::level_label(d.reading.level.as_deref().unwrap()),
                )
            } else {
                self.alerted.remove(&d.key);
                continue;
            };
            if low && self.alerted.insert(d.key.clone()) {
                out.push(LowBatteryAlert {
                    key: d.key.clone(),
                    name: d.display_name.clone(),
                    kind: d.kind,
                    value,
                });
            }
        }
        out
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::model::BatteryReading;

    fn dev(key: &str, reading: BatteryReading, connected: bool) -> DeviceEntry {
        DeviceEntry {
            key: key.into(),
            display_name: key.into(),
            hardware_id: String::new(),
            provider_name: String::new(),
            reading,
            kind: DeviceKind::Headset,
            origin: String::new(),
            is_live: false,
            is_connected: connected,
            cache: None,
        }
    }
    fn pct(key: &str, p: i32) -> DeviceEntry {
        dev(key, BatteryReading::percentage(p, None, None), true)
    }

    #[test]
    fn alerts_once_then_stays_quiet() {
        let mut t = LowBatteryTracker::default();
        assert_eq!(t.evaluate(&[pct("g933", 14)], 20).len(), 1);
        assert!(t.evaluate(&[pct("g933", 13)], 20).is_empty());
    }
    #[test]
    fn exactly_at_threshold_is_not_low() {
        let mut t = LowBatteryTracker::default();
        assert!(t.evaluate(&[pct("a", 20)], 20).is_empty());
    }
    #[test]
    fn rearms_only_above_threshold_plus_five() {
        let mut t = LowBatteryTracker::default();
        t.evaluate(&[pct("a", 10)], 20);
        t.evaluate(&[pct("a", 25)], 20); // 25 is not above 20 + 5
        assert!(t.evaluate(&[pct("a", 10)], 20).is_empty());
        t.evaluate(&[pct("a", 26)], 20);
        assert_eq!(t.evaluate(&[pct("a", 10)], 20).len(), 1);
    }
    #[test]
    fn disconnected_and_cached_never_alert() {
        let mut t = LowBatteryTracker::default();
        let gone = dev("a", BatteryReading::percentage(5, None, None), false);
        let silent = dev("b", BatteryReading::no_data("off"), true);
        assert!(t.evaluate(&[gone, silent], 20).is_empty());
    }
    #[test]
    fn level_words_alert() {
        let mut t = LowBatteryTracker::default();
        let mut r = BatteryReading::percentage(0, None, None);
        r.percent = None;
        r.level = Some("LOW".into());
        assert_eq!(t.evaluate(&[dev("pad", r, true)], 20).len(), 1);
    }
    #[test]
    fn clear_judges_again() {
        let mut t = LowBatteryTracker::default();
        t.evaluate(&[pct("a", 10)], 20);
        t.clear();
        assert_eq!(t.evaluate(&[pct("a", 10)], 20).len(), 1);
    }
}
