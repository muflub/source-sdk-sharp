using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

[ScriptedEntity(EntityNames.ItemRpgRound, ScriptedEntityKind.Item)]
public sealed class ItemRpgRound : AmmoItem
{
    public ItemRpgRound(IEnginePlayers players, IEngineWorldQuery world, PlacedObjects placed)
        : base(players, world, placed)
    {
    }

    protected override string Model => "models/weapons/w_missile_closed.mdl";
    protected override string AmmoName => "RPG_Round";
    protected override int AmmoCount => 1;
}
