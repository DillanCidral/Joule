# Architecture

[Português](arquitetura.md)

Joule is a single self-contained `win-x64` executable (.NET 8, WPF) that runs
without administrator rights.

## Layers

```
src/BatteryTray/
  Core/       reading model, provider registry, cache, settings, log
  Interop/    Windows APIs: HID, SetupAPI (Bluetooth), XInput
  Providers/  one class per hardware family
  Ui/         panel, edge widget, tray icon, onboarding, "Send my device"
src/BatteryScan/   JouleScan: the capture tool; the app reuses its code
tools/diag/        diagnostics and the self-test
```

`Core`, `Interop` and `Providers` do not depend on the interface. The same
reading code runs in the app, in JouleScan and in `tools/diag`.

Namespaces and folders still say `BatteryTray`, the project's first name.

## How a reading happens

1. `HidInventory.Scan` lists the HID collections present. The scan asks
   devices for their strings, so it only runs inside an on-demand reading.
2. `ProviderRegistry` hands each known VID:PID to its provider. Standalone
   providers (XInput, Bluetooth, the brands that ask) discover their own
   devices from the same list.
3. Each provider returns a `BatteryReading`: a percentage, a coarse level,
   "charging", or "no data" with the reason.
4. A device with no provider is listed as "unsupported". It never disappears
   and never shows a value.
5. The last value of each device is cached on disk and shown with its age when
   a reading comes back empty.

## On demand, plus two live sources

There are four triggers for a reading: app start, Windows login, Refresh and
Windows announcing that a device connected. That last one waits 1.5 s for the
device to settle, reads only the device that arrived and runs at most once
every 10 s.
The low battery alert is evaluated at the end of a reading, not by a watchdog.

Only what does not touch the device stays live:

- **Passive listening** (Attack Shark, Rapoo): one HID handle, opened once,
  receives the reports already travelling. A listener that dropped is not
  reopened outside an on-demand reading.
- **Bluetooth**: every 60 seconds Joule rereads the percentage Windows already
  stored on the device node. Nothing goes over the radio.

Live readings reuse the collections from the last full scan.

## The single write door

Providers that must ask for the battery do it through
`Interop/HidRequest.cs`, the only place in the project that writes to a
device. It refuses system mouse and keyboard collections, sends one question
at a time, waits for the answer and caps a session at 48 questions. See
[SECURITY.md](../SECURITY.md).

## Data, install and update

- Data lives in `%LOCALAPPDATA%\Joule\`: `settings.json`, `state.json`
  (cache) and `joule.log`.
- Installer and updater are [Velopack](https://github.com/velopack/velopack).
  The app checks GitHub Releases on start and every hour.
- A second instance does not start: it asks the running one to show the panel.
