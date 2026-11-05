# 多客戶端測試（階段 5）

規格要求：至少用 2 個 MCP 客戶端跑完整流程，包含一次被拒絕後重送。

## 測試環境

- rwat：發佈版 `F:\Apps\Tools\rwat\rwat.exe`，SHA-256 與 `SHA256SUMS.txt` 一致。
- 遊戲：RimWorld 1.7.4998（Waylith），簡體中文，啟用 400 個模組，橋接模組運作中。
- 每個客戶端各用一組獨立的資料夾（`RWAT_WORK_DIR`、`RWAT_PACK_OUTPUT_DIR`），位在 `F:\Project\rwat-client-test\` 底下。你真正的工作資料夾和遊戲 `Mods` 資料夾都不會被寫入。
- 兩邊用同一段測試提示詞：get_environment → list_mods → scan_mod → get_batch → 故意弄壞一條的佔位符後送出 → 修正重送 → validate_pack → build_pack，並要求不得修改設定。

## 1. Claude 桌面版：對話（Chat）分頁（2026-11-05，通過）

設定檔：`claude_desktop_config.json`，env 指向 `work-chat` 和 `pack-chat`。

| 步驟 | 結果 |
|---|---|
| get_environment | 目標語言 `ChineseSimplified`，來源 `game_language`，與遊戲一致；回報的資料夾路徑正確 |
| list_mods / scan_mod | Agent 掃了幾個小模組比較缺口，選定 SimpleCameraSetting（46 條 Keyed） |
| get_batch | 一次取完 46 條，`remaining: 0` |
| submit_translations（第一次） | Agent 在 `ZoomRange` 故意多加一個 `{0}`。結果 45 條接受，1 條拒絕：`placeholder_mismatch`，「Does not match the source: extra {0}.」，並提示用同一個 batch_id 修正後重送 |
| submit_translations（重送） | 接受 |
| validate_pack | `ready_entries: 46`，沒有 flagged、skipped 或 pack_problems |
| build_pack | 寫入 3 個檔案，共 46 條，`flagged: 0` |

我另外檢查了檔案：
- 46 筆記錄全部是 accepted。`ZoomRange` 的寫入時間比其他條晚 2 秒，就是重送的那一條。
- Keyed.xml 能正常解析，共 46 個元素，`<` 有跳脫；含 3 個換行的條目，換行數與原文一致。
- 只寫在 `work-chat` 和 `pack-chat`，Agent 沒有修改任何設定。

觀察：
- **設定檔的實際位置**：Claude 桌面版的使用者資料已經搬到 `%LOCALAPPDATA%\Claude-Data\claude_pzs8sxrjxfjjc\`。舊的 `%APPDATA%\Claude\` 只剩一個表示「已搬走」的標記檔。第一次寫錯位置，所以 Agent 找不到工具。玩家照 README 從「Settings > Developer > Edit Config」打開設定檔，就會開到正確的位置。
- **挑模組要逐一掃描**：list_mods 不顯示未翻譯數量，Agent 只能一個個 scan_mod 比較。這個想法已寫進 DESIGN_NOTES，還沒做。
- **試探用的批次**：Agent 挑模組時另外取了 5 個批次但沒有送出。這些批次留在 `work-chat\batches`，24 小時後過期，沒有其他影響。
- **譯文品質**：用詞自然，也沿用了官方術語（镜头、设置、小人）。

## 2. Claude 桌面版：Code 分頁（2026-11-05，通過）

在 `F:\Project\rwat-client-test` 開了新的工作階段。資料夾裡有一份 `.mcp.json`，env 指向 `work-code` 和 `pack-code`。

| 步驟 | 結果 |
|---|---|
| scan_mod | 掃了 4 個小模組，選定 Whip（`richard98.whip`），17 條 DefInjected，分屬 4 種 Def |
| get_batch / submit | 17 條全部 accepted |
| build_pack | 翻譯包裡新增 4 個 DefInjected 檔，對話分頁那次建置的 46 條 Keyed 也保留了；manifest 在同一次建置更新 |

我另外檢查了檔案：
- 6 個 XML 檔都能解析，包含 About.xml。
- DefInjected 依照 `DefInjected\<DefType>\<mod>\` 分資料夾。

觀察：
- **用的是桌面版設定，不是 `.mcp.json`**：結果寫進了 `work-chat` 和 `pack-chat`，`work-code` 和 `pack-code` 是空的。原因是 Code 分頁也會載入桌面版 `claude_desktop_config.json` 裡的伺服器，而那個伺服器和 `.mcp.json` 裡的同名（`rimworld-translation`），最後用的是桌面版那一個。
  - 不影響這次測試的有效性：兩個分頁的 Agent 執行環境不同，Code 分頁的 Agent 還有命令列權限。
  - 對玩家的建議：MCP 伺服器只在一個地方設定；如果真的要設兩份，名稱要取不同。
- **累加建置**：這次是在同一個翻譯包上做第二次建置，舊條目沒有遺失。
- **被拒絕後重送**：這次的回報原文我沒有拿到，所以無法從檔案確認。這一項已在對話分頁驗證過，見 §1。

## 結論

兩個客戶端都跑完了完整流程。被拒絕後重送、驗證、建置和累加建置都正常，所有寫入都留在指定的測試資料夾裡。
