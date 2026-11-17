# 支援裝置清單

[English](supported-devices.md) · [Português](aparelhos-suportados.md)

**已實測確認（Checked）** 代表已在實體裝置上完成電量讀取驗證。**尚未實機測試（Not tested）** 代表依據公開通訊協定文件編寫支援模組，但尚未有人在真實硬體上回報驗證結果。如果您擁有「尚未實機測試」清單中的裝置，歡迎透過開啟 [GitHub Issue](https://github.com/DillanCidral/Joule/issues) 或使用 Joule 內建的 **「回報我的裝置」** 功能告訴我們是否能正常運作。

「被動監聽（listens）」代表 Joule 不會向裝置發送任何指令；「主動查詢（asks）」代表每次讀取時會向裝置發送一次電量查詢指令（請參閱 [SECURITY.md](../SECURITY.md)）。

## 已實測確認

| 裝置名稱 | 連線方式 | 讀取方式 | 備註 |
| --- | --- | --- | --- |
| Attack Shark R1 | 無線接收器 | 被動監聽 | 滑鼠喚醒並移動時會自動更新電量；滑鼠靜止時不會發送任何訊號 |
| Attack Shark R1 | 傳輸線 | 連線狀態偵測 | 顯示「充電中」，不顯示電量百分比 |
| Rapoo MT760 Mini | 無線接收器 | 被動監聽 | 僅在 Rapoo 原廠軟體運行時才會回報電量；其餘時間 Joule 會顯示最後一次紀錄的數值與時間 |
| Razer BlackShark V3 | 無線接收器 | 主動查詢 | 每次讀取時查詢一次（已於實機驗證）；在兩次查詢之間會保留耳機自行回報的數值與時間 |
| DualSense (PS5) | 僅限有線連接 | 被動監聽 | 透過藍牙連線時，控制器需由主機端寫入指令才會回報電量，因此目前排除藍牙路徑 |
| Xbox 控制器 | 無線轉接器、傳輸線 | XInput | 使用無線轉接器時顯示粗略電量（低／中／高）；有線連接時顯示「有線連接」 |
| 8BitDo Ultimate 2C | 傳輸線 | XInput | 僅顯示「有線連接」，無百分比數值。透過接收器連接時會回報錯誤數值，Joule 已將其過濾捨棄 |
| 任何藍牙裝置 | 藍牙 | 讀取 Windows 系統資訊 | 顯示與 Windows「設定 > 藍牙與裝置」相同的電量數值，不發送任何無線電查詢封包 |
| Zaopin ZPW MAX (8K) | `3554:F527` 接收器 | 主動查詢 | 已於實體硬體確認可正確讀取電量百分比 |
| AULA F75 | `258A:010C` 傳輸線 | 連線狀態偵測 | 顯示「有線連接」，不會顯示猜測的電量百分比 |
| AJAZZ 2.4G 8K | `3151:402D` GearHub-V5 接收器 | 主動查詢 | 接收器狀態碼 `F7`；已於實體硬體確認可正確讀取電量百分比 |

## 經使用者回報確認

依據公開文件編寫，並已由使用者傳送封包擷取紀錄證實 Joule 可在其實際電腦上讀取電量（2026 年 11 月更新）。

| 裝置名稱 | 連線方式 |
| --- | --- |
| Logitech LIGHTSPEED 滑鼠（PRO X 2 等型號） | LIGHTSPEED 接收器 |
| Logitech MX Master 2S | Unifying 接收器 |
| SteelSeries Arctis 1 Wireless 與 Arctis 7 | 無線接收器 |
| VXE R1 Pro Max | `3554:F58A` 接收器 |
| VGN IO Aurora 與 Aurora Pro 8K | `3554:FB08` 與 `3554:FB40` 接收器 |

## 尚未實機測試

| 品牌 | 裝置型號 | 讀取方式 |
| --- | --- | --- |
| Logitech | 採用 Unifying、Bolt、Lightspeed 與 Nano 接收器的裝置；耳機型號包含 G533、G535、G633、G635、G733、G933、G935、G PRO、PRO X | 主動查詢（HID++ 2.0） |
| Razer | 71 款無線／有線滑鼠、13 款無線鍵盤、Nari 耳機 | 主動查詢（Feature Report） |
| SteelSeries | Arctis 1、7、7+、Nova 3P / 5 / 7 / Pro、GameBuds；Aerox 3 / 5 / 9、Prime、Rival 3、Rival 650 | 主動查詢 |
| HyperX | Cloud II Wireless、Cloud Alpha Wireless、Cloud Alpha 2 Wireless、Cloud Flight；Pulsefire Dart | 主動查詢 |
| Corsair | 22 款採用 Slipstream 接收器的滑鼠與鍵盤；Void、Void Wireless V2、HS80 Max、Virtuoso Max、Virtuoso XT / SE | 主動查詢（僅讀取） |
| Attack Shark | 與 R1 共用相同接收器的 X6、X11、X11 SE、Delux M600 Pro | 被動監聽 |
| Attack Shark | R2（GearHub-V5 接收器）、R5 Ultra、R6、R8 | 主動查詢 |
| ATK、VXE、VGN、Pulsar、Dareu、Hitscan | 採用 Compx/Beken 晶片的「Report 8」通訊協定裝置（共 192 款 ATK 型號） | 主動查詢 |
| AULA | F75（`3554:FA09` 接收器） | 僅向接收器查詢電量 |
| G-Wolves | xvi 系列世代與 HTX Ultra | 主動查詢 |
| Lamzu、LunaFury、CRDRAKO、Vancer、RAWM、WLmouse | 同系列通訊協定家族 | 主動查詢 |
| AJAZZ | NJ07、NJ08 | 主動查詢 |
| K-Snake / Noir | X11、M2-NEX | 主動查詢 |
| Rapoo | VT9 Pro | 讀取 Feature 欄位，不發送查詢 |
| Lingbao | M5 Pro（GearHub-V5 接收器；亦包含配對的鍵盤） | 主動查詢 |
| Bytech、Motospeed X6、Keychron 滑鼠 |  | 主動查詢 |
| Incott | 無線接收器與傳輸線 | 被動監聽 |

## 已知支援缺口

- **Logitech**：HID++ 1.0 舊款裝置、「Centurion」系列耳機（G PRO X 2 Lightspeed、G522、Astro A50），以及有線連接模式下的 Logitech 裝置。
- **Razer**：其他未列出的耳機型號。
- **SteelSeries**：Arctis 9、Arctis Pro Wireless。
- **HyperX**：其他 Pulsefire 系列滑鼠、無線鍵盤。
- **Corsair**：Slipstream V2 接收器、Katar Pro Wireless、Sabre V2 Pro，以及有線連接模式下的裝置。
- **MCHOSE、Ninjutso 以及 Rapoo VT9 其餘型號**：其通訊協定需要反覆發送查詢直到兩次回應一致，基於安全原則已刻意排除支援。
- 未具備 `0xFFC1` 集合的 **Keychron** 滑鼠。
- 同時連接無線接收器**與**藍牙的裝置，會在面板中顯示為兩列獨立項目。
- 同時連接兩支 DualSense 控制器時，目前僅會顯示為單一項目。