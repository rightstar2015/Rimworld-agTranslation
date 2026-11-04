using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace AgentTranslation.Core
{
    /// <summary>
    /// Extracts translatable fields from a Def node (inheritance already resolved). Path rules follow RimWorld 1.7.4998:
    /// - DefInjectionUtility: path = defName.field[.index|.field]...; a field with [NoTranslate]/[Unsaved] makes its whole subtree untranslatable;
    ///   Def references are not followed.
    /// - DefInjectionUtility.ShouldCheckMissingInjection: [MustTranslate] is required; [MayTranslate] is not counted;
    ///   other strings count only if they contain a space. Same rule here, so we never emit fields the game does not consider missing.
    /// - DefInjectionPackage.SetDefFieldAtPath: list items can be addressed by numeric index, so index paths are always emitted (no handles).
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
                result.Notes.Add(new ScanNote("defname_not_xml_name", "defName " + defName + " cannot start an XML element name, so it cannot be translated with DefInjected.", node.File));
                return;
            }
            var defType = _schema.ResolveXmlTypeName(defTypeName);
            if (defType == null || !defType.IsDef)
            {
                result.Notes.Add(new ScanNote("unknown_def_type", "Unknown Def type " + defTypeName + ".", node.File));
                return;
            }

            // DefInjected folder: the game adds a Def to the database of every class in its chain, and the standard folder is
            // the topmost non-abstract class below Def (GenDefDatabase.AllDefTypesWithDatabases), e.g. HAR's
            // AlienRace.AlienBackstoryDef -> BackstoryDef. Translations under any class name of the chain take effect.
            var chain = _schema.DefChain(defType);
            var databaseType = chain.LastOrDefault(t => !t.IsAbstract) ?? defType;
            var folderName = _schema.XmlNameOf(databaseType);
            var aliases = chain
                .SelectMany(t => new[] { _schema.XmlNameOf(t), t.FullName, t.FullName.Substring(t.FullName.LastIndexOf('.') + 1) })
                .Append(defTypeName)
                .Where(n => !string.Equals(n, folderName, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            defTypeName = folderName;

            // If the Def node has a Class attribute, fields are resolved on that type; the folder stays the database type.
            var concreteType = defType;
            var cls = resolved.Attribute("Class")?.Value;
            if (!string.IsNullOrEmpty(cls))
            {
                var t = _schema.ResolveXmlTypeName(cls!);
                if (t == null)
                {
                    result.Notes.Add(new ScanNote("unknown_class", "Unknown Class=" + cls + ", skipped " + defName + ".", node.File));
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
                result.Entries[i].DefTypeAliases = aliases;
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
                    result.Notes.Add(new ScanNote("unknown_class", "Unknown Class=" + cls + ", its content was skipped.", file));
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
    /// Keyed extraction, following RimWorld 1.7.4998 DirectXmlLoaderSimple: each child of the root element is one entry,
    /// key = element name, value = element text; "TODO" is a placeholder. The game reports duplicate keys within one file and keeps the first.
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
                    result.Notes.Add(new ScanNote("duplicate_key", "Duplicate key " + key + " in the same file.", relativeFile));
                    continue;
                }
                if (!seenKeys.Add(key))
                {
                    // In game a later file overrides an earlier one; here the first is kept (duplicates across files of one mod need only one pack entry).
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
