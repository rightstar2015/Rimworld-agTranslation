using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace AgentTranslation.Core
{
    /// <summary>
    /// Translation keys that already exist for a language (Keyed keys, DefInjected DefType:path).
    /// Used to decide "already translated, do not emit". Existing keys are never emitted, so the pack never depends on
    /// override order (see docs/LANGUAGE_MODE_PLAN.md section 1 for the verified duplicate-key behaviour).
    /// </summary>
    public sealed class ExistingTranslationIndex
    {
        public HashSet<string> Keyed { get; } = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>"DefType:defName.field...", DefType being the folder name (also the version without a trailing s).</summary>
        public HashSet<string> DefInjected { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>"DefType:defName" -> existing paths split into segments, used to detect the same field written in another path form.</summary>
        public Dictionary<string, List<string[]>> DefsTouched { get; } = new Dictionary<string, List<string[]>>(StringComparer.OrdinalIgnoreCase);
        public List<string> Sources { get; } = new List<string>();

        public bool Contains(SourceEntry entry)
        {
            if (entry.Kind == EntryKind.Keyed)
            {
                return Keyed.Contains(entry.InjectionPath);
            }
            return entry.CanonicalPathCandidates.Any(DefInjected.Contains);
        }

        /// <summary>
        /// Whether an existing translation may target the same field under a different path form, so a duplicate cannot be ruled out.
        /// Our paths always use numeric list indices. The same list item can also be written with a handle (stages.Name.label
        /// instead of stages.0.label), and a whole list can be injected at once (rulesStrings with li children instead of
        /// rulesStrings.0). So an entry is ambiguous only when, at one of its numeric segments, an existing path for the same
        /// Def has the same prefix followed by a non-numeric segment (a handle) or ends right there (a full-list injection).
        /// Fields without list indices have only one path form and are never ambiguous.
        /// </summary>
        public bool TouchesDef(SourceEntry entry)
        {
            if (entry.Kind != EntryKind.Def)
            {
                return false;
            }
            var segments = entry.InjectionPath.Split('.');
            var existing = new[] { entry.DefType! }.Concat(entry.DefTypeAliases)
                .SelectMany(n => DefsTouched.TryGetValue(n + ":" + segments[0], out var l) ? l : Enumerable.Empty<string[]>())
                .ToList();
            if (existing.Count == 0)
            {
                return false;
            }
            for (int i = 1; i < segments.Length; i++)
            {
                if (!IsIndex(segments[i]))
                {
                    continue;
                }
                foreach (var other in existing)
                {
                    if (other.Length < i || !SamePrefix(segments, other, i))
                    {
                        continue;
                    }
                    if (other.Length == i || !IsIndex(other[i]))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsIndex(string segment) => segment.Length > 0 && segment.All(char.IsDigit);

        private static bool SamePrefix(string[] a, string[] b, int length)
        {
            for (int k = 0; k < length; k++)
            {
                if (!string.Equals(a[k], b[k], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Reads one language folder (e.g. .../Languages/ChineseTraditional).</summary>
        public void AddLanguageFolder(string languageDir)
        {
            if (!Directory.Exists(languageDir))
            {
                return;
            }
            Sources.Add(languageDir);
            var keyed = Path.Combine(languageDir, "Keyed");
            if (Directory.Exists(keyed))
            {
                foreach (var file in Directory.GetFiles(keyed, "*.xml", SearchOption.AllDirectories))
                {
                    var doc = TryLoad(file);
                    if (doc?.Root == null)
                    {
                        continue;
                    }
                    foreach (var e in doc.Root.Elements())
                    {
                        if (!IsPlaceholder(e.Value))
                        {
                            Keyed.Add(e.Name.LocalName);
                        }
                    }
                }
            }
            var defInjected = Path.Combine(languageDir, "DefInjected");
            if (Directory.Exists(defInjected))
            {
                foreach (var typeDir in Directory.GetDirectories(defInjected))
                {
                    var typeName = Path.GetFileName(typeDir);
                    var names = new List<string> { typeName };
                    if (typeName.Length > 3 && typeName.EndsWith("s", StringComparison.Ordinal))
                    {
                        names.Add(typeName.Substring(0, typeName.Length - 1));
                    }
                    foreach (var file in Directory.GetFiles(typeDir, "*.xml", SearchOption.AllDirectories))
                    {
                        var doc = TryLoad(file);
                        if (doc?.Root == null)
                        {
                            continue;
                        }
                        foreach (var e in doc.Root.Elements())
                        {
                            string path;
                            if (e.Name.LocalName == "rep")
                            {
                                path = e.Element("path")?.Value ?? string.Empty;
                            }
                            else
                            {
                                path = e.Name.LocalName;
                            }
                            path = path.Replace("]", string.Empty).Replace('[', '.');
                            if (path.Length == 0 || (!e.HasElements && IsPlaceholder(e.Value)))
                            {
                                continue;
                            }
                            foreach (var n in names)
                            {
                                DefInjected.Add(n + ":" + path);
                                var parts = path.Split('.');
                                var defKey = n + ":" + parts[0];
                                if (!DefsTouched.TryGetValue(defKey, out var list))
                                {
                                    list = new List<string[]>();
                                    DefsTouched[defKey] = list;
                                }
                                list.Add(parts);
                            }
                        }
                    }
                }
            }
        }

        private static bool IsPlaceholder(string value) => value == "TODO";

        private static XDocument? TryLoad(string file)
        {
            try
            {
                return SafeXml.LoadFile(file);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>Writes RimWorld LanguageData XML (Keyed and DefInjected share the format: root LanguageData, child name = key/path).</summary>
    public static class LanguageDataWriter
    {
        public sealed class Item
        {
            public Item(string key, string value, string? englishComment)
            {
                Key = key;
                Value = value;
                EnglishComment = englishComment;
            }

            public string Key { get; }
            public string Value { get; }
            public string? EnglishComment { get; }
        }

        public static string Render(IEnumerable<Item> items)
        {
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                OmitXmlDeclaration = false,
                CheckCharacters = true,
            };
            var sb = new StringBuilder();
            using (var sw = new Utf8StringWriter(sb))
            using (var w = XmlWriter.Create(sw, settings))
            {
                w.WriteStartDocument();
                w.WriteStartElement("LanguageData");
                foreach (var item in items)
                {
                    XmlConvert.VerifyName(item.Key);
                    if (item.EnglishComment != null)
                    {
                        w.WriteComment(" EN: " + SafeComment(item.EnglishComment) + " ");
                    }
                    w.WriteElementString(item.Key, item.Value);
                }
                w.WriteEndElement();
                w.WriteEndDocument();
            }
            return sb.ToString() + "\n";
        }

        /// <summary>XML comments must not contain "--" or end with "-".</summary>
        public static string SafeComment(string text)
        {
            var s = text.Replace("\r", " ").Replace("\n", "\\n");
            while (s.Contains("--"))
            {
                s = s.Replace("--", "- -");
            }
            if (s.EndsWith("-", StringComparison.Ordinal))
            {
                s += " ";
            }
            return s;
        }

        private sealed class Utf8StringWriter : StringWriter
        {
            public Utf8StringWriter(StringBuilder sb) : base(sb) { }
            public override Encoding Encoding => new UTF8Encoding(false);
        }
    }
}
