# 階段 1 報告：MCP Server 最小版

> 日期：2026-11-04。環境：RimWorld 1.7.4998（Waylith CoreCLR），本機 400 個啟用模組。

## 1. 完成內容

| 項目 | 狀態 |
|---|---|
| `AgentTranslation.Core`（netstandard2.0，零外部相依） | 完成：穩定 ID、路徑沙箱、安全 XML 讀取、token 擷取與驗證、LoadFolders 解析、Defs 繼承解析、Def／Keyed 抽取、LanguageData 寫出、既有翻譯索引 |
| `rwat`（net10.0，MCP stdio + CLI） | 完成工具：`get_environment`、`list_mods`、`scan_mod`、`get_batch`、`submit_translations`、`validate_pack`、`build_pack` |
| 離線模式 | `rwat export` / `rwat import`，與 MCP 走同一套驗證 |
| 單元測試 | 39 項全部通過 |
| MCP stdio 實測 | 以標準函式庫寫的最小用戶端（`tests/smoke/`）完成 initialize → tools/list → 呼叫；annotations 正確回傳 |
| 端到端（兩個測試模組） | 43 筆譯文經驗證後寫入翻譯包；**遊戲內載入尚待你實測**（見 §5） |

依賴（版本鎖定，`packages.lock.json`）：`ModelContextProtocol` 2.3.1、`Microsoft.Extensions.Hosting` 10.0.0；測試專用 xunit 2.9.3。

## 2. 對照遊戲組件確認的事實（1.7.4998 Assembly-CSharp，以 ILSpy 反組譯閱讀，未複製程式碼）

| 主題 | 確認結果 | 對設計的影響 |
|---|---|---|
| 語言資料夾名稱 | `LoadedLanguage` 同時接受完整名稱 `ChineseTraditional (繁體中文)` 與括號前的舊名稱 `ChineseTraditional` | 翻譯包用 `ChineseTraditional` |
| Keyed 載入 | 依模組載入順序讀取，同鍵後者覆蓋（`SetOrAdd`）；同檔重複鍵報錯；值 `TODO` 視為占位；字面 `\n` 轉換行 | 翻譯包排在後面即可生效；仍不輸出已有翻譯的鍵 |
| DefInjected 重複鍵 | 所有模組共用一個表；同鍵再次出現會記錄 **"Duplicate def-injected translation key"** 錯誤；整串清單（`<li>`）重複時用 `Add` 會丟例外 | **已有翻譯的條目一律不輸出**，不依賴覆蓋順序 |
| DefInjected 路徑 | `defName.欄位.索引.欄位`；清單可用數字索引或 handle；用索引時遊戲只給「建議改用 handle」的提示，不算錯誤 | 一律輸出索引路徑 |
| 可翻譯欄位 | `[NoTranslate]`／`[Unsaved]` 不可翻；`[MustTranslate]` 必翻；`[MayTranslate]` 不列為缺漏；其餘字串「含空白」才列為缺漏 | 抽取器以組件中繼資料（System.Reflection.Metadata，不執行程式碼）判斷，規則相同 |
| DefInjected 資料夾 | 資料夾名稱對應型別名稱，找不到時再試去掉結尾一個字元 | 以 Defs 根元素名稱（如 `ThingDef`）命名 |
| Defs 繼承 | 父節點取「載入順序 ≤ 自身」中最大者；子節點文字取代、`li` 附加、同名元素遞迴合併、`Inherit="False"` 整個取代 | 自行實作同樣語意；測試模組的抽象父 Def 描述正確繼承到 4 個子 Def |
| 版本資料夾 | 無 `LoadFolders.xml` 時載入 `根目錄/1.7` → `Common` → 根目錄；1.7 已把 `default` 版本標為過時 | 翻譯包只放 `1.7/`，**不輸出 LoadFolders.xml**（修正階段 0 的 D5 細節） |
| 標記語法 | `ColoredText.TagRegex = \([\*\/][^\)]*\)`（例如 `(*Threat)`）、`XMLRegex = <[^>]*>` | 驗證器加入 `(*Tag)` 類標記 |
| 設定檔位置 | 與官方版相同（LocalLow），`Logs/Player.log` 為遊戲日誌 | 環境資訊不需要橋接器即可取得 |

仍未確認：大括號 token 的完整語法、`Def` 節點本身帶 `Class` 屬性時歸屬哪個 DefDatabase、同 defName 重複定義時的取用規則、Patches 對文字與清單索引的影響。程式碼註解皆已標記。

## 3. 測試模組結果

| 模組 | 條目 | 結果 |
|---|---|---|
| Outfit Stands Plus | Keyed 18、Def 14（含 4 個子 Def 繼承自抽象父節點的 description，以及繼承自 Core `MinifiedThing` 的 label） | 經 **離線模式**（export/import）提交，32 筆全部接受 |
| Prisoners Should Fear Turrets | Keyed 9、Def 2（`stages.0.label` 清單索引路徑） | 經 **MCP**（get_batch/submit_translations）提交；刻意漏掉 `{0}` 的一筆被拒（`placeholder_mismatch`），修正重送後接受 |

翻譯包輸出：`F:\SteamLibrary\steamapps\common\RimWorld\Mods\!AgentTranslation_Pack`，8 個語言檔 + About.xml + README.txt。重複建置寫入 0 個檔案（冪等）。

## 4. 過程中發現並修正的問題

1. **XML 空白節點蓋掉繼承內容**：讀檔時保留了純空白文字節點，合併繼承時被當成「子節點有文字」而清空父節點內容，導致 ThingDef 全部漏抽。改為與遊戲相同的「不保留純空白」，並加防護。
2. **暫定簡體字清單誤收繁簡同形字**（修、械、器、存、源…）：第一次匯入有 19 筆被誤拒。已刪除這些字並加回歸測試。這份清單是暫時的，階段 2 會換成 OpenCC 字典產生的集合。

## 5. 需要你做的遊戲內驗收

1. 遊戲語言切換為「繁體中文」（目前是簡體中文）。
2. 在模組清單啟用「Agent Translation Pack (繁體中文)」，放在兩個被翻譯模組之後（About.xml 已寫 loadAfter，自動排序會處理）。
3. 重啟遊戲，確認：
   - 模組設定頁（Outfit Stands Plus、Prisoners Should Fear Turrets）顯示中文；
   - 研究「機械化服裝架」「修補服裝架」、建築「修補服裝架」等名稱與描述為中文；
   - 開發者模式的日誌沒有與這兩個模組或翻譯包相關的紅字。
4. 告訴我結果後，我會讀 `Logs/Player.log` 比對與翻譯相關的錯誤（例如 "Duplicate def-injected"、"Couldn't inject"、"Translation data for language … has N errors"）。

## 6. 已知限制（留待後續階段）

| 限制 | 計畫 |
|---|---|
| 掃描一個模組約 20 秒（需讀取 400 個模組的既有繁中翻譯與官方 Defs） | 階段 2：檔案指紋快取 |
| 簡體字偵測只是暫定清單 | 階段 2：OpenCC 字典（Apache-2.0，附 NOTICE） |
| 沒有翻譯記憶與術語表；`memory` 欄位為空 | 階段 2 |
| 不處理 Strings、RulePack、SlateRef、英文 DefInjected 覆蓋、Patches 新增的文字 | 階段 2 評估 |
| 官方語言包（`.tar`）內的鍵未納入「已有翻譯」判定 | 階段 2 |
| `build_pack` 只支援完整建置 | 階段 2 |
| `list_mods` 不含覆蓋率（需逐一掃描） | 階段 2 快取後提供 |
