using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

//-----------------------------------------------------------------------------
// Health and armour.
//-----------------------------------------------------------------------------

[ScriptedEntity(EntityNames.ItemHealthkit, ScriptedEntityKind.Item)]
public sealed class ItemHealthkit(IUserMessageSender messages, IEngineWorldQuery world) : PickupItem(world)
{
    protected override string Model => "models/items/healthkit.mdl";
    protected override string Describe() => "a medkit";

    protected override bool TryGive(EntityHandle player)
        => TopUp(Entities.GetHealth(player), 25, 100, h => Entities.SetHealth(player, h))
           && Reported(player);

    // MyTouch's ItemPickup: the item's own classname, after the health.
    private bool Reported(EntityHandle player)
    {
        ReportPickup(messages, player, EntityNames.ItemHealthkit);
        return true;
    }
}
