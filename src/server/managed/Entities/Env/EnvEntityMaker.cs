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
/// env_entity_maker: keeps a supply of something in the world.
///
/// The first entity in this mod that creates other entities from a MAP's
/// description rather than from a managed one. Everything managed code has
/// spawned so far — a grenade, a bolt, a pickup — it knew the shape of, because
/// a C# class described it. A template is a group of entities a mapper drew,
/// and neither the count nor the classes are known here.
///
/// <para>
/// point_template, which holds that description, used to be native and is the
/// reason this class once reached across the boundary for its spawn. It is
/// <see cref="PointTemplate"/> now, so the maker finds it the way one entity
/// finds another — by name, in the registry — and asks it directly. The only
/// thing that crosses is what the template itself cannot do without: the map's
/// keyvalue text and the parser.
/// </para>
///
/// <para>
/// What this class decides is everything around the spawn: when to make one,
/// where, whether there is room, whether a player is watching, and how hard to
/// throw what comes out.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvEntityMaker)]
public sealed class EnvEntityMaker : ScriptedEntity
{
    public EnvEntityMaker(IEngineHost host, IEngineNetworking net, IEngineWorldQuery world)
    {
        Host = host;
        Net = net;
        World = world;
    }

    /// <summary>the process and the match.</summary>
    private readonly IEngineHost Host;

    /// <summary>putting this entity’s state on the wire.</summary>
    private readonly IEngineNetworking Net;

    /// <summary>asking the world what is there.</summary>
    private readonly IEngineWorldQuery World;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="EnvEntityMakerVars"/>.</summary>
    [ScriptVars] public EnvEntityMakerVars Vars = new();

    /// <summary>
    /// env_entity_maker's own spawnflags.
    ///
    /// <para>
    /// Four of the five are conditions on spawning and read as a checklist, which
    /// is why they belong together: a maker that has been given several of them
    /// must satisfy all of them, and that is far easier to see when the type says
    /// they are one set. IgnoreFacing is the odd one out, and reads inverted at its
    /// call site because stock defines it as the negative.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        AutoSpawn = 0b0000_0001,
        WaitForDestruction = 0b0000_0010,
        IgnoreFacing = 0b0000_0100,
        CheckForSpace = 0b0000_1000,
        CheckPlayerLooking = 0b0001_0000,
    }

    /// <summary>How often the automatic version reconsiders. Stock's cadence.</summary>
    private const float CheckInterval = 0.5f;

    public readonly Output OnEntitySpawned = new();
    public readonly Output OnEntityFailedSpawn = new();

    /// <summary>The most recent thing made, which is what "wait for destruction" waits on.</summary>
    private EntityHandle _current = EntityHandle.None;

    /// <summary>
    /// What was in the way last time, and where it was.
    ///
    /// Kept as a pair because the cheap test is "has it moved": something
    /// blocking this spot will usually still be blocking it a half second later,
    /// and comparing one origin is much less work than a hull trace.
    /// </summary>
    private EntityHandle _blocker = EntityHandle.None;
    private Vector _blockerOrigin;

    /// <summary>
    /// The hull to test with, learnt from the first thing spawned.
    ///
    /// It cannot be known before then: the template is a block of unparsed text
    /// and nothing here knows what shape comes out of it. So the first spawn is
    /// never room-checked, in stock as here — there is nothing to check with.
    /// </summary>
    private Vector _mins;
    private Vector _maxs;
    private bool _boundsKnown;

    public override void OnSpawn()
    {
        if (Vars.Template.Length == 0)
        {
            // Stock removes itself here. This warns and stays, for the same
            // reason point_teleport does: an entity that deleted itself at load
            // is invisible to the debugger, and a missing key is exactly what
            // somebody would want to look at.
            Log.Warning($"[SourceSharp] env_entity_maker '{Name}': no EntityTemplate key\n");
            return;
        }

        // Stock does this in Activate, because the template may not have spawned
        // yet. A logical entity here spawns after the whole CBaseEntity world
        // does, so there is nothing left to wait for.
        if (Flags.HasFlag(SpawnFlag.AutoSpawn))
            Spawn(Origin, Entities.GetAngles(Handle));
    }

    public override void OnThink()
    {
        SetNextThink(CheckInterval);

        // Something is still standing there and the map said to wait for it to
        // be destroyed rather than to keep topping the supply up.
        if (Entities.IsValid(_current) && Flags.HasFlag(SpawnFlag.WaitForDestruction))
            return;

        if (!HasRoomToSpawn())
            return;

        if (!Flags.HasFlag(SpawnFlag.IgnoreFacing) && IsPlayerLooking())
            return;

        Spawn(Origin, Entities.GetAngles(Handle));
    }

    [ScriptedEvent("ForceSpawn")]
    private void OnForceSpawn()
    {
        // The two checks are OPTIONAL on a forced spawn and mandatory on the
        // automatic one, which is the difference between the two paths: the
        // timer is topping up a supply and may quietly decline, while an input
        // was asked for by the map and reports when it could not be honoured.
        if (Flags.HasFlag(SpawnFlag.CheckForSpace) && !HasRoomToSpawn())
        {
            OnEntityFailedSpawn.Fire();
            return;
        }

        if (Flags.HasFlag(SpawnFlag.CheckPlayerLooking) && IsPlayerLooking())
        {
            OnEntityFailedSpawn.Fire();
            return;
        }

        Spawn(Origin, Entities.GetAngles(Handle));
    }

    /// <summary>
    /// Spawns somewhere else entirely — at an entity the input names.
    ///
    /// Neither check applies, in stock or here. A map that named a place has
    /// already decided where, and the room check would be testing this entity's
    /// position rather than that one's.
    /// </summary>
    [ScriptedEvent("ForceSpawnAtEntityOrigin")]
    private void OnForceSpawnAtEntityOrigin(string targetName)
    {
        var at = Entities.FindByName(targetName);

        if (!Entities.IsValid(at))
        {
            Log.Warning($"[SourceSharp] env_entity_maker '{Name}': no entity named '{targetName}' to spawn at\n");
            return;
        }

        Spawn(Entities.GetOrigin(at), Entities.GetAngles(at));
    }

    private void Spawn(Vector origin, Vector angles)
    {
        // By name through the registry rather than through the engine. The
        // template is a managed entity now, so this is one C# object asking
        // another — and the maker gets the same answer whichever hierarchy the
        // things inside the template turn out to live on.
        var template = FindByName<PointTemplate>(Vars.Template);

        if (template is null)
        {
            Log.Warning($"[SourceSharp] env_entity_maker '{Name}': nothing named '{Vars.Template}' " +
                        "is a point_template\n");
            return;
        }

        var made = template.CreateInstance(origin, QAngle.FromVector(angles));

        if (made.Count == 0)
        {
            Log.Warning($"[SourceSharp] env_entity_maker '{Name}': template '{Vars.Template}' made nothing\n");
            return;
        }

        _current = made[0];

        // Assume what was just made is what will block the next one, because it
        // is standing exactly where the next one goes. Stock's assumption, and
        // it is what makes the cheap "has it moved" test work at all.
        _blocker = _current;
        _blockerOrigin = Entities.GetOrigin(_current);

        if (!_boundsKnown)
        {
            (_mins, _maxs) = Entities.GetBounds(_current);
            _boundsKnown = true;
        }

        OnEntitySpawned.Fire();

        if (Vars.PostSpawnSpeed != 0f)
            Throw(made, angles);

        // Started here rather than at spawn, so a maker that has never made
        // anything is not running a timer. The automatic flag is what asks for
        // the supply to be kept up.
        if (Flags.HasFlag(SpawnFlag.AutoSpawn))
            SetNextThink(CheckInterval);
    }

    /// <summary>
    /// Gives everything just made a shove.
    ///
    /// The scatter comes from the ENGINE's random stream rather than a managed
    /// one, and that is a correctness point rather than a convenience: server
    /// randomness is replayed by demo playback and has to come out the same.
    /// </summary>
    private void Throw(List<EntityHandle> made, Vector spawnAngles)
    {
        Vector direction = Vars.PostSpawnDirection;

        if (Vars.PostSpawnInheritAngles)
            direction += spawnAngles;

        var basis = Angles.Vectors(direction);

        foreach (var entity in made)
        {
            // Something the engine does not move cannot be thrown, and asking
            // would set a velocity that nothing reads.
            Net.EntityGetState(entity, out MoveType move);

            if (move == MoveType.None)
                continue;

            Entities.SetVelocity(entity, Scatter(basis, Vars.PostSpawnVariance) * Vars.PostSpawnSpeed);
        }
    }

    /// <summary>
    /// One scattered direction, drawn fresh per entity so a group does not fly
    /// out in formation.
    ///
    /// Static and taking the basis so it can be reasoned about without an
    /// engine — though not tested with one, since the whole of it is the random
    /// draw. Stock adds the variance along forward TWICE and along right and up
    /// once each, which biases the scatter forwards. Kept: it is what tuned
    /// values in existing maps were tuned against.
    /// </summary>
    private Vector Scatter(Angles.Basis basis, float variance)
    {
        if (variance == 0f)
            return basis.Forward;

        Vector shoot = basis.Forward;

        shoot += basis.Right   * Host.RandomFloat(-1f, 1f) * variance;
        shoot += basis.Forward * Host.RandomFloat(-1f, 1f) * variance;
        shoot += basis.Up      * Host.RandomFloat(-1f, 1f) * variance;

        return shoot.Normalized;
    }

    /// <summary>
    /// Whether the next one would fit.
    ///
    /// Answers true before anything has ever been spawned, because the hull is
    /// learnt from the first instance and there is nothing to test with until
    /// then. Stock behaves the same way and for the same reason.
    /// </summary>
    private bool HasRoomToSpawn()
    {
        // The cheap test first: whatever blocked this spot last time is probably
        // still there, and one origin comparison is much less work than a trace.
        if (Entities.IsValid(_blocker) && Same(Entities.GetOrigin(_blocker), _blockerOrigin))
            return false;

        if (!_boundsKnown)
            return true;

        var hull = World.TraceHull(Origin, Origin, _mins, _maxs, TraceMask.Shot, Handle);

        if (!hull.Blocked && !hull.StartSolid)
            return true;

        // Remember what it was, so the next check can be the cheap one. A hull
        // that STARTED solid often names nothing — there was no surface to
        // cross — and leaving the old blocker in place would be worse than
        // clearing it, since the cheap test would then compare against an
        // entity that is no longer the reason.
        _blocker = hull.Entity;
        _blockerOrigin = Entities.IsValid(_blocker) ? Entities.GetOrigin(_blocker) : Vector.Zero;

        return false;
    }

    /// <summary>
    /// Exactly where it was, to the bit.
    ///
    /// An exact comparison and not a tolerance, which is stock's and is right
    /// here: the question is "has this moved AT ALL since I looked", and
    /// something resting on the floor writes back an identical origin every
    /// tick. A tolerance would make a slowly sliding crate read as stationary
    /// and the spot would never be reconsidered.
    /// </summary>
    private static bool Same(Vector a, Vector b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    /// <summary>
    /// Whether anybody can see this spot.
    ///
    /// A dot greater than zero, which is a 180-degree cone rather than a view
    /// frustum — so this is "roughly towards" and not "on screen". Stock's test,
    /// and the looseness is deliberate: appearing just off the edge of somebody's
    /// screen is as bad as appearing in the middle of it.
    /// </summary>
    private bool IsPlayerLooking()
    {
        foreach (var player in Entities.FindAllByClassname("player"))
        {
            Vector eye = Entities.GetEyePosition(player);
            Vector toward = (Origin - eye).Normalized;

            if (Angles.Forward(Entities.GetEyeAngles(player)).Dot(toward) > 0f)
                return true;
        }

        return false;
    }
}

/// <summary>
/// The map keys <see cref="EnvEntityMaker"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvEntityMakerVars
{
    /// <summary>Name of the point_template holding what to spawn.</summary>
    [VarName("EntityTemplate")]
    public string Template { get; set; } = string.Empty;

    /// <summary>Which way to throw what was spawned, as pitch/yaw/roll.</summary>
    public Vector PostSpawnDirection { get; set; }

    /// <summary>How much to scatter that direction. Zero throws everything the same way.</summary>
    [VarName("PostSpawnDirectionVariance")]
    public float PostSpawnVariance { get; set; }

    /// <summary>How hard. Zero — the default — spawns things at rest.</summary>
    public float PostSpawnSpeed { get; set; }

    /// <summary>Read the throw direction as relative to this entity's own facing.</summary>
    public bool PostSpawnInheritAngles { get; set; }
}
