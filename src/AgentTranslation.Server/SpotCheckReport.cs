using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

/// <summary>
/// Spot-check report of Simplified -> Traditional conversion (for the user, so the report text is Traditional Chinese):
/// samples active mods that have Simplified but no Traditional, listing English, Simplified, result, tier and reasons.
/// Reads files only; writes no translation.
/// </summary>
public static class SpotCheckReport
{
    public static string Generate(TranslationService svc, int count, int maxMods)
    {
        var env = svc.Env;
        var zh = svc.Chinese;
        var memory = svc.Simplified;
        var all = new List<SimplifiedSuggestion>();
        var scannedMods = new List<(InstalledMod Mod, int Translatable, int WithSc)>();

        foreach (var mod in env.ActiveMods)
        {
            if (scannedMods.Count >= maxMods)
            {
                break;
            }
            if (mod.Source == "official" || mod.PackageId == PackageIds.Normalize(svc.Config.PackPackageId))
            {
                continue;
            }
            var langs = env.LoadFoldersFor(mod).Select(f => Path.Combine(f, "Languages")).Where(Directory.Exists)
                .SelectMany(Directory.GetDirectories).Select(Path.GetFileName).ToList();
            if (langs.Any(l => l!.StartsWith("ChineseTraditional", StringComparison.Ordinal)) || !langs.Any(l => l!.StartsWith("ChineseSimplified", StringComparison.Ordinal)))
            {
                continue;
            }
            var scan = svc.Scan(mod);
            var suggestions = memory.Suggest(scan);
            scannedMods.Add((mod, scan.Translatable.Count(), suggestions.Count));
            all.AddRange(suggestions);
        }

        var picked = Pick(all, count);
        var sb = new StringBuilder();
        sb.Append("# 簡轉繁抽查報告\n\n");
        sb.Append("> 產生時間：").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("。遊戲 ").Append(env.VersionFull).Append("。");
        sb.Append("全部為**建議**，尚未寫入任何譯文。\n\n");
        sb.Append("## 摘要\n\n");
        sb.Append("| 項目 | 數值 |\n|---|---|\n");
        sb.Append("| 抽查的模組（有簡中、無繁中） | ").Append(scannedMods.Count).Append(" |\n");
        sb.Append("| 這些模組的待翻譯條目 | ").Append(scannedMods.Sum(m => m.Translatable)).Append(" |\n");
        sb.Append("| 找得到簡中的條目 | ").Append(all.Count).Append(" |\n");
        foreach (SuggestionTier t in Enum.GetValues(typeof(SuggestionTier)))
        {
            sb.Append("| ").Append(TierName(t)).Append(" | ").Append(all.Count(s => s.Tier == t)).Append(" |\n");
        }
        sb.Append("| 其中含一對多字（OpenCC 有其他候選） | ").Append(all.Count(s => s.Conversion.AmbiguousSegments.Any())).Append(" |\n");
        sb.Append("| 簡中記憶池：提供簡中的模組／條目 | ").Append(memory.ProviderCount).Append(" / ").Append(memory.RecordCount).Append(" |\n");
        sb.Append("| 官方術語：同字異體修正（用於轉換）／其中官方有多種寫法 | ").Append(zh.Glossary.Count).Append(" / ").Append(zh.Glossary.Values.Count(g => g.Conflicting)).Append(" |\n");
        sb.Append("| 官方用詞不同（只在整段相同時提示，不替換） | ").Append(zh.LabelHints.Count).Append(" |\n\n");
        var topAmbiguous = all.SelectMany(s => s.Conversion.AmbiguousSegments.Select(a => a.Source + "→" + a.Result + "（另有 " + string.Join("/", a.Alternatives) + "）").Distinct())
            .GroupBy(x => x).OrderByDescending(g => g.Count()).Take(15).ToList();
        if (topAmbiguous.Count > 0)
        {
            sb.Append("### 最常見的一對多字（依出現條目數）\n\n| 字 | 條目數 |\n|---|---|\n");
            foreach (var g in topAmbiguous)
            {
                sb.Append("| ").Append(g.Key).Append(" | ").Append(g.Count()).Append(" |\n");
            }
            sb.Append('\n');
        }

        sb.Append("## 抽查樣本（").Append(picked.Count).Append(" 筆）\n\n");
        int n = 0;
        foreach (var s in picked)
        {
            n++;
            sb.Append("### ").Append(n).Append(". ").Append(TierName(s.Tier)).Append(" — ").Append(Esc(s.Entry.InjectionPath)).Append("\n\n");
            sb.Append("- 模組：").Append(Esc(env.Find(s.Entry.PackageId)?.About.Name ?? s.Entry.PackageId)).Append("（").Append(EntryKindNames.ToId(s.Entry.Kind)).Append(s.Entry.DefType == null ? "" : " / " + s.Entry.DefType).Append("）\n");
            sb.Append("- 英文：").Append(Quote(s.Entry.Source)).Append("\n");
            sb.Append("- 簡中：").Append(Quote(s.Source.Record.Text)).Append("　來源：").Append(Esc(SourceLabel(s))).Append("\n");
            sb.Append("- 轉換：").Append(Quote(s.Conversion.Text)).Append("\n");
            sb.Append("- 可信度：").Append(s.Trust).Append("　新鮮度：").Append(FreshName(s.Freshness)).Append("\n");
            if (s.Reasons.Count > 0)
            {
                sb.Append("- 原因：").Append(Esc(string.Join("；", s.Reasons.Select(ReasonLabels.Zh)))).Append("\n");
            }
            if (s.TrustSignals.Count > 0)
            {
                sb.Append("- 訊號：").Append(Esc(string.Join("；", s.TrustSignals.Select(ReasonLabels.Zh)))).Append("\n");
            }
            sb.Append('\n');
        }

        sb.Append("## 抽查的模組\n\n| 模組 | 待翻譯 | 有簡中 |\n|---|---|---|\n");
        foreach (var (mod, tr, sc) in scannedMods)
        {
            sb.Append("| ").Append(Esc(mod.About.Name)).Append(" | ").Append(tr).Append(" | ").Append(sc).Append(" |\n");
        }
        return sb.ToString();
    }

    /// <summary>Picks by tier quota, round-robin across mods, with diverse reasons in the review tier.</summary>
    private static List<SimplifiedSuggestion> Pick(List<SimplifiedSuggestion> all, int count)
    {
        var quotas = new Dictionary<SuggestionTier, int>
        {
            [SuggestionTier.Direct] = (int)Math.Round(count * 0.4),
            [SuggestionTier.Review] = (int)Math.Round(count * 0.35),
        };
        quotas[SuggestionTier.Retranslate] = count - quotas[SuggestionTier.Direct] - quotas[SuggestionTier.Review];
        var picked = new List<SimplifiedSuggestion>();
        foreach (var (tier, quota) in quotas)
        {
            var pool = all.Where(s => s.Tier == tier && s.Entry.Source.Length is >= 4 and <= 200).ToList();
            if (tier == SuggestionTier.Review)
            {
                // One sample per reason kind first.
                foreach (var key in new[] { "ambiguous_chars", "official_term_conflict", "official_wording_differs", "residual_english", "machine_source", "old_version_folder", "validation_failed", "key_as_english" })
                {
                    var hit = pool.FirstOrDefault(s => !picked.Contains(s) && s.Reasons.Any(r => r.Code == key));
                    if (hit != null && picked.Count(p => p.Tier == tier) < quota)
                    {
                        picked.Add(hit);
                    }
                }
            }
            var byMod = pool.Where(s => !picked.Contains(s)).GroupBy(s => s.Entry.PackageId).Select(g => new Queue<SimplifiedSuggestion>(g)).ToList();
            while (picked.Count(p => p.Tier == tier) < quota && byMod.Any(q => q.Count > 0))
            {
                foreach (var q in byMod)
                {
                    if (q.Count > 0 && picked.Count(p => p.Tier == tier) < quota)
                    {
                        picked.Add(q.Dequeue());
                    }
                }
            }
        }
        // Fill up from other tiers when one tier is short.
        foreach (var s in all)
        {
            if (picked.Count >= count)
            {
                break;
            }
            if (!picked.Contains(s))
            {
                picked.Add(s);
            }
        }
        return picked.OrderBy(s => s.Tier).ToList();
    }

    private static string SourceLabel(SimplifiedSuggestion s)
    {
        var o = s.Source.Origin switch { SimplifiedOrigin.ModSelf => "模組自帶", SimplifiedOrigin.Patch => "補丁「" + s.Source.Provider.About.Name + "」", _ => "疑似機翻「" + s.Source.Provider.About.Name + "」" };
        var folder = s.Source.Folder.Length == 0 ? "根目錄" : s.Source.Folder;
        return o + "，" + folder + (s.Source.CurrentFolder ? "" : "（未載入）") + (s.Source.Record.EnglishComment != null ? "，有 EN 註解" : "");
    }

    private static string TierName(SuggestionTier t) => t switch { SuggestionTier.Direct => "直接採用", SuggestionTier.Review => "交給 Agent 校對", _ => "重新翻譯" };
    private static string FreshName(Freshness f) => f switch { Freshness.Confirmed => "已確認", Freshness.Unverified => "未驗證", Freshness.Stale => "過期", _ => "佔位符不一致" };
    private static string Quote(string s) => "`" + s.Replace("`", "'").Replace("\n", "\\n") + "`";
    private static string Esc(string s) => s.Replace("|", "\\|").Replace("\n", " ");
}
