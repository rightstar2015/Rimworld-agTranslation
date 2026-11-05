using System;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace AgentTranslation.Bridge;

/// <summary>
/// Two ways to open this mod's settings page, each switchable on the settings page:
/// - an icon in the bottom-right play-settings row (needs Harmony; patched through reflection so Harmony stays optional),
/// - a tab button in the bottom main-button bar (a MainButtonDef in Defs; no Harmony).
/// Checked against the decompiled game: PlaySettings.DoPlaySettingsGlobalControls(WidgetRow, bool),
/// WidgetRow.ButtonIcon(Texture2D, string, ...), Dialog_ModSettings(Mod), MainButtonWorker.Visible / Activate(),
/// ContentFinder&lt;Texture2D&gt;.Get, StaticConstructorOnStartup. Harmony postfixes on Waylith: verified in game (see docs).
/// </summary>
[StaticConstructorOnStartup]
internal static class QuickButtons
{
    public static readonly Texture2D Icon = ContentFinder<Texture2D>.Get("AgentTranslation/QuickButton", reportFailure: false) ?? BaseContent.BadTex;

    /// <summary>True once the play-settings postfix is in place; false when Harmony is missing or patching failed.</summary>
    public static bool CornerAvailable { get; private set; }

    public static void OpenSettings()
    {
        var mod = BridgeMod.Instance;
        if (mod == null)
        {
            return;
        }
        if (Find.WindowStack.WindowOfType<Dialog_ModSettings>() == null)
        {
            Find.WindowStack.Add(new Dialog_ModSettings(mod));
        }
    }

    /// <summary>Patches PlaySettings.DoPlaySettingsGlobalControls with a postfix through Harmony, found by reflection.</summary>
    public static void TryPatchCorner()
    {
        try
        {
            var harmonyAsm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "0Harmony");
            var harmonyType = harmonyAsm?.GetType("HarmonyLib.Harmony");
            var methodType = harmonyAsm?.GetType("HarmonyLib.HarmonyMethod");
            if (harmonyType == null || methodType == null)
            {
                GameApi.Log("Harmony not found; the bottom-right button is unavailable (the tab button still works).");
                return;
            }
            var target = typeof(PlaySettings).GetMethod(nameof(PlaySettings.DoPlaySettingsGlobalControls), BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(WidgetRow), typeof(bool) }, null);
            var postfix = typeof(QuickButtons).GetMethod(nameof(PlaySettingsPostfix), BindingFlags.NonPublic | BindingFlags.Static);
            var patch = harmonyType.GetMethod("Patch", new[] { typeof(MethodBase), methodType, methodType, methodType, methodType });
            var ctorMethod = methodType.GetConstructor(new[] { typeof(MethodInfo) });
            if (target == null || postfix == null || patch == null || ctorMethod == null)
            {
                GameApi.Warn("unexpected Harmony or game API; the bottom-right button is unavailable.");
                return;
            }
            var harmony = Activator.CreateInstance(harmonyType, "agenttranslation.bridge");
            patch.Invoke(harmony, new[] { target, null, ctorMethod.Invoke(new object[] { postfix }), null, null });
            CornerAvailable = true;
        }
        catch (Exception ex)
        {
            GameApi.Warn("could not add the bottom-right button: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    // Harmony binds postfix parameters by name: row and worldView match the original method.
    private static void PlaySettingsPostfix(WidgetRow row, bool worldView)
    {
        try
        {
            if (row == null || BridgeMod.Instance?.Settings.ShowCornerButton != true)
            {
                return;
            }
            if (row.ButtonIcon(Icon, GameApi.T("AgentTranslationBridge.QuickButtonTip")))
            {
                OpenSettings();
            }
        }
        catch (Exception)
        {
            // Never break the game's own controls.
        }
    }
}

/// <summary>Bottom bar tab button (Defs/MainButtonDefs.xml). Hidden when the user turns it off on the settings page.</summary>
public sealed class MainButtonWorker_AgentTranslation : MainButtonWorker
{
    public override bool Visible => base.Visible && BridgeMod.Instance?.Settings.ShowTabButton == true;

    public override void Activate() => QuickButtons.OpenSettings();
}
