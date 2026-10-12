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
/// Where a body should fall.
///
/// <para>
/// A mapper places one of these beside the ledge a dead guard is meant to drop
/// over, and the extra shove it contributes is added to the death force before
/// the ragdoll is built. It has no model, no collision and no think — it is a
/// position, a reach and a pull, and the whole of it is arithmetic on those
/// three.
/// </para>
///
/// <para>
/// Two shapes, chosen by a spawnflag. A POINT magnet is a sphere and pulls
/// towards its origin. A BAR magnet is the cylinder from its origin to
/// <c>axis</c> and pulls towards the nearest place on that line, which is how a
/// map aims bodies at a railing rather than at a spot on it.
/// </para>
///
/// <para>
/// The REGISTRY is the server module's <see cref="IEntityRoster{T}"/> of
/// magnets (it was a static list here), the same shape and for the same reason
/// as <see cref="EnvFire"/>'s: stock finds magnets with
/// <c>gEntList.FindEntityByClassname</c> on every death, and with the magnets
/// managed there is nothing left for that walk to find. The one native caller
/// that still needs it — <c>CBaseCombatCharacter::Event_Killed</c> — reaches it
/// through <c>OnRagdollMagnetForce</c>, and it asks for the whole answer rather
/// than for a magnet: <c>GetForceVector</c>, <c>GetRadius</c>, <c>DistToPoint</c>
/// and <c>IsBarMagnet</c> are all <c>CRagdollMagnet</c>'s own members, so there
/// is no pointer the native side could usefully be handed.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.PhysRagdollmagnet)]
public sealed class PhysRagdollMagnet : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="PhysRagdollMagnetVars"/>.</summary>
    [ScriptVars] public PhysRagdollMagnetVars Vars = new();

    /// <summary>Stock's SF_RAGDOLLMAGNET_BAR. Two, not one — bit zero is unused here.</summary>
    private const int BarFlag = 0x02;

    /// <summary>The module's magnets, which this one joins while it is live.</summary>
    public PhysRagdollMagnet(IEntityRoster<PhysRagdollMagnet> magnets)
    {
        _magnets = magnets;
    }

    private readonly IEntityRoster<PhysRagdollMagnet> _magnets;

    public override void OnSpawn() => _magnets.Add(this);

    public override void OnRemoved() => _magnets.Remove(this);

    [ScriptedEvent("Enable")]
    private void OnEnable() => Vars.Disabled = false;

    [ScriptedEvent("Disable")]
    private void OnDisable() => Vars.Disabled = true;

    public bool IsEnabled => !Vars.Disabled;

    /// <summary>A bar rather than a point. Read at every ask, since a spawnflag cannot change.</summary>
    public bool IsBar => (SpawnFlags & BarFlag) != 0;

    public float Radius => Vars.Radius;

    /// <summary>The one entity this magnet serves, or empty when it serves whoever is nearest.</summary>
    public string Target => Vars.Target;

    //-------------------------------------------------------------------------
    // The two questions a death asks a magnet, as instance methods reading live
    // position. Both take the dying entity's world-space centre rather than its
    // handle, because that is the only thing about it either answer depends on
    // and a caller that already has the centre should not be made to hand over
    // an entity so this can look it up again.
    //-------------------------------------------------------------------------

    public float DistanceTo(Vector point) => DistanceTo(point, Origin, Vars.Axis, IsBar);

    public Vector PullOn(Vector point) => PullOn(point, Origin, Vars.Axis, IsBar, Vars.Force);

    /// <summary>
    /// The magnet that should pull this entity's body, or null.
    ///
    /// <para>
    /// A TARGETED magnet wins outright — the first one naming this entity is
    /// returned whatever its radius says, which is stock's behaviour and is the
    /// point of naming one: a map that says "this body goes here" means it.
    /// Otherwise the nearest magnet whose reach covers the entity wins, and a
    /// disabled magnet is not considered at all.
    /// </para>
    /// </summary>
    /// <param name="magnets">The magnets to choose among: the module's roster.</param>
    public static PhysRagdollMagnet? Best(IEnumerable<PhysRagdollMagnet> magnets, EntityHandle subject, Vector centre)
    {
        PhysRagdollMagnet? best = null;
        float closest = float.MaxValue;

        // Read at most once, and only if a targeted magnet is actually met. Most
        // maps have none, and this runs on every death in the map.
        string? name = null;

        foreach (var magnet in magnets)
        {
            if (!magnet.IsEnabled)
                continue;

            if (magnet.Vars.Target.Length > 0)
            {
                // Through the MAGNET's own engine: the walk is over the roster
                // and is entered from the module on every death, so there
                // is no "this" here — but every magnet in the list has one.
                name ??= magnet.Entities.GetName(subject);

                // Ordinal, because stock compares pooled string_t handles and
                // two spellings that differ in case are two different pooled
                // strings. A map that got the case wrong got no magnet there
                // either.
                if (string.Equals(name, magnet.Vars.Target, StringComparison.Ordinal))
                    return magnet;

                // Named for somebody else, so it is not available to anyone —
                // not even to whoever it happens to be standing next to.
                continue;
            }

            float distance = magnet.DistanceTo(centre);

            if (distance < closest && distance <= magnet.Vars.Radius)
            {
                closest = distance;
                best = magnet;
            }
        }

        return best;
    }

    //-------------------------------------------------------------------------
    // The arithmetic, as static functions of everything they read.
    //
    // Separated from the entity so the rules can be checked without a live map,
    // which is the same shape PhysMagnet.Sticks has and for the same reason: a
    // magnet's geometry is where the mistakes are, and a mistake in it is
    // invisible in play — a body simply falls the way it would have anyway.
    //-------------------------------------------------------------------------

    /// <summary>
    /// How far a point is from this magnet, which means something different for
    /// each shape.
    ///
    /// <para>
    /// A point magnet answers the plain distance. A bar magnet answers only for
    /// points BETWEEN its two ends — anything past either cap is infinitely far,
    /// so a body beyond the end of a railing is not pulled sideways onto it —
    /// and for those it answers the larger of the two offsets from the bar's
    /// line, measured along that line's own right and up. That makes the reach a
    /// square cross-section rather than a round one, which is stock's shape and
    /// is kept: rounding it would move where bodies are caught in every map that
    /// already has one.
    /// </para>
    /// </summary>
    public static float DistanceTo(Vector point, Vector origin, Vector axis, bool bar)
    {
        if (!bar)
            return (origin - point).Length;

        Vector along = (axis - origin).Normalized;

        // Between the caps, which are the two planes facing each other at the
        // ends of the bar. Level with a cap counts as inside, because stock's
        // PointInFront answers on `>= 0` — so a body standing exactly at the end
        // of a railing is caught by it rather than missed by a rounding error.
        // The far cap's plane faces back down the bar, so its test is the same
        // dot product with the sign the other way round.
        if (along.Dot(point - origin) < 0f || along.Dot(point - axis) > 0f)
            return float.MaxValue;

        (Vector right, Vector up) = Perpendiculars(along);

        float horizontal = MathF.Abs(right.Dot(point - origin));
        float vertical = MathF.Abs(up.Dot(point - origin));

        return MathF.Max(horizontal, vertical);
    }

    /// <summary>
    /// The pull itself: towards the origin for a point magnet, towards the
    /// nearest place on the bar for a bar one.
    ///
    /// <para>
    /// Assumes the point is already within reach — stock says so too, and it is
    /// why <see cref="Best"/> asks <see cref="DistanceTo"/> first rather than
    /// this. A magnet asked about somewhere it does not reach still answers, and
    /// the answer is a full-strength pull from wherever that is.
    /// </para>
    /// </summary>
    public static Vector PullOn(Vector point, Vector origin, Vector axis, bool bar, float force)
    {
        Vector towards = bar ? ClosestPointOnSegment(point, origin, axis) - point
                             : origin - point;

        return towards.Normalized * force;
    }

    /// <summary>
    /// Two unit vectors at right angles to a third, matching mathlib's
    /// <c>VectorVectors</c> exactly — including its choice for a straight-up
    /// axis, where there is no world up to cross against and it falls back to a
    /// fixed pair. A vertical bar magnet is the common case (a stairwell, a
    /// lift shaft), so that fallback is not a corner nobody reaches.
    /// </summary>
    private static (Vector Right, Vector Up) Perpendiculars(Vector forward)
    {
        if (forward.X == 0f && forward.Y == 0f)
            return (new Vector(0f, -1f, 0f), new Vector(-forward.Z, 0f, 0f));

        Vector right = forward.Cross(new Vector(0f, 0f, 1f)).Normalized;

        return (right, right.Cross(forward).Normalized);
    }

    /// <summary>
    /// The nearest place on the segment from <paramref name="a"/> to
    /// <paramref name="b"/>, clamped to the segment's ends as mathlib's
    /// <c>CalcClosestPointOnLineSegment</c> clamps it.
    /// </summary>
    private static Vector ClosestPointOnSegment(Vector point, Vector a, Vector b)
    {
        Vector direction = b - a;
        float lengthSquared = direction.LengthSquared;

        if (lengthSquared <= 0f)
            return a;

        float t = Math.Clamp(direction.Dot(point - a) / lengthSquared, 0f, 1f);

        return a + direction * t;
    }
}

/// <summary>
/// The map keys <see cref="PhysRagdollMagnet"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysRagdollMagnetVars
{
    /// <summary>How far the magnet reaches. Measured differently by each shape.</summary>
    public float Radius { get; set; }

    /// <summary>How hard it pulls, in the same units as a death force.</summary>
    public float Force { get; set; }

    /// <summary>
    /// The far end of a bar magnet, as a WORLD position rather than a direction —
    /// Hammer draws it as a line you drag, and stock subtracts the origin every
    /// time it wants the direction. A point magnet ignores it.
    /// </summary>
    public Vector Axis { get; set; }

    [VarName("StartDisabled")]
    public bool Disabled { get; set; }

    /// <summary>
    /// One entity this magnet is for, and nothing else. A CBaseEntity key rather
    /// than one this entity declares, which is why stock reads it off the magnet
    /// without <c>phys_ragdollmagnet</c> mentioning it anywhere.
    /// </summary>
    public string Target { get; set; } = string.Empty;
}
