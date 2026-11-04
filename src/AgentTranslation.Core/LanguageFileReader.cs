using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace AgentTranslation.Core
{
    /// <summary>從某語言資料夾（或官方 .tar）讀出的一筆翻譯。</summary>
    public sealed class LanguageRecord
    {
        public LanguageRecord(EntryKind kind, string? defType, string path, string text, string? englishComment, string file)
        {
            Kind = kind;
            DefType = defType;
            Path = path;
            Text = text;
            EnglishComment = englishComment;
            File = file;
        }

        public EntryKind Kind { get; }
        /// <summary>DefInjected 資料夾名稱（可能是舊式複數，例如 ThingDefs）。</summary>
        public string? DefType { get; }
        public string Path { get; }
        public string Text { get; }
        /// <summary>緊接在元素前、以 "EN:" 開頭的註解內容（遊戲「產生翻譯檔」與多數翻譯工具會寫入）。</summary>
        public string? EnglishComment { get; }
        public string File { get; }

        /// <summary>與 SourceEntry.CanonicalPath 相同的鍵。</summary>
        public string CanonicalPath => Kind == EntryKind.Def ? DefType + ":" + Path : Path;

        /// <summary>DefType 去掉結尾 s 的版本（遊戲找不到型別時也會這樣嘗試）。</summary>
        public string? CanonicalPathSingular =>
            Kind == EntryKind.Def && DefType != null && DefType.Length > 3 && DefType.EndsWith("s", StringComparison.Ordinal)
                ? DefType.Substring(0, DefType.Length - 1) + ":" + Path
                : null;
    }

    /// <summary>
    /// 讀取一個語言資料夾的 Keyed 與 DefInjected，保留 EN 註解。
    /// 只處理字串值；整串清單（&lt;li&gt;）翻譯略過。
    /// </summary>
    public static class LanguageFileReader
    {
        /// <summary>files：相對於語言資料夾的路徑（'/' 分隔）與開啟串流的函式。</summary>
        public static IEnumerable<LanguageRecord> Read(IEnumerable<(string RelativePath, Func<Stream> Open)> files)
        {
            foreach (var (rel, open) in files)
            {
                var parts = rel.Replace('\\', '/').Split('/');
                if (parts.Length < 2 || !rel.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                EntryKind kind;
                string? defType = null;
                if (parts[0] == "Keyed")
                {
                    kind = EntryKind.Keyed;
                }
                else if (parts[0] == "DefInjected" && parts.Length >= 3)
                {
                    kind = EntryKind.Def;
                    defType = parts[1];
                }
                else
                {
                    continue;
                }

                XDocument doc;
                try
                {
                    using (var s = open())
                    {
                        doc = SafeXml.Load(s, keepComments: true);
                    }
                }
                catch (Exception)
                {
                    continue;
                }
                if (doc.Root == null)
                {
                    continue;
                }
                string? pendingComment = null;
                foreach (var node in doc.Root.Nodes())
                {
                    if (node is XComment c)
                    {
                        var v = c.Value.Trim();
                        pendingComment = v.StartsWith("EN:", StringComparison.Ordinal) ? v.Substring(3).Trim() : null;
                        continue;
                    }
                    if (!(node is XElement e))
                    {
                        continue;
                    }
                    var comment = pendingComment;
                    pendingComment = null;
                    if (e.HasElements)
                    {
                        continue;
                    }
                    var value = e.Value;
                    if (value.Length == 0 || value == "TODO")
                    {
                        continue;
                    }
                    var path = e.Name.LocalName;
                    if (kind == EntryKind.Def)
                    {
                        path = path.Replace("]", string.Empty).Replace('[', '.');
                    }
                    yield return new LanguageRecord(kind, defType, path, value, comment, rel);
                }
            }
        }

        public static IEnumerable<(string, Func<Stream>)> FilesInDirectory(string languageDir)
        {
            if (!Directory.Exists(languageDir))
            {
                yield break;
            }
            foreach (var file in Directory.GetFiles(languageDir, "*.xml", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                var rel = file.Substring(languageDir.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/');
                var captured = file;
                yield return (rel, () => File.OpenRead(captured));
            }
        }

        /// <summary>比較英文時的正規化：字面 \n 與換行視為相同、空白合併、去頭尾。</summary>
        public static string NormalizeEnglish(string s)
        {
            var t = (s ?? string.Empty).Replace("\\n", "\n").Replace("\r", string.Empty);
            var parts = t.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }
    }
}
