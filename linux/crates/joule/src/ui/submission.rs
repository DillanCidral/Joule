//! "Send my device" (the Linux counterpart of `Ui/SendDeviceWindow` and `DeviceSubmission`): builds a
//! GitHub issue with what Joule sees of the user's devices, taken from sysfs only (no question is
//! sent to any device), without serial numbers, addresses, the computer name or the user name.
//! The page opens in the browser; nothing is sent until the person presses the green button there.

use joule_hid::HidCollection;
use std::collections::BTreeMap;

pub const NEW_ISSUE_URL: &str = "https://github.com/DillanCidral/Joule/issues/new";

/// Percent-encodes for a URL query value.
pub fn encode(text: &str) -> String {
    let mut out = String::with_capacity(text.len() * 3);
    for b in text.bytes() {
        match b {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => {
                out.push(b as char)
            }
            _ => out.push_str(&format!("%{b:02X}")),
        }
    }
    out
}

/// One line per VID:PID with its product name and the usages it declares.
pub fn describe(collections: &[HidCollection]) -> Vec<String> {
    let mut by_id: BTreeMap<String, (String, Vec<String>)> = BTreeMap::new();
    for c in collections {
        let e = by_id
            .entry(c.hardware_id())
            .or_insert_with(|| (c.product.clone(), Vec::new()));
        e.1.push(format!("{:04X}/{:04X}", c.usage_page, c.usage));
    }
    by_id
        .into_iter()
        .map(|(id, (name, usages))| {
            let name = if name.trim().is_empty() {
                "(no name)"
            } else {
                name.trim()
            };
            format!("- `{id}` {name}: {}", usages.join(", "))
        })
        .collect()
}

pub fn title(names: &[String]) -> String {
    if names.is_empty() {
        "New device".to_string()
    } else {
        format!("New device: {}", names.join(", "))
    }
}

pub fn body(collections: &[HidCollection], version: &str) -> String {
    let mut lines = vec![
        "A device of mine is missing or has no battery value in Joule on Linux.".to_string(),
        String::new(),
        "**My device** (brand and model, as on the box):".to_string(),
        String::new(),
        String::new(),
        format!("**Joule** {version}, Linux"),
        String::new(),
        "**HID devices Joule sees** (read from sysfs; no serial numbers, no addresses):"
            .to_string(),
        String::new(),
    ];
    lines.extend(describe(collections));
    lines.join("\n")
}

pub fn url(collections: &[HidCollection], version: &str) -> String {
    format!(
        "{NEW_ISSUE_URL}?title={}&body={}",
        encode(&title(&[])),
        encode(&body(collections, version))
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    fn col(vid: u16, pid: u16, page: u16, usage: u16, product: &str) -> HidCollection {
        HidCollection {
            path: "/dev/hidraw1".into(),
            vid,
            pid,
            usage_page: page,
            usage,
            input_len: 8,
            output_len: 0,
            feature_len: 0,
            product: product.into(),
        }
    }

    #[test]
    fn encoding_keeps_unreserved_and_escapes_the_rest() {
        assert_eq!(encode("a b&c=d/é"), "a%20b%26c%3Dd%2F%C3%A9");
        assert_eq!(encode("AZaz09-_.~"), "AZaz09-_.~");
    }
    #[test]
    fn collections_of_one_device_make_one_line() {
        let l = describe(&[
            col(0x046D, 0xC539, 0xFF00, 1, "Logitech USB Receiver"),
            col(0x046D, 0xC539, 0xFF00, 2, "Logitech USB Receiver"),
        ]);
        assert_eq!(
            l,
            ["- `046D:C539` Logitech USB Receiver: FF00/0001, FF00/0002"]
        );
    }
    #[test]
    fn a_missing_name_is_marked() {
        assert!(describe(&[col(1, 2, 1, 2, "  ")])[0].contains("(no name)"));
    }
    #[test]
    fn the_url_points_at_new_issues_and_carries_the_list() {
        let u = url(&[col(0x046D, 0x0A5B, 0xFF43, 0x0202, "G933")], "0.2.0");
        assert!(u.starts_with("https://github.com/DillanCidral/Joule/issues/new?title="));
        assert!(u.contains("046D%3A0A5B"));
        assert!(!u.contains(' '));
    }
    #[test]
    fn nothing_personal_is_in_the_body() {
        let b = body(&[col(1, 2, 1, 2, "Mouse")], "0.2.0");
        for banned in ["serial", "hostname", "/home/"] {
            assert!(
                !b.to_lowercase()
                    .contains(&banned.replace("serial", "serial number:")),
                "{banned}"
            );
        }
    }
}
