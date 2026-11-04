using System.Collections.Generic;
namespace AgentTranslation.Core
{
    public enum FieldKind
    {
        /// <summary>A string field.</summary>
        String,
        /// <summary>A string collection such as List&lt;string&gt;.</summary>
        StringList,
        /// <summary>A list of custom objects (each li child is one object).</summary>
        ObjectList,
        /// <summary>A single custom object.</summary>
        Object,
        /// <summary>Def references or other non-recursive types (numbers, enums, Type, dictionaries...).</summary>
        Other,
    }

    public sealed class FieldShape
    {
        public string Name { get; set; } = string.Empty;
        public FieldKind Kind { get; set; }
        /// <summary>Object: the field type; ObjectList: the element type. Full name (Namespace.Type).</summary>
        public string? ValueTypeFullName { get; set; }
        public bool NoTranslate { get; set; }
        public bool Unsaved { get; set; }
        public bool MustTranslate { get; set; }
        public bool MayTranslate { get; set; }
        /// <summary>A quest SlateRef field; not handled yet.</summary>
        public bool SlateRef { get; set; }
        public bool TranslationCanChangeCount { get; set; }
    }

    public sealed class TypeShape
    {
        public string FullName { get; set; } = string.Empty;
        public bool IsDef { get; set; }
        public bool IsAbstract { get; set; }
    }

    /// <summary>
    /// Type information (fields, attributes) from game and mod assemblies. The server implements it by reading assembly
    /// metadata with System.Reflection.Metadata, without loading or executing any assembly code.
    /// </summary>
    public interface ITypeSchema
    {
        /// <summary>Resolves a type from its XML name (Defs root element name or Class attribute), same rules as GenTypes.GetTypeInAnyAssembly.</summary>
        TypeShape? ResolveXmlTypeName(string name);

        TypeShape? GetByFullName(string fullName);

        /// <summary>Finds a field: case-insensitive, includes base types, supports LoadAlias.</summary>
        FieldShape? FindField(TypeShape type, string xmlName);

        /// <summary>The type and its base types up to, but not including, Verse.Def (most derived first).</summary>
        IReadOnlyList<TypeShape> DefChain(TypeShape type);

        /// <summary>The shortest name that resolves back to this type (short name when it is in a fallback namespace, otherwise the full name).</summary>
        string XmlNameOf(TypeShape type);
    }
}
