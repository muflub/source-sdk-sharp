namespace SourceSharp.Abi;

/// <summary>
/// plan_engineapi_vtables.md step A3: the one native object the server's
/// managed side is handed at load (<c>LoadSideV2</c>), bound. Every other shim
/// interface is acquired from it by name, with the count native takes from the
/// compiler for that interface.
///
/// <para>
/// Two refusals, each naming what it saw, because a root that is accepted
/// wrongly misbinds every call made through anything acquired from it: a null
/// root, and a slot count other than the generated layout's (the header and
/// <see cref="ShimRootVtbl"/> disagree: a stale build). The count check is
/// EQUALITY, not the <c>&lt;=</c> the other shim acquires use: the root is the
/// first object across, and both sides regenerate it together. (A third, the
/// ABI version, was dropped with the version, 2026-10-01.)
/// </para>
/// </summary>
internal sealed unsafe class ShimRoot
{
    public const string InterfaceVersion = "SourceSharpRoot002";

    private readonly void* _self;
    private readonly ShimRootVtbl* _vt;

    private ShimRoot(void* self, ShimRootVtbl* vt)
    {
        _self = self;
        _vt = vt;
    }

    internal static int ExpectedEntries => sizeof(ShimRootVtbl) / sizeof(void*);

    internal static bool TryBind(void* root, int entries, out ShimRoot? bound, out string why)
    {
        bound = null;

        if (root == null)
        {
            why = "LoadSideV2 was handed no shim root";
            return false;
        }

        if (entries != ExpectedEntries)
        {
            why = $"the shim root has {entries} slot(s) and the generated ShimRootVtbl has "
                + $"{ExpectedEntries}: ishim_root.h and VtableLayouts.g.cs disagree";
            return false;
        }

        var vt = *(ShimRootVtbl**)root;
        bound = new ShimRoot(root, vt);
        why = string.Empty;
        return true;
    }

    /// <summary>
    /// An ENGINE interface by factory kind and version, with its slot count
    /// (plan_engineapi_vtables C5: what the struct's AcquireInterface slot did).
    /// </summary>
    internal void* AcquireInterface(int type, byte* interfaceVersion, int* entries, byte* error, int errorBytes)
        => _vt->AcquireInterface(_self, type, interfaceVersion, entries, error, errorBytes);

    /// <summary>
    /// A shim interface this side serves: its object and its slot count, or
    /// null and 0 for a name this side does not serve.
    /// </summary>
    internal void* Acquire(string interfaceVersion, out int entries)
    {
        int n = 0;
        byte[] name = Vtable.Utf8(interfaceVersion);

        void* self;
        fixed (byte* p = name)
            self = _vt->Acquire(_self, p, &n);

        entries = self == null ? 0 : n;
        return self;
    }
}
