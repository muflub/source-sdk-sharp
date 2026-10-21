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
/// Shoves whoever is inside. Jump pads, wind tunnels, conveyor floors.
/// </summary>
[ScriptedEntity(EntityNames.TriggerPush, ScriptedEntityKind.Brush)]
public sealed class TriggerPush : TriggerVolume, IPlayerCommandTouch
{
    public TriggerPush(IGameClock clock, IEngineNetworking networking, IEngineEntityPhysics physics, IEngineFrame frame,
                       IEngineEnvironment? environment = null, PlayerCommandTouches? commandTouches = null)
        : base(clock)
    {
        _commandTouches = commandTouches;
        _networking = networking;
        _physics = physics;
        _frame = frame;
        _environment = environment;
    }

    /// <summary>Where a player inside is registered for a touch every command; none pushes no player after entry.</summary>
    private readonly PlayerCommandTouches? _commandTouches;

    /// <summary>What a toucher is and what moves it: stock's IsSolid, GetMoveType and GetMoveParent in one read.</summary>
    private readonly IEngineNetworking _networking;

    /// <summary>The VPHYSICS arm's force.</summary>
    private readonly IEngineEntityPhysics _physics;

    /// <summary>The engine's frame time, which scales that force.</summary>
    private readonly IEngineFrame _frame;

    /// <summary>Where the tick interval comes from; none (a bare build) is the default tick.</summary>
    private readonly IEngineEnvironment? _environment;

    /// <summary>
    /// trigger_push's own spawnflag. It used to sit in
    /// <see cref="TriggerSpawnflags"/>, which is the SHARED set every trigger
    /// reads to decide what counts as touching it -- and this bit means nothing
    /// to any other trigger. It is 128, one place above that set's top bit,
    /// which is what a private bit on a shared word looks like.
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None     = 0b0000_0000,

        PushOnce = 0b1000_0000,
    }

    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="TriggerPushVars"/>.</summary>
    [ScriptVars] public TriggerPushVars TriggerPushVars = new();

    /// <summary>
    /// The push direction, computed ONCE in <see cref="OnSpawn"/> from the map's
    /// angle key, which a hot reload does not run again -- so it is
    /// <c>[SaveField]</c>. Uncarried, a rebuilt trigger would push everything
    /// along <c>Vector.Zero</c> (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private Vector _direction;

    /// <summary>
    /// One server tick, the cadence of the push on what is not a player:
    /// stock's Touch runs every frame a physics object is inside.
    /// </summary>
    private float Tick => _environment?.Globals is { IsAvailable: true } g ? g.IntervalPerTick : ServerGameDll.DefaultTickInterval;

    /// <summary>Which touch pass is asking: they differ for a player only.</summary>
    private enum Pass
    {
        /// <summary>The volume's entry report.</summary>
        Entry,

        /// <summary>The tick think, for whatever is inside.</summary>
        Tick,

        /// <summary>A player's command: stock's ProcessImpacts.</summary>
        Command,
    }

    public override void OnSpawn()
    {
        base.OnSpawn();

        // The engine converts the angles to a world direction, rotates that into
        // entity space, then rotates it back out again at every push. The two
        // rotations cancel, so the world direction is simply the facing the map
        // asked for.
        _direction = Angles.Forward(TriggerPushVars.PushDir);

        if (TriggerPushVars.Speed == 0f)
            TriggerPushVars.Speed = 100f;
    }

    /// <summary>The push, as a velocity: stock's <c>m_flPushSpeed * vecAbsDir</c>.</summary>
    private Vector Push => _direction * TriggerPushVars.Speed;

    protected override void Fire(EntityHandle toucher)
    {
        // A push-once pad that launched is gone: nothing left to think.
        if (!Touch(toucher, Pass.Entry))
            SetNextThink(Tick);
    }

    protected override void WhileInside(EntityHandle toucher) => Touch(toucher, Pass.Tick);

    /// <summary>Every tick while anything is inside, as stock's Touch is called every frame.</summary>
    public override void OnThink() => PumpWhileOccupied(Tick);

    /// <summary>
    /// Switched back on with things still inside: the pump stopped while it was
    /// off (and every player's registration lapsed), so it starts again.
    /// </summary>
    protected override void OnDisabledChanged()
    {
        if (!Disabled && Inside.Count > 0)
            SetNextThink(Tick);
    }

    /// <summary>
    /// A player's touch after each of their commands (stock's ProcessImpacts).
    /// False once they have left or the volume is off, which unregisters it.
    /// </summary>
    public bool TouchFromCommand(EntityHandle player)
    {
        if (Disabled || !Inside.Contains(player))
            return false;

        Touch(player, Pass.Command);
        return true;
    }

    /// <summary>
    /// Stock's <c>CTriggerPush::Touch</c> (triggers.cpp), arm for arm, read
    /// in one crossing (EntityGetState: a player's move type is its record's).
    /// True when a push-once pad launched it and removed itself.
    ///
    /// <para>
    /// A PLAYER IS PUSHED ONCE PER COMMAND. Stock's Touch reaches a player
    /// from CMoveHelperServer::ProcessImpacts after each command's move, and
    /// the next command's CheckMovingGround consumes the base velocity it
    /// set. Pushed per tick instead, a push renewed between the commands of
    /// one batch accumulates (two or three times over), and one not renewed
    /// between them is folded into the velocity for good: dm_lockdown's
    /// fan_wind (speed 800) flung the live gate's player thousands of units a
    /// second. So the entry pushes and registers the player
    /// (<see cref="PlayerCommandTouches"/>), each command pushes, and the
    /// tick only keeps the registration (a reload empties it).
    /// </para>
    /// </summary>
    private bool Touch(EntityHandle toucher, Pass pass)
    {
        EntityState state = _networking.EntityGetState(toucher, out MoveType moveType);

        // Not solid, a pusher or a fixture: nothing to push. A parented
        // entity is its parent's to move (stock's FIXME says so too).
        if ((state & EntityState.Solid) == 0 || moveType is MoveType.Push or MoveType.None
            || (state & EntityState.Parented) != 0)
            return false;

        // A one-shot pad launches and then gets out of the way. The ground is
        // let go of only when launching upwards: something pushed along a
        // floor should stay on it.
        if (Flags.HasFlag(SpawnFlag.PushOnce))
        {
            Entities.ApplyVelocityImpulse(toucher, Push, clearGround: _direction.Z > 0f);
            Remove();
            return true;
        }

        switch (moveType)
        {
            case MoveType.NoClip:
                break;

            // "Assume the velocity is for a 100kg object, scale with mass":
            // a force on the object, every frame. No object, no push, as
            // stock's VPhysicsGetObject() check leaves it.
            case MoveType.VPhysics:
                if (pass != Pass.Command)
                    _physics.ApplyForceCenter(toucher, Push * (100f * _frame.GetFrameTime()));
                break;

            // The default arm. A player's base velocity is the movement
            // record's (ServerPlayerMovement.AddBaseVelocity: added to an
            // unconsumed one, the ground let go of and a unit's unstick on an
            // upward push, FL_BASEVELOCITY set). Nothing else has a
            // base-velocity channel here, so a non-player that is not
            // simulated is not pushed (docs/todo.md).
            default:
                if ((state & EntityState.Player) == 0)
                    break;

                if (pass != Pass.Tick)
                    Entities.AddBaseVelocity(toucher, Push);

                if (pass != Pass.Command)
                    _commandTouches?.Hold(toucher, Handle);
                break;
        }

        return false;
    }
}

/// <summary>
/// The map keys <see cref="TriggerPush"/> declares.
/// </summary>
[ScriptVars]
public partial class TriggerPushVars
{
    /// <summary>Which way to shove, written by the map as angles rather than a vector.</summary>
    public QAngle PushDir { get; set; }

    public float Speed { get; set; } = 100f;
}
