using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

[ScriptedEntity(EntityNames.ItemAmmoSmg1, ScriptedEntityKind.Item)]
public sealed class ItemAmmoSmg1 : AmmoItem
{
    public ItemAmmoSmg1(IEnginePlayers players)
        : base(players)
    {
    }

    protected override string Model => "models/items/boxmrounds.mdl";
    protected override string AmmoName => "SMG1";
    protected override int AmmoCount => 45;
}
