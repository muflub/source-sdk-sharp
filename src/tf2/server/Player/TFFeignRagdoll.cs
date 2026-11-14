//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// The Dead Ringer's fake corpse's dress — stock's
/// <c>CTFPlayer::CreateFeignDeathRagdoll</c> (tf_player.cpp:15795-15894), the
/// second body a spy's death can leave. The death hooks'
/// (<see cref="IPlayerDeathHook"/>) walk does not run for a feign, because
/// the fake death is not a death: <c>FeignDeath</c> makes the body itself
/// (:15782) and dresses it there (:15870-15879), so the spy's arm asks this
/// on the same call (<see cref="TFSpyComponent.FeignDeathHit"/>), with the
/// answers stock computes before the strip — the disguise
/// (<see cref="TFSpyComponent.FeignDeathHit"/> reads it off the carrier
/// before the feign takes the burn off him, tf_player.cpp:15713).
/// </summary>
[SideService(Side.Server)]
public sealed class TFFeignRagdoll(IEntityDirectory directory, IEnginePlayers engine, TFPlayerGib gibs)
{
    /// <summary>
    /// Dresses the body a feign left, as :15816-15817 and :15869-15879 do: the
    /// corpse wears the disguise's team and class when the spy died disguised
    /// as a teammate — the <paramref name="disguised"/> arm, stock's
    /// <c>bDisguised</c> gate (:15713) — else the team he died on and the
    /// class he died as (:15877-15878), the pair <see cref="TFRagdollTeam"/>
    /// stamps a real death's body with; the feign's own corpse is not that
    /// hook's. It burns as the feign's own answer says (<paramref name="burning"/>,
    /// :15817, stock's bBurning :15716, taken before the strip) and arrives in
    /// pieces when the triggering hit would have gibbed a real death (:15816,
    /// <see cref="TFPlayerGib.LastHitGibs"/>). The client picks the body's
    /// skin and pose off the pair (c_tf_player.cpp:712-719), so an undressed
    /// fake corpse stands as the model's default.
    /// </summary>
    public void Dress(EntityHandle player, EntityHandle body, TFPlayerNetState state, bool disguised, bool burning)
    {
        if (body == EntityHandle.None || directory.Get(body) is not SsRagdoll ragdoll)
            return;

        ragdoll.State.Gib = gibs.LastHitGibs(player);                      // :15816

        ragdoll.State.Burning = burning;                                    // :15817

        if (disguised)                                                      // :15870-15874
        {
            ragdoll.State.Team = (byte)state.DisguiseTeam;
            ragdoll.State.Class = (byte)state.DisguiseClass;
        }
        else                                                                // :15875-15879
        {
            ragdoll.State.Team = (byte)engine.GetTeam(player);
            ragdoll.State.Class = (byte)state.PlayerClass;
        }
    }
}
