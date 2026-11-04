using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

/// <summary>Scan result of one mod.</summary>
public sealed class ModScan
{
    public required InstalledMod Mod { get; init; }
    public List<SourceEntry> Entries { get; init; } = new();
    public List<ScanNote> Notes { get; init; } = new();
    /// <summary>Ids of entries that already have a target-language translation from another source (including the mod itself).</summary>
    public HashSet<string> AlreadyTranslated { get; init; } = new(StringComparer.Ordinal);
    /// <summary>The same Def is already touched by another translation, possibly with a different path form; not emitted, to be safe.</summary>
    public HashSet<string> Ambiguous { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, SourceEntry> ById { get; init; } = new(StringComparer.Ordinal);
    /// <summary>Entries whose source text is already written in the target language (e.g. a Chinese mod's Defs in Simplified mode).</summary>
    public HashSet<string> SourceInTarget { get; init; } = new(StringComparer.Ordinal);

    public IEnumerable<SourceEntry> Translatable => Entries.Where(e => !AlreadyTranslated.Contains(e.Id) && !Ambiguous.Contains(e.Id) && !SourceInTarget.Contains(e.Id));
}

/// <summary>
/// Scans a mod: resolves load folders, Defs inheritance and type metadata, and extracts entries to translate. Reads files only.
/// </summary>
public sealed class ModScanner : IDisposable
{
    private readonly AppConfig _config;
    private readonly GameEnvironment _env;
    private readonly MetadataTypeSchema _schema = new();
    private readonly Dictionary<string, List<DefNode>> _defsByMod = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ModScan> _cache = new(StringComparer.Ordinal);
    private ExistingTranslationIndex? _existing;
    private bool _allAssembliesLoaded;

    public ModScanner(AppConfig config, GameEnvironment env)
    {
        _config = config;
        _env = env;
        var core = Path.Combine(env.GameDir, "Assembly-CSharp.dll");
        if (!File.Exists(core))
        {
            core = Path.Combine(env.GameDir, "RimWorldWin64_Data", "Managed", "Assembly-CSharp.dll");
        }
        if (!File.Exists(core))
        {
            throw new InvalidOperationException("Assembly-CSharp.dll not found; cannot tell which fields are translatable.");
        }
        _schema.AddAssembly(core);
    }

    public IReadOnlyList<string> SchemaWarnings => _schema.Warnings;

    private readonly HashSet<string> _forceFresh = new(StringComparer.Ordinal);

    public ModScan Scan(InstalledMod mod)
    {
        if (_cache.TryGetValue(mod.PackageId, out var cached))
        {
            return cached;
        }
        var fresh = _forceFresh.Remove(mod.PackageId);
        var fingerprint = ScanCache.Fingerprint(_env, mod);
        var extracted = fresh ? null : ScanCache.TryLoad(_config.WorkDir, mod.PackageId, fingerprint);
        if (extracted == null)
        {
            LoadModAssemblies(mod);
            extracted = Extract(mod);
            if (!_allAssembliesLoaded && extracted.Notes.Any(n => n.Code == "unknown_class" || n.Code == "unknown_def_type"))
            {
                // The Class may live in a dependency: load the assemblies of all active mods and scan again.
                foreach (var m in _env.ActiveMods)
                {
                    LoadModAssemblies(m);
                }
                _allAssembliesLoaded = true;
                extracted = Extract(mod);
            }
            ScanCache.Save(_config.WorkDir, mod.PackageId, fingerprint, extracted);
        }
        var scan = Classify(mod, extracted);
        _cache[mod.PackageId] = scan;
        return scan;
    }

    /// <summary>Drops the memory and disk cache so the next Scan extracts again.</summary>
    public void Invalidate(string packageId)
    {
        var id = PackageIds.Normalize(packageId);
        _cache.Remove(id);
        _forceFresh.Add(id);
    }

    private ExtractionResult Extract(InstalledMod mod)
    {
        var result = new ExtractionResult();
        var folders = _env.LoadFoldersFor(mod);

        // Keyed (English)
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var lang in new[] { "English" })
        {
            foreach (var (rel, full) in LoadFolderResolver.CollectFiles(mod.RootDir, folders, Path.Combine("Languages", lang, "Keyed")))
            {
                try
                {
                    KeyedExtractor.Extract(mod.PackageId, rel, SafeXml.LoadFile(full), result, seenKeys);
                }
                catch (Exception ex)
                {
                    result.Notes.Add(new ScanNote("xml_error", ex.Message, rel));
                }
            }
        }

        // Defs
        var nodes = DefsOf(mod);
        var resolver = new XmlInheritanceResolver(new NodeSource(this, mod));
        var extractor = new DefExtractor(_schema);
        foreach (var node in nodes)
        {
            if (node.IsAbstract)
            {
                continue;
            }
            var resolved = resolver.Resolve(node);
            extractor.Extract(mod.PackageId, node, resolved, result);
        }
        foreach (var w in resolver.Warnings.Distinct())
        {
            result.Notes.Add(new ScanNote("parent_not_found", w));
        }

        // Keep one entry per path (the later definition wins, as DefDatabase does for a repeated defName [unverified]).
        var entries = new List<SourceEntry>();
        var byPath = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in result.Entries)
        {
            var k = EntryKindNames.ToId(e.Kind) + "|" + e.CanonicalPath;
            if (byPath.TryGetValue(k, out var idx))
            {
                entries[idx] = e;
            }
            else
            {
                byPath[k] = entries.Count;
                entries.Add(e);
            }
        }
        entries.Sort((a, b) =>
        {
            var c = a.Kind.CompareTo(b.Kind);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.SourceFile, b.SourceFile);
            if (c != 0) return c;
            return string.CompareOrdinal(a.CanonicalPath, b.CanonicalPath);
        });
        var final = new ExtractionResult();
        final.Entries.AddRange(entries);
        final.Notes.AddRange(result.Notes);
        return final;
    }

    /// <summary>Marks entries already translated, or touched with a possibly different path form, by the target-language translations of all active mods.</summary>
    private ModScan Classify(InstalledMod mod, ExtractionResult extracted)
    {
        var existing = Existing();
        var scan = new ModScan { Mod = mod, Entries = extracted.Entries, Notes = extracted.Notes };
        foreach (var e in scan.Entries)
        {
            scan.ById[e.Id] = e;
            if (existing.Contains(e))
            {
                scan.AlreadyTranslated.Add(e.Id);
            }
            else if (existing.TouchesDef(e))
            {
                scan.Ambiguous.Add(e.Id);
            }
            else if (IsInTargetLanguage(e.Source))
            {
                scan.SourceInTarget.Add(e.Id);
            }
        }
        return scan;
    }

    private static readonly System.Text.RegularExpressions.Regex LatinWord = new(@"[A-Za-z]{3,}", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether a source text is already written in the target language: it has Chinese characters, no Latin word of three
    /// or more letters (outside tokens), and no character that only exists in the other script.
    /// </summary>
    private bool IsInTargetLanguage(string source)
    {
        var text = Tokens.StripTokens(source);
        if (!text.Any(c => c >= 0x3400 && c <= 0x9FFF) || LatinWord.IsMatch(text))
        {
            return false;
        }
        var wrong = _config.IsTraditional ? SimplifiedChars.FindIn(text) : TraditionalChars.FindIn(text);
        return wrong.Count == 0;
    }

    /// <summary>Target-language translations that already exist in all active mods (excluding this pack).</summary>
    public ExistingTranslationIndex Existing()
    {
        if (_existing != null)
        {
            return _existing;
        }
        var index = new ExistingTranslationIndex();
        foreach (var m in _env.ActiveMods)
        {
            if (m.PackageId == PackageIds.Normalize(_config.PackPackageId) || m.Source == "official")
            {
                continue;
            }
            foreach (var folder in _env.LoadFoldersFor(m))
            {
                var languages = Path.Combine(folder, "Languages");
                if (!Directory.Exists(languages))
                {
                    continue;
                }
                foreach (var dir in Directory.GetDirectories(languages))
                {
                    var name = Path.GetFileName(dir);
                    if (name == _config.TargetLanguageFolder || name.StartsWith(_config.TargetLanguageFolder + " (", StringComparison.Ordinal))
                    {
                        index.AddLanguageFolder(dir);
                    }
                }
            }
        }
        _existing = index;
        return index;
    }

    internal List<DefNode> DefsOf(InstalledMod mod)
    {
        if (_defsByMod.TryGetValue(mod.PackageId, out var list))
        {
            return list;
        }
        list = new List<DefNode>();
        var order = mod.LoadOrder >= 0 ? mod.LoadOrder : int.MaxValue / 2;
        foreach (var (rel, full) in LoadFolderResolver.CollectFiles(mod.RootDir, _env.LoadFoldersFor(mod), "Defs"))
        {
            XDocument doc;
            try
            {
                doc = SafeXml.LoadFile(full);
            }
            catch (Exception)
            {
                continue;
            }
            if (doc.Root == null)
            {
                continue;
            }
            foreach (var e in doc.Root.Elements())
            {
                list.Add(new DefNode(e, order, mod.PackageId, rel));
            }
        }
        _defsByMod[mod.PackageId] = list;
        return list;
    }

    private void LoadModAssemblies(InstalledMod mod)
    {
        foreach (var (_, full) in LoadFolderResolver.CollectFiles(mod.RootDir, _env.LoadFoldersFor(mod), "Assemblies", "*.dll"))
        {
            _schema.AddAssembly(full);
        }
    }

    public void Dispose() => _schema.Dispose();

    /// <summary>
    /// Finds parents: this mod first, then official data (Core and DLCs), and only then other mods earlier in the load order, one by one.
    /// </summary>
    private sealed class NodeSource : INamedNodeSource
    {
        private readonly ModScanner _owner;
        private readonly InstalledMod _mod;

        public NodeSource(ModScanner owner, InstalledMod mod)
        {
            _owner = owner;
            _mod = mod;
        }

        public IEnumerable<DefNode> FindNamed(string name)
        {
            var found = _owner.DefsOf(_mod).Where(n => n.Name == name).ToList();
            if (found.Count > 0)
            {
                return found;
            }
            foreach (var m in _owner._env.ActiveMods.Where(m => m.Source == "official"))
            {
                found.AddRange(_owner.DefsOf(m).Where(n => n.Name == name));
            }
            if (found.Count > 0)
            {
                return found;
            }
            var needle = "Name=\"" + name + "\"";
            foreach (var m in _owner._env.ActiveMods.Where(m => m.Source != "official" && m.LoadOrder < _mod.LoadOrder))
            {
                if (!_owner._defsByMod.ContainsKey(m.PackageId) && !ModMentions(m, needle))
                {
                    continue;
                }
                found.AddRange(_owner.DefsOf(m).Where(n => n.Name == name));
            }
            return found;
        }

        private bool ModMentions(InstalledMod m, string needle)
        {
            foreach (var (_, full) in LoadFolderResolver.CollectFiles(m.RootDir, _owner._env.LoadFoldersFor(m), "Defs"))
            {
                try
                {
                    if (File.ReadAllText(full).Contains(needle, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch (IOException)
                {
                }
            }
            return false;
        }
    }
}
