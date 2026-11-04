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
/// 產生翻譯包。只寫入 &lt;Mods&gt;/&lt;PackFolderName&gt;，只刪除上一次 manifest 列出的檔案。
/// 版面：&lt;版本&gt;/Languages/ChineseTraditional/{Keyed,DefInjected/&lt;DefType&gt;}/&lt;模組&gt;/&lt;檔名&gt;。
/// 對照 1.7.4998 ModContentPack.InitLoadFolders：沒有 LoadFolders.xml 時會載入「根目錄/目前版本」資料夾，
/// 因此不輸出 LoadFolders.xml（1.7 也已將 default 版本標為過時）。
/// </summary>
public sealed class PackBuilder
{
    private const string ManifestName = ".agent-manifest.json";
    private readonly AppConfig _config;
    private readonly GameEnvironment _env;
    private readonly ModScanner _scanner;
    private readonly TranslationStore _store;

    public PackBuilder(AppConfig config, GameEnvironment env, ModScanner scanner, TranslationStore store)
    {
        _config = config;
        _env = env;
        _scanner = scanner;
        _store = store;
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
                var revalidate = Validator.Validate(entry.Source, r.Text, new ValidatorOptions { ProperNounWhitelist = new HashSet<string>(_config.ProperNouns, StringComparer.OrdinalIgnoreCase) });
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
            throw new InvalidOperationException("找不到遊戲 Mods 資料夾：" + modsDir);
        }
        var manifestPath = PathSandbox.Resolve(modsDir, _config.PackFolderName + "/" + ManifestName);
        PackManifest? previous = null;
        if (File.Exists(manifestPath))
        {
            previous = JsonSerializer.Deserialize<PackManifest>(File.ReadAllText(manifestPath), AppConfig.Json);
        }
        else if (Directory.Exists(packDir) && Directory.EnumerateFileSystemEntries(packDir).Any())
        {
            throw new InvalidOperationException("翻譯包資料夾已存在但不是本工具產生的（沒有 manifest），為避免覆蓋請改用其他 PackFolderName：" + packDir);
        }

        var plan = Plan(onlyIds);
        if (onlyIds != null && previous != null)
        {
            // 部分建置：保留其他模組上次的輸出。
            throw new InvalidOperationException("階段 1 只支援完整建置（不指定 mod_ids）。");
        }

        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var manifest = new PackManifest { BuiltUtc = DateTime.UtcNow, GameVersion = _env.VersionFull };
        foreach (var (rel, items) in plan.Files.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var content = LanguageDataWriter.Render(items.Select(x => new LanguageDataWriter.Item(x.Entry.InjectionPath, x.Record.Text, x.Entry.Source)));
            // 寫出前自我檢查：必須能被安全 XML 解析器讀回，且鍵與值一致。
            var check = SafeXml.Parse(content);
            if (check.Root == null || check.Root.Elements().Count() != items.Count)
            {
                throw new InvalidOperationException("產生的 XML 自我檢查失敗：" + rel);
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
                ? (packMod.LoadOrder > lastTranslated ? "重新啟動遊戲即可載入。" : "翻譯包的載入順序必須在所有被翻譯模組之後，請在模組清單中調整。")
                : "請在遊戲模組清單啟用「" + _config.PackDisplayName + "」，並放在所有被翻譯模組之後；遊戲語言需為繁體中文。",
            hashes,
        };
    }

    public object Validate(string? onlyId)
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
        // 檢查已輸出的翻譯包檔案：XML 合法、鍵不重複、無 TODO。
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
                    packProblems.Add("缺少檔案：" + rel);
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
                            packProblems.Add("重複鍵：" + k + "（" + rel + "）");
                        }
                        if (e.Value == "TODO" || e.Value.Length == 0)
                        {
                            packProblems.Add("空值或 TODO：" + k);
                        }
                    }
                }
                catch (Exception ex)
                {
                    packProblems.Add("XML 無法解析：" + rel + "：" + ex.Message);
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
        };
    }

    private string AboutXml(List<InstalledMod> mods)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<ModMetaData>\n");
        sb.Append("  <packageId>").Append(SecurityElement.Escape(_config.PackPackageId)).Append("</packageId>\n");
        sb.Append("  <name>").Append(SecurityElement.Escape(_config.PackDisplayName)).Append("</name>\n");
        sb.Append("  <author>RimWorld Agent Translation（使用者本機產生）</author>\n");
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
        sb.Append("  <description>由 AI Agent 經 rwat 工具產生的繁體中文翻譯包（機器翻譯，未經人工校對）。\n\n")
          .Append("【重要】此模組必須放在所有被翻譯模組之後載入，否則翻譯可能無效。\n")
          .Append("不修改任何原始模組；刪除此模組即可完全移除翻譯。\n\n")
          .Append("翻譯模組：").Append(mods.Count).Append(" 個</description>\n");
        sb.Append("</ModMetaData>\n");
        return sb.ToString();
    }

    private string Readme()
    {
        return "此資料夾由 rwat（RimWorld Agent Translation）自動產生，請勿手動修改，下次建置會被覆寫。\n\n"
            + "載入順序：本翻譯包必須放在所有被翻譯模組之後。About.xml 已列出 loadAfter，遊戲的自動排序會照做。\n"
            + "遊戲語言：需設定為繁體中文（ChineseTraditional）。\n"
            + "內容來源：譯文由使用者所選的 AI Agent 產生；被翻譯的模組文字曾送往該 Agent 的服務供應商。\n"
            + "移除：直接刪除此資料夾即可，不影響任何原始模組。\n";
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
