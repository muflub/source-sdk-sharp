//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// What the trace rule reads of a candidate: four words of its engine block.
/// </summary>
/// <param name="CollisionGroup">Head.CollisionGroup, a <c>Collision_Group_t</c>.</param>
/// <param name="SolidType">Head.SolidType, a <c>SolidType_t</c>.</param>
/// <param name="ModelIndex">Head.ModelIndex, what <c>GetModelIndex</c> answers.</param>
/// <param name="Owner">Head.OwnerHandle.</param>
public readonly record struct TraceCandidate(int CollisionGroup, SolidType SolidType, int ModelIndex, EntityHandle Owner);

/// <summary>
/// Where the trace rule reads its candidates from: the engine blocks, and the
/// model table for the shape clause.
/// </summary>
public interface ITraceCandidates
{
    /// <summary>
    /// The block words of a game entity (one with an engine block: the
    /// standalone hierarchy, which is every game entity this server has), or
    /// false for anything else: a dead handle, the world, a static prop.
    /// </summary>
    bool TryRead(EntityHandle entity, out TraceCandidate candidate);

    /// <summary>Whether the handle is the world's (worldspawn's own handle, live).</summary>
    bool IsWorld(EntityHandle entity);

    /// <summary><c>modelinfo->GetModelType( modelinfo->GetModel( index ) ) == mod_brush</c>.</summary>
    bool IsBrushModel(int modelIndex);
}

/// <summary>
/// One trace's filter, resolved once before the trace runs: stock's
/// <c>CTraceFilterSimple( passentity, collisionGroup )</c>, with the pass
/// entity's own answers read up front because every candidate asks them.
/// </summary>
/// <param name="Pass">The entity to skip, as the caller named it.</param>
/// <param name="PassResolved">Whether <see cref="Pass"/> names a live entity
/// (a game entity or the world): stock's <c>m_pPassEnt != NULL</c>.</param>
/// <param name="PassOwner">The pass entity's owner (none for the world).</param>
/// <param name="CollisionGroup">The trace's <c>m_collisionGroup</c>.</param>
public readonly record struct TraceFilterQuery(
    EntityHandle Pass, bool PassResolved, EntityHandle PassOwner, int CollisionGroup);

/// <summary>
/// WHAT A TRACE MAY HIT (plan_server_native_v2 T4f, plan_server_native_min
/// P3f: Decision 2, ruled managed). The engine asks the filter once per
/// candidate entity, mid-trace; libss_fwd's ITraceFilter hands the candidate
/// here as its handle, and this answers.
///
/// <para>
/// A port of stock's <c>CTraceFilterSimple::ShouldHitEntity</c> with its two
/// helpers (util_shared.cpp's <c>StandardFilterRules</c> and
/// <c>PassServerEntityFilter</c>) and <c>CGameRules::ShouldCollide</c>, case
/// for case, as server.so's trace_filter_library.cpp answered them for this
/// hierarchy:
/// </para>
/// <list type="bullet">
///   <item>THE SHAPE CLAUSE (<c>StandardFilterRules</c>): a game entity that is
///   not a brush model solid as one (SOLID_BSP or SOLID_VPHYSICS) is hit only
///   by a mask that asks for entities (CONTENTS_MONSTER). Anything that is not a
///   game entity passes it (stock's static-prop arm).</item>
///   <item>THE PASS ENTITY (<c>PassServerEntityFilter</c>): never the pass
///   entity itself, never anything it owns (its missiles), never its owner.
///   Ownership is compared as handles: the block stores the owner as one.</item>
///   <item>THE ENTITY AND THE RULES: anything that is not a game entity is not
///   hit; a game entity's own <c>ShouldCollide</c> is CStandaloneEntity's,
///   which answers true; the rules' group matrix (CollisionRules' rows, stock's
///   predicate) decides the pair; out of range is true.</item>
/// </list>
///
/// <para>
/// WHAT STAYS AS IT WAS, AND IS NOT STOCK. Stock's shape clause also skips a
/// transparent entity under a mask without CONTENTS_WINDOW and a MOVETYPE_PUSH
/// one under a mask without CONTENTS_MOVEABLE; CBaseEntity::ShouldCollide skips
/// debris under a mask without CONTENTS_DEBRIS. The native filter this replaces
/// answered none of the three for this hierarchy (trace_filter_library.cpp and
/// shell_standalone.h say why), and a move is not where they change.
/// </para>
/// </summary>
public sealed class TraceFilterRules
{
    /// <summary>CONTENTS_MONSTER: the bit that says a trace wants entities.</summary>
    public const int ContentsMonster = 0x2000000;

    /// <summary>
    /// <c>TRACE_EVERYTHING</c>: CTraceFilterSimple's <c>GetTraceType</c> (its
    /// base CTraceFilter's), the world, the entities and the static props.
    /// The native filter is built answering it (TraceFilterObject.Build).
    /// </summary>
    public const int TraceEverything = 0;

    /// <summary>LAST_SHARED_COLLISION_GROUP: the matrix's width.</summary>
    public const int GroupCount = 20;

    /// <summary>
    /// The engine's static-prop handle mask as a serial: a static prop's
    /// handle carries 0x40000000, which is serial 0x40000 over the 12 entry bits.
    /// </summary>
    public const uint StaticPropSerial = 0x40000000u >> EntryBits;

    private const int EntryBits = 12;

    private readonly ITraceCandidates _candidates;
    private readonly uint[] _rows;

    /// <param name="candidates">The blocks and the model table.</param>
    /// <param name="collisionRows">CollisionRules.BuildMatrix(): bit b of row a
    /// is "group a collides with group b".</param>
    public static TraceFilterRules Over(ITraceCandidates candidates, uint[] collisionRows)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(collisionRows);
        if (collisionRows.Length != GroupCount)
            throw new ArgumentException($"the collision matrix has {GroupCount} rows, not {collisionRows.Length}", nameof(collisionRows));
        return new TraceFilterRules(candidates, (uint[])collisionRows.Clone());
    }

    // The rows are a copy Over made, checked there (a constructor only stores,
    // by the interop layer's rule).
    private TraceFilterRules(ITraceCandidates candidates, uint[] rows)
    {
        _candidates = candidates;
        _rows = rows;
    }

    /// <summary>Whether a raw handle is a static prop's (<c>IStaticPropMgr::IsStaticProp</c>).</summary>
    public static bool IsStaticProp(uint rawHandle) => (rawHandle >> EntryBits) == StaticPropSerial;

    /// <summary>
    /// <c>CTraceFilterSimple( pass, collisionGroup )</c>, its pass entity
    /// resolved: what stock reads of it once per candidate, read once.
    /// </summary>
    public TraceFilterQuery Resolve(EntityHandle pass, int collisionGroup)
    {
        if (pass == EntityHandle.None)
            return new TraceFilterQuery(pass, false, EntityHandle.None, collisionGroup);

        if (_candidates.TryRead(pass, out TraceCandidate passed))
            return new TraceFilterQuery(pass, true, passed.Owner, collisionGroup);

        // The world resolves (anyentity.h counts it as something) and owns nothing.
        return new TraceFilterQuery(pass, _candidates.IsWorld(pass), EntityHandle.None, collisionGroup);
    }

    /// <summary><c>CTraceFilterSimple::ShouldHitEntity( pHandleEntity, contentsMask )</c>.</summary>
    public bool ShouldHitEntity(in TraceFilterQuery query, uint rawCandidate, int contentsMask)
    {
        var touch = new EntityHandle(rawCandidate);

        // GameEntityFromEntityHandle: nothing, or a static prop, is no game entity.
        TraceCandidate candidate = default;
        bool game = rawCandidate != EntityHandle.None.Value && !IsStaticProp(rawCandidate)
                    && _candidates.TryRead(touch, out candidate);

        // StandardFilterRules: the shape clause, for a game entity only.
        if (game && !ShapePasses(candidate, contentsMask))
            return false;

        // PassServerEntityFilter, when there is a pass entity.
        if (query.PassResolved)
        {
            if (touch == query.Pass)
                return false;

            // Both must be something; the world counts and a static prop does not.
            if (game)
            {
                // don't clip against own missiles
                if (candidate.Owner == query.Pass)
                    return false;

                // don't clip against owner
                if (query.PassOwner == touch)
                    return false;
            }
        }

        // Not a game entity: not hit.
        if (!game)
            return false;

        // CStandaloneEntity::ShouldCollide answers true; the rules decide the pair.
        return GroupsCollide(query.CollisionGroup, candidate.CollisionGroup);
    }

    /// <summary>
    /// <c>CGameRules::ShouldCollide</c> as the matrix answers it: true for a
    /// group outside the table, as stock's predicate is for anything it does not
    /// name.
    /// </summary>
    public bool GroupsCollide(int group0, int group1)
    {
        if ((uint)group0 >= GroupCount || (uint)group1 >= GroupCount)
            return true;

        return (_rows[group0] & (1u << group1)) != 0;
    }

    /// <summary>
    /// standard_filter_shape.h: a brush solid as a brush stops every trace;
    /// anything else only one that asked for entities. The model table is asked
    /// only when the mask leaves the answer open.
    /// </summary>
    private bool ShapePasses(in TraceCandidate candidate, int contentsMask)
    {
        if ((contentsMask & ContentsMonster) != 0)
            return true;

        return candidate.SolidType is SolidType.Bsp or SolidType.VPhysics
               && _candidates.IsBrushModel(candidate.ModelIndex);
    }
}
