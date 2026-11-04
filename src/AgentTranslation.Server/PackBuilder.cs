using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

public sealed class PackManifest
{
    public string Tool { get; set; } = "rwat";
    public string ToolVersion { get; set; } = "0.1.0";
    public DateTime BuiltUtc { get; set; }
    public string GameVersion { get; set; } = string.Empty;
    public List<string> Files { get; set; } = new();
    public List<ManifestEntry> Entries { get; set; } = new();
}

public sealed class ManifestEntry
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<string> Flags { get; set; } = new();
}

/// <summary>
/// Builds the translation pack. Writes only inside &lt;Mods&gt;/&lt;PackFolderName&gt; and deletes only files listed in the previous manifest.
/// Layout: &lt;version&gt;/Languages/&lt;TargetLanguage&gt;/{Keyed,DefInjected/&lt;DefType&gt;}/&lt;mod&gt;/&lt;file&gt;.
/// Per 1.7.4998 ModContentPack.InitLoadFolders, without LoadFolders.xml the game loads root/&lt;current version&gt;,
/// so no LoadFolders.xml is written (1.7 also marks the "default" version as deprecated).
/// The pack holds one language at a time; switching target_language and rebuilding replaces the other language's files.
/// </summary>
public sealed class PackBuilder
{
    private const string ManifestName = ".agent-manifest.json";
    private readonly AppConfig _config;
    private readonly GameEnvironment _env;
    private readonly ModScanner _scanner;
    private readonly TranslationStore _store;
    private readonly ValidatorOptions _options;

    public PackBuilder(AppConfig config, GameEnvironment env, ModScanner scanner, TranslationStore store, ValidatorOptions options)
    {
        _config = config;
        _env = env;
        _scanner = scanner;
        _store = store;
        _options = options;
    }

    private sealed class Planned
    {
        public Dictionary<string, List<(TranslationRecord Record, SourceEntry Entry)>> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string PackageId, TranslationRecord Record, string Reason)> Skipped { get; } = new();
        public List<InstalledMod> Mods { get; } = new();
    }

    private Planned Plan(IReadOnlyCollection<string>? onlyIds)
    {
        var plan = new Planned();
        var langRoot = _env.Version + "/Languages/" + _config.TargetLanguageFolder + "/";
        foreach (var pkg in _store.AllPackageIds().Distinct().OrderBy(p => p, StringComparer.Ordinal))
        {
            if (onlyIds != null && !onlyIds.Contains(pkg))
            {
                continue;
            }
            var mod = _env.Find(pkg);
            var store = _store.Get(pkg);
            if (mod == null)
            {
                foreach (var r in store.Records.Values)
                {
                    plan.Skipped.Add((pkg, r, "mod_not_installed"));
                }
                continue;
            }
            var scan = _scanner.Scan(mod);
            var wrote = false;
            var modDir = TranslationStore.SafeFileStem(pkg);
            foreach (var r in store.Records.Values.OrderBy(r => r.SourceFile, StringComparer.Ordinal).ThenBy(r => r.Path, StringComparer.Ordinal))
            {
                if (!scan.ById.TryGetValue(r.Id, out var entry))
                {
                    plan.Skipped.Add((pkg, r, "stale_source_changed"));
                    continue;
                }
                if (scan.AlreadyTranslated.Contains(r.Id) || scan.Ambiguous.Contains(r.Id))
                {
                    plan.Skipped.Add((pkg, r, "translated_elsewhere"));
                    continue;
                }
                var revalidate = Validator.Validate(entry.Source, r.Text, _options);
                if (!revalidate.Accepted)
                {
                    plan.Skipped.Add((pkg, r, "fails_current_validation:" + string.Join(",", revalidate.Rejects.Select(x => x.Code))));
                    continue;
                }
                string rel;
                if (entry.Kind == EntryKind.Keyed)
                {
                    var marker = "/Keyed/";
                    var i = entry.SourceFile.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    var tail = i >= 0 ? entry.SourceFile.Substring(i + marker.Length) : Path.GetFileName(entry.SourceFile);
                    rel = langRoot + "Keyed/" + modDir + "/" + SafeRelative(tail);
                }
                else
                {
                    var marker = "Defs/";
                    var i = entry.SourceFile.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    var tail = i >= 0 ? entry.SourceFile.Substring(i + marker.Length) : Path.GetFileName(entry.SourceFile);
                    rel = langRoot + "DefInjected/" + SafeSegment(entry.DefType!) + "/" + modDir + "/" + SafeSegment(tail.Replace('/', '_'));
                }
                if (!plan.Files.TryGetValue(rel, out var list))
                {
                    list = new List<(TranslationRecord, SourceEntry)>();
                    plan.Files[rel] = list;
                }
                list.Add((r, entry));
                wrote = true;
            }
            if (wrote)
            {
                plan.Mods.Add(mod);
            }
        }
        return plan;
    }

    public object Build(IReadOnlyCollection<string>? onlyIds)
    {
        var packDir = _config.PackDir;
        var modsDir = Path.GetDirectoryName(packDir)!;
        if (!Directory.Exists(modsDir))
        {
            throw new InvalidOperationException("Pack output folder not found: " + modsDir);
        }
        var manifestPath = PathSandbox.Resolve(modsDir, _config.PackFolderName + "/" + ManifestName);
        PackManifest? previous = null;
        if (File.Exists(manifestPath))
        {
            previous = JsonSerializer.Deserialize<PackManifest>(File.ReadAllText(manifestPath), AppConfig.Json);
        }
        else if (Directory.Exists(packDir) && Directory.EnumerateFileSystemEntries(packDir).Any())
        {
            throw new InvalidOperationException("The pack folder exists but was not created by this tool (no manifest). Choose another PackFolderName to avoid overwriting it: " + packDir);
        }

        var plan = Plan(onlyIds);
        if (onlyIds != null && previous != null)
        {
            // Partial builds would have to keep the other mods' previous output.
            throw new InvalidOperationException("Only full builds are supported (no mod_ids).");
        }

        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var manifest = new PackManifest { BuiltUtc = DateTime.UtcNow, GameVersion = _env.VersionFull };
        foreach (var (rel, items) in plan.Files.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var content = LanguageDataWriter.Render(items.Select(x => new LanguageDataWriter.Item(x.Entry.InjectionPath, x.Record.Text, x.Entry.Source)));
            // Self-check before writing: the safe XML parser must read it back with the same number of entries.
            var check = SafeXml.Parse(content);
            if (check.Root == null || check.Root.Elements().Count() != items.Count)
            {
                throw new InvalidOperationException("Generated XML failed the self-check: " + rel);
            }
            outputs[rel] = content;
            foreach (var (record, entry) in items)
            {
                manifest.Entries.Add(new ManifestEntry
                {
                    Id = record.Id,
                    Key = record.Key,
                    PackageId = entry.PackageId,
                    Kind = record.Kind,
                    Path = record.Path,
                    File = rel,
                    Status = record.Status,
                    Flags = record.Flags.Select(f => f.Code).ToList(),
                });
            }
        }
        outputs["About/About.xml"] = AboutXml(plan.Mods);
        outputs["README.txt"] = Readme();

        Directory.CreateDirectory(packDir);
        PathSandbox.NormalizeRoot(packDir);
        var removed = 0;
        if (previous != null)
        {
            foreach (var old in previous.Files)
            {
                if (!outputs.ContainsKey(old))
                {
                    var p = PathSandbox.Resolve(packDir, old);
                    if (File.Exists(p))
                    {
                        File.Delete(p);
                        removed++;
                    }
                }
            }
        }
        var written = 0;
        var hashes = new List<object>();
        foreach (var (rel, content) in outputs)
        {
            var p = PathSandbox.Resolve(packDir, rel);
            var bytes = new UTF8Encoding(false).GetBytes(content);
            if (!File.Exists(p) || !File.ReadAllBytes(p).AsSpan().SequenceEqual(bytes))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                var tmp = p + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                File.Move(tmp, p, overwrite: true);
                written++;
            }
            hashes.Add(new { file = rel, sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
        }
        manifest.Files = outputs.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        TranslationStore.WriteAtomic(manifestPath, JsonSerializer.Serialize(manifest, AppConfig.Json));
        RemoveEmptyDirs(packDir);

        var packMod = _env.Find(_config.PackPackageId);
        var lastTranslated = plan.Mods.Count == 0 ? -1 : plan.Mods.Max(m => m.LoadOrder);
        return new
        {
            pack_dir = packDir,
            files_written = written,
            files_removed = removed,
            entries = manifest.Entries.Count,
            flagged = manifest.Entries.Count(e => e.Status == "flagged"),
            mods = plan.Mods.Select(m => m.About.PackageIdRaw).ToList(),
            skipped = plan.Skipped.GroupBy(s => s.Reason).Select(g => new { reason = g.Key, count = g.Count() }).ToList(),
            pack_active = packMod?.Active ?? false,
            load_order_ok = packMod == null || !packMod.Active ? (bool?)null : packMod.LoadOrder > lastTranslated,
            next_step = packMod?.Active == true
                ? (packMod.LoadOrder > lastTranslated ? "Restart the game to load it." : "The pack must load after every translated mod; fix the order in the mod list.")
                : "Enable \"" + _config.EffectivePackDisplayName + "\" in the mod list, after every translated mod, with the game language set to " + _config.TargetLanguage + ".",
            target_language = _config.TargetLanguage,
            hashes,
        };
    }

    public object Validate(string? onlyId, object? runtimeCheck = null)
    {
        var plan = Plan(onlyId == null ? null : new[] { onlyId });
        var flaggedEntries = new List<object>();
        foreach (var (rel, items) in plan.Files)
        {
            foreach (var (record, entry) in items)
            {
                if (record.Status == "flagged")
                {
                    flaggedEntries.Add(new { id = record.Id, package_id = entry.PackageId, path = entry.InjectionPath, source = entry.Source, text = record.Text, flags = record.Flags });
                }
            }
        }
        // Check the pack files already written: valid XML, no duplicate keys, no TODO.
        var packProblems = new List<string>();
        var manifestPath = Path.Combine(_config.PackDir, ManifestName);
        if (File.Exists(manifestPath))
        {
            var manifest = JsonSerializer.Deserialize<PackManifest>(File.ReadAllText(manifestPath), AppConfig.Json);
            var keyedKeys = new HashSet<string>(StringComparer.Ordinal);
            var defKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in manifest?.Files ?? new List<string>())
            {
                if (!rel.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("About/", StringComparison.Ordinal))
                {
                    continue;
                }
                var p = Path.Combine(_config.PackDir, rel);
                if (!File.Exists(p))
                {
                    packProblems.Add("Missing file: " + rel);
                    continue;
                }
                try
                {
                    var doc = SafeXml.LoadFile(p);
                    var isKeyed = rel.Contains("/Keyed/", StringComparison.Ordinal);
                    var defType = isKeyed ? null : rel.Split('/')[4];
                    foreach (var e in doc.Root!.Elements())
                    {
                        var k = isKeyed ? e.Name.LocalName : defType + ":" + e.Name.LocalName;
                        if (!(isKeyed ? keyedKeys.Add(k) : defKeys.Add(k)))
                        {
                            packProblems.Add("Duplicate key: " + k + " (" + rel + ")");
                        }
                        if (e.Value == "TODO" || e.Value.Length == 0)
                        {
                            packProblems.Add("Empty value or TODO: " + k);
                        }
                    }
                }
                catch (Exception ex)
                {
                    packProblems.Add("XML cannot be parsed: " + rel + ": " + ex.Message);
                }
            }
        }
        return new
        {
            ready_entries = plan.Files.Sum(f => f.Value.Count),
            files = plan.Files.Count,
            flagged = flaggedEntries,
            skipped = plan.Skipped.Select(s => new { package_id = s.PackageId, id = s.Record.Id, path = s.Record.Path, reason = s.Reason }).ToList(),
            pack_built = File.Exists(manifestPath),
            pack_problems = packProblems,
            runtime_check = runtimeCheck,
        };
    }

    private string AboutXml(List<InstalledMod> mods)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<ModMetaData>\n");
        sb.Append("  <packageId>").Append(SecurityElement.Escape(_config.PackPackageId)).Append("</packageId>\n");
        sb.Append("  <name>").Append(SecurityElement.Escape(_config.EffectivePackDisplayName)).Append("</name>\n");
        sb.Append("  <author>RimWorld Agent Translation (generated locally)</author>\n");
        sb.Append("  <supportedVersions>\n    <li>").Append(_env.Version).Append("</li>\n  </supportedVersions>\n");
        if (mods.Count > 0)
        {
            sb.Append("  <loadAfter>\n");
            foreach (var m in mods.OrderBy(m => m.LoadOrder))
            {
                sb.Append("    <li>").Append(SecurityElement.Escape(m.About.PackageIdRaw)).Append("</li>\n");
            }
            sb.Append("  </loadAfter>\n");
        }
        var tc = _config.IsTraditional;
        sb.Append("  <description>")
          .Append(tc ? "由 AI Agent 經 rwat 工具產生的繁體中文翻譯包（機器翻譯，未經人工校對）。\n\n【重要】此模組必須放在所有被翻譯模組之後載入。\n不修改任何原始模組；刪除此模組即可完全移除翻譯。\n\n翻譯模組："
                     : "由 AI Agent 经 rwat 工具生成的简体中文翻译包（机器翻译，未经人工校对）。\n\n【重要】此模组必须放在所有被翻译模组之后加载。\n不修改任何原始模组；删除此模组即可完全移除翻译。\n\n翻译模组：")
          .Append(mods.Count)
          .Append("\n\nMachine translation generated locally by an AI agent through rwat. Load after every translated mod.</description>\n");
        sb.Append("</ModMetaData>\n");
        return sb.ToString();
    }

    private string Readme()
    {
        return "Generated by rwat (RimWorld Agent Translation). Do not edit by hand; the next build overwrites it.\n\n"
            + "Load order: this pack must load after every translated mod. About.xml lists them in loadAfter, which the game's auto-sort follows.\n"
            + "Game language: " + _config.TargetLanguage + ".\n"
            + "Content: translations were produced by the AI agent the user chose; the translated mod text was sent to that agent's provider.\n"
            + "Removal: delete this folder. No original mod is changed.\n";
    }

    private static string SafeRelative(string rel)
    {
        return string.Join("/", rel.Split('/', '\\').Where(p => p.Length > 0).Select(SafeSegment));
    }

    private static string SafeSegment(string s)
    {
        var chars = s.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ' ' ? c : '_').ToArray();
        var r = new string(chars).Trim().Trim('.');
        return r.Length == 0 ? "_" : r;
    }

    private static void RemoveEmptyDirs(string root)
    {
        foreach (var d in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(d).Any() && (File.GetAttributes(d) & FileAttributes.ReparsePoint) == 0)
            {
                Directory.Delete(d);
            }
        }
    }
}
