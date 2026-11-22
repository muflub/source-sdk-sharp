//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>
/// The solid entities other than players, as the client's prediction collides
/// with them: what a movement hull trace answers once the props and brush
/// entities the client's engine trace cannot see are clipped in.
///
/// <para>
/// WHY. The client's shells are in no spatial partition, so its engine trace
/// stops on the world alone, while the server's stops on every solid entity.
/// A player standing on a control point's base (cp_granary's and
/// cp_badlands' <c>prop_dynamic</c> <c>cap_point_base.mdl</c>, solid 6) was on
/// that prop on the server and, in every predicted command, falling through
/// it to the floor seven units below on the client: every compare mispredicted
/// (ground 0 against the prop's index, z -383.97 against -376.82) and the
/// correction bobbed the view. Stock's client trace enumerates its solid
/// partition (<c>CEngineTrace::TraceRay</c>) and clips each entity its
/// <c>CTraceFilterSimple</c> admits; this is that, over the list native keeps
/// as the partition would (<see cref="IEngineWorldQuery.ClientSolidEntities"/>).
/// </para>
///
/// <para>
/// THE DECISION IS THE SERVER'S FILTER'S (<c>TraceFilterRules</c>, case for
/// case): the shape clause (a brush solid as a brush stops every trace,
/// anything else only a mask that asks for entities), never the mover itself,
/// the registered <see cref="IEntityCollideRule"/> chain, then the
/// collision-group pair (<see cref="CollisionRules"/>, true outside the table).
/// The owner clause is not asked: no owner is on the wire.
/// </para>
///
/// <para>
/// WHAT IS CLIPPED: entities solid as a BSP or by VPHYSICS, which is every
/// prop and brush entity. Players are <see cref="PredictionPlayerClip"/>'s.
/// Box-solid entities (buildings, projectiles) are left out, as before this
/// class: their owner decides whether the server's filter passes them, and
/// the client cannot ask it.
/// </para>
/// </summary>
public sealed class PredictionEntityClip(IEngineWorldQuery world, IHooks<IEntityCollideRule> rules, Func<int> maxClients)
{
    /// <summary><c>CONTENTS_MONSTER</c>: the bit that says a trace wants entities.</summary>
    public const int ContentsMonster = 0x2000000;

    /// <summary>How far past the swept hull an entity's box may lie and still be asked: the engine's DIST_EPSILON and change.</summary>
    public const float CullMargin = 1f;

    private ClientSolidEntity[] _solids = new ClientSolidEntity[256];
    private int _solidCount;
    private bool _listed;
    private readonly int[] _chosen = new int[EngineLimits.MaxEdicts];
    private readonly EntityHandle[] _chosenHandles = new EntityHandle[EngineLimits.MaxEdicts];

    /// <summary>A command starts: the partition is read again on its first trace.</summary>
    public void BeginCommand() => _listed = false;

    /// <summary>
    /// <paramref name="best"/>, the answer so far (the world's, with the
    /// players clipped in), with every solid entity the filter lets
    /// <paramref name="mover"/> hit clipped in. Answers the index of the entity
    /// that replaced the answer, or -1 when none did.
    /// </summary>
    public int Clip(ref MoveTrace best, EntityHandle mover, in Vector start, in Vector end, in Vector mins,
                    in Vector maxs, uint mask, int collisionGroup)
    {
        if (!_listed)
            List();

        var lo = new Vector(MathF.Min(start.X, end.X) + mins.X - CullMargin, MathF.Min(start.Y, end.Y) + mins.Y - CullMargin,
                            MathF.Min(start.Z, end.Z) + mins.Z - CullMargin);
        var hi = new Vector(MathF.Max(start.X, end.X) + maxs.X + CullMargin, MathF.Max(start.Y, end.Y) + maxs.Y + CullMargin,
                            MathF.Max(start.Z, end.Z) + maxs.Z + CullMargin);
        int players = maxClients();
        int chosen = 0;

        for (int i = 0; i < _solidCount; i++)
        {
            ref readonly ClientSolidEntity solid = ref _solids[i];
            if (solid.Index <= players || solid.Handle == mover
                || solid.SolidType is not (SolidType.Bsp or SolidType.VPhysics)
                || !Overlaps(lo, hi, solid.Mins, solid.Maxs)
                || !ShouldHit(solid, mover, (int)mask, collisionGroup))
                continue;

            _chosenHandles[chosen] = solid.Handle;
            _chosen[chosen++] = solid.Index;
        }

        if (chosen == 0)
            return -1;

        int hit = world.ClientClipHullToEntities(start, end, mins, maxs, mask, _chosen.AsSpan(0, chosen),
                                                 out MoveTrace clip);
        if (hit < 0)
            return -1;

        // CEngineTrace::ClipTraceToTrace: a start inside, or a sooner stop, replaces
        // the answer, and a start inside anything stays a start inside.
        if (clip.AllSolid || clip.StartSolid || clip.Fraction < best.Fraction)
        {
            clip.StartSolid = best.StartSolid || clip.StartSolid;
            clip.HasEntity = true;
            clip.HasGround = true;
            clip.HitEntity = HandleOf(hit, chosen);
            best = clip;
            return hit;
        }

        return -1;
    }

    /// <summary>
    /// <c>CTraceFilterSimple::ShouldHitEntity</c> for a client entity, as the
    /// server's <c>TraceFilterRules</c> answers it.
    /// </summary>
    public bool ShouldHit(in ClientSolidEntity solid, EntityHandle mover, int contentsMask, int collisionGroup)
    {
        // StandardFilterRules: the shape clause.
        if ((contentsMask & ContentsMonster) == 0
            && !(solid.SolidType is SolidType.Bsp or SolidType.VPhysics && solid.ModelType == (int)ModelType.Brush))
            return false;

        // PassServerEntityFilter: never the mover itself.
        if (solid.Handle == mover)
            return false;

        IReadOnlyList<IEntityCollideRule> all = rules.All;
        for (int i = 0; i < all.Count; i++)
        {
            if (!all[i].ShouldCollide(mover, solid.Handle, (CollisionGroup)collisionGroup, contentsMask))
                return false;
        }

        return GroupsCollide(collisionGroup, solid.CollisionGroup);
    }

    /// <summary><c>CGameRules::ShouldCollide</c>: true for a group outside the shared table.</summary>
    public static bool GroupsCollide(int group0, int group1)
    {
        if ((uint)group0 >= CollisionRules.GroupCount || (uint)group1 >= CollisionRules.GroupCount)
            return true;

        return CollisionRules.ShouldCollide((EngineCollisionGroup)group0, (EngineCollisionGroup)group1);
    }

    private void List()
    {
        int total = world.ClientSolidEntities(_solids);
        if (total > _solids.Length)
        {
            _solids = new ClientSolidEntity[Math.Max(total, _solids.Length * 2)];
            total = world.ClientSolidEntities(_solids);
        }

        _solidCount = Math.Min(total, _solids.Length);
        _listed = true;
    }

    private EntityHandle HandleOf(int index, int chosen)
    {
        for (int i = 0; i < chosen; i++)
        {
            if (_chosen[i] == index)
                return _chosenHandles[i];
        }

        return EntityHandle.None;
    }

    private static bool Overlaps(in Vector lo, in Vector hi, in Vector mins, in Vector maxs)
        => lo.X <= maxs.X && hi.X >= mins.X && lo.Y <= maxs.Y && hi.Y >= mins.Y && lo.Z <= maxs.Z && hi.Z >= mins.Z;
}
