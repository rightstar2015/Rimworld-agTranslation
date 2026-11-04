using System;
using System.Security.Cryptography;
using System.Text;

namespace AgentTranslation.Core
{
    /// <summary>
    /// Stable IDs.
    /// key = H(packageId, kind, path)；id = H(packageId, kind, path, normalize(source))。
    /// Hash input: parts joined with U+0000, UTF-8, SHA-256, first 16 bytes, RFC 4648 base32 lower-case without padding (26 chars).
    /// These rules must never change after release, or every existing translation would be treated as stale.
    /// </summary>
    public static class StableId
    {
        private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

        public static string Key(string packageId, EntryKind kind, string canonicalPath)
        {
            return Hash(PackageIds.Normalize(packageId), EntryKindNames.ToId(kind), canonicalPath);
        }

        public static string Id(string packageId, EntryKind kind, string canonicalPath, string source)
        {
            return Hash(PackageIds.Normalize(packageId), EntryKindNames.ToId(kind), canonicalPath, NormalizeSource(source));
        }

        /// <summary>Only normalises line endings; nothing else is rewritten.</summary>
        public static string NormalizeSource(string source)
        {
            return (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }

        public static bool LooksLikeId(string? value)
        {
            if (value == null || value.Length != 26)
            {
                return false;
            }
            foreach (var c in value)
            {
                if (Alphabet.IndexOf(c) < 0)
                {
                    return false;
                }
            }
            return true;
        }

        private static string Hash(params string[] parts)
        {
            var joined = string.Join("\u0000", parts);
            byte[] digest;
            using (var sha = SHA256.Create())
            {
                digest = sha.ComputeHash(Encoding.UTF8.GetBytes(joined));
            }
            return Base32(digest, 16);
        }

        private static string Base32(byte[] data, int length)
        {
            var sb = new StringBuilder((length * 8 + 4) / 5);
            int buffer = 0, bits = 0;
            for (int i = 0; i < length; i++)
            {
                buffer = (buffer << 8) | data[i];
                bits += 8;
                while (bits >= 5)
                {
                    sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                    bits -= 5;
                }
            }
            if (bits > 0)
            {
                sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
            }
            return sb.ToString();
        }
    }
}
