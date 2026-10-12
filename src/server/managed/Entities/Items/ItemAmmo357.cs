using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

[ScriptedEntity(EntityNames.ItemAmmo357, ScriptedEntityKind.Item)]
public sealed class ItemAmmo357 : AmmoItem
{
    public ItemAmmo357(IEnginePlayers players)
        : base(players)
    {
    }

    protected override string Model => "models/items/357ammo.mdl";
    protected override string AmmoName => "357";
    protected override int AmmoCount => 6;
}
