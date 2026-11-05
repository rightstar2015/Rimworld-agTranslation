using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using AgentTranslation.Core;

namespace AgentTranslation.Server;

/// <summary>
/// Read-only type metadata of game and mod assemblies via System.Reflection.Metadata.
/// Never loads or executes assembly code. Type name resolution follows RimWorld 1.7.4998 GenTypes.GetTypeInAnyAssembly:
/// full name first, then the RimWorld, Verse, LudeonTK, Verse.AI ... namespaces in order.
/// </summary>
public sealed class MetadataTypeSchema : ITypeSchema, IDisposable
{
    private static readonly string[] FallbackNamespaces =
    {
        "RimWorld", "Verse", "LudeonTK", "Verse.AI", "Verse.AI.Group", "Verse.Sound", "Verse.Grammar",
        "RimWorld.Planet", "RimWorld.BaseGen", "RimWorld.QuestGen", "RimWorld.SketchGen", "System",
    };

    private readonly List<PEReader> _pe = new();
    private readonly Dictionary<string, (MetadataReader Reader, TypeDefinitionHandle Handle)> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TypeShape?> _shapeCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, FieldShape>> _fieldCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadedFiles = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Warnings { get; } = new();

    public void AddAssembly(string path)
    {
        if (!_loadedFiles.Add(Path.GetFullPath(path)))
        {
            return;
        }
        try
        {
            // The reader keeps the file open for the life of the server. Share delete/write so Steam updates and
            // install-bridge can still replace the file (Windows keeps the old content alive for this handle).
            var pe = new PEReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            if (!pe.HasMetadata)
            {
                pe.Dispose();
                return;
            }
            var reader = pe.GetMetadataReader();
            _pe.Add(pe);
            foreach (var h in reader.TypeDefinitions)
            {
                var name = FullNameOf(reader, h);
                if (name.Length > 0)
                {
                    _types.TryAdd(name, (reader, h));
                }
            }
            _shapeCache.Clear();
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or InvalidOperationException)
        {
            Warnings.Add("Skipped an assembly that could not be read: " + Path.GetFileName(path) + ": " + ex.Message);
        }
    }

    public TypeShape? ResolveXmlTypeName(string name)
    {
        name = name.Trim();
        var t = GetByFullName(name);
        if (t != null)
        {
            return t;
        }
        foreach (var ns in FallbackNamespaces)
        {
            t = GetByFullName(ns + "." + name);
            if (t != null)
            {
                return t;
            }
        }
        return null;
    }

    public TypeShape? GetByFullName(string fullName)
    {
        if (_shapeCache.TryGetValue(fullName, out var cached))
        {
            return cached;
        }
        TypeShape? shape = null;
        if (_types.ContainsKey(fullName))
        {
            var (reader, handle) = _types[fullName];
            var isAbstract = (reader.GetTypeDefinition(handle).Attributes & System.Reflection.TypeAttributes.Abstract) != 0;
            shape = new TypeShape { FullName = fullName, IsDef = InheritsFrom(fullName, "Verse.Def"), IsAbstract = isAbstract };
        }
        _shapeCache[fullName] = shape;
        return shape;
    }

    public FieldShape? FindField(TypeShape type, string xmlName)
    {
        var fields = FieldsOf(type.FullName);
        return fields.TryGetValue(xmlName, out var f) ? f : null;
    }

    public IReadOnlyList<TypeShape> DefChain(TypeShape type)
    {
        var list = new List<TypeShape>();
        string? current = type.FullName;
        for (int guard = 0; guard < 64 && current != null && current != "Verse.Def"; guard++)
        {
            var shape = GetByFullName(current);
            if (shape == null)
            {
                break;
            }
            list.Add(shape);
            current = BaseTypeName(current);
        }
        return list;
    }

    public string XmlNameOf(TypeShape type)
    {
        var shortName = type.FullName.Substring(type.FullName.LastIndexOf('.') + 1);
        return ResolveXmlTypeName(shortName)?.FullName == type.FullName ? shortName : type.FullName;
    }
    private bool InheritsFrom(string fullName, string baseName)
    {
        var current = fullName;
        for (int guard = 0; guard < 64 && current != null; guard++)
        {
            if (current == baseName)
            {
                return true;
            }
            current = BaseTypeName(current);
        }
        return false;
    }

    private string? BaseTypeName(string fullName)
    {
        if (!_types.TryGetValue(fullName, out var t))
        {
            return null;
        }
        var def = t.Reader.GetTypeDefinition(t.Handle);
        if (def.BaseType.IsNil)
        {
            return null;
        }
        return TypeNameOfHandle(t.Reader, def.BaseType);
    }

    /// <summary>Field table keyed by case-insensitive field name or LoadAlias; fields of derived types win.</summary>
    private Dictionary<string, FieldShape> FieldsOf(string fullName)
    {
        if (_fieldCache.TryGetValue(fullName, out var cached))
        {
            return cached;
        }
        var result = new Dictionary<string, FieldShape>(StringComparer.OrdinalIgnoreCase);
        var aliases = new Dictionary<string, FieldShape>(StringComparer.Ordinal);
        var current = fullName;
        for (int guard = 0; guard < 64 && current != null && _types.TryGetValue(current, out var t); guard++)
        {
            var reader = t.Reader;
            var def = reader.GetTypeDefinition(t.Handle);
            foreach (var fh in def.GetFields())
            {
                var field = reader.GetFieldDefinition(fh);
                if ((field.Attributes & System.Reflection.FieldAttributes.Static) != 0)
                {
                    continue;
                }
                var name = reader.GetString(field.Name);
                var shape = new FieldShape { Name = name };
                string? alias = null;
                foreach (var ah in field.GetCustomAttributes())
                {
                    var attr = reader.GetCustomAttribute(ah);
                    var attrName = AttributeTypeName(reader, attr);
                    switch (attrName)
                    {
                        case "Verse.NoTranslateAttribute": shape.NoTranslate = true; break;
                        case "Verse.UnsavedAttribute": shape.Unsaved = true; break;
                        case "Verse.MustTranslateAttribute": shape.MustTranslate = true; break;
                        case "Verse.MayTranslateAttribute": shape.MayTranslate = true; break;
                        case "Verse.MustTranslate_SlateRefAttribute": shape.SlateRef = true; break;
                        case "Verse.TranslationCanChangeCountAttribute": shape.TranslationCanChangeCount = true; break;
                        case "Verse.LoadAliasAttribute": alias = ReadFirstStringArg(reader, attr); break;
                    }
                }
                var sig = field.DecodeSignature(new SigProvider(), null);
                Classify(shape, sig);
                result.TryAdd(name, shape);
                if (alias != null)
                {
                    aliases.TryAdd(alias, shape);
                }
            }
            current = BaseTypeName(current);
        }
        foreach (var a in aliases)
        {
            result.TryAdd(a.Key, a.Value);
        }
        _fieldCache[fullName] = result;
        return result;
    }

    private void Classify(FieldShape shape, Sig sig)
    {
        if (sig.Name == "System.String" && !sig.IsArray)
        {
            shape.Kind = FieldKind.String;
            return;
        }
        if (sig.Args.Length == 1 && IsListLike(sig.Name))
        {
            var arg = sig.Args[0];
            if (arg.Name == "System.String")
            {
                shape.Kind = FieldKind.StringList;
                return;
            }
            if (IsCustom(arg.Name) && arg.Args.Length == 0 && _types.ContainsKey(arg.Name) && !InheritsFrom(arg.Name, "Verse.Def") && !IsEnum(arg.Name))
            {
                shape.Kind = FieldKind.ObjectList;
                shape.ValueTypeFullName = arg.Name;
                return;
            }
            shape.Kind = FieldKind.Other;
            return;
        }
        if (!sig.IsArray && sig.Args.Length == 0 && IsCustom(sig.Name) && _types.ContainsKey(sig.Name) && !InheritsFrom(sig.Name, "Verse.Def") && !IsEnum(sig.Name))
        {
            shape.Kind = FieldKind.Object;
            shape.ValueTypeFullName = sig.Name;
            return;
        }
        shape.Kind = FieldKind.Other;
    }

    private bool IsEnum(string fullName) => BaseTypeName(fullName) == "System.Enum";

    private static bool IsListLike(string name) => name is "System.Collections.Generic.List`1" or "System.Collections.Generic.IList`1" or "System.Collections.Generic.IEnumerable`1";

    /// <summary>Same as GenTypes.IsCustomType: namespace does not start with System, UnityEngine or Steamworks.</summary>
    private static bool IsCustom(string fullName) =>
        !fullName.StartsWith("System", StringComparison.Ordinal) && !fullName.StartsWith("UnityEngine", StringComparison.Ordinal) && !fullName.StartsWith("Steamworks", StringComparison.Ordinal);

    private static string FullNameOf(MetadataReader reader, TypeDefinitionHandle h)
    {
        var def = reader.GetTypeDefinition(h);
        var name = reader.GetString(def.Name);
        var declaring = def.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return FullNameOf(reader, declaring) + "+" + name;
        }
        var ns = reader.GetString(def.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string TypeRefName(MetadataReader reader, TypeReferenceHandle h)
    {
        var r = reader.GetTypeReference(h);
        var name = reader.GetString(r.Name);
        if (r.ResolutionScope.Kind == HandleKind.TypeReference)
        {
            return TypeRefName(reader, (TypeReferenceHandle)r.ResolutionScope) + "+" + name;
        }
        var ns = reader.GetString(r.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string? TypeNameOfHandle(MetadataReader reader, EntityHandle h)
    {
        switch (h.Kind)
        {
            case HandleKind.TypeDefinition: return FullNameOf(reader, (TypeDefinitionHandle)h);
            case HandleKind.TypeReference: return TypeRefName(reader, (TypeReferenceHandle)h);
            case HandleKind.TypeSpecification:
                var spec = reader.GetTypeSpecification((TypeSpecificationHandle)h);
                return spec.DecodeSignature(new SigProvider(), null).Name;
            default: return null;
        }
    }

    private static string? AttributeTypeName(MetadataReader reader, CustomAttribute attr)
    {
        switch (attr.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var mr = reader.GetMemberReference((MemberReferenceHandle)attr.Constructor);
                return TypeNameOfHandle(reader, mr.Parent);
            case HandleKind.MethodDefinition:
                var md = reader.GetMethodDefinition((MethodDefinitionHandle)attr.Constructor);
                return FullNameOf(reader, md.GetDeclaringType());
            default:
                return null;
        }
    }

    /// <summary>Reads the first string argument of a custom attribute blob (prolog 0x0001 + SerString).</summary>
    private static string? ReadFirstStringArg(MetadataReader reader, CustomAttribute attr)
    {
        try
        {
            var blob = reader.GetBlobReader(attr.Value);
            if (blob.ReadUInt16() != 1)
            {
                return null;
            }
            return blob.ReadSerializedString();
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        foreach (var pe in _pe)
        {
            pe.Dispose();
        }
        _pe.Clear();
    }

    private sealed record Sig(string Name, ImmutableArray<Sig> Args, bool IsArray)
    {
        public static Sig Simple(string name) => new(name, ImmutableArray<Sig>.Empty, false);
    }

    private sealed class SigProvider : ISignatureTypeProvider<Sig, object?>
    {
        public Sig GetArrayType(Sig elementType, ArrayShape shape) => elementType with { IsArray = true };
        public Sig GetByReferenceType(Sig elementType) => elementType;
        public Sig GetFunctionPointerType(MethodSignature<Sig> signature) => Sig.Simple("System.IntPtr");
        public Sig GetGenericInstantiation(Sig genericType, ImmutableArray<Sig> typeArguments) => new(genericType.Name, typeArguments, false);
        public Sig GetGenericMethodParameter(object? genericContext, int index) => Sig.Simple("!!" + index);
        public Sig GetGenericTypeParameter(object? genericContext, int index) => Sig.Simple("!" + index);
        public Sig GetModifiedType(Sig modifier, Sig unmodifiedType, bool isRequired) => unmodifiedType;
        public Sig GetPinnedType(Sig elementType) => elementType;
        public Sig GetPointerType(Sig elementType) => Sig.Simple("System.IntPtr");
        public Sig GetSZArrayType(Sig elementType) => elementType with { IsArray = true };
        public Sig GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => Sig.Simple(FullNameOf(reader, handle));
        public Sig GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => Sig.Simple(TypeRefName(reader, handle));
        public Sig GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public Sig GetPrimitiveType(PrimitiveTypeCode typeCode) => Sig.Simple(typeCode switch
        {
            PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.Object => "System.Object",
            _ => "System." + typeCode,
        });
    }
}
