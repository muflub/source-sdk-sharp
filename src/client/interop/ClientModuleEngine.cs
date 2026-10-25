namespace SourceSharp.Abi;

/// <summary>
/// The client's <see cref="ModuleEngine"/> (plan_engineside_removal Phase 1):
/// what <c>ClientModule</c> is composed over. Its own type, so the client's
/// root takes it by constructor and cannot be handed the server's engine.
/// The client's composition asks nothing of its engine beyond the shared
/// surface.
/// </summary>
public sealed class ClientModuleEngine : ModuleEngine
{
    internal ClientModuleEngine(EngineSide api)
        : base(api)
    {
    }
}
