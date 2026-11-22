//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>
/// The other players, as the client's prediction collides with them: what a
/// movement hull trace answers once the players' boxes are added to the
/// engine's world trace.
///
/// <para>
/// WHY IT IS MANAGED. The client's engine trace hits no entity: the client's
/// shells are not in the spatial partition and its native
/// <c>CTraceFilterSimple</c> answers false past the pass entity
/// (trace_filter_library.cpp). So the prediction walked through every player
/// while the server stopped the walk at the first one, and every bump
/// mispredicted. Stock's client clips the ray against each solid player's box
/// (<c>CEngineTrace::ClipRayToBBox</c>), asking the trace filter first; this
/// is that clip over the networked players, asking the same
/// <see cref="IEntityCollideRule"/> chain the server's trace filter asks, so a
/// teammate the server passes is passed here too.
/// </para>
///
/// <para>
/// The box test is <c>IntersectRayWithBox( const Ray_t&amp;, ... )</c>
/// (collisionutils.cpp) with the engine's <c>DIST_EPSILON</c>, and the merge
/// with the world trace is <c>CEngineTrace::TraceRay</c>'s: no entity is asked
/// once the world stops the ray at its start, and an entity replaces the world's
/// answer when it starts solid or stops the ray sooner.
/// </para>
/// </summary>
public sealed class PredictionPlayerClip(IClientStandalones standalones, IEngineEntityRender render,
                                         IHooks<IEntityCollideRule> rules, Func<int> maxClients,
                                         PredictionEntityClip? entities = null,
                                         IHooks<IPredictionTraceClip>? clips = null)
{
    /// <summary><c>DIST_EPSILON</c> (enginetrace / cmodel): the gap a trace keeps from what it hits.</summary>
    public const float DistEpsilon = 0.03125f;

    /// <summary><c>CONTENTS_SOLID</c>, what a box hit reports.</summary>
    public const int ContentsSolid = 0x1;

    /// <summary><c>CONTENTS_MONSTER</c>: a box entity is hit only by a mask that asks for entities.</summary>
    public const int ContentsMonster = 0x2000000;

    /// <summary><c>FL_DUCKING</c>: the flag CBasePlayer::PostThink sizes the player's box by.</summary>
    public const uint FlDucking = 1u << 1;

    private MoveConstants _hull;

    // Whether a command has named the hulls: a trace outside a predicted
    // command (none is asked today) clips in no player, as before this class.
    private bool _hulls;

    /// <summary>
    /// The entity index of the last ground a trace found, when that ground
    /// was a player or a solid entity this clip added (stock's last-ground
    /// entity, which either is), or -1 when it was the engine's: the
    /// prediction's ground write-back reads it.
    /// </summary>
    public int LastGroundIndex { get; private set; } = -1;

    /// <summary>
    /// A command starts: the hulls the players' boxes take (the command's
    /// movement constants), and no player has been stood on yet.
    /// </summary>
    public void BeginCommand(in MoveConstants constants)
    {
        _hull = constants;
        _hulls = true;
        LastGroundIndex = -1;
        entities?.BeginCommand();
    }

    /// <summary>
    /// <paramref name="world"/>, the engine's answer for the swept hull, with
    /// every other solid player the rules let <paramref name="mover"/> hit
    /// clipped in, and then every other solid entity
    /// (<see cref="PredictionEntityClip"/>).
    /// </summary>
    public MoveTrace Clip(in MoveTrace world, EntityHandle mover, in Vector start, in Vector end, in Vector mins,
                          in Vector maxs, uint mask, int collisionGroup)
    {
        MoveTrace best = world;
        int bestIndex = -1;

        // CEngineTrace::TraceRay: a world trace stopped at its start asks no entity.
        if (_hulls && world.Fraction != 0f && ((int)mask & ContentsMonster) != 0
            && CollisionRules.ShouldCollide((EngineCollisionGroup)collisionGroup, EngineCollisionGroup.Player))
        {
            int count = maxClients();
            for (int index = 1; index <= count; index++)
            {
                if (standalones.GetByIndex(index) is not ClientSsPlayer player || player.Handle == mover
                    || !IsSolid(player) || !RulesAgree(mover, player.Handle, collisionGroup, (int)mask))
                    continue;

                PlayerNetState state = player.State;
                bool ducked = (state.Flags & FlDucking) != 0;
                Vector boxMins = state.Origin + (ducked ? _hull.DuckHullMin : _hull.HullMin);
                Vector boxMaxs = state.Origin + (ducked ? _hull.DuckHullMax : _hull.HullMax);

                if (!SweepBox(start, end, mins, maxs, boxMins, boxMaxs, out MoveTrace hit))
                    continue;

                if (hit.AllSolid || hit.StartSolid || hit.Fraction < best.Fraction)
                {
                    bool startSolid = best.StartSolid || hit.StartSolid;
                    hit.StartSolid = startSolid;
                    hit.HasEntity = true;
                    hit.HasGround = true;
                    hit.HitEntity = player.Handle;
                    hit.GroundVelocity = state.Velocity;
                    best = hit;
                    bestIndex = index;
                }
            }
        }

        // The props and brush entities, under the same no-entity-past-a-blocked-start rule.
        if (_hulls && world.Fraction != 0f && entities is not null)
        {
            int hitIndex = entities.Clip(ref best, mover, start, end, mins, maxs, mask, collisionGroup);
            if (hitIndex >= 0)
                bestIndex = hitIndex;
        }

        // What else the server's traces stop at (IPredictionTraceClip: an entity answering its own
        // traces), merged as an entity is: it replaces the answer when it starts solid or stops sooner.
        if (_hulls && world.Fraction != 0f && clips is not null)
        {
            IReadOnlyList<IPredictionTraceClip> all = clips.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (!all[i].Clip(start, end, mins, maxs, (int)mask, out MoveTrace hit))
                    continue;

                if (hit.AllSolid || hit.StartSolid || hit.Fraction < best.Fraction)
                {
                    hit.StartSolid = best.StartSolid || hit.StartSolid;
                    hit.HasEntity = true;
                    hit.HasGround = true;
                    best = hit;
                    bestIndex = -1;
                }
            }
        }

        // What the loop will name as its ground if this trace found one.
        if (best.HasGround)
            LastGroundIndex = bestIndex;

        return best;
    }

    /// <summary>A player that blocks: alive, playing rather than watching, and in this client's view.</summary>
    private bool IsSolid(ClientSsPlayer player)
        => player.State.Alive && player.State.ObserverMode == 0 && !render.IsDormant(player.Handle);

    private bool RulesAgree(EntityHandle mover, EntityHandle entity, int collisionGroup, int mask)
    {
        IReadOnlyList<IEntityCollideRule> all = rules.All;
        for (int i = 0; i < all.Count; i++)
        {
            if (!all[i].ShouldCollide(mover, entity, (CollisionGroup)collisionGroup, mask))
                return false;
        }

        return true;
    }

    /// <summary>
    /// <c>IntersectRayWithBox( const Ray_t &amp;ray, boxMins, boxMaxs, DIST_EPSILON, pTrace )</c>
    /// for a swept hull: the box grown by the hull's half extents, the ray from
    /// the hull's centre. False when the sweep misses the box.
    /// </summary>
    public static bool SweepBox(in Vector start, in Vector end, in Vector mins, in Vector maxs, in Vector boxMins,
                                in Vector boxMaxs, out MoveTrace trace)
    {
        // Ray_t::Init: the extents and the offset to the hull's centre.
        Vector extents = (maxs - mins) * 0.5f;
        Vector offset = (mins + maxs) * 0.5f;
        Vector rayStart = start + offset;
        Vector delta = end - start;
        Vector lo = boxMins - extents;
        Vector hi = boxMaxs + extents;

        trace = MoveTrace.Clear(end);

        float t1 = -1f;
        float t2 = 1f;
        int hitSide = -1;
        bool startSolid = true;

        for (int i = 0; i < 6; ++i)
        {
            float d1, d2;
            if (i >= 3)
            {
                d1 = Axis(rayStart, i - 3) - Axis(hi, i - 3);
                d2 = d1 + Axis(delta, i - 3);
            }
            else
            {
                d1 = -Axis(rayStart, i) + Axis(lo, i);
                d2 = d1 - Axis(delta, i);
            }

            // completely in front of face, no intersection
            if (d1 > 0 && d2 > 0)
                return false;

            // completely inside, check next face
            if (d1 <= 0 && d2 <= 0)
                continue;

            if (d1 > 0)
                startSolid = false;

            if (d1 > d2)
            {
                // crosses face
                float f = d1 - DistEpsilon;
                if (f < 0)
                    f = 0;
                f /= d1 - d2;
                if (f > t1)
                {
                    t1 = f;
                    hitSide = i;
                }
            }
            else
            {
                // leave
                float f = (d1 + DistEpsilon) / (d1 - d2);
                if (f < t2)
                    t2 = f;
            }
        }

        if (!startSolid && !(t1 < t2 && t1 >= 0f))
            return false;

        trace.StartSolid = startSolid;
        trace.Contents = ContentsSolid;
        if (t1 < t2 && t1 >= 0f)
        {
            trace.Fraction = t1;
            trace.EndPos = start + delta * t1;
            int axis = hitSide >= 3 ? hitSide - 3 : hitSide;
            float sign = hitSide >= 3 ? 1f : -1f;
            trace.PlaneNormal = axis switch
            {
                0 => new Vector(sign, 0f, 0f),
                1 => new Vector(0f, sign, 0f),
                _ => new Vector(0f, 0f, sign),
            };
            trace.PlaneDist = trace.EndPos.Dot(trace.PlaneNormal);
            return true;
        }

        // Starts inside: all solid unless the ray leaves the box before its end.
        trace.AllSolid = t2 <= 0f || t2 >= 1f;
        trace.Fraction = 0f;
        trace.EndPos = start;
        trace.PlaneNormal = new Vector(1f, 0f, 0f);
        trace.PlaneDist = start.X;
        return true;
    }

    private static float Axis(in Vector v, int axis) => axis switch
    {
        0 => v.X,
        1 => v.Y,
        _ => v.Z,
    };
}
