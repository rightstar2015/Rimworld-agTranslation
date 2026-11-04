using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace AgentTranslation.Bridge;

/// <summary>One entry of the translation pack, with the text the pack provides (literal \n already turned into a newline, as the game does).</summary>
public sealed record PackEntry(string Id, string PackageId, string Kind, string? DefType, string Path, string File, string FullPath, string Expected);

public sealed class PackSnapshot
{
    public required string Root { get; init; }
    public string? BuiltUtc { get; init; }
    /// <summary>Language folder name used by the pack files (e.g. ChineseSimplified), or null when the pack is empty.</summary>
    public string? LanguageFolder { get; init; }
    public List<PackEntry> Entries { get; init; } = new();
}

/// <summary>
/// Reads the pack written by rwat: its .agent-manifest.json and its LanguageData files. Reads only files the manifest
/// lists, and only inside the pack folder. No game dependency (shared with the tests).
/// </summary>
public static class PackReader
{
    public const string ManifestName = ".agent-manifest.json";
    private const long MaxManifestBytes = 64L * 1024 * 1024;
    private const long MaxXmlBytes = 32L * 1024 * 1024;

    /// <summary>The first mod folder that contains a manifest written by rwat.</summary>
    public static string? FindPackRoot(IEnumerable<string> modRoots)
    {
        foreach (var root in modRoots)
        {
            try
            {
                var manifest = System.IO.Path.Combine(root, ManifestName);
                var info = new FileInfo(manifest);
                if (!info.Exists || info.Length > MaxManifestBytes)
                {
                    continue;
                }
                using var doc = JsonDocument.Parse(File.ReadAllBytes(manifest));
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("tool", out var tool) && tool.ValueKind == JsonValueKind.String && tool.GetString() == "rwat")
                {
                    return root;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
            }
        }
        return null;
    }

    public static PackSnapshot Read(string packRoot)
    {
        var rootFull = System.IO.Path.GetFullPath(packRoot).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(System.IO.Path.Combine(rootFull, ManifestName)));
        var m = doc.RootElement;
        var built = m.TryGetProperty("built_utc", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
        var texts = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<PackEntry>();
        string? language = null;
        if (m.TryGetProperty("entries", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in list.EnumerateArray())
            {
                var id = Str(e, "id");
                var kind = Str(e, "kind");
                var path = Str(e, "path");
                var file = Str(e, "file");
                if (id == null || kind is not ("keyed" or "def") || path == null || file == null)
                {
                    continue;
                }
                var full = Resolve(rootFull, file);
                if (full == null)
                {
                    continue;
                }
                var segments = file.Split('/');
                // <version>/Languages/<language>/Keyed/... or <version>/Languages/<language>/DefInjected/<DefType>/...
                if (segments.Length < 5 || segments[1] != "Languages")
                {
                    continue;
                }
                language ??= segments[2];
                var defType = kind == "def" ? segments[4] : null;
                if (!texts.TryGetValue(full, out var values))
                {
                    values = ReadValues(full);
                    texts[full] = values;
                }
                if (!values.TryGetValue(path, out var text))
                {
                    continue;
                }
                entries.Add(new PackEntry(id, Str(e, "package_id") ?? string.Empty, kind, defType, path, System.IO.Path.GetFileName(full), full, text));
            }
        }
        return new PackSnapshot { Root = rootFull, BuiltUtc = built, LanguageFolder = language, Entries = entries };
    }

    /// <summary>Whether a file path lies inside the pack folder.</summary>
    public static bool IsInside(string rootFull, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            return full.StartsWith(rootFull + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string? Resolve(string rootFull, string relative)
    {
        if (relative.Length == 0 || relative.Contains("..", StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative) || relative.Contains(':'))
        {
            return null;
        }
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(rootFull, relative.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        return IsInside(rootFull, full) ? full : null;
    }

    private static Dictionary<string, string> ReadValues(string file)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var info = new FileInfo(file);
        if (!info.Exists || info.Length > MaxXmlBytes)
        {
            return map;
        }
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
        try
        {
            using var reader = XmlReader.Create(file, settings);
            var doc = XDocument.Load(reader);
            foreach (var el in doc.Root?.Elements() ?? Enumerable.Empty<XElement>())
            {
                if (!el.HasElements)
                {
                    map[el.Name.LocalName] = el.Value.Replace("\\n", "\n");
                }
            }
        }
        catch (Exception ex) when (ex is XmlException or IOException)
        {
        }
        return map;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
