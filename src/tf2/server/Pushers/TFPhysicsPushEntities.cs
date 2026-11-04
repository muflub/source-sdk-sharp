//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Entities.Items;
using TF2.Server.Entities.Rooms;

namespace TF2.Server;

/// <summary>
/// TF's answer at the pusher's two speculative-check points,
/// <c>CTFPhysicsPushEntities</c> (tf_pushentity.cpp:15-481): on escort maps a
/// <c>MOVETYPE_PUSH</c> mover does not stop at a player — it slides them along
/// the face it hits (<see cref="SpeculativelyCheckLinearPush"/>, :111-145;
/// <see cref="SpeculativelyCheckRotPush"/>, :70-106), and a train pushes an
/// enemy through a respawn room visualizer by killing them instead
/// (<c>MovePlayer</c>, :429-441). Everything else keeps the generic
/// <c>CPhysicsPushedEntities</c> answer, and so does every map that is not an
/// escort one — stock's gate <c>TFGameRules()->GameModeUsesEscortPushLogic()</c>
/// (:73, :114; tf_gamerules.cpp:2170-2182), which our game-type flags carry as
/// <c>tf_gamemode_payload</c>.
///
/// <para>
/// A player whose box does not meet the pusher's box is left exactly where it
/// is (:180-183, :302-305) — stock answers the entity from the player arm and
/// never falls back to the generic sweep. A player the arm did move cannot
/// block the mover: stock forces <c>m_bBlocked = false</c> (:267, :358), which
/// is why the core's finish skips the entity the hook handled.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(IPushEntitiesHook)])]
public sealed class TFPhysicsPushEntities(IPushWorld world, IEngineEntities entities, ITFRespawnRooms rooms,
                                          ITFMapPlayers players, IEngineDamage damage, TFGameModePayloadConVar payload)
    : IPushEntitiesHook
{
    /// <inheritdoc />
    public bool SpeculativelyCheckLinearPush(EntityHandle pusherRoot, EntityHandle blocker, Vector absPush)
    {
        // Only do this for "payload" or "escort" maps (:113-115).
        if (!UsesEscortPushLogic)
            return false;

        // Is the entity a TF Player? (:121-126) The others keep the generic check.
        if ((world.State(blocker, out _) & EntityState.Player) == 0)
            return false;

        LinearPushTFPlayer(pusherRoot, blocker, absPush);
        return true;
    }

    /// <inheritdoc />
    public bool SpeculativelyCheckRotPush(EntityHandle pusherRoot, EntityHandle blocker, QAngle amove)
    {
        if (!UsesEscortPushLogic)
            return false;

        if ((world.State(blocker, out _) & EntityState.Player) == 0)
            return false;

        RotationPushTFPlayer(pusherRoot, blocker, amove);
        return true;
    }

    /// <summary>
    /// Stock's gate (:73, :114; tf_gamerules.cpp:2170-2182): the escort game
    /// type, which our flags land on <c>tf_gamemode_payload</c>.
    /// </summary>
    private bool UsesEscortPushLogic => payload.Value;

    /// <summary>
    /// <c>RotationPushTFPlayer</c> (:150-226): the sphere approximation of the
    /// distance to shove the player by, upward when they ride the mover and
    /// away from it otherwise, then <see cref="RotationCheckPush"/>. Stock's
    /// early-outs on missing collision data (:159-177) are the unreadable box.
    /// </summary>
    private void RotationPushTFPlayer(EntityHandle pusherRoot, EntityHandle blocker, QAngle amove)
    {
        if (!TryPusherBox(pusherRoot, out Vector pMins, out Vector pMaxs))
            return;

        var (bMins, bMaxs) = world.Bounds(blocker);
        Vector playerOrigin = world.Origin(blocker);

        // Do we have a collision (:179-183).
        if (!IsPlayerAABBIntersectingPusherOBB(blocker, pusherRoot, pMins, pMaxs))
            return;

        // For speed use spheres to approximate push distance (:185-197): the
        // radius is half the box's diagonal, stock's BoundingRadius
        // (collisionproperty.h:331 with RadiusFromBounds).
        float playerRadius = (bMaxs - bMins).Length * 0.5f;
        float pusherRadius = (pMaxs - pMins).Length * 0.5f;

        Vector delta = playerOrigin - world.Origin(pusherRoot);
        float radiusTotal = playerRadius + pusherRadius;
        float length = delta.Length;

        Vector push;
        float dist;

        // Put special code in if we are riding the pusher - only push upward
        // (:199-214). Riding: the player stands on the pusher's root.
        if (world.GroundIndex(blocker) == world.Index(pusherRoot))
        {
            push = new Vector(0f, 0f, 1f);

            // The lift the rotation's pitch gives the rim (:204-213): the cart's
            // pitched turn moves its surface tan(pitch) * radius, plus 10%.
            dist = amove.Pitch != 0f
                 ? MathF.Abs(MathF.Tan(amove.Pitch * (MathF.PI / 180f))) * pusherRadius * 1.1f
                 : 0f;
        }
        else
        {
            // Away from the pusher's centre, by the sphere gap plus 10% (:215-223).
            push = length > 0f ? delta * (1f / length) : Vector.Zero;
            dist = MathF.Abs(radiusTotal - length) * 1.1f;
        }

        RotationCheckPush(pusherRoot, blocker, push, dist);
    }

    /// <summary>
    /// <c>RotationCheckPush</c> (:231-269): up to three short moves (a third of
    /// the distance) while the player's box still meets the pusher's box, and
    /// when it still does one move at full distance — the hard retry. Stock
    /// hides the pusher from the partition around the traces
    /// (<c>UnlinkPusherList</c>, :240); the ABI's sweep skips the swept entity
    /// alone and our mover is already out of the hull's way (the core's remark).
    /// </summary>
    private void RotationCheckPush(EntityHandle pusherRoot, EntityHandle blocker, Vector push, float dist)
    {
        for (int tryCount = 0; tryCount < 3; tryCount++)
        {
            MovePlayer(blocker, pusherRoot, push, dist * 0.35f);
            if (!IsPlayerAABBIntersectingPusherOBB(blocker, pusherRoot, out _, out _))
                break;
        }

        // Is the player still blocked — try again (:257-264), the hard retry at
        // full scale. Stock reads m_bPusherIsGround here (:250-254) only for its
        // own finish bookkeeping, which the handled skip already carries.
        if (IsPlayerAABBIntersectingPusherOBB(blocker, pusherRoot, out _, out _))
            MovePlayer(blocker, pusherRoot, push, dist);
    }

    /// <summary>
    /// <c>LinearPushTFPlayer</c> (:274-321): the mover's own step, along the
    /// flat ground when the player does not ride it and keeping its rise when
    /// they do (:307-318) — the distance is the step's whole length either way.
    /// </summary>
    private void LinearPushTFPlayer(EntityHandle pusherRoot, EntityHandle blocker, Vector absPush)
    {
        if (!TryPusherBox(pusherRoot, out Vector pMins, out Vector pMaxs))
            return;

        // Do we have a collision (:301-305).
        if (!IsPlayerAABBIntersectingPusherOBB(blocker, pusherRoot, pMins, pMaxs))
            return;

        float dist = absPush.Length;
        Vector push = dist > 0f ? absPush * (1f / dist) : Vector.Zero;

        // Not riding: shove along the ground only (:312-318).
        if (world.GroundIndex(blocker) != world.Index(pusherRoot))
        {
            Vector flat = new(push.X, push.Y, 0f);
            float flatLength = flat.Length;
            push = flatLength > 0f ? flat * (1f / flatLength) : Vector.Zero;
        }

        LinearCheckPush(pusherRoot, blocker, push, dist);
    }

    /// <summary>
    /// <c>LinearCheckPush</c> (:326-360): the move at full scale, and a second
    /// one when the player is not standing on the mover and the place it ended
    /// is not valid. Stock's <c>m_bPusherIsGround</c> (:341-345) compares the
    /// ground's root move parent with the pusher; our single mover is its root.
    /// </summary>
    private void LinearCheckPush(EntityHandle pusherRoot, EntityHandle blocker, Vector push, float dist)
    {
        MovePlayer(blocker, pusherRoot, push, dist);

        bool pusherIsGround = world.GroundIndex(blocker) == world.Index(pusherRoot);

        if (!pusherIsGround && !IsPushedPositionValid(blocker))
            MovePlayer(blocker, pusherRoot, push, dist);
    }

    /// <summary>
    /// <c>MovePlayer</c> (:413-461): up to four traces of four iterations. The
    /// trace starts four units over the player's feet and does not follow them
    /// as the traces move the player (:421-422); each stopped trace moves the
    /// player to where it ended, and the next one goes along the hit face.
    /// </summary>
    private void MovePlayer(EntityHandle blocker, EntityHandle pusherRoot, Vector pushStart, float dist)
    {
        float newDist = dist;
        Vector push = pushStart;

        // vecStart.z += 4 (:421-422), fixed across the four traces.
        Vector start = world.Origin(blocker);
        start = new Vector(start.X, start.Y, start.Z + 4f);

        for (int test = 0; test < 4; test++)
        {
            Vector origin = world.Origin(blocker);
            Vector end = origin + push * newDist;
            PushTrace trace = world.Sweep(blocker, start, end);

            // We don't want trains pushing enemy players through a respawn room
            // visualizer (:429-441): the clip kills them where the cart leaves
            // them instead of carrying them through.
            if (IsBaseTrain(pusherRoot)
                && (world.State(blocker, out _) & EntityState.Player) != 0
                && rooms.PointsCrossRespawnRoomVisualizer(start, trace.EndPos, players.Team(blocker)))
            {
                damage.Apply(blocker, EntityHandle.None, PlayerSelfCommand.SuicideDamage, DamageType.Generic);
                return;
            }

            if (trace.Fraction > 0f)
                world.SetOrigin(blocker, trace.EndPos);

            if (trace.Fraction == 1f || trace.Entity == EntityHandle.None)
                break;

            // New test distance and position (:452-454): what is left of the
            // move, stretched by how side-on the hit face was.
            float fractionLeft = 1f - trace.Fraction;
            newDist = fractionLeft * newDist * (1f + (1f - MathF.Abs(trace.Normal.Dot(push))));

            push = FindNewPushDirection(push, trace.Normal);
        }
    }

    /// <summary>
    /// <c>FindNewPushDirection</c> (:391-408): the move along the hit face, the
    /// way it leaves no depth through the plane.
    /// </summary>
    private static Vector FindNewPushDirection(Vector current, Vector normal)
    {
        float backOff = current.Dot(normal);

        Vector outPut = new(current.X - normal.X * backOff,
                            current.Y - normal.Y * backOff,
                            current.Z - normal.Z * backOff);

        // Iterate once to make sure we aren't still moving through the plane (:402-407).
        float adjust = outPut.Dot(normal);
        if (adjust < 0f)
            outPut = outPut - normal * adjust;

        return outPut;
    }

    /// <summary>
    /// <c>IsPlayerAABBIntersetingPusherOBB</c> (:365-386): the player's box at
    /// where it stands, against the pusher's oriented box. Stock reads the
    /// player's own collision angles for it; a player's hull is upright, which
    /// is what the core's separating-axis form answers for the same box
    /// (<see cref="PhysicsPushedEntities.ObbIntersectsAabb"/>).
    /// </summary>
    private bool IsPlayerAABBIntersectingPusherOBB(EntityHandle blocker, EntityHandle pusherRoot,
                                                   Vector pMins, Vector pMaxs)
    {
        var (bMins, bMaxs) = world.Bounds(blocker);
        Vector origin = world.Origin(blocker);

        return PhysicsPushedEntities.ObbIntersectsAabb(pMins, pMaxs, PusherToWorld(pusherRoot),
                                                       origin + bMins, origin + bMaxs);
    }

    private bool IsPlayerAABBIntersectingPusherOBB(EntityHandle blocker, EntityHandle pusherRoot,
                                                   out Vector pMins, out Vector pMaxs)
    {
        if (!TryPusherBox(pusherRoot, out pMins, out pMaxs))
            return false;

        return IsPlayerAABBIntersectingPusherOBB(blocker, pusherRoot, pMins, pMaxs);
    }

    /// <summary>The pusher's oriented box (<c>OBBMins</c>/<c>OBBMaxs</c>) and frame.</summary>
    private Transform PusherToWorld(EntityHandle pusherRoot)
        => Transform.FromOriginAngles(world.Origin(pusherRoot), world.Angles(pusherRoot));

    private bool TryPusherBox(EntityHandle pusherRoot, out Vector mins, out Vector maxs)
        => world.CollisionBox(pusherRoot, out mins, out maxs);

    /// <summary>
    /// <c>CFuncTrackTrain::IsBaseTrain</c> (trains.h:135, the base's false at
    /// baseentity.h:1022): the train arm of the respawn-room clip.
    /// </summary>
    private bool IsBaseTrain(EntityHandle pusherRoot)
        => entities.IsValid(pusherRoot)
           && string.Equals(entities.GetClassName(pusherRoot), "func_tracktrain", StringComparison.Ordinal);

    /// <summary>Stock's <c>IsPushedPositionValid</c> (pushentity.cpp), the fit test at the place the player reached.</summary>
    private bool IsPushedPositionValid(EntityHandle blocker)
    {
        Vector at = world.Origin(blocker);
        return !world.Sweep(blocker, at, at).StartSolid;
    }
}
