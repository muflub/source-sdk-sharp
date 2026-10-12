//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// One hull sweep, as <see cref="PhysicsPushedEntities"/> reads it:
/// <c>trace_t</c>'s fraction, end position and the two solid bits.
/// </summary>
public readonly record struct PushTrace(float Fraction, Vector EndPos, bool StartSolid, bool AllSolid);

/// <summary>
/// The world as the pusher asks about it — every question
/// <c>CPhysicsPushedEntities</c> puts to an entity or to the trace, and the
/// three things it does to one.
///
/// <para>
/// A seam rather than the engine areas directly, for the reason
/// <see cref="IHierarchyWorld"/> is one: the rules are checked by fact against a
/// world of boxes, and only <see cref="EnginePushWorld"/> knows that the answers
/// come from five areas and a player object.
/// </para>
///
/// <para>
/// <b>What it cannot ask, which is the stated difference from stock.</b> Stock
/// hides every pusher from the partition before it sweeps a blocker
/// (<c>UnlinkPusherList</c>) and filters moveable physics objects out of the
/// final position test (<c>CTraceFilterPushFinal</c>). The ABI's hull sweep
/// skips the swept entity and nothing else, so <see cref="Sweep"/> sees the
/// pusher and the props; <see cref="PhysicsPushedEntities"/> says how it is
/// ordered so that the first does not change an answer.
/// </para>
/// </summary>
public interface IPushWorld
{
    /// <summary>
    /// What is in a world box: <c>EnumerateElementsInBox</c> over the non-static
    /// edicts. Answers how many were written into <paramref name="into"/>; a
    /// full span means the answer was truncated.
    /// </summary>
    int EntitiesInBox(Vector mins, Vector maxs, Span<EntityHandle> into);

    /// <summary>Solid, player, on ground — and the move type.</summary>
    EntityState State(EntityHandle entity, out MoveType moveType);

    /// <summary>The <c>FSOLID_*</c> bits.</summary>
    SolidFlags SolidFlags(EntityHandle entity);

    /// <summary>The <c>SOLID_*</c> shape.</summary>
    SolidType SolidType(EntityHandle entity);

    /// <summary>The engine's own <c>Collision_Group_t</c>, untranslated.</summary>
    int CollisionGroup(EntityHandle entity);

    /// <summary><c>GetAbsOrigin</c>.</summary>
    Vector Origin(EntityHandle entity);

    /// <summary><c>GetAbsAngles</c>.</summary>
    QAngle Angles(EntityHandle entity);

    /// <summary>The world-space AABB, as offsets from <see cref="Origin"/>.</summary>
    (Vector Mins, Vector Maxs) Bounds(EntityHandle entity);

    /// <summary>
    /// <c>OBBMins</c>/<c>OBBMaxs</c>: the collision box in the entity's own
    /// space. False when there is none to read.
    /// </summary>
    bool CollisionBox(EntityHandle entity, out Vector mins, out Vector maxs);

    /// <summary>The entity index it stands on, -1 for nothing. Only a player has a ground here.</summary>
    int GroundIndex(EntityHandle entity);

    /// <summary>The entity's index, for comparing with <see cref="GroundIndex"/>.</summary>
    int Index(EntityHandle entity);

    /// <summary><c>SetAbsOrigin</c>.</summary>
    void SetOrigin(EntityHandle entity, Vector origin);

    /// <summary><c>SetAbsAngles</c>.</summary>
    void SetAngles(EntityHandle entity, QAngle angles);

    /// <summary>
    /// <c>UTIL_TraceEntity</c>: the entity's own hull from one place to another,
    /// with its own solid mask and collision group, hitting everything but
    /// itself. A sweep from a place to the same place is the "does it fit here"
    /// test.
    /// </summary>
    PushTrace Sweep(EntityHandle entity, Vector start, Vector end);

    /// <summary>
    /// <c>SetPhysicsFlag( PFLAG_GAMEPHYSICS_ROTPUSH, true )</c> on a player:
    /// the next shadow update checks whether the push left them stuck.
    /// </summary>
    void MarkRotPushed(EntityHandle player);
}

/// <summary>
/// <see cref="IPushWorld"/> answered by the engine areas.
///
/// <para>
/// <c>PhysicsSolidMaskForEntity</c> is ported here, because the sweep is the
/// only thing that reads it: <c>MASK_PLAYERSOLID</c> for a player
/// (<c>CBasePlayer::PlayerSolidMask</c>) and <c>MASK_SOLID</c> for anything else
/// (<c>CBaseEntity</c>'s).
/// </para>
/// </summary>
public sealed class EnginePushWorld : IPushWorld
{
    /// <summary>
    /// <c>MASK_SOLID</c>: CONTENTS_SOLID | MOVEABLE | WINDOW | MONSTER | GRATE.
    /// </summary>
    public const uint MaskSolid =
        GameMovement.ContentsSolid | GameMovement.ContentsMoveable | GameMovement.ContentsWindow
        | GameMovement.ContentsMonster | GameMovement.ContentsGrate;

    private static readonly int MinsOffset =
        EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Mins));
    private static readonly int MaxsOffset =
        EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.Maxs));
    private static readonly int GroupOffset =
        EngineBlockMarks.OffsetInBlockOf(nameof(EngineBlockHead.CollisionGroup));

    private readonly IEngineEntities _entities;
    private readonly IEngineWorldQuery _world;
    private readonly IEngineNetworking _net;
    private readonly EntityDirectory _directory;

    public EnginePushWorld(IEngineEntities entities, IEngineWorldQuery world,
                           IEngineNetworking net, EntityDirectory directory)
    {
        _entities = entities;
        _world = world;
        _net = net;
        _directory = directory;
    }

    /// <summary>
    /// The ABI has a sphere and no box, so the box's circumscribed sphere is
    /// walked and the box test is done here. Inclusive at the faces, as the
    /// partition's own enumeration is.
    /// </summary>
    public int EntitiesInBox(Vector mins, Vector maxs, Span<EntityHandle> into)
    {
        Vector centre = (mins + maxs) * 0.5f;
        float radius = ((maxs - mins) * 0.5f).Length;

        int found = _world.EntitiesInSphere(centre, radius, into);
        int kept = 0;

        for (int i = 0; i < found; i++)
        {
            EntityHandle candidate = into[i];
            Vector origin = _entities.GetOrigin(candidate);
            var (cMins, cMaxs) = _entities.GetBounds(candidate);

            Vector lo = origin + cMins;
            Vector hi = origin + cMaxs;

            if (lo.X <= maxs.X && hi.X >= mins.X && lo.Y <= maxs.Y && hi.Y >= mins.Y
                && lo.Z <= maxs.Z && hi.Z >= mins.Z)
            {
                into[kept++] = candidate;
            }
        }

        return kept;
    }

    public EntityState State(EntityHandle entity, out MoveType moveType)
        => _net.EntityGetState(entity, out moveType);

    public SolidFlags SolidFlags(EntityHandle entity) => _entities.GetSolidFlags(entity);

    public SolidType SolidType(EntityHandle entity) => _entities.GetSolidType(entity);

    /// <summary>
    /// Off the engine block, because <c>GetCollisionGroup</c> is many-to-one by
    /// design and the rule table needs the engine's number. Zero
    /// (<c>COLLISION_GROUP_NONE</c>, which collides with everything) when there is
    /// no block to read.
    /// </summary>
    public int CollisionGroup(EntityHandle entity)
    {
        Span<byte> block = _entities.GetEngineBlock(entity);

        return block.Length >= EngineBlock.SizeBytes
            ? MemoryMarshal.Read<int>(block.Slice(GroupOffset))
            : 0;
    }

    public Vector Origin(EntityHandle entity) => _entities.GetOrigin(entity);

    public QAngle Angles(EntityHandle entity) => QAngle.FromVector(_entities.GetAngles(entity));

    public (Vector Mins, Vector Maxs) Bounds(EntityHandle entity) => _entities.GetBounds(entity);

    public bool CollisionBox(EntityHandle entity, out Vector mins, out Vector maxs)
    {
        Span<byte> block = _entities.GetEngineBlock(entity);

        if (block.Length < EngineBlock.SizeBytes)
        {
            mins = maxs = Vector.Zero;
            return false;
        }

        mins = MemoryMarshal.Read<Vector>(block.Slice(MinsOffset));
        maxs = MemoryMarshal.Read<Vector>(block.Slice(MaxsOffset));
        return true;
    }

    public int GroundIndex(EntityHandle entity)
        => _directory.Resolve(entity) is SsPlayer player ? player.GroundIndex : -1;

    public int Index(EntityHandle entity) => _entities.GetIndex(entity);

    public void SetOrigin(EntityHandle entity, Vector origin) => _entities.SetOrigin(entity, origin);

    public void SetAngles(EntityHandle entity, QAngle angles) => _entities.SetAngles(entity, angles.AsVector());

    public PushTrace Sweep(EntityHandle entity, Vector start, Vector end)
    {
        var (mins, maxs) = _entities.GetBounds(entity);
        uint mask = _entities.IsPlayer(entity) ? GameMovement.MaskPlayerSolid : MaskSolid;

        EntityHullTrace trace = _world.EntityTraceHull(entity, start, end, mins, maxs, mask,
                                                       CollisionGroup(entity));

        return new PushTrace(FractionOf(start, end, trace.EndPos, trace.AllSolid),
                             trace.EndPos, trace.StartSolid, trace.AllSolid);
    }

    /// <summary>
    /// The fraction the sweep's answer does not carry, from where it ended: how
    /// far along the way it got. A sweep of no length went all of it unless it
    /// never left solid.
    /// </summary>
    public static float FractionOf(Vector start, Vector end, Vector endPos, bool allSolid)
    {
        if (allSolid)
            return 0f;

        float whole = (end - start).Length;

        if (whole <= 0f)
            return 1f;

        return Math.Clamp((endPos - start).Length / whole, 0f, 1f);
    }

    public void MarkRotPushed(EntityHandle player)
    {
        if (_directory.Resolve(player) is SsPlayer ss)
            ss.PhysicsFlags |= SsPlayer.PhysicsFlagGamePhysicsRotPush;
    }
}
