using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <inheritdoc cref="ItemHealthCharger"/>
[ScriptedEntity(EntityNames.ItemSuitcharger, ScriptedEntityKind.Item)]
public sealed class ItemSuitCharger : PickupItem
{
    protected override string Model => "models/props_combine/suit_charger001.mdl";
    protected override string Describe() => "a suit charger";
    protected override float RespawnDelay => 60f;

    protected override bool TryGive(EntityHandle player)
        // The armour is the player's managed state (PlayerNetState.Armour); the
        // native player has none since CBasePlayer retired.
        => FindByHandle<SsPlayer>(player) is { } p && TopUp(p.Armour, 50, SsPlayer.MaxArmour, a => p.Armour = a);
}
