# Third-party notices

Joule is licensed under AGPL-3.0 (see `LICENSE`). It includes or builds on the
work below.

## Bundled with the app

### Geist (font)

Used for the name "Joule" in the interface. File:
`src/BatteryTray/Fonts/Geist-SemiBold.ttf`.

Copyright 2024 The Geist Project Authors (https://github.com/vercel/geist-font)

Licensed under the SIL Open Font License, Version 1.1. The full text is in
`src/BatteryTray/Fonts/OFL.txt` and at https://openfontlicense.org.

### Velopack (installer and updater)

https://github.com/velopack/velopack

Copyright © 2021 Caelan Sayler
Copyright © 2024 Velopack Ltd.

Licensed under the MIT License (text below).

### .NET (runtime, WPF and Windows Forms)

The installed app carries the .NET 8 runtime (self-contained build).
https://github.com/dotnet/runtime, https://github.com/dotnet/wpf,
https://github.com/dotnet/winforms

Copyright (c) .NET Foundation and Contributors

Licensed under the MIT License (text below). The runtime's own third-party
notices: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT

## Data

### Razer mouse model table

The table of Razer mouse models in `src/BatteryTray/Providers/RazerProvider.cs`
comes from razer-battery-report (https://github.com/xzeldon/razer-battery-report),
which generated it from OpenRazer.

Copyright 2024 Timofey Gelazoniya <contact@zeldon.ru>

Licensed under the MIT License (text below).

### Razer keyboard model table

Transaction id, interface and wait time per keyboard model come from OpenRazer
(https://github.com/openrazer/openrazer), `driver/razerkbd_driver.c`,
GPL-2.0-or-later.

### Model tables from OpenMouse

The Attack Shark, ATK (Compx) and Keychron model tables, and the per-product
targets of the other brands in `ChineseBrandsProvider.cs`, `CompxProvider.cs`,
`AttackSharkProvider.cs` and `IncottProvider.cs`, come from OpenMouse
(https://github.com/OpenMouse-Project/mouse-protocol).

Copyright (C) 2026 OpenMouse-Project. AGPL-3.0-or-later.

## Protocol references

For these projects Joule uses only the **format** of the battery protocol. The
code is Joule's own.

| Project | Used for | License |
|---|---|---|
| [Solaar](https://github.com/pwr-Solaar/Solaar) | Logitech HID++ and the receiver list | GPL-2.0-or-later |
| [OpenRazer](https://github.com/openrazer/openrazer) | Razer feature report | GPL-2.0-or-later |
| [HeadsetControl](https://github.com/Sapd/HeadsetControl) | Logitech, SteelSeries, HyperX and Corsair headsets | GPL-3.0 |
| [OpenLinkHub](https://github.com/jurkovic-nikola/OpenLinkHub) | Corsair Slipstream | GPL-3.0 |
| [rivalcfg](https://github.com/flozz/rivalcfg) | SteelSeries mice | WTFPL |
| [hyperx_pulsefire_dart_reverse_engineering](https://github.com/santeri3700/hyperx_pulsefire_dart_reverse_engineering) | HyperX Pulsefire Dart | CC0-1.0 |
| [RazerNariBatteryLevel](https://github.com/Modzeleczek/RazerNariBatteryLevel) | Razer Nari (format of the reply only) | no license stated |
| [OpenMouse](https://github.com/OpenMouse-Project/mouse-protocol) | Attack Shark, Compx and the other brands | AGPL-3.0-or-later |

Thank you to everyone who documented these protocols in the open.

## MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
