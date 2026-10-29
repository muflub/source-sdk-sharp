using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

[ScriptedEntity(EntityNames.ItemHealthvial, ScriptedEntityKind.Item)]
public sealed class ItemHealthvial(IUserMessageSender messages, IEngineWorldQuery world, PlacedObjects placed) : PickupItem(world, placed)
{
    protected override string Model => "models/healthvial.mdl";
    protected override string Describe() => "a health vial";
    protected override float SizeZ => 16f;

    protected override bool TryGive(EntityHandle player)
        => TopUp(Entities.GetHealth(player), 10, 100, h => Entities.SetHealth(player, h))
           && Reported(player);

    // MyTouch's ItemPickup: the item's own classname, after the health.
    private bool Reported(EntityHandle player)
    {
        ReportPickup(messages, player, EntityNames.ItemHealthvial);
        return true;
    }
}
