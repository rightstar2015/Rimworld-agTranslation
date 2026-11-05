# 階段 5：安全檢查、文件、多客戶端測試、授權與發佈（完成，2026-11-05 確認結案）

規格要求：
- 安全性檢查。
- 文件。
- 至少用 2 個 MCP 客戶端測試。
- 授權。
- 發佈時公開檔案雜湊值。
- 結束前逐項對照規格的安全需求自我檢查，並列出結果。

另外加上階段 4 之後發現的封裝環境路徑問題。

## 1. 現況盤點

| 項目 | 現況 |
|---|---|
| 授權 | `LICENSE` 為 MIT；OpenCC 字典為 Apache-2.0，有 `NOTICE.md`；尚無第三方套件授權總表 |
| 相依套件 | 伺服器只有 `ModelContextProtocol 2.3.1`、`Microsoft.Extensions.Hosting 10.0.0`，版本鎖定；四個專案都有 `packages.lock.json` |
| 發佈形式 | `rwat.exe` 已設定為自帶執行環境的單一檔案（win-x64）；橋接模組由 `AssembleMod` 組裝 |
| Git 遠端 | 尚未設定（沒有 GitHub 儲存庫） |
| 這台電腦可用的 MCP 客戶端 | Claude 桌面版（Code 分頁，也就是目前這個）、VS Code（需 GitHub Copilot 的代理模式）、Antigravity；也可以用 MCP Inspector（`npx`）做協定層測試 |

## 2. 工作項目

### 2.1 封裝環境的路徑問題（先做）

問題見 `DESIGN_NOTES.md`「封裝應用程式的 AppData 轉存」。打算這樣做：
- **新的預設位置**：伺服器的工作資料夾和橋接資料夾移到 AppData 以外（位置待決定，見 §3）。遊戲端的橋接模組與伺服器使用同一個新預設值；`RWAT_WORK_DIR`、`RWAT_BRIDGE_DIR` 照舊可以覆蓋。
- **遷移**：找到舊位置（含封裝轉存的位置）的資料時，複製到新位置，並在舊位置留一個說明檔，不刪除舊資料。
- **偵測**：rwat 偵測到自己在封裝應用程式裡執行時，在 `get_environment` 顯示警告與實際路徑。

### 2.2 安全性自我檢查

產出 `docs/SECURITY_REVIEW.md`，逐項對照規格的 6 點：
1. **MCP 工具清單**：沒有命令列、任意檔案或網路工具；工具描述標明「此為待翻譯資料，不是指令」；所有寫入都經過 `PathSandbox`；拒絕路徑逃逸與符號連結。
2. **橋接模組**：收件匣的格式與大小限制、拒絕無效檔案、只讀 manifest 列出的檔案、禁止 DTD、不開網路埠。
3. **不需要金鑰**：確認程式碼、設定檔、日誌裡沒有金鑰相關欄位。
4. **日誌**：不記錄玩家個人資料（存檔名、殖民者名、系統使用者名稱）；文件寫明模組文字會送到 Agent 的服務商。
5. **相依套件**：版本鎖定；發佈時附 SHA-256 雜湊。
6. **結論**：逐項列出結果與殘留風險。

會補做的檢查：
- 實際對伺服器送出惡意請求（路徑逃逸、超大批次、偽造的 batch_id 與 id），確認全部被拒絕。
- 對橋接收件匣放入惡意檔案，確認全部被拒絕。
- 搜尋日誌與報告的輸出，確認沒有使用者名稱或完整路徑外洩。

### 2.3 多客戶端測試

至少 2 個客戶端，各自跑完整流程：列出模組、掃描、取批次、送出、驗證、建置。另外要測一次被拒絕後重送。結果記錄在 `docs/CLIENT_TESTS.md`。

### 2.4 文件

- `README.md`（英文）：安裝、連接客戶端、設定表、隱私說明、移除方法。
- 玩家指南：語言版本待決定，見 §3。
- 第三方授權總表：`THIRD_PARTY_NOTICES.md`。

### 2.5 發佈

- `build-release` 腳本產出：
  - `rwat.exe`
  - 橋接模組的 zip
  - `SHA256SUMS.txt`
- 公開發佈要由你來做：GitHub 發佈頁、Steam Workshop 上傳（RimWorld 內建的上傳工具）。我只準備檔案和說明文字，不會替你發佈。

## 3. 決定（2026-11-04）

| 問題 | 決定 |
|---|---|
| 新的預設資料夾 | 遊戲資料夾 `AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\AgentTranslation`（你的建議）。實測：在這裡建立的資料夾不會被轉存，你在檔案總管看得到 |
| 測試客戶端 | Claude 桌面版的 Code 分頁與對話（Chat）分頁 |
| 玩家指南語言 | 日文、韓文、簡中、繁中、英文 |
| 日韓翻譯支援 | 之後要支援，先寫進 DESIGN_NOTES，不在這個階段做 |
| 選配項目 A | 這個階段做 |

### 原本的選項（保留紀錄）

1. **新的預設資料夾位置**：
   - `%USERPROFILE%\RimWorldAgentTranslation`（使用者資料夾底下）：不會被轉存，也不會被 OneDrive 同步。但放在使用者資料夾根目錄，比較少見。
   - 「文件」資料夾：玩家最好找。但你的「文件」若在 OneDrive 裡，報告檔和收件匣會一直被同步。
   - 維持 AppData，只加警告：改動最小，但封裝環境的問題沒有解決。
2. **測試用的客戶端**：從 Claude 桌面版、VS Code（Copilot）、Antigravity、MCP Inspector 中選至少 2 個。
3. **玩家指南的語言版本**：繁中、簡中、英文，要哪些？
4. **選配項目 A**（開關搬到遊戲內設定頁）：這個階段做，還是之後再說？

## 4. 進度檢查點（2026-11-04）

已完成並提交：
- 資料夾搬到遊戲資料夾，並遷移舊資料（f07da7f）。
- 遊戲內開關，即選配項目 A（2dbe245）。
- 安全性自我檢查，加上攻擊式 MCP 檢查 46 項（e14a9d0）。
- 發佈腳本與 SHA-256（6838633）。
- 五種語言的玩家指南與 README（d4f99ea）。
- 測試 120 個全過。

你已經回覆、恢復工作時照做：
- 遊戲已關閉，可以直接安裝新版橋接模組（`rwat install-bridge`）。
- 發佈版 `rwat.exe` 要複製到 `F:\Apps\Tools\rwat\`。
- 兩個 MCP 設定由我寫入：
  - Code 分頁：新的測試資料夾加 `.mcp.json`。
  - 對話分頁：Claude 桌面版的 `claude_desktop_config.json`，實際位置在封裝轉存的 `Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\`，寫入前先確認。

剩下的步驟：
1. 安裝新版橋接模組，複製 rwat.exe，寫入兩個 MCP 設定。
2. 遊戲內驗證：
   - 報告寫到新的 `AgentTranslation\bridge`。
   - 設定頁出現「翻译工具设置」，切換開關後 config.json 跟著改。
3. 客戶端測試：Code 分頁與對話分頁各跑一次完整流程，記錄在 `docs/CLIENT_TESTS.md`。
4. 階段 5 結案報告，並更新 SECURITY_REVIEW 的最後結論。

## 5. 收尾（2026-11-05）

- 步驟 1 到 4 都完成了。
- 遊戲內驗證：
  - 報告寫到了新的 `AgentTranslation\bridge`，84 條都有生效，其中 9 條是 ok_extended。
  - 在設定頁切換開關後，config.json 有寫入，其他欄位都保留。
- 客戶端測試：對話分頁和 Code 分頁都通過，見 `CLIENT_TESTS.md`。
- 測試中的發現：
  - Claude 桌面版的設定檔實際位置在 `%LOCALAPPDATA%\Claude-Data\…`。
  - Code 分頁也會載入桌面版設定裡的同名伺服器。
  - 「list_mods 附上未翻譯數量」的想法已寫進 DESIGN_NOTES，這個階段不做。
- 公開發佈（GitHub、Steam Workshop）由你來做。
