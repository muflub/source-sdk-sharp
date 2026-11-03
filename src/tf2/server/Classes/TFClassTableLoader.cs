using SourceSharp.Abi;

using TF2.Shared;

namespace TF2.Server;

/// <summary>
/// Loads the server's <see cref="TFClassTable"/> as the first level loads,
/// before its entities exist (stock loads it at the game system's Init; a
/// level is the first point this side is sure to have its GAME paths).
/// </summary>
[SideService(Side.Server)]
public sealed class TFClassTableLoader(TFClassTable table) : IGameLifecycle
{
    /// <inheritdoc cref="TFClassTable.LoadOrder"/>
    public int LifecycleOrder => TFClassTable.LoadOrder;

    void IGameLifecycle.OnLevelInitPreEntity(string mapName) => table.Load();
}
