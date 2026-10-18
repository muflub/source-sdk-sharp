namespace SourceSharp.Abi;

/// <summary>
/// plan_engineapi_vtables.md step A4: <c>IShimConsole</c>, acquired from the
/// shim root by name, and called through its forwarder table
/// (<c>SSFwd_ShimConsole</c>, plan_forwarders_everywhere.md) rather than its
/// vtable.
///
/// <para>
/// The count check is <c>managed &lt;= native</c>, as for the other shim
/// interfaces: their tables only append, so a native side that has grown keeps
/// every slot this struct knows at its index.
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

    private readonly ShimRef<ShimConsoleFwd> _area;

    private ShimConsole(ShimRef<ShimConsoleFwd> area) => _area = area;

    internal static int ExpectedEntries => Forwarders.SlotCount<ShimConsoleFwd>();

    internal static bool TryAcquire(ShimRoot root, out ShimConsole? bound, out string why)
    {
        bound = null;

        if (!ShimRef<ShimConsoleFwd>.TryTake(root, InterfaceVersion, out var area, out why))
            return false;

        bound = new ShimConsole(area);
        return true;
    }

    public void Msg(string text)
    {
        byte[] utf8 = Vtable.Utf8(text);
        fixed (byte* p = utf8)
            _area.Vt->Msg(_area.Self, p);
    }

    public void Warning(string text)
    {
        byte[] utf8 = Vtable.Utf8(text);
        fixed (byte* p = utf8)
            _area.Vt->Warning(_area.Self, p);
    }
}
