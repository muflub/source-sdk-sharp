using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

[ScriptedEntity(EntityNames.ItemAmmoPistolLarge, ScriptedEntityKind.Item)]
public sealed class ItemAmmoPistolLarge : AmmoItem
{
    public ItemAmmoPistolLarge(IEnginePlayers players)
        : base(players)
    {
    }

    protected override string Model => "models/items/boxsrounds.mdl";
    protected override string AmmoName => "Pistol";
    protected override int AmmoCount => 100;
}
