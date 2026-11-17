\# 系統架構



\[English](architecture.md) · \[Português](arquitetura.md)



Joule 是一個獨立封裝（Self-contained）的 `win-x64` 單一執行檔（基於 .NET 8 與 WPF），無需系統管理員權限即可執行。



\## 架構分層



```

src/BatteryTray/

&#x20; Core/       電量讀取模型、Provider 註冊表、快取、設定與紀錄檔

&#x20; Interop/    Windows API 介接：HID、SetupAPI（藍牙）、XInput

&#x20; Providers/  各硬體系列的獨立讀取類別

&#x20; Ui/         主面板、螢幕邊緣小工具、系統匣圖示、初次導覽、「回報我的裝置」視窗

src/BatteryScan/   JouleScan：封包擷取診斷工具；主程式共用了此模組的程式碼

tools/diag/        診斷工具與自動化自我測試（Self-test）

```



`Core`、`Interop` 與 `Providers` 皆不依賴使用者介面層。相同的電量讀取核心同時運行於主應用程式、`JouleScan` 以及 `tools/diag` 中。



程式碼中的命名空間（Namespace）與資料夾名稱仍保留專案最初的名稱 `BatteryTray`。



\## 電量讀取流程



1\. `HidInventory.Scan` 列舉目前系統中存在的 HID 集合（Collections）。由於此掃描會向裝置查詢字串描述符，因此僅在「按需讀取（On-demand reading）」時執行。

2\. `ProviderRegistry` 將已知的 `VID:PID` 配對分派給對應的 Provider。獨立運作的 Provider（如 XInput、藍牙，以及需要主動發送查詢的品牌）則會自行從同一份清單中探索所屬裝置。

3\. 每個 Provider 皆會回傳一個 `BatteryReading` 結構：包含百分比數值、粗略電量等級、「充電中」狀態，或附帶原因說明的「無資料」。

4\. 沒有對應 Provider 的裝置會被標示為「不支援（unsupported）」，這類裝置不會從清單消失，也絕不會顯示猜測的電量數值。

5\. 每個裝置最後一次成功讀取的數值會快取於磁碟中；當後續讀取回傳空值時，介面會顯示該快取數值並附上經過時間。



\## 按需讀取與兩項即時資料來源



觸發電量讀取的時機共有四種：應用程式啟動、登入 Windows、手動點擊「重新整理」，以及 Windows 通知有新裝置連線。針對最後一項，程式會先等待 1.5 秒讓裝置完成初始化，隨後僅讀取該新連線的裝置，且每 10 秒最多觸發一次。 低電量警示會在每次讀取結束時進行評估，而非透過背景看門狗（Watchdog）持續輪詢。



唯有不會主動干擾硬體的機制才會保持即時運作：



\- \*\*被動監聽（Passive listening）\*\*（適用於 Attack Shark、Rapoo）：僅開啟單一 HID 控制代碼（Handle），接收裝置主動發送的資料報告。若監聽器中斷，不會在按需讀取之外的時間點自動重新開啟。

\- \*\*藍牙（Bluetooth）\*\*：Joule 每 60 秒重新讀取一次 Windows 已儲存在裝置節點中的電量百分比，完全不會透過無線電波發送額外封包。



即時讀取會直接沿用上一次完整掃描所取得的 HID 集合。



\## 單一寫入通道



必須向裝置發送查詢指令才能取得電量的 Provider，一律透過 `Interop/HidRequest.cs` 執行——這是整個專案中唯一會對裝置進行寫入操作的模組。該模組會拒絕存取系統滑鼠與鍵盤集合、每次僅發送一筆查詢並等待回應，且單次工作階段最多限制發送 48 筆查詢。詳細規範請參閱 \[SECURITY.md](../SECURITY.md)。



\## 資料儲存、安裝與更新



\- 應用程式資料儲存於 `%LOCALAPPDATA%\\Joule\\` 目錄下，包含 `settings.json`（設定檔）、`state.json`（快取狀態）與 `joule.log`（紀錄檔）。

\- 安裝程式與自動更新機制採用 \[Velopack](https://github.com/velopack/velopack)。應用程式會在啟動時及每小時檢查一次 GitHub Releases。

\- 防止重複啟動：若嘗試開啟第二個程式執行個體，它會通知正在執行的主程式顯示面板並隨即結束。

