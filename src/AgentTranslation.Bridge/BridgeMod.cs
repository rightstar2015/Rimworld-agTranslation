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
        list.Gap();
        foreach (var line in Bridge.StatusLines())
        {
            list.Label(line);
        }
        list.End();
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
