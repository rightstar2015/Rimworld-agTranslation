using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

/// <summary>一個模組的掃描結果。</summary>
public sealed class ModScan
{
    public required InstalledMod Mod { get; init; }
    public List<SourceEntry> Entries { get; init; } = new();
    public List<ScanNote> Notes { get; init; } = new();
    /// <summary>已有其他來源（含模組自身）提供繁中翻譯的條目 id。</summary>
    public HashSet<string> AlreadyTranslated { get; init; } = new(StringComparer.Ordinal);
    /// <summary>同一個 Def 已被其他翻譯以可能不同的路徑寫法觸及，保守起見不輸出。</summary>
    public HashSet<string> Ambiguous { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, SourceEntry> ById { get; init; } = new(StringComparer.Ordinal);

    public IEnumerable<SourceEntry> Translatable => Entries.Where(e => !AlreadyTranslated.Contains(e.Id) && !Ambiguous.Contains(e.Id));
}

/// <summary>
/// 掃描模組：解析載入資料夾、Defs 繼承、型別中繼資料，抽出待翻譯條目。只讀取檔案。
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
            throw new InvalidOperationException("找不到 Assembly-CSharp.dll，無法判斷欄位是否可翻譯。");
        }
        _schema.AddAssembly(core);
    }

    public IReadOnlyList<string> SchemaWarnings => _schema.Warnings;

    public ModScan Scan(InstalledMod mod)
    {
        if (_cache.TryGetValue(mod.PackageId, out var cached))
        {
            return cached;
        }
        LoadModAssemblies(mod);
        var scan = ScanOnce(mod);
        if (!_allAssembliesLoaded && scan.Notes.Any(n => n.Code == "unknown_class" || n.Code == "unknown_def_type"))
        {
            // Class 可能定義在依賴模組中：載入所有啟用模組的組件後重掃一次。
            foreach (var m in _env.ActiveMods)
            {
                LoadModAssemblies(m);
            }
            _allAssembliesLoaded = true;
            scan = ScanOnce(mod);
        }
        _cache[mod.PackageId] = scan;
        return scan;
    }

    public void Invalidate(string packageId) => _cache.Remove(PackageIds.Normalize(packageId));

    private ModScan ScanOnce(InstalledMod mod)
    {
        var result = new ExtractionResult();
        var folders = _env.LoadFoldersFor(mod);

        // Keyed（英文）
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

        // 同一路徑只保留一筆（後定義者覆蓋，與 DefDatabase 同 defName 後者覆蓋的行為一致【未確認】）。
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

        var existing = Existing();
        var scan = new ModScan { Mod = mod, Entries = entries, Notes = result.Notes };
        foreach (var e in entries)
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
        }
        return scan;
    }

    /// <summary>所有啟用模組（不含本翻譯包）中已存在的目標語言翻譯。</summary>
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
    /// 找父節點：先找本模組，再找官方資料（Core 與 DLC），最後才逐一載入其他載入順序較前的模組。
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
