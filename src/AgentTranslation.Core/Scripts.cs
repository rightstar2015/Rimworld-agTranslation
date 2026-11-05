using System;
using System.Linq;

namespace AgentTranslation.Core
{
    /// <summary>Script of the target language; decides the wrong-script checks and the length expectations.</summary>
    public enum TargetScript
    {
        /// <summary>ChineseSimplified: Traditional-only characters are rejected.</summary>
        Simplified,
        /// <summary>ChineseTraditional (Taiwan): Simplified-only characters are rejected.</summary>
        Traditional,
        /// <summary>Japanese: kanji and kana; Hangul is rejected.</summary>
        Japanese,
        /// <summary>Korean: Hangul; kana is rejected.</summary>
        Korean,
        /// <summary>Latin alphabet (English, German, French, Vietnamese, ...).</summary>
        Latin,
        /// <summary>Cyrillic alphabet (Russian, Ukrainian).</summary>
        Cyrillic,
        /// <summary>Greek alphabet.</summary>
        Greek,
    }

    /// <summary>Character classification by script. Ranges cover the common blocks only.</summary>
    public static class Scripts
    {
        public static bool IsHan(char c) => (c >= 0x3400 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF);
        public static bool IsKana(char c) => (c >= 0x3040 && c <= 0x30FF) || (c >= 0x31F0 && c <= 0x31FF) || (c >= 0xFF66 && c <= 0xFF9F);
        public static bool IsHangul(char c) => (c >= 0xAC00 && c <= 0xD7AF) || (c >= 0x1100 && c <= 0x11FF) || (c >= 0x3130 && c <= 0x318F);
        public static bool IsCyrillic(char c) => c >= 0x0400 && c <= 0x04FF;
        public static bool IsGreek(char c) => c >= 0x0370 && c <= 0x03FF;
        public static bool IsLatin(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= 0x00C0 && c <= 0x024F && c != 0x00D7 && c != 0x00F7) || (c >= 0x1E00 && c <= 0x1EFF);

        /// <summary>Wide characters (Han, kana, Hangul) take roughly the space of two to three Latin letters.</summary>
        public static bool IsWide(char c) => IsHan(c) || IsKana(c) || IsHangul(c);

        /// <summary>Length in "Latin letter units": wide characters count 2.5. Used to compare lengths across scripts.</summary>
        public static double WeightedLength(string text) => text.Sum(c => IsWide(c) ? 2.5 : 1.0);

        public static bool IsChinese(TargetScript s) => s == TargetScript.Simplified || s == TargetScript.Traditional;

        /// <summary>Whether a character belongs to the target script's own letters.</summary>
        public static bool IsOwnLetter(TargetScript s, char c)
        {
            switch (s)
            {
                case TargetScript.Simplified:
                case TargetScript.Traditional:
                    return IsHan(c);
                case TargetScript.Japanese:
                    return IsHan(c) || IsKana(c);
                case TargetScript.Korean:
                    return IsHangul(c);
                case TargetScript.Cyrillic:
                    return IsCyrillic(c);
                case TargetScript.Greek:
                    return IsGreek(c);
                default:
                    return IsLatin(c);
            }
        }

        /// <summary>Letters of a script that should never appear in the target (rejected, not just flagged).</summary>
        public static bool IsForbidden(TargetScript s, char c)
        {
            switch (s)
            {
                case TargetScript.Japanese:
                    return IsHangul(c);
                case TargetScript.Korean:
                    return IsKana(c);
                default:
                    return false;
            }
        }

        /// <summary>Letters that look out of place in the target but may be legitimate (names, symbols); flagged only.</summary>
        public static bool IsForeign(TargetScript s, char c)
        {
            if (!char.IsLetter(c) || IsOwnLetter(s, c) || IsForbidden(s, c))
            {
                return false;
            }
            // Latin letters are normal in every script (names, units, abbreviations).
            if (IsLatin(c))
            {
                return false;
            }
            // Han characters are normal in Japanese; Korean text occasionally keeps Hanja.
            return !(s == TargetScript.Korean && IsHan(c));
        }
    }
}
