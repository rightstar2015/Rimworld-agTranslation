using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace AgentTranslation.Bridge;

/// <summary>
/// The only place that touches game types. Every member used here was checked against the decompiled
/// Assembly-CSharp of RimWorld 1.7.4998 (Waylith) and is public:
/// LanguageDatabase.activeLanguage, LoadedLanguage.folderName/keyedReplacements/defInjections/loadErrors,
/// KeyedReplacement.key/value/fileSource/fileSourceFullPath/isPlaceholder, DefInjectionPackage.defType/injections/loadErrors,
/// DefInjection.injection/injected/fileSource, GenDefDatabase.GetDefSilentFail, LoadedModManager.RunningModsListForReading,
/// ModContentPack.PackageId/RootDir, VersionControl.CurrentVersionStringWithRev, Current.ProgramState,
/// LongEventHandler.ExecuteWhenFinished, GenFilePaths.SaveDataFolderPath (cached by the game before mods load).
/// Callers must be on the main thread. Any failure disables the feature instead of guessing another member.
/// </summary>
internal static class GameApi
{
    public static void Log(string message) => Verse.Log.Message("[AgentTranslation.Bridge] " + message);

    public static void Warn(string message) => Verse.Log.Warning("[AgentTranslation.Bridge] " + message);

    public static void WhenLoaded(Action action) => LongEventHandler.ExecuteWhenFinished(action);

    public static string GameVersion => VersionControl.CurrentVersionStringWithRev;

    public static bool AtMainMenu => Current.ProgramState == ProgramState.Entry;

    public static bool Playing => Current.ProgramState == ProgramState.Playing;

    public static float RealTime => Time.realtimeSinceStartup;

    public static void CreatePump()
    {
        var go = new GameObject("AgentTranslation.Bridge.Pump");
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<BridgePump>();
    }

    public sealed record ModInfo(string PackageId, string RootDir, int Order);

    public static List<ModInfo> RunningMods() =>
        LoadedModManager.RunningModsListForReading.Select((m, i) => new ModInfo(m.PackageId ?? string.Empty, m.RootDir ?? string.Empty, i)).ToList();

    public static (string Folder, string Legacy)? ActiveLanguage()
    {
        var lang = LanguageDatabase.activeLanguage;
        return lang == null ? null : (lang.folderName, lang.LegacyFolderName);
    }

    /// <summary>Same count as the game's "Translation data for language X has N errors" warning.</summary>
    public static int TranslationErrorCount()
    {
        var lang = LanguageDatabase.activeLanguage;
        if (lang == null)
        {
            return -1;
        }
        return lang.loadErrors.Count + lang.defInjections.Sum(p => p.loadErrors.Count);
    }

    public sealed record KeyedState(string Value, string? FullPath, bool Placeholder);

    public static KeyedState? Keyed(string key)
    {
        var lang = LanguageDatabase.activeLanguage;
        if (lang == null || !lang.keyedReplacements.TryGetValue(key, out var kr) || kr == null)
        {
            return null;
        }
        return new KeyedState(kr.value ?? string.Empty, kr.fileSourceFullPath, kr.isPlaceholder);
    }

    /// <summary>Writes one Keyed value into the active language, the same way the game's loader does (SetOrAdd).</summary>
    public static void SetKeyed(string key, string value, string fileName, string fullPath)
    {
        var lang = LanguageDatabase.activeLanguage ?? throw new InvalidOperationException("No active language.");
        lang.keyedReplacements[key] = new LoadedLanguage.KeyedReplacement
        {
            key = key,
            value = value,
            fileSource = fileName,
            fileSourceLine = 0,
            fileSourceFullPath = fullPath,
            isPlaceholder = false,
        };
    }

    public sealed record InjectionState(string? Injection, bool Injected, string? FileSource, Type DefType);

    /// <summary>The DefInjected entry for a Def type folder name (short or full type name) and path, or null when absent.</summary>
    public static InjectionState? DefInjection(string defTypeName, string path)
    {
        var lang = LanguageDatabase.activeLanguage;
        if (lang == null)
        {
            return null;
        }
        foreach (var package in lang.defInjections)
        {
            var t = package.defType;
            if (t == null || !(t.Name == defTypeName || t.FullName == defTypeName))
            {
                continue;
            }
            if (package.injections.TryGetValue(path, out var inj) && inj != null)
            {
                return new InjectionState(inj.injection, inj.injected, inj.fileSource, t);
            }
        }
        return null;
    }

    /// <summary>The game's data folder (GenFilePaths.SaveDataFolderPath: holds Saves and Config; honours -savedatafolder).</summary>
    public static string SaveDataFolder() => GenFilePaths.SaveDataFolderPath;

    /// <summary>Current value of a plain string field on a Def (paths of the form defName.field only), or null.</summary>
    public static string? DefStringField(Type defType, string defName, string field)
    {
        var def = GenDefDatabase.GetDefSilentFail(defType, defName, false);
        if (def == null)
        {
            return null;
        }
        var f = def.GetType().GetField(field, BindingFlags.Public | BindingFlags.Instance);
        return f != null && f.FieldType == typeof(string) ? (string?)f.GetValue(def) : null;
    }

    public static string T(string key) => key.Translate();

    public static string T(string key, params object[] args) =>
        key.Translate(args.Select(a => new NamedArgument(a, null)).ToArray());
}
