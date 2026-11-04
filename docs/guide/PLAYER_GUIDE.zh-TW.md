# RimWorld Agent Translation 玩家指南（繁體中文）

其他語言：[简体中文](PLAYER_GUIDE.zh-CN.md) ・ [English](PLAYER_GUIDE.en.md) ・ [日本語](PLAYER_GUIDE.ja.md) ・ [한국어](PLAYER_GUIDE.ko.md)

## 這是什麼

讓你已經在用的 AI 助理（例如 Claude）幫你把 RimWorld 1.7 的模組翻成**簡體中文或繁體中文**。

- 工具本身不連線到任何 AI 服務，也不需要 API 金鑰。翻譯是由你自己的 AI 助理完成的。
- 翻譯結果是一個獨立的模組「Agent Translation Pack」，不會修改任何原模組。刪掉它，翻譯就完全移除。
- 已經有人翻好的部分（模組自帶的中文、翻譯補丁）會保留，不會被蓋掉。

> 隱私：要翻譯的模組文字會送到你所使用 AI 助理的服務商。

## 準備

- RimWorld 1.7，並且至少啟動過一次遊戲。
- 支援 MCP 的 AI 助理，例如 Claude 桌面版。

## 三個步驟

### 1. 安裝橋接模組（建議）

訂閱「Agent Translation Bridge」，在模組清單啟用，位置不限。它會：
- 在遊戲裡確認每一筆譯文是否真的生效。
- 提供翻譯工具的設定頁：「選項 → 模組設定 → Agent Translation」。

### 2. 安裝 rwat，連到你的 AI 助理

從專案的發佈頁下載 `rwat.exe`，放在固定的資料夾，例如 `D:\Tools\rwat\`。

可以用發佈頁上的 `SHA256SUMS.txt` 核對檔案有沒有被竄改。在 PowerShell 執行：

```
Get-FileHash rwat.exe -Algorithm SHA256
```

算出來的值要和 `SHA256SUMS.txt` 裡的一樣。

**Claude 桌面版（對話）**：
1. 打開「設定 → 開發者 → 編輯設定」。
2. 在設定檔加入下面的內容，`command` 改成你放 rwat.exe 的位置。
3. 存檔後重新啟動 Claude。

```json
{
  "mcpServers": {
    "rimworld-translation": { "command": "D:\\Tools\\rwat\\rwat.exe", "args": ["serve"] }
  }
}
```

**Claude Code**：執行下面這行。

```
claude mcp add rimworld-translation -- "D:\Tools\rwat\rwat.exe" serve
```

**其他支援 MCP 的助理**：設定方式相同，指令是 `rwat.exe`，參數是 `serve`。

rwat 會自己找到遊戲資料夾、Workshop 資料夾和你的模組清單。

### 3. 請助理翻譯

在遊戲裡把語言設成簡體中文或繁體中文，然後關閉遊戲一次，讓設定存檔。翻譯包的語言預設會跟著遊戲語言。

然後對助理說，例如：

> 幫我把啟用中、還沒有中文的模組翻譯好，最後建置翻譯包。

完成後重新啟動遊戲，在模組清單啟用「Agent Translation Pack」。它會自動排在被翻譯的模組之後。

## 設定（都在遊戲裡）

打開「選項 → 模組設定 → Agent Translation」：

| 設定 | 預設 | 說明 |
|---|---|---|
| 產生執行期報告 | 開 | 檢查哪些譯文實際生效，結果顯示在同一頁 |
| 不重開遊戲重新載入介面文字 | 關 | 實驗性。只更新介面文字（Keyed）；物品名稱與描述仍需重開遊戲 |
| 翻譯包語言 | 跟著遊戲語言 | 也可以固定成簡體或繁體中文 |
| 補上模組漏定義的鍵 | 關 | 有些模組的介面連英文都顯示成 `Some.Key.Name`。開啟後助理會依鍵名推測譯文，每筆都會標示為推測 |
| 繁中模式：自動採用現成的簡中譯文 | 關 | 把現成的簡中譯文轉成繁中直接使用（條件可在 config.json 調整） |

- 後三項會存進 rwat 的設定檔，重新啟動 AI 助理後生效。
- AI 助理沒辦法替你打開這些選項。
- 進階使用者也可以直接編輯設定檔：`%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\AgentTranslation\config.json`，和遊戲的 `Saves`、`Config` 在同一層。

## 常見問題

- **翻譯沒出現**：
  - 確認遊戲語言和翻譯包的語言相同。
  - 確認「Agent Translation Pack」已啟用。
  - 橋接模組的設定頁會列出哪些模組的譯文沒有生效，以及被誰覆蓋。
- **想換成另一種中文**：在遊戲裡切換語言，重新啟動 AI 助理，再請它重新建置翻譯包。
- **移除**：刪除 Mods 資料夾裡的 `!AgentTranslation_Pack`。要的話，也一併取消訂閱橋接模組，並刪除上面那個 `AgentTranslation` 資料夾。
- **譯文品質**：譯文是 AI 產生的，沒有經過人工校對。需要時可以請助理用「查詢翻譯記憶」或「詞彙表」統一用語。
