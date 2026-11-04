using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

/// <summary>Traditional Chinese labels for reason codes, used only in reports written for the user.</summary>
public static class ReasonLabels
{
    public static string Zh(Reason r)
    {
        var label = r.Code switch
        {
            "token_mismatch" => "簡中的佔位符／標記與目前英文不一致",
            "en_comment_mismatch" => "簡中檔的 EN 註解與目前英文不同",
            "same_folder_english_mismatch" => "簡中所在資料夾的英文與目前英文不同",
            "identical_to_english" => "簡中與英文相同（未翻譯）",
            "residual_english" => "簡中殘留英文",
            "machine_source" => "來源疑似機器翻譯",
            "key_as_english" => "英文就是鍵名本身（作者可能沒寫英文）",
            "chinese_in_english" => "英文原文含中文",
            "ambiguous_chars" => "一對多字",
            "official_term_conflict" => "官方術語有多種寫法",
            "official_wording_differs" => "官方繁中用詞不同",
            "agent_glossary_term" => "使用了 Agent 新增的術語",
            "validation_failed" => "驗證失敗",
            "old_version_folder" => "來自未載入的資料夾",
            "en_comment_match" => "EN 註解吻合",
            "same_folder_english_match" => "同資料夾英文吻合",
            "unverified" => "無可比對的英文（未驗證）",
            "origin_mod_self" => "模組自帶",
            "origin_patch" => "翻譯補丁",
            "origin_machine" => "疑似機翻來源",
            "coverage_high" => "來源覆蓋率高",
            "coverage_low" => "來源覆蓋率低",
            "official_term_applied" => "套用官方術語",
            _ => r.Code,
        };
        return r.Data.Length > 0 ? label + "：" + r.Data : label;
    }
}

/// <summary>Reports about the target language for the user (Markdown in Traditional Chinese). Read-only.</summary>
public static class LanguageReports
{
    private static IEnumerable<InstalledMod> TranslatableMods(TranslationService svc) =>
        svc.Env.ActiveMods.Where(m => m.Source != "official" && m.PackageId != PackageIds.Normalize(svc.Config.PackPackageId));

    /// <summary>
    /// Existing, active Simplified translations that no longer match the current English (Simplified mode).
    /// The pack never overwrites them; this report only lists them.
    /// </summary>
    public static string Stale(TranslationService svc, int maxMods)
    {
        var sb = new StringBuilder();
        sb.Append("# 過期簡中翻譯報告\n\n");
        sb.Append("> 產生時間：").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("。遊戲 ").Append(svc.Env.VersionFull).Append("。目標語言 ").Append(svc.Config.TargetLanguage).Append("。\n");
        if (svc.Config.IsTraditional)
        {
            sb.Append("\n此報告只適用於簡體模式（target_language = ChineseSimplified）。\n");
            return sb.ToString();
        }
        sb.Append("> 列出「目前遊戲會顯示」的簡中條目中，與目前英文對不上的部分。翻譯包**不會**覆蓋它們（見 docs/LANGUAGE_MODE_PLAN.md 第 2.2 節）。\n\n");
        var rows = new List<(InstalledMod Mod, List<StaleTranslation> Items)>();
        foreach (var mod in TranslatableMods(svc).Take(maxMods))
        {
            var scan = svc.Scan(mod);
            if (scan.AlreadyTranslated.Count == 0)
            {
                continue;
            }
            var stale = svc.Simplified.StaleExisting(scan);
            if (stale.Count > 0)
            {
                rows.Add((mod, stale));
            }
        }
        var all = rows.SelectMany(r => r.Items).ToList();
        sb.Append("## 摘要\n\n| 項目 | 數量 |\n|---|---|\n");
        sb.Append("| 有過期條目的模組 | ").Append(rows.Count).Append(" |\n");
        sb.Append("| 佔位符／標記不一致（遊戲中可能顯示錯誤） | ").Append(all.Count(s => s.Freshness == Freshness.TokenMismatch)).Append(" |\n");
        sb.Append("| EN 註解或同資料夾英文不同（內容可能過時） | ").Append(all.Count(s => s.Freshness == Freshness.Stale)).Append(" |\n\n");
        sb.Append("佔位符不一致較嚴重：例如英文新增了 `{0}`，舊譯文少了它，遊戲會顯示不完整的文字。\n\n");
        sb.Append("## 依模組\n\n| 模組 | 佔位符不一致 | 內容過時 | 譯文來源 |\n|---|---|---|---|\n");
        foreach (var (mod, items) in rows.OrderByDescending(r => r.Items.Count(i => i.Freshness == Freshness.TokenMismatch)).ThenByDescending(r => r.Items.Count))
        {
            var providers = string.Join("、", items.Select(i => i.Source.Origin == SimplifiedOrigin.ModSelf ? "模組自帶" : i.Source.Provider.About.Name).Distinct().Take(3));
            sb.Append("| ").Append(Esc(mod.About.Name)).Append(" | ").Append(items.Count(i => i.Freshness == Freshness.TokenMismatch))
              .Append(" | ").Append(items.Count(i => i.Freshness == Freshness.Stale)).Append(" | ").Append(Esc(providers)).Append(" |\n");
        }
        sb.Append("\n## 範例（佔位符不一致，最多 20 筆）\n\n");
        foreach (var s in all.Where(s => s.Freshness == Freshness.TokenMismatch).Take(20))
        {
            sb.Append("- **").Append(Esc(svc.Env.Find(s.Entry.PackageId)?.About.Name ?? s.Entry.PackageId)).Append("** `").Append(Esc(s.Entry.InjectionPath)).Append("`\n");
            sb.Append("  - 英文：").Append(Quote(s.Entry.Source)).Append("\n");
            sb.Append("  - 簡中：").Append(Quote(s.Source.Record.Text)).Append("\n");
        }
        return sb.ToString();
    }

    /// <summary>Missing entries of every active mod for the target language.</summary>
    public static string Coverage(TranslationService svc)
    {
        var rows = new List<(InstalledMod Mod, int Total, int Existing, int Ambiguous, int InPack, int Missing, int Candidates, int MissingChars, int InTarget)>();
        foreach (var mod in TranslatableMods(svc))
        {
            ModScan scan;
            try
            {
                scan = svc.Scan(mod);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                continue;
            }
            var total = scan.Entries.Count;
            if (total == 0)
            {
                continue;
            }
            var translatable = scan.Translatable.ToList();
            var store = svc.StoreFor(mod.PackageId);
            var inPack = translatable.Count(e => store.Records.ContainsKey(e.Id));
            var candidates = svc.Simplified.Suggest(scan).Count(s => !store.Records.ContainsKey(s.Entry.Id));
            var missingChars = translatable.Where(e => !store.Records.ContainsKey(e.Id)).Sum(e => e.Source.Length);
            rows.Add((mod, total, scan.AlreadyTranslated.Count, scan.Ambiguous.Count, inPack, translatable.Count - inPack, candidates, missingChars, scan.SourceInTarget.Count));
        }
        var sb = new StringBuilder();
        sb.Append("# 翻譯缺漏統計\n\n");
        sb.Append("> 產生時間：").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("。遊戲 ").Append(svc.Env.VersionFull)
          .Append("。目標語言 **").Append(svc.Config.TargetLanguage).Append("**。範圍：啟用中的非官方模組（含翻譯補丁提供的譯文）。\n\n");
        var fullyEnglish = rows.Where(r => r.Existing == 0 && r.Ambiguous == 0 && r.InTarget == 0).ToList();
        var partial = rows.Where(r => r.Missing > 0 && !(r.Existing == 0 && r.Ambiguous == 0 && r.InTarget == 0)).ToList();
        var done = rows.Where(r => r.Missing == 0).ToList();
        sb.Append("## 摘要\n\n| 項目 | 模組數 | 條目數 |\n|---|---|---|\n");
        sb.Append("| 有可翻譯文字的模組 | ").Append(rows.Count).Append(" | ").Append(rows.Sum(r => r.Total)).Append(" |\n");
        sb.Append("| 已有譯文（含補丁） | — | ").Append(rows.Sum(r => r.Existing)).Append(" |\n");
        sb.Append("| 原文本身已是目標語言（例如中文作者直接寫在 Defs） | — | ").Append(rows.Sum(r => r.InTarget)).Append(" |\n");
        sb.Append("| 路徑寫法可能衝突、保守略過 | — | ").Append(rows.Sum(r => r.Ambiguous)).Append(" |\n");
        sb.Append("| 已完整（無缺漏） | ").Append(done.Count).Append(" | — |\n");
        sb.Append("| 部分缺漏（已有部分譯文或中文原文） | ").Append(partial.Count).Append(" | ").Append(partial.Sum(r => r.Missing)).Append(" |\n");
        sb.Append("| 完全沒有譯文（純英文） | ").Append(fullyEnglish.Count).Append(" | ").Append(fullyEnglish.Sum(r => r.Missing)).Append(" |\n");
        sb.Append("| **待翻譯合計** | — | **").Append(rows.Sum(r => r.Missing)).Append("** |\n");
        sb.Append("| 其中可用既有簡中候選（未啟用補丁、舊版本資料夾等） | — | ").Append(rows.Sum(r => r.Candidates)).Append(" |\n\n");
        sb.Append("份量參考：待翻譯條目的英文原文共約 ").Append(rows.Sum(r => r.MissingChars).ToString("N0")).Append(" 個字元。\n\n");
        sb.Append("## 缺漏最多的 40 個模組\n\n| 模組 | 條目 | 已有譯文 | 缺漏 | 簡中候選 |\n|---|---|---|---|---|\n");
        foreach (var r in rows.OrderByDescending(r => r.Missing).Take(40))
        {
            sb.Append("| ").Append(Esc(r.Mod.About.Name)).Append(" | ").Append(r.Total).Append(" | ").Append(r.Existing).Append(" | ").Append(r.Missing).Append(" | ").Append(r.Candidates).Append(" |\n");
        }
        return sb.ToString();
    }

    private static string Quote(string s) => "`" + s.Replace("`", "'").Replace("\n", "\\n") + "`";
    private static string Esc(string s) => s.Replace("|", "\\|").Replace("\n", " ");
}
