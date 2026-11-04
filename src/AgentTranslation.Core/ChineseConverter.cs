using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AgentTranslation.Core
{
    /// <summary>OpenCC text dictionary: one "key\tvalue1 value2..." per line; lines starting with # are comments.</summary>
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

        /// <summary>Finds the longest key starting at text[pos].</summary>
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
        /// <summary>Not converted (not Chinese, or not in any dictionary).</summary>
        Unchanged,
        /// <summary>Game or user glossary (highest priority).</summary>
        Glossary,
        /// <summary>OpenCC phrase (STPhrases).</summary>
        Phrase,
        /// <summary>OpenCC single character (STCharacters).</summary>
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
        /// <summary>Result of step 1 (script conversion), before Taiwan localisation.</summary>
        public string Converted { get; }
        public SegmentSource Kind { get; }
        /// <summary>Other candidates listed by the dictionary for a single character (one-to-many). Non-empty means ambiguous.</summary>
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
    /// Simplified to Traditional (Taiwan) conversion, a re-implementation of the OpenCC s2twp pipeline:
    /// 1. Segment and convert: glossary, then STPhrases, then STCharacters, each by left-to-right longest match.
    /// 2. Taiwan localisation: TWPhrases, then TWVariantsPhrases, then TWVariants, by longest match (first dictionary that hits wins).
    /// Glossary segments are not localised again (the glossary already holds the official Traditional form).
    /// Differences from OpenCC: no build-time STPhrases_GeneratedFromRegionalPhrases, and simple longest match instead of mmseg.
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
                    // Ambiguous only when the candidates include an option different from the first one.
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
            // Taiwan phrases (e.g. 软件 -> 軟體) often span several step-1 character segments. OpenCC solves this with the
            // build-time STPhrases_GeneratedFromRegionalPhrases; here every run of non-glossary segments is localised again
            // as a whole, which has the same effect. Glossary segments act as boundaries and are never changed.
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
