# Joule on Linux

A Rust port of Joule for Linux, built and checked on Fedora 44 with GNOME on Wayland. It lives in
[`linux/`](../linux) next to the Windows app and shares its rules ([SECURITY.md](../SECURITY.md)):
the battery is read on demand only, nothing is sent to a device without your permission, and there is
one door for writing to a device (`linux/crates/joule-hid/src/request.rs`).

## What works

| Piece | State |
|---|---|
| Window (libadwaita) with the Joule rings, settings and about dialogs | works, checked on GNOME (light and dark) |
| Top bar item with the pill icon and a menu listing every device | works (needs the AppIndicator extension on GNOME) |
| Notices (desktop notification) and sounds (sound theme through `canberra-gtk-play`) | works |
| Hardware arrival and removal (kernel uevents), settle 1.5 s, at most one reading per 10 s | works, checked by replugging a dongle |
| Logitech headsets G533, G535, G633/G635, G933/G935, G733, G PRO, G PRO X, G PRO X 2 | G933 checked on hardware, the rest from the documented bytes |
| Logitech receivers (Unifying, Bolt, Lightspeed, Nano) | Lightspeed checked on hardware (G703) |
| Other brands (Attack Shark, Rapoo, DualSense, Razer, SteelSeries, HyperX, Corsair, Compx, ...) | **not ported yet**: listed as "unsupported" |
| Bluetooth (BlueZ / UPower) | **not ported yet** |
| Edge widget, tinted glass, updater, onboarding and "what's new" screens | left out: Wayland cannot dock a window to an edge or blur behind it; Linux has no updater |
| A GNOME Shell drop-down in the top bar (the real counterpart of the Windows flyout) | planned; needs `mutter-devkit` to test in a nested shell |

## Install (Fedora 44)

```bash
sudo dnf install gcc gtk4-devel libadwaita-devel systemd-devel dbus-devel
cargo install --path linux/crates/joule --locked --root ~/.local
install -Dm644 linux/packaging/io.github.dillancidral.Joule.desktop ~/.local/share/applications/io.github.dillancidral.Joule.desktop
install -Dm644 linux/packaging/icons/io.github.dillancidral.Joule.png ~/.local/share/icons/hicolor/256x256/apps/io.github.dillancidral.Joule.png
# only if your user cannot open /dev/hidraw* yet (often already allowed by the desktop ACL):
sudo install -m644 linux/packaging/99-joule.rules /etc/udev/rules.d/ && sudo udevadm control --reload && sudo udevadm trigger
```

GNOME shows the top bar item only with the AppIndicator extension
(`gnome-shell-extension-appindicator`); the panel and the CLI work without it.

## Use

```bash
joule --panel          # the panel and the top bar item
joule --hidden         # start in the top bar only (Settings > Start at login writes this)
joule                  # one reading in the terminal; --json for scripts, --all lists unsupported devices
joule --watch          # keep running, reread when a hidraw node comes or goes
joule allow 046D       # let Joule ask this brand (VID, hex) for its battery; deny undoes it
joule --selftest       # check the security rules that need no device
joule --collections    # list the HID collections (sysfs only, nothing is sent)
```

A brand that Joule has not confirmed on real hardware gets no question until you allow it (the
"Allow" button in the panel does the same as `joule allow`). "Listen only" in Settings stops every
question. Data lives in `~/.local/share/joule/` (`settings.json`, `state.json`, `joule.log`).

## Develop

```bash
cd linux
cargo fmt --check && cargo clippy --workspace --all-targets -- -D warnings && cargo test --workspace
JOULE_SNAPSHOT=/tmp/shots joule --panel     # writes main/settings/about PNGs of the real window, then quits
joule --udev-rules > packaging/99-joule.rules   # regenerate the rule after adding a device
```

The crates: `joule-core` (model, cache, settings, display rules), `joule-hid` (hidraw, the gate and the
single write door), `joule-providers` (one module per hardware family), `joule` (CLI, panel, top bar item).
To add a device, follow [adding-a-device.md](adding-a-device.md) and port the provider to
`joule-providers`; the byte formats and curves are the same as in the C# provider.
