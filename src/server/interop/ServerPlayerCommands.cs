//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// Where a player's movement state goes after it changes: the player's
/// networked state (PlayerNetState), which the server's logic owns. The words
/// the engine's side holds -- the shell's origin, health, whether it is alive
/// -- native writes beside it (PlayerSyncEngineSide).
/// </summary>
public interface IPlayerMovePublisher
{
    void Publish(EntityHandle player, ServerPlayerMove move);
}

/// <summary>
/// Stock's <c>moveHelper-&gt;ProcessImpacts()</c> (player_command.cpp
/// RunCommand, after FinishMove): the player's trigger touches, once per
/// command. Stock's CMoveHelperServer::ProcessImpacts runs
/// <c>PhysicsTouchTriggers</c>, which calls <c>Touch</c> on every trigger the
/// player is in; a trigger_push's base velocity is set there and consumed by
/// the next command's CheckMovingGround, so it is renewed exactly once per
/// command. The server's logic answers it (the triggers are its).
/// </summary>
public interface IPlayerCommandImpacts
{
    void ProcessImpacts(EntityHandle player);
}

/// <summary>
/// The words that outlived a command, and a player, natively: the global
/// CMoveData's three outputs the next command read back in (g_pMoveData's
/// m_outStepHeight, m_outWishVel, m_outJumpVel) and the mover's two latches
/// (CShellGameMovement::m_nOnLadder, m_nOldWaterLevel). One per server, as
/// the native singletons were.
/// </summary>
public sealed class ServerMoveData
{
    public float OutStepHeight { get; set; }
    public Vector OutWishVel { get; set; }
    public Vector OutJumpVel { get; set; }
    public int OnLadder { get; set; }
    public int OldWaterLevel { get; set; }
}

/// <summary>
/// The server's user commands, run in managed (plan_server_native_v2 T4g).
///
/// <para>
/// CStandalonePlayer::RunQueuedCommands, RunCommandStage and RunCommandMove,
/// and shell_movement.cpp's RunManagedCommand, which between them were
/// stock's CPlayerMove::RunCommand around the movement loop: the tick base,
/// the clock, the angle sanity and fixangle, the base velocity fold,
/// SetupMove, the loop (managed already), the ground write-back, FinishMove
/// and the publish. The decisions are <see cref="ServerCommandRules"/>; the
/// player's state is <see cref="ServerPlayerMove"/>; native answers what the
/// engine's side holds and carries out the origin, the angles, the CPlayerState
/// view angle and the trigger sweep.
/// </para>
///
/// <para>
/// THE SAME THREE STAGES, at the same points of <see cref="PlayerCommandQueue.Run"/>:
/// the prepare before phase 0, the move between phases 0 and 1, and the tick
/// base and publish after the buttons, because the managed phases change state
/// the publish carries.
/// </para>
/// </summary>
public sealed class ServerPlayerCommands
{
    private readonly EngineSide _api;
    private readonly IMovementServerArea _movementArea;
    private readonly ServerPlayerMoves _moves;
    private readonly ServerMoveData _data;
    private readonly ServerMoveWorld _world;
    private readonly GameMovement _loop;
    private readonly MovementRunner _movement;
    private readonly Func<MoveFrameConstants> _constants;
    private readonly IPlayerMovePublisher _publisher;
    private readonly IEngineEnvironment _environment;

    // The command being run: what stage 0 learnt that stage 1 reads.
    private PlayerCommandFacts _facts;

    internal ServerPlayerCommands(EngineSide api, IMovementServerArea movementArea, ServerMoveWorld world,
                                  GameMovement loop, ServerPlayerMoves moves, ServerMoveData data,
                                  MovementRunner movement, Func<MoveFrameConstants> constants,
                                  IPlayerMovePublisher publisher, IEngineEnvironment environment)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _movementArea = movementArea ?? throw new ArgumentNullException(nameof(movementArea));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _loop = loop ?? throw new ArgumentNullException(nameof(loop));
        _moves = moves ?? throw new ArgumentNullException(nameof(moves));
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _movement = movement ?? throw new ArgumentNullException(nameof(movement));
        _constants = constants ?? throw new ArgumentNullException(nameof(constants));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <summary>
    /// The command loop over <paramref name="engine"/>'s side and its movement
    /// area, taken here once, with the world it moves players through.
    /// </summary>
    /// <param name="sounds">The movement's noises (the swim stroke, a landing), to the managed SoundEmitter.</param>
    /// <param name="game">The module the surface change goes to; null sends it nowhere.</param>
    /// <param name="trace">The side's engine trace, as its composition registered it.</param>
    /// <param name="surfaces">The side's surface table, as its composition registered it.</param>
    public static ServerPlayerCommands For(ModuleEngine engine, IFwdEngineTrace trace, IFwdPhysicsSurfaceProps surfaces,
                                           ServerPlayerMoves moves, ServerMoveData data,
                                           IMoveSoundSink sounds, IServerModule? game,
                                           MovementRunner movement, Func<MoveFrameConstants> constants,
                                           IPlayerMovePublisher publisher, IEngineEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(engine);
        EngineSide api = engine.Api;
        IMovementServerArea movementArea = api.Attachment.Areas.Require<IMovementServerArea>();
        var world = new ServerMoveWorld(api, movementArea, trace, surfaces, sounds, game);
        return new ServerPlayerCommands(api, movementArea, world, new GameMovement(world, default), moves, data,
                                        movement, constants, publisher, environment);
    }

    /// <summary>The module's three movement decisions, handed to the loop for each run.</summary>
    public MovementRules Rules { get; set; } = MovementRules.Stock;

    /// <summary>The per-command trigger touches (<see cref="IPlayerCommandImpacts"/>); null touches nothing.</summary>
    public IPlayerCommandImpacts? Impacts { get; set; }

    /// <summary>
    /// The useable ladders the loop climbs (HL2MP's CHL2GameMovement); null is
    /// base CGameMovement's brush ladders.
    /// </summary>
    public ILadderWorld? Ladders
    {
        get => _loop.Ladders;
        set => _loop.Ladders = value;
    }

    /// <summary>The three stages, as <see cref="PlayerCommandQueue.Run"/> calls them. Built once.</summary>
    public PlayerCommandStage Stage => _stage ??= RunStage;

    private PlayerCommandStage? _stage;

    /// <summary>How many commands the loop ran.</summary>
    public long Ran { get; private set; }

    /// <summary>
    /// AdjustPlayerTimeBase for a batch of <paramref name="commands"/>, as
    /// RunQueuedCommands placed it before the batch ran: a base never placed
    /// (0), or single player, pins it.
    /// </summary>
    public void AdjustTickBase(EntityHandle player, int commands)
    {
        Globals g = _environment.Globals;
        if (!g.IsAvailable)
            return;

        ServerPlayerMove m = _moves.For(player);
        m.TickBase = ServerCommandRules.AdjustTickBase(
            m.TickBase, g.TickCount, commands, g.SimTicksThisFrame,
            ServerCommandRules.ClockCorrectionTicks(g.IntervalPerTick),
            m.TickBase == 0 || g.MaxClients == 1);
    }

    private int RunStage(EntityHandle player, int index, int stage, in UserCmd cmd)
        => stage switch
        {
            0 => Prepare(player, in cmd) ? 1 : 0,
            1 => Move(player, in cmd) ? 1 : 0,
            2 => Finish(player) ? 1 : 0,
            _ => 0,
        };

    //-------------------------------------------------------------------------
    // Stage 0: the prepare, before phase 0.
    //-------------------------------------------------------------------------
    private bool Prepare(EntityHandle player, in UserCmd cmd)
    {
        Globals g = _environment.Globals;
        if (!g.IsAvailable)
            return false;

        ServerPlayerMove m = _moves.For(player);
        MoveState s = m.Move;
        float tick = g.IntervalPerTick;

        // Stock's PlayerRunCommand clears m_touchedPhysObject first; the touch
        // list it is decided from is per command.
        m.ClearTouches();

        // The clock the command runs on.
        g.SetClock(m.TickBase * tick, tick);

        _facts = default;
        _facts.Ground = m.Ground.Value;
        if (!_api.PlayerCommandBegin(_movementArea, player, ref _facts))
            return false;

        Vector viewAngles = ServerCommandRules.SaneViewAngles(new Vector(cmd.Pitch, cmd.Yaw, cmd.Roll));

        Vector velocity = s.Velocity, baseVelocity = s.BaseVelocity;
        int flags = s.Flags;
        ServerCommandRules.FoldBaseVelocity(ref velocity, ref baseVelocity, ref flags, tick);
        s.Velocity = velocity;
        s.BaseVelocity = baseVelocity;
        s.Flags = flags;

        // The old angles are last command's authority, recorded before this
        // command's overwrite them.
        Vector oldAngles = _facts.VAngle;
        (Vector vAngle, int fixAngle) = ServerCommandRules.CommandAngles(
            _facts.VAngle, _facts.FixAngle, _facts.AngleChange, viewAngles);

        if (_facts.FixAngle != ServerCommandRules.FixAngleAbsolute)
            _api.PlayerCommandView(_movementArea, player, vAngle, fixAngle);

        ServerCommandRules.SetupMove(s, in cmd, viewAngles, vAngle, oldAngles, _facts.Origin,
                                     m.PreviouslyPredictedOrigin, m.MaxSpeed, s.OldButtons, m.OldForwardMove);
        return true;
    }

    //-------------------------------------------------------------------------
    // Stage 1: the move, between the two managed phases.
    //-------------------------------------------------------------------------
    private bool Move(EntityHandle player, in UserCmd cmd)
    {
        Globals g = _environment.Globals;
        ServerPlayerMove m = _moves.For(player);
        MoveState s = m.Move;
        MoveFrameConstants frame = _constants();

        // The loop's inputs that are not the player's own words.
        s.FrameTime = g.IsAvailable ? g.FrameTime * s.LaggedMovementValue : 0f;
        s.CurTime = g.IsAvailable ? g.CurTime : 0f;
        s.DeadFlag = _facts.DeadFlag != 0;
        s.Health = _facts.Health;
        s.IsAlive = _facts.Health > 0 && _facts.DeadFlag == 0;
        s.HasGround = _facts.GroundResolves != 0;
        s.GroundVelocity = _facts.GroundVelocity;
        s.ModelScale = _facts.ModelScale;
        s.EntIndex = _facts.EntIndex;
        s.InAVehicle = false;
        s.IsServer = true;
        s.CurrentGravity = frame.CurrentGravity;
        s.OnLadder = _data.OnLadder;
        s.OldWaterLevel = _data.OldWaterLevel;
        s.OutStepHeight = _data.OutStepHeight;
        s.OutWishVel = _data.OutWishVel;
        s.OutJumpVel = _data.OutJumpVel;

        // FullObserverMove's target, from the side's own observer.
        s.HasObserverTarget = false;
        if (s.ObserverMode is GameMovement.ObsModeInEye or GameMovement.ObsModeChase or GameMovement.ObsModePoi
            && _movement.ObserverTargetPose?.Invoke(s.EntIndex) is { } pose)
        {
            s.HasObserverTarget = true;
            s.ObserverTargetOrigin = pose.Origin;
            s.ObserverTargetAngles = pose.Angles;
            s.ObserverTargetVelocity = pose.Velocity;
        }

        _world.Player = player;
        _world.EntIndex = _facts.EntIndex;
        _world.Host = m;
        _world.Time = frame.EngineTime;
        _world.ResetTouchList();

        _loop.Constants = frame.Constants;
        _loop.JumpHeight = frame.JumpHeight;
        _loop.Rules = Rules;

        bool ran;
        try
        {
            ran = _loop.ProcessMovement(s);
        }
        finally
        {
            _loop.Rules = MovementRules.Stock;
        }

        if (ran)
        {
            Ran++;
            _data.OnLadder = s.OnLadder;
            _data.OldWaterLevel = s.OldWaterLevel;
            _data.OutStepHeight = s.OutStepHeight;
            _data.OutWishVel = s.OutWishVel;
            _data.OutJumpVel = s.OutJumpVel;

            WriteBackGround(m, s, _facts.GroundResolves != 0, _world.LastGround);
        }

        // FinishMove: where the move left them; the next command's "moved by
        // game code?" compares against it; the old buttons are the command's.
        m.PreviouslyPredictedOrigin = s.Origin;
        s.OldButtons = cmd.Buttons;

        // The body's pitch, folded and clamped; the body's angles are the yaw.
        s.Angles = new Vector(ServerCommandRules.BodyPitch(s.Angles.X), s.Angles.Y, s.Angles.Z);

        var result = new PlayerCommandResult
        {
            Ran = ran ? 1 : 0,
            Origin = s.Origin,
            Yaw = s.Angles.Y,
            Flags = s.Flags,
            MoveType = s.MoveType,
            ViewOffset = s.ViewOffset,
            Velocity = s.Velocity,
            BaseVelocity = s.BaseVelocity,
            Ground = m.Ground.Value,
            FallVelocity = s.FallVelocity,
        };

        _api.PlayerCommandFinish(_movementArea, player, in result);

        _world.Host = null;
        _world.ResetTouchList();

        // ProcessImpacts, after FinishMove as stock's RunCommand orders them.
        Impacts?.ProcessImpacts(player);
        return true;
    }

    /// <summary>
    /// THE GROUND, and the one word that cannot round-trip as a value: the loop
    /// carries "is there ground", and the last trace that found standable ground
    /// names which (movement_ground_writeback.h's rule): no ground clears it,
    /// ground already held is left, other ground is set -- including to nothing,
    /// when no trace ever found one, as MvSetGroundEntity( NULL ) did.
    /// </summary>
    /// <param name="held">The ground the record named resolved when the command began.</param>
    /// <param name="lastGround">The last standable ground any trace found.</param>
    internal static void WriteBackGround(ServerPlayerMove m, MoveState s, bool held, EntityHandle lastGround)
    {
        bool holdsTheOneFound = held ? m.Ground == lastGround : lastGround == EntityHandle.None;

        if (!s.HasGround)
        {
            if (held)
                SetGround(m, s, EntityHandle.None);
            return;
        }

        if (!holdsTheOneFound)
            SetGround(m, s, lastGround);
    }

    private static void SetGround(ServerPlayerMove m, MoveState s, EntityHandle ground)
    {
        m.Ground = ground;

        if (ground != EntityHandle.None)
            s.Flags |= GameMovement.FlOnGround;
        else
            s.Flags &= ~GameMovement.FlOnGround;
    }

    //-------------------------------------------------------------------------
    // Stage 2: after phase 1 and the buttons.
    //-------------------------------------------------------------------------
    private bool Finish(EntityHandle player)
    {
        Globals g = _environment.Globals;
        ServerPlayerMove m = _moves.For(player);

        if (g.IsAvailable && g.FrameTime > 0)
            m.TickBase++;

        // The player at this command: what the wire sends and what the client's
        // ring compares its replay of this command against.
        Publish(player, m);
        return true;
    }

    /// <summary>
    /// The record into the player's networked state, and the engine side's
    /// three words beside it: what SyncToBlob wrote, from where each is held.
    /// </summary>
    public void Publish(EntityHandle player, ServerPlayerMove m)
    {
        _publisher.Publish(player, m);
        _api.PlayerSyncEngineSide(_movementArea, player);
    }
}
