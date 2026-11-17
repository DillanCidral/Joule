\# 新增支援裝置



\[English](adding-a-device.md) · \[Português](adicionar-um-aparelho.md)



在 Joule 中，每款裝置的支援皆由一個 \*\*Provider\*\* 負責：這是一個獨立的類別，負責定義該裝置回應的 USB 識別碼（VID/PID），以及如何將原始資料報告解析為電量讀數。應用程式的其他模組無需知道它的內部細節。



動手開發前，請務必先閱讀 \[SECURITY.md](../SECURITY.md)，其中的安全規範絕不容妥協。



\## 1. 確認裝置回報電量的方式



\- \*\*Windows 系統本身已可顯示電量\*\*（位於「設定 > 藍牙與裝置」）：無需進行任何開發，內建的 Bluetooth Provider 已能直接讀取。

\- \*\*已有開源專案記錄其通訊協定\*\*（例如 Solaar、OpenRazer、HeadsetControl、OpenMouse、rivalcfg 等）：請僅參考其\*\*封包格式\*\*並自行撰寫程式碼，同時在註解中標明資料來源。若需複製型號對照表等資料，請務必先確認其開源授權條款。

\- \*\*尚無任何公開文件記錄\*\*：請自行擷取封包。在操作裝置的同時執行封包收集器，觀察哪個位元組（Byte）會隨著電量變化：



```

dotnet run --project src/BatteryScan -c Release

```



請注意：輸出結果會包含裝置序號與藍牙位址等隱私資訊，切勿將原始擷取紀錄直接提交（Commit）至版本庫。



\## 2. 選擇對硬體最溫和的讀取方式



| 若該裝置… | 請使用 | 參考範例 |

| --- | --- | --- |

| 會自行發送電量，或在原廠軟體查詢時順帶發送 | `PassiveReportListener`（僅限被動監聽） | `RapooProvider`、`AttackSharkProvider` |

| 會持續不斷串流發送資料報告 | `HidOneShot.WaitForReport`（開啟、讀取一筆後隨即關閉） | `DualSenseProvider` |

| 僅在收到查詢指令時才會回應 | `HidRequest`（專案唯一的寫入通道） | `LogitechProvider`、`SteelSeriesProvider` |

| 不屬於 HID 集合（例如 XInput 插槽、Windows 裝置屬性） | `IStandaloneProvider` | `XInputProvider`、`BluetoothPnpProvider` |



如果該裝置的通訊協定要求「必須反覆發送查詢直到兩次回應一致」，請立即停止開發：本專案不收錄此類裝置。



\## 3. 撰寫 Provider 類別



請在 `src/BatteryTray/Providers/` 目錄下建立類別。一個純被動監聽的 Provider 結構非常精簡，範例如下：



```

public sealed class AcmeProvider : IBatteryProvider, IPassiveListenerProvider, IDisposable

{

&#x20;   private const ushort Vid = 0x1234, Pid = 0x5678;



&#x20;   private readonly PassiveReportListener \_listener = new(

&#x20;       "Acme",

&#x20;       c => c.Vid == Vid \&\& c.Pid == Pid \&\& c.UsagePage == 0xFF00 \&\& c.InputLen == 7,

&#x20;       ParsePercent);



&#x20;   public string Name => "Acme";

&#x20;   public IReadOnlyList<(ushort Vid, ushort Pid)> SupportedIds { get; } = new\[] { (Vid, Pid) };

&#x20;   public string LogicalKey(ushort vid, ushort pid) => "acme:m1";

&#x20;   public string DisplayName(ReadContext ctx) => "Acme M1";



&#x20;   public void EnsureListening(IReadOnlyList<HidCollection> collections) => \_listener.Ensure(collections);

&#x20;   public void StopListening() => \_listener.Stop();

&#x20;   public bool IsListening => \_listener.IsListening;

&#x20;   public event Action? ValueChanged

&#x20;   {

&#x20;       add => \_listener.ValueChanged += value;

&#x20;       remove => \_listener.ValueChanged -= value;

&#x20;   }

&#x20;   public void Dispose() => \_listener.Dispose();



&#x20;   public BatteryReading Read(ReadContext ctx)

&#x20;   {

&#x20;       if (!ctx.IsPresent(Vid, Pid)) return BatteryReading.Disconnected();

&#x20;       \_listener.Ensure(ctx.AllCollections);

&#x20;       return \_listener.Last is { } last

&#x20;           ? BatteryReading.Percentage(last.Percent, observedUtc: last.SeenUtc)

&#x20;           : BatteryReading.NoData("waiting for the device to report");

&#x20;   }



&#x20;   // 請務必驗證封包特徵碼（Signature），而非僅檢查長度；不確定時請回傳 null。

&#x20;   internal static int? ParsePercent(byte\[] r) =>

&#x20;       r.Length >= 7 \&\& r\[0] == 0xBB \&\& r\[6] <= 100 ? r\[6] : null;

}

```



撰寫 `Read` 與解析器（Parser）的開發規範：



\- 對讀取到的數值沒有把握？請回傳 `NoData` 並附上簡短原因。絕不猜測數值，也絕不回傳 `0`。

\- 使用傳輸線連接且無法取得電量百分比時：請回傳 `BatteryReading.Charging()`。

\- 同一款裝置的無線接收器與有線連接模式，應共用相同的 `LogicalKey`（邏輯鍵值）。

\- 若必須主動查詢：每次讀取僅發送一筆查詢並等待回應，且一律透過 `HidRequest` 執行。專案中其他任何檔案皆不可直接對裝置進行寫入。



\## 4. 註冊 Provider



將新建的 Provider 加入 `src/BatteryTray/Core/ProviderRegistry.cs` 檔案中的 `ProviderRegistry.CreateDefault()` 方法內。



\## 5. 測試解析器



在 `tools/diag/SelfTest.cs` 中為您的解析器新增單元測試案例（包含正常報告、長度過短的報告、特徵碼錯誤的報告，以及數值大於 100 的異常報告），並執行以下指令進行驗證：



```

dotnet run --project tools/diag -c Release -- --selftest

```



\## 6. 更新說明文件



請將新支援的裝置補入 \[supported-devices.md](supported-devices.md) 與 \[aparelhos-suportados.md](aparelhos-suportados.md) 清單中。若尚未在實體硬體上驗證過，請將其歸類於「尚未實機測試（Not tested）」區塊。若封包格式參考自其他開源專案，請務必將該專案列入根目錄的 `THIRD-PARTY-NOTICES.md` 中。

