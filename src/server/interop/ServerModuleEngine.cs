namespace SourceSharp.Abi;

/// <summary>
/// The server's <see cref="ModuleEngine"/> (plan_engineside_removal Phase 1):
/// what <c>ServerModule</c> is composed over. Its own type, so the server's
/// root takes it by constructor and cannot be handed the client's engine.
/// Besides the shared surface, it holds what only the server's composition
/// asks of its engine.
/// </summary>
public sealed class ServerModuleEngine : ModuleEngine
{
    internal ServerModuleEngine(EngineSide api)
        : base(api)
    {
    }

    /// <summary>
    /// This engine's entity factory table, its providers bound to the native
    /// dictionary when the side has one (the server, with its shims attached)
    /// and unbound otherwise. A new instance per call: the server's composition
    /// asks once and registers it (plan_abi_hotreload D7).
    /// </summary>
    public EntityFactories EntityFactories() => Api.BindEntityFactories(Attachment.Areas.Require<IEntityLifecycleArea>());
}
