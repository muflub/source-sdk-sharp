//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// CBasePlayer::VPhysicsShadowUpdate (player.cpp), once per physics frame after
/// the step (physics stock parity B7): the solver's answer for the player's
/// shadow, read back and reconciled with where the movement code put them.
/// <see cref="PhysicsEventRouter"/> calls it for each ShadowUpdate the native
/// PhysFrame walk records.
///
/// <para>
/// Stock's rules. The player counts as touching physics when the controller is
/// in contact, the movement touched a physics object this frame, or a
/// MOVETYPE_VPHYSICS mover is penetrating them. A frozen controller is
/// recovered toward the game position. Otherwise, past 2 units or 10 in/s of
/// error (a quarter of each on rideable ground, heavier than twice the shadow),
/// or on physics ground while not touching: a touching or physics-grounded
/// player is given the shadow's velocity less their own along their travel,
/// and moved to the shadow if they fit there; anyone else is checked for being
/// stuck. Within tolerance, a touching player found in solid is moved to the
/// shadow and then checked. A player still in solid is put back where they
/// were last valid.
/// </para>
///
/// <para>
/// PFLAG_GAMEPHYSICS_ROTPUSH is read and cleared as stock does: a rotating
/// SOLID_VPHYSICS pusher set it (<see cref="PhysicsPushedEntities"/>), and it
/// makes this update check whether the push left the player stuck.
/// </para>
///
/// <para>
/// <c>phys_timescale</c> 0 is stock's frozen simulation: physicsUpdated is
/// forced false (player.cpp:8349-8353), so nobody is moved toward a shadow
/// that is not advancing. The ConVar is declared managed
/// (<see cref="PhysTimescaleConVar"/>; native's physics.cpp reads the same
/// declaration to scale the step), so it is read here, per update, rather
/// than handed back through the probe.
/// </para>
///
/// <para>
/// Stated: no following physics and no alternate-tick halving -- this mod has
/// neither (no IsFollowingPhysics entity, no alternate-tick simulation); and
/// after the frozen recovery stock also teleports the shadow
/// (UpdateVPhysicsPosition), which here the next command's PlayerShadow.Plan
/// does by aiming it at the player.
/// </para>
///
/// <para>
/// <b>ACROSS A RELOAD</b> each player's last valid origin is carried
/// (<see cref="ISaveCustom"/>, the LifecycleDriver tier): stock saves
/// <c>m_oldOrigin</c> (player.cpp:311), and the overlapped arm's stuck check
/// puts the player back there, so a forgotten one put them at the world
/// origin. The frozen flag is NOT carried, as stock leaves
/// <c>m_bPhysicsWasFrozen</c> and <c>m_touchedPhysObject</c> out of the
/// DATADESC (:313-314); it comes back false.
/// </para>
/// </summary>
public sealed class PlayerShadowRecovery : IGameLifecycle, ISaveCustom
{
    /// <summary>VPHYS_MAX_DISTANCE.</summary>
    public const float MaxDistance = 2.0f;

    /// <summary>VPHYS_MAX_VEL.</summary>
    public const float MaxVelocity = 10.0f;

    private const int FlagOnGround = 1;
    private const int MoveTypeNoClip = 8;

    private readonly IEnginePlayers _players;

    /// <summary>The shadow as the solver left it: managed's bodies since plan_physics_bones_native_zero 2.5.</summary>
    private readonly IPlayerShadowProbe _shadows;
    private readonly IEngineEntities _entities;
    private readonly IEngineWorldQuery _world;

    /// <summary>To reach the player object, which holds <c>m_afPhysicsFlags</c>.</summary>
    private readonly EntityDirectory _directory;

    /// <summary>Stock's one m_oldOrigin, shared with the command's shadow step (PlayerShadowBodies.Memory).</summary>
    private readonly PlayerMoveMemory _moves;

    /// <summary>m_bPhysicsWasFrozen, per player.</summary>
    private readonly Dictionary<EntityHandle, bool> _wasFrozen = new();

    /// <summary><c>cs_sv_playershadow_trace</c>'s recovery half; null records nothing.</summary>
    private readonly PlayerShadowTrace? _trace;

    /// <summary><c>phys_timescale</c>, read live; null reads the declared default, 1.</summary>
    private readonly Func<float>? _physTimescale;
    private readonly Func<EntityHandle, bool>? _penetratedByMover;

    /// <param name="penetratedByMover">
    /// Whether anything the player penetrates is a MOVETYPE_VPHYSICS mover:
    /// the solver's penetration ledger (PhysicsSolver.PenetratedByMover, 2.1).
    /// </param>
    public PlayerShadowRecovery(IEnginePlayers players, IEngineEntities entities, IEngineWorldQuery world,
                                IPlayerShadowProbe shadows, EntityDirectory directory, PlayerShadowTrace? trace = null,
                                Func<float>? physTimescale = null, Func<EntityHandle, bool>? penetratedByMover = null,
                                PlayerMoveMemory? memory = null)
    {
        _moves = memory ?? new PlayerMoveMemory();
        _trace = trace;
        _physTimescale = physTimescale;
        _penetratedByMover = penetratedByMover;
        _directory = directory;
        _players = players;
        _entities = entities;
        _world = world;
        _shadows = shadows;
    }

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.PlayerShadows;

    /// <summary>Players moved to their shadow since the module started. For the gate.</summary>
    public long Moves { get; private set; }

    /// <summary>Velocity impulses given since the module started.</summary>
    public long Impulses { get; private set; }

    /// <summary>VPhysicsShadowUpdate for one player.</summary>
    public void Update(EntityHandle player)
    {
        if (_shadows.Probe(player) is not { } s)
            return;

        _wasFrozen.TryGetValue(player, out bool memoryWasFrozen);
        Vector origin = s.Origin;

        var ss = _directory.Resolve(player) as SsPlayer;

        // In a trigger_vphysics_motion's controller: touching, as stock's
        // IsInContact() || PFLAG_VPHYSICS_MOTIONCONTROLLER (player.cpp:8326).
        bool motionController = ss is not null
            && (ss.PhysicsFlags & SsPlayer.PhysicsFlagVPhysicsMotionController) != 0;

        // VPhysicsShadowUpdate's PhysGetListOfPenetratingEntities walk (B7):
        // the ledger is managed's (2.1), so the answer is asked here.
        bool penetrating = s.PenetratingMover != 0 || (_penetratedByMover?.Invoke(player) ?? false);
        bool touched = penetrating || s.ContactBits != 0 || motionController;

        // A rotating push since the last update: check for stuck, and the flag
        // is consumed here whatever else this update does.
        bool rotPushed = false;
        if (ss is not null && (ss.PhysicsFlags & SsPlayer.PhysicsFlagGamePhysicsRotPush) != 0)
        {
            rotPushed = true;
            ss.PhysicsFlags &= ~SsPlayer.PhysicsFlagGamePhysicsRotPush;
        }

        if (s.MoveType == MoveTypeNoClip || !_players.IsAlive(player))
        {
            _moves.SetOldOrigin(player, origin);
            return;
        }

        // A frozen simulation updated nothing (player.cpp:8349-8353).
        bool physicsUpdated = s.PhysicsUpdated != 0 && (_physTimescale?.Invoke() ?? 1f) != 0f;
        if (!physicsUpdated)
            return;

        bool groundIsPhysics = s.GroundPhysicsMass > 0f;
        bool rideable = PlayerShadow.IsRideable(in s);
        bool onGround = (s.Flags & FlagOnGround) != 0;

        Vector newPosition = s.ShadowOrigin;
        Vector newVelocity = s.ShadowVelocity;
        Vector lastValid = newPosition;

        Vector tmp = origin - newPosition;
        if (!touched && !onGround)
            tmp.Z *= 0.5f;

        float dist = tmp.LengthSquared;
        float deltaV = (newVelocity - s.Velocity).LengthSquared;

        float maxDist = MaxDistance * MaxDistance;
        float maxVel = MaxVelocity * MaxVelocity;
        if (rideable)
        {
            maxDist *= 0.25f;
            maxVel *= 0.25f;
        }

        bool tracing = _trace is { On: true };
        string arm = "none";
        Vector impulse = Vector.Zero;
        void Note(string what) => _trace!.Recovered(player, in s, touched, dist, deltaV, what, impulse);

        (Vector mins, Vector maxs) = _entities.GetBounds(player);
        bool checkStuck = rotPushed;
        bool wasFrozen = memoryWasFrozen;

        if (s.WasFrozen != 0)
        {
            wasFrozen = true;

            if (StartsClear(player, origin, mins, maxs))
            {
                if (tracing)
                    Note("frozen-fits");
                _wasFrozen[player] = wasFrozen;
                return;
            }

            // The shadow is probably not in solid: from there toward the game
            // position, and take where it stopped.
            EntityHullTrace back = Sweep(player, newPosition, origin, mins, maxs);
            if (!back.StartSolid)
            {
                if (tracing)
                    Note("frozen-back");
                Move(player, back.EndPos);
                _wasFrozen[player] = wasFrozen;
                return;
            }
        }

        if (dist >= maxDist || deltaV >= maxVel || (groundIsPhysics && !touched))
        {
            if (touched || groundIsPhysics)
            {
                if (deltaV >= maxVel && !wasFrozen)
                {
                    // The shadow's velocity less the player's own along their
                    // travel, clamped to it.
                    Vector dir = s.Velocity;
                    float len = dir.Length;
                    if (len > 0f)
                        dir = dir * (1f / len);
                    float dot = Math.Clamp(newVelocity.Dot(dir), -len, len);
                    newVelocity = newVelocity - dir * dot;

                    // Eased back toward the player's own travel: Lerp( 0.1, len, dot ).
                    if (motionController)
                    {
                        float eased = len + (dot - len) * 0.1f;
                        newVelocity = newVelocity + dir * (eased - len);
                    }

                    if (!rideable)
                    {
                        _entities.ApplyVelocityImpulse(player, newVelocity, clearGround: false);
                        Impulses++;
                        impulse = newVelocity;
                        arm = "impulse";
                    }
                }

                if (Fits(player, newPosition, mins, maxs))
                {
                    Move(player, newPosition);
                    origin = newPosition;
                    arm = arm == "impulse" ? "impulse+move" : "move";
                }
            }
            else
            {
                checkStuck = true;
                arm = "far-untouched";
            }
        }
        else if (touched && !Fits(player, origin, mins, maxs))
        {
            // The physics object may have simulated into the player: use the
            // stuck check to go back if moving to the shadow does not help.
            checkStuck = true;
            lastValid = _moves.OldOrigin(player);
            Move(player, newPosition);
            origin = newPosition;
            arm = "overlapped";
        }

        if (checkStuck && !Fits(player, origin, mins, maxs))
        {
            Move(player, lastValid);
            origin = lastValid;
            arm += "+unstuck";
        }

        if (tracing)
            Note(arm);

        _moves.SetOldOrigin(player, origin);
        _wasFrozen[player] = false;
    }

    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        foreach (var (player, oldOrigin) in _moves.OldOrigins())
            writer.AddRow().Set("player", player).Set("oldOrigin", oldOrigin);
    }

    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        _moves.Clear();
        _wasFrozen.Clear();

        foreach (ReloadRow row in reader.Rows)
        {
            EntityHandle player = reader.Resolve(row.GetHandle("player"));
            if (player != EntityHandle.None)
                _moves.SetOldOrigin(player, row.GetVector("oldOrigin"));
        }
    }

    private void Move(EntityHandle player, Vector to)
    {
        _entities.SetOrigin(player, to);
        Moves++;
    }

    /// <summary>A stationary UTIL_TraceEntity: neither started nor stayed in solid.</summary>
    private bool Fits(EntityHandle player, Vector at, Vector mins, Vector maxs)
    {
        EntityHullTrace t = Sweep(player, at, at, mins, maxs);
        return !t.StartSolid && !t.AllSolid;
    }

    /// <summary>
    /// The frozen arm's self-check, which is <c>if ( !trace.startsolid ) return;</c>
    /// only (player.cpp:8389-8390): an all-solid trace that did not START in
    /// solid still counts as fitting there, where <see cref="Fits"/> refuses it.
    /// </summary>
    private bool StartsClear(EntityHandle player, Vector at, Vector mins, Vector maxs)
        => !Sweep(player, at, at, mins, maxs).StartSolid;

    private EntityHullTrace Sweep(EntityHandle player, Vector from, Vector to, Vector mins, Vector maxs)
        => _world.EntityTraceHull(player, from, to, mins, maxs, GameMovement.MaskPlayerSolid,
                                  GameMovement.CollisionGroupPlayerMovement);
}
