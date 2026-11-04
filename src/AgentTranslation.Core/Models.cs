using System;
using System.Collections.Generic;

namespace AgentTranslation.Core
{
    /// <summary>Entry kind. Its numeric value and name feed the stable ID hash and must never change after release.</summary>
    public enum EntryKind
    {
        /// <summary>A key/value from Languages/English/Keyed.</summary>
        Keyed,
        /// <summary>A field extracted from Defs XML and written in DefInjected format.</summary>
        Def,
    }

    public static class EntryKindNames
    {
        public static string ToId(EntryKind kind)
        {
            switch (kind)
            {
                case EntryKind.Keyed: return "keyed";
                case EntryKind.Def: return "def";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static bool TryParse(string? value, out EntryKind kind)
        {
            switch (value)
            {
                case "keyed": kind = EntryKind.Keyed; return true;
                case "def": kind = EntryKind.Def; return true;
                default: kind = default; return false;
            }
        }
    }

    /// <summary>One source entry to translate. Produced by the extractors; its content is untrusted mod text.</summary>
    public sealed class SourceEntry
    {
        public SourceEntry(string packageId, EntryKind kind, string? defType, string injectionPath, string source, string sourceFile)
        {
            PackageId = PackageIds.Normalize(packageId);
            Kind = kind;
            DefType = defType;
            InjectionPath = injectionPath;
            Source = source;
            SourceFile = sourceFile;
            CanonicalPath = kind == EntryKind.Def ? defType + ":" + injectionPath : injectionPath;
            Key = StableId.Key(PackageId, kind, CanonicalPath);
            Id = StableId.Id(PackageId, kind, CanonicalPath, source);
        }

        /// <summary>Lower-case packageId.</summary>
        public string PackageId { get; }
        public EntryKind Kind { get; }
        /// <summary>Def type name (e.g. ThingDef); null for Keyed.</summary>
        public string? DefType { get; }
        /// <summary>Keyed: the key; Def: the DefInjected path in defName.field.0.field form.</summary>
        public string InjectionPath { get; }
        /// <summary>Path that feeds the hash: the key for Keyed; "DefType:path" for Def.</summary>
        public string CanonicalPath { get; }
        /// <summary>Source text (as in the XML; a literal \n stays as two characters).</summary>
        public string Source { get; }
        /// <summary>Source file, relative to the mod root, '/'-separated.</summary>
        public string SourceFile { get; }
        public string Key { get; }
        public string Id { get; }

        /// <summary>Label of the owning Def (context only; may be null).</summary>
        public string? DefLabel { get; set; }
        /// <summary>Description of the owning Def (context only; may be null).</summary>
        public string? DefDescription { get; set; }

        /// <summary>
        /// Other names under which a DefInjected translation of this entry takes effect: every class in the Def's
        /// inheritance chain (a Def is added to the database of each of them), short and full forms. Not part of the ID.
        /// </summary>
        public IReadOnlyList<string> DefTypeAliases { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Set for a Keyed key that mod code passes to Translate() but no language file defines (stage 4, opt-in).
        /// Such an entry has no English source: Source is empty and the agent infers the text from the key and context.
        /// </summary>
        public CodeKeyContext? Code { get; set; }

        /// <summary>CanonicalPath under every accepted Def type name (just CanonicalPath for Keyed).</summary>
        public IEnumerable<string> CanonicalPathCandidates
        {
            get
            {
                yield return CanonicalPath;
                if (Kind == EntryKind.Def)
                {
                    foreach (var alias in DefTypeAliases)
                    {
                        yield return alias + ":" + InjectionPath;
                    }
                }
            }
        }
    }

    /// <summary>Where mod code uses a key that no language file defines. Context only, not part of the ID.</summary>
    public sealed class CodeKeyContext
    {
        public CodeKeyContext(string assembly, string type, string method, int? argCount, IReadOnlyList<KeyValuePair<string, string>> nearby)
        {
            Assembly = assembly;
            Type = type;
            Method = method;
            ArgCount = argCount;
            Nearby = nearby;
        }

        public string Assembly { get; }
        public string Type { get; }
        public string Method { get; }
        /// <summary>Format arguments passed with the key; null when unknown.</summary>
        public int? ArgCount { get; }
        /// <summary>Other keys used in the same method, with their English text when known.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Nearby { get; }
    }

    /// <summary>A problem met during extraction that does not affect other entries.</summary>
    public sealed class ScanNote
    {
        public ScanNote(string code, string detail, string? file = null)
        {
            Code = code;
            Detail = detail;
            File = file;
        }

        public string Code { get; }
        public string Detail { get; }
        public string? File { get; }
    }

    public sealed class ExtractionResult
    {
        public List<SourceEntry> Entries { get; } = new List<SourceEntry>();
        public List<ScanNote> Notes { get; } = new List<ScanNote>();
    }

    public static class PackageIds
    {
        /// <summary>RimWorld compares packageIds case-insensitively; also strips the _steam/_copy postfix added on name clashes.</summary>
        public static string Normalize(string packageId)
        {
            var id = (packageId ?? string.Empty).Trim().ToLowerInvariant();
            return id;
        }

        public static string WithoutPostfix(string packageId)
        {
            var id = Normalize(packageId);
            foreach (var postfix in new[] { "_steam", "_copy" })
            {
                if (id.EndsWith(postfix, StringComparison.Ordinal))
                {
                    return id.Substring(0, id.Length - postfix.Length);
                }
            }
            return id;
        }
    }
}
