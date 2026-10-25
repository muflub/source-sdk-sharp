namespace SourceSharp.Abi;

/// <summary>
/// Binds the server side's IVEngineServer (plan_engine_forwarders.md, one
/// library per side): installs <see cref="EngineServer"/>'s acquirer in the
/// side's shared bound slot, which the shared bindings ask through
/// <c>EngineBound.Server</c> and which cannot name the server's wrapper itself.
/// The server's side entry calls it as the side is created.
/// </summary>
internal static class EngineServerBinding
{
    internal static void Bind(SideAttachment side)
    {
        ArgumentNullException.ThrowIfNull(side);
        side.Bound.UseServer((NativeInterfaceFactory factory, Side _, out string why) => EngineServer.Acquire(factory, out why));
    }
}
