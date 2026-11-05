using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentTranslation.Bridge;

/// <summary>
/// The user's switches in rwat's config.json, edited from the in-game settings page so players never have to open the
/// file. Game-independent (tested on its own).
/// - Only three fields are touched: target_language, code_keys.enabled, simplified_auto_adopt.enabled. Every other
///   field and unknown property is kept as it is.
/// - The file must already exist (rwat creates it); the game never creates it. Size is capped and the write is atomic.
/// - The game UI is something only the user can operate; the server and its MCP tools never write these switches.
/// </summary>
internal static class UserConfigFile
{
    public const long MaxBytes = 1024 * 1024;
    /// <summary>A value of target_language and the language's own name (shown in the menu).</summary>
    public sealed record LanguageOption(string Folder, string Native, bool Official);

    /// <summary>
    /// "auto" plus every language RimWorld 1.7 ships. Must match rwat's TargetLanguages.All (a unit test checks it); rwat
    /// rejects any other value.
    /// </summary>
    public static readonly LanguageOption[] Languages =
    {
        new("auto", "", true),
        new("ChineseSimplified", "简体中文", true),
        new("ChineseTraditional", "繁體中文", true),
        new("Japanese", "日本語", true),
        new("Korean", "한국어", true),
        new("English", "English", true),
        new("Catalan", "Català", false),
        new("Czech", "Čeština", false),
        new("Danish", "Dansk", false),
        new("Dutch", "Nederlands", false),
        new("Estonian", "Eesti", false),
        new("Finnish", "Suomi", false),
        new("French", "Français", false),
        new("German", "Deutsch", false),
        new("Greek", "Ελληνικά", false),
        new("Hungarian", "Magyar", false),
        new("Italian", "Italiano", false),
        new("Norwegian", "Norsk Bokmål", false),
        new("Polish", "Polski", false),
        new("Portuguese", "Português", false),
        new("PortugueseBrazilian", "Português Brasileiro", false),
        new("Romanian", "Română", false),
        new("Russian", "Русский", false),
        new("Slovak", "Slovenčina", false),
        new("Spanish", "Español (Castellano)", false),
        new("SpanishLatin", "Español (Latinoamérica)", false),
        new("Swedish", "Svenska", false),
        new("Turkish", "Türkçe", false),
        new("Ukrainian", "Українська", false),
        new("Vietnamese", "Tiếng Việt", false),
    };

    public static LanguageOption? FindLanguage(string? folder) => Array.Find(Languages, l => l.Folder == folder);

    public sealed record Values(string TargetLanguage, bool CodeKeys, bool AutoAdopt);

    /// <summary>config.json next to the bridge folder (&lt;work&gt;/bridge -> &lt;work&gt;/config.json); null for a custom bridge folder.</summary>
    public static string? PathFor(string bridgeDir)
    {
        var dir = new DirectoryInfo(bridgeDir);
        return dir.Name == "bridge" && dir.Parent != null ? Path.Combine(dir.Parent.FullName, "config.json") : null;
    }

    public static Values? Read(string path, out string? error)
    {
        var root = Load(path, out error);
        if (root == null)
        {
            return null;
        }
        var lang = root["target_language"] is JsonValue v && v.TryGetValue<string>(out var s) && FindLanguage(s) != null ? s : "auto";
        return new Values(lang, Flag(root, "code_keys"), Flag(root, "simplified_auto_adopt"));
    }

    public static bool Write(string path, Values values, out string? error)
    {
        if (FindLanguage(values.TargetLanguage) == null)
        {
            error = "invalid_language";
            return false;
        }
        var root = Load(path, out error);
        if (root == null)
        {
            return false;
        }
        root["target_language"] = values.TargetLanguage;
        SetFlag(root, "code_keys", values.CodeKeys);
        SetFlag(root, "simplified_auto_adopt", values.AutoAdopt);
        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(tmp); } catch (Exception) { }
            error = "write_failed";
            return false;
        }
    }

    private static JsonObject? Load(string path, out string? error)
    {
        error = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                error = "not_found";
                return null;
            }
            if (info.Length > MaxBytes)
            {
                error = "size";
                return null;
            }
            var node = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { MaxDepth = 16 });
            if (node is JsonObject obj)
            {
                return obj;
            }
            error = "not_an_object";
            return null;
        }
        catch (JsonException)
        {
            error = "invalid_json";
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = "read_failed";
            return null;
        }
    }

    private static bool Flag(JsonObject root, string section) =>
        root[section] is JsonObject o && o["enabled"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static void SetFlag(JsonObject root, string section, bool value)
    {
        if (root[section] is not JsonObject o)
        {
            o = new JsonObject();
            root[section] = o;
        }
        o["enabled"] = value;
    }
}
