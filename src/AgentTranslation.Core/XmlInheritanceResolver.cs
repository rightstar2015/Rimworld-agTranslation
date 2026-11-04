using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace AgentTranslation.Core
{
    /// <summary>一個 Defs 節點與它的來源。</summary>
    public sealed class DefNode
    {
        public DefNode(XElement element, int loadOrder, string packageId, string file)
        {
            Element = element;
            LoadOrder = loadOrder;
            PackageId = packageId;
            File = file;
        }

        public XElement Element { get; }
        public int LoadOrder { get; }
        public string PackageId { get; }
        public string File { get; }
        public string? Name => Element.Attribute("Name")?.Value;
        public string? ParentName => Element.Attribute("ParentName")?.Value;
        public bool IsAbstract => string.Equals(Element.Attribute("Abstract")?.Value, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 提供以 Name 屬性尋找父節點的來源。實作端可延遲載入其他模組的 Defs。
    /// </summary>
    public interface INamedNodeSource
    {
        /// <summary>回傳所有 Name 屬性等於 name 的節點（任何模組）。</summary>
        IEnumerable<DefNode> FindNamed(string name);
    }

    /// <summary>
    /// Defs XML 繼承解析。依 RimWorld 1.7.4998 XmlInheritance 的觀察行為自行實作：
    /// - 父節點選擇：載入順序 ≤ 自身模組者中，載入順序最大的那個。
    /// - 合併：子節點屬性覆蓋；子節點有文字則取代內容；子節點無內容時若父有子元素則保留；
    ///   li 一律附加；同名元素遞迴合併；Inherit="False" 時以子節點內容整個取代。
    /// 尚未支援：Patches 對 XML 的修改（Patch 在繼承前套用，可能改變文字或清單索引）。
    /// </summary>
    public sealed class XmlInheritanceResolver
    {
        private readonly INamedNodeSource _source;
        private readonly Dictionary<DefNode, XElement> _cache = new Dictionary<DefNode, XElement>();

        public XmlInheritanceResolver(INamedNodeSource source)
        {
            _source = source;
        }

        public List<string> Warnings { get; } = new List<string>();

        public XElement Resolve(DefNode node)
        {
            return Resolve(node, new HashSet<DefNode>());
        }

        private XElement Resolve(DefNode node, HashSet<DefNode> visiting)
        {
            if (_cache.TryGetValue(node, out var cached))
            {
                return cached;
            }
            XElement result;
            var parentName = node.ParentName;
            if (string.IsNullOrEmpty(parentName))
            {
                result = new XElement(node.Element);
            }
            else
            {
                var parent = FindParent(node, parentName!);
                if (parent == null || !visiting.Add(node))
                {
                    if (parent == null)
                    {
                        Warnings.Add("找不到父節點 " + parentName + "（" + node.File + "）");
                    }
                    result = new XElement(node.Element);
                }
                else
                {
                    var merged = new XElement(Resolve(parent, visiting));
                    Overwrite(node.Element, merged);
                    result = merged;
                    visiting.Remove(node);
                }
            }
            _cache[node] = result;
            return result;
        }

        private DefNode? FindParent(DefNode node, string parentName)
        {
            DefNode? best = null;
            foreach (var candidate in _source.FindNamed(parentName))
            {
                if (ReferenceEquals(candidate.Element, node.Element))
                {
                    continue;
                }
                if (candidate.LoadOrder <= node.LoadOrder && (best == null || candidate.LoadOrder > best.LoadOrder))
                {
                    best = candidate;
                }
            }
            return best;
        }

        private static void Overwrite(XElement child, XElement current)
        {
            var inherit = child.Attribute("Inherit");
            if (inherit != null && string.Equals(inherit.Value, "false", StringComparison.OrdinalIgnoreCase))
            {
                current.RemoveNodes();
                foreach (var n in child.Nodes())
                {
                    current.Add(Clone(n));
                }
                foreach (var a in child.Attributes())
                {
                    if (a.Name.LocalName != "Inherit")
                    {
                        current.SetAttributeValue(a.Name, a.Value);
                    }
                }
                return;
            }

            current.RemoveAttributes();
            foreach (var a in child.Attributes())
            {
                current.SetAttributeValue(a.Name, a.Value);
            }

            var text = child.Nodes().OfType<XText>().LastOrDefault(t => t is XCData || t.Value.Trim().Length > 0);
            bool hasElements = child.Elements().Any();
            if (text != null)
            {
                current.RemoveNodes();
                current.Add(new XText(text.Value));
                return;
            }
            if (!hasElements)
            {
                if (current.Elements().Any())
                {
                    return;
                }
                current.RemoveNodes();
                return;
            }
            foreach (var element in child.Elements())
            {
                if (element.Name.LocalName == "li")
                {
                    current.Add(new XElement(element));
                    continue;
                }
                var existing = current.Element(element.Name);
                if (existing != null)
                {
                    Overwrite(element, existing);
                }
                else
                {
                    current.Add(new XElement(element));
                }
            }
        }

        private static XNode Clone(XNode node)
        {
            switch (node)
            {
                case XElement e: return new XElement(e);
                case XCData c: return new XCData(c.Value);
                case XText t: return new XText(t.Value);
                default: return new XText(string.Empty);
            }
        }
    }
}
