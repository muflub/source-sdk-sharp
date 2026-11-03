using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// The <c>tf_player_data</c> carrier: one per player, holding
/// <see cref="TFPlayerNetState"/> on the hierarchy beside the core's player
/// entity, the way stock networks <c>tf_player_resource</c> beside its
/// players. Created and removed by <see cref="TFPlayerCarrier"/>, never placed
/// by a map.
///
/// <para>
/// Effect kind (no model, no collision) and ALWAYS TRANSMITTED: every client
/// needs every player's class and conditions (the HUD, effects), and the
/// carrier sits wherever it was made, so a PVS check would deliver it only to
/// players who can see that spot -- PlayerResource's reason, the same flag.
/// </para>
/// </summary>
[ScriptedEntity(TFEntityNames.PlayerData, ScriptedEntityKind.Effect)]
public sealed class TFPlayerDataEntity : ScriptedEntity
{
    private readonly IEngineEntityRender _render;

    public TFPlayerDataEntity(IEngineEntityRender render)
    {
        _render = render;
    }

    /// <summary>The player's networked, predicted TF state.</summary>
    [NetState] public TFPlayerNetState State = new();

    public override void OnSpawn() => _render.SetTransmitAlways(Handle, true);
}
