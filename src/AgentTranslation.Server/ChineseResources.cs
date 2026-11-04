using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Text;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

/// <summary>One game term: official Simplified -> official Traditional, kept only where it differs from OpenCC.</summary>
public sealed class GlossaryTerm
{
    public string Simplified { get; init; } = string.Empty;
    /// <summary>Official Traditional spellings with their counts, most frequent first.</summary>
    public List<(string Text, int Count)> Traditional { get; init; } = new();
    public string OpenCcResult { get; init; } = string.Empty;
    public string Example { get; init; } = string.Empty;
    /// <summary>The official data uses two or more spellings and the second one is not rare.</summary>
    public bool Conflicting => Traditional.Count > 1 && Traditional[1].Count * 4 >= Traditional[0].Count;
}

/// <summary>
/// Embedded OpenCC dictionaries and the glossary derived from the official language packs.
/// Official data: the ChineseSimplified and ChineseTraditional .tar files under Data/*/Languages (shipped with the game),
/// paired by key. Only short terms are used for the glossary: 2-8 characters, all CJK, same length in both scripts.
/// </summary>
public sealed class ChineseResources
{
    private ChineseResources() { }

    private PhraseDictionary _twVariants = null!;
    private PhraseDictionary _stCharacters = null!;
    private PhraseDictionary _stPhrases = null!;
    private PhraseDictionary[] _tw = null!;
    private PhraseDictionary _norm = null!;

    /// <summary>Sources of s2t terms added through MCP (by an agent). Conversions that hit them are never auto-adopted.</summary>
    public HashSet<string> AgentTerms { get; } = new(StringComparer.Ordinal);
    /// <summary>User and built-in s2t terms (Simplified -> Traditional).</summary>
    public Dictionary<string, GlossaryEntry> UserTerms { get; } = new(StringComparer.Ordinal);

    public ChineseConverter PlainConverter { get; private set; } = null!;
    /// <summary>Terms where official Traditional uses different wording, not just a script variant (e.g. 按钮 -> 鈕扣); used only as a review hint when the whole text matches.</summary>
    public Dictionary<string, GlossaryTerm> LabelHints { get; } = new(StringComparer.Ordinal);
    public ChineseConverter Converter { get; private set; } = null!;
    public Dictionary<string, GlossaryTerm> Glossary { get; } = new(StringComparer.Ordinal);
    public int OfficialPairs { get; private set; }
    /// <summary>Official Traditional translations keyed by CanonicalPath.</summary>
    public Dictionary<string, LanguageRecord> OfficialTraditional { get; } = new(StringComparer.Ordinal);
    /// <summary>Official Simplified translations keyed by CanonicalPath.</summary>
    public Dictionary<string, LanguageRecord> OfficialSimplified { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, LanguageRecord> OfficialFor(string language) =>
        language == TargetLanguages.Traditional ? OfficialTraditional : OfficialSimplified;

    public static ChineseResources Load(string gameDir)
    {
        var r = new ChineseResources();
        var asm = typeof(ChineseResources).Assembly;
        PhraseDictionary D(string name)
        {
            using var s = asm.GetManifestResourceStream("opencc/" + name + ".txt") ?? throw new InvalidOperationException("Missing embedded resource " + name);
            using var reader = new StreamReader(s, Encoding.UTF8);
            return PhraseDictionary.Parse(reader);
        }
        var stPhrases = D("STPhrases");
        var stChars = D("STCharacters");
        var tw = new[] { D("TWPhrases"), D("TWVariantsPhrases"), D("TWVariants") };
        var norm = D("CJK_Compatibility_Ideographs");
        r.PlainConverter = new ChineseConverter(stPhrases, stChars, tw, null, norm);

        // Full Simplified-only set: characters whose candidates (including their Taiwan variants) never include themselves.
        // Example: 群 -> 羣, and the Taiwan variant table maps 羣 back to 群, so 群 is not Simplified-only.
        var simplifiedOnly = new List<char>();
        r._twVariants = tw[2];
        r._stCharacters = stChars;
        foreach (var key in stChars.Keys.Where(k => k.Length == 1))
        {
            if (!r.AllowedTraditional(key).Contains(key))
            {
                simplifiedOnly.Add(key[0]);
            }
        }
        SimplifiedChars.UseSet(simplifiedOnly);

        // Full Traditional-only set: TSCharacters keys whose Simplified candidates never include themselves,
        // and which are not themselves a valid Simplified form of something (e.g. 乾 -> 干/乾 stays allowed).
        var ts = D("TSCharacters");
        var simplifiedTargets = new HashSet<string>(stChars.Keys, StringComparer.Ordinal);
        var traditionalOnly = new List<char>();
        foreach (var key in ts.Keys.Where(k => k.Length == 1))
        {
            if (ts.TryGet(key, out var cands) && !cands.Contains(key) && !simplifiedTargets.Contains(key))
            {
                traditionalOnly.Add(key[0]);
            }
        }
        TraditionalChars.UseSet(traditionalOnly);

        r._stPhrases = stPhrases;
        r._tw = tw;
        r._norm = norm;
        r.BuildOfficialGlossary(gameDir);
        r.ApplyUserGlossary(Array.Empty<GlossaryEntry>());
        return r;
    }

    /// <summary>Rebuilds the converter: official script-variant terms (non-conflicting) plus user s2t terms (which win).</summary>
    public void ApplyUserGlossary(IEnumerable<GlossaryEntry> entries)
    {
        var glossaryDict = new PhraseDictionary();
        foreach (var t in Glossary.Values.Where(t => !t.Conflicting))
        {
            glossaryDict.Add(t.Simplified, new[] { t.Traditional[0].Text });
        }
        AgentTerms.Clear();
        UserTerms.Clear();
        foreach (var e in entries.Where(e => e.Kind == "s2t"))
        {
            glossaryDict.Add(e.Source, new[] { e.Target });
            UserTerms[e.Source] = e;
            if (e.Origin == "agent")
            {
                AgentTerms.Add(e.Source);
            }
        }
        Converter = new ChineseConverter(_stPhrases, _stCharacters, _tw, glossaryDict, _norm);
    }

    private void BuildOfficialGlossary(string gameDir)
    {
        var data = Path.Combine(gameDir, "Data");
        if (!Directory.Exists(data))
        {
            return;
        }
        var sc = new Dictionary<string, string>(StringComparer.Ordinal);
        var tc = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var dlc in Directory.GetDirectories(data))
        {
            var langs = Path.Combine(dlc, "Languages");
            if (!Directory.Exists(langs))
            {
                continue;
            }
            foreach (var tar in Directory.GetFiles(langs, "*.tar"))
            {
                var name = Path.GetFileName(tar);
                var isSc = name.StartsWith(TargetLanguages.Simplified, StringComparison.Ordinal);
                var isTc = name.StartsWith(TargetLanguages.Traditional, StringComparison.Ordinal);
                if (!isSc && !isTc)
                {
                    continue;
                }
                var target = isSc ? sc : tc;
                var official = isSc ? OfficialSimplified : OfficialTraditional;
                foreach (var rec in LanguageFileReader.Read(TarFiles(tar)))
                {
                    target[Path.GetFileName(dlc) + "|" + rec.CanonicalPath] = rec.Text;
                    official[rec.CanonicalPath] = rec;
                }
            }
        }

        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var examples = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, s) in sc)
        {
            if (!tc.TryGetValue(key, out var t))
            {
                continue;
            }
            OfficialPairs++;
            var sTrim = s.Trim();
            var tTrim = t.Trim();
            if (sTrim.Length < 2 || sTrim.Length > 8 || sTrim.Length != tTrim.Length || !sTrim.All(IsCjk) || !tTrim.All(IsCjk))
            {
                continue;
            }
            if (!counts.TryGetValue(sTrim, out var byTc))
            {
                byTc = new Dictionary<string, int>(StringComparer.Ordinal);
                counts[sTrim] = byTc;
                examples[sTrim] = key.Substring(key.IndexOf('|') + 1);
            }
            byTc[tTrim] = byTc.GetValueOrDefault(tTrim) + 1;
        }
        foreach (var (term, byTc) in counts)
        {
            var open = PlainConverter.Convert(term).Text;
            var all = byTc.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).Select(x => (x.Key, x.Value)).ToList();
            // Only script-compatible spellings (character-by-character) may correct OpenCC's one-to-many choice;
            // the rest are different wording and serve only as a whole-text review hint, never as substring replacement.
            var compatible = all.Where(x => IsScriptCompatible(term, x.Key)).ToList();
            var other = all.Where(x => !IsScriptCompatible(term, x.Key)).ToList();
            if (compatible.Count > 0 && !(compatible.Count == 1 && compatible[0].Key == open))
            {
                Glossary[term] = new GlossaryTerm { Simplified = term, Traditional = compatible, OpenCcResult = open, Example = examples[term] };
            }
            if (other.Count > 0 && !all.Any(x => x.Key == open))
            {
                LabelHints[term] = new GlossaryTerm { Simplified = term, Traditional = other, OpenCcResult = open, Example = examples[term] };
            }
        }
    }

    /// <summary>Acceptable Traditional forms of a Simplified character: itself, the OpenCC candidates, and their Taiwan variants.</summary>
    public HashSet<string> AllowedTraditional(string ch)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        void AddWithVariant(string c)
        {
            set.Add(c);
            if (_twVariants.TryGet(c, out var v))
            {
                foreach (var x in v)
                {
                    set.Add(x);
                }
            }
        }
        if (_stCharacters.TryGet(ch, out var candidates))
        {
            foreach (var c in candidates)
            {
                AddWithVariant(c);
            }
        }
        else
        {
            AddWithVariant(ch);
        }
        return set;
    }

    /// <summary>Whether an official Traditional term is a plain character-by-character conversion of the Simplified one.</summary>
    public bool IsScriptCompatible(string simplified, string traditional)
    {
        if (simplified.Length != traditional.Length)
        {
            return false;
        }
        for (int i = 0; i < simplified.Length; i++)
        {
            if (!AllowedTraditional(simplified[i].ToString()).Contains(traditional[i].ToString()))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Official terms present in the Simplified text that have more than one official Traditional spelling.</summary>
    public List<GlossaryTerm> ConflictingTermsIn(string simplified)
    {
        return Glossary.Values.Where(t => t.Conflicting && simplified.Contains(t.Simplified, StringComparison.Ordinal)).ToList();
    }

    private static bool IsCjk(char c) => c >= 0x3400 && c <= 0x9FFF;

    private static IEnumerable<(string, Func<Stream>)> TarFiles(string tarPath)
    {
        using var fs = File.OpenRead(tarPath);
        using var reader = new TarReader(fs);
        TarEntry? entry;
        var list = new List<(string, byte[])>();
        while ((entry = reader.GetNextEntry()) != null)
        {
            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream == null)
            {
                continue;
            }
            if (entry.Length > SafeXml.MaxFileBytes)
            {
                continue;
            }
            using var ms = new MemoryStream();
            entry.DataStream.CopyTo(ms);
            list.Add((entry.Name.Replace('\\', '/').TrimStart('.', '/'), ms.ToArray()));
        }
        foreach (var (name, bytes) in list)
        {
            var captured = bytes;
            yield return (name, () => new MemoryStream(captured, writable: false));
        }
    }
}
