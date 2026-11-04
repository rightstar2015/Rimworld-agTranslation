using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

public sealed class IssueDto
{
    public string Code { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;

    public static IssueDto From(ValidationIssue i) => new() { Code = i.Code, Detail = i.Detail };
}

/// <summary>一筆已接受的譯文。</summary>
public sealed class TranslationRecord
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string? DefType { get; set; }
    public string Path { get; set; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    /// <summary>accepted 或 flagged。</summary>
    public string Status { get; set; } = "accepted";
    public List<IssueDto> Flags { get; set; } = new();
    /// <summary>agent（Agent 提交）或 simplified_auto（簡中自動採用）。</summary>
    public string Origin { get; set; } = "agent";
    /// <summary>來源細節，例如簡中提供者與可信度。</summary>
    public string? OriginDetail { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class ModTranslations
{
    public string PackageId { get; set; } = string.Empty;
    public string PackageIdRaw { get; set; } = string.Empty;
    public string ModName { get; set; } = string.Empty;
    public Dictionary<string, TranslationRecord> Records { get; set; } = new(StringComparer.Ordinal);
}

public sealed class BatchRecord
{
    public string BatchId { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public List<string> Ids { get; set; } = new();
}

/// <summary>
/// 工作資料夾內的 JSON 儲存：translations/&lt;packageId&gt;.json 與 batches/&lt;batchId&gt;.json。
/// 所有路徑經 PathSandbox 解析；檔名由伺服器產生，不採用外部輸入。
/// </summary>
public sealed class TranslationStore
{
    private readonly string _root;
    private readonly Dictionary<string, ModTranslations> _cache = new(StringComparer.Ordinal);

    public TranslationStore(string workDir)
    {
        _root = workDir;
    }

    public static string SafeFileStem(string packageId)
    {
        var chars = PackageIds.Normalize(packageId).Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').ToArray();
        var stem = new string(chars).Trim('.');
        return stem.Length == 0 ? "_" : stem;
    }

    public ModTranslations Get(string packageId)
    {
        var id = PackageIds.Normalize(packageId);
        if (_cache.TryGetValue(id, out var cached))
        {
            return cached;
        }
        var path = PathSandbox.Resolve(_root, "translations/" + SafeFileStem(id) + ".json");
        var mt = File.Exists(path)
            ? JsonSerializer.Deserialize<ModTranslations>(File.ReadAllText(path), AppConfig.Json) ?? new ModTranslations()
            : new ModTranslations();
        mt.PackageId = id;
        _cache[id] = mt;
        return mt;
    }

    public void Save(ModTranslations mt)
    {
        var path = PathSandbox.Resolve(_root, "translations/" + SafeFileStem(mt.PackageId) + ".json");
        WriteAtomic(path, JsonSerializer.Serialize(mt, AppConfig.Json));
    }

    public IEnumerable<string> AllPackageIds()
    {
        var dir = System.IO.Path.Combine(_root, "translations");
        if (!Directory.Exists(dir))
        {
            yield break;
        }
        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            ModTranslations? mt;
            try
            {
                mt = JsonSerializer.Deserialize<ModTranslations>(File.ReadAllText(f), AppConfig.Json);
            }
            catch (JsonException)
            {
                continue;
            }
            if (mt != null && mt.PackageId.Length > 0)
            {
                yield return mt.PackageId;
            }
        }
    }

    public void SaveBatch(BatchRecord batch)
    {
        var path = PathSandbox.Resolve(_root, "batches/" + batch.BatchId + ".json");
        WriteAtomic(path, JsonSerializer.Serialize(batch, AppConfig.Json));
    }

    public BatchRecord? LoadBatch(string batchId)
    {
        if (!IsBatchId(batchId))
        {
            return null;
        }
        var path = PathSandbox.Resolve(_root, "batches/" + batchId + ".json");
        return File.Exists(path) ? JsonSerializer.Deserialize<BatchRecord>(File.ReadAllText(path), AppConfig.Json) : null;
    }

    public void PurgeExpiredBatches()
    {
        var dir = System.IO.Path.Combine(_root, "batches");
        if (!Directory.Exists(dir))
        {
            return;
        }
        foreach (var f in Directory.GetFiles(dir, "b_*.json"))
        {
            try
            {
                var b = JsonSerializer.Deserialize<BatchRecord>(File.ReadAllText(f), AppConfig.Json);
                if (b == null || b.ExpiresUtc < DateTime.UtcNow)
                {
                    File.Delete(f);
                }
            }
            catch (JsonException)
            {
                File.Delete(f);
            }
        }
    }

    public static bool IsBatchId(string? id) =>
        id != null && id.Length == 28 && id.StartsWith("b_", StringComparison.Ordinal) && id.Skip(2).All(c => c is >= 'a' and <= 'z' or >= '2' and <= '7');

    public static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, new System.Text.UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }
}
