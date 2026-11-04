using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AgentTranslation.Core
{
    /// <summary>OpenCC 文字字典：每行「鍵\t值1 值2…」，# 開頭為註解。</summary>
    public sealed class PhraseDictionary
    {
        private readonly Dictionary<string, string[]> _map = new Dictionary<string, string[]>(StringComparer.Ordinal);

        public int MaxKeyLength { get; private set; }
        public int Count => _map.Count;
        public IEnumerable<string> Keys => _map.Keys;

        public static PhraseDictionary Parse(TextReader reader)
        {
            var d = new PhraseDictionary();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }
                var tab = line.IndexOf('\t');
                if (tab <= 0)
                {
                    continue;
                }
                var key = line.Substring(0, tab);
                var values = line.Substring(tab + 1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (values.Length > 0)
                {
                    d.Add(key, values);
                }
            }
            return d;
        }

        public void Add(string key, string[] values)
        {
            _map[key] = values;
            MaxKeyLength = Math.Max(MaxKeyLength, key.Length);
        }

        public bool TryGet(string key, out string[] values) => _map.TryGetValue(key, out values!);

        /// <summary>從 text[pos] 開始找最長的鍵。</summary>
        public bool TryMatchLongest(string text, int pos, out int length, out string[] values)
        {
            var max = Math.Min(MaxKeyLength, text.Length - pos);
            for (int len = max; len >= 1; len--)
            {
                if (_map.TryGetValue(text.Substring(pos, len), out values!))
                {
                    length = len;
                    return true;
                }
            }
            length = 0;
            values = Array.Empty<string>();
            return false;
        }
    }

    public enum SegmentSource
    {
        /// <summary>未轉換（非中文或字典中沒有）。</summary>
        Unchanged,
        /// <summary>遊戲術語表（最高優先）。</summary>
        Glossary,
        /// <summary>OpenCC 詞組（STPhrases）。</summary>
        Phrase,
        /// <summary>OpenCC 單字（STCharacters）。</summary>
        Character,
    }

    public sealed class ConversionSegment
    {
        public ConversionSegment(string source, string result, SegmentSource kind, string[]? alternatives)
        {
            Source = source;
            Result = result;
            Converted = result;
            Kind = kind;
            Alternatives = alternatives ?? Array.Empty<string>();
        }

        public string Source { get; }
        public string Result { get; set; }
        /// <summary>第 1 步（簡轉繁）的結果，台灣化之前。</summary>
        public string Converted { get; }
        public SegmentSource Kind { get; }
        /// <summary>單字轉換時，字典列出的其他候選（一對多）。非空即為歧義。</summary>
        public string[] Alternatives { get; }
        public bool Ambiguous => Kind == SegmentSource.Character && Alternatives.Length > 0;
    }

    public sealed class ConversionResult
    {
        public string Text { get; set; } = string.Empty;
        public List<ConversionSegment> Segments { get; } = new List<ConversionSegment>();
        public IEnumerable<ConversionSegment> AmbiguousSegments => Segments.Where(s => s.Ambiguous);
        public IEnumerable<ConversionSegment> GlossarySegments => Segments.Where(s => s.Kind == SegmentSource.Glossary);
    }

    /// <summary>
    /// 簡體 → 繁體（台灣）轉換，依 OpenCC s2twp 設定的流程自行實作：
    /// 1. 斷詞並轉換：遊戲術語表 → STPhrases → STCharacters，皆為由左至右最長匹配。
    /// 2. 台灣化：對第 1 步的每個片段，依序以 TWPhrases → TWVariantsPhrases → TWVariants 最長匹配（先命中者優先）。
    /// 術語表片段不再台灣化（術語表本身就是繁中官方用字）。
    /// 與 OpenCC 的差異：未使用建置時產生的 STPhrases_GeneratedFromRegionalPhrases，斷詞為簡單最長匹配而非 mmseg。
    /// </summary>
    public sealed class ChineseConverter
    {
        private readonly PhraseDictionary? _glossary;
        private readonly PhraseDictionary _stPhrases;
        private readonly PhraseDictionary _stCharacters;
        private readonly PhraseDictionary[] _twChain;
        private readonly PhraseDictionary? _normalization;

        public ChineseConverter(PhraseDictionary stPhrases, PhraseDictionary stCharacters, IEnumerable<PhraseDictionary> twChain, PhraseDictionary? glossary = null, PhraseDictionary? normalization = null)
        {
            _stPhrases = stPhrases;
            _stCharacters = stCharacters;
            _twChain = twChain.ToArray();
            _glossary = glossary;
            _normalization = normalization;
        }

        public ConversionResult Convert(string text)
        {
            var result = new ConversionResult();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }
            if (_normalization != null)
            {
                text = Substitute(text, _normalization);
            }

            var pending = new StringBuilder();
            void FlushUnchanged()
            {
                if (pending.Length > 0)
                {
                    result.Segments.Add(new ConversionSegment(pending.ToString(), pending.ToString(), SegmentSource.Unchanged, null));
                    pending.Clear();
                }
            }

            int i = 0;
            while (i < text.Length)
            {
                if (_glossary != null && _glossary.TryMatchLongest(text, i, out var glen, out var gval))
                {
                    FlushUnchanged();
                    result.Segments.Add(new ConversionSegment(text.Substring(i, glen), gval[0], SegmentSource.Glossary, null));
                    i += glen;
                    continue;
                }
                if (_stPhrases.TryMatchLongest(text, i, out var plen, out var pval) && plen > 1)
                {
                    FlushUnchanged();
                    result.Segments.Add(new ConversionSegment(text.Substring(i, plen), pval[0], SegmentSource.Phrase, null));
                    i += plen;
                    continue;
                }
                var step = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
                var ch = text.Substring(i, step);
                if (_stCharacters.TryGet(ch, out var cval))
                {
                    FlushUnchanged();
                    // 只有在候選中「有與原字不同的其他選項」時才算歧義。
                    var alts = cval.Skip(1).Where(v => v != cval[0]).ToArray();
                    result.Segments.Add(new ConversionSegment(ch, cval[0], SegmentSource.Character, alts.Length > 0 ? alts : null));
                }
                else
                {
                    pending.Append(ch);
                }
                i += step;
            }
            FlushUnchanged();

            foreach (var seg in result.Segments)
            {
                if (seg.Kind == SegmentSource.Glossary || seg.Kind == SegmentSource.Unchanged)
                {
                    continue;
                }
                seg.Result = Taiwanize(seg.Result);
            }
            // 台灣詞組（例如 軟件→軟體）常橫跨第 1 步的多個單字片段。OpenCC 以建置時產生的
            // STPhrases_GeneratedFromRegionalPhrases 讓這些詞成為一個片段；這裡改為對「連續的非術語表片段」
            // 整段再做一次台灣化，效果相同。術語表片段是邊界，不會被改動。
            var sb = new StringBuilder();
            var run = new StringBuilder();
            foreach (var seg in result.Segments)
            {
                if (seg.Kind == SegmentSource.Glossary)
                {
                    sb.Append(Taiwanize(run.ToString()));
                    run.Clear();
                    sb.Append(seg.Result);
                }
                else
                {
                    run.Append(seg.Kind == SegmentSource.Unchanged ? seg.Source : seg.Converted);
                }
            }
            sb.Append(Taiwanize(run.ToString()));
            result.Text = sb.ToString();
            return result;
        }

        private string Taiwanize(string text)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                var matched = false;
                foreach (var dict in _twChain)
                {
                    if (dict.TryMatchLongest(text, i, out var len, out var val))
                    {
                        sb.Append(val[0]);
                        i += len;
                        matched = true;
                        break;
                    }
                }
                if (!matched)
                {
                    sb.Append(text[i]);
                    i++;
                }
            }
            return sb.ToString();
        }

        private static string Substitute(string text, PhraseDictionary dict)
        {
            var sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                if (dict.TryMatchLongest(text, i, out var len, out var val))
                {
                    sb.Append(val[0]);
                    i += len;
                }
                else
                {
                    sb.Append(text[i]);
                    i++;
                }
            }
            return sb.ToString();
        }
    }
}
