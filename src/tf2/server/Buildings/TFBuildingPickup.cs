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
using TF2.Shared.Econ;
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
///
/// <para>
/// The Rescue Ranger's arm (<c>building_teleporting_pickup</c>,
/// tf_player_shared.cpp:12481-12507 and :12622-12647): the held item's worth
/// is the metal the far lift pays and its reach <see cref="RescueMaxRange"/>,
/// with the deadzone between the plain reach and 250 refusing both. Stock's
/// teleport is the complex control-point dispatch (:12640, the rail's point
/// the building); here the plain position form carries both records, the
/// rail's at the engineer, as the orb's trail waits with the complex arm.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFBuildingPickup)])]
[Precache(PrecacheKind.ParticleSystem, RescueRailRed)]
[Precache(PrecacheKind.ParticleSystem, RescueRailBlue)]
[Precache(PrecacheKind.ParticleSystem, RescueTeleportRed)]
[Precache(PrecacheKind.ParticleSystem, RescueTeleportBlue)]
[Precache(PrecacheKind.Sound, RescueSendSound)]
[Precache(PrecacheKind.Sound, RescueReceiveSound)]
public sealed class TFBuildingPickup(ITFMapPlayers players, ITFBuildings buildings,
                                     IEngineEntities entities, IEngineWorldQuery world,
                                     ITFBuilderPlacement builder, ITeamRoundState rounds,
                                     TFPlayerCarriers carriers, AttributeHooks hooks,
                                     ITFWeaponOwners owners, ITFAmmoPurse purse,
                                     ITempEnts temps, IEnginePrecache precache,
                                     IEngineSound sound)
    : ITFBuildingPickup, IPlayerDeathHook, IOrderedHook
{
    /// <summary>A stage of its own in TF2's thousand.</summary>
    public const int Order = 1_000_000 - 1_000 + 932;

    /// <summary><c>building_teleporting_pickup</c>, the rescue arm's ask (:12481, :12553).</summary>
    private const string RescueAttribute = "building_teleporting_pickup";

    /// <summary>The rescue's far tell, per team (:12635, :12636).</summary>
    private const string RescueRailRed = "dxhr_sniper_rail_red";
    private const string RescueRailBlue = "dxhr_sniper_rail_blue";
    private const string RescueTeleportRed = "teleported_red";
    private const string RescueTeleportBlue = "teleported_blue";

    /// <summary>The far lift's pair (:12644, :12645), the send on the building.</summary>
    private const string RescueSendSound = "Building_Teleporter.Send";
    private const string RescueReceiveSound = "Building_Teleporter.Receive";

    /// <summary><c>TF_BUILDING_RESCUE_MIN_RANGE_SQ</c> (tf_player_shared.cpp:218): 250 squared, the deadzone's far edge.</summary>
    public const float RescueMinRangeSq = 62500f;

    /// <summary><c>TF_BUILDING_RESCUE_MAX_RANGE</c> (tf_player_shared.cpp:219): the held rescue's reach.</summary>
    public const float RescueMaxRange = 5500f;

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
        // first (:12591-12620), the closest one the ray reaches winning —
        // the held rescue's reach when its arm is on (:12553-12557).
        int rescue = RescueCost(player);
        float reach = rescue != 0 ? RescueMaxRange : PickupRange;
        Vector eye = entities.GetEyePosition(player);
        Vector forward = Angles.Forward(entities.GetEyeAngles(player));
        Vector origin = entities.GetOrigin(player);
        TFBaseObject? picked = null;
        float bestSq = reach * reach;

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

        if (!CanLift(player, picked, rescue))
        {
            // Player.UseDeny (:12622-12631); the sound is the sapper lane's.
            return false;
        }

        // The far lift's fare and its tell (:12622-12646), past the deadzone
        // only: the worth's metal, the teleport's puff on the building and
        // the rail from the engineer, the send on the building and the
        // receive on him.
        float liftedSq = (entities.GetOrigin(picked.Handle) - entities.GetEyePosition(player)).LengthSquared;
        if (rescue != 0 && liftedSq > RescueMinRangeSq)
            RescueTell(player, picked, rescue);

        picked.MakeCarriedObject(player);

        // The builder comes out with the building in its arms —
        // CTFPlayerSharedUtils::GetBuilderForObjectType and the Weapon_Switch
        // (tf_player_shared.cpp:12668-12677); the deploy's carried branch
        // (tf_weapon_builder.cpp:1003-1007) does the rest.
        builder.ArmBuilder(player.EntryIndex);
        return true;
    }

    public bool CanPickup(EntityHandle player, TFBaseObject candidate)
        => CanLift(player, candidate, RescueCost(player));

    private bool CanLift(EntityHandle player, TFBaseObject candidate, int rescue)
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
        if (rounds.State is not (RoundState.RoundRunning or RoundState.Stalemate or RoundState.BetweenRounds))
            return false;

        // The reach (:12493-12511): the plain lift inside the 150; the held
        // rescue's arm instead — the deadzone between the plain reach and
        // 250 refuses both, and past 250 the purse must hold the worth.
        Vector eye = entities.GetEyePosition(player);
        float distSq = (entities.GetOrigin(candidate.Handle) - eye).LengthSquared;
        float nearSq = PickupRange * PickupRange;
        if (rescue == 0)
            return distSq <= nearSq;

        // False on the deadzone (:12504-12506).
        if (distSq > nearSq && distSq < RescueMinRangeSq)
            return false;

        // The far lift's metal (:12507-12508).
        return distSq < RescueMinRangeSq
               || purse.Pools(player).GetValueOrDefault(TFAmmoNames.Name(TFAmmo.Metal)) >= rescue;
    }

    /// <summary>
    /// The held weapon's <c>building_teleporting_pickup</c>
    /// (:12551-12553, the active item through CALL_ATTRIB_HOOK_INT_ON_OTHER):
    /// 0 when no rescue is out, else the metal the far lift costs.
    /// </summary>
    private int RescueCost(EntityHandle player)
        => owners.ActiveItemOf(player.EntryIndex) is { } item
            ? hooks.Int(0, RescueAttribute, item)
            : 0;

    /// <summary>The far lift's fare and tell (:12622-12646).</summary>
    private void RescueTell(EntityHandle player, TFBaseObject picked, int fare)
    {
        purse.TakeAmmo(player, fare, TFAmmoNames.Name(TFAmmo.Metal));

        bool red = (TFTeam)players.Team(player) != TFTeam.Blue;

        Vector at = entities.GetOrigin(picked.Handle);
        var gone = new TeEffectData
        {
            Origin = at,
            Start = at,
            HitBox = precache.ParticleSystemIndex(red ? RescueTeleportRed : RescueTeleportBlue),
        };
        temps.DispatchEffect(TeFilters.Pvs(at), 0f, "ParticleEffect", in gone);

        // The rail from the engineer; the complex dispatch's control point on
        // the building waits with that form, as the orb's trail does.
        Vector from = entities.GetOrigin(player);
        var beam = new TeEffectData
        {
            Origin = from,
            Start = from,
            HitBox = precache.ParticleSystemIndex(red ? RescueRailRed : RescueRailBlue),
        };
        temps.DispatchEffect(TeFilters.Pvs(from), 0f, "ParticleEffect", in beam);

        sound.EntityEmitSound(picked.Handle, RescueSendSound);
        sound.EntityEmitSound(player, RescueReceiveSound);
    }

    /// <summary><c>CTFPlayerShared::IsLoser</c> (tf_player_shared.cpp:13673): the round is won and not by the player's team.</summary>
    private bool Loser(EntityHandle player)
        => rounds.State == RoundState.TeamWin
           && rounds.WinningTeam != (TFTeam)players.Team(player);

    /// <summary>The loser-state or control stun of <c>CanPickupBuilding</c>: the carrier's
    /// words where it has them, the condition word alone otherwise.</summary>
    private bool Stunned(EntityHandle player)
        => carriers.Get(player.EntryIndex) is { } state
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
