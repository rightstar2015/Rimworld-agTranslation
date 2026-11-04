using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Text;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

/// <summary>一個遊戲術語：官方簡中 → 官方繁中，與 OpenCC 結果不同者才收錄。</summary>
public sealed class GlossaryTerm
{
    public string Simplified { get; init; } = string.Empty;
    /// <summary>官方繁中寫法與出現次數，次數多者在前。</summary>
    public List<(string Text, int Count)> Traditional { get; init; } = new();
    public string OpenCcResult { get; init; } = string.Empty;
    public string Example { get; init; } = string.Empty;
    /// <summary>官方有兩種以上寫法，且次要寫法占比不低。</summary>
    public bool Conflicting => Traditional.Count > 1 && Traditional[1].Count * 4 >= Traditional[0].Count;
}

/// <summary>
/// OpenCC 字典（內嵌資源）與官方語言包術語表。
/// 官方術語表來源：Data/*/Languages 下的 ChineseSimplified 與 ChineseTraditional .tar（遊戲本身附帶），
/// 以相同鍵配對；只取 2–8 字、純中文、繁簡長度相同、且 OpenCC 轉換結果與官方繁中不同的短詞（多為 Def 名稱）。
/// </summary>
public sealed class ChineseResources
{
    private ChineseResources() { }

    private PhraseDictionary _twVariants = null!;
    private PhraseDictionary _stCharacters = null!;
    private PhraseDictionary _stPhrases = null!;
    private PhraseDictionary[] _tw = null!;
    private PhraseDictionary _norm = null!;

    /// <summary>經 MCP（Agent）新增的 s2t 術語來源詞。命中這些詞的轉換不會被自動採用。</summary>
    public HashSet<string> AgentTerms { get; } = new(StringComparer.Ordinal);
    /// <summary>使用者／內建 s2t 術語（簡中 → 繁中）。</summary>
    public Dictionary<string, GlossaryEntry> UserTerms { get; } = new(StringComparer.Ordinal);

    public ChineseConverter PlainConverter { get; private set; } = null!;
    /// <summary>官方繁中與 OpenCC 用詞不同、但不是同字異體的詞（例如 按钮→鈕扣），只在整段文字完全相同時當成校對提示。</summary>
    public Dictionary<string, GlossaryTerm> LabelHints { get; } = new(StringComparer.Ordinal);
    public ChineseConverter Converter { get; private set; } = null!;
    public Dictionary<string, GlossaryTerm> Glossary { get; } = new(StringComparer.Ordinal);
    public int OfficialPairs { get; private set; }
    /// <summary>官方繁中翻譯（英文 → 繁中），供後續翻譯記憶使用。鍵：CanonicalPath。</summary>
    public Dictionary<string, LanguageRecord> OfficialTraditional { get; } = new(StringComparer.Ordinal);

    public static ChineseResources Load(string gameDir)
    {
        var r = new ChineseResources();
        var asm = typeof(ChineseResources).Assembly;
        PhraseDictionary D(string name)
        {
            using var s = asm.GetManifestResourceStream("opencc/" + name + ".txt") ?? throw new InvalidOperationException("缺少內嵌資源 " + name);
            using var reader = new StreamReader(s, Encoding.UTF8);
            return PhraseDictionary.Parse(reader);
        }
        var stPhrases = D("STPhrases");
        var stChars = D("STCharacters");
        var tw = new[] { D("TWPhrases"), D("TWVariantsPhrases"), D("TWVariants") };
        var norm = D("CJK_Compatibility_Ideographs");
        r.PlainConverter = new ChineseConverter(stPhrases, stChars, tw, null, norm);

        // 完整簡體專用字集合：候選（含經台灣異體字轉換後的結果）中不含自己者。
        // 例：群→羣，台灣異體字再轉回 群，所以 群 不算簡體字。
        var simplifiedOnly = new List<char>();
        r._twVariants = tw[2];
        r._stCharacters = stChars;
        foreach (var key in r.AllCharacterKeys())
        {
            if (!r.AllowedTraditional(key).Contains(key))
            {
                simplifiedOnly.Add(key[0]);
            }
        }
        SimplifiedChars.UseSet(simplifiedOnly);

        r._stPhrases = stPhrases;
        r._tw = tw;
        r._norm = norm;
        r.BuildOfficialGlossary(gameDir);
        r.ApplyUserGlossary(Array.Empty<GlossaryEntry>());
        return r;
    }

    /// <summary>重建轉換器：官方同字異體術語（無衝突者）＋ 使用者 s2t 術語（優先）。</summary>
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
                Dictionary<string, string>? target = name.StartsWith("ChineseSimplified", StringComparison.Ordinal) ? sc
                    : name.StartsWith("ChineseTraditional", StringComparison.Ordinal) ? tc : null;
                if (target == null)
                {
                    continue;
                }
                foreach (var rec in LanguageFileReader.Read(TarFiles(tar)))
                {
                    var key = Path.GetFileName(dlc) + "|" + rec.CanonicalPath;
                    target[key] = rec.Text;
                    if (target == tc)
                    {
                        OfficialTraditional[rec.CanonicalPath] = rec;
                    }
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
            // 同字異體（逐字可對應）的寫法才能拿來修正 OpenCC 的一對多選擇；
            // 其他是「翻譯用詞不同」，只作為整段相同時的校對提示，不做子字串替換。
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

    private IEnumerable<string> AllCharacterKeys() => _stCharacters.Keys.Where(k => k.Length == 1);

    /// <summary>一個簡體字可接受的繁體寫法：本身、OpenCC 候選、以及兩者經台灣異體字表轉換後的結果。</summary>
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

    /// <summary>官方繁中詞是否只是簡中詞的逐字轉換（每個字都在可接受寫法內）。</summary>
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

    /// <summary>簡中原文中出現、但官方有多種繁中寫法的術語。</summary>
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
