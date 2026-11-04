using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace AgentTranslation.Core
{
    /// <summary>
    /// 某語言已存在的翻譯鍵（Keyed 鍵名、DefInjected 的 DefType:路徑）。
    /// 用來判定「已有翻譯，不要輸出」。DefInjected 跨模組重複鍵在 1.7 會產生
    /// "Duplicate def-injected translation key" 錯誤，所以已存在的鍵一律不輸出。
    /// </summary>
    public sealed class ExistingTranslationIndex
    {
        public HashSet<string> Keyed { get; } = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>"DefType:defName.field…"，DefType 為資料夾名稱（含去掉結尾 s 的版本）。</summary>
        public HashSet<string> DefInjected { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>"DefType:defName"，用於偵測「該 Def 有任何翻譯」（handle 路徑無法精確比對時的保守判斷）。</summary>
        public HashSet<string> DefsTouched { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<string> Sources { get; } = new List<string>();

        public bool Contains(SourceEntry entry)
        {
            if (entry.Kind == EntryKind.Keyed)
            {
                return Keyed.Contains(entry.InjectionPath);
            }
            return DefInjected.Contains(entry.DefType + ":" + entry.InjectionPath);
        }

        /// <summary>同一個 Def 有其他翻譯（可能用 handle 路徑），無法確定是否重複。</summary>
        public bool TouchesDef(SourceEntry entry)
        {
            if (entry.Kind != EntryKind.Def)
            {
                return false;
            }
            var defName = entry.InjectionPath.Split('.')[0];
            return DefsTouched.Contains(entry.DefType + ":" + defName);
        }

        /// <summary>讀取一個語言資料夾（例如 …/Languages/ChineseTraditional）。</summary>
        public void AddLanguageFolder(string languageDir)
        {
            if (!Directory.Exists(languageDir))
            {
                return;
            }
            Sources.Add(languageDir);
            var keyed = Path.Combine(languageDir, "Keyed");
            if (Directory.Exists(keyed))
            {
                foreach (var file in Directory.GetFiles(keyed, "*.xml", SearchOption.AllDirectories))
                {
                    var doc = TryLoad(file);
                    if (doc?.Root == null)
                    {
                        continue;
                    }
                    foreach (var e in doc.Root.Elements())
                    {
                        if (!IsPlaceholder(e.Value))
                        {
                            Keyed.Add(e.Name.LocalName);
                        }
                    }
                }
            }
            var defInjected = Path.Combine(languageDir, "DefInjected");
            if (Directory.Exists(defInjected))
            {
                foreach (var typeDir in Directory.GetDirectories(defInjected))
                {
                    var typeName = Path.GetFileName(typeDir);
                    var names = new List<string> { typeName };
                    if (typeName.Length > 3 && typeName.EndsWith("s", StringComparison.Ordinal))
                    {
                        names.Add(typeName.Substring(0, typeName.Length - 1));
                    }
                    foreach (var file in Directory.GetFiles(typeDir, "*.xml", SearchOption.AllDirectories))
                    {
                        var doc = TryLoad(file);
                        if (doc?.Root == null)
                        {
                            continue;
                        }
                        foreach (var e in doc.Root.Elements())
                        {
                            string path;
                            if (e.Name.LocalName == "rep")
                            {
                                path = e.Element("path")?.Value ?? string.Empty;
                            }
                            else
                            {
                                path = e.Name.LocalName;
                            }
                            path = path.Replace("]", string.Empty).Replace('[', '.');
                            if (path.Length == 0 || (!e.HasElements && IsPlaceholder(e.Value)))
                            {
                                continue;
                            }
                            foreach (var n in names)
                            {
                                DefInjected.Add(n + ":" + path);
                                DefsTouched.Add(n + ":" + path.Split('.')[0]);
                            }
                        }
                    }
                }
            }
        }

        private static bool IsPlaceholder(string value) => value == "TODO";

        private static XDocument? TryLoad(string file)
        {
            try
            {
                return SafeXml.LoadFile(file);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>寫出 RimWorld LanguageData XML（Keyed 或 DefInjected 格式相同：根元素 LanguageData，子元素名 = 鍵／路徑）。</summary>
    public static class LanguageDataWriter
    {
        public sealed class Item
        {
            public Item(string key, string value, string? englishComment)
            {
                Key = key;
                Value = value;
                EnglishComment = englishComment;
            }

            public string Key { get; }
            public string Value { get; }
            public string? EnglishComment { get; }
        }

        public static string Render(IEnumerable<Item> items)
        {
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                OmitXmlDeclaration = false,
                CheckCharacters = true,
            };
            var sb = new StringBuilder();
            using (var sw = new Utf8StringWriter(sb))
            using (var w = XmlWriter.Create(sw, settings))
            {
                w.WriteStartDocument();
                w.WriteStartElement("LanguageData");
                foreach (var item in items)
                {
                    XmlConvert.VerifyName(item.Key);
                    if (item.EnglishComment != null)
                    {
                        w.WriteComment(" EN: " + SafeComment(item.EnglishComment) + " ");
                    }
                    w.WriteElementString(item.Key, item.Value);
                }
                w.WriteEndElement();
                w.WriteEndDocument();
            }
            return sb.ToString() + "\n";
        }

        /// <summary>XML 註解不可含 "--"，也不可以 "-" 結尾。</summary>
        public static string SafeComment(string text)
        {
            var s = text.Replace("\r", " ").Replace("\n", "\\n");
            while (s.Contains("--"))
            {
                s = s.Replace("--", "- -");
            }
            if (s.EndsWith("-", StringComparison.Ordinal))
            {
                s += " ";
            }
            return s;
        }

        private sealed class Utf8StringWriter : StringWriter
        {
            public Utf8StringWriter(StringBuilder sb) : base(sb) { }
            public override Encoding Encoding => new UTF8Encoding(false);
        }
    }
}
