using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

/// <summary>
/// Disk cache of scan results (work folder cache/scan/&lt;packageId&gt;.json). Only the extraction is cached;
/// "already translated by another mod" is decided again every time from the current state.
///
/// The fingerprint covers: cache format version, game version and Assembly-CSharp, the active mod list and order, every active
/// mod's About.xml (size and time), and path, size and time of every file under this mod's Defs, Languages/English and Assemblies.
/// Known limit: if a dependency changes only its Defs (parent content) without touching About.xml, the fingerprint does not change; scan_mod always forces a rescan.
/// </summary>
public static class ScanCache
{
    private const string FormatVersion = "scan-cache-2";
    private const long MaxBytes = 64L * 1024 * 1024;

    private sealed class EntryDto
    {
        public string Kind { get; set; } = string.Empty;
        public string? DefType { get; set; }
        public string Path { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public string? Label { get; set; }
        public string? Description { get; set; }
        public List<string>? Aliases { get; set; }
    }

    private sealed class NoteDto
    {
        public string Code { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string? File { get; set; }
    }

    private sealed class CacheFile
    {
        public string Fingerprint { get; set; } = string.Empty;
        public string PackageId { get; set; } = string.Empty;
        public List<EntryDto> Entries { get; set; } = new();
        public List<NoteDto> Notes { get; set; } = new();
    }

    /// <summary>
    /// Changes when anything the extraction depends on changes: game version and assembly, the active mod list, this mod's
    /// Defs, Languages and Assemblies, and the target language (it decides which language folder is the Keyed source).
    /// </summary>
    public static string Fingerprint(GameEnvironment env, InstalledMod mod, string targetLanguage)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string s) => sha.AppendData(Encoding.UTF8.GetBytes(s + "\n"));
        Add(FormatVersion);
        Add("target:" + targetLanguage);
        Add(env.VersionFull);
        AddFile(Add, Path.Combine(env.GameDir, "Assembly-CSharp.dll"));
        AddFile(Add, Path.Combine(env.GameDir, "RimWorldWin64_Data", "Managed", "Assembly-CSharp.dll"));
        foreach (var m in env.ActiveMods)
        {
            Add(m.PackageId + "|" + m.LoadOrder);
            AddFile(Add, Path.Combine(m.RootDir, "About", "About.xml"));
        }
        Add("mod:" + mod.PackageId + "|" + mod.LoadOrder + "|" + mod.Active);
        AddFile(Add, Path.Combine(mod.RootDir, "LoadFolders.xml"));
        foreach (var folder in env.LoadFoldersFor(mod))
        {
            Add("folder:" + Path.GetRelativePath(mod.RootDir, folder));
            foreach (var sub in new[] { "Defs", "Languages", "Assemblies" })
            {
                var dir = Path.Combine(folder, sub);
                if (!Directory.Exists(dir))
                {
                    continue;
                }
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                {
                    Add(Path.GetRelativePath(mod.RootDir, f));
                    AddFile(Add, f);
                }
            }
        }
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AddFile(Action<string> add, string path)
    {
        var info = new FileInfo(path);
        add(info.Exists ? info.Length + "@" + info.LastWriteTimeUtc.Ticks : "-");
    }

    private static string PathFor(string workDir, string packageId) =>
        PathSandbox.Resolve(workDir, "cache/scan/" + TranslationStore.SafeFileStem(packageId) + ".json");

    public static ExtractionResult? TryLoad(string workDir, string packageId, string fingerprint)
    {
        try
        {
            var path = PathFor(workDir, packageId);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxBytes)
            {
                return null;
            }
            var file = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path), AppConfig.Json);
            if (file == null || file.Fingerprint != fingerprint || file.PackageId != packageId)
            {
                return null;
            }
            var result = new ExtractionResult();
            foreach (var e in file.Entries)
            {
                if (!EntryKindNames.TryParse(e.Kind, out var kind))
                {
                    return null;
                }
                result.Entries.Add(new SourceEntry(packageId, kind, e.DefType, e.Path, e.Source, e.File) { DefLabel = e.Label, DefDescription = e.Description, DefTypeAliases = e.Aliases ?? new List<string>() });
            }
            foreach (var n in file.Notes)
            {
                result.Notes.Add(new ScanNote(n.Code, n.Detail, n.File));
            }
            return result;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(string workDir, string packageId, string fingerprint, ExtractionResult result)
    {
        var file = new CacheFile
        {
            Fingerprint = fingerprint,
            PackageId = packageId,
            Entries = result.Entries.Select(e => new EntryDto
            {
                Kind = EntryKindNames.ToId(e.Kind),
                DefType = e.DefType,
                Path = e.InjectionPath,
                Source = e.Source,
                File = e.SourceFile,
                Label = e.DefLabel,
                Description = e.DefDescription,
                Aliases = e.DefTypeAliases.Count > 0 ? e.DefTypeAliases.ToList() : null,
            }).ToList(),
            Notes = result.Notes.Select(n => new NoteDto { Code = n.Code, Detail = n.Detail, File = n.File }).ToList(),
        };
        try
        {
            TranslationStore.WriteAtomic(PathFor(workDir, packageId), JsonSerializer.Serialize(file, AppConfig.Json));
        }
        catch (IOException)
        {
            // A failed cache write does not affect the result.
        }
    }
}
