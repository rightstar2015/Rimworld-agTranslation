using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

/// <summary>
/// One glossary entry.
/// kind = "s2t": Simplified term -> Traditional term, used when converting Simplified to Traditional (wins over official terms and OpenCC).
/// kind = "en": English term -> translation in <see cref="Language"/>, shown to agents in get_batch for consistency.
/// origin: builtin, user (added with the CLI) or agent (added through MCP glossary_add).
/// </summary>
public sealed class GlossaryEntry
{
    public string Kind { get; set; } = "en";
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    /// <summary>Target language of an "en" entry. Entries written before languages existed are Traditional.</summary>
    public string? Language { get; set; }
    public string? Note { get; set; }
    public string Origin { get; set; } = "user";
    public DateTime UpdatedUtc { get; set; }

    /// <summary>The language of the target text: always Traditional for s2t.</summary>
    public string TargetLanguage => Kind == "s2t" ? TargetLanguages.Traditional : Language ?? TargetLanguages.Traditional;
}

/// <summary>glossary.json in the work folder. This class is the only writer.</summary>
public sealed class UserGlossary
{
    public const int MaxEntries = 5000;
    private static readonly Regex EnglishTerm = new(@"^[A-Za-z0-9][A-Za-z0-9 '\-\.]{0,63}$", RegexOptions.CultureInvariant);

    /// <summary>Taiwan forms OpenCC does not produce. Only a few verified terms; users add the rest.</summary>
    public static readonly IReadOnlyList<GlossaryEntry> Builtin = new[]
    {
        new GlossaryEntry { Kind = "s2t", Source = "身份", Target = "身分", Note = "Ministry of Education form", Origin = "builtin" },
        new GlossaryEntry { Kind = "s2t", Source = "调试", Target = "偵錯", Note = "Taiwan software term for debug", Origin = "builtin" },
    };

    private readonly string _path;

    private UserGlossary(string path)
    {
        _path = path;
    }

    public List<GlossaryEntry> Entries { get; private set; } = new();

    /// <summary>Built-in plus file entries; on the same kind+source(+language) the file wins.</summary>
    public IEnumerable<GlossaryEntry> Effective =>
        Entries.Concat(Builtin.Where(b => !Entries.Any(e => SameKey(e, b.Kind, b.Source, b.TargetLanguage))));

    /// <summary>Entries relevant to a target language: its "en" entries, plus s2t entries in Traditional mode.</summary>
    public IEnumerable<GlossaryEntry> For(string language) =>
        Effective.Where(e => e.Kind == "en" ? e.TargetLanguage == language : language == TargetLanguages.Traditional);

    private static bool SameKey(GlossaryEntry e, string kind, string source, string language) =>
        e.Kind == kind
        && e.TargetLanguage == language
        && string.Equals(e.Source, source, kind == "en" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public static UserGlossary Load(string workDir)
    {
        var g = new UserGlossary(PathSandbox.Resolve(workDir, "glossary.json"));
        if (File.Exists(g._path))
        {
            var info = new FileInfo(g._path);
            if (info.Length > 8 * 1024 * 1024)
            {
                throw new InvalidOperationException("glossary.json is larger than 8 MB.");
            }
            var list = JsonSerializer.Deserialize<List<GlossaryEntry>>(File.ReadAllText(g._path), AppConfig.Json) ?? new();
            // The file may be edited by hand: validate every entry and skip invalid ones.
            g.Entries = list.Where(e => Check(e.Kind, e.Source, e.Target, e.Note, e.TargetLanguage) == null).Take(MaxEntries).ToList();
        }
        return g;
    }

    /// <summary>Validates an entry; returns null when valid, otherwise the reason.</summary>
    public static string? Check(string? kind, string? source, string? target, string? note, string? language)
    {
        if (kind is not ("s2t" or "en"))
        {
            return "kind must be s2t or en.";
        }
        if (!TargetLanguages.IsValid(language))
        {
            return "language must be ChineseSimplified or ChineseTraditional.";
        }
        if (kind == "s2t" && language != TargetLanguages.Traditional)
        {
            return "s2t entries always target ChineseTraditional.";
        }
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
        {
            return "source and target must not be empty.";
        }
        if (target.Length > 64 || target.Any(char.IsControl) || target.IndexOfAny(new[] { '<', '>', '{', '}' }) >= 0)
        {
            return "target must be at most 64 characters, without control characters or < > { }.";
        }
        if (note != null && (note.Length > 200 || note.Any(char.IsControl)))
        {
            return "note must be at most 200 characters, without control characters.";
        }
        var wrong = language == TargetLanguages.Traditional ? SimplifiedChars.FindIn(target) : TraditionalChars.FindIn(target);
        if (wrong.Count > 0)
        {
            return "target contains characters of the other script: " + string.Join("", wrong.Take(5));
        }
        if (kind == "s2t")
        {
            // At least 2 characters, so one rule cannot rewrite every text.
            if (source.Length < 2 || source.Length > 16 || !source.All(IsCjk) || !target.All(IsCjk))
            {
                return "s2t source and target must be 2-16 Chinese characters.";
            }
        }
        else if (!EnglishTerm.IsMatch(source))
        {
            return "en source may contain only letters, digits, spaces, ' - . (at most 64 characters).";
        }
        return null;
    }

    public GlossaryEntry Add(string kind, string source, string target, string? note, string origin, string language)
    {
        source = source.Trim();
        target = target.Trim();
        if (kind == "s2t")
        {
            language = TargetLanguages.Traditional;
        }
        var problem = Check(kind, source, target, note, language);
        if (problem != null)
        {
            throw new ArgumentException(problem);
        }
        var existing = Entries.FindIndex(e => SameKey(e, kind, source, language));
        if (existing < 0 && Entries.Count >= MaxEntries)
        {
            throw new ArgumentException("The glossary is full (" + MaxEntries + " entries).");
        }
        if (existing >= 0 && origin == "agent" && Entries[existing].Origin == "user")
        {
            throw new ArgumentException("This term was set by the user and cannot be changed by an agent: " + source + " -> " + Entries[existing].Target);
        }
        var entry = new GlossaryEntry
        {
            Kind = kind,
            Source = source,
            Target = target,
            Language = kind == "en" ? language : null,
            Note = note,
            Origin = origin,
            UpdatedUtc = DateTime.UtcNow,
        };
        if (existing >= 0)
        {
            Entries[existing] = entry;
        }
        else
        {
            Entries.Add(entry);
        }
        TranslationStore.WriteAtomic(_path, JsonSerializer.Serialize(Entries, AppConfig.Json));
        return entry;
    }

    public bool Remove(string kind, string source, string language)
    {
        if (kind == "s2t")
        {
            language = TargetLanguages.Traditional;
        }
        var n = Entries.RemoveAll(e => SameKey(e, kind, source, language));
        if (n > 0)
        {
            TranslationStore.WriteAtomic(_path, JsonSerializer.Serialize(Entries, AppConfig.Json));
        }
        return n > 0;
    }

    private static bool IsCjk(char c) => c >= 0x3400 && c <= 0x9FFF;
}
