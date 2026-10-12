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
/// A single shove along a line: env_physimpact.
///
/// The odd one out. It does not query a radius at all — it traces where it is
/// pointed and hits the first thing it finds, which makes it the map's way of
/// saying "knock THAT over, from THIS side" without the mapper having to name
/// the target.
///
/// The force lands at the point of contact rather than at the centre of mass,
/// which is the entire reason it exists: hitting a crate near a corner tips it,
/// and that is what a mapper is drawing when they place one.
/// </summary>
[ScriptedEntity(EntityNames.EnvPhysimpact)]
public sealed class PhysImpact : PhysicsEmitter
{
    public PhysImpact(IEngineWorldQuery world, IEngineNetworking net, IEngineEntityPhysics physics)
        : base(world)
    {
        Net = net;
        Physics = physics;
    }

    /// <summary>putting this entity’s state on the wire.</summary>
    private readonly IEngineNetworking Net;

    /// <summary>this entity as the solver sees it.</summary>
    private readonly IEngineEntityPhysics Physics;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="PhysImpactVars"/>.</summary>
    [ScriptVars] public PhysImpactVars Vars = new();

    /// <summary>
    /// env_physimpact's own spawnflags, from physobj.cpp's SF_PHYSIMPACT_*.
    ///
    /// <para>
    /// The first two are related in a way the numbers do not show: an infinite
    /// line is over fifty thousand units long, so every falloff fraction along it
    /// rounds to nothing and the length flag has to imply the falloff flag. That
    /// implication lives in <see cref="Force"/>, and having both bits in one type
    /// is what makes it a readable expression rather than two loose ints.
    /// </para>
    /// </summary>
    [Flags]
    internal enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        NoFalloff = 0b0000_0001,
        InfiniteLength = 0b0000_0010,
        IgnoreMass = 0b0000_0100,
        IgnoreNormal = 0b0000_1000,
    }

    private const float DefaultDistance = 256f;
    private const float MaxTraceLength = 56756f;

    [ScriptedEvent("Impact")]
    private void OnImpact() => Impact();

    /// <summary>Exposed for tests, which have no map to fire an input from.</summary>
    internal void Impact()
    {
        Vector start = Origin;
        Vector direction = Direction(start);

        float distance = Flags.HasFlag(SpawnFlag.InfiniteLength)
            ? MaxTraceLength
            : (Vars.Distance > 0f ? Vars.Distance : DefaultDistance);

        var trace = World.TraceLine(start, start + direction * distance, TraceMask.Shot, Handle);

        if (!trace.Hit || !Entities.IsValid(trace.Entity))
            return;

        var state = Net.EntityGetState(trace.Entity, out _);

        // Nothing to push. Not a failure: the map pointed this at a wall, and a
        // wall is a perfectly ordinary thing for a line to end at.
        if (!state.HasFlag(EntityState.Physics))
            return;

        float force = Force(Vars.Magnitude, trace.Fraction, (SpawnFlag)SpawnFlags);

        if (Flags.HasFlag(SpawnFlag.IgnoreMass))
            force *= Physics.GetMass(trace.Entity);

        // Along the surface normal by default, which is more predictable than
        // the direction the line was travelling: a glancing hit shoves the
        // target away from the wall rather than along it.
        Vector push = Flags.HasFlag(SpawnFlag.IgnoreNormal) ? direction : trace.Normal * -1f;

        Physics.ApplyForceOffset(trace.Entity, push * force, trace.End);
    }

    /// <summary>
    /// How hard the shove is.
    ///
    /// Falloff is by trace FRACTION, not by distance: the same prop hit by an
    /// impact that reaches further takes less, because it is a smaller part of
    /// the way along the line. That is stock's rule, and it is why the
    /// infinite-length flag also has to turn falloff off — over 56,000 units
    /// every fraction rounds to nothing.
    /// </summary>
    internal static float Force(float magnitude, float fraction, SpawnFlag spawnFlags)
        // Either flag, not both: HasFlag of the two ORed would ask for both,
        // and an infinite-length impact usually sets only its own bit.
        => spawnFlags.HasFlag(SpawnFlag.NoFalloff) || spawnFlags.HasFlag(SpawnFlag.InfiniteLength)
            ? magnitude
            : magnitude * (1f - fraction);

    /// <summary>
    /// Which way the line goes: at a named entity if the map gave one, and
    /// otherwise wherever the mapper pointed it.
    /// </summary>
    private Vector Direction(Vector from)
    {
        if (Vars.DirectionEntityName.Length > 0)
        {
            var target = Entities.FindByName(Vars.DirectionEntityName);

            if (Entities.IsValid(target))
                return (Entities.GetWorldSpaceCenter(target) - from).Normalized;

            Log.Warning($"[SourceSharp] {ClassName} '{Name}': no entity named " +
                           $"'{Vars.DirectionEntityName}'; using its own angles\n");
        }

        return Facing;
    }
}

/// <summary>
/// The map keys <see cref="PhysImpact"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysImpactVars
{
    public float Magnitude { get; set; }

    /// <summary>How far the line reaches. Zero takes stock's default.</summary>
    public float Distance { get; set; }

    /// <summary>Point at this entity instead of using the mapper's angles.</summary>
    public string DirectionEntityName { get; set; } = string.Empty;
}
