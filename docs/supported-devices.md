# Supported devices

[Português](aparelhos-suportados.md)

**Checked** means read on a real device. **Not tested** means written from
open protocol documentation, with nobody having confirmed it on hardware yet.
If you own a "not tested" device, tell us whether it works: open an
[issue](https://github.com/DillanCidral/Joule/issues) or use
**Send my device** inside Joule.

"Listens" means Joule sends nothing to the device. "Asks" means it sends the
battery question once per reading (see [SECURITY.md](../SECURITY.md)).

## Checked

| Device | Connection | How | Notes |
|---|---|---|---|
| Attack Shark R1 | dongle | listens | Updates by itself while the mouse is awake. A mouse standing still sends nothing |
| Attack Shark R1 | cable | presence | Shows "charging", no percentage |
| Rapoo MT760 Mini | dongle | listens | The mouse only reports when Rapoo's own software is running; otherwise Joule shows the last value with its age |
| DualSense (PS5) | cable only | listens | Over Bluetooth the controller only reports battery after the host writes to it, so that path is left out |
| Xbox controllers | adapter, cable | XInput | Coarse level (low / medium / full) on the adapter; "on cable" when wired |
| 8BitDo Ultimate 2C | cable | XInput | Only "on cable", no percentage. On the dongle it reports a false value, which Joule discards |
| Any Bluetooth device | Bluetooth | reads Windows | The same number as *Settings > Bluetooth*. Nothing goes over the radio |

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
