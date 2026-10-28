//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// <c>sv_debug_player_use</c> (baseplayer_shared.cpp:1048): FindUseEntity
/// says what it found. The overlays stock draws beside the lines have no
/// managed crossing; the lines are stock's.
/// </summary>
[ConVar("sv_debug_player_use", "0", Flags = ConVarFlags.Replicated)]
public sealed class SvDebugPlayerUseConVar : BoolConVar { }

/// <summary>What a player's +USE is aimed at, or None.</summary>
public interface IUseTargetFinder
{
    EntityHandle Find(EntityHandle player);
}

/// <summary>
/// CBasePlayer::FindUseEntity (baseplayer_shared.cpp:1067): what a player's
/// +USE is aimed at. A line straight ahead and seven boxes swept down at
/// steeper angles find what is under the crosshair; the one straight ahead
/// wins outright. Then everything within <see cref="UseRadius"/> that asks to
/// be found by radius, roughly in front and not behind glass, competes on how
/// near it lies to the view.
///
/// <para>
/// Left out, with nothing on this side to reach them: the ally-NPC double
/// checks (no NPCs), the debug overlays, and the ground entity's
/// FCAP_USE_ONGROUND claim, which only func_tracktrain makes and which a
/// tracktrain can never be the ground of a player here (FuncTrackTrain's
/// OnControls).
/// </para>
/// </summary>
public sealed class PlayerUseFinder : IUseTargetFinder
{
    /// <summary>PLAYER_USE_RADIUS (baseplayer_shared.h).</summary>
    public const float UseRadius = 80f;

    /// <summary>How far the straight-ahead line reaches.</summary>
    public const float LineLength = 1024f;

    /// <summary>How far each of the downward boxes is swept.</summary>
    public const float BoxLength = 72f;

    /// <summary>The swept boxes' half-size.</summary>
    public const float BoxHalf = 16f;

    /// <summary>How square on the view a radius candidate must be (the cosine of its angle off it).</summary>
    public const float RadiusMinDot = 0.8f;

    /// <summary>
    /// MASK_SOLID | CONTENTS_DEBRIS | CONTENTS_PLAYERCLIP: some debris is
    /// usable, and a button may be made of clip brushes.
    /// </summary>
    public const uint UseableContents = 0x1u | 0x4000u | 0x2u | 0x2000000u | 0x8u | 0x4000000u | 0x10000u;

    /// <summary>FSOLID_FORCE_WORLD_ALIGNED: a box that does not turn with its entity.</summary>
    private const SolidFlags ForceWorldAligned = (SolidFlags)0x0040;

    /// <summary>
    /// The sweeps' slopes below the view: straight ahead, then 45, 30, 20, 15
    /// and 10 degrees down, and 10 and 15 up.
    /// </summary>
    private static ReadOnlySpan<float> Tangents =>
    [
        0f, 1f, 0.57735026919f, 0.3639702342f, 0.267949192431f, 0.1763269807f, -0.1763269807f, -0.267949192431f,
    ];

    private readonly IEngineWorldQuery _world;
    private readonly IEngineEntities _entities;
    private readonly IEntityDirectory _directory;
    private readonly ConVarRegistry _convars;
    private readonly ILog _log;

    // Scratch for the radius query.
    private readonly EntityHandle[] _near = new EntityHandle[256];

    public PlayerUseFinder(IEngineWorldQuery world, IEngineEntities entities, IEntityDirectory directory,
                           ConVarRegistry convars, ILog log)
    {
        _world = world;
        _entities = entities;
        _directory = directory;
        _convars = convars;
        _log = log;
    }

    private bool Debugging => _convars.Get<SvDebugPlayerUseConVar>()?.Value ?? false;

    /// <summary>GetDebugName: the name, else the classname.</summary>
    private string DebugName(EntityHandle entity)
    {
        if (entity == EntityHandle.None)
            return "no usable entity found";

        string name = _entities.GetName(entity);
        return name.Length > 0 ? name : _entities.GetClassName(entity);
    }

    /// <summary>What <paramref name="player"/>'s +USE would use, or None.</summary>
    public EntityHandle Find(EntityHandle player)
    {
        MoveVec.AngleVectors(_entities.GetEyeAngles(player), out Vector forward, out _, out Vector up);
        Vector searchCenter = _entities.GetEyePosition(player);

        // The player's own box, for the height test below: stock measures from
        // its WorldSpaceCenter().z PLUS the box's mins and maxs, centre-relative
        // offsets added to a centre that is already raised, and that is kept.
        var (playerMins, playerMaxs) = _entities.GetBounds(player);
        float centerZ = _entities.GetOrigin(player).Z + (playerMins.Z + playerMaxs.Z) * 0.5f;

        EntityHandle nearest = EntityHandle.None;
        Vector box = new(BoxHalf, BoxHalf, BoxHalf);

        for (int i = 0; i < Tangents.Length; i++)
        {
            EntityHullTrace tr;
            if (i == 0)
            {
                tr = _world.EntityTraceHull(player, searchCenter, searchCenter + forward * LineLength,
                                            Vector.Zero, Vector.Zero, UseableContents, (int)CollisionGroup.None);
            }
            else
            {
                Vector down = forward - up * Tangents[i];
                MoveVec.Normalize(ref down);
                tr = _world.EntityTraceHull(player, searchCenter, searchCenter + down * BoxLength,
                                            box * -1f, box, UseableContents, (int)CollisionGroup.None);
            }

            // Up the move-parent chain until something is usable. A chain ends
            // at None (or a parent naming itself), not at whatever the engine
            // says of None's validity.
            EntityHandle candidate = tr.Entity;
            bool usable = IsUseable(candidate, UseCaps.None);
            while (!usable && candidate != EntityHandle.None)
            {
                EntityHandle parent = _entities.GetParent(candidate);
                if (parent == EntityHandle.None || parent == candidate || !_entities.IsValid(parent))
                    break;

                candidate = parent;
                usable = IsUseable(candidate, UseCaps.None);
            }

            if (!usable)
                continue;

            Vector delta = tr.EndPos - searchCenter;
            delta.Z = IntervalDistance(tr.EndPos.Z, centerZ + playerMins.Z, centerZ + playerMaxs.Z);
            if (delta.Length >= UseRadius)
                continue;

            if (Debugging)
                _log.Msg($"Trace using: {DebugName(candidate)}\n");

            nearest = candidate;

            // Directly under the crosshair: taken at once.
            if (i == 0)
                return candidate;
        }

        float nearestDist = float.MaxValue;
        if (nearest != EntityHandle.None)
        {
            nearestDist = DistanceToLine(NearestPoint(nearest, searchCenter), searchCenter, forward);
            if (Debugging)
                _log.Msg($"Trace found {_entities.GetClassName(nearest)}, dist {nearestDist:F2}\n");
        }

        int count = _world.EntitiesInSphere(searchCenter, UseRadius, _near);
        for (int k = 0; k < count; k++)
        {
            EntityHandle candidate = _near[k];
            if (!IsUseable(candidate, UseCaps.UseInRadius))
                continue;

            // Roughly in front.
            Vector point = NearestPoint(candidate, searchCenter);
            Vector dir = point - searchCenter;
            MoveVec.Normalize(ref dir);
            if (dir.Dot(forward) < RadiusMinDot)
                continue;

            float dist = DistanceToLine(point, searchCenter, forward);
            if (Debugging)
                _log.Msg($"Radius found {_entities.GetClassName(candidate)}, dist {dist:F2}\n");

            if (dist >= nearestDist)
                continue;

            // A radius search sees through glass and grates; a line does not.
            EntityHullTrace occluded = _world.EntityTraceHull(player, searchCenter, point, Vector.Zero, Vector.Zero,
                                                              UseableContents, (int)CollisionGroup.None);
            if (occluded.Fraction >= 1f || occluded.Entity == candidate)
            {
                nearest = candidate;
                nearestDist = dist;
            }
        }

        if (Debugging)
            _log.Msg($"Radial using: {DebugName(nearest)}\n");

        return nearest;
    }

    /// <summary>
    /// CBasePlayer::IsUseableEntity: one of the four use bits, and every bit
    /// the search asks for.
    /// </summary>
    public bool IsUseable(EntityHandle entity, UseCaps required)
    {
        if (!_entities.IsValid(entity) || _directory.Get(entity) is not { } instance)
            return false;

        UseCaps caps = instance.ObjectCaps;
        return (caps & UseCaps.Usable) != 0 && (caps & required) == required;
    }

    /// <summary>
    /// CCollisionProperty::CalcNearestPoint: the point of the entity's box
    /// nearest <paramref name="world"/>, the box turned with the entity unless
    /// it is world-aligned (as SourceSharp_BoundsInEntitySpace rules).
    /// </summary>
    public Vector NearestPoint(EntityHandle entity, Vector world)
    {
        Vector origin = _entities.GetOrigin(entity);
        var (mins, maxs) = _entities.GetBounds(entity);
        Vector angles = _entities.GetAngles(entity);

        if (!InEntitySpace(_entities.GetSolidType(entity), _entities.GetSolidFlags(entity))
            || (angles.X == 0f && angles.Y == 0f && angles.Z == 0f))
        {
            return Clamp(world - origin, mins, maxs) + origin;
        }

        // The box's frame: forward, left and up (AngleMatrix's columns).
        MoveVec.AngleVectors(angles, out Vector forward, out Vector right, out Vector up);
        Vector left = right * -1f;

        Vector d = world - origin;
        Vector local = Clamp(new Vector(d.Dot(forward), d.Dot(left), d.Dot(up)), mins, maxs);
        return origin + forward * local.X + left * local.Y + up * local.Z;
    }

    /// <summary>CCollisionProperty::IsBoundsDefinedInEntitySpace.</summary>
    public static bool InEntitySpace(SolidType solid, SolidFlags flags)
        => (flags & ForceWorldAligned) == 0 && solid != SolidType.BoundingBox && solid != SolidType.None;

    /// <summary>
    /// IntervalDistance (baseplayer_shared.cpp:1050): how far <paramref name="x"/>
    /// lies outside the interval, 0 inside it.
    /// </summary>
    public static float IntervalDistance(float x, float x0, float x1)
    {
        if (x0 > x1)
            (x0, x1) = (x1, x0);

        if (x < x0)
            return x0 - x;
        if (x > x1)
            return x - x1;
        return 0f;
    }

    /// <summary>
    /// CalcDistanceToLine (mathlib_base.cpp:3122) as FindUseEntity calls it,
    /// with the view DIRECTION as the line's second point: stock's line runs
    /// from the eye to the point that direction names, near the world's
    /// origin, and the ranking it gives is stock's.
    /// </summary>
    public static float DistanceToLine(Vector p, Vector lineA, Vector lineB)
    {
        Vector dir = lineB - lineA;
        float div = dir.Dot(dir);
        float t = div < 0.00001f ? 0f : (dir.Dot(p) - dir.Dot(lineA)) / div;
        Vector closest = lineA + dir * t;
        return (p - closest).Length;
    }

    private static Vector Clamp(Vector v, Vector mins, Vector maxs)
        => new(Math.Clamp(v.X, mins.X, maxs.X), Math.Clamp(v.Y, mins.Y, maxs.Y), Math.Clamp(v.Z, mins.Z, maxs.Z));
}
