<h1 align="center"><img src="docs/images/capa.png" alt="Joule" width="100%"></h1>

<p align="center"><strong>Battery for your wireless mouse, headset, controller and keyboard, in the Windows tray.</strong></p>

<p align="center">
  <a href="https://github.com/DillanCidral/Joule/releases/latest"><img src="https://img.shields.io/github/v/release/DillanCidral/Joule?color=FFC21F&labelColor=1D1B18" alt="Latest release"></a>
  <a href="https://github.com/DillanCidral/Joule/releases"><img src="https://img.shields.io/github/downloads/DillanCidral/Joule/total?color=FFC21F&labelColor=1D1B18" alt="Downloads"></a>
  <img src="https://img.shields.io/badge/Windows-11-FFC21F?labelColor=1D1B18" alt="Windows 11">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-AGPL--3.0-FFC21F?labelColor=1D1B18" alt="License: AGPL-3.0"></a>
</p>

<p align="center"><img src="docs/images/bandeira-us.png" alt="" height="14"> <strong>English</strong> · <a href="README.pt-BR.md"><img src="docs/images/bandeira-br.png" alt="" height="14"> Português</a> · <a href="README.fr.md"><img src="docs/images/bandeira-fr.png" alt="" height="14"> Français</a> · <a href="README.ru.md"><img src="docs/images/bandeira-ru.png" alt="" height="14"> Русский</a> · <a href="README.zh-TW.md"><img src="docs/images/bandeira-tw.png" alt="" height="14"> 繁體中文</a></p>

> [!WARNING]
> **Windows Defender and version 0.2.3.** Since November 20, 2026, Defender
> flags the installer as `Trojan:Win32/Yomal!rfn` and quarantines it. The
> release files are the same ones published on November 19, and the app rebuilt
> from this code matches the released binary. Everything points to a false
> alarm, and the file has been sent to Microsoft for review. If Defender caught
> Joule, you can leave it in quarantine and wait: this notice will change when
> Microsoft replies. Details on the [0.2.3 release page](https://github.com/DillanCidral/Joule/releases/tag/v0.2.3).

<p align="center">
  <a href="https://github.com/DillanCidral/Joule/releases/latest/download/JouleApp-win-Setup.exe"><img src="https://img.shields.io/badge/Download_for_Windows-FFC21F?style=for-the-badge" alt="Download for Windows" height="40"></a>
</p>

<p align="center"><img src="docs/images/joule.png" alt="Joule panel and edge widget, dark theme" width="85%"></p>

Joule is a small Windows 11 app that sits in the tray and shows how much
battery your wireless devices have left. It is free, open source and in
**beta**.

## What you get

- **Panel**: click the tray icon to see every connected device, weakest first.
- **Edge widget**: battery rings on the edge of the screen. It hides itself and
  gets out of the way of full-screen apps.
- **Tray icon**: shows at a glance whether anything is running low; hover to
  see each device's battery.
- **Connect notice**: when a device turns on, Joule shows its battery, on the
  widget or as a Windows notification, with sound if you want.
- **Low battery alert**, with a limit you choose.
- **English, Portuguese, French and Russian**: follows the Windows language, and you can
  switch in Settings. French and Russian were translated by people who use Joule.
- **Full in green**: devices that report charging turn green on the widget and
  in the tray when they reach 100%.
- **Summary after updating**: how many devices Joule tracked and how many
  low-battery warnings it gave, counted only on your PC, plus what changed in
  the version.

## Download

Get `JouleApp-win-Setup.exe` with the button above or from the
[Releases page](https://github.com/DillanCidral/Joule/releases), and run it.

- No administrator rights needed. It installs for your user only and shows up
  in *Installed apps*, where you can remove it.
- The installer is not digitally signed, so Windows SmartScreen will warn you.
  Click **More info**, then **Run anyway**.
- Joule updates itself from GitHub Releases and tells you in the tray when a
  new version is ready.

## Supported devices

| Device | Connection | Status |
|---|---|---|
| Attack Shark R1 | dongle | ✅ checked |
| Rapoo MT760 Mini | dongle | ✅ checked |
| ATK 1K DONGLE-PAW | dongle | ✅ checked |
| ATK Mercury I / M1, receiver `0005` and headset `0007` | dongle | ✅ checked |
| Razer BlackShark V3 | dongle | ✅ checked |
| Zaopin ZPW MAX (8K) | dongle | ✅ checked |
| AJAZZ 2.4G 8K receiver | dongle | ✅ checked |
| AULA F75 | cable | ✅ checked |
| DualSense | cable | ✅ checked |
| DualSense on Bluetooth, DualShock 4 | cable, Bluetooth (with Steam or a similar program open) | 🧪 not tested yet |
| Xbox controllers | adapter and cable | ✅ checked |
| 8BitDo Ultimate 2C | cable | ✅ checked |
| Switch Pro Controller | Bluetooth (with Steam or a similar program open) | ✅ checked |
| Any Bluetooth device whose battery Windows already shows in *Settings > Bluetooth* | Bluetooth | ✅ checked |
| Logitech mice on LIGHTSPEED (PRO X 2 and others) and Unifying (MX Master 2S), SteelSeries Arctis 1 and Arctis 7, VXE R1 Pro Max, VGN IO Aurora, AULA AU75, Razer Basilisk V3 Pro 35K, SteelSeries Arctis Nova 7 Gen 2, Lamzu Maya X, Dark Project Novus, HyperX Cloud Alpha 2 Wireless, AULA F75 (dongle), Pulsar TenZ, SteelSeries Arctis Nova 3P/3X, Logitech G733 | dongle | ✅ confirmed by users |
| Logitech, Razer, SteelSeries, HyperX and Corsair: mice, keyboards and headsets | dongle | 🧪 not tested yet |
| About 20 other brands: ATK, VXE, VGN, Pulsar, Dareu, AJAZZ, Lamzu, WLmouse, G-Wolves, Hitscan, K-Snake, Bytech, Motospeed, Keychron, Incott and more Attack Shark models | dongle | 🧪 not tested yet |

🧪 means written from open protocol documentation and not yet tested on real
hardware. If you own one, your report is what confirms it. "Confirmed by users"
means someone sent a capture showing Joule reading it on their own PC. Full list:
[docs/supported-devices.md](docs/supported-devices.md).

## My device is not listed

First check the [device families](https://github.com/DillanCidral/Joule/issues?q=is%3Aissue%20is%3Aopen%20label%3Atracking): if yours is there, give it a 👍
instead of opening a new issue. Joule also links to similar requests when you
send one.

Open **Settings > Advanced > Submit a device** inside Joule. It listens for 30 seconds
while you use the device, shows you what it found, removes personal data
(serial number, Bluetooth address, PC name, user name) and saves a `.txt`.
You then send that file yourself, as a
[GitHub issue](https://github.com/DillanCidral/Joule/issues/new/choose) or by
[e-mail](#contact). Nothing leaves your PC on its own.

Want to add the device yourself? See
[docs/adding-a-device.md](docs/adding-a-device.md).

<details>
<summary><strong>What Joule does to your devices</strong></summary>

- It reads when you open the app, when you sign in to Windows, when you click
  **Refresh**, when you plug in or turn on a device (only that device, at
  most once every 10 seconds) and when you open the panel if the last reading
  is more than an hour old. It never polls in a loop.
- For most devices it only **listens** to what the dongle or cable already
  sends, or reads the number Windows itself stored (Bluetooth).
- For some brands on a dongle (Logitech, Razer, SteelSeries, HyperX, Corsair
  and part of the others) it **asks** the device for its battery, only after
  you allow that brand: the same
  question open projects such as Solaar, OpenRazer and HeadsetControl send,
  once per reading. It never sends a command that changes a setting. The
  **Listen only** switch in Settings turns off every question.
- It never shows a number it did not read. No data is "no data", never "0%".
- It sends nothing anywhere. The only network access is the update check on
  GitHub Releases.

Details: [SECURITY.md](SECURITY.md).

</details>

<details>
<summary><strong>Build from source</strong></summary>

You need Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build src/BatteryTray -c Debug
```

```bash
dotnet run --project src/BatteryTray -- --demo
```

`--demo` shows sample devices without touching any hardware. More in
[CONTRIBUTING.md](CONTRIBUTING.md) and [docs/architecture.md](docs/architecture.md).

</details>

## Thanks

Version 0.2.3 adds code and translations from [@DiogoLss](https://github.com/DiogoLss) (ATK 1K DONGLE-PAW and
Mercury I / M1), [@BatuhanUyurca](https://github.com/BatuhanUyurca) (ATK A9 Ultra V2), [@Ali-Fani](https://github.com/Ali-Fani) (Attack Shark V6),
[@mochi0820](https://github.com/mochi0820) (Traditional Chinese), [@TukaLeHibou](https://github.com/TukaLeHibou) (French, reviewed) and [@wrldwideme](https://github.com/wrldwideme)
(Russian README), and the clock format came from an idea by Yim.

Version 0.2.1 has code and translations from the community:
[@Ali-Fani](https://github.com/Ali-Fani) (Razer BlackShark V3),
[@wrldwideme](https://github.com/wrldwideme) (AULA F75, the AJAZZ and Zaopin
receivers and the Russian translation),
[@TukaLeHibou](https://github.com/TukaLeHibou) (French translation), and the
green full battery came from an idea by Daniel Palma. Thank you, and thanks to
everyone who sent a capture.

## Support Joule

Joule is free and open source. If it helps you, a coffee helps keep it that
way: [support Joule](https://buy.stripe.com/bJeeVe5yq5Pub7bfz8bZe00).

## Contact

- E-mail: dillan.apps@gmail.com
- Instagram: [@dillan.apps](https://www.instagram.com/dillan.apps/)
- YouTube: [@dillanapps](https://www.youtube.com/@dillanapps)

## License

[AGPL-3.0](LICENSE). Third-party licenses and the open projects whose protocol
documentation made Joule possible are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Joule is not affiliated with Logitech, Razer, SteelSeries, HyperX, Corsair,
Sony, Microsoft, Nintendo, 8BitDo or any other brand mentioned here. Their names and
trademarks belong to their owners.
