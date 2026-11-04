using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AgentTranslation.Core
{
    public enum TokenKind
    {
        /// <summary>{0}、{0:F1}、{PAWN_nameDef}、{PAWN_gender ? 他 : 她}（只比對 ? 前的名稱）。</summary>
        Brace,
        /// <summary>&lt;color=#fff&gt;、&lt;/color&gt;、&lt;b&gt; 等，對應遊戲 ColoredText.XMLRegex。</summary>
        Markup,
        /// <summary>(*Threat)、(/Threat)，對應遊戲 ColoredText.TagRegex。</summary>
        Tag,
        /// <summary>[PAWN_nameDef] 類文法 token。</summary>
        Grammar,
        /// <summary>換行（字面 \n 或實際換行字元）。</summary>
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
        /// <summary>原文中的完整片段。</summary>
        public string Text { get; }
        /// <summary>比對用的鍵：條件式只保留名稱；其他為完整片段。</summary>
        public string CompareKey { get; }
    }

    /// <summary>
    /// 從字串中找出必須原樣保留的 token。
    /// 已對照 RimWorld 1.7.4998 Assembly-CSharp：ColoredText.TagRegex = \([\*\/][^\)]*\)、
    /// ColoredText.XMLRegex = &lt;[^&gt;]*&gt;、DirectXmlLoaderSimple 與 DefInjectionPackage 載入時把字面 \n 轉成換行。
    /// 大括號 token 的完整語法【未確認】，目前以 {…} 整段比對，條件式只比 ? 前的名稱。
    /// </summary>
    public static class Tokens
    {
        private static readonly Regex BraceRegex = new Regex(@"\{[^{}]*\}", RegexOptions.CultureInvariant);
        private static readonly Regex MarkupRegex = new Regex(@"<[^>]*>", RegexOptions.CultureInvariant);
        private static readonly Regex TagRegex = new Regex(@"\([\*/][^\)]*\)", RegexOptions.CultureInvariant);
        private static readonly Regex GrammarRegex = new Regex(@"\[[A-Za-z_][A-Za-z0-9_]*\]", RegexOptions.CultureInvariant);

        /// <summary>
        /// includeGrammar：是否把 [xxx] 當成文法 token。只有 RulePack 規則字串需要；
        /// 一般 Keyed／Def 文字中的方括號是普通文字（例如「[Zone] 分頁」）。
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

        /// <summary>移除所有 token 後的文字，用於長度比較與簡體字偵測。</summary>
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

        /// <summary>回傳 (缺少的, 多出來的) token，依種類分組。</summary>
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
