using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

[ScriptedEntity(EntityNames.ItemAmmoSmg1Large, ScriptedEntityKind.Item)]
public sealed class ItemAmmoSmg1Large : AmmoItem
{
    public ItemAmmoSmg1Large(IEnginePlayers players, IEngineWorldQuery world, PlacedObjects placed)
        : base(players, world, placed)
    {
    }

    protected override string Model => "models/items/boxmrounds.mdl";
    protected override string AmmoName => "SMG1";
    protected override int AmmoCount => 225;
}
