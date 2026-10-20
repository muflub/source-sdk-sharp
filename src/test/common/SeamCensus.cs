using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace SourceSharp.Tests;

/// <summary>
/// plan_shim_areas_split phase 10: what logic and tests may name, read from the
/// built assemblies' metadata and IL.
///
/// <para>
/// <b>What the engine's wrapper is.</b> Four kinds of type, found by shape in
/// the Abi and each side's interop rather than listed, so a new area or a new
/// adapter is covered the day it is generated or written:
/// </para>
/// <list type="bullet">
/// <item><c>EngineSide</c> and <c>ModuleEngine</c>, the side itself;</item>
/// <item>an <b>area class</b>: a class holding a <c>ShimRef&lt;TFwd&gt;</c>,
/// which is how <c>tools/shim-areas</c> writes every area;</item>
/// <item>a <b>forwarder struct</b>: a value type implementing
/// <c>IForwarderTable</c>, the native table layout;</item>
/// <item>a <b>seam implementation</b>: a class implementing a seam (an
/// interface of <c>SourceSharp.Server.Interface</c> or
/// <c>SourceSharp.Client.Interface</c>, or the Abi's <c>ILog</c>) that holds
/// the engine: an instance field of one of the kinds above. A stateless null
/// object (<c>NoRenderPolicyBlocks</c>, <c>StandardErrorLog</c>) reaches no
/// engine and is not one.</item>
/// </list>
///
/// <para>
/// <b>What a reference is.</b> A type's base type and interfaces, its fields',
/// methods' and locals' signatures, and every type, field and method token its
/// IL names, attributed to the outermost type that declares it, so a lambda's
/// closure counts against the type the lambda was written in.
/// </para>
/// </summary>
internal static class SeamCensus
{
    /// <summary>The side a project sees: its interop and its interface assembly, or both.</summary>
    internal enum Sides { Server, Client, Both }

    private static string[] WrapperAssemblies(Sides sides) => sides switch
    {
        Sides.Server => ["SourceSharp.Abi", "SourceSharp.Server.Interop"],
        Sides.Client => ["SourceSharp.Abi", "SourceSharp.Client.Interop"],
        _ => ["SourceSharp.Abi", "SourceSharp.Server.Interop", "SourceSharp.Client.Interop"],
    };

    private static string[] InterfaceAssemblies(Sides sides) => sides switch
    {
        Sides.Server => ["SourceSharp.Server.Interface"],
        Sides.Client => ["SourceSharp.Client.Interface"],
        _ => ["SourceSharp.Server.Interface", "SourceSharp.Client.Interface"],
    };

    /// <summary>
    /// How a reference is made: in a signature (a field, parameter, return,
    /// local, base type or interface, or a called member's signature), by
    /// constructing the type, by calling one of its members, by
    /// <c>typeof</c> (<c>ldtoken</c>), or by any other IL token (a cast, a type
    /// test, a field access).
    /// </summary>
    internal enum Via { Signature, Construct, Call, TypeOf, Token }

    /// <summary>One reference: the referring type, the type it names, the kind that type is, and how.</summary>
    internal sealed record Hit(string Owner, string Type, string Kind, Via Via);

    /// <summary>
    /// Every type of the four kinds, by (assembly, full name), with its kind,
    /// read from the assemblies in <paramref name="dir"/>.
    /// </summary>
    internal static Dictionary<(string Asm, string Name), string> WrapperTypes(string dir, Sides sides = Sides.Both)
    {
        var seams = new HashSet<string>(StringComparer.Ordinal) { "SourceSharp.Abi.ILog" };
        foreach (string a in InterfaceAssemblies(sides))
        {
            using var pe = Open(Path.Combine(dir, a + ".dll"));
            var md = pe.GetMetadataReader();
            foreach (var h in md.TypeDefinitions)
            {
                var t = md.GetTypeDefinition(h);
                if ((t.Attributes & TypeAttributes.Interface) != 0)
                    seams.Add(FullName(md, h));
            }
        }

        // The side, the areas and the tables first, across every wrapper
        // assembly (the areas and tables are the Abi's, the side each
        // interop's): a seam implementation is known by holding one of them.
        var kinds = new Dictionary<(string, string), string>();
        var held = new HashSet<string>(StringComparer.Ordinal);
        foreach (string a in WrapperAssemblies(sides))
        {
            using var pe = Open(Path.Combine(dir, a + ".dll"));
            var md = pe.GetMetadataReader();
            var provider = new Names(md);
            foreach (var h in md.TypeDefinitions)
            {
                var t = md.GetTypeDefinition(h);
                string name = FullName(md, h);
                string? kind = name is "SourceSharp.Abi.EngineSide" or "SourceSharp.Abi.ModuleEngine" ? "wrapper"
                    : Implements(md, t, "SourceSharp.Abi.IForwarderTable") && IsValueType(md, t) ? "forwarder"
                    : FieldTypes(md, t, provider).Any(f => f.StartsWith("SourceSharp.Abi.ShimRef`1<", StringComparison.Ordinal)) ? "area"
                    : null;
                if (kind is null)
                    continue;
                kinds[(a, name)] = kind;
                held.Add(name);
                // An area is held by its interface (IXArea for XArea) since the
                // phase 11 review, so holding that is holding the engine too.
                if (kind == "area")
                    held.Add(name.Insert(name.LastIndexOf('.') + 1, "I"));
            }
        }

        foreach (string a in WrapperAssemblies(sides))
        {
            using var pe = Open(Path.Combine(dir, a + ".dll"));
            var md = pe.GetMetadataReader();
            var provider = new Names(md);
            foreach (var h in md.TypeDefinitions)
            {
                var t = md.GetTypeDefinition(h);
                string name = FullName(md, h);
                if (kinds.ContainsKey((a, name)) || (t.Attributes & TypeAttributes.Interface) != 0)
                    continue;
                string? seam = InterfacesOf(md, t).FirstOrDefault(seams.Contains);
                if (seam is not null && FieldTypes(md, t, provider).Any(f => HoldsEngine(f, held)))
                    kinds[(a, name)] = "implementation of " + seam;
            }
        }

        return kinds;
    }

    /// <summary>
    /// Every reference <paramref name="assemblyPath"/> makes to a type in
    /// <paramref name="wrapper"/>, attributed to its outermost referring type.
    /// </summary>
    internal static List<Hit> References(string assemblyPath, Dictionary<(string Asm, string Name), string> wrapper)
    {
        var hits = new List<Hit>();
        using var pe = Open(assemblyPath);
        var md = pe.GetMetadataReader();
        var provider = new Names(md);

        foreach (var th in md.TypeDefinitions)
        {
            var t = md.GetTypeDefinition(th);
            string owner = Outermost(md, th);
            var named = new List<(EntityHandle Handle, Via Via)>();

            if (!t.BaseType.IsNil)
                named.Add((t.BaseType, Via.Signature));
            foreach (var ih in t.GetInterfaceImplementations())
                named.Add((md.GetInterfaceImplementation(ih).Interface, Via.Signature));

            foreach (var fh in t.GetFields())
                foreach (var r in provider.Refs(md.GetFieldDefinition(fh).Signature, field: true))
                    Add(r, Via.Signature);

            foreach (var mh in t.GetMethods())
            {
                var m = md.GetMethodDefinition(mh);
                foreach (var r in provider.Refs(m.Signature, field: false))
                    Add(r, Via.Signature);
                if (m.RelativeVirtualAddress == 0)
                    continue;

                MethodBodyBlock body = pe.GetMethodBody(m.RelativeVirtualAddress);
                if (!body.LocalSignature.IsNil)
                    foreach (var r in provider.LocalRefs(md.GetStandaloneSignature(body.LocalSignature).Signature))
                        Add(r, Via.Signature);

                foreach (var (op, token) in Tokens(body.GetILBytes()!))
                    named.Add((MetadataTokens.EntityHandle(token),
                               op == OpCodes.Newobj.Value ? Via.Construct
                               : s_CallOps.Contains(op) ? Via.Call
                               : op == OpCodes.Ldtoken.Value ? Via.TypeOf
                               : Via.Token));
            }

            foreach (var (handle, via) in named)
            {
                foreach (var r in provider.Of(handle))
                    Add(r, via);
                foreach (var r in provider.SignatureOf(handle))
                    Add(r, Via.Signature);
            }

            void Add((string Asm, string Name) r, Via via)
            {
                if (wrapper.TryGetValue(r, out string? kind))
                    hits.Add(new Hit(owner, r.Name, kind, via));
            }
        }

        return hits.Distinct().ToList();
    }

    /// <summary>
    /// The second rule's offenders in <paramref name="assemblies"/>: a seam
    /// implementation constructed or held (in a signature, a cast or a field
    /// access) rather than reached through the side's registration and held as
    /// its interface. Two references hold nothing and are allowed: a call to
    /// one of its static members (an acquire that answers with the interface),
    /// and a <c>typeof</c>, by which a structural fact reads the class's shape
    /// (VtableBindingTests: no public state, one private constructor).
    /// </summary>
    internal static List<string> HeldImplementations(string dir, Sides sides, params string[] assemblies)
    {
        var wrapper = WrapperTypes(dir, sides);
        return assemblies
            .SelectMany(a => References(a, wrapper).Select(h => (Asm: Path.GetFileNameWithoutExtension(a), Hit: h)))
            .Where(x => x.Hit.Kind.StartsWith("implementation", StringComparison.Ordinal)
                        && x.Hit.Via is not (Via.Call or Via.TypeOf))
            .Select(x => $"{x.Asm}: {x.Hit.Owner} {x.Hit.Via.ToString().ToLowerInvariant()}s {x.Hit.Type}")
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
    }

    //-------------------------------------------------------------------------
    // Metadata helpers.
    //-------------------------------------------------------------------------

    private static PEReader Open(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("the census reads every assembly it names; a missing one proves nothing", path);
        return new(File.OpenRead(path));
    }

    private static bool HoldsEngine(string fieldType, HashSet<string> kinds)
        => kinds.Any(k => fieldType == k || fieldType.StartsWith(k + "*", StringComparison.Ordinal)
                               || fieldType.StartsWith(k + "?", StringComparison.Ordinal))
           || fieldType.StartsWith("SourceSharp.Abi.ShimRef`1<", StringComparison.Ordinal);

    private static IEnumerable<string> FieldTypes(MetadataReader md, TypeDefinition t, Names provider)
        => t.GetFields().Select(md.GetFieldDefinition)
            .Where(f => (f.Attributes & FieldAttributes.Static) == 0)
            .Select(f => f.DecodeSignature(provider, null));

    private static IEnumerable<string> InterfacesOf(MetadataReader md, TypeDefinition t)
        => t.GetInterfaceImplementations().Select(h => NameOf(md, md.GetInterfaceImplementation(h).Interface));

    private static bool Implements(MetadataReader md, TypeDefinition t, string iface)
        => InterfacesOf(md, t).Contains(iface);

    private static bool IsValueType(MetadataReader md, TypeDefinition t)
        => !t.BaseType.IsNil && NameOf(md, t.BaseType) == "System.ValueType";

    private static string NameOf(MetadataReader md, EntityHandle h) => h.Kind switch
    {
        HandleKind.TypeDefinition => FullName(md, (TypeDefinitionHandle)h),
        HandleKind.TypeReference => RefName(md, (TypeReferenceHandle)h, out _),
        HandleKind.TypeSpecification => new Names(md).Spec((TypeSpecificationHandle)h),
        _ => "",
    };

    internal static string FullName(MetadataReader md, TypeDefinitionHandle h)
    {
        var t = md.GetTypeDefinition(h);
        string name = md.GetString(t.Name);
        var outer = t.GetDeclaringType();
        if (!outer.IsNil)
            return FullName(md, outer) + "/" + name;
        string ns = md.GetString(t.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    private static string Outermost(MetadataReader md, TypeDefinitionHandle h)
    {
        for (var outer = md.GetTypeDefinition(h).GetDeclaringType(); !outer.IsNil;
             outer = md.GetTypeDefinition(h).GetDeclaringType())
            h = outer;
        return FullName(md, h);
    }

    private static string RefName(MetadataReader md, TypeReferenceHandle h, out string asm)
    {
        var r = md.GetTypeReference(h);
        string name = md.GetString(r.Name);
        switch (r.ResolutionScope.Kind)
        {
            case HandleKind.TypeReference:
                return RefName(md, (TypeReferenceHandle)r.ResolutionScope, out asm) + "/" + name;
            case HandleKind.AssemblyReference:
                asm = md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)r.ResolutionScope).Name);
                break;
            default:
                asm = "";
                break;
        }
        string ns = md.GetString(r.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }

    /// <summary>Every token-bearing instruction's opcode and token.</summary>
    private static IEnumerable<(short Op, int Token)> Tokens(byte[] il)
    {
        for (int i = 0; i < il.Length;)
        {
            short op = il[i] == 0xFE ? (short)(0xFE00 | il[i + 1]) : il[i];
            i += il[i] == 0xFE ? 2 : 1;
            OperandType kind = s_Operands[op];
            if (kind is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
                yield return (op, BitConverter.ToInt32(il, i));
            i += kind switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
        }
    }

    private static readonly FrozenSet<short> s_CallOps = new[]
    {
        OpCodes.Call.Value, OpCodes.Callvirt.Value, OpCodes.Ldftn.Value, OpCodes.Ldvirtftn.Value, OpCodes.Jmp.Value,
    }.ToFrozenSet();

    private static readonly FrozenDictionary<short, OperandType> s_Operands = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToFrozenDictionary(o => o.Value, o => o.OperandType);

    /// <summary>
    /// Spells a signature's types by full name, and collects the referenced
    /// types (with their assemblies) a decode walks through.
    /// </summary>
    private sealed class Names(MetadataReader md) : ISignatureTypeProvider<string, object?>
    {
        private List<(string Asm, string Name)>? _refs;

        public IEnumerable<(string Asm, string Name)> Refs(BlobHandle signature, bool field)
        {
            _refs = new();
            try
            {
                var decoder = new SignatureDecoder<string, object?>(this, md, null);
                BlobReader br = md.GetBlobReader(signature);
                if (field)
                    decoder.DecodeFieldSignature(ref br);
                else
                    decoder.DecodeMethodSignature(ref br);
            }
            catch (BadImageFormatException)
            {
            }
            return Take();
        }

        public IEnumerable<(string Asm, string Name)> LocalRefs(BlobHandle signature)
        {
            _refs = new();
            try
            {
                BlobReader br = md.GetBlobReader(signature);
                new SignatureDecoder<string, object?>(this, md, null).DecodeLocalSignature(ref br);
            }
            catch (BadImageFormatException)
            {
            }
            return Take();
        }

        /// <summary>
        /// The type a token names: the type itself, or a member's declaring
        /// type (what a <c>newobj</c> constructs).
        /// </summary>
        public IEnumerable<(string Asm, string Name)> Of(EntityHandle h)
        {
            switch (h.Kind)
            {
                case HandleKind.TypeReference:
                    _refs = new();
                    GetTypeFromReference(md, (TypeReferenceHandle)h, 0);
                    return Take();
                case HandleKind.TypeSpecification:
                    _refs = new();
                    Spec((TypeSpecificationHandle)h);
                    return Take();
                case HandleKind.MemberReference:
                    return Of(md.GetMemberReference((MemberReferenceHandle)h).Parent);
                case HandleKind.MethodSpecification:
                    return Of(md.GetMethodSpecification((MethodSpecificationHandle)h).Method);
                default:
                    return [];
            }
        }

        /// <summary>The types a member token's signature names: its parameters, return or field type, and generic arguments.</summary>
        public IEnumerable<(string Asm, string Name)> SignatureOf(EntityHandle h)
        {
            _refs = new();
            try
            {
                var decoder = new SignatureDecoder<string, object?>(this, md, null);
                if (h.Kind == HandleKind.MemberReference)
                {
                    BlobReader br = md.GetBlobReader(md.GetMemberReference((MemberReferenceHandle)h).Signature);
                    if (br.ReadSignatureHeader().Kind == SignatureKind.Field)
                    {
                        br.Reset();
                        decoder.DecodeFieldSignature(ref br);
                    }
                    else
                    {
                        br.Reset();
                        decoder.DecodeMethodSignature(ref br);
                    }
                }
                else if (h.Kind == HandleKind.MethodSpecification)
                {
                    var spec = md.GetMethodSpecification((MethodSpecificationHandle)h);
                    BlobReader br = md.GetBlobReader(spec.Signature);
                    decoder.DecodeMethodSpecificationSignature(ref br);
                    var refs = Take();
                    refs.AddRange(SignatureOf(spec.Method));
                    return refs;
                }
            }
            catch (BadImageFormatException)
            {
            }
            return Take();
        }

        private List<(string, string)> Take()
        {
            var r = _refs ?? new();
            _refs = null;
            return r;
        }

        public string Spec(TypeSpecificationHandle h)
        {
            BlobReader b = md.GetBlobReader(md.GetTypeSpecification(h).Signature);
            return new SignatureDecoder<string, object?>(this, md, null).DecodeType(ref b);
        }

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => FullName(reader, handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            string name = RefName(reader, handle, out string asm);
            _refs?.Add((asm, name));
            return name;
        }
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            => Spec(handle);
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPinnedType(string elementType) => elementType;
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            => genericType + "<" + string.Join(",", typeArguments) + ">";
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    }
}
