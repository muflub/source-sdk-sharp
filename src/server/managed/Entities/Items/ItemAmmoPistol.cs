using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

//-----------------------------------------------------------------------------
// Ammo. Counts are the stock HL2 ones, so a map's placement stays balanced.
//-----------------------------------------------------------------------------

[ScriptedEntity(EntityNames.ItemAmmoPistol, ScriptedEntityKind.Item)]
public sealed class ItemAmmoPistol : AmmoItem
{
    public ItemAmmoPistol(IEnginePlayers players, IEngineWorldQuery world, PlacedObjects placed)
        : base(players, world, placed)
    {
    }

    protected override string Model => "models/items/boxsrounds.mdl";
    protected override string AmmoName => "Pistol";
    protected override int AmmoCount => 20;
}
