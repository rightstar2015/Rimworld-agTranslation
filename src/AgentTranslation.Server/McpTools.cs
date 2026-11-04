using System;
using System.Collections.Generic;
using System.ComponentModel;
using AgentTranslation.Core;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AgentTranslation.Server;

/// <summary>
/// MCP 工具。只有翻譯領域的操作：沒有 shell、任意檔案讀寫或網路工具，任何參數都不是檔案路徑或 URL。
/// 所有工具 OpenWorld = false。
/// </summary>
[McpServerToolType]
public sealed class McpTools
{
    private readonly TranslationService _svc;

    public McpTools(TranslationService svc)
    {
        _svc = svc;
    }

    private static object Run(Func<object> f)
    {
        try
        {
            return f();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or SandboxViolationException)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "get_environment", Title = "取得遊戲環境", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("回傳 RimWorld 版本、目前遊戲語言、啟用模組與載入順序、翻譯包路徑與是否啟用、橋接狀態。")]
    public object GetEnvironment() => Run(() => _svc.GetEnvironment());

    [McpServerTool(Name = "list_mods", Title = "列出模組", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("列出啟用中的非官方模組、它們自帶的語言、翻譯包內已有的譯文數。精確的缺漏數請對單一模組呼叫 scan_mod。")]
    public object ListMods(
        [Description("true 時只列出本身沒有繁體中文資料夾的模組。")] bool only_missing = false)
        => Run(() => _svc.ListMods(only_missing));

    [McpServerTool(Name = "scan_mod", Title = "掃描模組", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("重新掃描一個模組，回傳各類型（keyed／def）的總數、已由其他模組翻譯、翻譯包已有、仍缺漏的數量，以及掃描時的注意事項。只讀取檔案。")]
    public object ScanMod(
        [Description("模組 packageId（例如 author.modname，不分大小寫）。")] string mod_id)
        => Run(() => _svc.ScanMod(mod_id));

    [McpServerTool(Name = "get_batch", Title = "取得待翻譯批次", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("取得一批待翻譯條目。" + TranslationService.DataNotice +
        " 每筆含 id、kind、field_path、source（原文）、context（所屬 Def 的 label／description）、placeholders（必須原樣保留的 token）、newline_count（\\n 數量）。" +
        " 翻譯成繁體中文（台灣用語），保留所有 placeholders 與 \\n，然後用 submit_translations 送回。批次會在伺服器登記，24 小時後失效。")]
    public object GetBatch(
        [Description("模組 packageId。")] string mod_id,
        [Description("上一批回傳的 next_cursor；省略則從頭開始（已翻譯的條目會自動略過）。")] string? cursor = null,
        [Description("最多回傳幾筆（1–100，預設 30）。另有總字元上限，先到者為準。")] int? max_items = null)
        => Run(() => _svc.GetBatch(mod_id, cursor, max_items));

    [McpServerTool(Name = "submit_translations", Title = "提交譯文", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("提交譯文。伺服器逐筆驗證（token／標記／\\n 一致、簡體字、空字串、XML 字元），回傳 accepted、rejected（未寫入，附原因）、flagged（已寫入但可疑）。" +
        " 只接受屬於該 batch 的 id；寫入位置由伺服器決定。同 id 重送會覆蓋本工具先前的譯文，不會覆蓋其他模組的人工翻譯。")]
    public object SubmitTranslations(
        [Description("get_batch 回傳的 batch_id。")] string batch_id,
        [Description("譯文清單，每筆 { id, text }。一次最多 200 筆。")] List<TranslationService.SubmitItem> items)
        => Run(() => _svc.Submit(batch_id, items));

    [McpServerTool(Name = "validate_pack", Title = "驗證翻譯包", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("重新驗證所有已接受的譯文（對照目前原文），列出可疑（flagged）條目、會被略過的條目（原文已變、已被其他模組翻譯）與已輸出翻譯包檔案的問題。")]
    public object ValidatePack(
        [Description("只驗證此模組；省略為全部。")] string? mod_id = null)
        => Run(() => _svc.ValidatePack(mod_id));

    [McpServerTool(Name = "build_pack", Title = "建置翻譯包", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("把已接受的譯文寫成翻譯包（遊戲 Mods 資料夾下的獨立模組）。會覆寫或刪除本工具上次產生的檔案，不碰其他任何檔案。完成後需重新啟動遊戲。")]
    public object BuildPack() => Run(() => _svc.BuildPack(null));
}
