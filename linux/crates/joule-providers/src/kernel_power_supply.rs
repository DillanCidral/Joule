//! Batteries the Linux kernel already publishes in `/sys/class/power_supply` (scope "Device"):
//! Logitech devices behind a receiver (hid-logitech-hidpp), DualSense, Bluetooth LE mice and
//! keyboards, and more. This is the first source on Linux, as planned in DillanCidral/Joule#174 and
//! sketched in DillanCidral/Joule#43: it only reads sysfs, nothing is sent to any device.
//!
//! A receiver whose paired device the kernel already reports is not asked over HID++ again
//! ([`covered_hid_roots`] / [`is_covered`]).

use crate::registry::StandaloneProvider;
use joule_core::{BatteryReading, ChargeState, DeviceEntry, known, l::t};
use joule_hid::{HidCollection, gate::Gate};
use std::{
    fs,
    path::{Path, PathBuf},
    sync::Arc,
};

fn read(dir: &Path, name: &str) -> Option<String> {
    fs::read_to_string(dir.join(name))
        .ok()
        .map(|s| s.trim().to_string())
        .filter(|s| !s.is_empty())
}

/// One device battery from sysfs.
#[derive(Clone, Debug, PartialEq)]
pub struct KernelBattery {
    pub name: String,
    pub model: String,
    pub manufacturer: Option<String>,
    pub capacity: Option<u8>,
    pub capacity_level: Option<String>,
    pub status: Option<String>,
    pub voltage_based: bool,
    /// `bus:vid:pid` of the HID device the battery belongs to (from its `uevent`), if any.
    pub hid_id: Option<(u16, u16, u16)>,
    /// Real sysfs path of the device the battery belongs to.
    pub device_path: Option<PathBuf>,
}

/// `HID_ID=0003:0000046D:00004086` to (bus, vid, pid).
pub fn parse_hid_id(uevent: &str) -> Option<(u16, u16, u16)> {
    let v = uevent.lines().find_map(|l| l.strip_prefix("HID_ID="))?;
    let mut parts = v.split(':');
    let bus = u16::from_str_radix(parts.next()?, 16).ok()?;
    let vid = u32::from_str_radix(parts.next()?, 16).ok()? as u16;
    let pid = u32::from_str_radix(parts.next()?, 16).ok()? as u16;
    Some((bus, vid, pid))
}

/// Every device-scope battery under `sys_root` (normally `/sys`).
pub fn scan_in(sys_root: &Path) -> Vec<KernelBattery> {
    let class = sys_root.join("class/power_supply");
    let Ok(entries) = fs::read_dir(&class) else {
        return Vec::new();
    };
    let mut out: Vec<KernelBattery> = entries
        .flatten()
        .filter_map(|e| {
            let dir = e.path();
            if read(&dir, "scope").as_deref() != Some("Device") {
                return None;
            }
            let device_path = fs::canonicalize(dir.join("device")).ok();
            let hid_id = device_path
                .as_ref()
                .and_then(|p| fs::read_to_string(p.join("uevent")).ok())
                .and_then(|u| parse_hid_id(&u));
            Some(KernelBattery {
                name: e.file_name().to_string_lossy().into_owned(),
                model: read(&dir, "model_name")
                    .unwrap_or_else(|| e.file_name().to_string_lossy().into_owned()),
                manufacturer: read(&dir, "manufacturer"),
                capacity: read(&dir, "capacity")
                    .and_then(|c| c.parse::<u8>().ok())
                    .filter(|c| *c <= 100),
                capacity_level: read(&dir, "capacity_level"),
                status: read(&dir, "status"),
                voltage_based: dir.join("voltage_now").exists(),
                hid_id,
                device_path,
            })
        })
        .collect();
    out.sort_by(|a, b| a.name.cmp(&b.name));
    out
}

pub fn scan() -> Vec<KernelBattery> {
    scan_in(Path::new("/sys"))
}

/// The reading the kernel gives. A voltage-based battery that charges is handed to the charge
/// estimate of the reading service (the kernel maps the raised charging voltage too).
pub fn reading(b: &KernelBattery) -> BatteryReading {
    let status = b.status.as_deref().unwrap_or("Unknown");
    if status == "Full" {
        let mut r = BatteryReading::percentage(
            100,
            Some(t("informado pelo kernel", "reported by the kernel")),
            Some(t("carga completa", "fully charged")),
        );
        r.charge_state = Some(ChargeState::Full);
        return r;
    }
    let charging = status == "Charging";
    let charge_text = charging.then(|| t("carregando", "charging"));
    let mut r = match (b.capacity, b.capacity_level.as_deref()) {
        (Some(c), _) => BatteryReading::percentage(
            i32::from(c),
            Some(t("informado pelo kernel", "reported by the kernel")),
            charge_text,
        ),
        (None, Some("Full" | "High")) => BatteryReading::coarse("FULL"),
        (None, Some("Normal")) => BatteryReading::coarse("MEDIUM"),
        (None, Some("Low")) => BatteryReading::coarse("LOW"),
        (None, Some("Critical")) => BatteryReading::coarse("EMPTY"),
        _ if charging => {
            BatteryReading::charging(Some(t("informado pelo kernel", "reported by the kernel")))
        }
        _ => BatteryReading::no_data(t(
            "o kernel não informou o nível",
            "the kernel didn’t report the level",
        )),
    };
    if charging {
        r.charge_state = Some(ChargeState::Charging);
        r.voltage_based = b.voltage_based && b.capacity.is_some();
    }
    r
}

pub fn entry(b: &KernelBattery) -> DeviceEntry {
    let name = match &b.manufacturer {
        Some(m) if !b.model.to_lowercase().contains(&m.to_lowercase()) => {
            format!("{m} {}", b.model)
        }
        _ => b.model.clone(),
    };
    let (hardware_id, origin) = match b.hid_id {
        Some((bus, vid, pid)) => {
            let under_receiver = b
                .device_path
                .as_ref()
                .and_then(|p| p.parent())
                .and_then(|p| p.file_name())
                .is_some_and(|n| n.to_string_lossy().starts_with("0003:"));
            let origin = match bus {
                0x0005 => "Bluetooth",
                _ if under_receiver => "Dongle",
                _ => "Cable",
            };
            (known::id(vid, pid), origin)
        }
        None => (String::new(), ""),
    };
    DeviceEntry {
        key: format!("kernel:{}", b.name),
        display_name: name.clone(),
        hardware_id,
        provider_name: "Kernel".into(),
        reading: reading(b),
        kind: known::bluetooth_kind(None, &name),
        origin: origin.into(),
        is_live: false,
        is_connected: true,
        cache: None,
    }
}

/// Sysfs paths of the HID devices whose batteries the kernel reports.
pub fn covered_hid_roots(batteries: &[KernelBattery]) -> Vec<PathBuf> {
    batteries
        .iter()
        .filter_map(|b| b.device_path.clone())
        .collect()
}

/// True when the kernel already reports a battery of a device behind this hidraw node (the battery's
/// device sits under the node's HID device in sysfs).
pub fn is_covered(sys_root: &Path, hidraw: &Path, roots: &[PathBuf]) -> bool {
    let Some(name) = hidraw.file_name() else {
        return false;
    };
    let Ok(hid) = fs::canonicalize(sys_root.join("class/hidraw").join(name).join("device")) else {
        return false;
    };
    roots.iter().any(|r| r.starts_with(&hid))
}

pub struct KernelProvider;

impl StandaloneProvider for KernelProvider {
    fn name(&self) -> &str {
        "Kernel"
    }
    fn handles(&self, _vid: u16, _pid: u16) -> bool {
        false
    }
    fn discover(&self, _gate: &Arc<Gate>, _collections: &[HidCollection]) -> Vec<DeviceEntry> {
        scan().iter().map(entry).collect()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::os::unix::fs::symlink;

    /// A fake /sys with the G703 behind its receiver, like on the test PC.
    fn fake_sys() -> tempfile::TempDir {
        let root = tempfile::tempdir().unwrap();
        let r = root.path();
        let receiver = r.join("devices/usb1/1-1/1-1:1.2/0003:046D:C539.0011");
        let mouse = receiver.join("0003:046D:4086.0012");
        fs::create_dir_all(&mouse).unwrap();
        fs::write(mouse.join("uevent"), "DRIVER=logitech-hidpp-device\nHID_ID=0003:0000046D:00004086\nHID_NAME=Logitech G703 LS\n").unwrap();
        let bat = r.join("class/power_supply/hidpp_battery_2");
        fs::create_dir_all(&bat).unwrap();
        for (f, v) in [
            ("scope", "Device\n"),
            (
                "model_name",
                "G703 LIGHTSPEED Wireless Gaming Mouse w/ HERO\n",
            ),
            ("manufacturer", "Logitech\n"),
            ("capacity", "60\n"),
            ("status", "Discharging\n"),
            ("voltage_now", "3862000\n"),
        ] {
            fs::write(bat.join(f), v).unwrap();
        }
        symlink(&mouse, bat.join("device")).unwrap();
        // a laptop battery is not a device battery
        let sys = r.join("class/power_supply/BAT0");
        fs::create_dir_all(&sys).unwrap();
        fs::write(sys.join("scope"), "System\n").unwrap();
        // the receiver's hidraw node
        let hidraw = r.join("class/hidraw/hidraw4");
        fs::create_dir_all(&hidraw).unwrap();
        symlink(&receiver, hidraw.join("device")).unwrap();
        let other = r.join("devices/usb2/0003:046D:0A5B.0001");
        fs::create_dir_all(&other).unwrap();
        let hidraw5 = r.join("class/hidraw/hidraw5");
        fs::create_dir_all(&hidraw5).unwrap();
        symlink(&other, hidraw5.join("device")).unwrap();
        root
    }

    #[test]
    fn reads_device_batteries_only() {
        let sys = fake_sys();
        let list = scan_in(sys.path());
        assert_eq!(list.len(), 1);
        let b = &list[0];
        assert_eq!(b.capacity, Some(60));
        assert_eq!(b.hid_id, Some((3, 0x046D, 0x4086)));
        assert!(b.voltage_based);
    }
    #[test]
    fn the_entry_names_the_device_and_its_receiver() {
        let sys = fake_sys();
        let e = entry(&scan_in(sys.path())[0]);
        assert_eq!(
            e.display_name,
            "Logitech G703 LIGHTSPEED Wireless Gaming Mouse w/ HERO"
        );
        assert_eq!(e.hardware_id, "046D:4086");
        assert_eq!(e.origin, "Dongle");
        assert_eq!(e.reading.percent, Some(60));
        assert_eq!(e.key, "kernel:hidpp_battery_2");
        assert_eq!(e.kind, joule_core::DeviceKind::Mouse);
    }
    #[test]
    fn a_receiver_the_kernel_covers_is_not_asked() {
        let sys = fake_sys();
        let roots = covered_hid_roots(&scan_in(sys.path()));
        assert!(
            is_covered(sys.path(), Path::new("/dev/hidraw4"), &roots),
            "the C539 receiver"
        );
        assert!(
            !is_covered(sys.path(), Path::new("/dev/hidraw5"), &roots),
            "the G933 dongle is not covered"
        );
    }
    #[test]
    fn statuses_map_to_readings() {
        let mut b = scan_in(fake_sys().path())[0].clone();
        b.status = Some("Charging".into());
        let r = reading(&b);
        assert_eq!(r.charge_state, Some(ChargeState::Charging));
        assert!(
            r.voltage_based,
            "voltage-based: the charge estimate takes over"
        );
        b.status = Some("Full".into());
        assert_eq!(reading(&b).percent, Some(100));
        b.status = Some("Discharging".into());
        b.capacity = None;
        b.capacity_level = Some("Low".into());
        assert_eq!(reading(&b).level.as_deref(), Some("LOW"));
        b.capacity_level = None;
        assert_eq!(reading(&b).status, joule_core::ReadingStatus::NoData);
    }
    #[test]
    fn hid_id_parsing() {
        assert_eq!(
            parse_hid_id("HID_ID=0005:0000054C:00000CE6\n"),
            Some((5, 0x054C, 0x0CE6))
        );
        assert_eq!(parse_hid_id("DRIVER=x\n"), None);
    }
}
