//! Consent, "listen only", block list and the per-reading question cap.

use crate::{HidCollection, inventory::is_system_reserved};
use std::{
    collections::{BTreeMap, HashSet},
    sync::{
        Mutex,
        atomic::{AtomicUsize, Ordering},
    },
};

pub const MAX_ASKS: usize = 48;

/// Any collection except the system mouse and keyboard.
pub fn can_ask(c: &HidCollection) -> bool {
    !is_system_reserved(c.usage_page, c.usage)
}

pub fn is_vendor(c: &HidCollection) -> bool {
    c.usage_page >= 0xFF00 && can_ask(c)
}

#[derive(Default)]
struct State {
    listen_only: bool,
    allowed: HashSet<u16>,
    waiting: BTreeMap<u16, String>,
}

pub struct Gate {
    state: Mutex<State>,
    blocked: HashSet<String>,
    /// Brands confirmed on real hardware ask without consent. Empty until a maintainer confirms one.
    tested: HashSet<u16>,
    reading_asks: AtomicUsize,
    sent: AtomicUsize,
}

impl Gate {
    pub fn new() -> Self {
        Self::with_blocked(&[])
    }

    pub fn with_blocked(blocked: &[&str]) -> Self {
        Self {
            state: Mutex::new(State::default()),
            blocked: blocked.iter().map(|s| s.to_uppercase()).collect(),
            tested: HashSet::new(),
            reading_asks: AtomicUsize::new(0),
            sent: AtomicUsize::new(0),
        }
    }

    /// A reading starts: the question counter resets and the current consent applies.
    pub fn new_reading(&self, listen_only: bool, allowed_vids: &[String]) {
        let mut s = self.state.lock().unwrap();
        s.listen_only = listen_only;
        s.allowed = allowed_vids
            .iter()
            .filter_map(|v| u16::from_str_radix(v.trim(), 16).ok())
            .collect();
        let allowed = s.allowed.clone();
        s.waiting.retain(|vid, _| !allowed.contains(vid));
        self.reading_asks.store(0, Ordering::SeqCst);
    }

    pub fn permit(&self, c: &HidCollection) -> bool {
        let mut s = self.state.lock().unwrap();
        if s.listen_only || self.blocked.contains(&c.hardware_id()) {
            return false;
        }
        if self.tested.contains(&c.vid) || s.allowed.contains(&c.vid) {
            return true;
        }
        s.waiting.entry(c.vid).or_insert_with(|| {
            joule_core::log::write(&format!(
                "Brand {:04X} ({}) has no consent to ask the battery: nothing sent.",
                c.vid, c.product
            ));
            c.product.clone()
        });
        false
    }

    /// Counts one question against the cap. False means over the cap.
    pub fn count_ask(&self) -> bool {
        let n = self.reading_asks.fetch_add(1, Ordering::SeqCst) + 1;
        if n == MAX_ASKS + 1 {
            joule_core::log::write(&format!(
                "Cap of {MAX_ASKS} questions for this reading reached."
            ));
        }
        n <= MAX_ASKS
    }

    pub(crate) fn note_sent(&self) {
        self.sent.fetch_add(1, Ordering::SeqCst);
    }

    /// Questions that reached a write since the process started.
    pub fn sent(&self) -> usize {
        self.sent.load(Ordering::SeqCst)
    }

    /// Why no question went out for this brand in this reading, in words for the user.
    /// `None` = questions were allowed (a missing answer is then the device's doing).
    pub fn silent_reason(&self, vid: u16) -> Option<String> {
        use joule_core::l::t;
        let s = self.state.lock().unwrap();
        if s.listen_only {
            Some(t(
                "só escutando: nenhuma pergunta enviada",
                "listen only: no question sent",
            ))
        } else if s.waiting.contains_key(&vid) {
            Some(t(
                &format!("sem licença para perguntar a bateria (joule allow {vid:04X})"),
                &format!("no consent to ask the battery (run: joule allow {vid:04X})"),
            ))
        } else {
            None
        }
    }

    pub fn waiting_consent(&self) -> Vec<(u16, String)> {
        self.state
            .lock()
            .unwrap()
            .waiting
            .iter()
            .map(|(k, v)| (*k, v.clone()))
            .collect()
    }
}

impl Default for Gate {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;

    fn col(vid: u16, page: u16, usage: u16) -> HidCollection {
        HidCollection {
            path: PathBuf::from("/dev/hidraw9"),
            vid,
            pid: 1,
            usage_page: page,
            usage,
            input_len: 20,
            output_len: 20,
            feature_len: 0,
            product: "T".into(),
        }
    }

    #[test]
    fn untested_brand_needs_consent() {
        joule_core::log::mute();
        let g = Gate::new();
        g.new_reading(false, &[]);
        assert!(!g.permit(&col(0x046D, 0xFF43, 0x0202)));
        assert_eq!(g.waiting_consent(), vec![(0x046D, "T".to_string())]);
        g.new_reading(false, &["046d".to_string()]);
        assert!(g.permit(&col(0x046D, 0xFF43, 0x0202)));
        assert!(g.waiting_consent().is_empty());
    }
    #[test]
    fn listen_only_refuses_even_allowed() {
        joule_core::log::mute();
        let g = Gate::new();
        g.new_reading(true, &["046D".to_string()]);
        assert!(!g.permit(&col(0x046D, 0xFF43, 0x0202)));
    }
    #[test]
    fn blocked_model_refused_even_when_allowed() {
        let g = Gate::with_blocked(&["046D:0001"]);
        g.new_reading(false, &["046D".to_string()]);
        assert!(!g.permit(&col(0x046D, 0xFF43, 0x0202)));
    }
    #[test]
    fn system_collections_never_askable() {
        assert!(!can_ask(&col(0x046D, 0x01, 0x02)));
        assert!(!can_ask(&col(0x046D, 0x01, 0x06)));
        assert!(can_ask(&col(0x046D, 0xFF43, 0x0202)));
        assert!(is_vendor(&col(0x046D, 0xFF43, 0x0202)));
        assert!(!is_vendor(&col(0x046D, 0x0C, 0x01)));
    }
    #[test]
    fn cap_is_48_per_reading() {
        joule_core::log::mute();
        let g = Gate::new();
        g.new_reading(false, &[]);
        for _ in 0..MAX_ASKS {
            assert!(g.count_ask());
        }
        assert!(!g.count_ask());
        g.new_reading(false, &[]);
        assert!(g.count_ask());
    }

    #[test]
    fn silent_reason_names_the_cause() {
        joule_core::log::mute();
        let g = Gate::new();
        g.new_reading(false, &[]);
        assert_eq!(g.silent_reason(0x046D), None, "nothing refused yet");
        g.permit(&col(0x046D, 0xFF43, 0x0202));
        assert!(g.silent_reason(0x046D).unwrap().contains("046D"));
        g.new_reading(true, &["046D".into()]);
        assert!(
            g.silent_reason(0x046D)
                .unwrap()
                .to_lowercase()
                .contains("listen")
        );
    }
}
