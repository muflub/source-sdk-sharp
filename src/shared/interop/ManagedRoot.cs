using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Shared.Interop;

/// <summary>
/// The one managed-built object a side hands native at load (imanaged_root.h).
/// plan_engineapi_vtables D4: every other managed-built interface native calls
/// is acquired from it by name.
/// </summary>
[ManagedVtable(typeof(ManagedRootTable))]
public unsafe interface IManagedRoot
{
    /// <summary>
    /// The named interface's object, or null. No slot count travels with it
    /// (plan_managed_vtable_reload.md L1): native asks the object's own
    /// Validate, against the hashes it was compiled with.
    /// </summary>
    void* Acquire(byte* pszName);
}

/// <summary>
/// The managed-built interfaces a side serves native, by name. A module
/// registers a builder for each; the root builds one on first acquire, in the
/// side's <see cref="ManagedVtables"/> registry, and hands out the same object
/// after that.
/// </summary>
public sealed class ManagedBindings
{
    private readonly Dictionary<string, Func<ManagedVtables, ManagedVtable>> _builders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ManagedVtable> _built = new(StringComparer.Ordinal);

    public void Add(string name, Func<ManagedVtables, ManagedVtable> build) => _builders.Add(name, build);

    public IReadOnlyCollection<string> Names => _builders.Keys;

    /// <summary>The object for <paramref name="name"/>, built once; null for a name nobody registered.</summary>
    public ManagedVtable? Acquire(string name, ManagedVtables vtables)
    {
        if (_built.TryGetValue(name, out var built))
            return built;
        if (!_builders.TryGetValue(name, out var build))
            return null;

        built = build(vtables);
        _built.Add(name, built);
        return built;
    }

    /// <summary>
    /// After a reload (plan_managed_vtable_reload.md L4): builds every
    /// registered interface whose block native still holds, detached, so the
    /// registry patches it in place. Native acquires once per process and does
    /// not ask again. Returns how many it rebuilt.
    /// </summary>
    public int Reattach(ManagedVtables vtables)
    {
        int rebuilt = 0;
        foreach (string name in _builders.Keys)
        {
            if (!vtables.IsDetached(name))
                continue;
            Acquire(name, vtables);
            rebuilt++;
        }
        return rebuilt;
    }
}

/// <summary>Resolves native's <c>Acquire(name)</c> from the side's <see cref="ManagedBindings"/>.</summary>
/// Every block it builds, itself included, lives in <paramref name="vtables"/>,
/// the side's registry, which the Bootstrap owns (plan_managed_vtable_reload.md L3).
public sealed unsafe class ManagedRoot(ManagedBindings bindings, ManagedVtables vtables) : IManagedRoot
{
    public void* Acquire(byte* pszName)
    {
        string name = Marshal.PtrToStringUTF8((nint)pszName) ?? string.Empty;
        return bindings.Acquire(name, vtables) is { } built ? built.Pointer : null;
    }

    /// <summary>
    /// This root as the native object LoadSideV2 hands back. After a reload it
    /// is the block native already holds, patched, and every interface native
    /// holds is patched with it (L4).
    /// </summary>
    public ManagedVtable Bind()
    {
        var root = IManagedRoot__ManagedVtable.Create(vtables, this);
        bindings.Reattach(vtables);
        return root;
    }
}
