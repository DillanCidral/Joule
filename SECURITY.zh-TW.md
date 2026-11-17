\# 安全性與隱私權



\[English](SECURITY.md) · \[Português](SECURITY.pt-BR.md)



Joule 會與您日常依賴的硬體裝置進行通訊。在開發初期，一次過於頻繁的 HID 讀取迴圈曾導致滑鼠與耳機從 USB 斷線數小時之久。以下安全規範正是為此而生，並由程式碼強制執行。



\## 程式碼強制執行的安全規則



| 規則 | 實作位置 |

|---|---|

| 僅在需要時讀取：應用程式啟動、登入 Windows、點擊「重新整理」按鈕，以及 Windows 通知有新裝置連線時（僅讀取該裝置，且每 10 秒最多一次）。無背景輪詢迴圈、無定時器 | `Core/ReadingService.cs` |

| 僅開啟單一控制代碼（Handle）並持續讀取，結束時僅呼叫\*\*一次\*\* `CancelIoEx` | `Interop/HidPassiveReader.cs` |

| 絕不開啟系統滑鼠（`0x01`/`0x02`）或系統鍵盤（`0x01`/`0x06`）的 HID 集合 | `HidInventory.IsSystemReserved` |

| 絕不對沒有輸入報告（Input reports）的集合執行阻塞式讀取 | `HidPassiveReader.Open` |

| 操作過程中若裝置中斷連線：立即停止對該裝置進行任何存取 | `HidPassiveReader` 迴圈 |

| 僅透過單一通道進行寫入：只發送開源專案已驗證使用的電量查詢指令，每次僅發送一筆並等待回應，單次讀取對所有裝置合計最多發送 48 筆查詢 | `Interop/HidRequest.cs`（專案中唯一呼叫 `WriteFile` 的位置） |

| 尚未在實體硬體上驗證過的品牌，在獲得您的明確許可前不會發送任何查詢（裝置首次出現時會跳出提示，或可於面板點擊 \*\*「允許」\*\*）。授權以品牌為單位，且隨時可在「設定」中撤銷 | `HidRequest`（同上單一通道） |

| 在「設定」中開啟 \*\*「僅限被動監聽」\*\* 後，將停用所有主動查詢（包含已允許的品牌） | `HidRequest` |

| 曾被回報出現異常行為的型號會列入封鎖清單，即使已授權也絕不發送查詢 | `HidRequest.Blocked` |

| 應用程式與 JouleScan 中除了自動更新器外不含任何網路連線程式碼，且更新器僅接受本官方儲存庫 | 由 `tools/diag --selftest` 驗證 |

| HID 掃描會向裝置查詢字串描述符，因此絕不透過定時器自動執行 | `Interop/HidInventory.cs` |

| 絕不憑空捏造數值。沒有讀到資料時一律顯示「無電量資料」，絕不顯示為「0%」 | 所有 Provider |



在兩次讀取之間保持即時更新的，僅限完全不會主動接觸硬體的來源：已開啟的被動監聽器，以及 Windows 系統本身儲存的藍牙電量百分比。將滑鼠游標移至螢幕邊緣小工具上方絕不會觸發硬體讀取。



\## Joule 會向裝置傳送什麼資料



\- \*\*僅限被動監聽\*\*：Attack Shark、Rapoo、Incott、DualSense、XInput 控制器與藍牙裝置（藍牙裝置僅讀取 Windows 系統記錄的數值，不會透過無線電波發送任何封包）。

\- \*\*主動查詢電量\*\*：Logitech、Razer、SteelSeries、HyperX、Corsair、採用 Compx 通訊協定的品牌（ATK、VXE、VGN、Pulsar、Dareu）及部分其他品牌。程式僅發送讀取電量的查詢指令，絕不會傳送任何更改硬體設定的指令。

\- 需要反覆發送查詢直到兩次回應一致的通訊協定，已基於安全原則刻意排除支援。



\## 隱私權保護



\- 無遙測（Telemetry）、無帳號系統、無任何後端伺服器。唯一的網路連線僅用於更新器檢查 GitHub Releases 是否有新版本。

\- Joule 僅會將資料寫入 `%LOCALAPPDATA%\\Joule\\` 目錄：`settings.json`（設定檔）、`state.json`（各裝置最後電量快取）與 `joule.log`（紀錄檔）。

\- \*\*回報我的裝置（Send my device）\*\* 會在移除序號、藍牙位址、電腦名稱、使用者名稱以及非周邊類型的藍牙裝置（手機、喇叭、車載系統）後，將 `.txt` 報告儲存於 `%LOCALAPPDATA%\\Joule\\envios\\`。您可以在送出前親自檢視檔案內容，並自行決定是否寄出。請注意，透過 GitHub Issue 提交的內容為公開資訊。



\## 若裝置出現異常行為



如果您在開啟 Joule 後發現裝置卡頓、從 USB 斷線或運作異常：



1\. 關閉 Joule（在系統匣圖示上按右鍵 > 結束），然後重新插拔無線接收器。

2\. 通知我們：請開啟 \[GitHub Issue](https://github.com/DillanCidral/Joule/issues) 或寄信至 dillan.apps@gmail.com，並附上裝置型號、`joule.log`（可從系統匣圖示 > 資料夾開啟）以及當時同時開啟的原廠控制軟體名稱。



若確認異常是由主動查詢的 Provider 所引起，我們會優先在新版本中停用該裝置的查詢功能，隨後再進行調查。



\## 回報安全性漏洞



請直接寄信至 dillan.apps@gmail.com，切勿公開建立 GitHub Issue。

