using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using AgentTranslation.Bridge;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

/// <summary>Bridge mod integration: status, runtime verification, reload requests and installation.</summary>
public sealed partial class TranslationService
{
    public const string BridgeFolderName = "!AgentTranslation_Bridge";
    private const string BridgeManifestName = ".agent-bridge-manifest.json";
    private readonly BridgeChannel _bridge = new();

    /// <summary>built_utc of the pack as written in its manifest (raw string, compared with the one the bridge saw).</summary>
    private (string? BuiltUtc, List<(string Id, string PackageId, string Kind, string Path)> Entries) CurrentPackManifest()
    {
        var path = Path.Combine(_config.PackDir, ".agent-manifest.json");
        var entries = new List<(string, string, string, string)>();
        if (!File.Exists(path))
        {
            return (null, entries);
        }
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = doc.RootElement;
        var built = root.TryGetProperty("built_utc", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
        if (root.TryGetProperty("entries", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in list.EnumerateArray())
            {
                string S(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;
                entries.Add((S("id"), S("package_id"), S("kind"), S("path")));
            }
        }
        return (built, entries);
    }

    /// <summary>Bridge section of get_environment.</summary>
    private object BridgeStatus()
    {
        var mod = _env.Find(BridgeChannel.BridgePackageId);
        var report = _bridge.ReadReport();
        string? built = null;
        try
        {
            built = CurrentPackManifest().BuiltUtc;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
        }
        return new
        {
            installed = mod != null,
            active = mod?.Active ?? false,
            load_order = mod?.LoadOrder,
            pending_requests = _bridge.PendingRequests(),
            report = report == null ? null : new
            {
                generated_utc = report.GeneratedUtc,
                trigger = report.Trigger,
                game_language = report.LanguageFolder,
                translation_error_count = report.TranslationErrorCount,
                pack_found = report.PackFound,
                pack_language_matches_game = report.PackLanguageMatchesGame,
                matches_current_pack = built != null && report.PackManifestBuiltUtc == built,
                summary = report.Summary,
                last_request = report.LastRequest is { } lr ? new { request_id = lr.RequestId, kind = lr.Kind, status = lr.Status, applied = lr.Applied } : null,
            },
            note = mod == null
                ? "The bridge mod is not installed. The user can install it with the CLI command: rwat install-bridge."
                : report == null ? "No runtime report yet: enable the bridge mod in game and start the game." : null,
        };
    }

    /// <summary>runtime_check section of validate_pack: which pack entries the game actually uses.</summary>
    private object RuntimeCheck(string? onlyPackageId)
    {
        var report = _bridge.ReadReport();
        if (report == null)
        {
            return new { available = false, note = "No runtime report. The user installs the bridge mod (rwat install-bridge), enables it and starts the game." };
        }
        var (built, entries) = CurrentPackManifest();
        var matches = built != null && report.PackManifestBuiltUtc == built;
        var relevant = entries.Where(e => onlyPackageId == null || e.PackageId == onlyPackageId).ToList();
        var problems = new List<object>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in relevant)
        {
            var status = report.Entries.TryGetValue(e.Id, out var r) ? r.Status : "not_in_report";
            counts[status] = counts.GetValueOrDefault(status) + 1;
            if (status != "ok" && problems.Count < 200)
            {
                problems.Add(new { id = e.Id, package_id = e.PackageId, kind = e.Kind, path = e.Path, status, source = r?.Source });
            }
        }
        return new
        {
            available = true,
            generated_utc = report.GeneratedUtc,
            game_language = report.LanguageFolder,
            pack_language_matches_game = report.PackLanguageMatchesGame,
            matches_current_pack = matches,
            translation_error_count = report.TranslationErrorCount,
            counts,
            problems,
            legend = "ok: in effect. overridden: another mod's file wins (source = its packageId or file name). changed_at_runtime: our file loaded but the value was changed afterwards. not_loaded / not_injected: the game did not take it. not_in_report: built after the report.",
            note = matches ? null : "The report describes an older build of the pack; restart the game (or reload Keyed) to check the current build.",
        };
    }

    // ---------- request_reload ----------
    public object RequestReload(string scope)
    {
        lock (_lock)
        {
            if (scope != "keyed")
            {
                throw new ArgumentException("scope must be \"keyed\" (only Keyed texts can be reloaded; Def names and descriptions need a restart).");
            }
            var mod = _env.Find(BridgeChannel.BridgePackageId);
            var id = _bridge.WriteRequest(InboxRequest.KindReloadKeyed);
            return new
            {
                request_id = id,
                bridge_active = mod?.Active ?? false,
                note = "Request written. The game applies it within a few seconds only if the bridge mod is active and the user turned on \"allow reloading Keyed\" in its mod settings; otherwise it is refused. Run build_pack first so the pack holds the new texts. The result appears in get_environment bridge.report.last_request.",
            };
        }
    }

    /// <summary>CLI only: asks the bridge for a fresh report.</summary>
    public object RequestBridgeReport()
    {
        lock (_lock)
        {
            return new { request_id = _bridge.WriteRequest(InboxRequest.KindRefreshReport), bridge_dir = _bridge.Directory };
        }
    }

    // ---------- install-bridge (CLI only: it installs code into the game, so agents cannot trigger it) ----------
    public object InstallBridge(string? from)
    {
        lock (_lock)
        {
            var source = Path.GetFullPath(from ?? Path.Combine(AppContext.BaseDirectory, "bridge-mod", BridgeFolderName));
            var about = Path.Combine(source, "About", "About.xml");
            var dll = Path.Combine(source, "1.7", "Assemblies", "AgentTranslation.Bridge.dll");
            if (!File.Exists(about) || !File.Exists(dll))
            {
                throw new ArgumentException("Bridge mod not found at " + source + ". Build it with: dotnet build src/AgentTranslation.Bridge -p:RimWorldDir=<game folder>, then pass --from artifacts/bridge-mod/" + BridgeFolderName);
            }
            var pkg = XDocument.Load(about).Root?.Element("packageId")?.Value.Trim();
            if (!string.Equals(pkg, BridgeChannel.BridgePackageId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The folder is not the Agent Translation bridge mod (packageId " + pkg + ").");
            }
            var target = Path.Combine(_config.PackOutputRoot, BridgeFolderName);
            var manifestPath = Path.Combine(target, BridgeManifestName);
            List<string> previous = new();
            if (File.Exists(manifestPath))
            {
                previous = JsonSerializer.Deserialize<BridgeManifest>(File.ReadAllText(manifestPath), AppConfig.Json)?.Files ?? new();
            }
            else if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            {
                throw new InvalidOperationException("The folder exists but was not installed by this tool: " + target);
            }
            Directory.CreateDirectory(target);
            PathSandbox.NormalizeRoot(target);
            var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(source, f).Replace('\\', '/'))
                .Where(r => r != BridgeManifestName)
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToList();
            var hashes = new List<object>();
            foreach (var rel in files)
            {
                var dest = PathSandbox.Resolve(target, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                var bytes = File.ReadAllBytes(Path.Combine(source, rel));
                File.WriteAllBytes(dest + ".tmp", bytes);
                File.Move(dest + ".tmp", dest, overwrite: true);
                hashes.Add(new { file = rel, sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() });
            }
            var removed = 0;
            foreach (var old in previous.Where(p => !files.Contains(p)))
            {
                var p = PathSandbox.Resolve(target, old);
                if (File.Exists(p))
                {
                    File.Delete(p);
                    removed++;
                }
            }
            TranslationStore.WriteAtomic(manifestPath, JsonSerializer.Serialize(new BridgeManifest { InstalledUtc = DateTime.UtcNow, Files = files }, AppConfig.Json));
            return new
            {
                installed_to = target,
                files = files.Count,
                removed,
                hashes,
                next_step = "Enable \"Agent Translation Bridge\" in the mod list and restart the game. Its settings are under Options > Mod settings > Agent Translation.",
            };
        }
    }

    private sealed class BridgeManifest
    {
        public DateTime InstalledUtc { get; set; }
        public List<string> Files { get; set; } = new();
    }
}
