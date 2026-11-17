Joule 是一款常駐於 Windows 11 系統匣的小型應用程式，能即時顯示所有無線周邊裝置的剩餘電量。本軟體完全免費、開放原始碼，目前處於 **Beta 測試階段**。

## 主要功能

- **主面板**：點擊系統匣圖示即可檢視所有已連線的裝置，並將電量最低的裝置排在最上方。
- **螢幕邊緣小工具**：在螢幕邊緣顯示電量圓環，具備自動隱藏機制，執行全螢幕應用程式時也會自動避讓。
- **系統匣圖示**：一眼就能看出是否有任何裝置電量過低；將滑鼠游標移至圖示上方即可查看各裝置的詳細電量。
- **連線通知**：當裝置開啟電源時，Joule 會在邊緣小工具或透過 Windows 系統通知顯示電量，並可依需求開啟音效提示。
- **低電量警示**：可自訂電量警示門檻值。
- **多國語言介面**：支援英文、葡萄牙文、法文、俄文與繁體中文。程式會自動跟隨 Windows 系統語言，也可於「設定」中手動切換。
- **充滿電綠色顯示**：支援回報充電狀態的裝置在充飽至 100% 時，邊緣小工具與系統匣圖示皆會轉為綠色。
- **更新後使用摘要**：僅在本機電腦統計 Joule 已追蹤的裝置數量與發出的低電量警告次數，並列出該版本的更新內容。

## 下載與安裝

點擊上方按鈕或前往 [Releases 頁面](https://github.com/DillanCidral/Joule/releases) 下載 `JouleApp-win-Setup.exe` 並執行安裝。

- 無需系統管理員權限。程式僅安裝於目前使用者帳戶下，並會列於 Windows 的「已安裝的應用程式」中，隨時可從該處解除安裝。
- 由於安裝程式尚未經過數位簽章，Windows SmartScreen 可能會跳出警告視窗。請點擊 **「其他資訊」**，再點選 **「仍要執行」** 即可。
- Joule 會自動透過 GitHub Releases 檢查更新，當新版本就緒時會在系統匣通知您。

## 支援裝置清單

| 裝置名稱 | 連線方式 | 狀態 |
| --- | --- | --- |
| Attack Shark R1 | 無線接收器 | ✅ 已實測確認 |
| Rapoo MT760 Mini | 無線接收器 | ✅ 已實測確認 |
| Razer BlackShark V3 | 無線接收器 | ✅ 已實測確認 |
| Zaopin ZPW MAX (8K) | 無線接收器 | ✅ 已實測確認 |
| AJAZZ 2.4G 8K 接收器 | 無線接收器 | ✅ 已實測確認 |
| AULA F75 | 有線連接（接收器尚未測試） | ✅ 已實測確認 |
| DualSense | 有線連接 | ✅ 已實測確認 |
| Xbox 控制器 | 無線轉接器與有線連接 | ✅ 已實測確認 |
| 8BitDo Ultimate 2C | 有線連接 | ✅ 已實測確認 |
| 任何已在 Windows「設定 > 藍牙與裝置」顯示電量的藍牙裝置 | 藍牙 | ✅ 已實測確認 |
| Logitech LIGHTSPEED 滑鼠（PRO X 2 等）與 Unifying 滑鼠（MX Master 2S）、SteelSeries Arctis 1 與 Arctis 7、VXE R1 Pro Max、VGN IO Aurora | 無線接收器 | ✅ 經使用者回報確認 |
| Logitech、Razer、SteelSeries、HyperX 與 Corsair：滑鼠、鍵盤與耳機麥克風 | 無線接收器 | 🧪 尚未實機測試 |
| 約 20 個其他品牌：ATK、VXE、VGN、Pulsar、Dareu、AJAZZ、Lamzu、WLmouse、G-Wolves、Hitscan、K-Snake、Bytech、Motospeed、Keychron、Incott 及更多 Attack Shark 型號 | 無線接收器 | 🧪 尚未實機測試 |

🧪 代表此支援模組係依據公開通訊協定文件編寫，尚未在實體硬體上完成驗證。若您擁有上述型號，歡迎回報測試結果協助確認。「經使用者回報確認」代表已有使用者傳送封包擷取紀錄，證實 Joule 能在其電腦上正常讀取該裝置電量。完整清單請參閱：[docs/supported-devices.md](docs/supported-devices.md)。

## 清單中沒有我的裝置

請先查詢 [追蹤中的裝置系列清單](https://github.com/DillanCidral/Joule/issues?q=is%3Aissue%20is%3Aopen%20label%3Atracking)：如果您的裝置已在其中，請直接對該 Issue 按 👍，無需重複建立新的回報。當您送出回報時，Joule 也會自動顯示相關的請求連結。

請開啟 Joule 內的 **「設定 > 進階 > 回報裝置資訊」**。程式會在您操作裝置時監聽 30 秒，向您展示偵測到的內容，並自動移除個人隱私資料（序號、藍牙位址、電腦名稱、使用者名稱）後儲存為 `.txt` 檔案。接著您可以自行將該檔案透過 [GitHub Issue](https://github.com/DillanCidral/Joule/issues/new/choose) 或 [電子郵件](#聯絡方式) 寄給我們。本程式絕不會擅自從您的電腦傳送任何資料。

想親自為專案新增裝置支援嗎？請參閱 [docs/adding-a-device.md](docs/adding-a-device.md)。

- 僅在開啟應用程式、登入 Windows、點擊 **「重新整理」**、插入或開啟裝置電源時（僅針對該裝置，且每 10 秒最多執行一次），以及開啟面板且距離上次讀取已超過 1 小時才會進行讀取，絕不會在背景無止盡地輪詢。
- 針對大多數裝置，Joule 僅會**被動監聽**接收器或傳輸線原本就會發送的訊號，或直接讀取 Windows 系統本身記錄的數值（藍牙裝置）。
- 針對部分透過接收器連接的品牌（Logitech、Razer、SteelSeries、HyperX、Corsair 及部分其他品牌），僅在您明確授權該品牌後，Joule 才會向裝置**發送電量查詢**：這與 Solaar、OpenRazer、HeadsetControl 等知名開源專案發送的查詢指令完全相同，每次讀取僅發送一次，絕不會傳送任何更改硬體設定的指令。您也可以在「設定」中開啟 **「僅限被動監聽」** 開關，停用所有主動查詢。
- 絕不顯示未實際讀取到的假數值。沒有讀到資料時一律顯示「無電量資料」，絕不會顯示為「0%」。
- 絕不向外部傳送任何遙測資料。唯一的網路連線僅用於檢查 GitHub Releases 是否有新版本。

詳細安全性規範請參閱：[SECURITY.md](SECURITY.md)。

您需要 Windows 作業系統與 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```
dotnet build src/BatteryTray -c Debug
```

```
dotnet run --project src/BatteryTray -- --demo
```

`--demo` 參數可顯示模擬的範例裝置，不會存取任何實體硬體。更多開發細節請參閱 [CONTRIBUTING.md](CONTRIBUTING.md) 與 [docs/architecture.md](docs/architecture.md)。

## 致謝

0.2.1 版本包含了來自社群貢獻者的程式碼與翻譯： 感謝 [@Ali-Fani](https://github.com/Ali-Fani)（Razer BlackShark V3 支援）、 [@wrldwideme](https://github.com/wrldwideme)（AULA F75、AJAZZ 與 Zaopin 接收器支援及俄文翻譯）、 [@TukaLeHibou](https://github.com/TukaLeHibou)（法文翻譯），以及 Daniel Palma 提出充滿電時以綠色顯示的靈感。由衷感謝每一位提供封包擷取資料的使用者。

## 贊助支持 Joule

Joule 是免費且開源的軟體。如果它對您有所幫助，歡迎請我喝杯咖啡支持專案持續開發：[贊助支持 Joule](https://buy.stripe.com/bJeeVe5yq5Pub7bfz8bZe00)。

## 聯絡方式

- 電子郵件：dillan.apps@gmail.com
- Instagram：[@dillan.apps](https://www.instagram.com/dillan.apps/)
- YouTube：[@dillanapps](https://www.youtube.com/@dillanapps)

## 授權條款

本專案採用 [AGPL-3.0](LICENSE) 授權條款。第三方授權聲明與使 Joule 得以實現的相關開源通訊協定專案清單，均列於 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

Joule 與 Logitech、Razer、SteelSeries、HyperX、Corsair、Sony、Microsoft、8BitDo 或本文提及的任何其他品牌均無隸屬關係，相關名稱與商標權皆歸其各自所有者所有。