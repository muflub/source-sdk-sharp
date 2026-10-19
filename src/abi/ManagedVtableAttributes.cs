namespace SourceSharp.Abi;

/// <summary>
/// On a generated vtable layout: native calls this table and MANAGED writes it
/// (tools/vtable-layout's <c>implemented-by-managed</c> chain kind). Names the
/// C++ abstract class. ManagedVtableGenerator fills no struct without it.
///
/// <para>
/// No header path: the argument is compiled into the assembly's metadata, and
/// a build-tree path there is meaningless at run time. The struct's own summary
/// says which header it was walked from.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class ImplementedByManagedAttribute(string nativeClass) : Attribute
{
    public string NativeClass { get; } = nativeClass;

    /// <summary>
    /// Words the block keeps after word 0 (the table) for native to use: the
    /// vphysics callbacks' native objects, which libss_fwd builds there over
    /// the block (plan_forwarders_everywhere.md phase 5, tools/managed-areas'
    /// CALLBACK_WORDS). The block leaves that many words between the table
    /// pointer and its own header, and keeps them across a reload. Zero for
    /// every other table.
    /// </summary>
    public int ReservedWords { get; init; }
}

/// <summary>
/// On a managed contract: generate the native-callable vtable that forwards
/// each slot of <paramref name="layout"/> (a struct marked
/// <see cref="ImplementedByManagedAttribute"/>) to this contract's method of
/// the same name, on whatever instance the side's container resolves.
/// plan_engineapi_vtables Phase D, §1.3.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class ManagedVtableAttribute(Type layout) : Attribute
{
    public Type Layout { get; } = layout;
}

/// <summary>
/// On a field of an <see cref="ImplementedByManagedAttribute"/> layout: the
/// slot's hash, FNV-1a 32 over its function-pointer type with whitespace
/// removed, written by tools/vtable-layout (plan_managed_vtable_reload.md L1).
/// The generated Validate answers from these; native checks against the same
/// values in its generated header.
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class SlotHashAttribute(uint hash) : Attribute
{
    public uint Hash { get; } = hash;
}

/// <summary>
/// On an assembly, one per managed-built contract it implements: the native
/// interface name and its per-slot hashes, written by ManagedVtableGenerator
/// (plan_managed_vtable_reload.md L5). A reload reads these from the new
/// build's files, without loading them, and refuses the reload unless every
/// contract native holds keeps its hashes.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class ManagedVtableHashesAttribute(string contract, uint[] hashes) : Attribute
{
    public string Contract { get; } = contract;

    public uint[] Hashes { get; } = hashes;
}
