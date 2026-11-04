using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AgentTranslation.Server;

/// <summary>
/// 設定檔：&lt;工作資料夾&gt;/config.json。只放路徑與名稱，不放任何密鑰。
/// 工作資料夾預設 %LOCALAPPDATA%\RimWorldAgentTranslation，可用環境變數 RWAT_WORK_DIR 覆寫。
/// </summary>
public sealed class AppConfig
{
    public string? GameDir { get; set; }
    public List<string> WorkshopDirs { get; set; } = new();
    public string? ConfigDir { get; set; }
    /// <summary>翻譯包輸出的上層資料夾；null 表示遊戲的 Mods 資料夾。可用環境變數 RWAT_PACK_OUTPUT_DIR 覆寫。</summary>
    public string? PackOutputDir { get; set; }
    public string PackFolderName { get; set; } = "!AgentTranslation_Pack";
    public string PackPackageId { get; set; } = "agenttranslation.pack";
    public string PackDisplayName { get; set; } = "Agent Translation Pack (繁體中文)";
    /// <summary>輸出語言資料夾名稱。遊戲以「完整名稱」或「括號前的舊名稱」比對，因此用舊名稱即可。</summary>
    public string TargetLanguageFolder { get; set; } = "ChineseTraditional";
    public int DefaultBatchSize { get; set; } = 30;
    public int MaxBatchSize { get; set; } = 100;
    public int MaxBatchChars { get; set; } = 12000;
    public List<string> ProperNouns { get; set; } = new();
    /// <summary>簡中轉繁的自動採用設定；預設關閉（全部只是建議）。只有使用者能改這個檔案，MCP 工具不能改。</summary>
    public SimplifiedAutoAdopt SimplifiedAutoAdopt { get; set; } = new();

    [JsonIgnore]
    public string WorkDir { get; private set; } = string.Empty;

    [JsonIgnore]
    public string PackDir => Path.Combine(PackOutputRoot, PackFolderName);

    /// <summary>翻譯包所在的上層資料夾。未設定時為 &lt;GameDir&gt;/Mods。</summary>
    [JsonIgnore]
    public string PackOutputRoot => string.IsNullOrWhiteSpace(PackOutputDir) ? Path.Combine(RequireGameDir(), "Mods") : Path.GetFullPath(PackOutputDir!);

    public string RequireGameDir()
    {
        if (string.IsNullOrEmpty(GameDir) || !Directory.Exists(GameDir))
        {
            throw new InvalidOperationException("找不到遊戲資料夾。請在 " + Path.Combine(WorkDir, "config.json") + " 設定 GameDir，或設定環境變數 RWAT_GAME_DIR。");
        }
        return GameDir!;
    }

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppConfig Load()
    {
        var workDir = Environment.GetEnvironmentVariable("RWAT_WORK_DIR");
        if (string.IsNullOrWhiteSpace(workDir))
        {
            workDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RimWorldAgentTranslation");
        }
        workDir = Path.GetFullPath(workDir);
        Directory.CreateDirectory(workDir);
        var path = Path.Combine(workDir, "config.json");
        AppConfig config;
        if (File.Exists(path))
        {
            config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Json) ?? new AppConfig();
        }
        else
        {
            config = new AppConfig();
        }
        config.WorkDir = workDir;

        var envGame = Environment.GetEnvironmentVariable("RWAT_GAME_DIR");
        if (!string.IsNullOrWhiteSpace(envGame))
        {
            config.GameDir = envGame;
        }
        var envPack = Environment.GetEnvironmentVariable("RWAT_PACK_OUTPUT_DIR");
        if (!string.IsNullOrWhiteSpace(envPack))
        {
            config.PackOutputDir = envPack;
        }
        var detectedNew = false;
        if (string.IsNullOrEmpty(config.GameDir))
        {
            config.GameDir = SteamLocator.FindGameDir();
            detectedNew = config.GameDir != null;
        }
        if (config.WorkshopDirs.Count == 0 && config.GameDir != null)
        {
            var ws = SteamLocator.WorkshopDirFor(config.GameDir);
            if (ws != null)
            {
                config.WorkshopDirs.Add(ws);
                detectedNew = true;
            }
        }
        if (string.IsNullOrEmpty(config.ConfigDir))
        {
            var cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Ludeon Studios", "RimWorld by Ludeon Studios", "Config");
            if (Directory.Exists(cfg))
            {
                config.ConfigDir = cfg;
                detectedNew = true;
            }
        }
        if (!Regex.IsMatch(config.PackFolderName, @"^[A-Za-z0-9!_\-\. ]{1,64}$") || config.PackFolderName.Contains(".."))
        {
            throw new InvalidOperationException("PackFolderName 只能含英數、!、_、-、.、空白。");
        }
        // 舊版設定檔沒有 simplified_auto_adopt：補上（預設關閉），讓使用者看得到可以改的欄位。
        if (File.Exists(path) && !File.ReadAllText(path).Contains("\"simplified_auto_adopt\"", StringComparison.Ordinal))
        {
            detectedNew = true;
        }
        if (!File.Exists(path) || detectedNew)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(config, Json));
        }
        return config;
    }
}

/// <summary>
/// 自動採用條件。只會採用「直接採用」分級的條目，「校對」與「重新翻譯」永遠不會自動採用。
/// per_mod 的欄位會覆蓋 default 中同名的欄位；未寫的欄位沿用 default。
/// </summary>
public sealed class SimplifiedAutoAdopt
{
    /// <summary>總開關。false 時 per_mod 也不生效。</summary>
    public bool Enabled { get; set; }
    public AdoptRule Default { get; set; } = new() { Enabled = true, MinTrust = 80, RequireConfirmedFreshness = true };
    /// <summary>鍵為 packageId（不分大小寫）。</summary>
    public Dictionary<string, AdoptRule> PerMod { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public AdoptRule RuleFor(string packageId)
    {
        var d = Default;
        var o = PerMod.FirstOrDefault(kv => string.Equals(kv.Key.Trim(), packageId, StringComparison.OrdinalIgnoreCase)).Value;
        return new AdoptRule
        {
            Enabled = (o?.Enabled ?? d.Enabled ?? true) && Enabled,
            MinTrust = o?.MinTrust ?? d.MinTrust ?? 80,
            RequireConfirmedFreshness = o?.RequireConfirmedFreshness ?? d.RequireConfirmedFreshness ?? true,
            Kinds = o?.Kinds ?? d.Kinds,
            Origins = o?.Origins ?? d.Origins,
        };
    }
}

public sealed class AdoptRule
{
    public bool? Enabled { get; set; }
    /// <summary>可信度下限（0–100）。</summary>
    public int? MinTrust { get; set; }
    /// <summary>true 時只採用有 EN 註解或同資料夾英文證明為最新的條目。</summary>
    public bool? RequireConfirmedFreshness { get; set; }
    /// <summary>限定條目類型："keyed"、"def"；null 為全部。</summary>
    public List<string>? Kinds { get; set; }
    /// <summary>限定來源類型："mod_self"、"patch"；null 為兩者。疑似機翻來源永遠不會自動採用。</summary>
    public List<string>? Origins { get; set; }
}

/// <summary>從 Steam 安裝資料找出 RimWorld 與工坊資料夾。</summary>
public static class SteamLocator
{
    public static string? FindGameDir()
    {
        foreach (var lib in SteamLibraries())
        {
            var dir = Path.Combine(lib, "steamapps", "common", "RimWorld");
            if (File.Exists(Path.Combine(dir, "Version.txt")))
            {
                return dir;
            }
        }
        return null;
    }

    public static string? WorkshopDirFor(string gameDir)
    {
        // gameDir = <lib>/steamapps/common/RimWorld
        var steamapps = Directory.GetParent(gameDir)?.Parent;
        if (steamapps == null)
        {
            return null;
        }
        var ws = Path.Combine(steamapps.FullName, "workshop", "content", "294100");
        return Directory.Exists(ws) ? ws : null;
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (seen.Add(root))
            {
                yield return root;
            }
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
            {
                continue;
            }
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            {
                var lib = m.Groups[1].Value.Replace("\\\\", "\\");
                if (seen.Add(lib))
                {
                    yield return lib;
                }
            }
        }
    }
}
