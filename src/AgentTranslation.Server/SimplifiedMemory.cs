using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

/// <summary>簡中翻譯的來源類型，依可信度由高到低。</summary>
public enum SimplifiedOrigin
{
    /// <summary>被翻譯模組自己附帶的簡中（作者或貢獻者維護）。</summary>
    ModSelf,
    /// <summary>另一個翻譯補丁模組。</summary>
    Patch,
    /// <summary>名稱或作者顯示為機器翻譯／AI 產生的翻譯包。</summary>
    Machine,
}

public sealed class SimplifiedSource
{
    public required LanguageRecord Record { get; init; }
    public required InstalledMod Provider { get; init; }
    public required SimplifiedOrigin Origin { get; init; }
    /// <summary>語言資料夾所在的子資料夾（"" 為模組根目錄，"1.4"、"Common"…）。</summary>
    public required string Folder { get; init; }
    /// <summary>該資料夾目前是否會被遊戲載入。</summary>
    public required bool CurrentFolder { get; init; }
    /// <summary>同一資料夾下的英文（若存在），用於過期偵測。</summary>
    public string? SameFolderEnglish { get; init; }
}

public enum Freshness
{
    /// <summary>EN 註解或同資料夾英文與目前英文一致。</summary>
    Confirmed,
    /// <summary>沒有可比對的證據，但 token 一致。</summary>
    Unverified,
    /// <summary>EN 註解或同資料夾英文與目前英文不同。</summary>
    Stale,
    /// <summary>佔位符／標記與目前英文不一致。</summary>
    TokenMismatch,
}

public enum SuggestionTier
{
    /// <summary>符合直接採用條件（目前仍只作為建議，需使用者開啟自動採用）。</summary>
    Direct,
    /// <summary>交給 Agent 校對。</summary>
    Review,
    /// <summary>簡中已過期，需重新翻譯（簡中只作參考）。</summary>
    Retranslate,
}

public sealed class SimplifiedSuggestion
{
    public required SourceEntry Entry { get; init; }
    public required SimplifiedSource Source { get; init; }
    public required ConversionResult Conversion { get; init; }
    public required Freshness Freshness { get; init; }
    public required SuggestionTier Tier { get; init; }
    public required int Trust { get; init; }
    public required List<string> Reasons { get; init; }
    public List<string> TrustSignals { get; init; } = new();
    public ValidationOutcome? Validation { get; init; }
}

/// <summary>
/// 全域簡中翻譯記憶：收集所有已安裝模組（含未啟用的翻譯補丁、舊版本資料夾）的 ChineseSimplified 翻譯，
/// 依 key／DefInjected 路徑對應到目前英文條目，轉成繁中並分級。
/// </summary>
public sealed class SimplifiedMemory
{
    private static readonly Regex MachineHint = new(@"(\bAI\b|machine|auto[\s\-_]*translat|gpt|deepl|google\s*translat|机翻|機翻|自动翻译|自動翻譯|机器翻译|機器翻譯)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ResidualEnglish = new(@"[A-Za-z]{4,}", RegexOptions.CultureInvariant);

    private readonly AppConfig _config;
    private readonly GameEnvironment _env;
    private readonly ChineseResources _zh;
    private readonly Dictionary<string, List<SimplifiedSource>> _byPath = new(StringComparer.Ordinal);
    private bool _built;

    public SimplifiedMemory(AppConfig config, GameEnvironment env, ChineseResources zh)
    {
        _config = config;
        _env = env;
        _zh = zh;
    }

    public int ProviderCount { get; private set; }
    public int RecordCount { get; private set; }

    private void EnsureBuilt()
    {
        if (_built)
        {
            return;
        }
        _built = true;
        foreach (var mod in _env.Mods.Where(m => m.Source != "official"))
        {
            var langDirs = FindLanguageDirs(mod.RootDir);
            if (langDirs.Count == 0)
            {
                continue;
            }
            var current = new HashSet<string>(_env.LoadFoldersFor(mod).Select(f => Path.GetFullPath(f).TrimEnd('\\')), StringComparer.OrdinalIgnoreCase);
            var origin = ClassifyProvider(mod);
            var provided = false;
            foreach (var (folderRel, languagesDir) in langDirs)
            {
                foreach (var dir in Directory.GetDirectories(languagesDir))
                {
                    var name = Path.GetFileName(dir);
                    if (!(name == "ChineseSimplified" || name.StartsWith("ChineseSimplified (", StringComparison.Ordinal)))
                    {
                        continue;
                    }
                    var folderFull = Path.GetFullPath(Path.Combine(mod.RootDir, folderRel)).TrimEnd('\\');
                    var english = ReadEnglish(Path.Combine(languagesDir, "English"));
                    foreach (var rec in LanguageFileReader.Read(LanguageFileReader.FilesInDirectory(dir)))
                    {
                        english.TryGetValue(rec.CanonicalPath, out var en);
                        var src = new SimplifiedSource
                        {
                            Record = rec,
                            Provider = mod,
                            Origin = origin,
                            Folder = folderRel,
                            CurrentFolder = current.Contains(folderFull),
                            SameFolderEnglish = en,
                        };
                        Add(rec.CanonicalPath, src);
                        if (rec.CanonicalPathSingular != null)
                        {
                            Add(rec.CanonicalPathSingular, src);
                        }
                        RecordCount++;
                        provided = true;
                    }
                }
            }
            if (provided)
            {
                ProviderCount++;
            }
        }
    }

    private void Add(string key, SimplifiedSource src)
    {
        if (!_byPath.TryGetValue(key, out var list))
        {
            list = new List<SimplifiedSource>();
            _byPath[key] = list;
        }
        list.Add(src);
    }

    private static Dictionary<string, string> ReadEnglish(string dir)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rec in LanguageFileReader.Read(LanguageFileReader.FilesInDirectory(dir)))
        {
            map[rec.CanonicalPath] = rec.Text;
        }
        return map;
    }

    /// <summary>模組內所有 Languages 資料夾（深度 ≤ 3），回傳 (所在子資料夾, Languages 路徑)。</summary>
    private static List<(string, string)> FindLanguageDirs(string root)
    {
        var result = new List<(string, string)>();
        void Walk(string dir, string rel, int depth)
        {
            var langs = Path.Combine(dir, "Languages");
            if (Directory.Exists(langs))
            {
                result.Add((rel, langs));
            }
            if (depth >= 3)
            {
                return;
            }
            foreach (var sub in Directory.GetDirectories(dir))
            {
                var n = Path.GetFileName(sub);
                if (n is "Languages" or "Defs" or "Textures" or "Sounds" or "Assemblies" or "Patches" or "About" or "Source" || n.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }
                Walk(sub, rel.Length == 0 ? n : rel + "/" + n, depth + 1);
            }
        }
        if (Directory.Exists(root))
        {
            Walk(root, string.Empty, 0);
        }
        return result;
    }

    private SimplifiedOrigin ClassifyProvider(InstalledMod mod)
    {
        if (mod.PackageId == PackageIds.Normalize(_config.PackPackageId))
        {
            return SimplifiedOrigin.Machine;
        }
        var text = mod.About.PackageIdRaw + " " + mod.About.Name + " " + mod.About.Author;
        return MachineHint.IsMatch(text) ? SimplifiedOrigin.Machine : SimplifiedOrigin.Patch;
    }

    private readonly Dictionary<string, (ModScan Scan, List<SimplifiedSuggestion> List)> _suggestCache = new(StringComparer.Ordinal);

    /// <summary>術語表變更後呼叫，讓下次 Suggest 重新轉換。</summary>
    public void ClearSuggestions() => _suggestCache.Clear();

    /// <summary>為一個模組的待翻譯條目產生簡中轉繁建議（同一份掃描結果只計算一次）。</summary>
    public List<SimplifiedSuggestion> Suggest(ModScan scan)
    {
        if (_suggestCache.TryGetValue(scan.Mod.PackageId, out var hit) && ReferenceEquals(hit.Scan, scan))
        {
            return hit.List;
        }
        var list = SuggestUncached(scan);
        _suggestCache[scan.Mod.PackageId] = (scan, list);
        return list;
    }

    private List<SimplifiedSuggestion> SuggestUncached(ModScan scan)
    {
        EnsureBuilt();
        var result = new List<SimplifiedSuggestion>();
        var coverage = new Dictionary<string, int>(StringComparer.Ordinal);
        var candidates = new List<(SourceEntry Entry, SimplifiedSource Src)>();
        foreach (var e in scan.Translatable)
        {
            if (!_byPath.TryGetValue(e.CanonicalPath, out var list))
            {
                continue;
            }
            var best = list
                .Select(s => s.Provider.PackageId == scan.Mod.PackageId ? s.WithOrigin(SimplifiedOrigin.ModSelf) : s)
                .OrderBy(s => s.Origin)
                .ThenByDescending(s => s.CurrentFolder)
                .ThenByDescending(s => s.Provider.Active)
                .First();
            candidates.Add((e, best));
            coverage[best.Provider.PackageId] = coverage.GetValueOrDefault(best.Provider.PackageId) + 1;
        }
        var total = Math.Max(1, scan.Translatable.Count());
        foreach (var (e, src) in candidates)
        {
            result.Add(Evaluate(e, src, (double)coverage[src.Provider.PackageId] / total));
        }
        return result;
    }

    private SimplifiedSuggestion Evaluate(SourceEntry e, SimplifiedSource src, double providerCoverage)
    {
        var reasons = new List<string>();
        var signals = new List<string>();
        var sc = src.Record.Text;
        var conversion = _zh.Converter.Convert(sc);

        // ---- 過期偵測 ----
        Freshness fresh;
        var current = LanguageFileReader.NormalizeEnglish(e.Source);
        if (Tokens.Diff(e.Source, sc).Count > 0)
        {
            fresh = Freshness.TokenMismatch;
            reasons.Add("簡中的佔位符／標記與目前英文不一致");
        }
        else if (src.Record.EnglishComment != null)
        {
            fresh = LanguageFileReader.NormalizeEnglish(src.Record.EnglishComment) == current ? Freshness.Confirmed : Freshness.Stale;
            if (fresh == Freshness.Stale)
            {
                reasons.Add("簡中檔的 EN 註解與目前英文不同");
            }
            signals.Add(fresh == Freshness.Confirmed ? "EN 註解吻合" : "EN 註解不符");
        }
        else if (src.SameFolderEnglish != null)
        {
            fresh = LanguageFileReader.NormalizeEnglish(src.SameFolderEnglish) == current ? Freshness.Confirmed : Freshness.Stale;
            if (fresh == Freshness.Stale)
            {
                reasons.Add("簡中所在資料夾的英文與目前英文不同");
            }
            signals.Add(fresh == Freshness.Confirmed ? "同資料夾英文吻合" : "同資料夾英文不符");
        }
        else
        {
            fresh = Freshness.Unverified;
            signals.Add("無可比對的英文（未驗證）");
        }

        // ---- 可信度訊號 ----
        int trust = src.Origin switch { SimplifiedOrigin.ModSelf => 70, SimplifiedOrigin.Patch => 60, _ => 25 };
        signals.Add(src.Origin switch { SimplifiedOrigin.ModSelf => "模組自帶", SimplifiedOrigin.Patch => "翻譯補丁：" + src.Provider.About.Name, _ => "疑似機翻來源：" + src.Provider.About.Name });
        if (fresh == Freshness.Confirmed) trust += 15;
        if (fresh == Freshness.Unverified) trust -= 5;
        if (!src.CurrentFolder)
        {
            trust -= 15;
            signals.Add("來自未載入的資料夾 " + (src.Folder.Length == 0 ? "(根目錄)" : src.Folder));
        }
        if (providerCoverage >= 0.8)
        {
            trust += 5;
            signals.Add("來源覆蓋率高（" + providerCoverage.ToString("P0") + "）");
        }
        else if (providerCoverage < 0.3)
        {
            trust -= 5;
            signals.Add("來源覆蓋率低（" + providerCoverage.ToString("P0") + "）");
        }
        var residual = ResidualEnglish.Matches(Tokens.StripTokens(sc)).Select(m => m.Value).Where(w => !_config.ProperNouns.Contains(w, StringComparer.OrdinalIgnoreCase)).ToList();
        var suspicious = false;
        if (LanguageFileReader.NormalizeEnglish(sc) == current)
        {
            suspicious = true;
            trust -= 40;
            reasons.Add("簡中與英文相同（未翻譯）");
        }
        else if (residual.Count > 0)
        {
            suspicious = true;
            trust -= 15;
            reasons.Add("簡中殘留英文：" + string.Join("、", residual.Distinct().Take(3)));
        }
        if (src.Origin == SimplifiedOrigin.Machine)
        {
            reasons.Add("來源疑似機器翻譯");
        }
        // 英文原文本身可疑：作者沒寫英文（值就是鍵名），或英文資料夾裡放的是中文。
        var englishSuspect = false;
        if (e.Kind == EntryKind.Keyed && string.Equals(e.Source.Trim(), e.InjectionPath, StringComparison.OrdinalIgnoreCase))
        {
            englishSuspect = true;
            reasons.Add("英文就是鍵名本身（作者可能沒寫英文），無法對照原文");
        }
        else if (e.Source.Any(c => c >= 0x3400 && c <= 0x9FFF))
        {
            englishSuspect = true;
            reasons.Add("英文原文含中文（模組可能把中文放在 English 資料夾）");
        }

        // ---- 轉換品質 ----
        var ambiguous = conversion.AmbiguousSegments.ToList();
        if (ambiguous.Count > 0)
        {
            reasons.Add("一對多字：" + string.Join("、", ambiguous.Select(a => a.Source + "→" + a.Result + "（另有 " + string.Join("/", a.Alternatives) + "）").Distinct().Take(4)));
        }
        var conflicts = _zh.ConflictingTermsIn(sc);
        if (conflicts.Count > 0)
        {
            reasons.Add("官方術語有多種寫法：" + string.Join("、", conflicts.Select(c => c.Simplified + "→" + string.Join("/", c.Traditional.Select(t => t.Text)))));
        }
        var labelHint = _zh.LabelHints.TryGetValue(sc.Trim(), out var hint) ? hint : null;
        if (labelHint != null)
        {
            reasons.Add("官方繁中對同一簡中詞譯為「" + string.Join("／", labelHint.Traditional.Select(t => t.Text)) + "」（用詞不同，非逐字轉換）");
        }
        var glossaryHits = conversion.GlossarySegments.Select(g => g.Source + "→" + g.Result).Distinct().ToList();
        if (glossaryHits.Count > 0)
        {
            signals.Add("套用官方術語：" + string.Join("、", glossaryHits.Take(4)));
        }
        var agentTerms = conversion.GlossarySegments.Where(g => _zh.AgentTerms.Contains(g.Source)).Select(g => g.Source + "→" + g.Result).Distinct().ToList();
        if (agentTerms.Count > 0)
        {
            reasons.Add("使用了 Agent 新增的術語（需人工或 Agent 確認）：" + string.Join("、", agentTerms.Take(3)));
        }
        var validation = Validator.Validate(e.Source, conversion.Text, new ValidatorOptions { ProperNounWhitelist = new HashSet<string>(_config.ProperNouns, StringComparer.OrdinalIgnoreCase) });
        if (!validation.Accepted)
        {
            reasons.Add("轉換結果驗證失敗：" + string.Join("、", validation.Rejects.Select(r => r.Code)));
        }

        trust = Math.Clamp(trust, 0, 100);
        SuggestionTier tier;
        if (fresh is Freshness.Stale or Freshness.TokenMismatch)
        {
            tier = SuggestionTier.Retranslate;
        }
        else if (ambiguous.Count > 0 || conflicts.Count > 0 || labelHint != null || suspicious || englishSuspect || agentTerms.Count > 0 || src.Origin == SimplifiedOrigin.Machine || !src.CurrentFolder || !validation.Accepted)
        {
            tier = SuggestionTier.Review;
            if (!src.CurrentFolder && !reasons.Any(r => r.Contains("資料夾")))
            {
                reasons.Add("來源為舊版本資料夾");
            }
        }
        else
        {
            tier = SuggestionTier.Direct;
        }

        return new SimplifiedSuggestion
        {
            Entry = e,
            Source = src,
            Conversion = conversion,
            Freshness = fresh,
            Tier = tier,
            Trust = trust,
            Reasons = reasons,
            TrustSignals = signals,
            Validation = validation,
        };
    }
}

internal static class SimplifiedSourceExtensions
{
    public static SimplifiedSource WithOrigin(this SimplifiedSource s, SimplifiedOrigin origin) => new()
    {
        Record = s.Record,
        Provider = s.Provider,
        Origin = origin,
        Folder = s.Folder,
        CurrentFolder = s.CurrentFolder,
        SameFolderEnglish = s.SameFolderEnglish,
    };
}
