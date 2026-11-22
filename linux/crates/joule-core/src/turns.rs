//! Who reads and when (ported from `Core/ReadingTurns.cs`, the "read on demand" rule of SECURITY.md).
//! One full reading at a time; a live or arrival reading never runs during a full one or next to
//! another. Hotplug notices collect what came and went, wait [`SETTLE`], and read at most once per [`GAP`].

use std::{
    collections::BTreeSet,
    time::{Duration, Instant},
};

/// A device shows up as several collections in a row: wait until it settles.
pub const SETTLE: Duration = Duration::from_millis(1500);
/// At most one arrival reading per GAP.
pub const GAP: Duration = Duration::from_secs(10);

#[derive(Debug, Default, PartialEq, Eq)]
pub struct Taken {
    pub removed: Vec<String>,
    pub arrived: Vec<String>,
    /// How long until what was postponed should be tried again.
    pub retry: Option<Duration>,
}

#[derive(Default, Debug)]
pub struct ReadingTurns {
    arrived: BTreeSet<String>,
    removed: BTreeSet<String>,
    last_arrival_read: Option<Instant>,
    full: bool,
    live: bool,
}

impl ReadingTurns {
    /// Refresh. False = a full reading is already running.
    pub fn try_start_full(&mut self) -> bool {
        !std::mem::replace(&mut self.full, true)
    }
    pub fn end_full(&mut self) {
        self.full = false;
    }

    /// Live or arrival. False = a full or another live reading is running.
    pub fn try_start_live(&mut self) -> bool {
        if self.full || self.live {
            return false;
        }
        self.live = true;
        true
    }
    pub fn end_live(&mut self) {
        self.live = false;
    }

    /// The kernel said a hidraw node came or went.
    pub fn note(&mut self, path: &str, arrived: bool) {
        if arrived {
            self.arrived.insert(path.to_string());
            self.removed.remove(path);
        } else {
            self.removed.insert(path.to_string());
            self.arrived.remove(path);
        }
    }

    /// No full reading yet: the first one sees everything itself.
    pub fn forget(&mut self) {
        self.arrived.clear();
        self.removed.clear();
    }

    /// The settle wait is over. When something is returned the live turn is taken: call
    /// [`end_live`](Self::end_live) afterwards.
    pub fn take(&mut self, now: Instant) -> Taken {
        if self.arrived.is_empty() && self.removed.is_empty() {
            return Taken::default();
        }
        if !self.try_start_live() {
            return Taken {
                retry: Some(SETTLE),
                ..Taken::default()
            };
        }
        let removed: Vec<String> = std::mem::take(&mut self.removed).into_iter().collect();
        let mut arrived = Vec::new();
        let mut retry = None;
        let wait = self
            .last_arrival_read
            .map(|t| GAP.saturating_sub(now.duration_since(t)));
        match wait {
            Some(w) if !self.arrived.is_empty() && !w.is_zero() => retry = Some(w),
            _ if !self.arrived.is_empty() => {
                arrived = std::mem::take(&mut self.arrived).into_iter().collect();
                self.last_arrival_read = Some(now);
            }
            _ => {}
        }
        if removed.is_empty() && arrived.is_empty() {
            self.end_live();
        }
        Taken {
            removed,
            arrived,
            retry,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn double_refresh_collapses_into_one() {
        let mut t = ReadingTurns::default();
        assert!(t.try_start_full());
        assert!(!t.try_start_full());
        t.end_full();
        assert!(t.try_start_full());
    }
    #[test]
    fn live_waits_for_full_and_for_other_live() {
        let mut t = ReadingTurns::default();
        assert!(t.try_start_full());
        assert!(!t.try_start_live());
        t.end_full();
        assert!(t.try_start_live());
        assert!(!t.try_start_live());
        t.end_live();
        assert!(t.try_start_live());
    }
    #[test]
    fn nothing_noted_takes_nothing() {
        let mut t = ReadingTurns::default();
        assert_eq!(t.take(Instant::now()), Taken::default());
    }
    #[test]
    fn arrival_is_read_once_and_takes_the_live_turn() {
        let mut t = ReadingTurns::default();
        t.note("/dev/hidraw5", true);
        let got = t.take(Instant::now());
        assert_eq!(got.arrived, vec!["/dev/hidraw5".to_string()]);
        assert!(
            !t.try_start_live(),
            "the live turn stays taken until end_live"
        );
        t.end_live();
        assert!(t.try_start_live());
    }
    #[test]
    fn second_arrival_within_gap_is_postponed() {
        let mut t = ReadingTurns::default();
        let t0 = Instant::now();
        t.note("/dev/hidraw5", true);
        assert_eq!(t.take(t0).arrived.len(), 1);
        t.end_live();

        t.note("/dev/hidraw6", true);
        let got = t.take(t0 + Duration::from_secs(4));
        assert!(got.arrived.is_empty());
        assert_eq!(got.retry, Some(Duration::from_secs(6)));
        assert!(
            t.try_start_live(),
            "a postponed take leaves no live turn behind"
        );
        t.end_live();

        let got = t.take(t0 + Duration::from_secs(10));
        assert_eq!(got.arrived, vec!["/dev/hidraw6".to_string()]);
    }
    #[test]
    fn removal_is_never_rate_limited() {
        let mut t = ReadingTurns::default();
        let t0 = Instant::now();
        t.note("/dev/hidraw5", true);
        t.take(t0);
        t.end_live();
        t.note("/dev/hidraw5", false);
        assert_eq!(
            t.take(t0 + Duration::from_secs(1)).removed,
            vec!["/dev/hidraw5".to_string()]
        );
    }
    #[test]
    fn busy_turn_retries_after_settle() {
        let mut t = ReadingTurns::default();
        t.note("/dev/hidraw5", true);
        assert!(t.try_start_full());
        let got = t.take(Instant::now());
        assert_eq!(got.retry, Some(SETTLE));
        assert!(got.arrived.is_empty());
    }
    #[test]
    fn a_node_that_came_and_went_is_only_removed() {
        let mut t = ReadingTurns::default();
        t.note("/dev/hidraw5", true);
        t.note("/dev/hidraw5", false);
        let got = t.take(Instant::now());
        assert!(got.arrived.is_empty());
        assert_eq!(got.removed.len(), 1);
    }
    #[test]
    fn forget_drops_pending_notes() {
        let mut t = ReadingTurns::default();
        t.note("/dev/hidraw5", true);
        t.forget();
        assert_eq!(t.take(Instant::now()), Taken::default());
    }
}
