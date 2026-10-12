using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A wall charger. Modelled as a large-capacity pickup rather than the stock
/// hold-to-drain behaviour: it tops the player up on contact and takes a long
/// time to come back.
///
/// It was written this way because the mod had no use key at all. It has one
/// now -- the brush and prop shells claim FCAP_IMPULSE_USE and route it to the
/// "Use" input -- so draining on hold is available to be written; this stays a
/// pickup until someone decides the drain is worth the tick.
/// </summary>
[ScriptedEntity(EntityNames.ItemHealthcharger, ScriptedEntityKind.Item)]
public sealed class ItemHealthCharger : PickupItem
{
    protected override string Model => "models/props_combine/health_charger001.mdl";
    protected override string Describe() => "a health charger";
    protected override float RespawnDelay => 60f;

    protected override bool TryGive(EntityHandle player)
        => TopUp(Entities.GetHealth(player), 50, 100, h => Entities.SetHealth(player, h));
}
