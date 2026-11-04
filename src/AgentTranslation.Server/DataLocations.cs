using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AgentTranslation.Server;

/// <summary>
/// Where the work folder and the bridge channel live.
/// Default: a folder inside RimWorld's own data folder (the folder that holds Saves and Config). Reasons:
/// - Packaged (MSIX) apps such as the Claude desktop app get AppData folders they create redirected to private
///   storage. Writes into a folder that already exists outside the package (the game creates its data folder) reach the
///   real location, so the game and every other process see the same files (observed 2026-11-04, not from docs).
/// - The bridge mod finds the same folder through the game's GenFilePaths.SaveDataFolderPath without any setting.
/// RWAT_WORK_DIR and RWAT_BRIDGE_DIR override the defaults.
/// </summary>
public static class DataLocations
{
    public const string FolderName = "AgentTranslation";
    public const string LegacyFolderName = "RimWorldAgentTranslation";

    /// <summary>RimWorld's default data folder: %USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios.</summary>
    public static string GameDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Ludeon Studios", "RimWorld by Ludeon Studios");

    public static string DefaultWorkDir => Path.Combine(GameDataDir, FolderName);

    /// <summary>Work folder of versions before stage 5.</summary>
    public static string LegacyWorkDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyFolderName);

    /// <summary>Bridge inbox/outbox. Same rule as the bridge mod: RWAT_BRIDGE_DIR, else &lt;game data&gt;/AgentTranslation/bridge.</summary>
    public static string BridgeDir
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("RWAT_BRIDGE_DIR");
            return !string.IsNullOrWhiteSpace(custom) && Path.IsPathRooted(custom)
                ? Path.GetFullPath(custom)
                : Path.Combine(DefaultWorkDir, "bridge");
        }
    }

    /// <summary>
    /// Copies the legacy work folder into a new, empty work folder (everything except the old bridge channel) and leaves
    /// a note in the legacy folder. Nothing is deleted. Returns true when something was copied.
    /// </summary>
    public static bool MigrateLegacy(string workDir, string? legacyDir = null)
    {
        var legacy = legacyDir ?? LegacyWorkDir;
        if (File.Exists(Path.Combine(workDir, "config.json")) || !File.Exists(Path.Combine(legacy, "config.json")))
        {
            return false;
        }
        Directory.CreateDirectory(workDir);
        foreach (var file in Directory.GetFiles(legacy, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(legacy, file);
            var top = rel.Split(Path.DirectorySeparatorChar)[0];
            if (top == "bridge" || rel == "MOVED.txt" || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }
            var dest = Path.Combine(workDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: false);
        }
        File.WriteAllText(Path.Combine(legacy, "MOVED.txt"),
            "This work folder was copied to " + workDir + " and is no longer used. It can be deleted.\n", new UTF8Encoding(false));
        return true;
    }

    /// <summary>
    /// Full name of the package this process runs in, or null. Informational only: processes started by the Claude
    /// desktop app were observed to have AppData redirection but no package identity, so this cannot detect redirection.
    /// </summary>
    public static string? PackageName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        try
        {
            uint length = 0;
            var rc = GetCurrentPackageFullName(ref length, null);
            if (rc != ErrorInsufficientBuffer || length == 0 || length > 1024)
            {
                return null;
            }
            var buffer = new char[length];
            rc = GetCurrentPackageFullName(ref length, buffer);
            return rc == 0 ? new string(buffer, 0, (int)Math.Max(0, length - 1)) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>True when the path is under AppData\Local or AppData\Roaming, where a packaged app's new folders are redirected.</summary>
    public static bool IsRedirectable(string path)
    {
        var full = Path.GetFullPath(path);
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) })
        {
            if (!string.IsNullOrEmpty(root) && full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private const int ErrorInsufficientBuffer = 122;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentPackageFullName(ref uint packageFullNameLength, [Out] char[]? packageFullName);
}
