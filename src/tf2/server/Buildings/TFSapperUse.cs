//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Combat;
using TF2.Server.Entities.Items;
using TF2.Shared;
using TF2.Shared.Combat;

namespace TF2.Server.Buildings;

/// <summary>
/// The sapper's use — the placement and the wrench — as the server's answer
/// to <see cref="ITFSapperState"/>.
///
/// <para>
/// <b>The placement</b> is <c>FindNearestBuildPoint</c>'s hostile arm
/// (tf_obj.cpp:916-1168) over <c>CTFWeaponBuilder::PrimaryAttack</c>'s
/// <c>BS_PLACING</c> attachment arm (tf_weapon_builder.cpp:358-424): of the
/// other team's standing buildings — a placing one or an already-sapped one
/// is passed over, which is the one-sapper-per-building rule
/// (<c>CanBuildObjectOnBuildPoint</c>) — the nearest the spy faces and
/// reaches. Facing is stock's <c>FInViewCone</c>: the flat dot from the eye
/// over the player's 0.766. Reaching is <c>tf_obj_max_attach_dist</c>, 160,
/// measured from the spy's feet. And the line must be clear — stock skips a
/// build point a trace stops on short of the building itself. The build
/// point is the building's centre, where stock reads the object's build
/// point attachment the models do not have yet.
/// </para>
///
/// <para>
/// <b>The pair</b> is <c>PrimaryAttack</c>'s teleporter arm
/// (tf_weapon_builder.cpp:393-409): a sapped entrance's match takes its own
/// sapper too, while it stands unsapped.
/// </para>
///
/// <para>
/// <b>The wrench</b> is <c>CTFWrench::Smack</c> →
/// <c>OnFriendlyBuildingHit</c> → <c>InputWrenchHit</c>
/// (tf_weapon_wrench.cpp:145-199, tf_obj.cpp:2746-2802): the friendly
/// building inside the swing takes the hit, routed as stock routes it — a
/// sapped building's blow goes to its sapper at <c>WRENCH_DMG_VS_SAPPER</c>
/// (65) as <c>DMG_CLUB</c>, the one blow a sapper accepts
/// (<see cref="TFSapper.OnDamaged"/>); anything else is the building's own
/// <c>OnWrenchHit</c> — the repair, the refill, the upgrade metal. Stock
/// sweeps a ±18 hull and lands the hit at the smack; buildings are
/// non-solid here, so the hull stands in as a point distance to the swing's
/// line, and the hit is asked at the swing (the knife's precedent). Stock's
/// pair arm — the match's sapper takes the same blow, guarded by
/// <c>DMG_FROM_OTHER_SAPPER</c> — lives here rather than in the sapper's
/// own gate: the wrench is the only source that reaches a sapper, and a
/// gate-level propagation recurses through <c>TakeDamage</c> with no
/// channel for the guard.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFSapperState)])]
public sealed class TFSapperUse(ITFMapPlayers players, IEnginePlayers enginePlayers, IEngineEntities entities,
                                IEngineWorldQuery world, ITFBuildings buildings, IEngineDamage damage,
                                TFDamageScopes scopes, ITFObjectSpawner spawner, ILog log,
                                ITFBuilderArms? arms = null, ILagCompensation? lagCompensation = null) : ITFSapperState
{
    /// <summary>The player's <c>m_flFieldOfView</c> (player.cpp:5108).</summary>
    public const float FieldOfView = 0.766f;

    /// <summary><c>tf_obj_max_attach_dist</c> (baseobject_shared.cpp:219-227).</summary>
    public const float MaxAttachDistance = 160f;

    /// <summary><c>CTFWrench::Smack</c>'s hull half-width.</summary>
    public const float SwingHull = 18f;

    /// <summary><c>WRENCH_DMG_VS_SAPPER</c> (tf_shareddefs.h).</summary>
    public const float WrenchDmgVsSapper = 65f;

    public bool PlaceSapper(int ownerIndex)
    {
        EntityHandle spy = enginePlayers.GetHandle(ownerIndex);
        if (spy == EntityHandle.None
            || NearestSappable(spy, out Vector point) is not { } builtOn
            || !PlaceOne(spy, builtOn, point))
            return false;

        // The placed sapper leaves the hands for the last weapon
        // (SwitchOwnersWeaponToLast, tf_weapon_builder.cpp:421) — the sapper
        // row carries no OF_ALLOW_REPEAT_PLACEMENT to hold it out.
        arms?.SwitchLast(spy);
        return true;
    }

    /// <summary>
    /// The attachment walk's find (<c>FindSnapToBuildPos</c>'s hostile pass,
    /// tf_obj.cpp:1189-1250): the other teams' standing buildings, the
    /// nearest the view cone, the reach and the sight line allow. A placing
    /// or already-sapped one is passed over — the one sapper per building of
    /// <c>CanBuildObjectOnBuildPoint</c>.
    /// </summary>
    public bool FindSappable(int ownerIndex, out EntityHandle building, out Vector point)
    {
        building = EntityHandle.None;
        point = default;
        if (NearestSappable(enginePlayers.GetHandle(ownerIndex), out Vector found) is not { } best)
            return false;

        building = best.Handle;
        point = found;
        return true;
    }

    /// <summary>The find over the objects themselves, the place's and the walk's shared ask.</summary>
    private TFBaseObject? NearestSappable(EntityHandle spy, out Vector point)
    {
        point = default;
        int team = players.Team(spy);
        Vector eye = enginePlayers.GetShootOrigin(spy);
        Vector aim = Flat(Angles.Vectors(entities.GetEyeAngles(spy)).Forward);
        Vector origin = entities.GetOrigin(spy);

        TFBaseObject? best = null;
        Vector bestPoint = default;
        float bestDistance = MaxAttachDistance;

        foreach (TFBaseObject candidate in buildings.OwnedBy())
        {
            // The hostile attachment looks at the other team's objects only,
            // and never at one being placed or already sapped — the one
            // sapper per building of CanBuildObjectOnBuildPoint.
            if (candidate.Team == team || candidate.IsPlacing || candidate.HasSapper)
                continue;

            Vector centre = entities.GetWorldSpaceCenter(candidate.Handle);

            // FInViewCone: the flat dot from the eye, strictly past it.
            if (Flat(centre - eye).Dot(aim) <= FieldOfView)
                continue;

            // Stock's trace: a build point something stops on short of the
            // building itself is out. (The buildings are non-solid, so the
            // trace names only a wall.)
            TraceResult trace = world.TraceLine(eye, centre, TraceMask.Brushes, spy);
            if (trace.Hit && trace.Entity != candidate.Handle)
                continue;

            // The nearest wins, under tf_obj_max_attach_dist from the feet.
            float distance = (centre - origin).Length;
            if (distance >= bestDistance)
                continue;

            best = candidate;
            bestPoint = centre;
            bestDistance = distance;
        }

        point = bestPoint;
        return best;
    }

    /// <summary>
    /// The sapper's birth at the build point, stock's
    /// <c>UpdateAttachmentPlacement</c> + <c>StartBuilding</c> — and, when
    /// the building is a teleporter whose match stands unsapped, the match
    /// takes its own sapper (tf_weapon_builder.cpp:393-409).
    /// </summary>
    private bool PlaceOne(EntityHandle spy, TFBaseObject building, Vector point)
    {
        if (spawner.Spawn(TFObjectType.AttachmentSapper, point, entities.GetAngles(spy)) is not TFSapper sapper)
        {
            log.Warning($"[TF2/Sapper] the world refused to create {TFSapper.MapClass}\n");
            return false;
        }

        sapper.StartPlacement(spy);
        sapper.AttachTo(building);
        sapper.StartBuilding();
        sapper.OnThink();   // the instant build: the sapper rides

        // The console's word, the built line's pair: the building reads
        // sapped the moment the leech rides.
        log.Msg($"[TF2/Build] {TFObjectClassNames.Raw(building.Type)} sapped\n");

        if (building is TFTeleporter tp && tp.Match is { } match && !match.HasSapper)
            PlaceOne(spy, match, entities.GetWorldSpaceCenter(match.Handle));

        return true;
    }

    public bool WrenchSmack(int ownerIndex, float range)
    {
        EntityHandle player = enginePlayers.GetHandle(ownerIndex);
        if (player == EntityHandle.None)
            return false;

        // The wrench's session (tf_weapon_wrench.cpp:497, :521): the swing's
        // trace and the building pick see the other players where the
        // engineer saw them.
        lagCompensation?.Start(player);
        try
        {
            int team = players.Team(player);
            Vector eye = enginePlayers.GetShootOrigin(player);
            Vector aim = Angles.Vectors(entities.GetEyeAngles(player)).Forward;
            Vector reach = eye + aim * range;

            // Stock's line trace shortens the swing to what it reaches: a wall
            // before a point stops the blow. One trace serves every candidate.
            TraceResult wall = world.TraceLine(eye, reach, TraceMask.Brushes, player);

            TFBaseObject? best = null;
            TFSapper? bestSapper = null;
            float bestAlong = 0f;

            foreach (TFBaseObject building in buildings.OwnedBy())
            {
                // OnFriendlyBuildingHit: the building is the engineer's team's.
                if (building.Team != team)
                    continue;

                // The swing's ±18 hull reaches a building whose centre sits this
                // close to the swing's line (buildings are non-solid, so the
                // hull sweep stands in as a point distance).
                Vector point = entities.GetWorldSpaceCenter(building.Handle);
                if (PointLineDistance(eye, reach, point) > SwingHull)
                    continue;

                // Stock's trace lands the blow on the nearest hit along the
                // swing (tf_weapon_wrench.cpp:170-186) — the engine's answer is
                // the closest entity, not the first enumerated.
                float along = (point - eye).Dot(aim);
                if (wall.Hit && wall.Fraction * range < along)
                    continue;

                if (best is not null && along >= bestAlong)
                    continue;

                best = building;
                bestSapper = building.HasSapper ? SapperOn(building) : null;
                bestAlong = along;
            }

            if (best is null)
                return false;

            // InputWrenchHit's arms (tf_obj.cpp:2746-2794): a sapped building's
            // blow goes to its sapper; anything else is the building's own
            // OnWrenchHit — the repair, the refill and the upgrade metal.
            if (bestSapper is not null)
            {
                using (scopes.Push(TFDamageCustom.WrenchFix, TFWeaponId.Wrench, TFWrenchHook.WrenchScript))
                {
                    // InputWrenchHit's sapper arm: the one blow a sapper takes.
                    damage.TakeDamage(bestSapper.Handle, player, WrenchDmgVsSapper, DamageType.Club);

                    // The pair block of CObjectSapper::OnTakeDamage: the match's
                    // sapper takes the same blow, wherever it rides.
                    if (best is TFTeleporter tp
                        && tp.Match is { } match && match.HasSapper
                        && SapperOn(match) is { } twin)
                    {
                        damage.TakeDamage(twin.Handle, player, WrenchDmgVsSapper, DamageType.Club);
                    }
                }

                return true;
            }

            return best.WrenchHit(player);
        }
        finally
        {
            lagCompensation?.Finish(player);
        }
    }

    /// <summary>The sapper riding <paramref name="building"/>, as the use layer keeps it.</summary>
    private TFSapper? SapperOn(TFBaseObject building) =>
        buildings.OwnedBy(type: TFObjectType.AttachmentSapper).OfType<TFSapper>()
                 .FirstOrDefault(s => s.BuiltOn == building.Handle);

    /// <summary>Stock's z-flattened normalize; a zero vector stays zero.</summary>
    private static Vector Flat(Vector v)
    {
        v.Z = 0f;
        float length = v.Length;
        return length > 0f ? v * (1f / length) : v;
    }

    /// <summary>The distance from the point to the segment, its ends included.</summary>
    private static float PointLineDistance(Vector from, Vector to, Vector point)
    {
        Vector span = to - from;
        float lengthSquared = span.LengthSquared;
        if (lengthSquared < 1f)
            return (point - from).Length;

        float t = Math.Clamp((point - from).Dot(span) / lengthSquared, 0f, 1f);
        return (point - (from + span * t)).Length;
    }
}
