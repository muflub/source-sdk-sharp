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
/// Entities that act on whatever happens to be near them.
///
/// The constraints and the controllers both work on a NAMED thing: the map says
/// what to attach to and the entity resolves it once. These do not. An
/// explosion, a push field and an impact all begin by asking the world what is
/// in range, and then decide per candidate whether it is worth acting on.
///
/// That question is the only new primitive this group needed —
/// <see cref="IEngineWorldQuery.EntitiesInSphere"/> — and the shape it forces is worth
/// naming. A radius query answers with everything: decorations, triggers, the
/// world brush itself. The filtering below is not defensive coding; it IS the
/// entity. What an explosion chooses to ignore is what distinguishes it from a
/// push field looking at the same list.
/// </summary>
public abstract class PhysicsEmitter : ScriptedEntity
{
    public PhysicsEmitter(IEngineWorldQuery world)
    {
        World = world;
    }

    /// <summary>asking the world what is there.</summary>
    protected readonly IEngineWorldQuery World;
    /// <summary>
    /// How many entities one query can return.
    ///
    /// Stock uses 256 and so does this. The number matters because the engine
    /// answers in no particular order — a truncated query does not drop the
    /// FARTHEST entities, it drops arbitrary ones — so a blast that overflows
    /// is not merely reduced, it is unpredictable. Hence <see cref="Query"/>
    /// says so out loud rather than silently returning a short list.
    /// </summary>
    protected const int MaxInRange = 256;

    /// <summary>
    /// Everything within a radius, complained about if the answer was cut off.
    ///
    /// Returns how many of <paramref name="buffer"/> were filled.
    /// </summary>
    protected int Query(Vector center, float radius, Span<EntityHandle> buffer)
    {
        int count = World.EntitiesInSphere(center, radius, buffer);

        if (count == buffer.Length)
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': more than {buffer.Length} entities " +
                           $"within {radius:F0} units; some were not affected\n");
        }

        return count;
    }

    /// <summary>
    /// Whether a wall stands between here and there.
    ///
    /// Brush-only, so the answer is about cover rather than clutter: a crate in
    /// the way is something the blast throws, not something it stops at. An
    /// inner radius moves the start point outward, which is how a mapper puts
    /// the source of a blast outside the thing it is buried in.
    /// </summary>
    protected bool IsShielded(Vector from, EntityHandle target, float innerRadius)
    {
        Vector to = Entities.GetWorldSpaceCenter(target);
        Vector start = from;

        if (innerRadius != 0f)
            start = from + (to - from).Normalized * innerRadius;

        var trace = World.TraceLine(start, to, TraceMask.Brushes, Handle);

        // Hitting the target itself is not shielding: the line arrived.
        return trace.Hit && trace.Entity != target;
    }

    /// <summary>
    /// Which way the entity points.
    ///
    /// Read from its CURRENT angles rather than its spawn angles, which is the
    /// opposite of what a brush entity wants and is worth saying why. The
    /// engine consumes a brush's angles during spawn — for a sliding door they
    /// are a travel direction, not a rotation — so a brush has to remember what
    /// the map wrote. Nothing consumes a point entity's angles, so its current
    /// ones are still the mapper's, and asking for spawn angles gets zero: they
    /// are only recorded by the brush and prop shells.
    /// </summary>
    protected Vector Facing => Angles.Vectors(Entities.GetAngles(Handle)).Forward;

    /// <summary>
    /// Source's RemapValClamped. One over a range, clamped at both ends, and
    /// clamped to the near end when the range is degenerate — a falloff over
    /// zero distance is full strength, not a division by zero.
    /// </summary>
    internal static float Remap(float value, float a, float b, float toA, float toB)
    {
        if (a == b)
            return value >= b ? toB : toA;

        float t = Math.Clamp((value - a) / (b - a), 0f, 1f);
        return toA + (toB - toA) * t;
    }
}
