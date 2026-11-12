# Safety and privacy

[Português](SECURITY.pt-BR.md)

Joule talks to hardware you depend on. During development, an aggressive HID
read loop knocked a mouse and a headset off USB for hours. The rules below
exist because of that, and the code enforces them.

## Rules the code enforces

| Rule | Where |
|---|---|
| Read on demand only: app start, Windows login, the Refresh button and Windows announcing that a device connected (one read of that device only, at most once every 10 s). No loop, no timer | `Core/ReadingService.cs` |
| One handle opened once, continuous read, a **single** `CancelIoEx` at the end | `Interop/HidPassiveReader.cs` |
| Never open the system mouse (`0x01`/`0x02`) or system keyboard (`0x01`/`0x06`) collections | `HidInventory.IsSystemReserved` |
| Never a blocking read on a collection with no input reports | `HidPassiveReader.Open` |
| Device gone in the middle of an operation: stop touching it | `HidPassiveReader` loop |
| Writing only through one door: only the battery question open projects already use, one at a time, waiting for the answer, at most 48 per reading for all devices together | `Interop/HidRequest.cs` (the only `WriteFile` in the project) |
| A brand not yet confirmed on real hardware gets no question until you allow it (a notice when the device first appears, or **Allow** in the panel). Permission is per brand and can be removed in Settings | `HidRequest` (same door) |
| **Listen only** in Settings turns every question off, even for allowed brands | `HidRequest` |
| A model reported as misbehaving goes on a block list and never gets a question, even when allowed | `HidRequest.Blocked` |
| No network code in the app or in JouleScan other than the updater, which only accepts this repository | checked by `tools/diag --selftest` |
| The HID scan asks the device for strings, so it never runs on a timer | `Interop/HidInventory.cs` |
| Never invent a number. No data is "no data", never "0%" | every provider |

What stays live between readings is only what does not touch the device: a
passive listener that is already open, and the Bluetooth percentage Windows
itself stored. Hovering the widget never triggers a reading.

## What Joule sends to a device

- **Listen only**: Attack Shark, Rapoo, Incott, DualSense, XInput controllers
  and Bluetooth (for Bluetooth it reads the value Windows stored; nothing goes
  over the radio).
- **Asks for the battery**: Logitech, Razer, SteelSeries, HyperX, Corsair, the
  Compx protocol (ATK, VXE, VGN, Pulsar, Dareu) and part of the other brands.
  Only the read question, never a command that changes a setting.
- Protocols that require repeating the question until two answers match are
  left out on purpose.

## Privacy

- No telemetry, no account, no server. The only network access is the updater
  checking GitHub Releases.
- Joule writes only to `%LOCALAPPDATA%\Joule\`: `settings.json`, `state.json`
  (last value of each device) and `joule.log`.
- **Send my device** saves a `.txt` in `%LOCALAPPDATA%\Joule\envios\` after
  removing serial numbers, Bluetooth addresses, the PC name, the user name and
  Bluetooth devices that are not peripherals (phone, speaker, car). You see
  the file before sending it, and you are the one who sends it. A GitHub issue
  is public.

## If a device misbehaves

If a device froze, dropped off USB or acted strangely after you opened Joule:

1. Close Joule (tray icon > Exit) and replug the dongle.
2. Tell us: open an [issue](https://github.com/DillanCidral/Joule/issues) or
   write to dillan.apps@gmail.com with the device model, `joule.log` (tray
   icon > data folder) and which manufacturer software was open.

If a provider that asks questions is the cause, that device is switched off in
a new release first and investigated after.

## Reporting a vulnerability

Write to dillan.apps@gmail.com instead of opening a public issue.
