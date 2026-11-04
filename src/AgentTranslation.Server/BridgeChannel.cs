using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using AgentTranslation.Bridge;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

/// <summary>One verified entry from the bridge's runtime report.</summary>
public sealed record RuntimeEntry(string Id, string Status, string? Source);

/// <summary>The bridge's runtime report (outbox/runtime.json), parsed defensively: it is written by a game mod.</summary>
public sealed class RuntimeReport
{
    public DateTime GeneratedUtc { get; init; }
    public string? Trigger { get; init; }
    public string? GameVersion { get; init; }
    public string? LanguageFolder { get; init; }
    public int TranslationErrorCount { get; init; }
    public bool PackFound { get; init; }
    public string? PackManifestBuiltUtc { get; init; }
    public string? PackLanguageFolder { get; init; }
    public bool PackLanguageMatchesGame { get; init; }
    public int? PackLoadOrder { get; init; }
    public Dictionary<string, int> Summary { get; init; } = new();
    public Dictionary<string, RuntimeEntry> Entries { get; init; } = new(StringComparer.Ordinal);
    public (string RequestId, string Kind, string Status, int Applied)? LastRequest { get; init; }
}

/// <summary>
/// File channel to the in-game bridge mod. The location is fixed so the game finds it without any setting:
/// %LOCALAPPDATA%\RimWorldAgentTranslation\bridge, or RWAT_BRIDGE_DIR when set (for tests; the game must see the same value).
/// The server only writes fixed-format requests into inbox/ and only reads outbox/runtime.json.
/// </summary>
public sealed class BridgeChannel
{
    public const string BridgePackageId = "agenttranslation.bridge";
    private const long MaxReportBytes = 64L * 1024 * 1024;
    private readonly string _dir;

    public BridgeChannel()
    {
        var custom = Environment.GetEnvironmentVariable("RWAT_BRIDGE_DIR");
        _dir = !string.IsNullOrWhiteSpace(custom) && Path.IsPathRooted(custom)
            ? Path.GetFullPath(custom)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RimWorldAgentTranslation", "bridge");
    }

    public string Directory => _dir;

    /// <summary>Writes one request atomically into inbox/ and returns its id.</summary>
    public string WriteRequest(string kind)
    {
        var id = NewId();
        var inbox = Path.Combine(_dir, "inbox");
        System.IO.Directory.CreateDirectory(inbox);
        var name = "req_" + id + ".json";
        var path = PathSandbox.Resolve(inbox, name);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, InboxRequest.Serialize(kind, id));
        File.Move(tmp, path, overwrite: true);
        return id;
    }

    /// <summary>Requests still waiting in the inbox (the bridge removes them when it picks them up).</summary>
    public int PendingRequests()
    {
        var inbox = Path.Combine(_dir, "inbox");
        return System.IO.Directory.Exists(inbox) ? System.IO.Directory.GetFiles(inbox, "req_*.json").Length : 0;
    }

    public RuntimeReport? ReadReport()
    {
        var path = Path.Combine(_dir, "outbox", "runtime.json");
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaxReportBytes)
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions { MaxDepth = 8 });
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || Int(r, "schema") != 1)
            {
                return null;
            }
            var pack = r.TryGetProperty("pack", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
            var summary = new Dictionary<string, int>(StringComparer.Ordinal);
            if (r.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in s.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var n) && prop.Name.Length <= 40)
                    {
                        summary[prop.Name] = n;
                    }
                }
            }
            var entries = new Dictionary<string, RuntimeEntry>(StringComparer.Ordinal);
            if (r.TryGetProperty("entries", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in list.EnumerateArray())
                {
                    var id = Str(e, "id");
                    var status = Str(e, "status");
                    if (id != null && StableId.LooksLikeId(id) && status != null && status.Length <= 40)
                    {
                        entries[id] = new RuntimeEntry(id, status, Clip(Str(e, "source"), 200));
                    }
                }
            }
            (string, string, string, int)? last = null;
            if (r.TryGetProperty("last_request", out var lr) && lr.ValueKind == JsonValueKind.Object)
            {
                last = (Clip(Str(lr, "request_id"), 40) ?? string.Empty, Clip(Str(lr, "kind"), 40) ?? string.Empty, Clip(Str(lr, "status"), 40) ?? string.Empty, Int(lr, "applied") ?? 0);
            }
            return new RuntimeReport
            {
                GeneratedUtc = r.TryGetProperty("generated_utc", out var g) && g.ValueKind == JsonValueKind.String && g.TryGetDateTime(out var dt) ? dt.ToUniversalTime() : default,
                Trigger = Clip(Str(r, "trigger"), 40),
                GameVersion = Clip(Str(r, "game_version"), 80),
                LanguageFolder = Clip(Str(r, "language_folder"), 120),
                TranslationErrorCount = Int(r, "translation_error_count") ?? -1,
                PackFound = pack.ValueKind == JsonValueKind.Object && pack.TryGetProperty("found", out var f) && f.ValueKind == JsonValueKind.True,
                PackManifestBuiltUtc = pack.ValueKind == JsonValueKind.Object ? Clip(Str(pack, "manifest_built_utc"), 60) : null,
                PackLanguageFolder = pack.ValueKind == JsonValueKind.Object ? Clip(Str(pack, "language_folder"), 120) : null,
                PackLanguageMatchesGame = pack.ValueKind == JsonValueKind.Object && pack.TryGetProperty("language_matches_game", out var lm) && lm.ValueKind == JsonValueKind.True,
                PackLoadOrder = pack.ValueKind == JsonValueKind.Object ? Int(pack, "load_order") : null,
                Summary = summary,
                Entries = entries,
                LastRequest = last,
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    private static string? Clip(string? s, int max) => s == null ? null : s.Length <= max ? s : s.Substring(0, max);

    private static string NewId()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        var chars = new char[26];
        int buffer = 0, bits = 0, n = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5 && n < 26)
            {
                chars[n++] = alphabet[(buffer >> (bits - 5)) & 31];
                bits -= 5;
            }
        }
        if (n < 26)
        {
            chars[n++] = alphabet[(buffer << (5 - bits)) & 31];
        }
        return new string(chars);
    }
}
