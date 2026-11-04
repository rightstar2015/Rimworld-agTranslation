# 階段 3：遊戲內橋接模組（設計，待確認）

> 狀態：第 7 節的決定已確認（全部照建議），A／B／C1／D 已實作，等待遊戲內驗收（第 8 節）。
> 依據：反編譯 1.7.4998 的 Assembly-CSharp（ilspycmd）、本機 Waylith 的 `RimWorld.runtimeconfig.json`、你的 `Player.log`（2026-11-04 15:22 那次啟動）。

## 1. 對照實際環境確認的事實

| 項目 | 確認結果 | 對設計的影響 |
|---|---|---|
| 模組組件的編譯目標 | 本機 2,787 個模組 DLL：net472／net48 約 2,200 個，**net10.0 有 154 個**，包括 Harmony、HAR、Vanilla Expanded Framework、Dubs 系列的 `1.7/Assemblies` | 橋接模組直接用 **net10.0** 編譯，不需要 .NET Framework 參考套件；與 MCP Server 同一套 SDK |
| Waylith 載入設定 | `AssemblyNameRedirects` 把 `UnityEngine*` 導向 `UnityStubs.dll`；開啟 `LaxMemberSignatures`、`LaxCallvirt` 等寬鬆繫結 | 編譯時參考遊戲資料夾的 `Assembly-CSharp.dll` 與 `UnityStubs.dll`（**不隨模組發布**）。寬鬆繫結可能讓錯誤呼叫靜默失敗，所以反射取得的成員找不到時一律停用該功能並記錄 |
| 主執行緒每幀呼叫 | 日誌中其他模組的 `MonoBehaviour.Update` 由 `Waylith.UnityCompat.UnityWorld.InvokeMessage` 正常呼叫 | 用一個 `MonoBehaviour` 在主執行緒消化佇列，**不需要 Harmony** |
| 模組進入點 | `Verse.Mod(ModContentPack)` 建構子、`SettingsCategory()`、`DoSettingsWindowContents(Rect)` 皆為 public | 設定頁直接用遊戲原生的模組設定視窗 |
| 載入完成時機 | `LongEventHandler.ExecuteWhenFinished(Action)`：沒有進行中的長事件時立即執行，否則排到結束後 | 在遊戲資料全部載入後才產生報告 |
| 目前語言的 Keyed 資料 | `LanguageDatabase.activeLanguage.keyedReplacements`（public 字典），每筆 `KeyedReplacement` 有 `value`、`fileSourceFullPath` | 可以逐條確認「遊戲實際用的是哪個檔案的哪段文字」 |
| 目前語言的 DefInjected 資料 | `activeLanguage.defInjections`（每個 Def 類型一個 `DefInjectionPackage`），其中每筆 `DefInjection` 有 `injection`、`injected`、`fileSource`（public）；實際寫入 Def 的 `SetDefFieldAtPath` 是 private | 可以確認每筆是否注入成功、來自哪個檔案；熱重載 DefInjected 需要呼叫 private 方法 |
| 遊戲內建的語言重載 | `LanguageDatabase.SelectLanguage` = `PlayDataLoader.ClearAllPlayData()` + `LoadAllPlayData()`，等於重新載入全部遊戲資料 | 官方路徑的成本與重開遊戲相近，不算「快速重載」 |

## 2. 量測：重開遊戲的成本

以你這次的日誌為準（401 個模組）：

| 階段 | 時間 |
|---|---|
| 程式啟動 → 語言資料載入完成 | 15:22:53 → 15:24:33（1 分 40 秒） |
| → 主選單（各模組初始化完成） | 約 15:24:53（共約 **2 分鐘**） |
| 讀取存檔 | 另計（你從主選單操作到開始讀檔約 18 秒，讀檔本身未量） |

每改一次譯文要看效果，至少要花 2 分鐘以上加上讀檔時間。這是重載唯一能省下的成本。

## 3. 重要發現：參考專案的模組正在你的環境中執行

- 「AI 智能漢化網絡（`Auto.AITranslation.Core`）」**有啟用**（第 37 位）。日誌顯示它在執行期注入譯文（`ApplyNativeStartupPayload`、`TranslationSelfRepairHooks.AfterFinalLanguageInjection`），來源是 Mods 資料夾裡的 `!Translation_AI_Pack`（該包本身沒有在模組清單中啟用）。
- 這次測試的兩個模組，AI 包裡也有 28 筆譯文：18 筆與我們一字不差，10 筆不同（例如設定頁「Skipdoor Access Mode」：我們是「跳跃门使用权限：」，AI 包是「跳跃门访问模式：」；Hediff「skip-shock」：我們「跳跃休克」，AI 包「相位震荡」）。我們另有 54 筆是 AI 包沒有的（含 FemaleBB 全部 13 筆）。
- 影響：
  1. 階段 1 的畫面驗收對「只有我們有」的條目是成立的（例如「BB 体型支持」），但對兩邊都有的條目無法區分來源。
  2. 若它在我們之後注入，同一條目會顯示它的譯文、蓋掉我們的。這不是錯誤，但使用者會以為我們的翻譯沒生效。
  3. 因此橋接模組最有價值的功能是：**逐條確認遊戲實際顯示的是誰的譯文**，不再依賴人工看畫面。
- 我們不會停用、修改或干擾它；只偵測並在報告中列出。

## 4. 建議範圍

### A. 執行期報告（核心功能，預設開啟）
遊戲資料載入完成後，在背景執行緒寫出一份報告：
- 實際語言（`activeLanguage.folderName`）、實際載入的模組與順序、翻譯包是否載入。
- 翻譯錯誤數（與日誌中「has N errors」同一算法）。
- **翻譯包逐條驗證**（讀翻譯包自己的 `.agent-manifest.json`）：
  - Keyed：遊戲中的值是否等於我們的譯文、且 `fileSourceFullPath` 指向翻譯包。
  - DefInjected：`injected` 是否為真、`fileSource` 是否為我們的檔案；簡單路徑（`defName.label`、`defName.description` 等）再直接讀 Def 欄位比對實際值。
  - 每筆結果為 `ok`／`overridden_by_other`（被別人蓋掉，附來源檔名）／`not_injected`／`unknown`。
- MCP Server 讀取這份報告：`get_environment` 的 `bridge` 欄位顯示摘要，`validate_pack` 加上逐條的執行期結果。

### B. 遊戲內頁面（預設開啟，唯讀）
在「模組設定」新增「Agent Translation」頁：各模組的譯文數、驗證通過數、被覆蓋數、未注入數，以及報告產生時間。不提供任何會寫檔或改變遊戲狀態的按鈕（重載除外，見 C）。

### C. 重載（實驗性，預設關閉）
| 方案 | 內容 | 風險 | 建議 |
|---|---|---|---|
| C0 不做 | 每次重開遊戲（約 2 分鐘以上） | 無 | — |
| C1 只重載 Keyed | 重讀翻譯包的 Keyed 檔，寫回 `keyedReplacements` | 低：多數 UI 每次繪製時才 `Translate()`，會立即生效；少數在啟動時快取字串的地方不會更新 | **建議** |
| C2 再加 DefInjected | 透過反射呼叫 private `SetDefFieldAtPath`，再清 Def 的快取 | 中：`LabelCap` 等快取、已生成物件的描述、檢視面板文字可能不一致；private 方法在 Waylith 更新時可能改名 | 暫不做，等 C1 用過再評估 |

C1 由設定頁的按鈕或 MCP 的 `request_reload(scope="keyed")` 觸發；設定頁明確標示「實驗性」。

### D. 通訊通道
- 共享資料夾：`%LOCALAPPDATA%\RimWorldAgentTranslation\bridge\`（固定位置；測試時可用環境變數 `RWAT_BRIDGE_DIR` 改位置）。
  - `outbox/runtime.json`：遊戲寫、伺服器讀。
  - `inbox/*.json`：伺服器寫、遊戲讀；只接受固定種類（`refresh_report`、`reload_keyed`），每檔 ≤ 64 KB、每次最多 16 檔，格式不符或有未知欄位一律移到 `inbox/rejected/` 並只記錄原因。
- 收件匣請求**沒有任何路徑、型別名稱、方法名稱欄位**；橋接模組讀寫的檔案位置全部寫死。
- 不開任何網路埠。

## 5. 打包與安裝

- 原始碼：`src/AgentTranslation.Bridge`（net10.0）。只參考遊戲資料夾的 `Assembly-CSharp.dll`、`UnityStubs.dll`（編譯時參考，不複製），不用 Harmony，沒有其他相依套件。
- 所有遊戲 API 呼叫集中在 `GameApi.cs`（依 DESIGN_NOTES「目標環境與遊戲 API 隔離」）。
- 安裝：`rwat install-bridge` 把編譯好的模組複製到 `Mods\!AgentTranslation_Bridge`，同樣以 manifest 管理、只動自己的檔案。
- 檔案 I/O 一律在背景執行緒；主執行緒每幀處理的工作有時間上限（例如 2 毫秒），報告產生分批完成，不卡遊戲。

## 6. 安全性對照（規格第 2 點）

| 要求 | 做法 |
|---|---|
| 不接受遊戲外指令寫任意路徑 | 收件匣只有列舉種類、無路徑欄位；寫入位置寫死在 `bridge\outbox` |
| 收件匣格式與大小檢查 | 64 KB／16 檔上限、嚴格 JSON、未知欄位即拒絕，不合格移到 `rejected` 並只記原因 |
| localhost 通道 | 不做；若日後要做，依規格綁 127.0.0.1、每次啟動隨機 token、驗 Host／Origin |
| 日誌不記錄個人資料 | 報告只含 packageId、鍵名、檔名與狀態；不含存檔名稱、殖民者名稱或 Windows 使用者名稱（路徑以相對於遊戲或模組資料夾表示） |
| 不阻塞主執行緒 | 見第 5 節 |

## 7. 需要你決定

1. **橋接模組獨立成一個模組**（建議）還是放進翻譯包裡？
   - 獨立：翻譯包維持純 XML，可以分享給別人；橋接可選擇裝或不裝。代價是多一個模組要啟用。
   - 放進翻譯包：只要啟用一個模組；但翻譯包變成帶程式碼的模組，分享時對方也會一併執行它。
2. **重載範圍**：建議 C1（只重載 Keyed，預設關閉）。
3. **執行期驗證結果放在既有工具**（`get_environment`、`validate_pack`，建議）還是新增一個 `get_runtime_report` 工具？前者不增加工具數量；後者回傳內容較集中。
4. **參考專案模組的處理**：建議只偵測並在報告與設定頁列出「哪些條目被它覆蓋」，不做任何干預。

## 8. 實作內容（2026-11-04）

| 部分 | 檔案 | 說明 |
|---|---|---|
| 橋接模組 | `src/AgentTranslation.Bridge/` | net10.0；只參考遊戲的 `Assembly-CSharp.dll`、`UnityStubs.dll`（不複製）；不用 Harmony、沒有套件相依。輸出到 `artifacts/bridge-mod/!AgentTranslation_Bridge` |
| 遊戲 API 隔離 | `GameApi.cs` | 所有遊戲型別只在這個檔案出現，且只用 public 成員（清單寫在檔案開頭） |
| 收件匣格式 | `InboxRequest.cs` | 伺服器與橋接共用同一份程式；只有 `schema`、`kind`、`request_id` 三個欄位，多一個欄位就拒絕 |
| 翻譯包讀取 | `PackReader.cs` | 只讀 manifest 列出、且位於翻譯包資料夾內的檔案；禁止 DTD |
| 伺服器 | `BridgeChannel.cs`、`TranslationService.Bridge.cs` | `get_environment.bridge`、`validate_pack.runtime_check`、MCP 工具 `request_reload(scope="keyed")` |
| CLI | `rwat install-bridge`、`bridge-status`、`bridge-refresh`、`reload` | 安裝橋接只能用 CLI：它是會執行程式碼的模組，不讓 Agent 觸發 |

報告產生時機：主選單停留 10 秒後一次、進入遊戲 10 秒後一次、收到請求或按設定頁按鈕時。逐條比對在主執行緒分批進行（每批 200 筆，每幀最多 2 毫秒），讀寫檔案都在背景執行緒。

偏離原設計的一點：`GameApi` 直接引用遊戲型別編譯，而不是全部用反射。理由是只用 public 成員、型別檢查能在編譯時發現錯誤；所有呼叫仍集中在 `GameApi.cs` 一個檔案。

測試：新增 22 個（收件匣格式 15 個、翻譯包讀取 4 個、伺服器通道 2 個，以及路徑逃逸與 DTD）。全部 99 個測試通過。

## 9. 遊戲內驗收步驟

1. 在模組清單啟用「Agent Translation Bridge」（位置不限），重新啟動遊戲。
2. 在主選單停留至少 10 秒。
3. 打開「選項 → 模組設定 → Agent Translation」，畫面應顯示上次報告時間與各項數字（82 筆、已生效數等）。
4. 告訴我，我會讀取報告與日誌（`[AgentTranslation.Bridge]` 開頭的訊息），確認逐條驗證結果。
5. （可選，實驗性重載）在同一頁勾選「允許不重啟遊戲重新載入 Keyed 譯文」。之後我改一筆 Keyed 譯文並重建翻譯包，送出重載請求，你在不重開遊戲的情況下確認文字是否改變。