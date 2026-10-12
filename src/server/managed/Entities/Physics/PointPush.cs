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
/// A field that shoves everything near it, for as long as it is on: point_push.
///
/// The one emitter that runs continuously rather than on an input, which is
/// what makes it the expensive one — a radius query every 0.05 seconds, and the
/// entity has no idea whether anything is there to push.
///
/// The physics push is scaled by the server's FRAME time, as stock's is, inside a
/// think that runs every 0.05 seconds. That is not a framerate dependence: on
/// the server, gpGlobals->frametime during thinks is the tick interval. This
/// once scaled by the 0.05 s since the last push instead, which pushed 3.3
/// times harder than stock (plan_physics_managed A27).
/// </summary>
[ScriptedEntity(EntityNames.PointPush)]
public sealed class PointPush : PhysicsEmitter
{
    public PointPush(IEngineWorldQuery world, IGameClock clock, IEngineNetworking net, IEngineEntityPhysics physics,
                     IEngineFrame frame)
        : base(world)
    {
        Clock = clock;
        Net = net;
        Physics = physics;
        Frame = frame;
    }

    /// <summary>gpGlobals->frametime, which stock's physics push is scaled by.</summary>
    private IEngineFrame Frame { get; }

    /// <summary>what the server thinks the time is.</summary>
    private readonly IGameClock Clock;

    /// <summary>putting this entity’s state on the wire.</summary>
    private readonly IEngineNetworking Net;

    /// <summary>this entity as the solver sees it.</summary>
    private readonly IEngineEntityPhysics Physics;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="PointPushVars"/>.</summary>
    [ScriptVars] public PointPushVars Vars = new();

    /// <summary>
    /// point_push's own spawnflags.
    ///
    /// <para>
    /// Two of these decide WHO is pushed and three decide HOW hard, and the two
    /// groups have nothing to do with one another -- which is why they are one
    /// enum and not two: they are all answers to "what did the mapper tick on this
    /// entity", and every one of them is read off the same word.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        TestLos = 0b0000_0001,
        Directional = 0b0000_0010,
        NoFalloff = 0b0000_0100,
        PushPlayers = 0b0000_1000,
        PushPhysics = 0b0001_0000,
    }

    /// <summary>How often the field looks around. Stock's interval, kept.</summary>
    private const float Interval = 0.05f;

    private float _lastPush;

    public override void OnSpawn()
    {
        if (Vars.Enabled)
            Start();
    }

    [ScriptedEvent("Enable")]
    private void OnEnable() => Start();

    [ScriptedEvent("Disable")]
    private void OnDisable()
    {
        Vars.Enabled = false;
        SetNextThink(-1f);
    }

    private void Start()
    {
        Vars.Enabled = true;
        _lastPush = Clock.Now;
        SetNextThink(Interval);
    }

    public override void OnThink()
    {
        if (!Vars.Enabled)
            return;

        _lastPush = Clock.Now;

        // Stock scales the physics push by gpGlobals->frametime, one server
        // frame, although it thinks every 0.05 s (physobj.cpp:2022; A27).
        Push(Frame.GetFrameTime());
        SetNextThink(Interval);
    }

    /// <summary>Exposed for tests, which have no engine to think for them.</summary>
    internal void Push(float frameTime)
    {
        Vector origin = Origin;

        Span<EntityHandle> found = stackalloc EntityHandle[MaxInRange];
        int count = Query(origin, Vars.Radius, found);

        for (int i = 0; i < count; i++)
            PushOne(found[i], origin, frameTime);
    }

    /// <summary>
    /// Full strength within a quarter of the radius, nothing at the edge.
    ///
    /// A much steeper curve than the explosion's, which is what makes a push
    /// field feel like a fan rather than a blast: almost all of its strength is
    /// in the middle.
    /// </summary>
    internal static float Falloff(float distance, float radius)
        => Remap(distance, radius, radius * 0.25f, 0f, 1f);

    /// <summary>
    /// The impulse a physics object gets. Scaled by mass so the magnitude means
    /// a SPEED rather than a force — a heavy crate and a light can accelerate
    /// alike — and by the server frame time, as stock's
    /// m_flMagnitude * flFalloff * 100 * mass * gpGlobals->frametime.
    /// </summary>
    internal static float Impulse(float magnitude, float falloff, float mass, float frameTime)
        => magnitude * falloff * 100f * mass * frameTime;

    private void PushOne(EntityHandle target, Vector origin, float frameTime)
    {
        var state = Net.EntityGetState(target, out MoveType moveType);

        if ((state & EntityState.Solid) == 0)
            return;

        // A child moves with its parent, so pushing it does nothing at all.
        if ((state & EntityState.Parented) != 0)
            return;

        if (moveType != MoveType.VPhysics && moveType != MoveType.Walk && moveType != MoveType.Step)
            return;

        if ((state & EntityState.Player) != 0 && !Flags.HasFlag(SpawnFlag.PushPlayers))
            return;

        if (moveType == MoveType.VPhysics && !Flags.HasFlag(SpawnFlag.PushPhysics))
            return;

        if (Flags.HasFlag(SpawnFlag.TestLos) && IsShielded(origin, target, Vars.InnerRadius))
            return;

        Vector spot = Entities.GetWorldSpaceCenter(target);
        Vector away = spot - origin;
        float distance = away.Length;

        // Directional pushes everything the same way — a wind — rather than
        // outward from a point. The direction is the entity's own facing.
        Vector direction = Flags.HasFlag(SpawnFlag.Directional)
            ? Facing
            : away.Normalized;

        // Stock remaps the length of the vector it normalised: for a directional
        // push that is the entity's forward, so 1, and the falloff is full
        // wherever the target stands (physobj.cpp:2005-2007; A27).
        float falloff = Flags.HasFlag(SpawnFlag.NoFalloff) ? 1f
                      : Falloff(Flags.HasFlag(SpawnFlag.Directional) ? 1f : distance, Vars.Radius);

        if (moveType == MoveType.VPhysics)
        {
            float mass = Physics.GetMass(target);
            Physics.ApplyForceCenter(target, direction * Impulse(Vars.Magnitude, falloff, mass, frameTime));
            return;
        }

        // Anything that walks is pushed flat. Source's step movement cannot be
        // lifted properly, so a vertical component would fight the mover rather
        // than move it.
        if (moveType == MoveType.Step)
            direction.Z = 0f;

        Entities.AddBaseVelocity(target, direction * (Vars.Magnitude * falloff));
    }
}

/// <summary>
/// The map keys <see cref="PointPush"/> declares.
/// </summary>
[ScriptVars]
public partial class PointPushVars
{
    public bool Enabled { get; set; }

    public float Magnitude { get; set; }

    public float Radius { get; set; }

    [VarName("inner_radius")]
    public float InnerRadius { get; set; }
}
