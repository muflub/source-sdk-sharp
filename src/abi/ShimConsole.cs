namespace SourceSharp.Abi;

/// <summary>
/// plan_engineapi_vtables.md step A4: <c>IShimConsole</c>, acquired from the
/// shim root by name. The first EngineApi area reachable as a vtable; Phase B
/// makes it the implementation behind the injected console contract, and
/// Phase C moves the rest of the server's slots the same way.
///
/// <para>
/// The span check is <c>managed &lt;= native</c>, as for the other shim
/// interfaces: they are append-only, so a native side that has grown keeps
/// every slot this layout knows at its index.
/// </para>
///
/// <para>
/// It is also <see cref="Log"/>'s sink for its side while no composition is
/// attached (plan_abi_hotreload D5): the bootstrap attaches it as soon as it is
/// acquired, so the lines a load prints before the module composes, and those
/// a reload prints after the old module has shut down, reach the console
/// without going through <c>Engine</c>.
/// </para>
/// </summary>
internal sealed unsafe class ShimConsole : ILog
{
    public const string InterfaceVersion = "SourceSharpConsole001";

    private readonly void* _self;
    private readonly ShimConsoleVtbl* _vt;

    private ShimConsole(void* self, ShimConsoleVtbl* vt)
    {
        _self = self;
        _vt = vt;
    }

    internal static int ExpectedEntries => sizeof(ShimConsoleVtbl) / sizeof(void*);

    internal static bool TryAcquire(ShimRoot root, out ShimConsole? bound, out string why)
    {
        bound = null;
        void* self = root.Acquire(InterfaceVersion, out int entries);

        if (self == null)
        {
            why = $"the shim root does not serve {InterfaceVersion}";
            return false;
        }

        if (ExpectedEntries > entries)
        {
            why = $"{InterfaceVersion} has {entries} slot(s) and the generated ShimConsoleVtbl "
                + $"needs {ExpectedEntries}: ishim_console.h and VtableLayouts.g.cs disagree";
            return false;
        }

        bound = new ShimConsole(self, *(ShimConsoleVtbl**)self);
        why = string.Empty;
        return true;
    }

    public void Msg(string text)
    {
        byte[] utf8 = Vtable.Utf8(text);
        fixed (byte* p = utf8)
            _vt->Msg(_self, p);
    }

    public void Warning(string text)
    {
        byte[] utf8 = Vtable.Utf8(text);
        fixed (byte* p = utf8)
            _vt->Warning(_self, p);
    }
}
