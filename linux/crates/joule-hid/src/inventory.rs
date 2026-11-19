//! Read-only inventory from sysfs. It never opens a /dev node.

use crate::{HidCollection, descriptor};
use std::{
    fs,
    path::{Path, PathBuf},
};

/// System mouse (`0x01/0x02`) and keyboard (`0x01/0x06`) collections: never touched.
pub fn is_system_reserved(usage_page: u16, usage: u16) -> bool {
    usage_page == 0x0001 && (usage == 0x0002 || usage == 0x0006)
}

pub fn scan() -> Vec<HidCollection> {
    scan_in(Path::new("/sys/class/hidraw"), Path::new("/dev"))
}

pub fn scan_in(sys_root: &Path, dev_root: &Path) -> Vec<HidCollection> {
    let Ok(entries) = fs::read_dir(sys_root) else {
        return Vec::new();
    };
    let mut names: Vec<String> = entries
        .filter_map(|e| e.ok()?.file_name().into_string().ok())
        .filter(|n| n.starts_with("hidraw"))
        .collect();
    names.sort();

    let mut out = Vec::new();
    for name in names {
        let dev = sys_root.join(&name).join("device");
        let Ok(uevent) = fs::read_to_string(dev.join("uevent")) else {
            continue;
        };
        let (mut vid, mut pid, mut product) = (None, None, String::new());
        for line in uevent.lines() {
            if let Some(v) = line.strip_prefix("HID_ID=") {
                let parts: Vec<&str> = v.split(':').collect();
                if parts.len() == 3 {
                    vid = u32::from_str_radix(parts[1], 16).ok().map(|x| x as u16);
                    pid = u32::from_str_radix(parts[2], 16).ok().map(|x| x as u16);
                }
            } else if let Some(v) = line.strip_prefix("HID_NAME=") {
                product = v.trim().to_string();
            }
        }
        let (Some(vid), Some(pid)) = (vid, pid) else {
            continue;
        };
        let Ok(desc) = fs::read(dev.join("report_descriptor")) else {
            continue;
        };
        let path: PathBuf = dev_root.join(&name);
        for t in descriptor::parse(&desc) {
            out.push(HidCollection {
                path: path.clone(),
                vid,
                pid,
                usage_page: t.usage_page,
                usage: t.usage,
                input_len: t.input_len,
                output_len: t.output_len,
                feature_len: t.feature_len,
                product: product.clone(),
            });
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    fn fake(root: &Path, node: &str, hid_id: &str, name: &str, desc: &[u8]) {
        let dev = root.join(node).join("device");
        fs::create_dir_all(&dev).unwrap();
        fs::write(
            dev.join("uevent"),
            format!("DRIVER=hid-generic\nHID_ID={hid_id}\nHID_NAME={name}\n"),
        )
        .unwrap();
        fs::write(dev.join("report_descriptor"), desc).unwrap();
    }

    #[test]
    fn lists_collections_from_sysfs() {
        let sys = tempfile::tempdir().unwrap();
        let vendor = [
            0x06, 0x43, 0xFF, 0x0A, 0x02, 0x02, 0xA1, 0x01, 0x85, 0x11, 0x75, 0x08, 0x95, 0x13,
            0x09, 0x01, 0x81, 0x00, 0x09, 0x01, 0x91, 0x00, 0xC0,
        ];
        fake(
            sys.path(),
            "hidraw3",
            "0003:0000046D:00000A5B",
            "Logitech G933 Wireless Headset Dongle",
            &vendor,
        );
        fake(
            sys.path(),
            "hidraw4",
            "0003:00001234:00005678",
            "Broken",
            &[0xFF],
        ); // no collections
        let found = scan_in(sys.path(), Path::new("/dev"));
        assert_eq!(found.len(), 1);
        let c = &found[0];
        assert_eq!((c.vid, c.pid), (0x046D, 0x0A5B));
        assert_eq!(c.hardware_id(), "046D:0A5B");
        assert_eq!(c.path, PathBuf::from("/dev/hidraw3"));
        assert_eq!(c.product, "Logitech G933 Wireless Headset Dongle");
        assert_eq!((c.usage_page, c.usage, c.output_len), (0xFF43, 0x0202, 20));
    }

    #[test]
    fn system_reserved() {
        assert!(is_system_reserved(0x01, 0x02));
        assert!(is_system_reserved(0x01, 0x06));
        assert!(!is_system_reserved(0xFF43, 0x0202));
    }
}
