//! Minimal HID report descriptor parser: top-level collections with their report lengths.

use std::collections::BTreeMap;

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Tlc {
    pub usage_page: u16,
    pub usage: u16,
    pub input_len: usize,
    pub output_len: usize,
    pub feature_len: usize,
}

struct Builder {
    usage_page: u16,
    usage: u16,
    /// report id -> [input, output, feature] bits
    bits: BTreeMap<u8, [u32; 3]>,
}

impl Builder {
    fn finish(self) -> Tlc {
        let len = |k: usize| {
            self.bits
                .values()
                .filter(|b| b[k] > 0)
                .map(|b| (b[k] as usize).div_ceil(8) + 1)
                .max()
                .unwrap_or(0)
        };
        Tlc {
            usage_page: self.usage_page,
            usage: self.usage,
            input_len: len(0),
            output_len: len(1),
            feature_len: len(2),
        }
    }
}

pub fn parse(desc: &[u8]) -> Vec<Tlc> {
    let mut out = Vec::new();
    let (mut page, mut usage) = (0u16, 0u16);
    let (mut size, mut count, mut report_id) = (0u32, 0u32, 0u8);
    let mut depth = 0u32;
    let mut cur: Option<Builder> = None;
    let mut i = 0usize;

    while i < desc.len() {
        let prefix = desc[i];
        if prefix == 0xFE {
            // long item: skip it
            let Some(&n) = desc.get(i + 1) else { break };
            i += 3 + n as usize;
            continue;
        }
        let data_len = match prefix & 0x03 {
            3 => 4,
            n => n as usize,
        };
        if i + 1 + data_len > desc.len() {
            break;
        }
        let mut val = 0u32;
        for (k, b) in desc[i + 1..i + 1 + data_len].iter().enumerate() {
            val |= (*b as u32) << (8 * k);
        }
        let kind = (prefix >> 2) & 0x03;
        let tag = prefix >> 4;
        i += 1 + data_len;

        match kind {
            1 => match tag {
                // global
                0x0 => page = val as u16,
                0x7 => size = val,
                0x8 => report_id = val as u8,
                0x9 => count = val,
                _ => {}
            },
            2 => {
                // local: usage
                if tag == 0x0 {
                    if data_len == 4 {
                        page = (val >> 16) as u16;
                    }
                    usage = val as u16;
                }
            }
            0 => {
                // main
                match tag {
                    0xA => {
                        if depth == 0 {
                            cur = Some(Builder {
                                usage_page: page,
                                usage,
                                bits: BTreeMap::new(),
                            });
                        }
                        depth += 1;
                    }
                    0xC => {
                        depth = depth.saturating_sub(1);
                        if depth == 0
                            && let Some(b) = cur.take()
                        {
                            out.push(b.finish());
                        }
                    }
                    0x8 | 0x9 | 0xB => {
                        if let Some(b) = cur.as_mut() {
                            let k = match tag {
                                0x8 => 0,
                                0x9 => 1,
                                _ => 2,
                            };
                            b.bits.entry(report_id).or_insert([0; 3])[k] += size * count;
                        }
                    }
                    _ => {}
                }
                usage = 0; // local items reset after every main item
            }
            _ => {}
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    // Logitech-style vendor collection: page FF43, usage 0202, report id 0x11, 19 data bytes each way.
    const VENDOR: &[u8] = &[
        0x06, 0x43, 0xFF, 0x0A, 0x02, 0x02, 0xA1, 0x01, 0x85, 0x11, 0x75, 0x08, 0x95, 0x13, 0x09,
        0x01, 0x81, 0x00, 0x09, 0x01, 0x91, 0x00, 0xC0,
    ];
    // Boot keyboard: 8 modifier bits, 1 reserved byte, 5 LED bits + 3 padding, 6 key bytes.
    const KEYBOARD: &[u8] = &[
        0x05, 0x01, 0x09, 0x06, 0xA1, 0x01, 0x05, 0x07, 0x19, 0xE0, 0x29, 0xE7, 0x15, 0x00, 0x25,
        0x01, 0x75, 0x01, 0x95, 0x08, 0x81, 0x02, 0x95, 0x01, 0x75, 0x08, 0x81, 0x01, 0x95, 0x05,
        0x75, 0x01, 0x05, 0x08, 0x19, 0x01, 0x29, 0x05, 0x91, 0x02, 0x95, 0x01, 0x75, 0x03, 0x91,
        0x01, 0x95, 0x06, 0x75, 0x08, 0x15, 0x00, 0x25, 0x65, 0x05, 0x07, 0x19, 0x00, 0x29, 0x65,
        0x81, 0x00, 0xC0,
    ];

    #[test]
    fn vendor_collection() {
        let t = parse(VENDOR);
        assert_eq!(t.len(), 1);
        assert_eq!((t[0].usage_page, t[0].usage), (0xFF43, 0x0202));
        assert_eq!(
            (t[0].input_len, t[0].output_len, t[0].feature_len),
            (20, 20, 0)
        );
    }
    #[test]
    fn keyboard_collection() {
        let t = parse(KEYBOARD);
        assert_eq!(t.len(), 1);
        assert_eq!((t[0].usage_page, t[0].usage), (0x0001, 0x0006));
        // input: 8 + 8 + 48 bits = 8 bytes + id byte; output: 5 + 3 bits = 1 byte + id byte
        assert_eq!((t[0].input_len, t[0].output_len), (9, 2));
    }
    #[test]
    fn two_collections_in_one_descriptor() {
        let mut d = VENDOR.to_vec();
        d.extend_from_slice(KEYBOARD);
        assert_eq!(parse(&d).len(), 2);
    }
    #[test]
    fn truncated_descriptor_does_not_panic() {
        assert!(parse(&VENDOR[..7]).is_empty());
        assert!(parse(&[]).is_empty());
    }
}
