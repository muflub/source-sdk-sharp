using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

[ScriptedEntity(EntityNames.ItemBoxBuckshot, ScriptedEntityKind.Item)]
public sealed class ItemBoxBuckshot : AmmoItem
{
    public ItemBoxBuckshot(IEnginePlayers players, IEngineWorldQuery world, PlacedObjects placed)
        : base(players, world, placed)
    {
    }

    protected override string Model => "models/items/boxbuckshot.mdl";
    protected override string AmmoName => "Buckshot";
    protected override int AmmoCount => 20;
}
