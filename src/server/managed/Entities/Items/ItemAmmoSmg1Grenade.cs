using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

[ScriptedEntity(EntityNames.ItemAmmoSmg1Grenade, ScriptedEntityKind.Item)]
public sealed class ItemAmmoSmg1Grenade : AmmoItem
{
    public ItemAmmoSmg1Grenade(IEnginePlayers players)
        : base(players)
    {
    }

    protected override string Model => "models/items/ar2_grenade.mdl";
    protected override string AmmoName => "SMG1_Grenade";
    protected override int AmmoCount => 1;
}
