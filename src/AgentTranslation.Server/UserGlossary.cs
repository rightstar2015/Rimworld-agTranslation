using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

/// <summary>
/// 一筆使用者術語。
/// kind = "s2t"：簡中詞 → 繁中詞，參與簡轉繁（優先於官方術語與 OpenCC）。
/// kind = "en"：英文詞 → 繁中譯名，在 get_batch 中提示 Agent 保持一致。
/// origin：builtin（內建）、user（使用者以 CLI 新增）、agent（經 MCP glossary_add 新增）。
/// </summary>
public sealed class GlossaryEntry
{
    public string Kind { get; set; } = "en";
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string Origin { get; set; } = "user";
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>工作資料夾內的 glossary.json。只有這個類別會寫入它。</summary>
public sealed class UserGlossary
{
    public const int MaxEntries = 5000;
    private static readonly Regex EnglishTerm = new(@"^[A-Za-z0-9][A-Za-z0-9 '\-\.]{0,63}$", RegexOptions.CultureInvariant);

    /// <summary>OpenCC 沒處理、但台灣慣用的寫法。只放查證過的少數詞，其餘交給使用者新增。</summary>
    public static readonly IReadOnlyList<GlossaryEntry> Builtin = new[]
    {
        new GlossaryEntry { Kind = "s2t", Source = "身份", Target = "身分", Note = "教育部用字", Origin = "builtin" },
        new GlossaryEntry { Kind = "s2t", Source = "调试", Target = "偵錯", Note = "台灣軟體用語（debug）", Origin = "builtin" },
    };

    private readonly string _path;

    private UserGlossary(string path)
    {
        _path = path;
    }

    public List<GlossaryEntry> Entries { get; private set; } = new();

    /// <summary>內建 + 檔案內容；同 kind+source 時檔案內容優先。</summary>
    public IEnumerable<GlossaryEntry> Effective =>
        Entries.Concat(Builtin.Where(b => !Entries.Any(e => e.Kind == b.Kind && e.Source == b.Source)));

    public static UserGlossary Load(string workDir)
    {
        var g = new UserGlossary(PathSandbox.Resolve(workDir, "glossary.json"));
        if (File.Exists(g._path))
        {
            var info = new FileInfo(g._path);
            if (info.Length > 8 * 1024 * 1024)
            {
                throw new InvalidOperationException("glossary.json 超過 8 MB。");
            }
            var list = JsonSerializer.Deserialize<List<GlossaryEntry>>(File.ReadAllText(g._path), AppConfig.Json) ?? new();
            // 檔案可能被手動編輯：逐筆驗證，不合格的略過。
            g.Entries = list.Where(e => Check(e.Kind, e.Source, e.Target, e.Note) == null).Take(MaxEntries).ToList();
        }
        return g;
    }

    /// <summary>驗證一筆術語；合格回傳 null，否則回傳原因。</summary>
    public static string? Check(string? kind, string? source, string? target, string? note)
    {
        if (kind is not ("s2t" or "en"))
        {
            return "kind 必須是 s2t 或 en。";
        }
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
        {
            return "source 與 target 不可為空。";
        }
        if (target.Length > 64 || target.Any(char.IsControl) || target.IndexOfAny(new[] { '<', '>', '{', '}' }) >= 0)
        {
            return "target 最多 64 字，且不可含控制字元或 < > { }。";
        }
        if (note != null && (note.Length > 200 || note.Any(char.IsControl)))
        {
            return "note 最多 200 字，且不可含控制字元。";
        }
        var simplified = SimplifiedChars.FindIn(target);
        if (simplified.Count > 0)
        {
            return "target 含簡體字：" + string.Join("", simplified.Take(5));
        }
        if (kind == "s2t")
        {
            // 至少 2 字，避免單字規則大範圍改寫所有文字。
            if (source.Length < 2 || source.Length > 16 || !source.All(IsCjk) || !target.All(IsCjk))
            {
                return "s2t 的 source 與 target 必須是 2–16 個中文字。";
            }
        }
        else if (!EnglishTerm.IsMatch(source))
        {
            return "en 的 source 只能是英數、空白、' - .，最多 64 字。";
        }
        return null;
    }

    public GlossaryEntry Add(string kind, string source, string target, string? note, string origin)
    {
        source = source.Trim();
        target = target.Trim();
        var problem = Check(kind, source, target, note);
        if (problem != null)
        {
            throw new ArgumentException(problem);
        }
        var existing = Entries.FindIndex(e => e.Kind == kind && string.Equals(e.Source, source, kind == "en" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        if (existing < 0 && Entries.Count >= MaxEntries)
        {
            throw new ArgumentException("術語表已達上限 " + MaxEntries + " 筆。");
        }
        if (existing >= 0 && origin == "agent" && Entries[existing].Origin == "user")
        {
            throw new ArgumentException("此詞由使用者設定，Agent 不能覆蓋：" + source + " → " + Entries[existing].Target);
        }
        var entry = new GlossaryEntry { Kind = kind, Source = source, Target = target, Note = note, Origin = origin, UpdatedUtc = DateTime.UtcNow };
        if (existing >= 0)
        {
            Entries[existing] = entry;
        }
        else
        {
            Entries.Add(entry);
        }
        TranslationStore.WriteAtomic(_path, JsonSerializer.Serialize(Entries, AppConfig.Json));
        return entry;
    }

    public bool Remove(string kind, string source)
    {
        var n = Entries.RemoveAll(e => e.Kind == kind && string.Equals(e.Source, source, kind == "en" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        if (n > 0)
        {
            TranslationStore.WriteAtomic(_path, JsonSerializer.Serialize(Entries, AppConfig.Json));
        }
        return n > 0;
    }

    private static bool IsCjk(char c) => c >= 0x3400 && c <= 0x9FFF;
}
