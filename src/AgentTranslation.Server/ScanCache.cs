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
/// 掃描結果的磁碟快取（工作資料夾 cache/scan/&lt;packageId&gt;.json）。只快取「抽取」的結果；
/// 「是否已被其他模組翻譯」每次依目前狀態重新判斷。
///
/// 指紋包含：快取格式版本、遊戲版本與 Assembly-CSharp、啟用模組清單與順序、每個啟用模組的 About.xml（大小與時間），
/// 以及本模組載入資料夾下 Defs、Languages/English、Assemblies 每個檔案的路徑、大小、修改時間。
/// 已知限制：依賴模組只改 Defs 而 About.xml 不變時（父節點內容改變），指紋不會變；scan_mod 一律強制重掃。
/// </summary>
public static class ScanCache
{
    private const string FormatVersion = "scan-cache-1";
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

    public static string Fingerprint(GameEnvironment env, InstalledMod mod)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string s) => sha.AppendData(Encoding.UTF8.GetBytes(s + "\n"));
        Add(FormatVersion);
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
            foreach (var sub in new[] { "Defs", Path.Combine("Languages", "English"), "Assemblies" })
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
                result.Entries.Add(new SourceEntry(packageId, kind, e.DefType, e.Path, e.Source, e.File) { DefLabel = e.Label, DefDescription = e.Description });
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
            }).ToList(),
            Notes = result.Notes.Select(n => new NoteDto { Code = n.Code, Detail = n.Detail, File = n.File }).ToList(),
        };
        try
        {
            TranslationStore.WriteAtomic(PathFor(workDir, packageId), JsonSerializer.Serialize(file, AppConfig.Json));
        }
        catch (IOException)
        {
            // 快取寫入失敗不影響結果。
        }
    }
}
