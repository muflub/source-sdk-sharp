using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

[ScriptedEntity(EntityNames.ItemAmmoAr2, ScriptedEntityKind.Item)]
public sealed class ItemAmmoAr2 : AmmoItem
{
    public ItemAmmoAr2(IEnginePlayers players, IEngineWorldQuery world)
        : base(players, world)
    {
    }

    protected override string Model => "models/items/combine_rifle_cartridge01.mdl";
    protected override string AmmoName => "AR2";
    protected override int AmmoCount => 20;
}
