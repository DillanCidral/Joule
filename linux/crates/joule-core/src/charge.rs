//! The level of a Logitech battery while it charges.
//!
//! Many Logitech devices only report the battery voltage. While charging, the charger pushes that
//! voltage up at once (the G933 reads 4.3 to 4.5 V a minute after it is plugged in), so a discharge
//! curve says "100%" for a nearly empty battery. HeadsetControl and Solaar just apply the voltage
//! curve anyway; g933-utils measured a separate charging curve for the G933. Logitech's own software
//! estimates. Joule does the same:
//!
//! 1. Start from the last level read while *not* charging (the cache keeps it), and add the charge
//!    that the elapsed time gives at the typical full-charge time of the device. Never above 99%
//!    until the device itself says "full".
//! 2. With nothing known from before the charge, use the measured charging curve when the model has
//!    one (G933/G935), and continue from there.
//! 3. Otherwise show "charging" without a number: a guess with no basis would be an invented number.

use crate::cache::CachedReading;

/// A value read before the charge is used only when it is at most this old.
pub const MAX_START_AGE_SECS: i64 = 14 * 24 * 3600;

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct Estimate {
    pub percent: Option<u8>,
    /// Where the estimate starts from: (percent, unix time). Stored in the cache.
    pub start: Option<(u8, i64)>,
}

/// Percent after `elapsed_secs` of charging from `start_percent`, at `full_minutes` for 0 to 100%.
pub fn charged(start_percent: u8, elapsed_secs: i64, full_minutes: f64) -> u8 {
    let gained = (elapsed_secs.max(0) as f64 / 60.0) * 100.0 / full_minutes.max(1.0);
    let v = (f64::from(start_percent) + gained).round();
    v.clamp(f64::from(start_percent), 99.0) as u8
}

/// The estimate for a voltage-based reading of a charging device.
pub fn estimate(
    previous: Option<&CachedReading>,
    curve_percent: Option<u8>,
    now_unix: i64,
    full_minutes: f64,
) -> Estimate {
    let start = previous
        .and_then(
            |p| match (p.charging, p.charge_start_percent, p.charge_start_unix) {
                // a charge already under way
                (true, Some(pct), Some(at)) => Some((pct, at)),
                _ => None,
            },
        )
        .or_else(|| {
            // the last level read before the charge
            previous.and_then(|p| match (p.discharge_percent, p.discharge_unix) {
                (Some(pct), Some(at)) if now_unix - at <= MAX_START_AGE_SECS => {
                    Some((pct, now_unix))
                }
                _ => None,
            })
        })
        .or_else(|| curve_percent.map(|c| (c.min(99), now_unix)));
    match start {
        Some((pct, at)) => Estimate {
            percent: Some(charged(pct, now_unix - at, full_minutes)),
            start: Some((pct, at)),
        },
        None => Estimate {
            percent: None,
            start: None,
        },
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const NOW: i64 = 1_000_000;

    fn before_charge(pct: u8, at: i64) -> CachedReading {
        CachedReading {
            percent: Some(pct),
            discharge_percent: Some(pct),
            discharge_unix: Some(at),
            ..Default::default()
        }
    }

    #[test]
    fn time_adds_charge_at_the_typical_rate() {
        assert_eq!(charged(20, 0, 120.0), 20);
        assert_eq!(
            charged(20, 60 * 60, 120.0),
            70,
            "an hour of a two-hour charge is 50 points"
        );
        assert_eq!(
            charged(20, 3 * 3600, 120.0),
            99,
            "never 100 before the device says full"
        );
        assert_eq!(charged(50, -10, 120.0), 50);
    }
    #[test]
    fn a_new_charge_starts_from_the_last_discharge_value() {
        let prev = before_charge(12, NOW - 600);
        let e = estimate(Some(&prev), Some(92), NOW, 120.0);
        assert_eq!(
            e.percent,
            Some(12),
            "the voltage curve (92%) is ignored when the start is known"
        );
        assert_eq!(e.start, Some((12, NOW)));
    }
    #[test]
    fn a_running_charge_continues() {
        let prev = CachedReading {
            charging: true,
            charge_start_percent: Some(12),
            charge_start_unix: Some(NOW - 1800),
            discharge_percent: Some(12),
            discharge_unix: Some(NOW - 2400),
            ..Default::default()
        };
        assert_eq!(estimate(Some(&prev), None, NOW, 120.0).percent, Some(37));
    }
    #[test]
    fn a_very_old_value_is_not_a_start() {
        let prev = before_charge(80, NOW - MAX_START_AGE_SECS - 1);
        assert_eq!(estimate(Some(&prev), None, NOW, 120.0).percent, None);
    }
    #[test]
    fn without_history_the_charging_curve_is_the_start() {
        let e = estimate(None, Some(45), NOW, 120.0);
        assert_eq!(e.percent, Some(45));
        assert_eq!(estimate(None, Some(100), NOW, 120.0).percent, Some(99));
    }
    #[test]
    fn without_history_or_curve_there_is_no_number() {
        assert_eq!(
            estimate(None, None, NOW, 120.0),
            Estimate {
                percent: None,
                start: None
            }
        );
    }
}
