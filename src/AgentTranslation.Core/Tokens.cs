using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AgentTranslation.Core
{
    public enum TokenKind
    {
        /// <summary>{0}, {0:F1}, {PAWN_nameDef}, {PAWN_gender ? he : she} (conditionals compare only the name before ?).</summary>
        Brace,
        /// <summary>&lt;color=#fff&gt;, &lt;/color&gt;, &lt;b&gt; and similar; matches the game's ColoredText.XMLRegex.</summary>
        Markup,
        /// <summary>(*Threat), (/Threat); matches the game's ColoredText.TagRegex.</summary>
        Tag,
        /// <summary>Grammar tokens such as [PAWN_nameDef].</summary>
        Grammar,
        /// <summary>A newline (literal \n or an actual newline character).</summary>
        Newline,
    }

    public sealed class Token
    {
        public Token(TokenKind kind, string text, string compareKey)
        {
            Kind = kind;
            Text = text;
            CompareKey = compareKey;
        }

        public TokenKind Kind { get; }
        /// <summary>The full token text in the source.</summary>
        public string Text { get; }
        /// <summary>Comparison key: the name only for conditionals; the full text otherwise.</summary>
        public string CompareKey { get; }
    }

    /// <summary>
    /// Finds the tokens that must be kept unchanged.
    /// Checked against RimWorld 1.7.4998 Assembly-CSharp: ColoredText.TagRegex = \([\*\/][^\)]*\),
    /// ColoredText.XMLRegex = &lt;[^&gt;]*&gt;; DirectXmlLoaderSimple and DefInjectionPackage turn a literal \n into a newline on load.
    /// The full brace token grammar is [unverified]; {...} is matched as a whole, conditionals by the name before ?.
    /// </summary>
    public static class Tokens
    {
        private static readonly Regex BraceRegex = new Regex(@"\{[^{}]*\}", RegexOptions.CultureInvariant);
        private static readonly Regex MarkupRegex = new Regex(@"<[^>]*>", RegexOptions.CultureInvariant);
        private static readonly Regex TagRegex = new Regex(@"\([\*/][^\)]*\)", RegexOptions.CultureInvariant);
        private static readonly Regex GrammarRegex = new Regex(@"\[[A-Za-z_][A-Za-z0-9_]*\]", RegexOptions.CultureInvariant);

        /// <summary>
        /// includeGrammar: whether [xxx] counts as a grammar token. Only RulePack rule strings need this;
        /// square brackets in ordinary Keyed/Def text are plain text (e.g. "[Zone] tab").
        /// </summary>
        public static List<Token> Extract(string text, bool includeGrammar = false)
        {
            var result = new List<Token>();
            if (string.IsNullOrEmpty(text))
            {
                return result;
            }

            foreach (Match m in BraceRegex.Matches(text))
            {
                var inner = m.Value.Substring(1, m.Value.Length - 2);
                var q = inner.IndexOf('?');
                string key = q >= 0 ? "{" + inner.Substring(0, q).Trim() + " ?…}" : "{" + inner.Trim() + "}";
                result.Add(new Token(TokenKind.Brace, m.Value, key));
            }
            foreach (Match m in MarkupRegex.Matches(text))
            {
                result.Add(new Token(TokenKind.Markup, m.Value, m.Value.Replace(" ", string.Empty)));
            }
            foreach (Match m in TagRegex.Matches(text))
            {
                result.Add(new Token(TokenKind.Tag, m.Value, m.Value));
            }
            if (includeGrammar)
            {
                foreach (Match m in GrammarRegex.Matches(text))
                {
                    result.Add(new Token(TokenKind.Grammar, m.Value, m.Value));
                }
            }

            int newlines = CountOccurrences(text, "\\n") + text.Count(c => c == '\n');
            for (int i = 0; i < newlines; i++)
            {
                result.Add(new Token(TokenKind.Newline, "\\n", "\\n"));
            }
            return result;
        }

        /// <summary>Text with all tokens removed; used for length comparison and script detection.</summary>
        public static string StripTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            var s = BraceRegex.Replace(text, " ");
            s = MarkupRegex.Replace(s, " ");
            s = TagRegex.Replace(s, " ");
            s = s.Replace("\\n", " ").Replace('\n', ' ');
            return s;
        }

        /// <summary>Returns (missing, extra) tokens grouped by kind.</summary>
        public static Dictionary<TokenKind, (List<string> Missing, List<string> Extra)> Diff(string source, string translation, bool includeGrammar = false)
        {
            var src = Extract(source, includeGrammar);
            var dst = Extract(translation, includeGrammar);
            var diff = new Dictionary<TokenKind, (List<string>, List<string>)>();
            foreach (TokenKind kind in Enum.GetValues(typeof(TokenKind)))
            {
                var a = src.Where(t => t.Kind == kind).Select(t => t.CompareKey).ToList();
                var b = dst.Where(t => t.Kind == kind).Select(t => t.CompareKey).ToList();
                var missing = MultisetMinus(a, b);
                var extra = MultisetMinus(b, a);
                if (missing.Count > 0 || extra.Count > 0)
                {
                    diff[kind] = (missing, extra);
                }
            }
            return diff;
        }

        private static List<string> MultisetMinus(List<string> a, List<string> b)
        {
            var remaining = new List<string>(b);
            var result = new List<string>();
            foreach (var item in a)
            {
                var idx = remaining.IndexOf(item);
                if (idx >= 0)
                {
                    remaining.RemoveAt(idx);
                }
                else
                {
                    result.Add(item);
                }
            }
            return result;
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0, idx = 0;
            while ((idx = text.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
            {
                count++;
                idx += needle.Length;
            }
            return count;
        }
    }
}
