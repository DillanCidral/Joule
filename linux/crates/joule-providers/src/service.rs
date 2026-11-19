//! One reading from start to end (ported from `Core/ReadingService.cs`).
//! On demand only: nothing in this file runs on a timer or in a background loop, by project
//! decision (SECURITY.md). The caller decides when: start, login, Refresh, or a hardware arrival.

use crate::registry::Registry;
use joule_core::{
    BatteryReading, DeviceEntry, DeviceKind, ReadingStatus,
    alerts::{LowBatteryAlert, LowBatteryTracker},
    cache::ReadingCache,
    compare_for_display, known,
    l::t,
    log,
    settings::Settings,
};
use joule_hid::{HidCollection, gate::Gate};
use std::{
    collections::{BTreeMap, HashSet},
    path::PathBuf,
    sync::Arc,
    time::{SystemTime, UNIX_EPOCH},
};

#[derive(Clone, Debug, Default)]
pub struct RefreshResult {
    pub devices: Vec<DeviceEntry>,
    pub when_unix: i64,
    pub fatal_error: Option<String>,
    /// Keys an arrival reading just read: the notice says "connected" even if the row already existed.
    pub arrived: Vec<String>,
}

pub struct ReadingService {
    registry: Registry,
    settings: Settings,
    cache: ReadingCache,
    gate: Arc<Gate>,
    dir: PathBuf,
    scan: Box<dyn Fn() -> Vec<HidCollection> + Send>,
    last_collections: Vec<HidCollection>,
    low: LowBatteryTracker,
}

fn now_unix() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0)
}

impl ReadingService {
    /// `dir` is the data directory (settings.json, state.json, joule.log).
    pub fn new(registry: Registry, settings: Settings, dir: PathBuf) -> Self {
        Self::with_scan(
            registry,
            settings,
            dir,
            Box::new(joule_hid::inventory::scan),
        )
    }

    pub fn with_scan(
        registry: Registry,
        settings: Settings,
        dir: PathBuf,
        scan: Box<dyn Fn() -> Vec<HidCollection> + Send>,
    ) -> Self {
        let cache = ReadingCache::load_from(&dir);
        Self {
            registry,
            settings,
            cache,
            gate: Arc::new(Gate::new()),
            dir,
            scan,
            last_collections: Vec::new(),
            low: LowBatteryTracker::default(),
        }
    }

    pub fn settings(&self) -> &Settings {
        &self.settings
    }

    pub fn gate(&self) -> &Arc<Gate> {
        &self.gate
    }

    /// Settings changed in the UI. A new threshold makes the alerts judge again.
    pub fn set_settings(&mut self, settings: Settings) {
        if settings.low_battery_threshold != self.settings.low_battery_threshold {
            self.low.clear();
        }
        self.settings = settings;
        self.settings.save_to(&self.dir);
    }

    /// Full reading: scan, ask every provider that claims something, list the rest as unsupported.
    pub fn refresh(&mut self) -> RefreshResult {
        self.gate
            .new_reading(self.settings.listen_only, &self.settings.ask_allowed_vids);
        let collections = (self.scan)();
        log::write(&format!(
            "Refresh: {} HID collections present.",
            collections.len()
        ));

        let mut entries: Vec<DeviceEntry> = Vec::new();
        let mut seen: HashSet<String> = HashSet::new();
        self.run_providers(&collections, &collections, &mut entries, &mut seen);
        self.add_unsupported(&collections, &collections, &mut entries, &mut seen);

        self.last_collections = collections;
        self.cache.save_to(&self.dir);
        entries.sort_by(compare_for_display);
        RefreshResult {
            devices: entries,
            when_unix: now_unix(),
            fatal_error: None,
            arrived: Vec::new(),
        }
    }

    /// Hardware arrived (kernel uevent for these nodes). Describes only the new collections and asks
    /// only the providers of those VID:PIDs, once, on top of the previous result. Nothing else is touched.
    /// `None` = nothing new.
    pub fn read_arrived(
        &mut self,
        previous: &RefreshResult,
        paths: &[String],
    ) -> Option<RefreshResult> {
        let known_paths: HashSet<_> = self
            .last_collections
            .iter()
            .map(|c| c.path.clone())
            .collect();
        let fresh: Vec<HidCollection> = (self.scan)()
            .into_iter()
            .filter(|c| {
                paths.iter().any(|p| c.path.as_os_str() == p.as_str())
                    && !known_paths.contains(&c.path)
            })
            .collect();
        if fresh.is_empty() {
            return None;
        }
        self.gate
            .new_reading(self.settings.listen_only, &self.settings.ask_allowed_vids);
        let ids: HashSet<(u16, u16)> = fresh.iter().map(|c| (c.vid, c.pid)).collect();
        log::write(&format!(
            "Arrival: {} new collections ({}).",
            fresh.len(),
            fresh
                .iter()
                .map(|c| c.hardware_id())
                .collect::<HashSet<_>>()
                .into_iter()
                .collect::<Vec<_>>()
                .join(", ")
        ));

        let mut all = self.last_collections.clone();
        all.extend(fresh.iter().cloned());
        // Only the VID:PIDs that arrived: devices of the same brand that were already on are not asked again.
        let arrived_only: Vec<HidCollection> = all
            .iter()
            .filter(|c| ids.contains(&(c.vid, c.pid)))
            .cloned()
            .collect();

        let mut replaced: Vec<DeviceEntry> = Vec::new();
        let mut seen = HashSet::new();
        self.run_providers(&arrived_only, &all, &mut replaced, &mut seen);
        self.add_unsupported(&fresh, &all, &mut replaced, &mut seen);
        self.last_collections = all;
        self.cache.save_to(&self.dir);

        let before: BTreeMap<&str, &DeviceEntry> = previous
            .devices
            .iter()
            .map(|d| (d.key.as_str(), d))
            .collect();
        let arrived = replaced
            .iter()
            .filter(|e| {
                e.is_connected
                    && before.get(e.key.as_str()).is_none_or(|o| {
                        !o.is_connected
                            || o.reading.status != e.reading.status
                            || o.origin != e.origin
                    })
            })
            .map(|e| e.key.clone())
            .collect();

        let mut devices: Vec<DeviceEntry> = previous
            .devices
            .iter()
            .filter(|d| !replaced.iter().any(|r| r.key == d.key))
            .cloned()
            .collect();
        devices.extend(replaced);
        devices.sort_by(compare_for_display);
        Some(RefreshResult {
            devices,
            when_unix: now_unix(),
            fatal_error: previous.fatal_error.clone(),
            arrived,
        })
    }

    /// Hardware left: a device with no collection left becomes "disconnected". Touches nothing
    /// (the device is gone, stop touching it). `None` = nothing known left.
    pub fn mark_removed(
        &mut self,
        previous: &RefreshResult,
        paths: &[String],
    ) -> Option<RefreshResult> {
        let gone = |c: &HidCollection| paths.iter().any(|p| c.path.as_os_str() == p.as_str());
        if !self.last_collections.iter().any(gone) {
            return None;
        }
        let removed: Vec<HidCollection> = self
            .last_collections
            .iter()
            .filter(|c| gone(c))
            .cloned()
            .collect();
        self.last_collections.retain(|c| !gone(c));
        let present: HashSet<String> = self
            .last_collections
            .iter()
            .map(|c| c.hardware_id())
            .collect();
        let lost: HashSet<String> = removed
            .iter()
            .map(|c| c.hardware_id())
            .filter(|id| !present.contains(id))
            .collect();
        if lost.is_empty() {
            return None;
        }

        let devices = previous
            .devices
            .iter()
            .map(|d| {
                let mine: Vec<&str> = d
                    .hardware_id
                    .split(", ")
                    .filter(|s| !s.is_empty())
                    .collect();
                if !d.is_connected
                    || !mine.iter().any(|id| lost.contains(*id))
                    || mine.iter().any(|id| present.contains(*id))
                {
                    return d.clone();
                }
                log::write(&format!("Disconnected: {}.", d.display_name));
                DeviceEntry {
                    reading: BatteryReading::disconnected(t(
                        "desconectou agora",
                        "just disconnected",
                    )),
                    is_connected: false,
                    is_live: false,
                    ..d.clone()
                }
            })
            .collect();
        Some(RefreshResult {
            devices,
            when_unix: previous.when_unix,
            fatal_error: previous.fatal_error.clone(),
            arrived: Vec::new(),
        })
    }

    /// Alerts for the end of a reading. Empty when alerts are off.
    pub fn low_battery_alerts(&mut self, result: &RefreshResult) -> Vec<LowBatteryAlert> {
        if !self.settings.alerts_enabled {
            return Vec::new();
        }
        self.low
            .evaluate(&result.devices, self.settings.low_battery_threshold)
    }

    fn run_providers(
        &mut self,
        asked: &[HidCollection],
        everything: &[HidCollection],
        entries: &mut Vec<DeviceEntry>,
        seen: &mut HashSet<String>,
    ) {
        let _ = everything;
        for provider in self.registry.standalone() {
            for mut entry in provider.discover(&self.gate, asked) {
                self.cache.update(
                    &entry.key,
                    &entry.display_name,
                    &entry.hardware_id,
                    &entry.reading,
                    now_unix(),
                );
                entry.cache = self.cache.get(&entry.key);
                seen.insert(entry.key.clone());
                log::write(&format!(
                    "  {}: {}",
                    entry.display_name,
                    describe(&entry.reading)
                ));
                entries.push(entry);
            }
        }
    }

    /// Collections no provider claims: listed as "unsupported", never with a value.
    fn add_unsupported(
        &self,
        groups_from: &[HidCollection],
        everything: &[HidCollection],
        entries: &mut Vec<DeviceEntry>,
        seen: &mut HashSet<String>,
    ) {
        let mut groups: BTreeMap<String, Vec<&HidCollection>> = BTreeMap::new();
        for c in groups_from {
            if !self.registry.handles(c.vid, c.pid) {
                groups.entry(c.hardware_id()).or_default().push(c);
            }
        }
        for (id, _) in groups {
            let group: Vec<&HidCollection> = everything
                .iter()
                .filter(|c| c.hardware_id() == id)
                .collect();
            let first = group[0];
            let entry = unsupported_row(&id, &group, first);
            if seen.insert(entry.key.clone()) {
                entries.push(entry);
            }
        }
    }
}

fn unsupported_row(id: &str, group: &[&HidCollection], first: &HidCollection) -> DeviceEntry {
    let name = known::name(first.vid, first.pid)
        .or_else(|| {
            group
                .iter()
                .map(|c| c.product.as_str())
                .find(|s| !s.trim().is_empty())
                .map(String::from)
        })
        .unwrap_or_else(|| t("Dispositivo HID", "HID device"));
    let reason = known::dead_end_reason(first.vid, first.pid);
    let usages: Vec<(u16, u16)> = group.iter().map(|c| (c.usage_page, c.usage)).collect();
    let kind = match known::kind(first.vid, first.pid) {
        DeviceKind::Generic => known::kind_from_usages(&usages),
        k => k,
    };
    DeviceEntry {
        key: format!("hid:{id}"),
        display_name: name,
        hardware_id: id.to_string(),
        provider_name: String::new(),
        reading: match reason {
            Some(r) => BatteryReading::unsupported_because(r),
            None => BatteryReading::unsupported(),
        },
        kind,
        origin: String::new(),
        is_live: false,
        is_connected: true,
        cache: None,
    }
}

pub fn describe(r: &BatteryReading) -> String {
    match r.status {
        ReadingStatus::Ok if r.percent.is_some() => format!("{}%", r.percent.unwrap()),
        ReadingStatus::Ok => r.level.clone().unwrap_or_else(|| "ok".into()),
        ReadingStatus::Charging => t("no cabo", "wired"),
        s => format!("{s:?} ({})", r.detail.clone().unwrap_or_default()),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::registry::StandaloneProvider;
    use std::sync::Mutex;

    fn col(path: &str, vid: u16, pid: u16, page: u16, usage: u16, product: &str) -> HidCollection {
        HidCollection {
            path: path.into(),
            vid,
            pid,
            usage_page: page,
            usage,
            input_len: 20,
            output_len: 20,
            feature_len: 0,
            product: product.into(),
        }
    }

    /// Claims 1234:0001 and answers with whatever the test put in `reading`.
    struct Fake {
        reading: Arc<Mutex<BatteryReading>>,
        calls: Arc<Mutex<Vec<usize>>>,
    }
    impl StandaloneProvider for Fake {
        fn name(&self) -> &str {
            "Fake"
        }
        fn handles(&self, vid: u16, pid: u16) -> bool {
            (vid, pid) == (0x1234, 0x0001)
        }
        fn discover(&self, _gate: &Arc<Gate>, collections: &[HidCollection]) -> Vec<DeviceEntry> {
            self.calls.lock().unwrap().push(collections.len());
            if !collections
                .iter()
                .any(|c| (c.vid, c.pid) == (0x1234, 0x0001))
            {
                return vec![];
            }
            vec![DeviceEntry {
                key: "fake:1".into(),
                display_name: "Fake headset".into(),
                hardware_id: "1234:0001".into(),
                provider_name: "Fake".into(),
                reading: self.reading.lock().unwrap().clone(),
                kind: DeviceKind::Headset,
                origin: "Dongle".into(),
                is_live: false,
                is_connected: true,
                cache: None,
            }]
        }
    }

    struct Rig {
        svc: ReadingService,
        reading: Arc<Mutex<BatteryReading>>,
        calls: Arc<Mutex<Vec<usize>>>,
        present: Arc<Mutex<Vec<HidCollection>>>,
        _dir: tempfile::TempDir,
    }

    fn rig(initial: Vec<HidCollection>) -> Rig {
        joule_core::log::mute();
        let dir = tempfile::tempdir().unwrap();
        let reading = Arc::new(Mutex::new(BatteryReading::percentage(70, None, None)));
        let calls = Arc::new(Mutex::new(Vec::new()));
        let present = Arc::new(Mutex::new(initial));
        let p2 = present.clone();
        let registry = Registry::new(vec![Box::new(Fake {
            reading: reading.clone(),
            calls: calls.clone(),
        })]);
        let svc = ReadingService::with_scan(
            registry,
            Settings::default(),
            dir.path().to_path_buf(),
            Box::new(move || p2.lock().unwrap().clone()),
        );
        Rig {
            svc,
            reading,
            calls,
            present,
            _dir: dir,
        }
    }

    fn headset(path: &str) -> HidCollection {
        col(path, 0x1234, 0x0001, 0xFF43, 0x0202, "Fake")
    }
    fn mouse(path: &str) -> HidCollection {
        col(path, 0x5678, 0x0002, 0x01, 0x02, "Office mouse")
    }

    #[test]
    fn refresh_lists_supported_first_then_unsupported() {
        let mut r = rig(vec![mouse("/dev/hidraw1"), headset("/dev/hidraw5")]);
        let out = r.svc.refresh();
        let names: Vec<_> = out
            .devices
            .iter()
            .map(|d| d.display_name.as_str())
            .collect();
        assert_eq!(names, ["Fake headset", "Office mouse"]);
        assert_eq!(out.devices[1].reading.status, ReadingStatus::Unsupported);
        assert_eq!(out.devices[1].kind, DeviceKind::Mouse);
        assert_eq!(
            out.devices[1].reading.percent, None,
            "unsupported never shows a number"
        );
    }
    #[test]
    fn two_collections_of_one_unsupported_device_make_one_row() {
        let mut r = rig(vec![
            mouse("/dev/hidraw1"),
            col("/dev/hidraw2", 0x5678, 0x0002, 0xFF00, 1, ""),
        ]);
        assert_eq!(r.svc.refresh().devices.len(), 1);
    }
    #[test]
    fn cache_fills_in_when_the_headset_goes_silent() {
        let mut r = rig(vec![headset("/dev/hidraw5")]);
        r.svc.refresh();
        *r.reading.lock().unwrap() = BatteryReading::no_data("off");
        let out = r.svc.refresh();
        let d = &out.devices[0];
        assert_eq!(d.reading.percent, None);
        assert_eq!(d.effective_percent(), Some(70), "value from the cache");
        assert!(d.shows_battery());
        assert!(d.cache.is_some());
    }
    #[test]
    fn cache_survives_a_restart() {
        let mut r = rig(vec![headset("/dev/hidraw5")]);
        r.svc.refresh();
        let dir = r._dir.path().to_path_buf();
        assert_eq!(
            ReadingCache::load_from(&dir).get("fake:1").unwrap().percent,
            Some(70)
        );
    }
    #[test]
    fn arrival_asks_only_the_new_device_and_keeps_the_rest() {
        let mut r = rig(vec![mouse("/dev/hidraw1")]);
        let first = r.svc.refresh();
        assert_eq!(first.devices.len(), 1);
        r.calls.lock().unwrap().clear();

        r.present.lock().unwrap().push(headset("/dev/hidraw5"));
        let out = r
            .svc
            .read_arrived(&first, &["/dev/hidraw5".into()])
            .unwrap();
        assert_eq!(out.devices.len(), 2);
        assert_eq!(out.arrived, vec!["fake:1".to_string()]);
        assert_eq!(
            *r.calls.lock().unwrap(),
            vec![1],
            "only the arrived collection was offered to providers"
        );
    }
    #[test]
    fn arrival_of_a_known_node_does_nothing() {
        let mut r = rig(vec![headset("/dev/hidraw5")]);
        let first = r.svc.refresh();
        assert!(
            r.svc
                .read_arrived(&first, &["/dev/hidraw5".into()])
                .is_none()
        );
    }
    #[test]
    fn removal_turns_the_row_into_disconnected_without_touching_anything() {
        let mut r = rig(vec![headset("/dev/hidraw5")]);
        let first = r.svc.refresh();
        r.calls.lock().unwrap().clear();
        let out = r
            .svc
            .mark_removed(&first, &["/dev/hidraw5".into()])
            .unwrap();
        assert!(!out.devices[0].is_connected);
        assert_eq!(out.devices[0].reading.status, ReadingStatus::Disconnected);
        assert!(!out.devices[0].shows_battery() && !out.devices[0].shows_as_other());
        assert!(r.calls.lock().unwrap().is_empty());
    }
    #[test]
    fn removal_of_one_of_two_collections_keeps_the_device() {
        let mut r = rig(vec![
            headset("/dev/hidraw5"),
            col("/dev/hidraw6", 0x1234, 0x0001, 0x01, 0x02, "Fake"),
        ]);
        let first = r.svc.refresh();
        assert!(
            r.svc
                .mark_removed(&first, &["/dev/hidraw6".into()])
                .is_none()
        );
    }
    #[test]
    fn low_battery_alert_comes_once_and_respects_the_switch() {
        let mut r = rig(vec![headset("/dev/hidraw5")]);
        *r.reading.lock().unwrap() = BatteryReading::percentage(12, None, None);
        let out = r.svc.refresh();
        assert_eq!(r.svc.low_battery_alerts(&out).len(), 1);
        assert!(r.svc.low_battery_alerts(&out).is_empty());

        let mut s = r.svc.settings().clone();
        s.alerts_enabled = false;
        r.svc.set_settings(s);
        assert!(r.svc.low_battery_alerts(&out).is_empty());
    }
    #[test]
    fn a_new_threshold_judges_again() {
        let mut r = rig(vec![headset("/dev/hidraw5")]);
        *r.reading.lock().unwrap() = BatteryReading::percentage(12, None, None);
        let out = r.svc.refresh();
        r.svc.low_battery_alerts(&out);
        let mut s = r.svc.settings().clone();
        s.low_battery_threshold = 30;
        r.svc.set_settings(s);
        assert_eq!(r.svc.low_battery_alerts(&out).len(), 1);
    }
}
