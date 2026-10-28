using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

[ScriptedEntity(EntityNames.ItemBattery, ScriptedEntityKind.Item)]
public sealed class ItemBattery(IUserMessageSender messages, IEngineWorldQuery world, PlacedObjects placed) : PickupItem(world, placed)
{
    protected override string Model => "models/items/battery.mdl";
    protected override string Describe() => "a battery";

    protected override bool TryGive(EntityHandle player)
        // The armour is the player's managed state (PlayerNetState.Armour); the
        // native player has none since CBasePlayer retired.
        => FindByHandle<SsPlayer>(player) is { } p && TopUp(p.Armour, 15, SsPlayer.MaxArmour, a => p.Armour = a)
           && Reported(player);

    // ApplyBattery's ItemPickup: the literal "item_battery", after the charge.
    private bool Reported(EntityHandle player)
    {
        ReportPickup(messages, player, EntityNames.ItemBattery);
        return true;
    }
}
