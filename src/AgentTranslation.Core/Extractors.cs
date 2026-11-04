using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace AgentTranslation.Core
{
    /// <summary>
    /// 從（已解析繼承的）Def 節點抽出可翻譯欄位，路徑規則對照 RimWorld 1.7.4998：
    /// - DefInjectionUtility：路徑 = defName.欄位[.索引|.欄位]…；欄位帶 [NoTranslate]／[Unsaved] 時整個子樹不可翻譯；
    ///   Def 參照不遞迴。
    /// - DefInjectionUtility.ShouldCheckMissingInjection：[MustTranslate] 必翻；[MayTranslate] 不列入；
    ///   其餘字串「含空白」才列入。本抽取器沿用同一規則，避免輸出遊戲不認為需要翻譯的欄位。
    /// - DefInjectionPackage.SetDefFieldAtPath：清單可用數字索引定位，因此一律輸出索引路徑（不使用 handle）。
    /// </summary>
    public sealed class DefExtractor
    {
        private readonly ITypeSchema _schema;

        public DefExtractor(ITypeSchema schema)
        {
            _schema = schema;
        }

        public void Extract(string packageId, DefNode node, XElement resolved, ExtractionResult result)
        {
            if (node.IsAbstract)
            {
                return;
            }
            var defTypeName = node.Element.Name.LocalName;
            var defName = resolved.Element("defName")?.Value.Trim();
            if (string.IsNullOrEmpty(defName))
            {
                return;
            }
            if (!IsValidXmlNameStart(defName!))
            {
                result.Notes.Add(new ScanNote("defname_not_xml_name", "defName " + defName + " 不能當 XML 元素名稱開頭，無法以 DefInjected 翻譯。", node.File));
                return;
            }
            var defType = _schema.ResolveXmlTypeName(defTypeName);
            if (defType == null || !defType.IsDef)
            {
                result.Notes.Add(new ScanNote("unknown_def_type", "無法辨識 Def 類型 " + defTypeName + "。", node.File));
                return;
            }

            // 若 Def 節點本身有 Class 屬性，欄位依該型別解析；DefInjected 資料夾仍用根元素名稱（DefDatabase 所屬類型）。
            var concreteType = defType;
            var cls = resolved.Attribute("Class")?.Value;
            if (!string.IsNullOrEmpty(cls))
            {
                var t = _schema.ResolveXmlTypeName(cls!);
                if (t == null)
                {
                    result.Notes.Add(new ScanNote("unknown_class", "無法辨識 Class=" + cls + "，略過 " + defName + "。", node.File));
                    return;
                }
                concreteType = t;
            }

            var label = resolved.Element("label")?.Value;
            var description = resolved.Element("description")?.Value;
            var before = result.Entries.Count;
            Walk(packageId, defTypeName, node.File, concreteType, resolved, defName!, true, result);
            for (int i = before; i < result.Entries.Count; i++)
            {
                result.Entries[i].DefLabel = label;
                result.Entries[i].DefDescription = description;
            }
        }

        private void Walk(string packageId, string defTypeName, string file, TypeShape type, XElement element, string path, bool allowed, ExtractionResult result)
        {
            foreach (var child in element.Elements())
            {
                var name = child.Name.LocalName;
                if (name == "li")
                {
                    continue;
                }
                var field = _schema.FindField(type, name);
                if (field == null)
                {
                    continue;
                }
                var fieldAllowed = allowed && !field.NoTranslate && !field.Unsaved;
                var fieldPath = path + "." + field.Name;
                switch (field.Kind)
                {
                    case FieldKind.String:
                        if (fieldAllowed && !child.HasElements)
                        {
                            AddIfTranslatable(packageId, defTypeName, file, field, fieldPath, child.Value, result);
                        }
                        break;
                    case FieldKind.StringList:
                        if (fieldAllowed)
                        {
                            int i = 0;
                            foreach (var li in child.Elements("li"))
                            {
                                if (!li.HasElements)
                                {
                                    AddIfTranslatable(packageId, defTypeName, file, field, fieldPath + "." + i, li.Value, result);
                                }
                                i++;
                            }
                        }
                        break;
                    case FieldKind.ObjectList:
                        if (!fieldAllowed)
                        {
                            break;
                        }
                        {
                            int i = 0;
                            foreach (var li in child.Elements("li"))
                            {
                                var elementType = ResolveElementType(li, field.ValueTypeFullName, file, result);
                                if (elementType != null && !elementType.IsDef)
                                {
                                    Walk(packageId, defTypeName, file, elementType, li, fieldPath + "." + i, fieldAllowed, result);
                                }
                                i++;
                            }
                        }
                        break;
                    case FieldKind.Object:
                        if (fieldAllowed && child.HasElements)
                        {
                            var objType = ResolveElementType(child, field.ValueTypeFullName, file, result);
                            if (objType != null && !objType.IsDef)
                            {
                                Walk(packageId, defTypeName, file, objType, child, fieldPath, fieldAllowed, result);
                            }
                        }
                        break;
                }
            }
        }

        private TypeShape? ResolveElementType(XElement node, string? declared, string file, ExtractionResult result)
        {
            var cls = node.Attribute("Class")?.Value;
            if (!string.IsNullOrEmpty(cls))
            {
                var t = _schema.ResolveXmlTypeName(cls!);
                if (t == null)
                {
                    result.Notes.Add(new ScanNote("unknown_class", "無法辨識 Class=" + cls + "，略過其內容。", file));
                }
                return t;
            }
            return declared == null ? null : _schema.GetByFullName(declared);
        }

        private static void AddIfTranslatable(string packageId, string defTypeName, string file, FieldShape field, string path, string value, ExtractionResult result)
        {
            if (string.IsNullOrEmpty(value) || field.MayTranslate || field.SlateRef)
            {
                return;
            }
            if (!field.MustTranslate && value.IndexOf(' ') < 0)
            {
                return;
            }
            result.Entries.Add(new SourceEntry(packageId, EntryKind.Def, defTypeName, path, value, file));
        }

        private static bool IsValidXmlNameStart(string name)
        {
            try
            {
                XmlConvert.VerifyName(name);
                return true;
            }
            catch (XmlException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Keyed 抽取。對照 RimWorld 1.7.4998 DirectXmlLoaderSimple：根元素下每個子元素為一筆，
    /// 鍵 = 元素名稱、值 = 元素文字；值為 "TODO" 視為占位。同檔重複鍵遊戲會報錯並保留第一筆。
    /// </summary>
    public static class KeyedExtractor
    {
        public static void Extract(string packageId, string relativeFile, XDocument doc, ExtractionResult result, HashSet<string> seenKeys)
        {
            if (doc.Root == null)
            {
                return;
            }
            var inFile = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in doc.Root.Elements())
            {
                var key = element.Name.LocalName;
                var value = element.Value;
                if (!inFile.Add(key))
                {
                    result.Notes.Add(new ScanNote("duplicate_key", "同檔重複鍵 " + key + "。", relativeFile));
                    continue;
                }
                if (!seenKeys.Add(key))
                {
                    // 遊戲中後讀到的檔案覆蓋前者；此處保留第一個（同模組內不同檔重複，翻譯包只需一筆）。
                    continue;
                }
                if (value.Length == 0 || value == "TODO")
                {
                    continue;
                }
                result.Entries.Add(new SourceEntry(packageId, EntryKind.Keyed, null, key, value, relativeFile));
            }
        }
    }
}
