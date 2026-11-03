//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Player;
using TF2.Shared.Rules;

namespace TF2.Server.Buildings;

/// <summary>
/// The engineer's +attack2 lift (<c>CTFPlayer::CanPickupBuilding</c> and
/// <c>CTFPlayer::TryToPickupBuilding</c>,
/// <c>tf_player_shared.cpp:12440-12692</c>): one of his own standing
/// buildables, in front of him and within reach, taken into the arms — and
/// the death that destroys it there (tf_player.cpp:11901-11907).
/// </summary>
public interface ITFBuildingPickup
{
    /// <summary><c>CanPickupBuilding</c>: the gates on one candidate.</summary>
    bool CanPickup(EntityHandle player, TFBaseObject candidate);

    /// <summary><c>TryToPickupBuilding</c>: find the building in front and lift it.</summary>
    bool TryPickup(EntityHandle player);

    /// <summary><c>m_Shared.GetCarriedObject</c>: what the player carries, if anything.</summary>
    TFBaseObject? Carried(EntityHandle player);
}

/// <summary>
/// The lift itself: the owned buildings walked nearest-first, the eye ray
/// that must reach one (<c>EnumerateEntities</c> over the pickup ray,
/// tf_player_shared.cpp:12578-12620), <see cref="CanPickup"/>'s gates, and
/// the carried state the redeploy arms.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFBuildingPickup)])]
public sealed class TFBuildingPickup(ITFMapPlayers players, ITFBuildings buildings,
                                     IEngineEntities entities, IEngineWorldQuery world,
                                     ITFBuilderPlacement? builder = null, ITeamRoundState? rounds = null,
                                     TFPlayerCarriers? carriers = null)
    : ITFBuildingPickup, IPlayerDeathHook, IOrderedHook
{
    /// <summary>A stage of its own in TF2's thousand.</summary>
    public const int Order = 1_000_000 - 1_000 + 932;

    /// <summary><c>TF_BUILDING_PICKUP_RANGE</c> (tf_player_shared.cpp:217): 150.</summary>
    public const float PickupRange = 150f;

    /// <summary>
    /// How far off the eye ray a building's centre may sit. Stock asks the
    /// ray of the building's solid box; ours are non-solid, so the reach is
    /// a building's half width standing in for its bounds.
    /// </summary>
    public const float PickupRadius = 48f;

    public int HookOrder => Order;

    public TFBaseObject? Carried(EntityHandle player)
        => buildings.OwnedBy(player).FirstOrDefault(b => b.IsCarried && !b.IsDead);

    public bool TryPickup(EntityHandle player)
    {
        // m_Shared.IsCarryingObject (tf_player_shared.cpp:12538): the arms
        // hold one building at a time.
        if (Carried(player) is not null)
            return false;

        // The condition gates of :12541-12551, plus the loser state of
        // CanPickupBuilding's own list (:12470 — state TEAM_WIN and the
        // other team the winner, CTFPlayerShared::IsLoser, :13673).
        if (players.InCond(player, TFCond.Taunting) || players.InCond(player, TFCond.HalloweenTiny)
            || players.InCond(player, TFCond.MeleeOnly) || players.InCond(player, TFCond.SwimmingCurse)
            || Loser(player))
            return false;

        // The eye ray (:12566-12579) and the owned buildings walked nearest
        // first (:12591-12620), the closest one the ray reaches winning.
        Vector eye = entities.GetEyePosition(player);
        Vector forward = Angles.Forward(entities.GetEyeAngles(player));
        Vector origin = entities.GetOrigin(player);
        TFBaseObject? picked = null;
        float bestSq = PickupRange * PickupRange;

        foreach (TFBaseObject owned in buildings.OwnedBy(player))
        {
            Vector toObject = entities.GetOrigin(owned.Handle) - origin;
            float distSq = toObject.LengthSquared;
            if (distSq > bestSq)
                continue;

            // In front of the eye and near its ray — stock's enumerate over
            // the ray, whose stand-in is this geometry (see PickupRadius).
            float along = toObject.X * forward.X + toObject.Y * forward.Y + toObject.Z * forward.Z;
            if (along <= 0f)
                continue;

            Vector offRay = toObject - forward * along;
            if (offRay.LengthSquared > PickupRadius * PickupRadius)
                continue;

            // The world before the building breaks the reach (:12615-12618):
            // our buildings are non-solid, so the trace sees only the world.
            Vector center = entities.GetWorldSpaceCenter(owned.Handle);
            TraceResult wall = world.TraceLine(eye, center, TraceMask.Solid, player);
            if (wall.Hit && wall.Entity == EntityHandle.None)
                continue;

            picked = owned;
            bestSq = distSq;
        }

        if (picked is null)
            return false;

        if (!CanPickup(player, picked))
        {
            // Player.UseDeny (:12622-12631); the sound is the sapper lane's.
            return false;
        }

        picked.MakeCarriedObject(player);

        // The builder comes out with the building in its arms —
        // CTFPlayerSharedUtils::GetBuilderForObjectType and the Weapon_Switch
        // (tf_player_shared.cpp:12668-12677); the deploy's carried branch
        // (tf_weapon_builder.cpp:1003-1007) does the rest.
        builder?.ArmBuilder(player.EntryIndex);
        return true;
    }

    public bool CanPickup(EntityHandle player, TFBaseObject candidate)
    {
        if (candidate is null || candidate.IsDead || candidate.IsCarried)
            return false;

        // IsBuilding (:12445): still under construction.
        if (candidate.IsBuilding)
            return false;

        // HasSapper (:12451) and IsPlasmaDisabled (:12454).
        if (candidate.HasSapper || candidate.IsPlasmaDisabled)
            return false;

        // The level gate of :12458 rides with the upgrade milestone; both
        // levels are one today.

        if (!players.IsAlive(player))
            return false;

        // Already carrying (:12464) and the two stuns (:12467) —
        // IsLoserStateStunned || IsControlStunned (tf_player_shared.cpp:9952,
        // :9966): the active stun entry, the stunned condition and the
        // loser-state or controls bit; a movement-only stun leaves the hands
        // free. Without a carrier the condition word answers alone, as the
        // capture rule reads its player (TFCapturePlayerRules).
        if (Carried(player) is not null || Stunned(player))
            return false;

        // RND_RUNNING, STALEMATE, BETWEEN_RNDS (:12473).
        if (rounds is { } state
            && state.State is not (RoundState.RoundRunning or RoundState.Stalemate or RoundState.BetweenRounds))
            return false;

        // Within reach of the eye (:12494-12511).
        Vector eye = entities.GetEyePosition(player);
        Vector toObject = entities.GetOrigin(candidate.Handle) - eye;
        return toObject.LengthSquared <= PickupRange * PickupRange;
    }

    /// <summary><c>CTFPlayerShared::IsLoser</c> (tf_player_shared.cpp:13673): the round is won and not by the player's team.</summary>
    private bool Loser(EntityHandle player)
        => rounds is { } state && state.State == RoundState.TeamWin
           && state.WinningTeam != (TFTeam)players.Team(player);

    /// <summary>The loser-state or control stun of <c>CanPickupBuilding</c>: the carrier's
    /// words where it has them, the condition word alone otherwise.</summary>
    private bool Stunned(EntityHandle player)
        => carriers?.Get(player.EntryIndex) is { } state
            ? state.StunIndex >= 0
              && state.InCond(TFCond.Stunned)
              && ((TFStunFlags)state.StunFlags & (TFStunFlags.Controls | TFStunFlags.LoserState)) != 0
            : players.InCond(player, TFCond.Stunned);

    /// <summary>The death of a carrying engineer destroys the building in his arms (tf_player.cpp:11901-11907).</summary>
    void IPlayerDeathHook.OnPlayerDied(PlayerDeath death)
    {
        if (Carried(death.Player) is { } carried)
            carried.DestroyObject();
    }
}
