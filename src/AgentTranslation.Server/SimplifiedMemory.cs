using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

/// <summary>Kind of Simplified translation source, from most to least trusted.</summary>
public enum SimplifiedOrigin
{
    /// <summary>Shipped by the translated mod itself (author or contributors).</summary>
    ModSelf,
    /// <summary>Another translation patch mod.</summary>
    Patch,
    /// <summary>A pack whose name or author says it is machine/AI translated.</summary>
    Machine,
}

public sealed class SimplifiedSource
{
    public required LanguageRecord Record { get; init; }
    public required InstalledMod Provider { get; init; }
    public required SimplifiedOrigin Origin { get; init; }
    /// <summary>Sub folder holding the Languages folder ("" for the mod root, "1.4", "Common"...).</summary>
    public required string Folder { get; init; }
    /// <summary>Whether the game currently loads that folder.</summary>
    public required bool CurrentFolder { get; init; }
    /// <summary>English in the same folder, if any; used to detect staleness.</summary>
    public string? SameFolderEnglish { get; init; }
}

public enum Freshness
{
    /// <summary>EN comment or same-folder English equals the current English.</summary>
    Confirmed,
    /// <summary>Nothing to compare against, but tokens match.</summary>
    Unverified,
    /// <summary>EN comment or same-folder English differs from the current English.</summary>
    Stale,
    /// <summary>Placeholders or markup differ from the current English.</summary>
    TokenMismatch,
}

public enum SuggestionTier
{
    /// <summary>Meets the conditions for direct adoption (still only a suggestion until the user enables auto-adopt).</summary>
    Direct,
    /// <summary>Needs agent review.</summary>
    Review,
    /// <summary>The Simplified text is stale; retranslate (Simplified is reference only).</summary>
    Retranslate,
}

/// <summary>A reason or signal with a stable code. Agents get the English text; user reports map the code to Chinese.</summary>
public sealed record Reason(string Code, string Data = "")
{
    public string English => Code switch
    {
        "token_mismatch" => "Placeholders/markup in the Simplified text differ from the current English",
        "en_comment_mismatch" => "The EN comment in the Simplified file differs from the current English",
        "same_folder_english_mismatch" => "English next to the Simplified file differs from the current English",
        "identical_to_english" => "The Simplified text equals the English (untranslated)",
        "residual_english" => "English words left in the Simplified text: " + Data,
        "machine_source" => "The source looks machine translated",
        "key_as_english" => "The English is just the key name (the author probably wrote no English)",
        "chinese_in_english" => "The English source contains Chinese (the mod may keep Chinese in its English folder)",
        "ambiguous_chars" => "One-to-many characters: " + Data,
        "official_term_conflict" => "Official term has several Traditional spellings: " + Data,
        "official_wording_differs" => "Official Traditional uses different wording for this text: " + Data,
        "agent_glossary_term" => "Uses glossary terms added by an agent (needs confirmation): " + Data,
        "validation_failed" => "The result fails validation: " + Data,
        "old_version_folder" => "Comes from a folder the game does not load: " + Data,
        "en_comment_match" => "EN comment matches",
        "same_folder_english_match" => "Same-folder English matches",
        "unverified" => "No English to compare against (unverified)",
        "origin_mod_self" => "Shipped by the mod itself",
        "origin_patch" => "Translation patch: " + Data,
        "origin_machine" => "Suspected machine translation: " + Data,
        "coverage_high" => "Provider covers most of this mod (" + Data + ")",
        "coverage_low" => "Provider covers little of this mod (" + Data + ")",
        "official_term_applied" => "Official terms applied: " + Data,
        _ => Code + (Data.Length > 0 ? ": " + Data : ""),
    };
}

public sealed class SimplifiedSuggestion
{
    public required SourceEntry Entry { get; init; }
    public required SimplifiedSource Source { get; init; }
    /// <summary>In Traditional mode the converted text; in Simplified mode the Simplified text unchanged.</summary>
    public required ConversionResult Conversion { get; init; }
    public required Freshness Freshness { get; init; }
    public required SuggestionTier Tier { get; init; }
    public required int Trust { get; init; }
    public required List<Reason> Reasons { get; init; }
    public List<Reason> TrustSignals { get; init; } = new();
    public ValidationOutcome? Validation { get; init; }
}

/// <summary>An existing, active Simplified translation that no longer matches the current English.</summary>
public sealed class StaleTranslation
{
    public required SourceEntry Entry { get; init; }
    public required SimplifiedSource Source { get; init; }
    public required Freshness Freshness { get; init; }
}

/// <summary>
/// Global Simplified translation memory: collects the ChineseSimplified translations of every installed mod (including
/// inactive translation patches and old version folders), maps them by key / DefInjected path to current English entries,
/// and classifies them. Traditional mode converts them with OpenCC; Simplified mode uses them as they are.
/// </summary>
public sealed class SimplifiedMemory
{
    private static readonly Regex MachineHint = new(@"(\bAI\b|machine|auto[\s\-_]*translat|gpt|deepl|google\s*translat|机翻|機翻|自动翻译|自動翻譯|机器翻译|機器翻譯)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ResidualEnglish = new(@"[A-Za-z]{4,}", RegexOptions.CultureInvariant);

    private readonly AppConfig _config;
    private readonly GameEnvironment _env;
    private readonly ChineseResources _zh;
    private readonly Dictionary<string, List<SimplifiedSource>> _byPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Text, string Provider)>> _byEnglish = new(StringComparer.Ordinal);
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
                    if (!TargetLanguages.FolderMatches(Path.GetFileName(dir), TargetLanguages.Simplified))
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
                        AddEnglish(rec.EnglishComment ?? en, rec.Text, origin, mod);
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

    /// <summary>English -> Simplified index for style consistency. Only pairs whose English is known and not from machine sources.</summary>
    private void AddEnglish(string? english, string text, SimplifiedOrigin origin, InstalledMod provider)
    {
        if (english == null || origin == SimplifiedOrigin.Machine || text.Length == 0)
        {
            return;
        }
        var en = LanguageFileReader.NormalizeEnglish(english);
        if (en.Length == 0 || LanguageFileReader.NormalizeEnglish(text) == en)
        {
            return;
        }
        if (!_byEnglish.TryGetValue(en, out var list))
        {
            list = new List<(string, string)>();
            _byEnglish[en] = list;
        }
        if (list.Count < 3 && !list.Any(x => x.Text == text))
        {
            list.Add((text, provider.About.Name));
        }
    }

    /// <summary>Existing Simplified translations of the same English in any installed mod (at most 3).</summary>
    public IReadOnlyList<(string Text, string Provider)> ByEnglish(string english)
    {
        EnsureBuilt();
        return _byEnglish.TryGetValue(LanguageFileReader.NormalizeEnglish(english), out var list) ? list : Array.Empty<(string, string)>();
    }

    /// <summary>Iterates the English -> Simplified index (for query_memory).</summary>
    public IEnumerable<KeyValuePair<string, List<(string Text, string Provider)>>> EnglishIndex()
    {
        EnsureBuilt();
        return _byEnglish;
    }

    /// <summary>Simplified sources for an entry under any accepted Def type name.</summary>
    private List<SimplifiedSource> SourcesFor(SourceEntry e)
    {
        var result = new List<SimplifiedSource>();
        foreach (var key in e.CanonicalPathCandidates.Distinct(StringComparer.Ordinal))
        {
            if (_byPath.TryGetValue(key, out var list))
            {
                result.AddRange(list);
            }
        }
        return result;
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

    /// <summary>All Languages folders in a mod (depth &lt;= 3), as (sub folder, Languages path).</summary>
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

    /// <summary>Call after glossary changes so the next Suggest converts again.</summary>
    public void ClearSuggestions() => _suggestCache.Clear();

    /// <summary>Suggestions for the untranslated entries of one mod (computed once per scan result).</summary>
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

    private static SimplifiedSource WithOwnOrigin(SimplifiedSource s, ModScan scan) =>
        s.Provider.PackageId == scan.Mod.PackageId ? s.WithOrigin(SimplifiedOrigin.ModSelf) : s;

    private List<SimplifiedSuggestion> SuggestUncached(ModScan scan)
    {
        EnsureBuilt();
        var result = new List<SimplifiedSuggestion>();
        var coverage = new Dictionary<string, int>(StringComparer.Ordinal);
        var candidates = new List<(SourceEntry Entry, SimplifiedSource Src)>();
        // Keys used in code have no English source to compare freshness with.
        foreach (var e in scan.Translatable.Where(e => e.Code == null))
        {
            var list = SourcesFor(e);
            if (list.Count == 0)
            {
                continue;
            }
            var best = list
                .Select(s => WithOwnOrigin(s, scan))
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

    /// <summary>
    /// Entries already translated by an active, loaded Simplified file that no longer matches the current English.
    /// Only meaningful in Simplified mode, where those files count as existing translations.
    /// </summary>
    public List<StaleTranslation> StaleExisting(ModScan scan)
    {
        EnsureBuilt();
        var result = new List<StaleTranslation>();
        foreach (var e in scan.Entries.Where(e => e.Code == null && scan.AlreadyTranslated.Contains(e.Id)))
        {
            var list = SourcesFor(e);
            if (list.Count == 0)
            {
                continue;
            }
            // The text the game shows: the last loaded active provider in a loaded folder.
            var shown = list.Where(s => s.Provider.Active && s.CurrentFolder).OrderByDescending(s => s.Provider.LoadOrder).FirstOrDefault();
            if (shown == null)
            {
                continue;
            }
            var fresh = FreshnessOf(e, shown, out _);
            if (fresh is Freshness.Stale or Freshness.TokenMismatch)
            {
                result.Add(new StaleTranslation { Entry = e, Source = WithOwnOrigin(shown, scan), Freshness = fresh });
            }
        }
        return result;
    }

    private static Freshness FreshnessOf(SourceEntry e, SimplifiedSource src, out Reason reason)
    {
        var current = LanguageFileReader.NormalizeEnglish(e.Source);
        if (Tokens.Diff(e.Source, src.Record.Text).Count > 0)
        {
            reason = new Reason("token_mismatch");
            return Freshness.TokenMismatch;
        }
        if (src.Record.EnglishComment != null)
        {
            var ok = LanguageFileReader.NormalizeEnglish(src.Record.EnglishComment) == current;
            reason = new Reason(ok ? "en_comment_match" : "en_comment_mismatch");
            return ok ? Freshness.Confirmed : Freshness.Stale;
        }
        if (src.SameFolderEnglish != null)
        {
            var ok = LanguageFileReader.NormalizeEnglish(src.SameFolderEnglish) == current;
            reason = new Reason(ok ? "same_folder_english_match" : "same_folder_english_mismatch");
            return ok ? Freshness.Confirmed : Freshness.Stale;
        }
        reason = new Reason("unverified");
        return Freshness.Unverified;
    }

    private SimplifiedSuggestion Evaluate(SourceEntry e, SimplifiedSource src, double providerCoverage)
    {
        var reasons = new List<Reason>();
        var signals = new List<Reason>();
        var sc = src.Record.Text;
        var traditional = _config.IsTraditional;
        // Simplified mode: same language, the text is used unchanged.
        ConversionResult conversion;
        if (traditional)
        {
            conversion = _zh.Converter.Convert(sc);
        }
        else
        {
            conversion = new ConversionResult { Text = sc };
            conversion.Segments.Add(new ConversionSegment(sc, sc, SegmentSource.Unchanged, null));
        }

        // ---- Staleness ----
        var current = LanguageFileReader.NormalizeEnglish(e.Source);
        var fresh = FreshnessOf(e, src, out var freshReason);
        if (fresh is Freshness.Stale or Freshness.TokenMismatch)
        {
            reasons.Add(freshReason);
        }
        else
        {
            signals.Add(freshReason);
        }

        // ---- Trust signals ----
        int trust = src.Origin switch { SimplifiedOrigin.ModSelf => 70, SimplifiedOrigin.Patch => 60, _ => 25 };
        signals.Add(src.Origin switch
        {
            SimplifiedOrigin.ModSelf => new Reason("origin_mod_self"),
            SimplifiedOrigin.Patch => new Reason("origin_patch", src.Provider.About.Name),
            _ => new Reason("origin_machine", src.Provider.About.Name),
        });
        if (fresh == Freshness.Confirmed) trust += 15;
        if (fresh == Freshness.Unverified) trust -= 5;
        if (!src.CurrentFolder)
        {
            trust -= 15;
            signals.Add(new Reason("old_version_folder", src.Folder.Length == 0 ? "(root)" : src.Folder));
        }
        if (providerCoverage >= 0.8)
        {
            trust += 5;
            signals.Add(new Reason("coverage_high", providerCoverage.ToString("P0")));
        }
        else if (providerCoverage < 0.3)
        {
            trust -= 5;
            signals.Add(new Reason("coverage_low", providerCoverage.ToString("P0")));
        }
        var residual = ResidualEnglish.Matches(Tokens.StripTokens(sc)).Select(m => m.Value).Where(w => !_config.ProperNouns.Contains(w, StringComparer.OrdinalIgnoreCase)).ToList();
        var suspicious = false;
        if (LanguageFileReader.NormalizeEnglish(sc) == current)
        {
            suspicious = true;
            trust -= 40;
            reasons.Add(new Reason("identical_to_english"));
        }
        else if (residual.Count > 0)
        {
            suspicious = true;
            trust -= 15;
            reasons.Add(new Reason("residual_english", string.Join(", ", residual.Distinct().Take(3))));
        }
        if (src.Origin == SimplifiedOrigin.Machine)
        {
            reasons.Add(new Reason("machine_source"));
        }
        // The English source itself is suspicious: no English written (value equals the key) or Chinese in the English folder.
        var englishSuspect = false;
        if (e.Kind == EntryKind.Keyed && string.Equals(e.Source.Trim(), e.InjectionPath, StringComparison.OrdinalIgnoreCase))
        {
            englishSuspect = true;
            reasons.Add(new Reason("key_as_english"));
        }
        else if (e.Source.Any(c => c >= 0x3400 && c <= 0x9FFF))
        {
            englishSuspect = true;
            reasons.Add(new Reason("chinese_in_english"));
        }

        // ---- Conversion quality (Traditional mode only) ----
        var ambiguous = conversion.AmbiguousSegments.ToList();
        if (ambiguous.Count > 0)
        {
            reasons.Add(new Reason("ambiguous_chars", string.Join(", ", ambiguous.Select(a => a.Source + "->" + a.Result + " (also " + string.Join("/", a.Alternatives) + ")").Distinct().Take(4))));
        }
        var conflicts = traditional ? _zh.ConflictingTermsIn(sc) : new List<GlossaryTerm>();
        if (conflicts.Count > 0)
        {
            reasons.Add(new Reason("official_term_conflict", string.Join(", ", conflicts.Select(c => c.Simplified + "->" + string.Join("/", c.Traditional.Select(t => t.Text))))));
        }
        var labelHint = traditional && _zh.LabelHints.TryGetValue(sc.Trim(), out var hint) ? hint : null;
        if (labelHint != null)
        {
            reasons.Add(new Reason("official_wording_differs", string.Join("/", labelHint.Traditional.Select(t => t.Text))));
        }
        var glossaryHits = conversion.GlossarySegments.Select(g => g.Source + "->" + g.Result).Distinct().ToList();
        if (glossaryHits.Count > 0)
        {
            signals.Add(new Reason("official_term_applied", string.Join(", ", glossaryHits.Take(4))));
        }
        var agentTerms = conversion.GlossarySegments.Where(g => _zh.AgentTerms.Contains(g.Source)).Select(g => g.Source + "->" + g.Result).Distinct().ToList();
        if (agentTerms.Count > 0)
        {
            reasons.Add(new Reason("agent_glossary_term", string.Join(", ", agentTerms.Take(3))));
        }
        var validation = Validator.Validate(e.Source, conversion.Text, new ValidatorOptions
        {
            ProperNounWhitelist = new HashSet<string>(_config.ProperNouns, StringComparer.OrdinalIgnoreCase),
            Script = TargetLanguages.ScriptOf(_config.TargetLanguage),
        });
        if (!validation.Accepted)
        {
            reasons.Add(new Reason("validation_failed", string.Join(", ", validation.Rejects.Select(r => r.Code))));
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
            if (!src.CurrentFolder)
            {
                reasons.Add(new Reason("old_version_folder", src.Folder.Length == 0 ? "(root)" : src.Folder));
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
