using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace AgentTranslation.Core
{
    /// <summary>Required fields of About/About.xml.</summary>
    public sealed class ModAbout
    {
        public string PackageId { get; set; } = string.Empty;
        /// <summary>packageId as written in About.xml (case kept; used for loadAfter).</summary>
        public string PackageIdRaw { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public List<string> SupportedVersions { get; } = new List<string>();
        public string RootDir { get; set; } = string.Empty;

        public static ModAbout? TryLoad(string modRoot)
        {
            var path = Path.Combine(modRoot, "About", "About.xml");
            if (!File.Exists(path))
            {
                return null;
            }
            try
            {
                var doc = SafeXml.LoadFile(path);
                var root = doc.Root;
                if (root == null)
                {
                    return null;
                }
                var about = new ModAbout
                {
                    PackageIdRaw = (ChildIgnoreCase(root, "packageId")?.Value ?? string.Empty).Trim(),
                    Name = (ChildIgnoreCase(root, "name")?.Value ?? string.Empty).Trim(),
                    Author = (ChildIgnoreCase(root, "author")?.Value ?? string.Empty).Trim(),
                    RootDir = modRoot,
                };
                about.PackageId = PackageIds.Normalize(about.PackageIdRaw);
                var versions = ChildIgnoreCase(root, "supportedVersions");
                if (versions != null)
                {
                    about.SupportedVersions.AddRange(versions.Elements("li").Select(e => e.Value.Trim()));
                }
                return about.PackageId.Length == 0 ? null : about;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static XElement? ChildIgnoreCase(XElement parent, string name)
        {
            return parent.Elements().FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Decides which folders a mod actually loads (highest priority first).
    /// Re-implements ModContentPack.InitLoadFolders from RimWorld 1.7.4998 Assembly-CSharp (written from scratch, no code copied):
    /// 1. LoadFolders.xml with versions: use the current version; else the highest version below it; else default.
    ///    Entries are added in reverse; IfModActive/IfModActiveAll/IfModNotActive that do not match are skipped.
    /// 2. Otherwise: root/current version; if missing, the highest version folder below current (the lowest if all are newer);
    ///    then Common, then the root.
    /// </summary>
    public static class LoadFolderResolver
    {
        public static List<string> Resolve(string modRoot, string currentVersion, Func<string, bool> isModActive)
        {
            var result = new List<string>();
            var current = ParseVersion(currentVersion) ?? new Version(0, 0);
            var loadFoldersPath = Path.Combine(modRoot, "LoadFolders.xml");
            if (File.Exists(loadFoldersPath))
            {
                var defined = ReadLoadFolders(loadFoldersPath);
                if (defined.Count > 0)
                {
                    List<LoadFolderEntry>? list = null;
                    if (defined.TryGetValue(currentVersion, out var exact) && exact.Count > 0)
                    {
                        list = exact;
                    }
                    else
                    {
                        var best = defined.Keys
                            .Where(k => k != "default" && k.Contains("."))
                            .Select(k => new { Key = k, V = ParseVersion(k) })
                            .Where(x => x.V != null && x.V <= current)
                            .OrderByDescending(x => x.Key, StringComparer.Ordinal)
                            .FirstOrDefault();
                        if (best != null)
                        {
                            list = defined[best.Key];
                        }
                        else if (defined.TryGetValue("default", out var def))
                        {
                            list = def;
                        }
                    }
                    if (list != null)
                    {
                        for (int i = list.Count - 1; i >= 0; i--)
                        {
                            if (list[i].ShouldLoad(isModActive))
                            {
                                result.Add(list[i].Folder.Length == 0 ? modRoot : Path.Combine(modRoot, list[i].Folder));
                            }
                        }
                        return result;
                    }
                }
            }

            var exactDir = Path.Combine(modRoot, currentVersion);
            if (Directory.Exists(exactDir))
            {
                result.Add(exactDir);
            }
            else if (Directory.Exists(modRoot))
            {
                var versions = new List<Version>();
                foreach (var dir in Directory.GetDirectories(modRoot))
                {
                    var v = ParseVersion(Path.GetFileName(dir));
                    if (v != null)
                    {
                        versions.Add(v);
                    }
                }
                versions.Sort();
                var chosen = new Version(0, 0);
                foreach (var v in versions)
                {
                    if ((v > chosen || chosen > current) && (v <= current || chosen.Major == 0))
                    {
                        chosen = v;
                    }
                }
                if (chosen.Major > 0)
                {
                    result.Add(Path.Combine(modRoot, chosen.ToString()));
                }
            }
            var common = Path.Combine(modRoot, "Common");
            if (Directory.Exists(common))
            {
                result.Add(common);
            }
            result.Add(modRoot);
            return result;
        }

        /// <summary>Collects all *.xml under subPath in load-folder priority order; for each relative path only the highest priority file is kept.</summary>
        public static List<(string RelativeToMod, string FullPath)> CollectFiles(string modRoot, List<string> folders, string subPath, string pattern = "*.xml")
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = new List<(string, string)>();
            foreach (var folder in folders)
            {
                var dir = Path.Combine(folder, subPath);
                if (!Directory.Exists(dir))
                {
                    continue;
                }
                foreach (var file in Directory.GetFiles(dir, pattern, SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                {
                    var name = Path.GetFileName(file);
                    if (name.StartsWith(".", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    var relToFolder = file.Substring(folder.TrimEnd(Path.DirectorySeparatorChar).Length + 1);
                    if (!seen.Add(relToFolder))
                    {
                        continue;
                    }
                    var relToMod = file.Substring(modRoot.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/');
                    files.Add((relToMod, file));
                }
            }
            return files;
        }

        public static Version? ParseVersion(string text)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains("."))
            {
                return null;
            }
            var parts = text.Split('.');
            if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            {
                return null;
            }
            return new Version(major, minor);
        }

        private sealed class LoadFolderEntry
        {
            public string Folder = string.Empty;
            public List<string>? AnyOf;
            public List<string>? AllOf;
            public List<string>? NoneOf;

            public bool ShouldLoad(Func<string, bool> isActive)
            {
                if (AnyOf != null && AnyOf.Count > 0 && !AnyOf.Any(isActive))
                {
                    return false;
                }
                if (AllOf != null && AllOf.Count > 0 && !AllOf.All(isActive))
                {
                    return false;
                }
                if (NoneOf != null && NoneOf.Count > 0 && NoneOf.Any(isActive))
                {
                    return false;
                }
                return true;
            }
        }

        private static Dictionary<string, List<LoadFolderEntry>> ReadLoadFolders(string path)
        {
            var result = new Dictionary<string, List<LoadFolderEntry>>(StringComparer.Ordinal);
            XDocument doc;
            try
            {
                doc = SafeXml.LoadFile(path);
            }
            catch (Exception)
            {
                return result;
            }
            if (doc.Root == null)
            {
                return result;
            }
            foreach (var versionNode in doc.Root.Elements())
            {
                var key = versionNode.Name.LocalName.ToLowerInvariant();
                if (key.StartsWith("v", StringComparison.Ordinal))
                {
                    key = key.Substring(1);
                }
                if (!result.TryGetValue(key, out var list))
                {
                    list = new List<LoadFolderEntry>();
                    result[key] = list;
                }
                foreach (var li in versionNode.Elements())
                {
                    var text = li.Value;
                    list.Add(new LoadFolderEntry
                    {
                        Folder = text == "/" || text == "\\" ? string.Empty : text.Replace('/', Path.DirectorySeparatorChar),
                        AnyOf = Split(li.Attribute("IfModActive")?.Value),
                        AllOf = Split(li.Attribute("IfModActiveAll")?.Value),
                        NoneOf = Split(li.Attribute("IfModNotActive")?.Value),
                    });
                }
            }
            return result;
        }

        private static List<string>? Split(string? value)
        {
            return value?.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }
    }
}
