# RimWorld Agent Translation（開發中，階段 1）

讓支援 MCP 的 AI Agent 為 RimWorld 1.7 模組產出繁體中文翻譯包。本工具不呼叫任何 AI 服務、不需要 API Key；翻譯由你使用的 Agent 完成，本工具負責抽取、驗證與寫出。

> 被翻譯的模組文字會被送到你所選 Agent 的服務供應商。

## 建置

```bash
dotnet test
dotnet publish src/AgentTranslation.Server -c Release
```

## 在 MCP Client 中使用

指令：`rwat serve`（stdio）。範例（Claude Code）：

```bash
claude mcp add rimworld-translation -- "C:\path\to\rwat.exe" serve
```

## 離線模式

```bash
rwat export <packageId> --out batch.json
rwat import batch-result.json
rwat build
```

設定檔：`%LOCALAPPDATA%\RimWorldAgentTranslation\config.json`（環境變數 `RWAT_WORK_DIR` 可改位置）。

文件：`docs/STAGE0_PROPOSAL.md`（架構）、`DESIGN_NOTES.md`（設計規則）、`docs/STAGE1_REPORT.md`（目前進度）。

授權：MIT。設計理念參考 RimWorld-Auto-AI-Translation-Core（未使用其程式碼）。
