//! Logitech headsets over HID++ directly (G533, G535, G633/G635, G933/G935, G733, G PRO, G PRO X).
//! Format documented by HeadsetControl (lib/devices/logitech_*.hpp); the code is written independently.
//! Reply: [4..5] millivolts big-endian, [6] == 3 means charging, [2] == 0xFF/0x8F means the headset is off.

use joule_core::{BatteryReading, ChargeState, DeviceEntry, DeviceKind, curve::interpolate, l::t};
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
/// G933/G935 charging curve, measured by g933-utils (MIT, ash lea; `libg933/src/maps/0A5B/
/// charging_ascending.csv`, reduced to 24 points). Used only when nothing is known from before a charge.
pub const CHARGE_CURVE_G933: [(i32, i32); 24] = [
    (4292, 92),
    (4275, 90),
    (4250, 88),
    (4225, 85),
    (4200, 82),
    (4175, 79),
    (4150, 75),
    (4125, 71),
    (4100, 67),
    (4075, 62),
    (4050, 57),
    (4025, 52),
    (4000, 45),
    (3985, 39),
    (3975, 33),
    (3965, 26),
    (3950, 21),
    (3925, 16),
    (3900, 12),
    (3880, 8),
    (3870, 6),
    (3850, 5),
    (3750, 2),
    (3600, 0),
];

/// The measured charging curve of a model, when there is one.
pub fn charge_curve_for(pid: u16) -> Option<&'static [(i32, i32)]> {
    matches!(pid, 0x0A5B | 0x0A87).then_some(&CHARGE_CURVE_G933[..])
}

/// Battery status byte [6]: 1 discharging, 3 charging, 7 full (g933-utils, `BatteryStatus::from_bytes`).
pub fn parse_headset(
    r: &[u8],
    curve: &[(i32, i32)],
    charge_curve: Option<&[(i32, i32)]>,
) -> Option<BatteryReading> {
    if r.len() < 7 || matches!(r[2], 0xFF | 0x8F) {
        return None;
    }
    let mv = ((r[4] as i32) << 8) | r[5] as i32;
    // The charger lifts the voltage up to about 4.5 V: allow it while charging.
    let top = if r[6] == 0x03 { 4700 } else { 4500 };
    if mv < curve[curve.len() - 1].0 || mv > top {
        return None;
    }
    let mut reading = match r[6] {
        0x07 => {
            let mut full =
                BatteryReading::percentage(100, None, Some(t("carga completa", "fully charged")));
            full.charge_state = Some(ChargeState::Full);
            full
        }
        0x03 => {
            let mut c = BatteryReading::percentage(
                interpolate(mv, curve),
                Some(t("estimado durante a carga", "estimated while charging")),
                Some(t("carregando", "charging")),
            );
            c.charge_state = Some(ChargeState::Charging);
            c.voltage_based = true;
            c.charge_curve_percent = charge_curve.map(|cc| interpolate(mv, cc).clamp(0, 100) as u8);
            c
        }
        _ => BatteryReading::percentage(
            interpolate(mv, curve),
            Some(t("estimado pela tensão", "estimated from voltage")),
            None,
        ),
    };
    reading.observed_unix = None;
    Some(reading)
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
                            Some(r) => parse_headset(&r, spec.curve, charge_curve_for(pid)).unwrap_or_else(|| {
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
        let r = parse_headset(&raw, CURVE, None).unwrap();
        assert_eq!(r.percent, Some(10));
        assert_eq!(r.charge_text, None);
    }
    #[test]
    fn charging_adds_text() {
        let r = parse_headset(&reply(0x08, 4150, 3), CURVE, None).unwrap();
        assert_eq!(r.percent, Some(100));
        assert!(r.charge_text.is_some());
    }
    #[test]
    fn headset_off_is_none() {
        assert!(parse_headset(&reply(0xFF, 3800, 1), CURVE, None).is_none());
        assert!(parse_headset(&reply(0x8F, 3800, 1), CURVE, None).is_none());
    }
    #[test]
    fn out_of_range_voltage_is_none() {
        assert!(parse_headset(&reply(0x08, 3100, 1), CURVE, None).is_none()); // below the 3150 floor
        assert!(parse_headset(&reply(0x08, 4600, 1), CURVE, None).is_none());
    }
    #[test]
    fn short_reply_is_none() {
        assert!(parse_headset(&[0x11, 0xFF, 0x08, 0x0A, 0x0D], CURVE, None).is_none());
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

    #[test]
    fn charging_is_flagged_for_an_estimate_with_the_measured_curve() {
        let r = parse_headset(&reply(0x08, 4100, 3), CURVE, charge_curve_for(0x0A5B)).unwrap();
        assert_eq!(r.charge_state, Some(ChargeState::Charging));
        assert!(r.voltage_based);
        assert_eq!(
            r.charge_curve_percent,
            Some(67),
            "g933-utils: 4100 mV while charging is about 67%"
        );
    }
    #[test]
    fn the_charger_voltage_above_4_5_v_is_accepted_while_charging() {
        // real G933 log: 4497 mV, state 0x03
        let r = parse_headset(&reply(0x08, 4497, 3), CURVE, charge_curve_for(0x0A5B)).unwrap();
        assert_eq!(r.charge_curve_percent, Some(92));
    }
    #[test]
    fn status_7_is_full() {
        // real G933 log: mV=3941 state=0x07
        let r = parse_headset(&reply(0x08, 3941, 7), CURVE, None).unwrap();
        assert_eq!(r.percent, Some(100));
        assert_eq!(r.charge_state, Some(ChargeState::Full));
    }
    #[test]
    fn only_the_g933_family_has_a_charging_curve() {
        assert!(charge_curve_for(0x0A5B).is_some());
        assert!(charge_curve_for(0x0A87).is_some());
        assert!(charge_curve_for(0x0AB5).is_none());
    }
}
