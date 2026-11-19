<h1 align="center"><img src="docs/images/capa.png" alt="Joule" width="100%"></h1>

<p align="center"><strong>Заряд беспроводной мыши, гарнитуры, геймпада и клавиатуры в трее Windows.</strong></p>

<p align="center">
  <a href="https://github.com/DillanCidral/Joule/releases/latest"><img src="https://img.shields.io/github/v/release/DillanCidral/Joule?color=FFC21F&labelColor=1D1B18" alt="Последняя версия"></a>
  <a href="https://github.com/DillanCidral/Joule/releases"><img src="https://img.shields.io/github/downloads/DillanCidral/Joule/total?color=FFC21F&labelColor=1D1B18" alt="Загрузки"></a>
  <img src="https://img.shields.io/badge/Windows-11-FFC21F?labelColor=1D1B18" alt="Windows 11">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-AGPL--3.0-FFC21F?labelColor=1D1B18" alt="Лицензия: AGPL-3.0"></a>
</p>

<p align="center"><a href="README.md"><img src="docs/images/bandeira-us.png" alt="" height="14"> English</a> · <a href="README.pt-BR.md"><img src="docs/images/bandeira-br.png" alt="" height="14"> Português</a> · <a href="README.fr.md"><img src="docs/images/bandeira-fr.png" alt="" height="14"> Français</a> · <img src="docs/images/bandeira-ru.png" alt="" height="14"> <strong>Русский</strong> · <a href="README.zh-TW.md"><img src="docs/images/bandeira-tw.png" alt="" height="14"> 繁體中文</a></p>

<p align="center">
  <a href="https://github.com/DillanCidral/Joule/releases/latest/download/JouleApp-win-Setup.exe"><img src="https://img.shields.io/badge/Скачать_для_Windows-FFC21F?style=for-the-badge" alt="Скачать для Windows" height="40"></a>
</p>

<p align="center"><img src="docs/images/joule.png" alt="Панель Joule и виджет у края экрана, тёмная тема" width="85%"></p>

Joule — небольшое приложение для Windows 11, которое живёт в трее и показывает,
сколько заряда осталось у беспроводных устройств. Оно бесплатное, с открытым
исходным кодом и пока находится в **бета-версии**.

## Что умеет

- **Панель**: щёлкните по значку в трее, чтобы увидеть все подключённые
  устройства, начиная с самого разряженного.
- **Виджет у края экрана**: кольца заряда на краю экрана. Он сам прячется и не
  мешает полноэкранным приложениям.
- **Значок в трее**: сразу видно, есть ли устройства с низким зарядом; наведите
  курсор, чтобы увидеть заряд каждого из них.
- **Уведомление о подключении**: когда устройство включается, Joule показывает
  его заряд в виджете или в уведомлении Windows, со звуком, если хотите.
- **Предупреждение о низком заряде** с настраиваемым порогом.
- **Английский, португальский, французский и русский**: Joule автоматически
  выбирает язык Windows, а изменить его можно в **Настройках**. Французский и
  русский перевели пользователи Joule.
- **Зелёный цвет при полной зарядке**: устройства, которые передают состояние
  зарядки, становятся зелёными в виджете и трее, когда достигают 100%.
- **Сводка после обновления приложения**: сколько устройств Joule отслеживал,
  сколько раз предупреждал о низком заряде (считается только на вашем ПК) и что
  нового в версии.

## Скачать

Скачайте `JouleApp-win-Setup.exe` кнопкой выше или со
[страницы релизов](https://github.com/DillanCidral/Joule/releases) и запустите.

- Права администратора не нужны. Приложение ставится только для вашего
  пользователя и появляется в *Установленных приложениях*, где его можно удалить.
- У установщика нет цифровой подписи, поэтому Windows SmartScreen предупредит
  вас. Нажмите **Подробнее**, затем **Выполнить в любом случае**.
- Joule обновляется сам через GitHub Releases и сообщает в трее, когда новая
  версия готова.

## Поддерживаемые устройства

| Устройство | Подключение | Статус |
|---|---|---|
| Attack Shark R1 | ресивер | ✅ проверено |
| Rapoo MT760 Mini | ресивер | ✅ проверено |
| ATK 1K DONGLE-PAW | ресивер | ✅ проверено |
| ATK Mercury I / M1 | ресивер | ✅ проверено |
| Razer BlackShark V3 | ресивер | ✅ проверено |
| Zaopin ZPW MAX (8K) | ресивер | ✅ проверено |
| Ресивер AJAZZ 2.4G 8K | ресивер | ✅ проверено |
| AULA F75 | кабель | ✅ проверено |
| DualSense | кабель | ✅ проверено |
| DualSense по Bluetooth, DualShock 4 | кабель, Bluetooth (при открытом Steam или похожей программе) | 🧪 ещё не проверено |
| Геймпады Xbox | адаптер и кабель | ✅ проверено |
| 8BitDo Ultimate 2C | кабель | ✅ проверено |
| Любое Bluetooth-устройство, заряд которого Windows уже показывает в разделе *Параметры → Bluetooth и устройства* | Bluetooth | ✅ проверено |
| Мыши Logitech через LIGHTSPEED (PRO X 2 и другие) и Unifying (MX Master 2S), SteelSeries Arctis 1 и Arctis 7, VXE R1 Pro Max, VGN IO Aurora, AULA AU75, Razer Basilisk V3 Pro 35K, SteelSeries Arctis Nova 7 Gen 2, Lamzu Maya X, Dark Project Novus, HyperX Cloud Alpha 2 Wireless, AULA F75 (ресивер), Pulsar TenZ, SteelSeries Arctis Nova 3P/3X, Logitech G733 | ресивер | ✅ подтверждено пользователями |
| Logitech, Razer, SteelSeries, HyperX и Corsair: мыши, клавиатуры и гарнитуры | ресивер | 🧪 ещё не проверено |
| Около 20 других брендов: ATK, VXE, VGN, Pulsar, Dareu, AJAZZ, Lamzu, WLmouse, G-Wolves, Hitscan, K-Snake, Bytech, Motospeed, Keychron, Incott и другие модели Attack Shark | ресивер | 🧪 ещё не проверено |

🧪 означает, что поддержка написана по открытой документации протокола и ещё не
проверена на реальном устройстве. Если оно у вас есть, именно ваш отчёт это
подтвердит. «Подтверждено пользователями» означает, что пользователь прислал
диагностический файл, подтверждающий чтение заряда на его компьютере. Полный
список (на английском):
[docs/supported-devices.md](docs/supported-devices.md).

## Моего устройства нет в списке

Сначала посмотрите [семейства устройств](https://github.com/DillanCidral/Joule/issues?q=is%3Aissue%20is%3Aopen%20label%3Atracking):
если ваше устройство уже указано там, поставьте 👍 вместо того, чтобы создавать
новое обращение. При отправке данных Joule также покажет похожие запросы.

Откройте в Joule **Настройки → Дополнительно → Отправить данные устройства**.
Joule в течение 30 секунд получает данные, пока вы пользуетесь устройством,
показывает, что обнаружил, удаляет личные данные (серийный номер,
Bluetooth-адрес, имя ПК и имя пользователя) и сохраняет файл `.txt`. Затем вы
сами отправляете этот файл, создав
[обращение на GitHub](https://github.com/DillanCidral/Joule/issues/new/choose)
или написав по [электронной почте](#контакты). Ничего не отправляется
автоматически.

Хотите добавить устройство сами? См.
[docs/adding-a-device.md](docs/adding-a-device.md) (на английском).

<details>
<summary><strong>Что Joule делает с вашими устройствами</strong></summary>

- Он читает заряд при запуске приложения, входе в Windows, нажатии кнопки
  **Обновить**, а также при подключении или включении устройства (только этого
  устройства, не чаще раза в 10 секунд) и при открытии панели, если последнее
  чтение было более часа назад. Он никогда не опрашивает устройства в цикле.
- Для большинства устройств он только **слушает** то, что ресивер или кабель и так
  отправляют, или читает число, которое сохранила сама Windows (Bluetooth).
- Для некоторых брендов с ресивером (Logitech, Razer, SteelSeries, HyperX,
  Corsair и часть остальных) он **запрашивает** заряд только после того, как вы
  разрешите запросы для этого бренда. Это тот же запрос, что отправляют проекты
  Solaar, OpenRazer и HeadsetControl, один раз за чтение. Он никогда не
  отправляет команду, меняющую настройки. Переключатель **Только слушать** в
  **Настройках** отключает все запросы.
- Он никогда не показывает число, которое не прочитал. Нет данных — значит «нет
  данных», а не «0%».
- Он никуда ничего не отправляет. Единственное сетевое обращение — проверка
  обновлений через GitHub Releases.

Подробнее (на английском): [SECURITY.md](SECURITY.md).

</details>

<details>
<summary><strong>Сборка из исходников</strong></summary>

Нужны Windows и [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build src/BatteryTray -c Debug
```

```bash
dotnet run --project src/BatteryTray -- --demo
```

`--demo` показывает примеры устройств, не обращаясь к реальному оборудованию.
Подробнее (на английском) в [CONTRIBUTING.md](CONTRIBUTING.md) и
[docs/architecture.md](docs/architecture.md).

</details>

## Спасибо

В версию 0.2.1 вошли код и переводы от сообщества:
[@Ali-Fani](https://github.com/Ali-Fani) (Razer BlackShark V3),
[@wrldwideme](https://github.com/wrldwideme) (AULA F75, ресиверы AJAZZ и Zaopin
и русский перевод),
[@TukaLeHibou](https://github.com/TukaLeHibou) (французский перевод), а зелёный
цвет полной батареи появился благодаря идее Daniel Palma. Obrigado — и спасибо
всем, кто прислал диагностические данные.

## Поддержать Joule

Joule бесплатный и имеет открытый исходный код. Если приложение вам полезно,
можно [поддержать его разработку](https://buy.stripe.com/bJeeVe5yq5Pub7bfz8bZe00).

## Контакты

- Эл. почта: dillan.apps@gmail.com
- Instagram: [@dillan.apps](https://www.instagram.com/dillan.apps/)
- YouTube: [@dillanapps](https://www.youtube.com/@dillanapps)

## Лицензия

[AGPL-3.0](LICENSE). Сторонние лицензии и открытые проекты, документация
которых использована в Joule, перечислены в
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Joule не связан с Logitech, Razer, SteelSeries, HyperX, Corsair, Sony,
Microsoft, 8BitDo и другими упомянутыми здесь брендами. Все названия и товарные
знаки принадлежат соответствующим правообладателям.
