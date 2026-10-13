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
    /// The table keeps the caller's own (Valve's) slots, so it has no
    /// Release/Validate pair: tools/vtable-layout's
    /// <c>valve-implemented-by-managed</c> kind, for the callbacks vphysics
    /// calls (plan_physics_managed B0b). Managed frees the block itself, with
    /// <see cref="ManagedVtable.Release"/>, once the holder is done with it.
    /// </summary>
    public bool ValveLayout { get; init; }

    /// <summary>
    /// Words the C++ class keeps after its vptr, which the caller reads and
    /// writes (CCallbackBase's flags and callback id, which
    /// SteamAPI_RegisterCallback sets). The block leaves that many words
    /// between the vptr and its own header, and keeps them across a reload.
    /// Zero for an interface with no data members. tools/vtable-layout's
    /// <c>reserved_words</c>, which a parity fact holds to the header's
    /// <c>sizeof</c>.
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
/// On a slot of a generated layout whose Microsoft-ABI order is not its
/// Itanium order (tools/vtable-layout): the slot's index under each ABI.
///
/// <para>
/// The engine's Windows binaries are MSVC's, and MSVC gives a virtual
/// destructor one slot rather than two and lays overloads out grouped and in
/// reverse declaration order. A called-by-managed slot is a property that
/// reads <see cref="Microsoft"/> on Windows and <see cref="Itanium"/>
/// elsewhere; on a managed-written field, ManagedVtableGenerator writes the
/// thunk at the index for the ABI it runs on. Tests read it to find a slot
/// without knowing which form the layout took.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = false)]
public sealed class VtableSlotAttribute(int itanium, int microsoft) : Attribute
{
    /// <summary>The slot under the Itanium ABI: the header's declaration order.</summary>
    public int Itanium { get; } = itanium;

    /// <summary>The slot under the Microsoft ABI, or -1 where it has none (a destructor's second slot).</summary>
    public int Microsoft { get; } = microsoft;

    /// <summary>The index for the ABI this process's engine was built with.</summary>
    public int ForThisProcess => OperatingSystem.IsWindows() ? Microsoft : Itanium;
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
