//! Logitech devices behind Unifying, Bolt, Lightspeed and Nano receivers, over HID++ 2.0
//! (ported from the receiver half of `LogitechProvider.cs`).
//!
//! The format is the one Logitech documents and Solaar uses; no code is copied.
//!   short question = 10 <slot> <feature index> <function<<4 | swid> p0 p1 p2
//!   reply          = 10|11 <slot> <index> <function|swid> data...
//!   2.0 error      = .. <slot> FF <index> <function|swid> <code>
//!   1.0 error      = 10 <slot> 8F <index> <function|swid> <code> (comes from the receiver)
//! Feature 0x0000 (root, index 0) translates a feature number into its index on that device.
//! Battery is 0x1004 (unified), 0x1000 (status) or 0x1001 (voltage), in that order of preference.
//!
//! Questions per reading: one per slot 1..=6. The first time a device answers in this run, a few more
//! find its battery index and name; after that each reading is one question per slot. An empty slot or
//! a sleeping device makes the receiver answer with an error at once, without bothering the device.

use crate::registry::StandaloneProvider;
use joule_core::{BatteryReading, ChargeState, DeviceEntry, curve::interpolate, known, l::t, log};
use joule_hid::{
    HidCollection,
    gate::{Gate, is_vendor},
    request::HidRequest,
};
use std::{collections::HashMap, sync::Arc, sync::Mutex, time::Duration};

const VID: u16 = 0x046D;
const SOFTWARE_ID: u8 = 0x0D;
const SLOTS: u8 = 6;

const FEATURE_NAME: u16 = 0x0005;
const FEATURE_UNIFIED: u16 = 0x1004;
const FEATURE_STATUS: u16 = 0x1000;
const FEATURE_VOLTAGE: u16 = 0x1001;

// HID++ 1.0 error codes the receiver returns for a slot.
const ERR_INVALID_SUB_ID: u8 = 0x01; // HID++ 1.0 device
const ERR_CONNECT_FAILED: u8 = 0x04;
const ERR_UNKNOWN_DEVICE: u8 = 0x08; // empty slot
const ERR_RESOURCE_ERROR: u8 = 0x09; // paired but asleep or off

const ASK_TIMEOUT: Duration = Duration::from_millis(1500);

/// Known receivers (Solaar's list, base_usb.py).
pub const RECEIVERS: &[(u16, &str)] = &[
    (0xC548, "Bolt"),
    (0xC52B, "Unifying"),
    (0xC532, "Unifying"),
    (0xC539, "Lightspeed"),
    (0xC53A, "Lightspeed"),
    (0xC53D, "Lightspeed"),
    (0xC53F, "Lightspeed"),
    (0xC541, "Lightspeed"),
    (0xC545, "Lightspeed"),
    (0xC547, "Lightspeed"),
    (0xC54D, "Lightspeed"),
    (0xC52F, "Nano"),
    (0xC518, "Nano"),
    (0xC51A, "Nano"),
    (0xC51B, "Nano"),
    (0xC521, "Nano"),
    (0xC525, "Nano"),
    (0xC526, "Nano"),
    (0xC52E, "Nano"),
    (0xC531, "Nano"),
    (0xC534, "Nano"),
    (0xC535, "Nano"),
    (0xC537, "Nano"),
];

pub fn receiver_name(pid: u16) -> Option<&'static str> {
    RECEIVERS.iter().find(|(p, _)| *p == pid).map(|(_, n)| *n)
}

/// Average Li-ion cell curve. If testers see a skewed value, this is the table to calibrate.
pub const CURVE: [(i32, i32); 12] = [
    (4150, 100),
    (4050, 90),
    (3970, 80),
    (3900, 70),
    (3840, 60),
    (3800, 50),
    (3770, 40),
    (3740, 30),
    (3710, 20),
    (3670, 10),
    (3620, 5),
    (3500, 0),
];

pub fn percent_from_millivolts(mv: i32) -> i32 {
    interpolate(mv, &CURVE)
}

/// What is already known about a device in this run, so later readings ask only the battery.
#[derive(Clone)]
struct DeviceInfo {
    battery_index: u8,
    battery_feature: u16,
    name: String,
}

/// The one thing the slot logic needs from the write door. Tests script it.
pub trait Asker {
    fn ask(
        &mut self,
        target: &HidCollection,
        request: &[u8],
        is_reply: &dyn Fn(&[u8]) -> bool,
        timeout: Duration,
    ) -> Option<Vec<u8>>;
}

impl Asker for HidRequest {
    fn ask(
        &mut self,
        target: &HidCollection,
        request: &[u8],
        is_reply: &dyn Fn(&[u8]) -> bool,
        timeout: Duration,
    ) -> Option<Vec<u8>> {
        HidRequest::ask(self, target, request, |r| is_reply(r), timeout)
    }
}

#[derive(Debug, Default, PartialEq, Eq)]
pub struct CallResult {
    pub data: Option<Vec<u8>>,
    pub error10: Option<u8>,
    pub error20: Option<u8>,
    pub timeout: bool,
}

pub fn build_request(slot: u8, index: u8, function: u8, args: &[u8]) -> [u8; 7] {
    let mut r = [0u8; 7];
    r[0] = 0x10;
    r[1] = slot;
    r[2] = index;
    r[3] = (function << 4) | SOFTWARE_ID;
    for (i, b) in args.iter().take(3).enumerate() {
        r[4 + i] = *b;
    }
    r
}

/// Is this reply to this question? Data from byte 4 on, or the error code.
/// `None` = a report about something else (notification, another question).
pub fn match_reply(request: &[u8], r: &[u8]) -> Option<CallResult> {
    if r.len() < 7 || (r[0] != 0x10 && r[0] != 0x11) || r[1] != request[1] {
        return None;
    }
    if r[2] == request[2] && r[3] == request[3] {
        return Some(CallResult {
            data: Some(r[4..].to_vec()),
            ..Default::default()
        });
    }
    if (r[2] == 0x8F || r[2] == 0xFF) && r[3] == request[2] && r[4] == request[3] {
        return Some(if r[2] == 0x8F {
            CallResult {
                error10: Some(r[5]),
                ..Default::default()
            }
        } else {
            CallResult {
                error20: Some(r[5]),
                ..Default::default()
            }
        });
    }
    None
}

fn feature_bytes(id: u16) -> [u8; 2] {
    id.to_be_bytes()
}

fn call(
    asker: &mut dyn Asker,
    receiver: &HidCollection,
    slot: u8,
    index: u8,
    function: u8,
    args: &[u8],
) -> CallResult {
    let request = build_request(slot, index, function, args);
    match asker.ask(
        receiver,
        &request,
        &|r| match_reply(&request, r).is_some(),
        ASK_TIMEOUT,
    ) {
        None => CallResult {
            timeout: true,
            ..Default::default()
        },
        Some(reply) => match_reply(&request, &reply).unwrap_or_default(),
    }
}

/// 0x1004, function 1: [0] percent, [1] band (8 full, 4 good, 2 low, 1 critical), [2] charge state.
pub fn parse_unified(d: &[u8]) -> BatteryReading {
    if d.len() < 3 {
        return BatteryReading::no_data(t("resposta curta", "short reply"));
    }
    let charge = match d[2] {
        1 | 2 => Some(t("carregando", "charging")),
        3 => Some(t("carga completa", "fully charged")),
        _ => None,
    };
    if (1..=100).contains(&d[0]) {
        let mut r = BatteryReading::percentage(i32::from(d[0]), None, charge);
        r.charge_state = match d[2] {
            1 | 2 => Some(ChargeState::Charging),
            3 => Some(ChargeState::Full),
            _ => None,
        };
        return r;
    }
    match d[1] & 0x0F {
        8.. => BatteryReading::coarse("FULL"),
        4..=7 => BatteryReading::coarse("MEDIUM"),
        2..=3 => BatteryReading::coarse("LOW"),
        1 => BatteryReading::coarse("EMPTY"),
        _ => BatteryReading::no_data(t(
            "o aparelho nao informou o nivel",
            "the device didn’t report its level",
        )),
    }
}

/// 0x1000, function 0: [0] percent (0 = unknown), [2] state.
pub fn parse_status(d: &[u8]) -> BatteryReading {
    if d.len() < 3 {
        return BatteryReading::no_data(t("resposta curta", "short reply"));
    }
    if d[0] == 0 || d[0] > 100 {
        return BatteryReading::no_data(t(
            "o aparelho nao informou o nivel",
            "the device didn’t report its level",
        ));
    }
    let charge = match d[2] {
        1 | 2 | 4 => Some(t("carregando", "charging")),
        3 => Some(t("carga completa", "fully charged")),
        _ => None,
    };
    BatteryReading::percentage(i32::from(d[0]), None, charge)
}

/// 0x1001, function 0: [0..1] millivolts (big-endian), [2] flags (bit 7 = charging).
pub fn parse_voltage(d: &[u8]) -> BatteryReading {
    if d.len() < 3 {
        return BatteryReading::no_data(t("resposta curta", "short reply"));
    }
    let mv = (i32::from(d[0]) << 8) | i32::from(d[1]);
    if !(3000..=4500).contains(&mv) {
        return BatteryReading::no_data(t(
            &format!("tensao fora da faixa ({mv} mV)"),
            &format!("voltage out of range ({mv} mV)"),
        ));
    }
    // flags: bit 7 charging, low two bits 1 = full (Solaar, decipher_battery_voltage)
    if d[2] & 0x80 != 0 && d[2] & 0x03 == 1 {
        let mut full =
            BatteryReading::percentage(100, None, Some(t("carga completa", "fully charged")));
        full.charge_state = Some(ChargeState::Full);
        return full;
    }
    if d[2] & 0x80 != 0 {
        // The voltage under charge is not the charge: the reading service estimates it.
        let mut c = BatteryReading::percentage(
            percent_from_millivolts(mv),
            Some(t("estimado durante a carga", "estimated while charging")),
            Some(t("carregando", "charging")),
        );
        c.charge_state = Some(ChargeState::Charging);
        c.voltage_based = true;
        return c;
    }
    BatteryReading::percentage(
        percent_from_millivolts(mv),
        Some(t("estimado pela tensao", "estimated from voltage")),
        None,
    )
}

pub fn clean_name(raw: &str) -> Option<String> {
    let name = raw.trim();
    if name.is_empty() {
        None
    } else if name.to_lowercase().starts_with("logitech") {
        Some(name.to_string())
    } else {
        Some(format!("Logitech {name}"))
    }
}

struct SlotOutcome {
    entry: Option<DeviceEntry>,
    stop_receiver: bool,
}

fn skip() -> SlotOutcome {
    SlotOutcome {
        entry: None,
        stop_receiver: false,
    }
}

fn entry(key: &str, receiver: &HidCollection, name: &str, reading: BatteryReading) -> DeviceEntry {
    DeviceEntry {
        key: key.to_string(),
        display_name: name.to_string(),
        hardware_id: receiver.hardware_id(),
        provider_name: "Logitech".into(),
        reading,
        kind: known::bluetooth_kind(None, name),
        origin: "Dongle".into(),
        is_live: false,
        is_connected: true,
        cache: None,
    }
}

#[derive(Default)]
pub struct LogitechReceiverProvider {
    known: Mutex<HashMap<String, DeviceInfo>>,
}

impl LogitechReceiverProvider {
    fn read_slot(
        &self,
        asker: &mut dyn Asker,
        receiver: &HidCollection,
        slot: u8,
        key: &str,
    ) -> SlotOutcome {
        let known = self.known.lock().unwrap().get(key).cloned();
        let info = match known {
            Some(i) => i,
            None => {
                let first = call(
                    asker,
                    receiver,
                    slot,
                    0x00,
                    0,
                    &feature_bytes(FEATURE_UNIFIED),
                );
                if first.timeout {
                    return SlotOutcome {
                        entry: None,
                        stop_receiver: true,
                    };
                }
                if matches!(
                    first.error10,
                    Some(ERR_UNKNOWN_DEVICE | ERR_RESOURCE_ERROR | ERR_CONNECT_FAILED)
                ) {
                    return skip();
                }
                if first.error10 == Some(ERR_INVALID_SUB_ID) {
                    let name = format!("Logitech (slot {slot})");
                    let reading = BatteryReading::no_data(t(
                        "aparelho Logitech antigo (HID++ 1.0), ainda sem suporte",
                        "older Logitech device (HID++ 1.0), not supported yet",
                    ));
                    return SlotOutcome {
                        entry: Some(entry(key, receiver, &name, reading)),
                        stop_receiver: false,
                    };
                }
                let Some(data) = first.data else {
                    return skip();
                };
                let mut index = data.first().copied().unwrap_or(0);
                let mut feature = FEATURE_UNIFIED;
                for next in [FEATURE_STATUS, FEATURE_VOLTAGE] {
                    if index != 0 {
                        break;
                    }
                    let r = call(asker, receiver, slot, 0x00, 0, &feature_bytes(next));
                    let Some(d) = r.data else { return skip() };
                    index = d.first().copied().unwrap_or(0);
                    feature = next;
                }
                let name = read_name(asker, receiver, slot)
                    .unwrap_or_else(|| format!("Logitech (slot {slot})"));
                if index == 0 {
                    let reading = BatteryReading::no_data(t(
                        "o aparelho nao informa bateria por HID++",
                        "the device doesn’t report battery over HID++",
                    ));
                    return SlotOutcome {
                        entry: Some(entry(key, receiver, &name, reading)),
                        stop_receiver: false,
                    };
                }
                let info = DeviceInfo {
                    battery_index: index,
                    battery_feature: feature,
                    name,
                };
                self.known
                    .lock()
                    .unwrap()
                    .insert(key.to_string(), info.clone());
                info
            }
        };

        let function = if info.battery_feature == FEATURE_UNIFIED {
            1
        } else {
            0
        };
        let reply = call(asker, receiver, slot, info.battery_index, function, &[]);
        if reply.timeout {
            return SlotOutcome {
                entry: None,
                stop_receiver: true,
            };
        }
        if reply.error10.is_some() {
            return skip(); // fell asleep between discovery and now
        }
        let Some(data) = reply.data else {
            // 2.0 error: the index changed (re-paired with another device). Rediscover next time.
            self.known.lock().unwrap().remove(key);
            return skip();
        };
        let reading = match info.battery_feature {
            FEATURE_UNIFIED => parse_unified(&data),
            FEATURE_STATUS => parse_status(&data),
            _ => parse_voltage(&data),
        };
        SlotOutcome {
            entry: Some(entry(key, receiver, &info.name, reading)),
            stop_receiver: false,
        }
    }

    /// One receiver: slot 1..=6. `None` slots are skipped. A timeout stops the receiver.
    fn read_receiver(
        &self,
        asker: &mut dyn Asker,
        receiver: &HidCollection,
        receiver_key: &str,
    ) -> Vec<DeviceEntry> {
        let mut found = Vec::new();
        for slot in 1..=SLOTS {
            let outcome = self.read_slot(asker, receiver, slot, &format!("{receiver_key}:{slot}"));
            if outcome.stop_receiver {
                break;
            }
            found.extend(outcome.entry);
        }
        found
    }
}

fn read_name(asker: &mut dyn Asker, receiver: &HidCollection, slot: u8) -> Option<String> {
    let idx = call(asker, receiver, slot, 0x00, 0, &feature_bytes(FEATURE_NAME));
    let name_index = *idx.data?.first().filter(|b| **b != 0)?;
    let count = call(asker, receiver, slot, name_index, 0, &[]);
    let length = usize::from(*count.data?.first()?).min(64);

    let mut chars: Vec<u8> = Vec::new();
    while chars.len() < length {
        let part = call(asker, receiver, slot, name_index, 1, &[chars.len() as u8]);
        let Some(d) = part.data else { break };
        let piece: Vec<u8> = d
            .iter()
            .copied()
            .take_while(|b| *b != 0)
            .take(length - chars.len())
            .collect();
        if piece.is_empty() {
            break;
        }
        chars.extend(piece);
    }
    clean_name(&String::from_utf8_lossy(&chars))
}

impl StandaloneProvider for LogitechReceiverProvider {
    fn name(&self) -> &str {
        "Logitech"
    }

    fn handles(&self, vid: u16, pid: u16) -> bool {
        vid == VID && receiver_name(pid).is_some()
    }

    fn discover(&self, gate: &Arc<Gate>, collections: &[HidCollection]) -> Vec<DeviceEntry> {
        let mine: Vec<HidCollection> = collections
            .iter()
            .filter(|c| c.vid == VID && receiver_name(c.pid).is_some() && is_vendor(c))
            .cloned()
            .collect();
        // Each receiver has one short collection (0xFF00/0x0001, report 0x10). Long replies arrive on
        // collection 0x0002 of the same receiver: all are listened to, the questions go one at a time.
        let mut shorts: Vec<&HidCollection> = mine
            .iter()
            .filter(|c| c.usage_page == 0xFF00 && c.usage == 0x0001 && c.output_len >= 7)
            .collect();
        if shorts.is_empty() {
            return Vec::new();
        }
        shorts.sort_by(|a, b| a.pid.cmp(&b.pid).then_with(|| a.path.cmp(&b.path)));

        let Some(mut session) = HidRequest::open(gate.clone(), &mine) else {
            log::write("Logitech: couldn't open any HID++ collection.");
            return Vec::new();
        };

        let mut found = Vec::new();
        let mut ordinal_by_pid: HashMap<u16, usize> = HashMap::new();
        for receiver in shorts {
            let ordinal = ordinal_by_pid
                .entry(receiver.pid)
                .and_modify(|n| *n += 1)
                .or_insert(1);
            // The key is receiver + slot. Moving a receiver to another port keeps the key, but two
            // identical receivers swap order if they change ports.
            let receiver_key = format!(
                "logitech:{:04X}{}",
                receiver.pid,
                if *ordinal > 1 {
                    format!("#{ordinal}")
                } else {
                    String::new()
                }
            );
            let devices = self.read_receiver(&mut session, receiver, &receiver_key);
            if devices.is_empty() {
                let reading = BatteryReading::no_data(
                    gate.silent_reason(receiver.vid).unwrap_or_else(|| {
                        t(
                            "nenhum aparelho respondeu; mexa no aparelho e atualize",
                            "no device answered; move the device and refresh",
                        )
                    }),
                );
                let name = format!(
                    "{} {}",
                    t("Receptor Logitech", "Logitech receiver"),
                    receiver_name(receiver.pid).unwrap_or_default()
                );
                found.push(entry(&receiver_key, receiver, &name, reading));
            } else {
                found.extend(devices);
            }
        }
        log::write(&format!("Logitech: {} questions.", session.asks()));
        found
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use joule_core::ReadingStatus;
    use std::collections::VecDeque;

    fn receiver() -> HidCollection {
        HidCollection {
            path: "/dev/hidraw2".into(),
            vid: VID,
            pid: 0xC539,
            usage_page: 0xFF00,
            usage: 1,
            input_len: 7,
            output_len: 7,
            feature_len: 0,
            product: "Logitech USB Receiver".into(),
        }
    }

    /// Reports for one slot: `10 slot idx fn|swid d0 d1 d2`.
    fn reply(slot: u8, idx: u8, function: u8, data: [u8; 3]) -> Vec<u8> {
        vec![
            0x10,
            slot,
            idx,
            (function << 4) | SOFTWARE_ID,
            data[0],
            data[1],
            data[2],
        ]
    }
    fn err10(slot: u8, idx: u8, function: u8, code: u8) -> Vec<u8> {
        vec![
            0x10,
            slot,
            0x8F,
            idx,
            (function << 4) | SOFTWARE_ID,
            code,
            0,
        ]
    }
    fn err20(slot: u8, idx: u8, function: u8, code: u8) -> Vec<u8> {
        vec![
            0x11,
            slot,
            0xFF,
            idx,
            (function << 4) | SOFTWARE_ID,
            code,
            0,
        ]
    }

    /// Answers each question from a script; records the questions it was asked.
    struct Script {
        replies: VecDeque<Option<Vec<u8>>>,
        asked: Vec<Vec<u8>>,
    }
    impl Script {
        fn new(replies: Vec<Option<Vec<u8>>>) -> Self {
            Self {
                replies: replies.into(),
                asked: Vec::new(),
            }
        }
    }
    impl Asker for Script {
        fn ask(
            &mut self,
            _t: &HidCollection,
            request: &[u8],
            is_reply: &dyn Fn(&[u8]) -> bool,
            _timeout: Duration,
        ) -> Option<Vec<u8>> {
            self.asked.push(request.to_vec());
            let r = self
                .replies
                .pop_front()
                .expect("more questions than the script expected")?;
            assert!(
                is_reply(&r),
                "the script's reply must pass the provider's own filter: {r:02x?}"
            );
            Some(r)
        }
    }

    #[test]
    fn request_layout() {
        assert_eq!(
            build_request(1, 0, 0, &[0x10, 0x04]),
            [0x10, 0x01, 0x00, 0x0D, 0x10, 0x04, 0x00]
        );
        assert_eq!(
            build_request(3, 5, 1, &[]),
            [0x10, 0x03, 0x05, 0x1D, 0, 0, 0]
        );
        assert_eq!(
            build_request(1, 0, 0, &[1, 2, 3, 4])[4..],
            [1, 2, 3],
            "at most three parameters"
        );
    }
    #[test]
    fn replies_are_matched_to_their_question() {
        let q = build_request(1, 0x05, 1, &[]);
        assert_eq!(
            match_reply(&q, &reply(1, 5, 1, [80, 4, 1]))
                .unwrap()
                .data
                .unwrap()[..3],
            [80, 4, 1]
        );
        assert_eq!(
            match_reply(&q, &err10(1, 5, 1, 0x09)).unwrap().error10,
            Some(0x09)
        );
        assert_eq!(
            match_reply(&q, &err20(1, 5, 1, 0x05)).unwrap().error20,
            Some(0x05)
        );
        // another slot, another function, a notification and a short report are not ours
        assert!(match_reply(&q, &reply(2, 5, 1, [0; 3])).is_none());
        assert!(match_reply(&q, &reply(1, 5, 0, [0; 3])).is_none());
        assert!(match_reply(&q, &reply(1, 9, 1, [0; 3])).is_none());
        assert!(match_reply(&q, &[0x10, 1, 5]).is_none());
        assert!(
            match_reply(&q, &err10(1, 7, 1, 0x09)).is_none(),
            "an error about another index"
        );
    }
    #[test]
    fn unified_percent_and_charge() {
        let r = parse_unified(&[80, 4, 1]);
        assert_eq!(r.percent, Some(80));
        assert!(r.charge_text.is_some());
        assert_eq!(parse_unified(&[55, 4, 0]).charge_text, None);
        assert_eq!(parse_unified(&[100, 8, 3]).percent, Some(100));
    }
    #[test]
    fn unified_without_percent_gives_a_band_never_a_percent() {
        for (band, word) in [(8, "FULL"), (4, "MEDIUM"), (2, "LOW"), (1, "EMPTY")] {
            let r = parse_unified(&[0, band, 0]);
            assert_eq!(r.level.as_deref(), Some(word));
            assert_eq!(r.percent, None);
        }
        assert_eq!(parse_unified(&[0, 0, 0]).status, ReadingStatus::NoData);
        assert_eq!(parse_unified(&[1]).status, ReadingStatus::NoData);
    }
    #[test]
    fn status_feature() {
        assert_eq!(parse_status(&[42, 0, 0]).percent, Some(42));
        assert_eq!(parse_status(&[0, 0, 0]).status, ReadingStatus::NoData);
        assert!(parse_status(&[42, 0, 4]).charge_text.is_some());
    }
    #[test]
    fn voltage_feature() {
        let r = parse_voltage(&[0x0F, 0x00, 0x00]); // 3840 mV
        assert_eq!(r.percent, Some(60));
        assert_eq!(r.charge_text, None);
        assert!(parse_voltage(&[0x0F, 0x00, 0x80]).charge_text.is_some());
        assert_eq!(
            parse_voltage(&[0x0F, 0x00, 0x81]).percent,
            Some(100),
            "charging + full flag"
        );
        assert!(
            parse_voltage(&[0x0F, 0x00, 0x80]).voltage_based,
            "charging: the service estimates"
        );
        assert_eq!(
            parse_voltage(&[0x0B, 0xB7, 0]).status,
            ReadingStatus::NoData,
            "2999 mV"
        );
        assert_eq!(
            parse_voltage(&[0x11, 0x95, 0]).status,
            ReadingStatus::NoData,
            "4501 mV"
        );
    }
    #[test]
    fn curve_is_pinned() {
        assert_eq!(percent_from_millivolts(4200), 100);
        assert_eq!(percent_from_millivolts(3800), 50);
        assert_eq!(percent_from_millivolts(3400), 0);
        assert_eq!(percent_from_millivolts(3785), 45); // midway 3770 (40) and 3800 (50)
    }
    #[test]
    fn names_are_cleaned() {
        assert_eq!(
            clean_name(" G703 LIGHTSPEED ").as_deref(),
            Some("Logitech G703 LIGHTSPEED")
        );
        assert_eq!(
            clean_name("Logitech MX Master").as_deref(),
            Some("Logitech MX Master")
        );
        assert_eq!(clean_name("   "), None);
    }
    #[test]
    fn receiver_table_covers_the_lightspeed_on_this_machine() {
        let p = LogitechReceiverProvider::default();
        assert!(p.handles(0x046D, 0xC539));
        assert!(
            !p.handles(0x046D, 0x4086),
            "the mouse itself on a cable is not a receiver"
        );
        assert!(!p.handles(0x1234, 0xC539));
    }

    #[test]
    fn first_discovery_then_one_question_per_slot() {
        let p = LogitechReceiverProvider::default();
        let rx = receiver();
        // slot 1: root asks 0x1004 -> index 6, name feature -> index 3, count 4, chars, battery
        let mut s = Script::new(vec![
            Some(reply(1, 0, 0, [6, 0, 0])), // 0x1004 is at index 6
            Some(reply(1, 0, 0, [3, 0, 0])), // 0x0005 is at index 3
            Some(reply(1, 3, 0, [4, 0, 0])), // name length 4
            Some(reply(1, 3, 1, *b"MX3")),
            Some(reply(1, 3, 1, [b'!', 0, 0])),
            Some(reply(1, 6, 1, [73, 4, 0])), // battery 73%
        ]);
        let out = p.read_slot(&mut s, &rx, 1, "logitech:C539:1");
        let e = out.entry.unwrap();
        assert_eq!(e.display_name, "Logitech MX3!");
        assert_eq!(e.reading.percent, Some(73));
        assert_eq!(e.hardware_id, "046D:C539");
        assert_eq!(
            s.asked[0][4..6],
            [0x10, 0x04],
            "the root asks for feature 0x1004"
        );

        // the second reading asks the battery only
        let mut s = Script::new(vec![Some(reply(1, 6, 1, [70, 4, 0]))]);
        let e = p
            .read_slot(&mut s, &rx, 1, "logitech:C539:1")
            .entry
            .unwrap();
        assert_eq!(e.reading.percent, Some(70));
        assert_eq!(s.asked.len(), 1);
        assert_eq!(s.asked[0][2], 6);
    }
    #[test]
    fn empty_and_sleeping_slots_make_no_row() {
        let p = LogitechReceiverProvider::default();
        let rx = receiver();
        for code in [ERR_UNKNOWN_DEVICE, ERR_RESOURCE_ERROR, ERR_CONNECT_FAILED] {
            let mut s = Script::new(vec![Some(err10(1, 0, 0, code))]);
            let out = p.read_slot(&mut s, &rx, 1, "k");
            assert!(out.entry.is_none() && !out.stop_receiver);
        }
    }
    #[test]
    fn timeout_stops_the_receiver() {
        let p = LogitechReceiverProvider::default();
        let mut s = Script::new(vec![None]);
        assert!(p.read_slot(&mut s, &receiver(), 1, "k").stop_receiver);
    }
    #[test]
    fn old_hidpp10_device_is_listed_without_a_value() {
        let p = LogitechReceiverProvider::default();
        let mut s = Script::new(vec![Some(err10(2, 0, 0, ERR_INVALID_SUB_ID))]);
        let e = p.read_slot(&mut s, &receiver(), 2, "k").entry.unwrap();
        assert_eq!(e.reading.status, ReadingStatus::NoData);
        assert_eq!(e.reading.percent, None);
    }
    #[test]
    fn falls_back_to_the_status_then_the_voltage_feature() {
        let p = LogitechReceiverProvider::default();
        let mut s = Script::new(vec![
            Some(reply(1, 0, 0, [0, 0, 0])), // no 0x1004
            Some(reply(1, 0, 0, [0, 0, 0])), // no 0x1000
            Some(reply(1, 0, 0, [9, 0, 0])), // 0x1001 at 9
            Some(reply(1, 0, 0, [0, 0, 0])), // no name feature
            Some(reply(1, 9, 0, [0x0F, 0x00, 0x00])),
        ]);
        let e = p.read_slot(&mut s, &receiver(), 1, "k").entry.unwrap();
        assert_eq!(e.reading.percent, Some(60));
        assert_eq!(e.display_name, "Logitech (slot 1)");
        assert_eq!(s.asked[1][4..6], [0x10, 0x00]);
        assert_eq!(s.asked[2][4..6], [0x10, 0x01]);
    }
    #[test]
    fn device_without_any_battery_feature_says_so() {
        let p = LogitechReceiverProvider::default();
        let mut s = Script::new(vec![
            Some(reply(1, 0, 0, [0, 0, 0])),
            Some(reply(1, 0, 0, [0, 0, 0])),
            Some(reply(1, 0, 0, [0, 0, 0])),
            Some(reply(1, 0, 0, [0, 0, 0])),
        ]);
        let e = p.read_slot(&mut s, &receiver(), 1, "k").entry.unwrap();
        assert_eq!(e.reading.status, ReadingStatus::NoData);
        assert!(
            p.known.lock().unwrap().is_empty(),
            "nothing is remembered for a device without battery"
        );
    }
    #[test]
    fn a_2_0_error_forgets_the_device() {
        let p = LogitechReceiverProvider::default();
        let rx = receiver();
        p.known.lock().unwrap().insert(
            "k".into(),
            DeviceInfo {
                battery_index: 6,
                battery_feature: FEATURE_UNIFIED,
                name: "Logitech X".into(),
            },
        );
        let mut s = Script::new(vec![Some(err20(1, 6, 1, 0x05))]);
        assert!(p.read_slot(&mut s, &rx, 1, "k").entry.is_none());
        assert!(p.known.lock().unwrap().is_empty());
    }
    #[test]
    fn a_receiver_walks_six_slots_and_collects_the_answers() {
        let p = LogitechReceiverProvider::default();
        let mut replies = vec![
            // slot 1: empty
            Some(err10(1, 0, 0, ERR_UNKNOWN_DEVICE)),
            // slot 2: a device
            Some(reply(2, 0, 0, [6, 0, 0])),
            Some(reply(2, 0, 0, [0, 0, 0])), // no name feature
            Some(reply(2, 6, 1, [55, 4, 0])),
        ];
        for slot in 3..=6 {
            replies.push(Some(err10(slot, 0, 0, ERR_UNKNOWN_DEVICE)));
        }
        let mut s = Script::new(replies);
        let found = p.read_receiver(&mut s, &receiver(), "logitech:C539");
        assert_eq!(found.len(), 1);
        assert_eq!(found[0].key, "logitech:C539:2");
        assert_eq!(found[0].reading.percent, Some(55));
        assert_eq!(s.asked.len(), 8);
    }
}
