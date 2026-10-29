using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>An item that hands over a quantity of one ammo type.</summary>
public abstract class AmmoItem : PickupItem
{
    public AmmoItem(IEnginePlayers players, IEngineWorldQuery world, PlacedObjects placed)
        : base(world, placed)
    {
        Players = players;
    }

    /// <summary>the players this entity acts on.</summary>
    protected readonly IEnginePlayers Players;
    protected abstract string AmmoName { get; }
    protected abstract int AmmoCount { get; }

    protected override string Describe() => $"{AmmoCount} {AmmoName}";

    // GivePlayerAmmo reports how much was actually taken, which is zero for a
    // player already carrying the maximum — exactly the "nothing to give" case.
    protected override bool TryGive(EntityHandle player)
        => Players.GiveAmmo(player, AmmoCount, AmmoName) > 0;
}
