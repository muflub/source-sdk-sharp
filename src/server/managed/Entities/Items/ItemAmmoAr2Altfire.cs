using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

[ScriptedEntity(EntityNames.ItemAmmoAr2Altfire, ScriptedEntityKind.Item)]
public sealed class ItemAmmoAr2Altfire : AmmoItem
{
    public ItemAmmoAr2Altfire(IEnginePlayers players, IEngineWorldQuery world, PlacedObjects placed)
        : base(players, world, placed)
    {
    }

    protected override string Model => "models/items/combine_rifle_ammo01.mdl";
    protected override string AmmoName => "AR2AltFire";
    protected override int AmmoCount => 1;
}
