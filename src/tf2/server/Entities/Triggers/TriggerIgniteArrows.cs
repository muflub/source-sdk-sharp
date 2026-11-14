//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using TF2.Server.Entities.Items;
using TF2.Shared;

namespace TF2.Server.Entities.Triggers;

/// <summary>
/// trigger_ignite_arrows (stock's <c>CTriggerIgniteArrows</c>,
/// tf_triggers.cpp:173-232): a brush trigger that lights the arrow of the
/// CompoundBow a Sniper is holding while he crosses it and looks at the
/// trigger's origin — stock's <c>Touch</c> (:202-231) answers in order: the
/// trigger filters (:204, held by the volume's own gate), a player (:207),
/// the Sniper class (:213), the eye-forward against the eye-to-origin
/// direction at <c>0.95</c> (:216-223, <c>EyePositionAndVectors</c> and the
/// dot of :221-222), then the held bow (:226-227) and its
/// <c>SetArrowAlight( true )</c> (:230). The bow state is the same
/// <see cref="SsWeapon.State"/> the Huntsman's flame lights
/// (<c>TFFlameManager.LightTeammatesBow</c>), so the arrow the bow next
/// fires rides the one flag.
/// </summary>
[ScriptedEntity(MapClass, ScriptedEntityKind.Brush)]
public sealed class TriggerIgniteArrows(IGameClock clock, IEngineEntities entities, IEntityDirectory directory,
                                        ITFMapPlayers players, IEnginePlayers enginePlayers)
    : TFPlayerTrigger(clock)
{
    public const string MapClass = "trigger_ignite_arrows";

    /// <summary>Stock's look-at threshold, the <c>0.95</c> of <c>:222</c>.</summary>
    public const float LookAtDot = 0.95f;

    /// <summary>
    /// <c>CTriggerIgniteArrows::Touch</c> (:202-231), once per entry through
    /// the volume. The order is stock's: player, class, the look-at dot, then
    /// the held bow's arrow lit.
    /// </summary>
    protected override void StartTouch(EntityHandle toucher)
    {
        // !pOther->IsPlayer() (:207-208).
        if (!entities.IsPlayer(toucher))
            return;

        // Ignore non-snipers (:212-214).
        if (players.ClassOf(toucher) != TFClass.Sniper)
            return;

        // Make sure they're looking at the origin (:216-223): eye position and
        // forward from the player, the origin of this trigger, and the dot of
        // the two directions. Stock's <c>fDotPr &lt; 0.95</c> leaves early.
        Vector eye = entities.GetEyePosition(toucher);
        Vector forward = Angles.Forward(entities.GetEyeAngles(toucher));
        Vector toOrigin = (entities.GetOrigin(Handle) - eye).Normalized;
        if (forward.Dot(toOrigin) < LookAtDot)
            return;

        // Does he have the bow? (:225-231): the held weapon's id, then the
        // carried state's arrow lit.
        if (TFWeaponNames.IdOf(enginePlayers.GetActiveWeapon(toucher)) != TFWeaponId.CompoundBow)
            return;

        if (directory.Get(enginePlayers.GetActiveWeaponEntity(toucher)) is SsWeapon { State: { } state })
            state.ArrowAlight = true;
    }
}
