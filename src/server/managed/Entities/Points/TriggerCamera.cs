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
/// Takes a player's eyes, points them at something, optionally flies them along
/// a path, and gives them back.
///
/// Stock's <c>CTriggerCamera</c>, and the largest single class left in
/// triggers.cpp. It is a POINT entity despite the class name — the map keyword
/// is point_viewcontrol — and it is not a trigger at all: nothing touches it,
/// map logic switches it on.
///
/// <para>
/// Three jobs that read as one and are separable, which is how the code below is
/// laid out. AIMING is a think that turns the camera toward its target a little
/// each tick. TRAVELLING is a walk of a path_corner chain with acceleration and
/// per-corner speeds and waits. HOLDING THE PLAYER is everything done to the
/// player while it runs — the view, the freeze, the invulnerability, the hidden
/// viewmodel — and every one of those has to be given back exactly as it was
/// found, because a camera that ends leaving a player frozen is a softlock.
/// </para>
///
/// <para>
/// The reason it moved is not that it was big. Stock holds its path node as a
/// <c>CBaseEntity *</c> and asks it for a speed, a delay, a position and its
/// next link — and path_corner is C# here, so <c>CBaseEntity::Instance</c>
/// answers NULL for one and the native camera walked a chain it could never
/// find. Recorded in docs/divergences.md as an entity that simply did not work.
/// On this side both are managed objects and the chain is an ordinary reference,
/// so the divergence is not worked around, it stops existing.
/// </para>
///
/// <para>
/// What is deliberately NOT here: <c>interpolatepositiontoplayer</c>, which is
/// inside <c>#if HL2_EPISODIC</c> in stock and this mod does not build episodic
/// — server_csharp.vpc derives from server_base, not server_episodic. Carrying
/// it would be adding behaviour the C++ in this tree does not have. The key is
/// still read, so that a map asking for it is told rather than ignored.
/// </para>
/// </summary>
/// <summary>
/// Which <c>point_viewcontrol</c> currently holds each player's view: one table
/// per server module, shared by every camera in it.
/// </summary>
public interface IViewControlHolders
{
    /// <summary>The camera holding <paramref name="player"/>'s view, if one does.</summary>
    bool TryGet(EntityHandle player, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TriggerCamera? camera);

    /// <summary><paramref name="camera"/> now holds <paramref name="player"/>'s view.</summary>
    void Hold(EntityHandle player, TriggerCamera camera);

    /// <summary>Nothing holds <paramref name="player"/>'s view.</summary>
    void Release(EntityHandle player);
}

/// <inheritdoc cref="IViewControlHolders"/>
public sealed class ViewControlHolders : IViewControlHolders
{
    private readonly Dictionary<uint, TriggerCamera> _byPlayer = new();

    /// <inheritdoc/>
    public bool TryGet(EntityHandle player, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TriggerCamera? camera)
        => _byPlayer.TryGetValue(player.Value, out camera);

    /// <inheritdoc/>
    public void Hold(EntityHandle player, TriggerCamera camera) => _byPlayer[player.Value] = camera;

    /// <inheritdoc/>
    public void Release(EntityHandle player) => _byPlayer.Remove(player.Value);
}

[ScriptedEntity(EntityNames.PointViewcontrol, ScriptedEntityKind.Effect)]
public sealed class TriggerCamera : ScriptedEntity
{
    public TriggerCamera(IGameClock clock, IEngineDamage damage, IEngineEntityPhysics physics, IEnginePlayerMovement playerMovement, IEnginePlayers players, IEngineEntityRender render,
                         IViewControlHolders holders)
    {
        _holders = holders;
        Clock = clock;
        Damage = damage;
        Physics = physics;
        PlayerMovement = playerMovement;
        Players = players;
        Render = render;
    }

    /// <summary>what the server thinks the time is.</summary>
    private readonly IGameClock Clock;

    /// <summary>hurting things.</summary>
    private readonly IEngineDamage Damage;

    /// <summary>this entity as the solver sees it.</summary>
    private readonly IEngineEntityPhysics Physics;

    /// <summary>how a player moves, and what may stop them.</summary>
    private readonly IEnginePlayerMovement PlayerMovement;

    /// <summary>the players this entity acts on.</summary>
    private readonly IEnginePlayers Players;

    /// <summary>what this entity looks like.</summary>
    private readonly IEngineEntityRender Render;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    //-------------------------------------------------------------------------
    // Spawnflags, stock's SF_CAMERA_PLAYER_*.
    //-------------------------------------------------------------------------

    /// <summary>
    /// point_viewcontrol's own spawnflags, stock's SF_CAMERA_PLAYER_*.
    ///
    /// <para>
    /// One domain and one word: everything a mapper ticks on a camera. They are
    /// not independent of each other in practice -- TakeControl and PlayerNotSolid
    /// both act on the player rather than the camera, and Interrupt only means
    /// anything alongside one of them -- which is far easier to see as a member
    /// list than as seven loose ints.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        /// <summary>Start where the player's eyes are rather than where the map drew this.</summary>
        StartAtPlayer = 0b0000_0001,

        /// <summary>Aim at the player instead of at whatever <c>target</c> names.</summary>
        AimAtPlayer = 0b0000_0010,

        /// <summary>Freeze the player for the duration.</summary>
        TakeControl = 0b0000_0100,

        /// <summary>Never time out. <c>wait</c> is ignored and only Disable ends it.</summary>
        InfiniteWait = 0b0000_1000,

        /// <summary>Snap to the aim on the first think rather than swinging round to it.</summary>
        SnapTo = 0b0001_0000,

        /// <summary>Make the player non-solid while held, so a cutscene can pass through them.</summary>
        PlayerNotSolid = 0b0010_0000,

        /// <summary>Any key the player presses ends it.</summary>
        Interrupt = 0b0100_0000,
    }

    //-------------------------------------------------------------------------
    // Map keys.
    //-------------------------------------------------------------------------

    /// <summary>What a map may set on this entity. See <see cref="TriggerCameraVars"/>.</summary>
    [ScriptVars] public TriggerCameraVars Vars = new();

    /// <summary>Fired when the camera lets go, however it let go.</summary>
    public readonly Output OnEndFollow = new();

    //-------------------------------------------------------------------------
    // Live state.
    //-------------------------------------------------------------------------

    private EntityHandle _player = EntityHandle.None;
    private EntityHandle _aimTarget = EntityHandle.None;

    /// <summary>The node being flown TOWARD, or null when the path has run out.</summary>
    private CameraPathNode? _path;

    private bool _enabled;
    private bool _snapToGoal;

    private float _returnTime;
    private float _stopTime;
    private float _moveDistance;
    private float _speed;
    private float _targetSpeed;
    private Vector _moveDir;

    /// <summary>
    /// What the player's own state was before this took it, so it can be given
    /// back rather than reset to what "normal" is assumed to be.
    /// </summary>
    private TakeDamageMode _oldTakeDamage = TakeDamageMode.Yes;
    private bool _madeNotSolid;
    private bool _frozePlayer;
    private EntityHandle _hiddenWeapon = EntityHandle.None;
    private InputButtons _playerButtons;

    /// <summary>
    /// Velocity and angular velocity, integrated here rather than by the engine.
    ///
    /// Stock is MOVETYPE_NOCLIP and writes SetAbsVelocity / SetLocalAngularVelocity,
    /// leaving the engine to advance the transform. An entity on the parallel
    /// hierarchy has no such movetype: SetVelocity there means a projectile,
    /// which traces its step and stops at the first thing it hits — the exact
    /// opposite of what noclip means. So the same arithmetic runs here and the
    /// origin is written directly, which is what MOVETYPE_NOCLIP does anyway.
    /// </summary>
    private Vector _velocity;
    private Vector _angularVelocity;

    /// <summary>
    /// When the think last ran, which is where the frame time comes from.
    ///
    /// Stock reads gpGlobals->frametime. There is no ABI slot for it and there
    /// does not need to be: this thinks every tick, so the gap since the last
    /// think IS the frame time, measured rather than assumed. It also stays
    /// correct if the think is ever rescheduled less often, which a constant
    /// would not.
    /// </summary>
    private float _lastThink;

    /// <summary>
    /// Which camera currently holds each player's view.
    ///
    /// Stock asks the player for its view entity and dynamic_casts the answer to
    /// CTriggerCamera — so it only ever recognises another point_viewcontrol,
    /// and this is the same question asked the way round that works here. A
    /// standalone entity cannot live in CBasePlayer::m_hViewEntity (see
    /// PlayerSetViewEntity in sourcesharp.h), so the camera side remembers —
    /// in the server module's table, which every camera shares (it was a static
    /// dictionary).
    /// </summary>
    private readonly IViewControlHolders _holders;

    public bool IsEnabled => _enabled;

    /// <summary>The player whose view this holds. Invalid when it holds none.</summary>
    public EntityHandle HeldPlayer => _enabled ? _player : EntityHandle.None;

    /// <summary>The node being flown toward. Null before it starts and after the path ends.</summary>
    internal CameraPathNode? CurrentPathNode => _path;

    /// <summary>The speed of the leg in flight, which a node may have overridden.</summary>
    public float CurrentSpeed => _speed;

    public override void OnSpawn()
    {
        // Stock's defaults, applied at spawn rather than declared as field
        // initialisers, because the map writing "0" must mean the default too —
        // stock tests the value, not whether the key was present.
        if (Vars.Acceleration == 0f)
            Vars.Acceleration = 500f;

        if (Vars.Deceleration == 0f)
            Vars.Deceleration = 500f;

        // Off, and therefore not worth sending. See EntitySetTransmitAlways —
        // an active camera has to reach a client that is nowhere near it, and an
        // inactive one has nothing to say.
        Render.SetTransmitAlways(Handle, false);
    }

    //-------------------------------------------------------------------------
    // Inputs.
    //
    // Enable and Disable are stock's, and its Use is reached both by a legacy
    // targetname fire and by the Toggle a mapper writes today.
    //-------------------------------------------------------------------------

    [ScriptedEvent("Enable")]
    private void OnEnable(InputData input)
    {
        _player = input.Activator;
        Enable();
    }

    [ScriptedEvent("Disable")]
    private void OnDisable() => Disable();

    [ScriptedEvent("Use")]
    private void OnUse(InputData input) => Toggle(input.Activator);

    [ScriptedEvent("Toggle")]
    private void OnToggle(InputData input) => Toggle(input.Activator);

    private void Toggle(EntityHandle activator)
    {
        if (_enabled)
        {
            Disable();
            return;
        }

        _player = activator;
        Enable();
    }

    /// <summary>
    /// Takes the view and starts running.
    ///
    /// The order here is stock's and two parts of it matter. The previous camera
    /// is disabled BEFORE anything of the player's is saved, or this one would
    /// save the state that camera had already replaced and hand the player back
    /// a damage setting that was never theirs. And Move is called once at the
    /// end with a move distance of zero, which is what makes the first path node
    /// the STARTING point rather than the first destination: the camera fires
    /// InPass on it immediately and sets off for the one after.
    /// </summary>
    public void Enable()
    {
        _enabled = true;

        if (!Entities.IsValid(_player) || !Entities.IsPlayer(_player))
            _player = FirstPlayer();

        if (!Entities.IsValid(_player))
        {
            // Stock warns and gives up in one branch and silently gives up in
            // the other. One message either way: a camera enabled with nobody to
            // look through it is a map or a sequencing mistake, not a state.
            Log.Warning($"[SourceSharp] point_viewcontrol '{Name}': enabled with no player to take the view from\n");
            Render.SetTransmitAlways(Handle, false);
            return;
        }

        if (_holders.TryGet(_player, out var previous))
        {
            // A camera that has been removed while holding somebody leaves
            // nothing behind to disable, and asking it to would only walk a
            // handful of handles that no longer resolve.
            if (!Entities.IsValid(previous.Handle))
            {
                _holders.Release(_player);
                previous = this;
            }

            if (ReferenceEquals(previous, this))
            {
                // Stock's own message, kept word for word: it is the one thing
                // here somebody may already have searched the internet for.
                Log.Warning($"Viewcontrol {Name} was enabled twice in a row!\n");
                return;
            }

            previous.Disable();
        }

        _playerButtons = PlayerMovement.GetButtons(_player);

        // Invulnerable while held. Stock's comment is the reason and it is a
        // good one: a player who dies under camera control cannot restart,
        // because the inputs that would restart them are disabled.
        _oldTakeDamage = Damage.SetTakeDamageMode(_player, TakeDamageMode.No);

        _madeNotSolid = Flags.HasFlag(SpawnFlag.PlayerNotSolid);
        if (_madeNotSolid)
            Physics.SetNotSolid(_player, true);

        _returnTime = Clock.Now + Vars.Wait;
        _speed = Vars.MapSpeed;
        _targetSpeed = Vars.MapSpeed;

        // About the AIM, not the position, despite reading like a teleport.
        if (Flags.HasFlag(SpawnFlag.SnapTo))
            _snapToGoal = true;

        _aimTarget = Flags.HasFlag(SpawnFlag.AimAtPlayer)
            ? _player
            : Entities.FindByName(Vars.TargetName);

        if (Entities.IsValid(_aimTarget) && Vars.TargetAttachment.Length > 0)
        {
            Log.Warning($"[SourceSharp] point_viewcontrol '{Name}': targetattachment " +
                           $"'{Vars.TargetAttachment}' is not supported; aiming at the target's origin\n");
        }

        if (Vars.InterpolatePosition)
        {
            Log.Warning($"[SourceSharp] point_viewcontrol '{Name}': " +
                           "interpolatepositiontoplayer is episodic-only and this build is not episodic\n");
        }

        _frozePlayer = Flags.HasFlag(SpawnFlag.TakeControl);
        if (_frozePlayer)
            PlayerMovement.SetFrozen(_player, true);

        _path = Vars.PathName.Length > 0 ? FindPathNode(Vars.PathName) : null;

        _stopTime = Clock.Now;

        if (_path is not null)
        {
            if (_path.Speed != 0f)
                _targetSpeed = _path.Speed;

            _stopTime += _path.Delay;
        }

        if (Flags.HasFlag(SpawnFlag.StartAtPlayer))
        {
            Origin = Entities.GetEyePosition(_player);

            var playerAngles = Entities.GetAngles(_player);
            SetAngles(new Vector(playerAngles.X, playerAngles.Y, 0f));

            _velocity = Entities.GetVelocity(_player);
        }
        else
        {
            _velocity = Vector.Zero;
        }

        _angularVelocity = Vector.Zero;

        // Before the view is handed over, not after: the client takes the origin
        // and the angles off its own copy of this entity, and a camera that is
        // still PVS-checked when the view moves to it is a camera the client was
        // never sent.
        Render.SetTransmitAlways(Handle, true);

        _holders.Hold(_player, this);
        Players.SetViewEntity(_player, Handle);

        _hiddenWeapon = Players.GetActiveWeaponEntity(_player);
        if (Entities.IsValid(_hiddenWeapon))
            Render.SetVisible(_hiddenWeapon, false);

        _lastThink = Clock.Now;

        // Only tracks if there is something to track. A camera with no target
        // still MOVES — Move is called below and the path keeps running — it
        // just never re-arms this think, which is stock's behaviour and is why a
        // path-only camera holds the view until something disables it.
        if (Entities.IsValid(_aimTarget))
            SetNextThink(0f);

        _moveDistance = 0f;
        Move(0f);
    }

    /// <summary>
    /// Gives everything back, in the reverse of the order it was taken.
    ///
    /// Reached four ways: the Disable input, the wait running out, the target
    /// disappearing, and the player pressing a key with
    /// <see cref="SpawnFlag.Interrupt"/> set. All four end the same way because the
    /// player's state does not know which of them happened.
    /// </summary>
    public void Disable()
    {
        if (Entities.IsValid(_player) && Players.IsAlive(_player))
        {
            // Only what was actually taken.
            //
            // Stock removes the solid flag, unfreezes and restores damage
            // unconditionally, so a camera WITHOUT SpawnFlag.PlayerNotSolid still makes
            // a player solid that something else had made non-solid, and one
            // without SpawnFlag.TakeControl unfreezes a player something else froze.
            // Recorded in docs/divergences.md; the ABI comments on
            // PlayerForceButtons make the same argument about the same shape of
            // mistake.
            if (_madeNotSolid)
                Physics.SetNotSolid(_player, false);

            if (_frozePlayer)
                PlayerMovement.SetFrozen(_player, false);

            if (Entities.IsValid(_hiddenWeapon))
                Render.SetVisible(_hiddenWeapon, true);

            Damage.SetTakeDamageMode(_player, _oldTakeDamage);
        }

        // Outside the alive test, and deliberately so: a player who died under
        // the camera is respawning and must not respawn still looking through
        // it. Stock leaves the view behind in exactly that case.
        if (Entities.IsValid(_player))
            Players.SetViewEntity(_player, EntityHandle.None);

        if (Entities.IsValid(_player) && _holders.TryGet(_player, out var holder)
            && ReferenceEquals(holder, this))
        {
            _holders.Release(_player);
        }

        _madeNotSolid = false;
        _frozePlayer = false;
        _hiddenWeapon = EntityHandle.None;

        _enabled = false;
        _returnTime = Clock.Now;
        StopThinking();

        _velocity = Vector.Zero;
        _angularVelocity = Vector.Zero;

        OnEndFollow.Fire();

        Render.SetTransmitAlways(Handle, false);
    }

    /// <summary>
    /// Hands the view back if this is removed while holding it.
    ///
    /// Stock does not: CTriggerCamera has no UpdateOnRemove, so a map that kills
    /// an active camera leaves the player looking through an entity that is
    /// gone, frozen if it froze them. Recorded in docs/divergences.md.
    /// </summary>
    public override void OnRemoved()
    {
        if (_enabled)
            Disable();
    }

    /// <summary>
    /// One tick of aiming, and then one tick of travelling.
    ///
    /// Stock's FollowTarget. It ends the camera for two reasons before it does
    /// anything: the target went away, or the wait ran out — and neither is
    /// checked while <see cref="SpawnFlag.InfiniteWait"/> is set, which is what makes a
    /// camera a map holds open indefinitely.
    /// </summary>
    public override void OnThink()
    {
        float now = Clock.Now;
        float dt = now - _lastThink;
        _lastThink = now;

        // The frame the engine would have integrated for us, done here. Before
        // anything reads the position, because everything below is relative to
        // where the camera is NOW.
        Integrate(dt);

        if (!Entities.IsValid(_player))
            return;

        if (!Entities.IsValid(_aimTarget))
        {
            Disable();
            return;
        }

        if (!Flags.HasFlag(SpawnFlag.InfiniteWait) && _returnTime < now)
        {
            Disable();
            return;
        }

        AimAt(Entities.GetOrigin(_aimTarget), dt);

        // A camera holding a frozen player keeps whatever velocity the path is
        // giving it. One that is not holds only a decaying remnant — which is
        // what makes a camera started at a running player's eyes drift to a stop
        // rather than carrying on at their speed.
        if (!Flags.HasFlag(SpawnFlag.TakeControl))
        {
            _velocity *= 0.8f;

            if (_velocity.Length < 10f)
                _velocity = Vector.Zero;
        }

        SetNextThink(0f);

        Move(dt);
    }

    /// <summary>
    /// Turns toward a point, or snaps to it once if the map asked.
    ///
    /// The turn is a rate rather than a step: the shortest signed difference on
    /// each axis, times forty, times the frame — so a camera catches up quickly
    /// at first and eases in, and a target moving steadily is trailed rather
    /// than tracked exactly. That constant is stock's and maps are timed to it.
    /// </summary>
    private void AimAt(Vector targetOrigin, float dt)
    {
        var goal = Angles.FromVector(targetOrigin - Origin);

        if (_snapToGoal)
        {
            SetAngles(goal);
            _angularVelocity = Vector.Zero;
            _snapToGoal = false;
            return;
        }

        var angles = Entities.GetAngles(Handle);

        // Stock's own normalisation, kept rather than replaced with a modulo:
        // it only ever brings ONE turn back into range, which is all the entity
        // can accumulate in a tick, and the wrapping below depends on the result
        // being in [0, 360).
        if (angles.Y > 360f)
            angles.Y -= 360f;

        if (angles.Y < 0f)
            angles.Y += 360f;

        SetAngles(angles);

        float dx = WrapAngle(goal.X - angles.X);
        float dy = WrapAngle(goal.Y - angles.Y);

        _angularVelocity = new Vector(dx * 40f * dt, dy * 40f * dt, _angularVelocity.Z);
    }

    /// <summary>The shortest way round, in degrees.</summary>
    private static float WrapAngle(float delta)
    {
        if (delta < -180f)
            delta += 360f;

        if (delta > 180f)
            delta -= 360f;

        return delta;
    }

    /// <summary>
    /// One tick of travelling the path, and the interrupt check that has to
    /// happen whether there is a path or not.
    ///
    /// The interrupt lives here rather than in the think because stock calls
    /// this from Enable as well, and a camera enabled while the player is
    /// already holding a key must not end on its first tick for a press that
    /// happened before it started — which is why the buttons are SAMPLED at
    /// Enable and only a CHANGE counts.
    /// </summary>
    internal void Move(float dt)
    {
        if (Flags.HasFlag(SpawnFlag.Interrupt) && Entities.IsValid(_player))
        {
            var buttons = PlayerMovement.GetButtons(_player);

            if (buttons != _playerButtons && buttons != 0)
            {
                Disable();
                return;
            }

            _playerButtons = buttons;
        }

        if (_path is null)
            return;

        _moveDistance -= _speed * dt;

        if (_moveDistance <= 0f)
        {
            // The node is passed as it is REACHED, and the very first call — the
            // one from Enable, with a move distance of zero — passes the first
            // node immediately. That is what makes "moveto" the place the camera
            // starts from rather than the first place it flies to.
            _path.Pass(Handle);

            _path = _path.Next;

            if (_path is null)
            {
                _velocity = Vector.Zero;
            }
            else
            {
                if (_path.Speed != 0f)
                    _targetSpeed = _path.Speed;

                _moveDir = _path.Position - Origin;
                _moveDistance = _moveDir.Length;
                _moveDir = _moveDir.Normalized;

                _stopTime = Clock.Now + _path.Delay;
            }
        }

        // Sitting out a corner's wait is spelt as decelerating to nothing rather
        // than as a stop, which is why a camera on a path with waits glides
        // instead of stuttering.
        _speed = _stopTime > Clock.Now
            ? Approach(0f, _speed, Vars.Deceleration * dt)
            : Approach(_targetSpeed, _speed, Vars.Acceleration * dt);

        // A first-order lag toward the leg's velocity rather than the velocity
        // itself: it is what rounds the corners off. At the tick rate the
        // fraction is about an eighth, so a change takes a handful of ticks to
        // take full effect.
        float fraction = 2f * dt;
        _velocity = (_moveDir * _speed) * fraction + _velocity * (1f - fraction);
    }

    /// <summary>
    /// What MOVETYPE_NOCLIP does: advance the transform by the velocities and
    /// collide with nothing.
    /// </summary>
    private void Integrate(float dt)
    {
        if (dt <= 0f)
            return;

        if (_velocity.LengthSquared > 0f)
            Origin += _velocity * dt;

        if (_angularVelocity.LengthSquared > 0f)
            SetAngles(Entities.GetAngles(Handle) + _angularVelocity * dt);
    }

    /// <summary>Source's UTIL_Approach: move toward a value by at most a step.</summary>
    private static float Approach(float target, float value, float step)
    {
        float delta = target - value;

        if (delta > step)
            return value + step;

        if (delta < -step)
            return value - step;

        return target;
    }

    /// <summary>
    /// The first player in the game, which is what stock's UTIL_GetLocalPlayer
    /// means on a listen server.
    ///
    /// Only reached when the input carried no activator — a logic_auto or a
    /// console fire. With more than one player that is a guess, and it is the
    /// same guess stock makes; a camera that means a particular player is fired
    /// by something that has one.
    /// </summary>
    private EntityHandle FirstPlayer()
    {
        for (int slot = 1; slot <= MaxPlayerSlots; slot++)
        {
            var player = Players.GetHandle(slot);

            if (Entities.IsValid(player))
                return player;
        }

        return EntityHandle.None;
    }

    private const int MaxPlayerSlots = 64;

    /// <summary>
    /// Resolves the name in <c>moveto</c> to something the camera can walk.
    ///
    /// Stock takes any CBaseEntity and asks it for m_flSpeed, GetDelay(),
    /// GetLocalOrigin() and GetNextTarget() — a generality that only ever meant
    /// path_corner and path_track, because GetDelay() answers 0 for everything
    /// except a corner and nothing else in the SDK links itself into a chain a
    /// camera can follow. Both of those are managed classes here, so the two
    /// cases are named instead of a fifth accessor being invented for the sake
    /// of a genericity nothing uses.
    /// </summary>
    private CameraPathNode? FindPathNode(string name)
    {
        if (FindByName<PathCorner>(name) is { } corner)
            return new CameraPathCorner(corner);

        if (FindByName<PathTrack>(name) is { } track)
            return new CameraPathTrack(track);

        Log.Warning($"[SourceSharp] point_viewcontrol '{Name}': " +
                       $"'{name}' is not a path_corner or path_track, so there is no path to fly\n");
        return null;
    }

    /// <summary>
    /// For tests, which have no map to have named a chain. The camera reaches
    /// its path by name everywhere else.
    ///
    /// <see cref="Move"/> is internal for the same reason and no other: walking
    /// the chain is the part of this class that has an answer without a game,
    /// and it is the part the divergence this port closes was about.
    /// </summary>
    internal void StartOnPath(PathCorner first) => _path = new CameraPathCorner(first);

    /// <summary>The same for a path_track chain.</summary>
    internal void StartOnPath(PathTrack first) => _path = new CameraPathTrack(first);
}

/// <summary>
/// One stop on a camera's path, whichever kind of node it actually is.
///
/// Four questions and nothing else, because four is all stock asks of the
/// CBaseEntity it holds: where, how fast to leave, how long to wait, and what
/// comes next — plus the announcement that it has been reached. path_corner and
/// path_track answer all four and do not share a base class, so the shape is
/// stated here rather than pushed into either of them: a camera is the only
/// thing that walks both, and neither node has any business knowing that.
/// </summary>
internal abstract class CameraPathNode
{
    public abstract Vector Position { get; }

    /// <summary>The speed of the leg LEAVING this node, or 0 to keep the current one.</summary>
    public abstract float Speed { get; }

    /// <summary>Seconds to sit here on arrival. Always 0 for a path_track, as in stock.</summary>
    public abstract float Delay { get; }

    public abstract CameraPathNode? Next { get; }

    /// <summary>Fires the node's own OnPass, which is stock's "InPass" input.</summary>
    public abstract void Pass(EntityHandle follower);
}

internal sealed class CameraPathCorner : CameraPathNode
{
    private readonly PathCorner _node;

    public CameraPathCorner(PathCorner node) => _node = node;

    /// <summary>Which corner this wraps, so a test can name it.</summary>
    internal PathCorner Node => _node;

    public override Vector Position => _node.Position;
    public override float Speed => _node.Speed;
    public override float Delay => _node.Delay;

    public override CameraPathNode? Next
        => _node.Next is { } next ? new CameraPathCorner(next) : null;

    public override void Pass(EntityHandle follower) => _node.Pass(follower);
}

internal sealed class CameraPathTrack : CameraPathNode
{
    private readonly PathTrack _node;

    public CameraPathTrack(PathTrack node) => _node = node;

    internal PathTrack Node => _node;

    public override Vector Position => _node.Position;
    public override float Speed => _node.NodeSpeed;

    /// <summary>
    /// Zero, and not an omission. CBaseEntity::GetDelay answers 0 and only
    /// path_corner overrides it, so a camera has never waited at a path_track.
    /// </summary>
    public override float Delay => 0f;

    public override CameraPathNode? Next
        => _node.GetNext() is { } next ? new CameraPathTrack(next) : null;

    public override void Pass(EntityHandle follower) => _node.Pass(follower);
}

/// <summary>
/// The map keys <see cref="TriggerCamera"/> declares.
/// </summary>
[ScriptVars]
public partial class TriggerCameraVars
{
    /// <summary>What to look at, unless <see cref="SF_AimAtPlayer"/> says the player.</summary>
    [VarName("target")]
    public string TargetName { get; set; } = string.Empty;

    /// <summary>The first node of the path to fly, or empty to sit still.</summary>
    [VarName("moveto")]
    public string PathName { get; set; } = string.Empty;

    /// <summary>Seconds to hold the view before handing it back.</summary>
    public float Wait { get; set; }

    /// <summary>How fast it sets off. A path node may override it per leg.</summary>
    [VarName("speed")]
    public float MapSpeed { get; set; }

    /// <summary>Units per second per second gained when below the leg's speed.</summary>
    public float Acceleration { get; set; }

    /// <summary>The same, lost, while sitting out a corner's wait.</summary>
    public float Deceleration { get; set; }

    /// <summary>
    /// An attachment on the target to aim at rather than its origin.
    ///
    /// Read so that a map naming one is told it does nothing, rather than being
    /// aimed silently at the model's origin instead. See the warning in
    /// <see cref="Enable"/> and docs/divergences.md.
    /// </summary>
    public string TargetAttachment { get; set; } = string.Empty;

    /// <summary>Episodic-only in stock, and this build is not episodic. See the class note.</summary>
    [VarName("interpolatepositiontoplayer")]
    public bool InterpolatePosition { get; set; }
}
