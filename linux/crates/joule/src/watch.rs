//! `joule --watch`: stays alive, rereads when a hidraw node comes or goes, prints what changed.
//! The timing rules live in `joule_core::turns`; this loop only wires events to them.

use crate::hotplug::{self, HidrawEvent};
use joule_core::{
    DeviceEntry, ReadingStatus,
    turns::{ReadingTurns, SETTLE},
};
use joule_providers::service::{ReadingService, RefreshResult, describe};
use std::{
    sync::mpsc,
    time::{Duration, Instant},
};

pub fn line(e: &DeviceEntry) -> String {
    let pct = e
        .effective_percent()
        .map(|p| format!("{p}%"))
        .unwrap_or_else(|| "-".into());
    let state = if !e.is_connected {
        "disconnected".into()
    } else if e.reading.status == ReadingStatus::Ok {
        e.reading
            .charge_text
            .clone()
            .or(e.reading.detail.clone())
            .unwrap_or_default()
    } else {
        describe(&e.reading)
    };
    format!("{:<24} {:>5}  {}", e.display_name, pct, state)
}

fn print_changes(before: &RefreshResult, after: &RefreshResult) {
    for e in &after.devices {
        let old = before.devices.iter().find(|o| o.key == e.key);
        if old.map(line).as_deref() != Some(&line(e)) {
            println!("[{}] {}", clock(), line(e));
        }
    }
}

fn clock() -> String {
    let secs = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0) as libc::time_t;
    // SAFETY: `tm` is a plain C struct that localtime_r fully writes; both pointers are valid for the call.
    let tm = unsafe {
        let mut tm: libc::tm = std::mem::zeroed();
        libc::localtime_r(&secs, &mut tm);
        tm
    };
    format!("{:02}:{:02}:{:02}", tm.tm_hour, tm.tm_min, tm.tm_sec)
}

pub fn run(mut svc: ReadingService) {
    let (tx, rx) = mpsc::channel::<HidrawEvent>();
    if let Err(e) = hotplug::spawn(move |ev| tx.send(ev).is_ok()) {
        eprintln!("cannot listen for hardware changes: {e}");
        std::process::exit(1);
    }
    let mut turns = ReadingTurns::default();
    assert!(turns.try_start_full());
    let mut result = svc.refresh();
    turns.end_full();
    turns.forget();
    for e in &result.devices {
        println!("{}", line(e));
    }
    println!("watching hidraw nodes (Ctrl+C to stop)");

    let mut due: Option<Instant> = None;
    loop {
        let event = match due {
            Some(at) => match rx.recv_timeout(at.saturating_duration_since(Instant::now())) {
                Ok(ev) => Some(ev),
                Err(mpsc::RecvTimeoutError::Timeout) => None,
                Err(mpsc::RecvTimeoutError::Disconnected) => return,
            },
            None => match rx.recv() {
                Ok(ev) => Some(ev),
                Err(_) => return,
            },
        };
        if let Some(ev) = event {
            turns.note(&ev.path, ev.arrived);
            due = Some(Instant::now() + SETTLE); // wait for the burst of collections to end
            continue;
        }

        let taken = turns.take(Instant::now());
        due = taken
            .retry
            .map(|d| Instant::now() + d.max(Duration::from_millis(50)));
        let had_work = !taken.removed.is_empty() || !taken.arrived.is_empty();
        if !taken.removed.is_empty()
            && let Some(next) = svc.mark_removed(&result, &taken.removed)
        {
            print_changes(&result, &next);
            result = next;
        }
        if !taken.arrived.is_empty()
            && let Some(next) = svc.read_arrived(&result, &taken.arrived)
        {
            print_changes(&result, &next);
            for a in svc.low_battery_alerts(&next) {
                println!("[{}] LOW BATTERY {} {}", clock(), a.name, a.value);
            }
            result = next;
        }
        if had_work {
            turns.end_live();
        }
    }
}
