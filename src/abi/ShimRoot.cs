namespace SourceSharp.Abi;

/// <summary>
/// plan_engineapi_vtables.md step A3: the one native object a side's managed
/// code is handed at load (<c>LoadSideV2</c>), bound. Every other shim
/// interface is acquired from it by name. Since plan_forwarders_everywhere.md
/// phase 3 native hands the root's forwarder table (<c>SSFwd_ShimRoot</c>)
/// beside it, and every call to the root goes through that table.
///
/// <para>
/// Two refusals, each naming what it saw, because a root that is accepted
/// wrongly misbinds every call made through anything acquired from it: a null
/// root, and a table native serves with fewer slots than
/// <see cref="ShimRootFwd"/> reads (<see cref="Forwarders.TryBind"/>: the
/// library is older than this assembly). The table only appends, so a longer
/// one is a newer library and binds. (A third, the ABI version, was dropped
/// with the version, 2026-10-01.)
/// </para>
/// </summary>
internal sealed unsafe class ShimRoot
{
    public const string InterfaceVersion = "SourceSharpRoot003";

    private readonly void* _self;
    private readonly ShimRootFwd* _vt;

    private ShimRoot(void* self, ShimRootFwd* vt)
    {
        _self = self;
        _vt = vt;
    }

    internal static int ExpectedEntries => Forwarders.SlotCount<ShimRootFwd>();

    internal static bool TryBind(void* root, void* table, out ShimRoot? bound, out string why)
    {
        bound = null;

        if (root == null)
        {
            why = "LoadSideV2 was handed no shim root";
            return false;
        }

        if (!Forwarders.TryBind((ForwarderTableHeader*)table, out ShimRootFwd* vt, out why))
        {
            why = $"the shim root's table: {why}";
            return false;
        }

        bound = new ShimRoot(root, vt);
        return true;
    }

    /// <summary>
    /// An ENGINE interface by factory kind and version
    /// (plan_engineapi_vtables C5: what the struct's AcquireInterface slot did).
    /// No slot count comes back (plan_forwarders_everywhere.md phase 6): it is
    /// called through its forwarder table, whose header holds its own.
    /// </summary>
    internal void* AcquireInterface(int type, byte* interfaceVersion, byte* error, int errorBytes)
        => _vt->AcquireInterface(_self, type, interfaceVersion, error, errorBytes);

    /// <summary>
    /// A shim interface or forwarder table this side serves, or null for a
    /// name this side does not serve.
    /// </summary>
    internal void* Acquire(string interfaceVersion)
    {
        byte[] name = Vtable.Utf8(interfaceVersion);

        fixed (byte* p = name)
            return _vt->Acquire(_self, p);
    }
}
