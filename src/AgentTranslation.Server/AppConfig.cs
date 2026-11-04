using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

/// <summary>Supported target languages. Only these two folder names are ever accepted.</summary>
public static class TargetLanguages
{
    public const string Simplified = "ChineseSimplified";
    public const string Traditional = "ChineseTraditional";

    public static bool IsValid(string? value) => value is Simplified or Traditional;

    public static TargetScript ScriptOf(string language) => language == Traditional ? TargetScript.Traditional : TargetScript.Simplified;

    /// <summary>Language name shown to agents.</summary>
    public static string AgentName(string language) => language == Traditional
        ? "Traditional Chinese (Taiwan usage, e.g. 軟體, 介面, 資料夾)"
        : "Simplified Chinese (mainland usage)";

    /// <summary>Native name used in the pack's About.xml.</summary>
    public static string NativeName(string language) => language == Traditional ? "繁體中文" : "简体中文";

    /// <summary>The other Chinese language (source of conversion memory in Traditional mode).</summary>
    public static string Other(string language) => language == Traditional ? Simplified : Traditional;

    /// <summary>True when a language folder name ("ChineseSimplified" or "ChineseSimplified (简体中文)") belongs to the language.</summary>
    public static bool FolderMatches(string folderName, string language) =>
        folderName == language || folderName.StartsWith(language + " (", StringComparison.Ordinal);
}

/// <summary>
/// Settings file: &lt;work folder&gt;/config.json. Paths and names only, never any secret.
/// The work folder defaults to %LOCALAPPDATA%\RimWorldAgentTranslation and can be overridden with RWAT_WORK_DIR.
/// </summary>
public sealed class AppConfig
{
    private const string OldDefaultDisplayName = "Agent Translation Pack (繁體中文)";

    public string? GameDir { get; set; }
    public List<string> WorkshopDirs { get; set; } = new();
    public string? ConfigDir { get; set; }
    /// <summary>Parent folder of the pack; null means the game's Mods folder. Overridden by RWAT_PACK_OUTPUT_DIR.</summary>
    public string? PackOutputDir { get; set; }
    public string PackFolderName { get; set; } = "!AgentTranslation_Pack";
    public string PackPackageId { get; set; } = "agenttranslation.pack";
    /// <summary>Pack name shown in the mod list; null means "Agent Translation Pack (&lt;language&gt;)".</summary>
    public string? PackDisplayName { get; set; }
    /// <summary>
    /// ChineseSimplified (default) or ChineseTraditional. Decides the output folder, the "already translated" check,
    /// memory sources, the wrong-script validation and the glossary targets. The game matches language folders by full
    /// name or by the legacy name before the parenthesis, so the legacy name is enough.
    /// </summary>
    public string TargetLanguage { get; set; } = TargetLanguages.Simplified;
    public int DefaultBatchSize { get; set; } = 30;
    public int MaxBatchSize { get; set; } = 100;
    public int MaxBatchChars { get; set; } = 12000;
    public List<string> ProperNouns { get; set; } = new();
    /// <summary>Auto-adopting existing Simplified translations; off by default (suggestions only). Only the user edits this file; MCP tools cannot.</summary>
    public SimplifiedAutoAdopt SimplifiedAutoAdopt { get; set; } = new();

    [JsonIgnore]
    public string TargetLanguageFolder => TargetLanguage;

    [JsonIgnore]
    public bool IsTraditional => TargetLanguage == TargetLanguages.Traditional;

    [JsonIgnore]
    public string EffectivePackDisplayName =>
        string.IsNullOrWhiteSpace(PackDisplayName) || PackDisplayName == OldDefaultDisplayName
            ? "Agent Translation Pack (" + TargetLanguages.NativeName(TargetLanguage) + ")"
            : PackDisplayName!;

    [JsonIgnore]
    public string WorkDir { get; private set; } = string.Empty;

    [JsonIgnore]
    public string PackDir => Path.Combine(PackOutputRoot, PackFolderName);

    /// <summary>Parent folder of the pack. Defaults to &lt;GameDir&gt;/Mods.</summary>
    [JsonIgnore]
    public string PackOutputRoot => string.IsNullOrWhiteSpace(PackOutputDir) ? Path.Combine(RequireGameDir(), "Mods") : Path.GetFullPath(PackOutputDir!);

    public string RequireGameDir()
    {
        if (string.IsNullOrEmpty(GameDir) || !Directory.Exists(GameDir))
        {
            throw new InvalidOperationException("Game folder not found. Set GameDir in " + Path.Combine(WorkDir, "config.json") + " or the RWAT_GAME_DIR environment variable.");
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
        var existingText = File.Exists(path) ? File.ReadAllText(path) : null;
        config = existingText != null ? JsonSerializer.Deserialize<AppConfig>(existingText, Json) ?? new AppConfig() : new AppConfig();
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
            throw new InvalidOperationException("PackFolderName may only contain letters, digits, !, _, -, . and spaces.");
        }
        if (!TargetLanguages.IsValid(config.TargetLanguage))
        {
            throw new InvalidOperationException("target_language must be ChineseSimplified or ChineseTraditional.");
        }
        // Older config files lack newer sections: write them back (with defaults) so the user can see what can be changed.
        if (existingText != null && (!existingText.Contains("\"simplified_auto_adopt\"", StringComparison.Ordinal) || !existingText.Contains("\"target_language\"", StringComparison.Ordinal)))
        {
            detectedNew = true;
        }
        if (existingText == null || detectedNew)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(config, Json));
        }
        // Applied after saving so a temporary override never ends up in config.json.
        var envLang = Environment.GetEnvironmentVariable("RWAT_TARGET_LANGUAGE");
        if (!string.IsNullOrWhiteSpace(envLang))
        {
            if (!TargetLanguages.IsValid(envLang.Trim()))
            {
                throw new InvalidOperationException("RWAT_TARGET_LANGUAGE must be ChineseSimplified or ChineseTraditional.");
            }
            config.TargetLanguage = envLang.Trim();
        }
        return config;
    }
}

/// <summary>
/// Auto-adopt conditions. Only entries in the "direct" tier are ever adopted; "review" and "retranslate" never are.
/// Fields set in per_mod override the same fields of default; fields left out fall back to default.
/// </summary>
public sealed class SimplifiedAutoAdopt
{
    /// <summary>Master switch. When false, per_mod has no effect either.</summary>
    public bool Enabled { get; set; }
    public AdoptRule Default { get; set; } = new() { Enabled = true, MinTrust = 80, RequireConfirmedFreshness = true };
    /// <summary>Keyed by packageId (case-insensitive).</summary>
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
    /// <summary>Minimum trust score (0-100).</summary>
    public int? MinTrust { get; set; }
    /// <summary>When true, only entries proven current by an EN comment or same-folder English are adopted.</summary>
    public bool? RequireConfirmedFreshness { get; set; }
    /// <summary>Entry kinds to adopt: "keyed", "def"; null for all.</summary>
    public List<string>? Kinds { get; set; }
    /// <summary>Source kinds to adopt: "mod_self", "patch"; null for both. Suspected machine translations are never adopted.</summary>
    public List<string>? Origins { get; set; }
}

/// <summary>Finds RimWorld and its Workshop folder from the Steam installation data.</summary>
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
