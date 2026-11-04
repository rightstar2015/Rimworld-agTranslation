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
    /// <summary>Position in ModsConfig; -1 when not active.</summary>
    public int LoadOrder { get; set; } = -1;
    public bool Active => LoadOrder >= 0;
    public string PackageId => About.PackageId;
    public string RootDir => About.RootDir;
}

/// <summary>
/// Snapshot of the game environment: version, language, installed and active mods. Reads files only; never touches the game process.
/// Checked against the local 1.7.4998: config lives in LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config,
/// the order of ModsConfig.xml activeMods/li is the load order, and Prefs.xml langFolderName is the current language.
/// </summary>
public sealed class GameEnvironment
{
    private GameEnvironment() { }

    public string GameDir { get; private set; } = string.Empty;
    /// <summary>Full version string, e.g. "1.7.4998 rev1299".</summary>
    public string VersionFull { get; private set; } = string.Empty;
    /// <summary>Major.minor, e.g. "1.7", used for version folders.</summary>
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
            // Local Mods win over Workshop copies (how the game picks between the same packageId is [unverified]; local first here).
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
