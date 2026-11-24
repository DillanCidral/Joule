# Supported devices

[Português](aparelhos-suportados.md)

**Checked** means read on a real device. **Not tested** means written from
open protocol documentation, with nobody having confirmed it on hardware yet.
If you own a "not tested" device, tell us whether it works: open an
[issue](https://github.com/DillanCidral/Joule/issues) or use
**Send my device** inside Joule.

"Listens" means Joule sends nothing to the device. "Asks" means it sends the
each protocol query once per reading (see [SECURITY.md](../SECURITY.md)).

## Checked

| Device | Connection | How | Notes |
|---|---|---|---|
| Attack Shark R1 | dongle | listens | Updates by itself while the mouse is awake. A mouse standing still sends nothing |
| Attack Shark R1 | cable | presence | Shows "charging", no percentage |
| Attack Shark V6 | dongle | listens | Seen only at 100% so far (the mouse sends a "full" message); lower levels use the same format as the X11 but are untested |
| Rapoo MT760 Mini | dongle | listens | The mouse only reports when Rapoo's own software is running; otherwise Joule shows the last value with its age |
| Razer BlackShark V3 | dongle | asks | Asks once per reading (checked on hardware); between readings it keeps what the headset reported on its own, with its age |
| DualSense (PS5) | cable; Bluetooth not tested yet | listens | Over Bluetooth the controller only reports battery after a program such as Steam writes to it; Joule only reads what it already sends |
| DualShock 4 (PS4) | cable and Bluetooth, not tested yet | listens | Same as the DualSense over Bluetooth |
| Xbox controllers | adapter, cable | XInput | Coarse level (low / medium / full) on the adapter; "on cable" when wired |
| Switch Pro Controller (`057E:2009`) | Bluetooth; cable not tested yet | listens | Coarse level (empty / low / medium / full) and charging. Like the DualSense over Bluetooth, it only reports battery after a program such as Steam switches it to full reports; Joule only reads what it already sends |
| 8BitDo Ultimate 2C | cable | XInput | Only "on cable", no percentage. On the dongle it reports a false value, which Joule discards |
| Any Bluetooth device | Bluetooth | reads Windows | The same number as *Settings > Bluetooth*. Nothing goes over the radio |
| Zaopin ZPW MAX (8K) | `3554:F527` receiver | asks | Battery percentage confirmed on hardware |
| AULA F75 | `258A:010C` cable | presence | Shows "wired", without inventing a percentage |
| AJAZZ 2.4G 8K | `3151:402D` GearHub-V5 receiver | asks | Receiver status `F7`; battery percentage confirmed on hardware |
| ATK A9 Ultra V2 | `373B:11FE` 8K NANO receiver | asks | Battery percentage confirmed on hardware. A mouse asleep does not answer; move it and the next reading picks it up |
| ATK 1K DONGLE-PAW (`373B:10F0`) | dongle | asks | One battery query per reading, independent of ATK HUB. The receiver may report charging with the cable unplugged |
| ATK Mercury I / M1 (`373B:107C`) | dongle | asks | Receiver `0005`, headset `0007`. Three queries verify version, layout and percentage. Charging state pending |

### Reading the checked ATK devices

Queries follow Joule's brand permissions, **Listen only** setting and global
query limit. Allow the ATK brand when prompted. The providers run when Joule
starts, on **Refresh** and on the receiver's USB arrival. Reading a receiver
that is already connected requires **Refresh**.

- The `373B:10F0` mouse uses collection `FF10/1`, a 65-byte input report and
  a 9-byte feature report. It sends `00 82 00 00 00 00 00 00 00` once and waits
  up to three seconds for the interrupt reply `82 08 02 00 FF 03`. In the
  Windows report, the lower seven bits of byte 9 contain the percentage and
  the high bit indicates charging. The parser checks the signature and the
  0–100 range. The query was validated with ATK HUB closed, including a 27%
  reading. The charging bit can be set while the mouse is unplugged.
- The `373B:107C` Mercury uses collection `FF22/1` at `mi_00/col02`, with
  64-byte feature reports. Receiver version `0005` is required. Command
  `AA 01` verifies headset firmware `0007`. Two `AA 03` queries read four
  bytes each, first the pointer at `0x0100FE94`, which must contain
  `0x0100FE5C`, then capacity at `0x0100FE78`. Addresses are sent in big-endian
  and values are returned in little-endian. Each query waits 300 ms before
  reading one reply. An invalid version, pointer or percentage produces
  **no data**. Comparisons with Bluetooth readings of 100%, 90% and 80%
  returned 100%, 93% and 89% through this channel, respectively. Tests also
  confirmed no reply with the headset switched off. Charging state still
  needs to be mapped.

## Confirmed by users

Written from open documentation, then confirmed by someone who sent a capture
showing Joule reading it on their own PC (November 2026).

| Device | Connection |
|---|---|
| Logitech mice on LIGHTSPEED (PRO X 2 and others) | LIGHTSPEED receiver |
| Logitech MX Master 2S | Unifying receiver |
| SteelSeries Arctis 1 Wireless and Arctis 7 | dongle |
| VXE R1 Pro Max | `3554:F58A` receiver |
| VGN IO Aurora and Aurora Pro 8K | `3554:FB08` and `3554:FB40` receivers |
| AULA AU75 | `3554:FA09` receiver (same as the F75) |
| Razer Basilisk V3 Pro 35K | dongle `1532:00CD` |
| SteelSeries Arctis Nova 7 Gen 2 | dongle `1038:227E` |
| Lamzu Maya X | `373E:001E` 8K dongle |
| Dark Project Novus | `3554:FB0A` VGN/VXE receiver |
| HyperX Cloud Alpha 2 Wireless | dongle |
| AULA F75 | `3554:FA09` receiver |
| Pulsar TenZ Signature Edition | Pulsar 8K receiver |
| SteelSeries Arctis Nova 3P / 3X | dongle |
| Logitech G733 | dongle |

## Not tested

| Brand | Devices | How |
|---|---|---|
| Logitech | Devices on Unifying, Bolt, Lightspeed and Nano receivers; headsets G533, G535, G633, G635, G733, G933, G935, G PRO, PRO X | asks (HID++ 2.0) |
| Razer | 71 mice on dongle and cable, 13 wireless keyboards, Nari headset | asks (feature report) |
| SteelSeries | Arctis 1, 7, 7+, Nova 3P / 5 / 7 / Pro, GameBuds; Aerox 3 / 5 / 9, Prime, Rival 3, Rival 650 | asks |
| HyperX | Cloud II Wireless, Cloud Alpha Wireless, Cloud Alpha 2 Wireless, Cloud Flight; Pulsefire Dart | asks |
| Corsair | 22 mice and keyboards on the Slipstream dongle; Void, Void Wireless V2, HS80 Max, Virtuoso Max, Virtuoso XT / SE | asks (read only) |
| Attack Shark | X6, X11, X11 SE, Delux M600 Pro on the same receiver as the R1 | listens |
| Attack Shark | R2 (GearHub-V5 receiver), R5 Ultra, R6, R8 | asks |
| ATK, VXE, VGN, Pulsar, Dareu, Hitscan | "report 8" protocol of Compx/Beken chips (192 ATK models) | asks |
| AULA | F75 (`3554:FA09` receiver) | asks only for battery on the receiver |
| G-Wolves | xvi generations and HTX Ultra | asks |
| Lamzu, LunaFury, CRDRAKO, Vancer, RAWM, WLmouse | same protocol family | asks |
| AJAZZ | NJ07, NJ08 | asks |
| K-Snake / Noir | X11, M2-NEX | asks |
| Rapoo | VT9 Pro | reads a feature, no question |
| Lingbao | M5 Pro (GearHub-V5 receiver; also the paired keyboard) | asks |
| Bytech, Motospeed X6, Keychron mice | | asks |
| Incott | receiver and cable | listens |

## Known gaps

- **Logitech**: HID++ 1.0 devices, the "Centurion" headsets (G PRO X 2
  Lightspeed, G522, Astro A50), Logitech on cable.
- **Razer**: other headsets.
- **SteelSeries**: Arctis 9, Arctis Pro Wireless.
- **HyperX**: other Pulsefire mice, wireless keyboards.
- **Corsair**: Slipstream V2 dongle, Katar Pro Wireless, Sabre V2 Pro, devices
  on cable.
- **MCHOSE, Ninjutso, the rest of Rapoo VT9**: their protocol needs the
  question repeated until answers match. Left out on purpose.
- **Keychron** mice without the `0xFFC1` collection.
- A device on a dongle **and** on Bluetooth shows up as two rows.
- Two DualSense controllers show up as one row.
