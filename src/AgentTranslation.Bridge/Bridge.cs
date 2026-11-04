using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgentTranslation.Bridge;

/// <summary>Result of checking one pack entry against what the game actually holds.</summary>
internal sealed record EntryResult(PackEntry Entry, string Status, string? Source);

internal sealed class ReportSummary
{
    public required DateTime GeneratedUtc { get; init; }
    public required bool PackFound { get; init; }
    public string? PackLanguage { get; init; }
    public string? GameLanguage { get; init; }
    public bool LanguageMatches { get; init; }
    public int ErrorCount { get; init; }
    public Dictionary<string, int> Counts { get; init; } = new();
    public List<(string PackageId, int Overridden, int Changed, int Missing)> ProblemMods { get; init; } = new();
    public (string RequestId, string Kind, string Status, int Applied)? LastRequest { get; init; }
}

/// <summary>
/// Bridge logic. Threads: the game's main thread runs Tick, the verification chunks and every GameApi call;
/// a background thread polls the inbox and all file reads and writes run on background tasks.
/// Fixed locations only: %LOCALAPPDATA%\RimWorldAgentTranslation\bridge (or RWAT_BRIDGE_DIR) for inbox/outbox,
/// and the pack folder found among the running mods for reading.
/// </summary>
internal sealed class Bridge
{
    public const string Ok = "ok";
    public const string Overridden = "overridden";
    public const string Changed = "changed_at_runtime";
    public const string NotLoaded = "not_loaded";
    public const string NotInjected = "not_injected";
    public const string Unknown = "unknown";
    private const int ChunkSize = 200;
    private const float SettleSeconds = 10f;

    private readonly BridgeMod _mod;
    private readonly string _dir;
    private readonly string _inbox;
    private readonly string _outbox;
    private Thread? _poller;
    private volatile bool _busy;
    private volatile bool _again;
    private volatile ReportSummary? _summary;
    private float _menuSince = -1, _playSince = -1;
    private bool _menuDone, _playDone;
    private (string RequestId, string Kind, string Status, int Applied)? _lastRequest;

    public Bridge(BridgeMod mod)
    {
        _mod = mod;
        var custom = Environment.GetEnvironmentVariable("RWAT_BRIDGE_DIR");
        _dir = !string.IsNullOrWhiteSpace(custom) && Path.IsPathRooted(custom)
            ? Path.GetFullPath(custom)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RimWorldAgentTranslation", "bridge");
        _inbox = Path.Combine(_dir, "inbox");
        _outbox = Path.Combine(_dir, "outbox");
    }

    public void Start()
    {
        Directory.CreateDirectory(_inbox);
        Directory.CreateDirectory(_outbox);
        _poller = new Thread(PollLoop) { IsBackground = true, Name = "AgentTranslation.Bridge.Inbox" };
        _poller.Start();
        GameApi.Log("started");
    }

    /// <summary>Main thread, every frame: write a report once the main menu, and later a loaded game, have settled.</summary>
    public void Tick()
    {
        if (!_mod.Settings.ReportEnabled)
        {
            return;
        }
        var now = GameApi.RealTime;
        if (GameApi.AtMainMenu)
        {
            if (_menuSince < 0) _menuSince = now;
            if (!_menuDone && now - _menuSince > SettleSeconds)
            {
                _menuDone = true;
                RequestReport("main_menu");
            }
        }
        else if (GameApi.Playing)
        {
            if (_playSince < 0) _playSince = now;
            if (!_playDone && now - _playSince > SettleSeconds)
            {
                _playDone = true;
                RequestReport("game_loaded");
            }
        }
    }

    // ---------------- Report ----------------

    /// <summary>Main thread. Captures game state, then reads the pack in the background and verifies entries in chunks.</summary>
    public void RequestReport(string trigger)
    {
        if (_busy)
        {
            _again = true;
            return;
        }
        _busy = true;
        var mods = GameApi.RunningMods();
        var lang = GameApi.ActiveLanguage();
        var errors = GameApi.TranslationErrorCount();
        var version = GameApi.GameVersion;
        Task.Run(() =>
        {
            PackSnapshot? snap = null;
            try
            {
                var root = PackReader.FindPackRoot(mods.Select(m => m.RootDir));
                snap = root == null ? null : PackReader.Read(root);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                GameApi.Warn("could not read the pack: " + ex.GetType().Name);
            }
            var results = new List<EntryResult>();
            void Chunk(int start)
            {
                var entries = snap?.Entries ?? new List<PackEntry>();
                var end = Math.Min(start + ChunkSize, entries.Count);
                for (int i = start; i < end; i++)
                {
                    results.Add(Check(entries[i], snap!.Root, mods));
                }
                if (end < entries.Count)
                {
                    BridgePump.Post(() => Chunk(end));
                }
                else
                {
                    var request = _lastRequest;
                    Task.Run(() => Finish(trigger, version, lang, errors, mods, snap, results, request));
                }
            }
            BridgePump.Post(() => Chunk(0));
        });
    }

    /// <summary>Main thread. Compares one pack entry with the game's loaded data.</summary>
    private static EntryResult Check(PackEntry e, string packRoot, List<GameApi.ModInfo> mods)
    {
        try
        {
            if (e.Kind == "keyed")
            {
                var k = GameApi.Keyed(e.Path);
                if (k == null)
                {
                    return new EntryResult(e, NotLoaded, null);
                }
                if (!PackReader.IsInside(packRoot, k.FullPath))
                {
                    return new EntryResult(e, Overridden, OwnerOf(k.FullPath, mods));
                }
                return new EntryResult(e, k.Value == e.Expected ? Ok : Changed, k.Value == e.Expected ? null : "runtime");
            }
            var inj = GameApi.DefInjection(e.DefType!, e.Path);
            if (inj == null)
            {
                return new EntryResult(e, NotLoaded, null);
            }
            if (inj.Injection != e.Expected)
            {
                return new EntryResult(e, Overridden, inj.FileSource);
            }
            if (!inj.Injected)
            {
                return new EntryResult(e, NotInjected, null);
            }
            var parts = e.Path.Split('.');
            if (parts.Length == 2)
            {
                var actual = GameApi.DefStringField(inj.DefType, parts[0], parts[1]);
                if (actual != null && actual != e.Expected)
                {
                    return new EntryResult(e, Changed, "runtime");
                }
            }
            return new EntryResult(e, Ok, null);
        }
        catch (Exception ex)
        {
            return new EntryResult(e, Unknown, ex.GetType().Name);
        }
    }

    /// <summary>packageId of the running mod whose folder holds the file; never a file system path.</summary>
    private static string? OwnerOf(string? fullPath, List<GameApi.ModInfo> mods)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            return null;
        }
        var owner = mods.Where(m => m.RootDir.Length > 0 && PackReader.IsInside(Path.GetFullPath(m.RootDir).TrimEnd(Path.DirectorySeparatorChar), fullPath))
            .OrderByDescending(m => m.RootDir.Length)
            .FirstOrDefault();
        return owner?.PackageId ?? "(not a running mod)";
    }

    /// <summary>Background. Writes outbox/runtime.json and updates the summary shown in game.</summary>
    private void Finish(string trigger, string version, (string Folder, string Legacy)? lang, int errors, List<GameApi.ModInfo> mods, PackSnapshot? snap, List<EntryResult> results, (string RequestId, string Kind, string Status, int Applied)? request)
    {
        try
        {
            var matches = snap?.LanguageFolder == null || lang == null || snap.LanguageFolder == lang.Value.Folder || snap.LanguageFolder == lang.Value.Legacy;
            var counts = new[] { Ok, Overridden, Changed, NotLoaded, NotInjected, Unknown }.ToDictionary(s => s, s => results.Count(r => r.Status == s));
            var generated = DateTime.UtcNow;
            var tmp = Path.Combine(_outbox, "runtime.json.tmp");
            using (var stream = File.Create(tmp))
            using (var w = new Utf8JsonWriter(stream))
            {
                w.WriteStartObject();
                w.WriteNumber("schema", 1);
                w.WriteString("generated_utc", generated);
                w.WriteString("trigger", trigger);
                w.WriteString("game_version", version);
                w.WriteString("language_folder", lang?.Folder);
                w.WriteString("language_legacy_folder", lang?.Legacy);
                w.WriteNumber("translation_error_count", errors);
                w.WriteStartObject("pack");
                w.WriteBoolean("found", snap != null);
                var packMod = snap == null ? null : mods.FirstOrDefault(m => Path.GetFullPath(m.RootDir).TrimEnd(Path.DirectorySeparatorChar).Equals(snap.Root, StringComparison.OrdinalIgnoreCase));
                w.WriteString("package_id", packMod?.PackageId);
                if (packMod != null) w.WriteNumber("load_order", packMod.Order);
                w.WriteString("manifest_built_utc", snap?.BuiltUtc);
                w.WriteString("language_folder", snap?.LanguageFolder);
                w.WriteBoolean("language_matches_game", matches);
                w.WriteEndObject();
                w.WriteStartObject("summary");
                w.WriteNumber("total", results.Count);
                foreach (var (k, v) in counts) w.WriteNumber(k, v);
                w.WriteEndObject();
                w.WriteStartArray("entries");
                foreach (var r in results)
                {
                    w.WriteStartObject();
                    w.WriteString("id", r.Entry.Id);
                    w.WriteString("status", r.Status);
                    if (r.Source != null) w.WriteString("source", r.Source);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteStartArray("running_mods");
                foreach (var m in mods) w.WriteStringValue(m.PackageId);
                w.WriteEndArray();
                if (request is { } req)
                {
                    w.WriteStartObject("last_request");
                    w.WriteString("request_id", req.RequestId);
                    w.WriteString("kind", req.Kind);
                    w.WriteString("status", req.Status);
                    w.WriteNumber("applied", req.Applied);
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            File.Move(tmp, Path.Combine(_outbox, "runtime.json"), overwrite: true);

            _summary = new ReportSummary
            {
                GeneratedUtc = generated,
                PackFound = snap != null,
                PackLanguage = snap?.LanguageFolder,
                GameLanguage = lang?.Folder,
                LanguageMatches = matches,
                ErrorCount = errors,
                Counts = counts,
                ProblemMods = results.Where(r => r.Status != Ok).GroupBy(r => r.Entry.PackageId)
                    .Select(g => (g.Key, g.Count(r => r.Status == Overridden), g.Count(r => r.Status == Changed), g.Count(r => r.Status is NotLoaded or NotInjected or Unknown)))
                    .OrderByDescending(x => x.Item2 + x.Item3 + x.Item4).Take(10).ToList(),
                LastRequest = request,

            };
            GameApi.Log("report written (" + trigger + "): " + results.Count + " entries, " + counts[Ok] + " in effect");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GameApi.Warn("could not write the report: " + ex.GetType().Name);
        }
        finally
        {
            _busy = false;
            if (_again)
            {
                _again = false;
                BridgePump.Post(() => RequestReport("queued"));
            }
        }
    }

    // ---------------- Keyed reload (experimental) ----------------

    /// <summary>Main thread. Re-reads the pack's Keyed files and writes them into the active language, then reports.</summary>
    public void RequestReloadKeyed(string? requestId)
    {
        var id = requestId ?? "in_game";
        if (!_mod.Settings.ReloadEnabled)
        {
            _lastRequest = (id, InboxRequest.KindReloadKeyed, "disabled", 0);
            RequestReport("reload_refused");
            return;
        }
        var mods = GameApi.RunningMods();
        Task.Run(() =>
        {
            PackSnapshot? snap = null;
            try
            {
                var root = PackReader.FindPackRoot(mods.Select(m => m.RootDir));
                snap = root == null ? null : PackReader.Read(root);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                GameApi.Warn("could not read the pack: " + ex.GetType().Name);
            }
            BridgePump.Post(() =>
            {
                var applied = 0;
                foreach (var e in snap?.Entries.Where(x => x.Kind == "keyed") ?? Enumerable.Empty<PackEntry>())
                {
                    var current = GameApi.Keyed(e.Path);
                    // Only keys that are ours or missing: never replace another mod's translation.
                    if (current == null || PackReader.IsInside(snap!.Root, current.FullPath))
                    {
                        if (current?.Value != e.Expected)
                        {
                            GameApi.SetKeyed(e.Path, e.Expected, e.File, e.FullPath);
                            applied++;
                        }
                    }
                }
                _lastRequest = (id, InboxRequest.KindReloadKeyed, snap == null ? "no_pack" : "done", applied);
                RequestReport("reload");
            });
        });
    }

    // ---------------- Inbox ----------------

    private void PollLoop()
    {
        while (true)
        {
            Thread.Sleep(2000);
            try
            {
                PollOnce();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private void PollOnce()
    {
        if (!Directory.Exists(_inbox))
        {
            return;
        }
        foreach (var file in Directory.EnumerateFiles(_inbox, "*.json").Take(InboxRequest.MaxFilesPerPass).ToList())
        {
            var name = Path.GetFileName(file);
            if (!InboxRequest.IsRequestFileName(name))
            {
                Reject(file, name, "file_name");
                continue;
            }
            var info = new FileInfo(file);
            if (info.Length > InboxRequest.MaxBytes)
            {
                Reject(file, name, "size");
                continue;
            }
            var request = InboxRequest.TryParse(File.ReadAllBytes(file), out var reason);
            if (request == null)
            {
                Reject(file, name, reason ?? "invalid");
                continue;
            }
            File.Delete(file);
            BridgePump.Post(() =>
            {
                if (request.Kind == InboxRequest.KindReloadKeyed)
                {
                    RequestReloadKeyed(request.RequestId);
                }
                else
                {
                    _lastRequest = (request.RequestId, request.Kind, "done", 0);
                    RequestReport("request");
                }
            });
        }
    }

    /// <summary>Moves an invalid request aside and records only the reason, never its content.</summary>
    private void Reject(string file, string name, string reason)
    {
        var dir = Path.Combine(_inbox, "rejected");
        Directory.CreateDirectory(dir);
        var safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_').Take(80).ToArray());
        File.Move(file, Path.Combine(dir, safe), overwrite: true);
        File.WriteAllText(Path.Combine(dir, safe + ".reason.txt"), reason + " " + DateTime.UtcNow.ToString("o"));
    }

    // ---------------- Settings page ----------------

    public IEnumerable<string> StatusLines()
    {
        if (_busy)
        {
            yield return GameApi.T("AgentTranslationBridge.Working");
        }
        var s = _summary;
        if (s == null)
        {
            yield return GameApi.T("AgentTranslationBridge.NoReport");
            yield break;
        }
        yield return GameApi.T("AgentTranslationBridge.LastReport", s.GeneratedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
        if (!s.PackFound)
        {
            yield return GameApi.T("AgentTranslationBridge.NoPack");
            yield break;
        }
        if (!s.LanguageMatches)
        {
            yield return GameApi.T("AgentTranslationBridge.LanguageMismatch", s.PackLanguage ?? "?", s.GameLanguage ?? "?");
        }
        yield return GameApi.T("AgentTranslationBridge.Summary", s.Counts.Values.Sum(), s.Counts[Ok], s.Counts[Overridden], s.Counts[Changed], s.Counts[NotLoaded] + s.Counts[NotInjected], s.Counts[Unknown]);
        yield return GameApi.T("AgentTranslationBridge.ErrorCount", s.ErrorCount);
        if (s.LastRequest is { Kind: InboxRequest.KindReloadKeyed } rr)
        {
            yield return rr.Status == "disabled" ? GameApi.T("AgentTranslationBridge.ReloadDisabled") : GameApi.T("AgentTranslationBridge.ReloadResult", rr.Applied);
        }
        if (s.ProblemMods.Count > 0)
        {
            yield return GameApi.T("AgentTranslationBridge.ProblemsHeader");
            foreach (var (pkg, o, c, m) in s.ProblemMods)
            {
                yield return GameApi.T("AgentTranslationBridge.ProblemRow", pkg, o, c, m);
            }
        }
    }
}
