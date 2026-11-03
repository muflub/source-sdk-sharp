using SourceSharp.Abi;

using TF2.Shared.Econ;

namespace TF2.Server.Econ;

/// <summary>
/// Loads the item schema when the server's first level starts, so its cost
/// and its counts land in the log at a moment a reader expects them rather
/// than at whichever lookup happens to come first. Stock loads it once, in
/// <c>CEconItemSystem::Init</c> (econ_item_system.cpp), not per level; the
/// schema answers every later level from that one load.
/// </summary>
[SideService(Side.Server)]
public sealed class ItemSchemaLevelLoad(ItemSchema schema) : IGameLifecycle
{
    /// <summary>TF2's stage range (999,000-999,999: below the mods' ModStages.First, as TFClassTable.LoadOrder).</summary>
    public const int Order = 1_000_000 - 1_000 + 40;

    public int LifecycleOrder => Order;

    public void OnLevelInitPreEntity(string mapName) => schema.EnsureLoaded();
}
