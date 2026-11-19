//! The notice sounds (ported from `Ui/Sounds.cs`: connected, found, not found, low battery,
//! disconnected). Played with the desktop sound theme through `canberra-gtk-play`; silent when the
//! tool is missing. Only plays when the setting is on.

use std::process::{Command, Stdio};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum Tone {
    Connected,
    NotFound,
    Low,
    Disconnected,
}

/// The freedesktop sound theme event id of each tone.
pub fn event_id(t: Tone) -> &'static str {
    match t {
        Tone::Connected => "device-added",
        Tone::NotFound => "dialog-warning",
        Tone::Low => "battery-low",
        Tone::Disconnected => "device-removed",
    }
}

pub fn play(enabled: bool, t: Tone) {
    if !enabled {
        return;
    }
    let _ = Command::new("canberra-gtk-play")
        .args(["-i", event_id(t)])
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn();
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn every_tone_has_a_distinct_event() {
        let ids: Vec<_> = [
            Tone::Connected,
            Tone::NotFound,
            Tone::Low,
            Tone::Disconnected,
        ]
        .iter()
        .map(|t| event_id(*t))
        .collect();
        let mut sorted = ids.clone();
        sorted.sort();
        sorted.dedup();
        assert_eq!(sorted.len(), 4);
    }
}
