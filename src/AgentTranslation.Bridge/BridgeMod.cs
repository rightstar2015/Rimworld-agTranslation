using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Verse;

namespace AgentTranslation.Bridge;

public sealed class BridgeSettings : ModSettings
{
    public bool ReportEnabled = true;
    /// <summary>Experimental Keyed reload; off by default and only the user can turn it on (in game).</summary>
    public bool ReloadEnabled;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref ReportEnabled, "reportEnabled", true);
        Scribe_Values.Look(ref ReloadEnabled, "reloadEnabled", false);
    }
}

/// <summary>Entry point created by the game for every Mod subclass. Does nothing heavy here: work starts after loading.</summary>
public sealed class BridgeMod : Mod
{
    internal static BridgeMod? Instance { get; private set; }
    internal BridgeSettings Settings { get; }
    internal Bridge Bridge { get; }

    public BridgeMod(ModContentPack content) : base(content)
    {
        Instance = this;
        Settings = GetSettings<BridgeSettings>();
        Bridge = new Bridge(this);
        GameApi.WhenLoaded(() =>
        {
            try
            {
                GameApi.CreatePump();
                Bridge.Start();
            }
            catch (Exception ex)
            {
                GameApi.Warn("disabled: " + ex.GetType().Name + ": " + ex.Message);
            }
        });
    }

    public override string SettingsCategory() => GameApi.T("AgentTranslationBridge.Title");

    public override void DoSettingsWindowContents(Rect inRect)
    {
        var list = new Listing_Standard();
        list.Begin(inRect);
        list.CheckboxLabeled(GameApi.T("AgentTranslationBridge.ReportEnabled"), ref Settings.ReportEnabled, GameApi.T("AgentTranslationBridge.ReportEnabledTip"));
        list.CheckboxLabeled(GameApi.T("AgentTranslationBridge.ReloadEnabled"), ref Settings.ReloadEnabled, GameApi.T("AgentTranslationBridge.ReloadEnabledTip"));
        list.GapLine();
        if (list.ButtonText(GameApi.T("AgentTranslationBridge.Refresh")))
        {
            Bridge.RequestReport("settings_button");
        }
        if (Settings.ReloadEnabled && list.ButtonText(GameApi.T("AgentTranslationBridge.ReloadNow")))
        {
            Bridge.RequestReloadKeyed(null);
        }
        // Settings first: the report below can grow and must not push them out of the window.
        list.GapLine();
        DrawToolSettings(list);
        list.GapLine();
        foreach (var line in Bridge.StatusLines())
        {
            list.Label(line);
        }
        list.End();
    }

    private UserConfigFile.Values? _toolConfig;
    private string? _toolConfigError;
    private DateTime _toolConfigReadUtc;
    private bool _toolConfigSaved;

    /// <summary>
    /// The user's rwat switches (stored in config.json). Re-read at most every 2 seconds while the window is open, so
    /// edits made in the file show up too.
    /// </summary>
    private void DrawToolSettings(Listing_Standard list)
    {
        list.Label(GameApi.T("AgentTranslationBridge.ToolSettings"));
        var path = Bridge.UserConfigPath;
        if (path == null)
        {
            list.Label(GameApi.T("AgentTranslationBridge.ToolSettingsCustomDir"));
            return;
        }
        if (DateTime.UtcNow - _toolConfigReadUtc > TimeSpan.FromSeconds(2))
        {
            _toolConfig = UserConfigFile.Read(path, out _toolConfigError);
            _toolConfigReadUtc = DateTime.UtcNow;
        }
        if (_toolConfig == null)
        {
            list.Label(_toolConfigError == "not_found"
                ? GameApi.T("AgentTranslationBridge.ToolSettingsMissing", path)
                : GameApi.T("AgentTranslationBridge.ToolSettingsError", _toolConfigError ?? "?"));
            return;
        }
        var current = _toolConfig;
        var lang = current.TargetLanguage;
        list.Label(GameApi.T("AgentTranslationBridge.LanguageHeader"));
        foreach (var option in UserConfigFile.Languages)
        {
            if (list.RadioButton(GameApi.T("AgentTranslationBridge.Language." + option), lang == option, 12f))
            {
                lang = option;
            }
        }
        var codeKeys = current.CodeKeys;
        var autoAdopt = current.AutoAdopt;
        list.CheckboxLabeled(GameApi.T("AgentTranslationBridge.CodeKeys"), ref codeKeys, GameApi.T("AgentTranslationBridge.CodeKeysTip"));
        list.CheckboxLabeled(GameApi.T("AgentTranslationBridge.AutoAdopt"), ref autoAdopt, GameApi.T("AgentTranslationBridge.AutoAdoptTip"));
        var changed = new UserConfigFile.Values(lang, codeKeys, autoAdopt);
        if (changed != current)
        {
            if (UserConfigFile.Write(path, changed, out var error))
            {
                _toolConfig = changed;
                _toolConfigSaved = true;
            }
            else
            {
                _toolConfigError = error;
                _toolConfig = null;
            }
            _toolConfigReadUtc = DateTime.UtcNow;
        }
        list.Label(GameApi.T(_toolConfigSaved ? "AgentTranslationBridge.ToolSettingsSaved" : "AgentTranslationBridge.ToolSettingsNote"));
    }
}

/// <summary>Main-thread work queue, drained every frame within a small time budget.</summary>
public sealed class BridgePump : MonoBehaviour
{
    private const double BudgetMs = 2.0;
    private static readonly ConcurrentQueue<Action> Queue = new();
    private readonly Stopwatch _watch = new();

    internal static void Post(Action action) => Queue.Enqueue(action);

    public void Update()
    {
        _watch.Restart();
        while (_watch.Elapsed.TotalMilliseconds < BudgetMs && Queue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                GameApi.Warn("task failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
        BridgeMod.Instance?.Bridge.Tick();
    }
}
