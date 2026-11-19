//! Logitech headsets over HID++ directly (G533, G535, G633/G635, G933/G935, G733, G PRO, G PRO X).
//! Format documented by HeadsetControl (lib/devices/logitech_*.hpp); the code is written independently.
//! Reply: [4..5] millivolts big-endian, [6] == 3 means charging, [2] == 0xFF/0x8F means the headset is off.

use joule_core::{BatteryReading, DeviceEntry, DeviceKind, curve::interpolate, l::t};
use joule_hid::{
    HidCollection,
    gate::{Gate, can_ask},
    request::HidRequest,
};
use std::{sync::Arc, time::Duration};

const VID: u16 = 0x046D;
const ASK_TIMEOUT: Duration = Duration::from_millis(1500);

pub const CURVE_G533: [(i32, i32); 6] = [
    (4200, 100),
    (3850, 50),
    (3790, 30),
    (3750, 20),
    (3680, 5),
    (3330, 0),
];
pub const CURVE_G535: [(i32, i32); 6] = [
    (4175, 100),
    (3817, 50),
    (3766, 30),
    (3730, 20),
    (3664, 5),
    (3310, 0),
];
pub const CURVE_G633: [(i32, i32); 8] = [
    (4100, 100),
    (3950, 80),
    (3850, 60),
    (3750, 40),
    (3650, 20),
    (3500, 10),
    (3300, 5),
    (3150, 0),
];
pub const CURVE_GPRO: [(i32, i32); 6] = [
    (4150, 100),
    (3830, 50),
    (3780, 30),
    (3740, 20),
    (3670, 5),
    (3320, 0),
];

pub struct HeadsetSpec {
    pub name: &'static str,
    pub index: u8,
    pub fn_sw: u8,
    pub page: u16,
    pub usage: u16,
    pub curve: &'static [(i32, i32)],
}

const fn spec(
    name: &'static str,
    index: u8,
    fn_sw: u8,
    page: u16,
    usage: u16,
    curve: &'static [(i32, i32)],
) -> HeadsetSpec {
    HeadsetSpec {
        name,
        index,
        fn_sw,
        page,
        usage,
        curve,
    }
}

static TABLE: &[(u16, HeadsetSpec)] = &[
    (
        0x0A66,
        spec("Logitech G533", 0x07, 0x01, 0xFF43, 0x0202, &CURVE_G533),
    ),
    (
        0x0AC4,
        spec("Logitech G535", 0x05, 0x0D, 0x000C, 0x0001, &CURVE_G535),
    ),
    (
        0x0A5C,
        spec("Logitech G633", 0x08, 0x0A, 0xFF43, 0x0202, &CURVE_G633),
    ),
    (
        0x0A89,
        spec("Logitech G635", 0x08, 0x0A, 0xFF43, 0x0202, &CURVE_G633),
    ),
    (
        0x0A5B,
        spec("Logitech G933", 0x08, 0x0A, 0xFF43, 0x0202, &CURVE_G633),
    ),
    (
        0x0A87,
        spec("Logitech G935", 0x08, 0x0A, 0xFF43, 0x0202, &CURVE_G633),
    ),
    (
        0x0AB5,
        spec("Logitech G733", 0x08, 0x0A, 0xFF43, 0x0202, &CURVE_G633),
    ),
    (
        0x0AFE,
        spec("Logitech G733", 0x08, 0x0A, 0xFF43, 0x0202, &CURVE_G633),
    ),
    (
        0x0B1F,
        spec("Logitech G733", 0x08, 0x0A, 0xFF43, 0x0202, &CURVE_G633),
    ),
    (
        0x0AA7,
        spec("Logitech G PRO", 0x06, 0x0D, 0xFF43, 0x0202, &CURVE_GPRO),
    ),
    (
        0x0AAA,
        spec("Logitech G PRO X", 0x06, 0x0D, 0xFF43, 0x0202, &CURVE_GPRO),
    ),
    (
        0x0ABA,
        spec("Logitech G PRO X", 0x06, 0x0D, 0xFF43, 0x0202, &CURVE_GPRO),
    ),
    (
        0x0AFB,
        spec(
            "Logitech G PRO X 2",
            0x06,
            0x0D,
            0xFF43,
            0x0202,
            &CURVE_GPRO,
        ),
    ),
    (
        0x0AFC,
        spec(
            "Logitech G PRO X 2",
            0x06,
            0x0D,
            0xFF43,
            0x0202,
            &CURVE_GPRO,
        ),
    ),
];

/// Every headset product id in the table.
pub fn known_pids() -> Vec<u16> {
    TABLE.iter().map(|(p, _)| *p).collect()
}

pub fn spec_for(pid: u16) -> Option<&'static HeadsetSpec> {
    TABLE.iter().find(|(p, _)| *p == pid).map(|(_, s)| s)
}

pub fn handles(pid: u16) -> bool {
    spec_for(pid).is_some()
}

/// `None` = headset off (the dongle answers FF/8F in place of the feature index) or an implausible voltage.
pub fn parse_headset(r: &[u8], curve: &[(i32, i32)]) -> Option<BatteryReading> {
    if r.len() < 7 || matches!(r[2], 0xFF | 0x8F) {
        return None;
    }
    let mv = ((r[4] as i32) << 8) | r[5] as i32;
    if mv < curve[curve.len() - 1].0 || mv > 4500 {
        return None;
    }
    let charge = (r[6] == 0x03).then(|| t("carregando", "charging"));
    Some(BatteryReading::percentage(
        interpolate(mv, curve),
        Some(t("estimado pela tensão", "estimated from voltage")),
        charge,
    ))
}

/// True when `r` answers our question: the battery reply itself, or the dongle's error echo of it
/// (`ff`/`8f`, then our feature index and function). Foreign reports on the same node are ignored.
pub fn is_answer(r: &[u8], spec: &HeadsetSpec) -> bool {
    r.len() >= 7
        && matches!(r[0], 0x10 | 0x11)
        && r[1] == 0xFF
        && match r[2] {
            0xFF | 0x8F => r[3] == spec.index && r[4] == spec.fn_sw,
            i => i == spec.index && r[3] == spec.fn_sw,
        }
}

pub struct LogitechHeadsetProvider;

impl crate::registry::StandaloneProvider for LogitechHeadsetProvider {
    fn name(&self) -> &str {
        "Logitech"
    }
    fn handles(&self, vid: u16, pid: u16) -> bool {
        vid == VID && handles(pid)
    }
    fn discover(&self, gate: &Arc<Gate>, collections: &[HidCollection]) -> Vec<DeviceEntry> {
        read_headsets(gate, collections)
    }
}

pub fn read_headsets(gate: &Arc<Gate>, collections: &[HidCollection]) -> Vec<DeviceEntry> {
    let mut pids: Vec<u16> = collections
        .iter()
        .filter(|c| c.vid == VID && handles(c.pid))
        .map(|c| c.pid)
        .collect();
    pids.sort_unstable();
    pids.dedup();

    pids.into_iter()
        .map(|pid| {
            let spec = spec_for(pid).unwrap();
            let target = collections.iter().find(|c| {
                c.vid == VID
                    && c.pid == pid
                    && can_ask(c)
                    && c.usage_page == spec.page
                    && c.usage == spec.usage
                    && c.output_len >= 20
                    && c.input_len > 0
            });
            let reading = match target {
                None => BatteryReading::no_data(t(
                    "não achei o canal de bateria deste fone",
                    "couldn't find this headset's battery channel",
                )),
                Some(target) => {
                    match HidRequest::open(gate.clone(), std::slice::from_ref(target)) {
                        None => BatteryReading::no_data(t(
                            "não abriu o canal do fone (regra udev?)",
                            "couldn't open the headset channel (udev rule?)",
                        )),
                        Some(mut session) => {
                            let request = [0x11, 0xFF, spec.index, spec.fn_sw];
                            let reply =
                                session.ask(target, &request, |r| is_answer(r, spec), ASK_TIMEOUT);
                            match reply {
                            None => no_answer(gate, target),
                            Some(r) => parse_headset(&r, spec.curve).unwrap_or_else(|| {
                                BatteryReading::no_data(t(
                                    "fone desligado ou longe do receptor; ligue e atualize",
                                    "headset off or far from the receiver; turn it on and refresh",
                                ))
                            }),
                        }
                        }
                    }
                }
            };
            DeviceEntry {
                key: format!("logitech:{pid:04X}"),
                display_name: spec.name.to_string(),
                hardware_id: format!("{VID:04X}:{pid:04X}"),
                provider_name: "Logitech".into(),
                reading,
                kind: DeviceKind::Headset,
                origin: "Dongle".into(),
                is_live: false,
                is_connected: true,
                cache: None,
            }
        })
        .collect()
}

fn no_answer(gate: &Arc<Gate>, target: &HidCollection) -> BatteryReading {
    match gate.silent_reason(target.vid) {
        Some(why) => BatteryReading::no_data(why),
        None => BatteryReading::no_data(t(
            "não respondeu; ligue o fone e atualize",
            "didn't answer; turn the headset on and refresh",
        )),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn reply(index: u8, mv: u16, status: u8) -> Vec<u8> {
        let mut r = vec![0x11, 0xFF, index, 0x0A, (mv >> 8) as u8, mv as u8, status];
        r.resize(20, 0);
        r
    }
    const CURVE: &[(i32, i32)] = &CURVE_G633;

    #[test]
    fn real_g933_log_line_discharging() {
        // ~/.cache/battery-indicator.log: raw=11ff080a0da0010000000000 -> 3488 mV, status 1
        let raw = [0x11, 0xFF, 0x08, 0x0A, 0x0D, 0xA0, 0x01, 0, 0, 0, 0, 0];
        let r = parse_headset(&raw, CURVE).unwrap();
        assert_eq!(r.percent, Some(10));
        assert_eq!(r.charge_text, None);
    }
    #[test]
    fn charging_adds_text() {
        let r = parse_headset(&reply(0x08, 4150, 3), CURVE).unwrap();
        assert_eq!(r.percent, Some(100));
        assert!(r.charge_text.is_some());
    }
    #[test]
    fn headset_off_is_none() {
        assert!(parse_headset(&reply(0xFF, 3800, 1), CURVE).is_none());
        assert!(parse_headset(&reply(0x8F, 3800, 1), CURVE).is_none());
    }
    #[test]
    fn out_of_range_voltage_is_none() {
        assert!(parse_headset(&reply(0x08, 3100, 1), CURVE).is_none()); // below the 3150 floor
        assert!(parse_headset(&reply(0x08, 4600, 1), CURVE).is_none());
    }
    #[test]
    fn short_reply_is_none() {
        assert!(parse_headset(&[0x11, 0xFF, 0x08, 0x0A, 0x0D], CURVE).is_none());
    }
    #[test]
    fn answer_filter_accepts_reply_and_own_error_only() {
        let s = spec_for(0x0A5B).unwrap();
        assert!(is_answer(&reply(0x08, 3488, 1), s));
        // dongle error echo for our question, captured on the real G933 with the headset off
        assert!(is_answer(
            &[
                0x11, 0xFF, 0xFF, 0x08, 0x0A, 0x05, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
            ],
            s
        ));
        // foreign error flood seen on the same node (OpenRGB traffic): not our answer
        assert!(!is_answer(
            &[
                0x11, 0xFF, 0xFF, 0x04, 0x3A, 0x05, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
            ],
            s
        ));
        assert!(!is_answer(&reply(0x04, 3488, 1), s));
        assert!(!is_answer(&[0x11, 0xFF], s));
    }
    #[test]
    fn g933_is_registered() {
        let s = spec_for(0x0A5B).unwrap();
        assert_eq!(
            (s.name, s.index, s.fn_sw, s.page, s.usage),
            ("Logitech G933", 0x08, 0x0A, 0xFF43, 0x0202)
        );
        assert!(spec_for(0x0001).is_none());
        assert!(handles(0x0A5B));
    }
}
