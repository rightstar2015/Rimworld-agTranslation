using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace AgentTranslation.Server;

/// <summary>A literal key passed straight to Translate() in mod code.</summary>
/// <param name="ArgCount">Number of format arguments of the Translate overload; null when unknown (params array).</param>
/// <param name="NearbyKeys">Other literal keys translated in the same method (context for the agent).</param>
public sealed record CodeKeyRef(string Key, string Assembly, string Type, string Method, int? ArgCount, IReadOnlyList<string> NearbyKeys);

/// <summary>A string literal that looks like English text passed to a common UI text call (heuristic).</summary>
public sealed record CodeLiteral(string Text, string Assembly, string Type, string Method, string Call);

public sealed class CodeStrings
{
    public List<CodeKeyRef> Keys { get; } = new();
    public List<CodeLiteral> Literals { get; } = new();
    public List<string> Errors { get; } = new();
}

/// <summary>
/// Reads mod assemblies as files (System.Reflection.Metadata) and looks for literal Translate() keys and hardcoded UI
/// strings. Nothing is loaded or executed. Detection is pattern-based: a string constant directly before the call.
/// Keys built at runtime (concatenation, variables) are not found. Verified against 1.7: Translator.Translate falls back
/// to English and then returns the key itself when no language defines it.
/// </summary>
public static class CodeStringScanner
{
    public const long MaxAssemblyBytes = 64L * 1024 * 1024;
    private const int MaxStringLength = 2000;

    private static readonly Regex KeyShape = new(@"^[A-Za-z_][A-Za-z0-9_.\-]{0,199}$", RegexOptions.CultureInvariant);
    private static readonly Regex HasWord = new(@"[A-Za-z]{3,}", RegexOptions.CultureInvariant);
    private static readonly Regex LooksLikeText = new(@"\s|^[A-Z][a-z]+[.!?:]?$", RegexOptions.CultureInvariant);
    private static readonly Regex IdentifierLike = new(@"^[A-Za-z0-9_.]+$", RegexOptions.CultureInvariant);

    /// <summary>Translate overloads (Verse.Translator / TranslatorFormattedStringExtensions). TryTranslate is excluded: callers usually provide a fallback.</summary>
    private static readonly HashSet<string> TranslateTypes = new(StringComparer.Ordinal) { "Verse.Translator", "Verse.TranslatorFormattedStringExtensions" };

    private static readonly HashSet<string> UiTypes = new(StringComparer.Ordinal)
    {
        "Verse.Widgets", "Verse.Listing_Standard", "Verse.Listing", "Verse.Messages", "Verse.TooltipHandler", "Verse.Dialog_MessageBox", "Verse.FloatMenuOption",
    };

    private static readonly HashSet<string> UiMethods = new(StringComparer.Ordinal)
    {
        "Label", "LabelFit", "ButtonText", "CheckboxLabeled", "Message", "TipRegion", "SliderLabeled", "LabelDouble", "ButtonTextSubtle", "RadioButtonLabeled", ".ctor",
    };

    private static readonly HashSet<string> CommandFields = new(StringComparer.Ordinal) { "defaultLabel", "defaultDesc" };

    public static CodeStrings Scan(IEnumerable<string> assemblyPaths)
    {
        var result = new CodeStrings();
        foreach (var path in assemblyPaths)
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length > MaxAssemblyBytes)
                {
                    continue;
                }
                using var fs = File.OpenRead(path);
                using var pe = new PEReader(fs);
                if (!pe.HasMetadata)
                {
                    continue;
                }
                ScanAssembly(pe, Path.GetFileName(path), result);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or IOException or ArgumentException)
            {
                result.Errors.Add(Path.GetFileName(path) + ": " + ex.GetType().Name);
            }
        }
        return result;
    }

    private static void ScanAssembly(PEReader pe, string assemblyName, CodeStrings result)
    {
        var md = pe.GetMetadataReader();
        foreach (var typeHandle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(typeHandle);
            var typeName = TypeName(md, typeHandle);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = md.GetMethodDefinition(methodHandle);
                if (method.RelativeVirtualAddress == 0)
                {
                    continue;
                }
                var methodName = md.GetString(method.Name);
                var keys = new List<(string Key, int? Args)>();
                var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
                var pending = new List<string>(4);
                while (il.RemainingBytes > 0)
                {
                    var op = ReadOpCode(ref il);
                    if (op == 0x72) // ldstr
                    {
                        var s = md.GetUserString(MetadataTokens.UserStringHandle(il.ReadInt32()));
                        if (s.Length <= MaxStringLength)
                        {
                            pending.Add(s);
                            if (pending.Count > 8)
                            {
                                pending.RemoveAt(0);
                            }
                        }
                        continue;
                    }
                    if (op is 0x28 or 0x6F or 0x73 or 0x7D or 0x80) // call, callvirt, newobj, stfld, stsfld
                    {
                        var target = MetadataTokens.EntityHandle(il.ReadInt32());
                        var (owner, member) = MemberName(md, target);
                        if (pending.Count > 0 && member == "Translate" && TranslateTypes.Contains(owner))
                        {
                            // The key is pushed first, then the format arguments. Without arguments it is the literal
                            // right before the call; with arguments only literals used as argument names (Named("PAWN"))
                            // may follow it, so the first pending literal is the key.
                            var args = ArgCountOf(md, target);
                            var key = args == 0 ? pending[^1] : pending[0];
                            if (KeyShape.IsMatch(key))
                            {
                                keys.Add((key, args));
                            }
                            pending.Clear();
                            continue;
                        }
                        if (member == "Named" || (owner == "Verse.NamedArgument" && member == ".ctor"))
                        {
                            // Building a named format argument: the literal right before it is the argument's name
                            // ("PAWN"), never the key. Drop it and keep the pending key.
                            if (pending.Count > 0)
                            {
                                pending.RemoveAt(pending.Count - 1);
                            }
                            continue;
                        }
                        if (owner == "Verse.NamedArgument")
                        {
                            // Implicit conversion of a value to an argument: no name involved, keep everything.
                            continue;
                        }
                        var ui = (UiTypes.Contains(owner) && UiMethods.Contains(member)) || (CommandFields.Contains(member) && owner.StartsWith("Verse.Command", StringComparison.Ordinal));
                        if (ui)
                        {
                            foreach (var lit in pending)
                            {
                                if (IsCandidateLiteral(lit))
                                {
                                    result.Literals.Add(new CodeLiteral(lit, assemblyName, typeName, methodName, owner + "." + member));
                                }
                            }
                            pending.Clear();
                            continue;
                        }
                        // string -> TaggedString conversions sit between a literal and the UI call.
                        if (member != "op_Implicit")
                        {
                            pending.Clear();
                        }
                        continue;
                    }
                    SkipOperand(ref il, op);
                    if (EndsExpression(op))
                    {
                        // Stores, branches and returns end the argument list: earlier literals are not passed to a later call.
                        pending.Clear();
                    }
                }
                foreach (var (key, args) in keys.Distinct())
                {
                    var nearby = keys.Select(k => k.Key).Where(k => k != key).Distinct().Take(8).ToList();
                    result.Keys.Add(new CodeKeyRef(key, assemblyName, typeName, methodName, args, nearby));
                }
            }
        }
    }

    /// <summary>English-looking text, not a dev-mode label, not an identifier.</summary>
    public static bool IsCandidateLiteral(string s)
    {
        var t = s.Trim();
        if (t.Length < 3 || !HasWord.IsMatch(t) || !LooksLikeText.IsMatch(t))
        {
            return false;
        }
        if (t.StartsWith("DEV", StringComparison.OrdinalIgnoreCase) || t.StartsWith("Debug", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return !(IdentifierLike.IsMatch(t) && !Regex.IsMatch(t, @"^[A-Z][a-z]+$"));
    }

    /// <summary>
    /// Stores to locals/arguments/memory, pop, ret, branches, throw and leave. Array element stores (stelem) are not in
    /// the list: a params array of format arguments is filled with them between the key and the call.
    /// </summary>
    private static bool EndsExpression(int op) => op is
        (>= 0x0A and <= 0x0D) or 0x10 or 0x13 or 0x26 or 0x2A or (>= 0x2B and <= 0x45) or (>= 0x52 and <= 0x57) or 0xDF
        or 0x7A or 0x81 or 0xDC or 0xDD or 0xDE or 0xFE0B or 0xFE0E;

    private static int ReadOpCode(ref BlobReader r)
    {
        int b = r.ReadByte();
        return b == 0xFE ? 0xFE00 | r.ReadByte() : b;
    }

    /// <summary>Skips the operand of an IL instruction (ECMA-335 III).</summary>
    private static void SkipOperand(ref BlobReader r, int op)
    {
        if (op == 0x45) // switch
        {
            var count = r.ReadInt32();
            r.Offset += count * 4;
            return;
        }
        var n = op switch
        {
            0x21 or 0x23 => 8,
            0x0E or 0x0F or 0x10 or 0x11 or 0x12 or 0x13 or 0x1F or 0xDE or 0xFE12 or 0xFE19 => 1,
            >= 0x2B and <= 0x37 => 1,
            0x20 or 0x22 or 0xDD or 0x27 or 0x29 => 4,
            >= 0x38 and <= 0x44 => 4,
            0x70 or 0x71 or 0x74 or 0x75 or 0x79 or 0x7B or 0x7C or 0x7E or 0x7F or 0x81 or 0x8C or 0x8D or 0x8F or 0xA3 or 0xA4 or 0xA5 or 0xC2 or 0xC6 or 0xD0 => 4,
            0xFE06 or 0xFE07 or 0xFE15 or 0xFE16 or 0xFE1C => 4,
            >= 0xFE09 and <= 0xFE0E => 2,
            _ => 0,
        };
        r.Offset += n;
    }

    private static string TypeName(MetadataReader md, TypeDefinitionHandle h)
    {
        var t = md.GetTypeDefinition(h);
        var name = md.GetString(t.Name);
        var declaring = t.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return TypeName(md, declaring) + "+" + name;
        }
        var ns = md.GetString(t.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string TypeRefName(MetadataReader md, EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeReference => md.GetString(md.GetTypeReference((TypeReferenceHandle)h).Namespace) + "." + md.GetString(md.GetTypeReference((TypeReferenceHandle)h).Name),
        HandleKind.TypeDefinition => TypeName(md, (TypeDefinitionHandle)h),
        _ => "?",
    };

    private static (string Owner, string Member) MemberName(MetadataReader md, EntityHandle h)
    {
        switch (h.Kind)
        {
            case HandleKind.MemberReference:
            {
                var m = md.GetMemberReference((MemberReferenceHandle)h);
                var owner = m.Parent.Kind is HandleKind.TypeReference or HandleKind.TypeDefinition ? TypeRefName(md, (EntityHandle)m.Parent) : "?";
                return (owner, md.GetString(m.Name));
            }
            case HandleKind.MethodDefinition:
            {
                var m = md.GetMethodDefinition((MethodDefinitionHandle)h);
                return (TypeName(md, m.GetDeclaringType()), md.GetString(m.Name));
            }
            case HandleKind.FieldDefinition:
            {
                var f = md.GetFieldDefinition((FieldDefinitionHandle)h);
                return (TypeName(md, f.GetDeclaringType()), md.GetString(f.Name));
            }
            case HandleKind.MethodSpecification:
                return MemberName(md, md.GetMethodSpecification((MethodSpecificationHandle)h).Method);
            default:
                return ("?", "?");
        }
    }

    /// <summary>Format arguments of the called Translate overload: parameters minus the key; null for a params array.</summary>
    private static int? ArgCountOf(MetadataReader md, EntityHandle h)
    {
        try
        {
            MethodSignature<string> sig;
            switch (h.Kind)
            {
                case HandleKind.MemberReference:
                    sig = md.GetMemberReference((MemberReferenceHandle)h).DecodeMethodSignature(ShapeProvider.Instance, null);
                    break;
                case HandleKind.MethodDefinition:
                    sig = md.GetMethodDefinition((MethodDefinitionHandle)h).DecodeSignature(ShapeProvider.Instance, null);
                    break;
                default:
                    return null;
            }
            var ps = sig.ParameterTypes;
            if (ps.Length == 0)
            {
                return null;
            }
            if (ps[^1] == ShapeProvider.Array)
            {
                return null;
            }
            return ps.Length - 1;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>Only tells arrays apart from everything else.</summary>
    private sealed class ShapeProvider : ISignatureTypeProvider<string, object?>
    {
        public const string Array = "[]";
        private const string Other = "t";
        public static readonly ShapeProvider Instance = new();

        public string GetArrayType(string elementType, ArrayShape shape) => Array;
        public string GetByReferenceType(string elementType) => Other;
        public string GetFunctionPointerType(MethodSignature<string> signature) => Other;
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => Other;
        public string GetGenericMethodParameter(object? genericContext, int index) => Other;
        public string GetGenericTypeParameter(object? genericContext, int index) => Other;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => Other;
        public string GetPointerType(string elementType) => Other;
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => Other;
        public string GetSZArrayType(string elementType) => Array;
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => Other;
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => Other;
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => Other;
    }
}
