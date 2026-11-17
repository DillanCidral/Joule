# 貢獻指南



[English](CONTRIBUTING-en.md) · [Português](CONTRIBUTING.pt-BR.md)



對本專案最有幫助的貢獻方式就是新增裝置支援：您可以透過軟體內建的 **「回報我的裝置」** 寄送封包擷取紀錄（無需撰寫任何程式碼），或是親自為專案撰寫新的 Provider（請參閱 [docs/adding-a-device.md](docs/adding-a-device.md)）。



## 建置與測試



您需要 Windows 作業系統與 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。請在專案儲存庫的根目錄下執行以下指令：



```

dotnet build src/BatteryTray -c Debug

```



```

dotnet run --project tools/diag -c Release -- --selftest

```



```

dotnet run --project src/BatteryScan -c Release -- --selftest

```



上述兩項 `--selftest` 指令皆不會存取任何實體硬體。每當修改 `Core`、`Interop`、`Providers` 或 `BatteryScan` 中的任何程式碼後，請務必執行這兩項自我測試。



若要在沒有連接實體硬體的情況下預覽使用者介面：



```

dotnet run --project src/BatteryTray -- --demo

```



## 提交 Pull Request 前的注意事項



- 請先詳閱 \[SECURITY.md](SECURITY.md)。任何包含無窮讀取迴圈、開啟系統滑鼠或鍵盤集合，或是在 `Interop/HidRequest.cs` 以外之處對裝置進行寫入操作的變更，皆不會被合併。

- 手邊沒有實體硬體可供測試？請在提交時明確說明，該裝置將會被標記為「尚未實機測試（not tested）」並列入支援清單。

- 若參考了其他開源專案的實作：請僅擷取通訊協定格式並自行撰寫程式碼。若需使用型號對照表等資料，請務必先確認其授權條款，並在程式碼註解及 `THIRD-PARTY-NOTICES.md` 中標明出處。

- 切勿將封包擷取紀錄、裝置序號、藍牙位址、使用者名稱或電腦名稱提交至版本庫中。

- 原始程式碼中的註解主要為葡萄牙文，且命名空間仍保留專案最初的名稱 `BatteryTray`。新增程式碼時使用英文註解即可。



## 多國語言翻譯



每種語言在 `src/BatteryTray/Core/` 目錄下皆有獨立的檔案（例如 `French.cs`、`RussianTranslations.cs`、`TraditionalChinese.cs`），並以英文原文作為鍵值。若遇到字典中尚未收錄的新字串，介面會自動顯示英文，因此新功能不會破壞既有翻譯。



- 新增語系或大規模翻譯更新需由該語言的母語人士撰寫，或在合併前經過母語人士審閱。

- 若您在翻譯過程中使用了機器翻譯工具，請在 Pull Request 中註明。在母語人士完成審閱前，該翻譯仍可先行合併並標記為機器翻譯。

- 我們也非常歡迎您一併提供對應語言的說明文件（`README.<語系代碼>.md`）。



## 授權條款



向本專案提交任何貢獻，即代表您同意您的貢獻內容將依據 [AGPL-3.0](LICENSE) 授權條款釋出。

