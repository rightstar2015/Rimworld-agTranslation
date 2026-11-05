using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    /// <summary>Settings page mode: false = simple (default), true = advanced.</summary>
    public bool AdvancedMode;
    /// <summary>Icon in the bottom-right play-settings row (needs Harmony).</summary>
    public bool ShowCornerButton = true;
    /// <summary>Tab button in the bottom main-button bar.</summary>
    public bool ShowTabButton = true;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref ReportEnabled, "reportEnabled", true);
        Scribe_Values.Look(ref ReloadEnabled, "reloadEnabled", false);
        Scribe_Values.Look(ref AdvancedMode, "advancedMode", false);
        Scribe_Values.Look(ref ShowCornerButton, "showCornerButton", true);
        Scribe_Values.Look(ref ShowTabButton, "showTabButton", true);
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
        QuickButtons.TryPatchCorner();
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

    private Vector2 _scroll;
    private float _contentHeight = 900f;

    /// <summary>
    /// Settings page. Simple mode (default): how-to, status with a next step, pack language, quick-button switches.
    /// Advanced mode adds the in-game check switches, rwat's advanced switches and the full report. The page scrolls.
    /// </summary>
    public override void DoSettingsWindowContents(Rect inRect)
    {
        var view = new Rect(0f, 0f, inRect.width - 20f, Math.Max(_contentHeight, inRect.height));
        Widgets.BeginScrollView(inRect, ref _scroll, view);
        var list = new Listing_Standard();
        list.Begin(view);

        DrawModeSwitch(list);
        list.GapLine();
        if (!Settings.AdvancedMode)
        {
            Header(list, "AgentTranslationBridge.HowToHeader");
            list.Label(GameApi.T("AgentTranslationBridge.HowTo1"));
            list.Label(GameApi.T("AgentTranslationBridge.HowTo2"));
            list.Label(GameApi.T("AgentTranslationBridge.HowTo3"));
            list.GapLine();
        }

        Header(list, "AgentTranslationBridge.StatusHeader");
        var (status, next) = Bridge.SimpleStatus();
        list.Label(status);
        if (next.Length > 0)
        {
            list.Label(GameApi.T("AgentTranslationBridge.NextStep", next));
        }
        list.GapLine();

        var config = ToolConfig(list);
        if (config != null)
        {
            DrawLanguage(list, config);
        }
        list.GapLine();

        Header(list, "AgentTranslationBridge.ButtonsHeader");
        list.CheckboxLabeled(GameApi.T("AgentTranslationBridge.ShowCornerButton") + (QuickButtons.CornerAvailable ? string.Empty : GameApi.T("AgentTranslationBridge.NeedsHarmony")),
            ref Settings.ShowCornerButton, GameApi.T("AgentTranslationBridge.ShowCornerButtonTip"));
        list.CheckboxLabeled(GameApi.T("AgentTranslationBridge.ShowTabButton"), ref Settings.ShowTabButton, GameApi.T("AgentTranslationBridge.ShowTabButtonTip"));
        list.Label(GameApi.T("AgentTranslationBridge.Immediate"));

        if (Settings.AdvancedMode)
        {
            list.GapLine();
            Header(list, "AgentTranslationBridge.CheckHeader");
            list.CheckboxLabeled(GameApi.T("AgentTranslationBridge.ReportEnabled"), ref Settings.ReportEnabled, GameApi.T("AgentTranslationBridge.ReportEnabledTip"));
            list.CheckboxLabeled(GameApi.T("AgentTranslationBridge.ReloadEnabled"), ref Settings.ReloadEnabled, GameApi.T("AgentTranslationBridge.ReloadEnabledTip"));
            if (list.ButtonText(GameApi.T("AgentTranslationBridge.Refresh")))
            {
                Bridge.RequestReport("settings_button");
            }
            if (Settings.ReloadEnabled && list.ButtonText(GameApi.T("AgentTranslationBridge.ReloadNow")))
            {
                Bridge.RequestReloadKeyed(null);
            }
            if (config != null)
            {
                list.GapLine();
                DrawAdvancedTool(list, config);
            }
            list.GapLine();
            Header(list, "AgentTranslationBridge.ReportHeader");
            foreach (var line in Bridge.StatusLines())
            {
                list.Label(line);
            }
        }

        _contentHeight = list.CurHeight + 24f;
        list.End();
        Widgets.EndScrollView();
    }

    private static void Header(Listing_Standard list, string key)
    {
        Text.Font = GameFont.Medium;
        list.Label(GameApi.T(key));
        Text.Font = GameFont.Small;
    }

    private void DrawModeSwitch(Listing_Standard list)
    {
        var row = list.GetRect(32f);
        var simple = (Settings.AdvancedMode ? string.Empty : "▶ ") + GameApi.T("AgentTranslationBridge.ModeSimple");
        var advanced = (Settings.AdvancedMode ? "▶ " : string.Empty) + GameApi.T("AgentTranslationBridge.ModeAdvanced");
        if (Widgets.ButtonText(row.LeftHalf().ContractedBy(2f), simple))
        {
            Settings.AdvancedMode = false;
        }
        if (Widgets.ButtonText(row.RightHalf().ContractedBy(2f), advanced))
        {
            Settings.AdvancedMode = true;
        }
        list.Label(GameApi.T(Settings.AdvancedMode ? "AgentTranslationBridge.ModeAdvancedTip" : "AgentTranslationBridge.ModeSimpleTip"));
    }

    private UserConfigFile.Values? _toolConfig;
    private string? _toolConfigError;
    private DateTime _toolConfigReadUtc;
    private bool _toolConfigSaved;

    /// <summary>
    /// The user's rwat switches (stored in config.json). Re-read at most every 2 seconds while the window is open, so
    /// edits made in the file show up too. Writes a label and returns null when the file cannot be used.
    /// </summary>
    private UserConfigFile.Values? ToolConfig(Listing_Standard list)
    {
        var path = Bridge.UserConfigPath;
        if (path == null)
        {
            list.Label(GameApi.T("AgentTranslationBridge.ToolSettingsCustomDir"));
            return null;
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
        }
        return _toolConfig;
    }

    private void SaveToolConfig(UserConfigFile.Values changed)
    {
        var path = Bridge.UserConfigPath;
        if (path == null || changed == _toolConfig)
        {
            return;
        }
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

    private static string LanguageLabel(string folder)
    {
        if (folder == "auto")
        {
            return GameApi.T("AgentTranslationBridge.Language.auto");
        }
        var option = UserConfigFile.FindLanguage(folder);
        var label = option == null ? folder : option.Native + " (" + option.Folder + ")";
        return option is { Official: false } ? label + GameApi.T("AgentTranslationBridge.LanguageExperimental") : label;
    }

    private void DrawLanguage(Listing_Standard list, UserConfigFile.Values config)
    {
        Header(list, "AgentTranslationBridge.LanguageHeader");
        if (list.ButtonText(LanguageLabel(config.TargetLanguage) + "  ▼"))
        {
            var options = new List<FloatMenuOption>();
            foreach (var option in UserConfigFile.Languages.OrderByDescending(o => o.Official))
            {
                var folder = option.Folder;
                options.Add(new FloatMenuOption(LanguageLabel(folder), () =>
                {
                    if (_toolConfig != null)
                    {
                        SaveToolConfig(_toolConfig with { TargetLanguage = folder });
                    }
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
        list.Label(GameApi.T("AgentTranslationBridge.LanguageTip"));
        list.Label(GameApi.T(_toolConfigSaved ? "AgentTranslationBridge.ToolSettingsSaved" : "AgentTranslationBridge.ToolSettingsNote"));
    }

    private void DrawAdvancedTool(Listing_Standard list, UserConfigFile.Values config)
    {
        Header(list, "AgentTranslationBridge.ToolSettings");
        var codeKeys = config.CodeKeys;
        var autoAdopt = config.AutoAdopt;
        list.CheckboxLabeled(GameApi.T("AgentTranslationBridge.CodeKeys"), ref codeKeys, GameApi.T("AgentTranslationBridge.CodeKeysTip"));
        list.CheckboxLabeled(GameApi.T("AgentTranslationBridge.AutoAdopt"), ref autoAdopt, GameApi.T("AgentTranslationBridge.AutoAdoptTip"));
        SaveToolConfig(config with { CodeKeys = codeKeys, AutoAdopt = autoAdopt });
        list.Label(GameApi.T("AgentTranslationBridge.ToolSettingsNote"));
        if (Bridge.UserConfigPath is { } path)
        {
            list.Label(GameApi.T("AgentTranslationBridge.ConfigPath", path));
        }
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
