using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SourceSharp.Generators;

/// <summary>
/// Writes the native-callable vtable for every <c>[ManagedVtable(typeof(Layout))]</c>
/// contract. plan_engineapi_vtables Phase D, §1.3.
///
/// <para>
/// <b>TWO INPUTS, AND NEITHER IS WRITTEN BY HAND TWICE.</b> The LAYOUT is the struct
/// <c>tools/vtable-layout</c> generated from the C++ abstract class: field N is slot N,
/// every field is a <c>delegate* unmanaged&lt;void*, ...&gt;</c>, the last is
/// <c>Release</c>, and it carries <c>[ImplementedByManaged]</c>. The CONTRACT is the
/// managed interface the side's container resolves. Each slot is forwarded to the
/// contract's method of the same name, whose parameters and return must be the slot's
/// exactly: a thunk converts nothing, so there is nothing in it that can be wrong.
/// </para>
///
/// <para>
/// <b>WHAT IT WRITES</b>, per contract, a static class holding one
/// <c>[UnmanagedCallersOnly]</c> thunk per slot and a <c>Create(contract)</c> that
/// builds the <c>ManagedVtable</c> block around the instance. Each thunk recovers its
/// target through <c>GCHandle&lt;TContract&gt;</c>, never an unchecked cast of an
/// <c>object</c>, and catches every exception, because one that unwinds into native
/// frames takes the process down with no managed line at all. <c>Release</c> is the
/// generator's own, and a contract may not declare it.
/// </para>
///
/// <para>
/// <b>IT REFUSES THE RAW-ADDRESS FORM.</b> The only way a slot is filled is
/// <c>&amp;Thunk</c> on a generated <c>[UnmanagedCallersOnly]</c> method, whose
/// function-pointer type the compiler checks against the slot's. Nothing here takes an
/// address as an integer from anywhere else.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ManagedVtableGenerator : IIncrementalGenerator
{
    private const string VtableAttribute = "SourceSharp.Abi.ManagedVtableAttribute";
    private const string ImplementedByManaged = "SourceSharp.Abi.ImplementedByManagedAttribute";
    private const string SlotHash = "SourceSharp.Abi.SlotHashAttribute";
    private const string Runtime = "global::SourceSharp.Abi.ManagedVtable";
    private const string Registry = "global::SourceSharp.Abi.ManagedVtables";
    private const string HashesAttribute = "global::SourceSharp.Abi.ManagedVtableHashes";

    private static readonly DiagnosticDescriptor NotAManagedLayout = new(
        "SSVT001", "A managed vtable's layout must be an [ImplementedByManaged] struct",
        "[ManagedVtable(typeof({0}))] on {1}: {0} is not a struct tools/vtable-layout emitted with " +
        "[ImplementedByManaged], so nothing says native calls it or what its slots are",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor SlotWithoutMethod = new(
        "SSVT002", "Every slot of the layout needs a contract method",
        "{1} has no method named '{0}' for slot {2} of {3}: native would call through a hole",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MethodWithoutSlot = new(
        "SSVT003", "Every contract method needs a slot in the layout",
        "{1} declares '{0}', which is not a slot of {2}: native can never call it, so it is either " +
        "misnamed or belongs to another contract",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor SignatureDiffers = new(
        "SSVT004", "A contract method must take and return exactly what its slot does",
        "{1}.{0} is {2}, and slot '{0}' of {3} is {4}: a thunk converts nothing, so the contract " +
        "has to speak the slot's types",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ReleaseIsTheGenerators = new(
        "SSVT005", "Release (slot 0) and Validate (slot 1) are the generator's slots",
        "{0}: {1}",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor Overloaded = new(
        "SSVT006", "A contract method name is a slot name, so it may not be overloaded",
        "{1} declares '{0}' more than once: a slot is one name with one signature",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MustBeTopLevel = new(
        "SSVT007", "A managed vtable contract must be a non-generic interface at namespace level",
        "[ManagedVtable] on {0}: declare it as a non-generic interface at namespace level, so the " +
        "generated binding can name it",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor NoSlotHash = new(
        "SSVT008", "Every slot of a managed layout carries its [SlotHash]",
        "slot '{0}' of {1} has no [SlotHash]: Validate could not answer for it, and native checks every " +
        "slot's hash before it calls any (tools/vtable-layout writes them)",
        "SourceSharp", DiagnosticSeverity.Error, isEnabledByDefault: true);

    /// <summary>One slot: its name, its C# argument types after self, its return, and its hash.</summary>
    private sealed record Slot(string Name, ImmutableArray<string> Parameters, string Returns, string Pointer, uint Hash, bool Valve);

    private sealed record Declaration(
        string? Namespace,
        string Contract,
        string ContractFull,
        string Native,
        int ReservedWords,
        ImmutableArray<Slot> Slots,
        ImmutableArray<Diagnostic> Diagnostics);

    private static readonly SymbolDisplayFormat Full = SymbolDisplayFormat.FullyQualifiedFormat;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var declarations = context.SyntaxProvider.ForAttributeWithMetadataName(
            VtableAttribute,
            predicate: static (node, _) => node is InterfaceDeclarationSyntax,
            transform: static (ctx, _) => Read(ctx));

        context.RegisterSourceOutput(declarations, static (spc, d) =>
        {
            foreach (var diagnostic in d.Diagnostics)
                spc.ReportDiagnostic(diagnostic);

            if (d.Diagnostics.IsEmpty)
                spc.AddSource(d.Contract + "__ManagedVtable.g.cs", SourceText.From(Write(d), Encoding.UTF8));
        });
    }

    private static Declaration Read(GeneratorAttributeSyntaxContext ctx)
    {
        var contract = (INamedTypeSymbol)ctx.TargetSymbol;
        var location = ctx.TargetNode.GetLocation();
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var slots = ImmutableArray.CreateBuilder<Slot>();

        string? ns = contract.ContainingNamespace.IsGlobalNamespace
            ? null
            : contract.ContainingNamespace.ToDisplayString();

        // The native interface name the layout's [ImplementedByManaged] gives:
        // the registry's key (plan_managed_vtable_reload.md L3).
        string native = contract.Name;
        int reservedWords = 0;

        Declaration Done() => new(ns, contract.Name, contract.ToDisplayString(Full), native, reservedWords,
                                  slots.ToImmutable(), diagnostics.ToImmutable());

        if (contract.ContainingType is not null || contract.IsGenericType)
        {
            diagnostics.Add(Diagnostic.Create(MustBeTopLevel, location, contract.Name));
            return Done();
        }

        var layout = ctx.Attributes[0].ConstructorArguments.FirstOrDefault().Value as INamedTypeSymbol;
        string layoutName = layout?.Name ?? "?";

        if (layout is null || layout.TypeKind != TypeKind.Struct
            || !layout.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == ImplementedByManaged))
        {
            diagnostics.Add(Diagnostic.Create(NotAManagedLayout, location, layoutName, contract.Name));
            return Done();
        }

        var implemented = layout.GetAttributes()
                                .First(a => a.AttributeClass?.ToDisplayString() == ImplementedByManaged);
        native = implemented.ConstructorArguments.FirstOrDefault().Value as string ?? contract.Name;

        // VALVE'S LAYOUT (plan_physics_managed B0b): the caller's own slots, so
        // there is no Release/Validate pair to require or to write; every field
        // is a contract method.
        bool valveLayout = implemented.NamedArguments
                                      .Any(a => a.Key == "ValveLayout" && a.Value.Value is true);

        // RESERVED WORDS: the C++ class's data members after its vptr, which
        // the caller writes (CCallbackBase); the block keeps them free.
        reservedWords = implemented.NamedArguments
                                   .Where(a => a.Key == "ReservedWords" && a.Value.Value is int)
                                   .Select(a => (int)a.Value.Value!)
                                   .FirstOrDefault();

        // The layout's slots, in field order: field N is slot N.
        var fields = layout.GetMembers().OfType<IFieldSymbol>()
                           .Where(f => !f.IsStatic && f.Type is IFunctionPointerTypeSymbol)
                           .ToList();

        // THE TWO FIXED SLOTS (plan_managed_vtable_reload.md L1): Release at 0,
        // a fixed index like a destructor; Validate at 1, with exactly
        // (void* self, uint* or void* hashes, int count) -> int (the layouts
        // write every pointer as void*), because native calls it
        // before it knows anything else about the object.
        var release = fields.ElementAtOrDefault(0);
        if (valveLayout)
        {
        }
        else if (release is null || release.Name != "Release"
            || ((IFunctionPointerTypeSymbol)release.Type).Signature.Parameters.Length != 1
            || !((IFunctionPointerTypeSymbol)release.Type).Signature.ReturnsVoid)
        {
            diagnostics.Add(Diagnostic.Create(ReleaseIsTheGenerators, location, contract.Name,
                $"{layoutName} does not start with `void Release()` at slot 0"));
            return Done();
        }

        var validate = fields.ElementAtOrDefault(1);
        var validateSignature = (validate?.Type as IFunctionPointerTypeSymbol)?.Signature;
        if (valveLayout)
        {
        }
        else if (validate is null || validate.Name != "Validate" || validateSignature is null
            || validateSignature.ReturnType.SpecialType != SpecialType.System_Int32
            || validateSignature.Parameters.Length != 3
            || validateSignature.Parameters[1].Type is not IPointerTypeSymbol { PointedAtType.SpecialType: SpecialType.System_UInt32 or SpecialType.System_Void }
            || validateSignature.Parameters[2].Type.SpecialType != SpecialType.System_Int32)
        {
            diagnostics.Add(Diagnostic.Create(ReleaseIsTheGenerators, location, contract.Name,
                $"{layoutName} does not have `int Validate(uint* slotHashes, int count)` at slot 1"));
            return Done();
        }

        var methods = contract.GetMembers().OfType<IMethodSymbol>()
                              .Where(m => m.MethodKind == MethodKind.Ordinary && m.IsAbstract)
                              .ToList();

        foreach (var group in methods.GroupBy(m => m.Name).Where(g => g.Count() > 1))
            diagnostics.Add(Diagnostic.Create(Overloaded, location, group.Key, contract.Name));

        foreach (var fixedSlot in methods.Where(m => !valveLayout && m.Name is "Release" or "Validate"))
            diagnostics.Add(Diagnostic.Create(ReleaseIsTheGenerators, location, contract.Name,
                $"the contract declares {fixedSlot.Name}, which the generator writes: Release frees the " +
                "handle and Validate answers from the layout's slot hashes, and a contract's own would " +
                "run instead of that or beside it"));

        var slotNames = new HashSet<string>(fields.Select(f => f.Name));
        foreach (var method in methods.Where(m => !slotNames.Contains(m.Name) && (valveLayout || m.Name is not ("Release" or "Validate"))))
            diagnostics.Add(Diagnostic.Create(MethodWithoutSlot, location, method.Name, contract.Name, layoutName));

        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var signature = ((IFunctionPointerTypeSymbol)field.Type).Signature;
            var parameters = signature.Parameters.Skip(1).Select(p => p.Type.ToDisplayString(Full)).ToImmutableArray();
            string returns = signature.ReturnsVoid ? "void" : signature.ReturnType.ToDisplayString(Full);
            string pointer = field.Type.ToDisplayString(Full);

            var hash = field.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == SlotHash)
                            ?.ConstructorArguments.FirstOrDefault().Value as uint?;
            if (hash is null)
                diagnostics.Add(Diagnostic.Create(NoSlotHash, location, field.Name, layoutName));

            slots.Add(new Slot(field.Name, parameters, returns, pointer, hash ?? 0, valveLayout));

            if (!valveLayout && field.Name is "Release" or "Validate")
                continue;

            var method = methods.FirstOrDefault(m => m.Name == field.Name);
            if (method is null)
            {
                diagnostics.Add(Diagnostic.Create(SlotWithoutMethod, location, field.Name, contract.Name, i, layoutName));
                continue;
            }

            var mine = method.Parameters.Select(p => p.RefKind == RefKind.None ? p.Type.ToDisplayString(Full) : "ref")
                             .ToImmutableArray();
            string myReturn = method.ReturnsVoid ? "void" : method.ReturnType.ToDisplayString(Full);

            if (!mine.SequenceEqual(parameters) || myReturn != returns)
                diagnostics.Add(Diagnostic.Create(SignatureDiffers, location, field.Name, contract.Name,
                    Spell(mine, myReturn), layoutName, Spell(parameters, returns)));
        }

        return Done();
    }

    private static string Spell(IEnumerable<string> parameters, string returns)
        => $"({string.Join(", ", parameters)}) -> {returns}";

    private static string Write(Declaration d)
    {
        var s = new StringBuilder();
        s.AppendLine("// <auto-generated/> by SourceSharp.Generators.ManagedVtableGenerator");
        s.AppendLine("#nullable enable");
        // plan_managed_vtable_reload.md L5: the build's slot hashes, readable
        // from the file without loading it, so a reload can be refused before
        // the running build is unloaded.
        s.AppendLine($"[assembly: {HashesAttribute}(\"{d.Native}\", new uint[] {{ {string.Join(", ", d.Slots.Select(x => $"0x{x.Hash:x8}u"))} }})]");
        s.AppendLine();
        if (d.Namespace is not null)
            s.AppendLine($"namespace {d.Namespace};");
        s.AppendLine();
        s.AppendLine($"/// <summary>The native-callable vtable for <see cref=\"{d.Contract}\"/>, in slot order.</summary>");
        s.AppendLine($"internal static unsafe class {d.Contract}__ManagedVtable");
        s.AppendLine("{");
        s.AppendLine($"    public const int Entries = {d.Slots.Length};");
        s.AppendLine();
        s.AppendLine("    /// <summary>Each slot's hash, from the layout's [SlotHash] (tools/vtable-layout).</summary>");
        s.AppendLine("    private static global::System.ReadOnlySpan<uint> SlotHashes => new uint[]");
        s.AppendLine("    {");
        foreach (var slot in d.Slots)
            s.AppendLine($"        0x{slot.Hash:x8}u,   // {slot.Name}");
        s.AppendLine("    };");
        s.AppendLine();
        s.AppendLine($"    public const string Native = \"{d.Native}\";");
        s.AppendLine();
        s.AppendLine("    /// <summary>The caller's words between the vptr and the block's header (the layout's ReservedWords).</summary>");
        s.AppendLine($"    public const int ReservedWords = {d.ReservedWords};");
        s.AppendLine();
        s.AppendLine($"    public static {Runtime} Create({Registry} vtables, {d.ContractFull} target)");
        s.AppendLine("        => Create(vtables, target, null);");
        s.AppendLine();
        s.AppendLine("    /// <summary>One of many live blocks of this contract, named <paramref name=\"instance\"/>; a reload patches each by its name.</summary>");
        s.AppendLine($"    public static {Runtime} Create({Registry} vtables, {d.ContractFull} target, string? instance)");
        s.AppendLine("    {");
        s.AppendLine($"        global::System.IntPtr* slots = stackalloc global::System.IntPtr[{d.Slots.Length}];");
        for (int i = 0; i < d.Slots.Length; i++)
            s.AppendLine($"        slots[{i}] = (global::System.IntPtr)({d.Slots[i].Pointer})&{d.Slots[i].Name};");
        // A Valve-layout table has no Release/Validate pair, and can be one
        // slot long (IPhysicsConstraintEvent, IMotionEvent); the registry is told.
        string valve = d.Slots.Any(x => x.Valve) ? ", valveLayout: true" : "";
        s.AppendLine($"        return vtables.Create<{d.ContractFull}>(Native, target, new global::System.ReadOnlySpan<global::System.IntPtr>(slots, {d.Slots.Length}), SlotHashes, ReservedWords, instance{valve});");
        s.AppendLine("    }");

        foreach (var slot in d.Slots)
        {
            var args = slot.Parameters.Select((t, i) => $"{t} a{i}").ToList();
            string names = string.Join(", ", slot.Parameters.Select((_, i) => $"a{i}"));
            bool isVoid = slot.Returns == "void";

            s.AppendLine();
            s.AppendLine("    [global::System.Runtime.InteropServices.UnmanagedCallersOnly]");
            s.AppendLine($"    private static {slot.Returns} {slot.Name}({string.Join(", ", new[] { "void* self" }.Concat(args))})");
            s.AppendLine("    {");
            s.AppendLine($"        {Registry}? owner = {Runtime}.Enter(self);   // L6's depth, on the block's own registry");
            s.AppendLine("        try");
            s.AppendLine("        {");
            if (slot.Name == "Release" && !slot.Valve)
                s.AppendLine($"            {Runtime}.ReleaseBlock<{d.ContractFull}>(self);");
            else if (slot.Name == "Validate" && !slot.Valve)
                s.AppendLine($"            return {Runtime}.ValidateSlots(SlotHashes, (uint*)a0, a1);");
            else
                s.AppendLine($"            {(isVoid ? "" : "return ")}{Runtime}.Target<{d.ContractFull}>(self).{slot.Name}({names});");
            s.AppendLine("        }");
            s.AppendLine("        catch (global::System.Exception e)");
            s.AppendLine("        {");
            s.AppendLine($"            {Runtime}.Fail(owner?.Log, \"{d.Contract}.{slot.Name}\", e);");
            if (slot.Name == "Validate" && !slot.Valve)
                s.AppendLine("            return 0;   // a fault is never a match: slot 0 differs");
            else if (!isVoid)
                s.AppendLine("            return default;");
            s.AppendLine("        }");
            s.AppendLine("        finally");
            s.AppendLine("        {");
            s.AppendLine($"            {Runtime}.Leave(owner);");
            s.AppendLine("        }");
            s.AppendLine("    }");
        }

        s.AppendLine("}");
        return s.ToString();
    }
}
