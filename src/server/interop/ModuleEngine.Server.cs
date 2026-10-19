namespace SourceSharp.Abi;

/// <summary>
/// The server's half of <see cref="ModuleEngine"/> (plan_shim_areas_split
/// phase 7): what only the server's composition asks of its engine.
/// </summary>
public sealed partial class ModuleEngine
{
    /// <summary>
    /// This engine's entity factory table, its providers bound to the native
    /// dictionary when the side has one (the server, with its shims attached)
    /// and unbound otherwise. A new instance per call: the server's composition
    /// asks once and registers it (plan_abi_hotreload D7).
    /// </summary>
    public EntityFactories EntityFactories() => Api.BindEntityFactories();
}
