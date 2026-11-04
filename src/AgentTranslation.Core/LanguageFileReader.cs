using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace AgentTranslation.Core
{
    /// <summary>One translation read from a language folder (or an official .tar).</summary>
    public sealed class LanguageRecord
    {
        public LanguageRecord(EntryKind kind, string? defType, string path, string text, string? englishComment, string file)
        {
            Kind = kind;
            DefType = defType;
            Path = path;
            Text = text;
            EnglishComment = englishComment;
            File = file;
        }

        public EntryKind Kind { get; }
        /// <summary>DefInjected folder name (may be an old plural form such as ThingDefs).</summary>
        public string? DefType { get; }
        public string Path { get; }
        public string Text { get; }
        /// <summary>Content of the comment right before the element that starts with "EN:" (written by the game's translation file generator and most tools).</summary>
        public string? EnglishComment { get; }
        public string File { get; }

        /// <summary>Same key as SourceEntry.CanonicalPath.</summary>
        public string CanonicalPath => Kind == EntryKind.Def ? DefType + ":" + Path : Path;

        /// <summary>DefType without a trailing s (the game tries this too when a type is not found).</summary>
        public string? CanonicalPathSingular =>
            Kind == EntryKind.Def && DefType != null && DefType.Length > 3 && DefType.EndsWith("s", StringComparison.Ordinal)
                ? DefType.Substring(0, DefType.Length - 1) + ":" + Path
                : null;
    }

    /// <summary>
    /// Reads the Keyed and DefInjected files of a language folder, keeping EN comments.
    /// Only string values are handled; full-list (&lt;li&gt;) translations are skipped.
    /// </summary>
    public static class LanguageFileReader
    {
        /// <summary>files: path relative to the language folder ('/'-separated) and a function that opens the stream.</summary>
        public static IEnumerable<LanguageRecord> Read(IEnumerable<(string RelativePath, Func<Stream> Open)> files)
        {
            foreach (var (rel, open) in files)
            {
                var parts = rel.Replace('\\', '/').Split('/');
                if (parts.Length < 2 || !rel.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                EntryKind kind;
                string? defType = null;
                if (parts[0] == "Keyed")
                {
                    kind = EntryKind.Keyed;
                }
                else if (parts[0] == "DefInjected" && parts.Length >= 3)
                {
                    kind = EntryKind.Def;
                    defType = parts[1];
                }
                else
                {
                    continue;
                }

                XDocument doc;
                try
                {
                    using (var s = open())
                    {
                        doc = SafeXml.Load(s, keepComments: true);
                    }
                }
                catch (Exception)
                {
                    continue;
                }
                if (doc.Root == null)
                {
                    continue;
                }
                string? pendingComment = null;
                foreach (var node in doc.Root.Nodes())
                {
                    if (node is XComment c)
                    {
                        var v = c.Value.Trim();
                        pendingComment = v.StartsWith("EN:", StringComparison.Ordinal) ? v.Substring(3).Trim() : null;
                        continue;
                    }
                    if (!(node is XElement e))
                    {
                        continue;
                    }
                    var comment = pendingComment;
                    pendingComment = null;
                    if (e.HasElements)
                    {
                        continue;
                    }
                    var value = e.Value;
                    if (value.Length == 0 || value == "TODO")
                    {
                        continue;
                    }
                    var path = e.Name.LocalName;
                    if (kind == EntryKind.Def)
                    {
                        path = path.Replace("]", string.Empty).Replace('[', '.');
                    }
                    yield return new LanguageRecord(kind, defType, path, value, comment, rel);
                }
            }
        }

        public static IEnumerable<(string, Func<Stream>)> FilesInDirectory(string languageDir)
        {
            if (!Directory.Exists(languageDir))
            {
                yield break;
            }
            foreach (var file in Directory.GetFiles(languageDir, "*.xml", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                var rel = file.Substring(languageDir.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/');
                var captured = file;
                yield return (rel, () => File.OpenRead(captured));
            }
        }

        /// <summary>Normalisation for comparing English: literal \n equals a newline, whitespace collapsed, trimmed.</summary>
        public static string NormalizeEnglish(string s)
        {
            var t = (s ?? string.Empty).Replace("\\n", "\n").Replace("\r", string.Empty);
            var parts = t.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }
    }
}
