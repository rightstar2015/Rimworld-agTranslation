using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

public sealed class InstalledMod
{
    public required ModAbout About { get; init; }
    public required string Source { get; init; } // official / local / workshop
    /// <summary>在 ModsConfig 中的位置；未啟用為 -1。</summary>
    public int LoadOrder { get; set; } = -1;
    public bool Active => LoadOrder >= 0;
    public string PackageId => About.PackageId;
    public string RootDir => About.RootDir;
}

/// <summary>
/// 遊戲環境快照：版本、語言、已安裝與啟用的模組。只讀取檔案，不碰遊戲行程。
/// 已對照本機 1.7.4998：設定檔位於 LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config，
/// ModsConfig.xml 的 activeMods/li 順序即載入順序；Prefs.xml 的 langFolderName 為目前語言。
/// </summary>
public sealed class GameEnvironment
{
    private GameEnvironment() { }

    public string GameDir { get; private set; } = string.Empty;
    /// <summary>完整版本字串，例如 "1.7.4998 rev1299"。</summary>
    public string VersionFull { get; private set; } = string.Empty;
    /// <summary>主.次版本，例如 "1.7"，用於版本資料夾。</summary>
    public string Version { get; private set; } = string.Empty;
    public string? Language { get; private set; }
    public List<InstalledMod> Mods { get; } = new();
    public List<string> ActiveIds { get; } = new();
    public List<string> MissingActiveIds { get; } = new();

    public IEnumerable<InstalledMod> ActiveMods => Mods.Where(m => m.Active).OrderBy(m => m.LoadOrder);

    public InstalledMod? Find(string packageId)
    {
        var id = PackageIds.Normalize(packageId);
        var active = Mods.FirstOrDefault(m => m.PackageId == id && m.Active);
        return active ?? Mods.FirstOrDefault(m => m.PackageId == id);
    }

    public bool IsActive(string packageId)
    {
        var id = PackageIds.WithoutPostfix(packageId);
        return ActiveIds.Any(a => PackageIds.WithoutPostfix(a) == id);
    }

    public static GameEnvironment Load(AppConfig config)
    {
        var env = new GameEnvironment { GameDir = config.RequireGameDir() };
        var versionFile = Path.Combine(env.GameDir, "Version.txt");
        env.VersionFull = File.Exists(versionFile) ? File.ReadAllText(versionFile).Trim() : "unknown";
        var v = LoadFolderResolver.ParseVersion(env.VersionFull.Split(' ')[0]);
        env.Version = v == null ? "1.7" : v.Major + "." + v.Minor;

        if (config.ConfigDir != null)
        {
            var modsConfig = Path.Combine(config.ConfigDir, "ModsConfig.xml");
            if (File.Exists(modsConfig))
            {
                var doc = SafeXml.LoadFile(modsConfig);
                foreach (var li in doc.Root?.Element("activeMods")?.Elements("li") ?? Enumerable.Empty<System.Xml.Linq.XElement>())
                {
                    env.ActiveIds.Add(PackageIds.Normalize(li.Value));
                }
            }
            var prefs = Path.Combine(config.ConfigDir, "Prefs.xml");
            if (File.Exists(prefs))
            {
                env.Language = SafeXml.LoadFile(prefs).Root?.Element("langFolderName")?.Value;
            }
        }

        void AddFrom(string dir, string source)
        {
            if (!Directory.Exists(dir))
            {
                return;
            }
            foreach (var modDir in Directory.GetDirectories(dir))
            {
                var about = ModAbout.TryLoad(modDir);
                if (about != null)
                {
                    env.Mods.Add(new InstalledMod { About = about, Source = source });
                }
            }
        }

        AddFrom(Path.Combine(env.GameDir, "Data"), "official");
        AddFrom(Path.Combine(env.GameDir, "Mods"), "local");
        foreach (var ws in config.WorkshopDirs)
        {
            AddFrom(ws, "workshop");
        }

        for (int i = 0; i < env.ActiveIds.Count; i++)
        {
            var id = env.ActiveIds[i];
            // 本機 Mods 優先於工坊（遊戲對同 packageId 的取用細節【未確認】，此處採本機優先）。
            var mod = env.Mods.Where(m => m.PackageId == id || PackageIds.WithoutPostfix(m.PackageId) == PackageIds.WithoutPostfix(id))
                .OrderBy(m => m.Source == "official" ? 0 : m.Source == "local" ? 1 : 2)
                .FirstOrDefault(m => !m.Active);
            if (mod != null)
            {
                mod.LoadOrder = i;
            }
            else
            {
                env.MissingActiveIds.Add(id);
            }
        }
        return env;
    }

    public List<string> LoadFoldersFor(InstalledMod mod) => LoadFolderResolver.Resolve(mod.RootDir, Version, IsActive);
}
