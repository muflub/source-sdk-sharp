using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

[ScriptedEntity(EntityNames.ItemAmmo357Large, ScriptedEntityKind.Item)]
public sealed class ItemAmmo357Large : AmmoItem
{
    public ItemAmmo357Large(IEnginePlayers players, IEngineWorldQuery world)
        : base(players, world)
    {
    }

    protected override string Model => "models/items/357ammobox.mdl";
    protected override string AmmoName => "357";
    protected override int AmmoCount => 12;
}
