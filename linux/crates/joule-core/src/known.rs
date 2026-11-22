//! Friendly names and notes for known VID:PIDs (ported from `Core/Known.cs`).
//! Cosmetic only: support is decided by the provider registry.

use crate::{l::t, model::DeviceKind};

pub fn id(vid: u16, pid: u16) -> String {
    format!("{vid:04X}:{pid:04X}")
}

pub fn name(vid: u16, pid: u16) -> Option<String> {
    let n = match id(vid, pid).as_str() {
        "1D57:FA60" => "Mouse Attack Shark (dongle 2.4GHz)".to_string(),
        "1D57:FA61" => t("Mouse Attack Shark (cabo)", "Attack Shark mouse (wired)"),
        "24AE:1871" => "Rapoo MT760 Mini".to_string(),
        "291D:385D" => "MCHOSE V9 PRO (headset)".to_string(),
        "2DC8:310A" => "8BitDo Ultimate 2C".to_string(),
        "2DC8:301C" => t(
            "8BitDo Ultimate 2C (dongle ocioso)",
            "8BitDo Ultimate 2C (idle dongle)",
        ),
        "054C:0CE6" => "DualSense (PS5)".to_string(),
        "045E:0B12" => "Xbox Wireless Controller".to_string(),
        "045E:02E6" => "Xbox Wireless Adapter".to_string(),
        "045E:028E" => "Xbox 360 Controller".to_string(),
        _ => return None,
    };
    Some(n)
}

/// Cases proven to have no battery path on this transport. Listed as "unsupported" with the reason.
pub fn dead_end_reason(vid: u16, pid: u16) -> Option<String> {
    let r = match id(vid, pid).as_str() {
        "291D:385D" => t(
            "Pelo dongle não há bateria (firmware 0004 do receptor); pelo Bluetooth o sistema informa.",
            "No battery through the dongle (receiver firmware 0004); over Bluetooth, the system reports it.",
        ),
        "2DC8:310A" => t(
            "Pelo dongle não há caminho de bateria; pelo Bluetooth o sistema informa.",
            "No battery path through the dongle; over Bluetooth, the system reports it.",
        ),
        "04CA:00BD" => t(
            "Sem caminho de bateria por este receptor: o teclado só responde a um comando que ninguém documentou.",
            "No battery path through this receiver: the keyboard only answers a command nobody has documented.",
        ),
        "0951:16B7" => t(
            "Teclado com fio, sem bateria.",
            "Wired keyboard, no battery.",
        ),
        _ => return None,
    };
    Some(r)
}

pub fn kind(vid: u16, pid: u16) -> DeviceKind {
    match id(vid, pid).as_str() {
        "1D57:FA60" | "1D57:FA61" | "24AE:1871" => DeviceKind::Mouse,
        "291D:385D" => DeviceKind::Headset,
        "2DC8:310A" | "2DC8:301C" | "054C:0CE6" | "045E:0B12" | "045E:02E6" | "045E:028E"
        | "045E:02FF" => DeviceKind::Gamepad,
        "0951:16B7" | "04CA:00BD" => DeviceKind::Keyboard,
        _ => DeviceKind::Generic,
    }
}

/// Kind of an unknown HID from the collections it declares. Mouse and keyboard are the system
/// reserved usages (0x01/0x02, 0x01/0x06); joystick/gamepad 0x01/0x04-0x05; telephony (page 0x0B)
/// is the call control only headsets have.
pub fn kind_from_usages(usages: &[(u16, u16)]) -> DeviceKind {
    let has = |p: u16, u: u16| usages.contains(&(p, u));
    if has(0x01, 0x04) || has(0x01, 0x05) {
        return DeviceKind::Gamepad;
    }
    if usages.iter().any(|(p, _)| *p == 0x0B) {
        return DeviceKind::Headset;
    }
    // Many wireless mice declare a "keyboard" for macros: mouse wins.
    if has(0x01, 0x02) {
        return DeviceKind::Mouse;
    }
    if has(0x01, 0x06) {
        return DeviceKind::Keyboard;
    }
    DeviceKind::Generic
}

/// Kind of a Bluetooth device. Classic: from the Class of Device (major 0x04 audio, 0x05 peripheral
/// with a minor type). BLE has no such property, so the advertised name decides.
pub fn bluetooth_kind(class_of_device: Option<u32>, name: &str) -> DeviceKind {
    if let Some(cod) = class_of_device.filter(|c| *c != 0) {
        let major = (cod >> 8) & 0x1F;
        let minor = (cod >> 2) & 0x3F;
        if major == 0x04 {
            return DeviceKind::Headset;
        }
        if major == 0x05 {
            if matches!(minor & 0x0F, 0x01 | 0x02) {
                return DeviceKind::Gamepad;
            }
            if minor & 0x20 != 0 {
                return DeviceKind::Mouse;
            }
            if minor & 0x10 != 0 {
                return DeviceKind::Keyboard;
            }
        }
    }
    let n = name.to_lowercase();
    let has = |words: &[&str]| words.iter().any(|w| n.contains(w));
    if has(&["mouse"]) {
        DeviceKind::Mouse
    } else if has(&["keyboard", "teclado"]) {
        DeviceKind::Keyboard
    } else if has(&["controller", "gamepad", "8bitdo", "dualsense"]) {
        DeviceKind::Gamepad
    } else if has(&["buds", "headset", "headphone", "earbud", "fone"]) {
        DeviceKind::Headset
    } else {
        DeviceKind::Generic
    }
}

/// Devices that only talk over the cable; every other known HID comes through a dongle.
pub fn origin(vid: u16, pid: u16) -> &'static str {
    match id(vid, pid).as_str() {
        "054C:0CE6" | "1D57:FA61" => "Cable",
        _ => "Dongle",
    }
}

/// Rough percent used only for icon colour and ordering. Never shown.
pub fn approx_percent_for_level(level: Option<&str>) -> i32 {
    match level {
        Some("EMPTY") => 5,
        Some("LOW") => 20,
        Some("MEDIUM") => 60,
        Some("FULL") => 95,
        _ => 50,
    }
}

pub fn level_label(level: &str) -> String {
    match level {
        "EMPTY" => t("Vazia", "Empty"),
        "LOW" => t("Baixa", "Low"),
        "MEDIUM" => t("Média", "Medium"),
        "FULL" => t("Cheia", "Full"),
        other => other.to_string(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn id_is_uppercase_hex_padded() {
        assert_eq!(id(0x46D, 0xA5B), "046D:0A5B");
    }
    #[test]
    fn kinds_from_usages_follow_the_csharp_priority() {
        assert_eq!(
            kind_from_usages(&[(1, 2), (1, 6)]),
            DeviceKind::Mouse,
            "mouse beats the macro keyboard"
        );
        assert_eq!(kind_from_usages(&[(1, 6)]), DeviceKind::Keyboard);
        assert_eq!(kind_from_usages(&[(1, 5), (1, 2)]), DeviceKind::Gamepad);
        assert_eq!(kind_from_usages(&[(0x0B, 1), (1, 2)]), DeviceKind::Headset);
        assert_eq!(kind_from_usages(&[(0xFF43, 0x0202)]), DeviceKind::Generic);
    }
    #[test]
    fn dead_end_and_origin() {
        assert!(dead_end_reason(0x291D, 0x385D).is_some());
        assert!(dead_end_reason(0x046D, 0x0A5B).is_none());
        assert_eq!(origin(0x054C, 0x0CE6), "Cable");
        assert_eq!(origin(0x24AE, 0x1871), "Dongle");
        assert_eq!(name(0x24AE, 0x1871).as_deref(), Some("Rapoo MT760 Mini"));
    }
    #[test]
    fn bluetooth_kind_from_class_then_name() {
        assert_eq!(bluetooth_kind(Some(0x240404), "x"), DeviceKind::Headset);
        assert_eq!(bluetooth_kind(Some(0x002580), "x"), DeviceKind::Mouse);
        assert_eq!(bluetooth_kind(Some(0x002540), "x"), DeviceKind::Keyboard);
        assert_eq!(bluetooth_kind(None, "MX Master Mouse"), DeviceKind::Mouse);
        assert_eq!(
            bluetooth_kind(None, "Logitech G703 LS"),
            DeviceKind::Generic
        );
        assert_eq!(bluetooth_kind(Some(0), "Galaxy Buds"), DeviceKind::Headset);
    }
    #[test]
    fn level_approximation() {
        assert_eq!(approx_percent_for_level(Some("LOW")), 20);
        assert_eq!(approx_percent_for_level(None), 50);
    }
}
