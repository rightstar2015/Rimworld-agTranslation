using System;
using System.Collections.Generic;

namespace AgentTranslation.Core
{
    /// <summary>條目類型。數值與名稱會進入穩定 ID 的雜湊輸入，發布後不可更改。</summary>
    public enum EntryKind
    {
        /// <summary>Languages/English/Keyed 的鍵值。</summary>
        Keyed,
        /// <summary>由 Defs XML 抽出、以 DefInjected 格式輸出的欄位。</summary>
        Def,
    }

    public static class EntryKindNames
    {
        public static string ToId(EntryKind kind)
        {
            switch (kind)
            {
                case EntryKind.Keyed: return "keyed";
                case EntryKind.Def: return "def";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static bool TryParse(string? value, out EntryKind kind)
        {
            switch (value)
            {
                case "keyed": kind = EntryKind.Keyed; return true;
                case "def": kind = EntryKind.Def; return true;
                default: kind = default; return false;
            }
        }
    }

    /// <summary>一筆待翻譯的來源條目。由抽取器產生，內容是不可信的模組文字。</summary>
    public sealed class SourceEntry
    {
        public SourceEntry(string packageId, EntryKind kind, string? defType, string injectionPath, string source, string sourceFile)
        {
            PackageId = PackageIds.Normalize(packageId);
            Kind = kind;
            DefType = defType;
            InjectionPath = injectionPath;
            Source = source;
            SourceFile = sourceFile;
            CanonicalPath = kind == EntryKind.Def ? defType + ":" + injectionPath : injectionPath;
            Key = StableId.Key(PackageId, kind, CanonicalPath);
            Id = StableId.Id(PackageId, kind, CanonicalPath, source);
        }

        /// <summary>小寫的 packageId。</summary>
        public string PackageId { get; }
        public EntryKind Kind { get; }
        /// <summary>Def 類型名稱（例如 ThingDef），Keyed 時為 null。</summary>
        public string? DefType { get; }
        /// <summary>Keyed：鍵名；Def：defName.field.0.field 形式的 DefInjected 路徑。</summary>
        public string InjectionPath { get; }
        /// <summary>參與雜湊的路徑：Keyed 為鍵名；Def 為 "DefType:路徑"。</summary>
        public string CanonicalPath { get; }
        /// <summary>原文（XML 原樣，字面 \n 保留為兩個字元）。</summary>
        public string Source { get; }
        /// <summary>來源檔，相對於模組根目錄，使用 '/' 分隔。</summary>
        public string SourceFile { get; }
        public string Key { get; }
        public string Id { get; }

        /// <summary>所屬 Def 的 label（上下文用，可能為 null）。</summary>
        public string? DefLabel { get; set; }
        /// <summary>所屬 Def 的 description（上下文用，可能為 null）。</summary>
        public string? DefDescription { get; set; }
    }

    /// <summary>抽取時遇到、但不影響其他條目的問題。</summary>
    public sealed class ScanNote
    {
        public ScanNote(string code, string detail, string? file = null)
        {
            Code = code;
            Detail = detail;
            File = file;
        }

        public string Code { get; }
        public string Detail { get; }
        public string? File { get; }
    }

    public sealed class ExtractionResult
    {
        public List<SourceEntry> Entries { get; } = new List<SourceEntry>();
        public List<ScanNote> Notes { get; } = new List<ScanNote>();
    }

    public static class PackageIds
    {
        /// <summary>RimWorld 比對 packageId 時不分大小寫；另外去掉同名衝突時加上的 _steam／_copy 後綴。</summary>
        public static string Normalize(string packageId)
        {
            var id = (packageId ?? string.Empty).Trim().ToLowerInvariant();
            return id;
        }

        public static string WithoutPostfix(string packageId)
        {
            var id = Normalize(packageId);
            foreach (var postfix in new[] { "_steam", "_copy" })
            {
                if (id.EndsWith(postfix, StringComparison.Ordinal))
                {
                    return id.Substring(0, id.Length - postfix.Length);
                }
            }
            return id;
        }
    }
}
