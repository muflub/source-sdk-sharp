using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace SourceSharp.Tests;

/// <summary>What a walked method body reaches on the engine side of the ABI.</summary>
internal enum ReachKind
{
    /// <summary>A member of a <c>SourceSharp.Abi.IEngine*</c> interface: joined through the binding map.</summary>
    Interface,

    /// <summary>A member of <c>EngineSide</c> called directly: joined through the wrapper map.</summary>
    Wrapper,

    /// <summary>A typed call on <c>ShimAreas</c> / <c>ClientShimAreas</c>: the slot itself.</summary>
    Slot,
}

/// <summary>
/// One engine member an assembly reaches, and the helper chain it reaches it
/// through. <see cref="Path"/> starts with the audited assembly's name and
/// names every helper method on the way, so a violation reads as
/// "SourceSharp.Client -> CollisionRules.Register -> IEngineCollisionRules.RegisterCollisionRules".
/// </summary>
internal sealed record Reach(string Path, ReachKind Kind, string Type, string Member);

/// <summary>
/// The transitive half of Audit 2. <see cref="SideAudit.EngineMemberRefs"/>
/// sees only an assembly's OWN references to IEngine* members; a call that
/// goes through a helper in SourceSharp.Abi or an Interop assembly is invisible
/// to it, and that is how the client's CollisionRules.Register reached a
/// server-only slot and did nothing.
///
/// <para>
/// This follows calls, by metadata and IL, from the audited assembly into the
/// helper assemblies it references (to a fixpoint), and reports every engine
/// member those helper bodies reach. Edges are <c>call</c>, <c>callvirt</c>,
/// <c>newobj</c>, <c>jmp</c>, <c>ldftn</c> and <c>ldvirtftn</c>, so lambdas
/// and local functions are followed through their delegates. Virtual dispatch
/// to an override is not followed: a call on an abstract or interface member
/// that is not an IEngine* member reaches only that member's own body.
/// </para>
///
/// <para>
/// A PURE FORWARDER IS PASS-THROUGH. A member of the audited assembly whose
/// whole body is <c>Inner.Same(args)</c>, on a field of the interface its type
/// implements, costs its caller exactly what calling Inner would; its own
/// reference is not a use. What counts is what its subclasses override and
/// call, and what callers call through it: a call to such a member is charged
/// with the member it forwards to. This is how EngineEffectsForwarder is shared
/// source, compiled into both sides, without either "using" the other side's
/// effects it forwards and never calls.
/// </para>
/// </summary>
internal static class SideReach
{
    private const string AbiNamespace = "SourceSharp.Abi";

    /// <summary>
    /// Every engine member the assembly at <paramref name="assemblyPath"/>
    /// reaches. Its own direct references come back with a path of just its
    /// name; references through helpers name the chain. Helper assemblies are
    /// looked up by simple name beside the audited assembly, and one that is
    /// referenced and listed but missing is an error, never a quiet pass.
    /// </summary>
    public static List<Reach> EngineReach(string assemblyPath, IReadOnlySet<string> helperAssemblies,
                                          out List<string> unresolved, IReadOnlySet<string>? chain = null)
    {
        var walker = new Walker(Path.GetDirectoryName(assemblyPath)!, helperAssemblies,
                                chain ?? new HashSet<string>(StringComparer.Ordinal) { "EngineSide" });
        try
        {
            return walker.Run(assemblyPath, out unresolved);
        }
        finally
        {
            walker.Dispose();
        }
    }

    /// <summary>
    /// Which reaches lean on a slot the given side does not fill. A direct
    /// interface reference (path = the assembly alone) keeps
    /// <see cref="SideAudit.UsageViolations"/>' wording exactly, so the waiver
    /// lists mean what they meant; every other reach is prefixed by its path.
    /// </summary>
    public static List<string> ReachViolations(
        IEnumerable<Reach> reaches,
        Dictionary<(string Iface, string Member), HashSet<string>> bindingMap,
        Dictionary<string, HashSet<string>> wrapperSlots,
        Dictionary<string, SlotSide> slotSides,
        Func<SlotSide, bool> allowed)
    {
        var complaints = new List<string>();

        foreach (Reach r in reaches.OrderBy(r => r.Path, StringComparer.Ordinal)
                                   .ThenBy(r => r.Type + "." + r.Member, StringComparer.Ordinal))
        {
            IEnumerable<string> slots = r.Kind switch
            {
                ReachKind.Interface => bindingMap.TryGetValue((r.Type, r.Member), out var wrappers)
                    ? SideAudit.SlotsOf(wrappers, wrapperSlots)
                    : Enumerable.Empty<string>(),
                ReachKind.Wrapper => wrapperSlots.TryGetValue(r.Member, out var ws) ? ws : Enumerable.Empty<string>(),
                _ => new[] { r.Member },
            };

            bool direct = !r.Path.Contains(" -> ", StringComparison.Ordinal);
            foreach (string slot in slots.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal))
            {
                if (!slotSides.TryGetValue(slot, out SlotSide side) || allowed(side))
                    continue;

                complaints.Add(direct && r.Kind == ReachKind.Interface
                    ? $"{r.Type}.{r.Member} is backed by {side}-only slot '{slot}'"
                    : $"{r.Path} is backed by {side}-only slot '{slot}'");
            }
        }

        return complaints.Distinct(StringComparer.Ordinal).ToList();
    }

    //-------------------------------------------------------------------------

    private readonly record struct MethodKey(string Assembly, string Type, string Name, string Signature);

    private sealed class Module : IDisposable
    {
        public readonly string Name;
        public readonly PEReader Pe;
        public readonly MetadataReader Md;
        public readonly Dictionary<string, TypeDefinitionHandle> Types = new(StringComparer.Ordinal);

        public Module(string path)
        {
            Pe = new PEReader(File.OpenRead(path));
            Md = Pe.GetMetadataReader();
            Name = Md.GetString(Md.GetAssemblyDefinition().Name);
            foreach (TypeDefinitionHandle h in Md.TypeDefinitions)
                Types[TypeDefName(Md, h)] = h;
        }

        public void Dispose() => Pe.Dispose();
    }

    private sealed class Walker : IDisposable
    {
        private readonly string _dir;
        private readonly IReadOnlySet<string> _helpers;
        private readonly Dictionary<string, Module> _modules = new(StringComparer.Ordinal);

        private readonly IReadOnlySet<string> _chain;

        public Walker(string dir, IReadOnlySet<string> helpers, IReadOnlySet<string> chain)
        {
            _dir = dir;
            _helpers = helpers;
            _chain = chain;
        }

        public void Dispose()
        {
            foreach (Module m in _modules.Values)
                m.Dispose();
        }

        private Module Load(string name)
        {
            if (_modules.TryGetValue(name, out var m))
                return m;

            string path = Path.Combine(_dir, name + ".dll");
            if (!File.Exists(path))
                throw new FileNotFoundException($"helper assembly '{name}' is referenced but not beside the audited one", path);
            return _modules[name] = new Module(path);
        }

        public List<Reach> Run(string assemblyPath, out List<string> unresolved)
        {
            var root = new Module(assemblyPath);
            _modules[root.Name] = root;

            var reaches = new HashSet<Reach>();
            var missing = new SortedSet<string>(StringComparer.Ordinal);

            // A pure forwarder's own references are not uses (see PassThrough):
            // a reference that only forwarder bodies make is dropped, and a call
            // to a forwarder from any other body is charged with the member it
            // forwards to.
            var pass = PassThrough(root, _chain);

            // The audited assembly's own references, from the member-reference
            // table (every reference, reachable or not -- the granularity the
            // direct audit has always had).
            var entries = new List<(string Label, MethodKey Key)>();
            foreach (MemberReferenceHandle h in root.Md.MemberReferences)
            {
                if (pass.ForwardOnly.Contains(h))
                    continue;

                Target t = Classify(root, h);
                switch (t.Kind)
                {
                    case TargetKind.Engine:
                        reaches.Add(new Reach(root.Name + " -> " + t.Label, t.ReachKind, t.Type, t.Member));
                        break;
                    case TargetKind.Helper:
                        entries.Add((t.Label, t.Key));
                        break;
                }
            }

            // The audited assembly's own IEngine* references keep the direct
            // path (no arrow), so ReachViolations words them as the direct
            // audit always has.
            foreach (Reach r in reaches.ToList())
            {
                if (r.Kind == ReachKind.Interface)
                {
                    reaches.Remove(r);
                    reaches.Add(r with { Path = root.Name });
                }
            }

            // Internal calls into the chain (EngineSide or an IEngine* member
            // the assembly itself defines), from bodies that are not the chain:
            // charged exactly as a member reference was when the chain lived in
            // Abi, so an interface member keeps the direct wording.
            foreach (EntityHandle callee in pass.ChainCalls)
            {
                Target t = Classify(root, callee);
                if (t.Kind == TargetKind.Engine)
                    reaches.Add(new Reach(t.ReachKind == ReachKind.Interface ? root.Name : root.Name + " -> " + t.Label,
                                          t.ReachKind, t.Type, t.Member));
            }

            // Calls through the assembly's own forwarders, from bodies that are
            // not forwarders themselves.
            foreach (var (label, callee) in pass.Calls)
            {
                Target t = Classify(root, callee);
                if (t.Kind == TargetKind.Engine)
                    reaches.Add(new Reach(root.Name + " -> " + label + " -> " + t.Label, t.ReachKind, t.Type, t.Member));
                else if (t.Kind == TargetKind.Helper)
                    entries.Add((label + " -> " + t.Label, t.Key));
            }

            // One walk per entry point, so each helper the assembly calls is
            // named as the start of its own chains.
            foreach (var (label, key) in entries.DistinctBy(e => e.Key))
            {
                var seen = new HashSet<MethodKey> { key };
                var queue = new Queue<(MethodKey Key, string Path)>();
                queue.Enqueue((key, root.Name + " -> " + label));

                while (queue.Count > 0)
                {
                    var (k, path) = queue.Dequeue();
                    foreach (Target t in Callees(k, missing))
                    {
                        if (t.Kind == TargetKind.Engine)
                            reaches.Add(new Reach(path + " -> " + t.Label, t.ReachKind, t.Type, t.Member));
                        else if (t.Kind == TargetKind.Helper && seen.Add(t.Key))
                            queue.Enqueue((t.Key, path + " -> " + t.Label));
                    }
                }
            }

            unresolved = missing.ToList();
            return reaches.ToList();
        }

        //---------------------------------------------------------------------
        // Pure forwarders.
        //---------------------------------------------------------------------

        /// <summary>
        /// The audited assembly's pass-through bodies, read from its IL: its
        /// pure forwarders, and the audit's own chain when it sits here (the
        /// types named in <paramref name="chain"/>, in SourceSharp.Abi:
        /// EngineSide's wrappers and the IEngine* binding classes, which the
        /// wrapper and binding maps already model).
        /// <c>ForwardOnly</c> is every member reference that only pass-through
        /// bodies make; <c>Calls</c> is every call from any other body to a
        /// forwarder, labelled with the forwarder and carrying the member it
        /// forwards to; <c>ChainCalls</c> is every method definition any other
        /// body calls (the caller classifies which are engine members).
        /// </summary>
        private static (HashSet<MemberReferenceHandle> ForwardOnly, List<(string Label, EntityHandle Callee)> Calls,
                        HashSet<EntityHandle> ChainCalls)
            PassThrough(Module m, IReadOnlySet<string> chain)
        {
            var forwards = new Dictionary<MethodDefinitionHandle, EntityHandle>();
            foreach (MethodDefinitionHandle h in m.Md.MethodDefinitions)
            {
                if (ForwardedMember(m, h) is EntityHandle callee)
                    forwards[h] = callee;
            }

            var byForwarders = new HashSet<MemberReferenceHandle>();
            var byOthers = new HashSet<MemberReferenceHandle>();
            var calls = new List<(string, EntityHandle)>();
            var chainCalls = new HashSet<EntityHandle>();
            foreach (MethodDefinitionHandle h in m.Md.MethodDefinitions)
            {
                MethodDefinition def = m.Md.GetMethodDefinition(h);
                if (def.RelativeVirtualAddress == 0)
                    continue;

                bool isForwarder = forwards.ContainsKey(h) || InChain(m.Md, def.GetDeclaringType(), chain);
                foreach (int token in CallTokens(m.Pe.GetMethodBody(def.RelativeVirtualAddress).GetILBytes()!))
                {
                    EntityHandle callee = MetadataTokens.EntityHandle(token);
                    if (callee.Kind == HandleKind.MemberReference)
                        (isForwarder ? byForwarders : byOthers).Add((MemberReferenceHandle)callee);
                    else if (!isForwarder && callee.Kind == HandleKind.MethodDefinition
                             && forwards.TryGetValue((MethodDefinitionHandle)callee, out EntityHandle target))
                    {
                        MethodDefinition fwd = m.Md.GetMethodDefinition((MethodDefinitionHandle)callee);
                        calls.Add(($"{m.Md.GetString(m.Md.GetTypeDefinition(fwd.GetDeclaringType()).Name)}.{m.Md.GetString(fwd.Name)}",
                                   target));
                    }
                    else if (!isForwarder && callee.Kind == HandleKind.MethodDefinition)
                        chainCalls.Add(callee);
                }
            }

            byForwarders.ExceptWith(byOthers);
            return (byForwarders, calls, chainCalls);
        }

        /// <summary>Whether a type (or the outermost type a nested one sits in) is one of the chain's, in SourceSharp.Abi.</summary>
        private static bool InChain(MetadataReader md, TypeDefinitionHandle type, IReadOnlySet<string> chain)
        {
            TypeDefinition td = md.GetTypeDefinition(type);
            while (!td.GetDeclaringType().IsNil)
                td = md.GetTypeDefinition(td.GetDeclaringType());
            return md.GetString(td.Namespace) == AbiNamespace && chain.Contains(md.GetString(td.Name));
        }

        /// <summary>
        /// The member a method forwards to, when its whole body is
        /// <c>this.Field.Same(arg1, ..., argN)</c>: an instance method of a type
        /// that implements interface I, loading a field whose type is I and
        /// calling I's member of the same name and signature with its own
        /// arguments in order. Anything else (a second call, a computed
        /// argument, another member, another interface) is null: not pass-through.
        /// </summary>
        private static EntityHandle? ForwardedMember(Module m, MethodDefinitionHandle h)
        {
            MetadataReader md = m.Md;
            MethodDefinition def = md.GetMethodDefinition(h);
            if (def.RelativeVirtualAddress == 0 || (def.Attributes & MethodAttributes.Static) != 0)
                return null;

            var ins = Instructions(m.Pe.GetMethodBody(def.RelativeVirtualAddress).GetILBytes()!)
                .Where(i => i.Op != OpCodes.Nop.Value).ToList();

            BlobReader sigReader = md.GetBlobReader(def.Signature);
            SignatureHeader header = sigReader.ReadSignatureHeader();
            if (header.IsGeneric)
                return null;
            int argc = sigReader.ReadCompressedInteger();

            // ldarg.0; ldfld F; ldarg 1..N; callvirt M; ret
            if (ins.Count != argc + 4
                || ins[0].Op != OpCodes.Ldarg_0.Value
                || ins[1].Op != OpCodes.Ldfld.Value
                || ins[^2].Op != OpCodes.Callvirt.Value
                || ins[^1].Op != OpCodes.Ret.Value)
                return null;
            for (int a = 1; a <= argc; a++)
            {
                if (ArgIndex(ins[a + 1]) != a)
                    return null;
            }

            EntityHandle callee = MetadataTokens.EntityHandle(ins[^2].Operand);
            if (!CalleeOf(md, callee, out string iface, out string name, out BlobHandle calleeSig)
                || name != md.GetString(def.Name)
                || Signature(md, calleeSig) != Signature(md, def.Signature))
                return null;

            EntityHandle field = MetadataTokens.EntityHandle(ins[1].Operand);
            if (FieldTypeName(md, field) != iface)
                return null;

            TypeDefinition declaring = md.GetTypeDefinition(def.GetDeclaringType());
            bool implements = declaring.GetInterfaceImplementations()
                .Any(ih => TypeName(md, md.GetInterfaceImplementation(ih).Interface) == iface);
            return implements ? callee : null;
        }

        private static int ArgIndex((short Op, int Operand) i)
        {
            if (i.Op == OpCodes.Ldarg_1.Value) return 1;
            if (i.Op == OpCodes.Ldarg_2.Value) return 2;
            if (i.Op == OpCodes.Ldarg_3.Value) return 3;
            if (i.Op == OpCodes.Ldarg_S.Value || i.Op == OpCodes.Ldarg.Value) return i.Operand;
            return -1;
        }

        private static bool CalleeOf(MetadataReader md, EntityHandle callee, out string iface, out string name,
                                     out BlobHandle sig)
        {
            iface = name = string.Empty;
            sig = default;
            switch (callee.Kind)
            {
                case HandleKind.MemberReference:
                {
                    MemberReference mr = md.GetMemberReference((MemberReferenceHandle)callee);
                    if (mr.GetKind() != MemberReferenceKind.Method || mr.Parent.Kind != HandleKind.TypeReference
                        || !TryTypeRef(md, (TypeReferenceHandle)mr.Parent, out _, out _, out _, out iface))
                        return false;
                    name = md.GetString(mr.Name);
                    sig = mr.Signature;
                    return true;
                }
                case HandleKind.MethodDefinition:
                {
                    MethodDefinition d = md.GetMethodDefinition((MethodDefinitionHandle)callee);
                    iface = TypeDefName(md, d.GetDeclaringType());
                    name = md.GetString(d.Name);
                    sig = d.Signature;
                    return true;
                }
                default:
                    return false;
            }
        }

        private static string? FieldTypeName(MetadataReader md, EntityHandle field)
        {
            BlobHandle sig = field.Kind switch
            {
                HandleKind.FieldDefinition => md.GetFieldDefinition((FieldDefinitionHandle)field).Signature,
                HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)field).Signature,
                _ => default,
            };
            if (sig.IsNil)
                return null;
            BlobReader br = md.GetBlobReader(sig);
            return new SignatureDecoder<string, object?>(new NameProvider(), md, null).DecodeFieldSignature(ref br);
        }

        private static string? TypeName(MetadataReader md, EntityHandle type) => type.Kind switch
        {
            HandleKind.TypeDefinition => TypeDefName(md, (TypeDefinitionHandle)type),
            HandleKind.TypeReference => TryTypeRef(md, (TypeReferenceHandle)type, out _, out _, out _, out string full) ? full : null,
            _ => null,
        };

        //---------------------------------------------------------------------

        private enum TargetKind { Other, Engine, Helper }

        private readonly record struct Target(TargetKind Kind, ReachKind ReachKind, string Type, string Member,
                                              string Label, MethodKey Key);

        private Target Classify(Module m, EntityHandle method)
        {
            switch (method.Kind)
            {
                case HandleKind.MethodSpecification:
                    return Classify(m, m.Md.GetMethodSpecification((MethodSpecificationHandle)method).Method);

                case HandleKind.MethodDefinition:
                {
                    MethodDefinition def = m.Md.GetMethodDefinition((MethodDefinitionHandle)method);
                    TypeDefinitionHandle th = def.GetDeclaringType();
                    TypeDefinition td = m.Md.GetTypeDefinition(th);
                    string ns = OuterNamespace(m.Md, th);
                    string name = m.Md.GetString(def.Name);
                    string sig = Signature(m.Md, def.Signature);
                    return Make(m.Name, ns, m.Md.GetString(td.Name), TypeDefName(m.Md, th), name, sig);
                }

                case HandleKind.MemberReference:
                {
                    MemberReference mr = m.Md.GetMemberReference((MemberReferenceHandle)method);
                    if (mr.GetKind() != MemberReferenceKind.Method)
                        return default;
                    if (!TryParent(m.Md, mr.Parent, out string asm, out string ns, out string simple, out string full))
                        return default;
                    if (!_helpers.Contains(asm))
                        return default;
                    return Make(asm, ns, simple, full, m.Md.GetString(mr.Name), Signature(m.Md, mr.Signature));
                }

                default:
                    return default;
            }
        }

        private Target Make(string asm, string ns, string simpleType, string fullType, string name, string sig)
        {
            string label = $"{simpleType}.{name}";

            if (ns == AbiNamespace && simpleType.StartsWith("IEngine", StringComparison.Ordinal))
            {
                string member = name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("set_", StringComparison.Ordinal)
                    ? name[4..] : name;
                return new Target(TargetKind.Engine, ReachKind.Interface, simpleType, member, $"{simpleType}.{member}", default);
            }
            if (ns == AbiNamespace && simpleType == "EngineSide")
            {
                string member = name.StartsWith("get_", StringComparison.Ordinal) ? name[4..] : name;
                return new Target(TargetKind.Engine, ReachKind.Wrapper, simpleType, member, $"EngineSide.{member}", default);
            }
            if (ns == AbiNamespace && simpleType.EndsWith("Area", StringComparison.Ordinal))
            {
                string member = name.StartsWith("get_", StringComparison.Ordinal) ? name[4..] : name;
                // The generated area classes hold only typed slot calls, and
                // since plan_shim_areas_split phase 11 a consumer holds the area
                // it calls, so a call on one is the slot itself.
                return new Target(TargetKind.Engine, ReachKind.Slot, simpleType, member,
                                  $"{simpleType}.{member}", default);
            }

            return new Target(TargetKind.Helper, default, simpleType, name, label, new MethodKey(asm, fullType, name, sig));
        }

        private IEnumerable<Target> Callees(MethodKey key, SortedSet<string> missing)
        {
            Module m = Load(key.Assembly);
            if (!m.Types.TryGetValue(key.Type, out TypeDefinitionHandle th))
            {
                missing.Add($"{key.Assembly}: type {key.Type}");
                yield break;
            }

            TypeDefinition td = m.Md.GetTypeDefinition(th);
            var byName = td.GetMethods().Where(h => m.Md.GetString(m.Md.GetMethodDefinition(h).Name) == key.Name).ToList();
            var exact = byName.Where(h => Signature(m.Md, m.Md.GetMethodDefinition(h).Signature) == key.Signature).ToList();

            // A signature the decoder spells differently across assemblies falls
            // back to every overload of the name: more reach, never less.
            var bodies = exact.Count > 0 ? exact : byName;
            if (bodies.Count == 0)
            {
                missing.Add($"{key.Assembly}: {key.Type}::{key.Name}");
                yield break;
            }

            foreach (MethodDefinitionHandle h in bodies)
            {
                MethodDefinition def = m.Md.GetMethodDefinition(h);
                if (def.RelativeVirtualAddress == 0)
                    continue;

                MethodBodyBlock body = m.Pe.GetMethodBody(def.RelativeVirtualAddress);
                foreach (int token in CallTokens(body.GetILBytes()!))
                {
                    Target t = Classify(m, MetadataTokens.EntityHandle(token));
                    if (t.Kind != TargetKind.Other)
                        yield return t;
                }
            }
        }
    }

    //-------------------------------------------------------------------------
    // Names and signatures.
    //-------------------------------------------------------------------------

    private static string TypeDefName(MetadataReader md, TypeDefinitionHandle h)
    {
        TypeDefinition td = md.GetTypeDefinition(h);
        TypeDefinitionHandle outer = td.GetDeclaringType();
        return outer.IsNil
            ? Join(md.GetString(td.Namespace), md.GetString(td.Name))
            : TypeDefName(md, outer) + "/" + md.GetString(td.Name);
    }

    private static string OuterNamespace(MetadataReader md, TypeDefinitionHandle h)
    {
        TypeDefinition td = md.GetTypeDefinition(h);
        TypeDefinitionHandle outer = td.GetDeclaringType();
        return outer.IsNil ? md.GetString(td.Namespace) : OuterNamespace(md, outer);
    }

    private static string Join(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;

    private static bool TryParent(MetadataReader md, EntityHandle parent, out string asm, out string ns,
                                  out string simple, out string full)
    {
        asm = ns = simple = full = string.Empty;
        switch (parent.Kind)
        {
            case HandleKind.TypeReference:
                return TryTypeRef(md, (TypeReferenceHandle)parent, out asm, out ns, out simple, out full);

            case HandleKind.TypeSpecification:
            {
                // A generic instantiation: the member lives on the generic type.
                BlobReader br = md.GetBlobReader(md.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
                if (br.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
                    return false;
                br.ReadSignatureTypeCode(); // class / valuetype
                EntityHandle generic = br.ReadTypeHandle();
                return generic.Kind == HandleKind.TypeReference
                    && TryTypeRef(md, (TypeReferenceHandle)generic, out asm, out ns, out simple, out full);
            }

            default:
                return false;
        }
    }

    private static bool TryTypeRef(MetadataReader md, TypeReferenceHandle h, out string asm, out string ns,
                                   out string simple, out string full)
    {
        TypeReference tr = md.GetTypeReference(h);
        simple = md.GetString(tr.Name);
        asm = ns = full = string.Empty;

        switch (tr.ResolutionScope.Kind)
        {
            case HandleKind.AssemblyReference:
                asm = md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)tr.ResolutionScope).Name);
                ns = md.GetString(tr.Namespace);
                full = Join(ns, simple);
                return true;

            case HandleKind.TypeReference:
                if (!TryTypeRef(md, (TypeReferenceHandle)tr.ResolutionScope, out asm, out ns, out _, out string outer))
                    return false;
                full = outer + "/" + simple;
                return true;

            default:
                return false;
        }
    }

    private static string Signature(MetadataReader md, BlobHandle blob)
    {
        try
        {
            var decoder = new SignatureDecoder<string, object?>(new NameProvider(), md, null);
            BlobReader br = md.GetBlobReader(blob);
            MethodSignature<string> sig = decoder.DecodeMethodSignature(ref br);
            return $"{sig.ReturnType}({string.Join(",", sig.ParameterTypes)})`{sig.GenericParameterCount}";
        }
        catch (BadImageFormatException)
        {
            return "?";
        }
    }

    /// <summary>Spells a type by its full name, the same from either end of a reference.</summary>
    private sealed class NameProvider : ISignatureTypeProvider<string, object?>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => TypeDefName(reader, handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => TryTypeRef(reader, handle, out _, out _, out _, out string full) ? full : "?";
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            BlobReader b = reader.GetBlobReader(reader.GetTypeSpecification(handle).Signature);
            return new SignatureDecoder<string, object?>(this, reader, genericContext).DecodeType(ref b);
        }
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPinnedType(string elementType) => elementType;
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
            => genericType + "<" + string.Join(",", typeArguments) + ">";
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetFunctionPointerType(MethodSignature<string> signature)
            => "fnptr(" + string.Join(",", signature.ParameterTypes) + ")" + signature.ReturnType;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    }

    //-------------------------------------------------------------------------
    // IL: the tokens of every call-shaped instruction.
    //-------------------------------------------------------------------------

    private static readonly FrozenDictionary<short, OperandType> s_Operands = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToFrozenDictionary(o => o.Value, o => o.OperandType);

    private static readonly FrozenSet<short> s_Calls = new[]
    {
        OpCodes.Call, OpCodes.Callvirt, OpCodes.Newobj, OpCodes.Jmp, OpCodes.Ldftn, OpCodes.Ldvirtftn,
    }.Select(o => o.Value).ToFrozenSet();

    internal static IEnumerable<int> CallTokens(byte[] il)
        => Instructions(il).Where(i => s_Calls.Contains(i.Op)).Select(i => i.Operand);

    /// <summary>
    /// Every instruction, with its operand when it is a token or an argument
    /// index (InlineMethod, InlineField, InlineTok, InlineVar, ShortInlineVar...);
    /// other operands read as 0.
    /// </summary>
    internal static IEnumerable<(short Op, int Operand)> Instructions(byte[] il)
    {
        int i = 0;
        while (i < il.Length)
        {
            short op = il[i] == 0xFE ? (short)(0xFE00 | il[i + 1]) : il[i];
            i += il[i] == 0xFE ? 2 : 1;

            if (!s_Operands.TryGetValue(op, out OperandType kind))
                throw new BadImageFormatException($"unknown IL opcode 0x{op:X4} at {i}");

            yield return (op, kind switch
            {
                OperandType.ShortInlineVar => il[i],
                OperandType.InlineVar => BitConverter.ToUInt16(il, i),
                OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok
                    or OperandType.InlineType or OperandType.InlineSig or OperandType.InlineString => BitConverter.ToInt32(il, i),
                _ => 0,
            });

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
}
