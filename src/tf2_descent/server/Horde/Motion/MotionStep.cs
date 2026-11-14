using SourceSharp.Abi;

using TF2.Shared.Movement;

namespace TF2.Descent.Server.Horde.Motion;

/// <summary>
/// What of a <see cref="MoveState"/> a body carries from one command to the
/// next: its place and speed, the ground flags, the duck and jump timers, the
/// fall speed, the eye, and the buttons it held. A value, so it can be handed
/// to a worker and back without sharing anything.
/// </summary>
public struct BodyKinematics
{
    public Vector Origin;
    public Vector Velocity;
    public int Flags;
    public bool Ducked, Ducking, InDuckJump, HasGround;
    public float DuckTime, DuckJumpTime, JumpTime, FallVelocity, SurfaceFriction;
    public Vector ViewOffset;
    public int OldButtons, WaterLevel, WaterType, OldWaterLevel, StuckLast;

    public readonly bool OnGround => (Flags & GameMovement.FlOnGround) != 0;

    /// <summary>A body as it is put down at <paramref name="at"/>: still, in the air until the first command finds the floor.</summary>
    public static BodyKinematics At(in Vector at, in Vector view) => new()
    {
        Origin = at,
        SurfaceFriction = 1f,
        ViewOffset = view,
    };
}

/// <summary>
/// One body's command for one step: whose it is (body index, the lend's
/// serial, the entity the sweeps pass through), where it starts, the yaw it
/// walks along and how hard (the forward move), the buttons it holds, its
/// speed cap, its scale (the hull and eye follow it), the clock, and the
/// distance the wish asks this step (what the blocked test measures against).
/// </summary>
public struct StepInput
{
    public int Body;
    public int Serial;
    public EntityHandle Handle;
    public BodyKinematics State;
    public float Yaw;
    public float ForwardMove;

    /// <summary>The sideways move: the teammate pushaway's part of the command.</summary>
    public float SideMove;
    public int Buttons;
    public float MaxSpeed;
    public float Scale;
    public float CurTime;
    public float Expect;

    /// <summary>The body the pushaway pushed this one from (0 for none).</summary>
    public int Crowd;
}

/// <summary>
/// One body's step done: the state the movement left, and the last thing a
/// sweep of the step struck that was not floor (a wall, a prop, a body, a
/// hero), when one was.
/// </summary>
public struct StepOutput
{
    public int Body;
    public int Serial;
    public float Expect;
    public int Crowd;
    public BodyKinematics State;
    public bool Struck;
    public MotionTrace Blocker;
}

/// <summary>One movement step of one body. An instance serves one thread at a time.</summary>
public interface IMotionStep
{
    void Run(in StepInput input, out StepOutput output);
}

/// <summary>
/// The game's own movement (<see cref="GameMovement"/>, the managed port of the
/// player's) run on a body's state against <see cref="IMotionWorld"/>, with
/// TF's body (<see cref="TFViewVectors"/>: hull, eye, step, gravity, jump
/// height, the accelerations and friction) scaled by the robot's scale, as a
/// player's hull follows its model scale. A command is the robot's wish: the
/// view yaw it walks along, the forward move, jump and crouch. The rules are
/// stock's (no class, no condition, no weapon), except the arm: TF's (the
/// non-HL2 fall and jump constants). Water is not a robot's: every point is
/// empty. One <see cref="MoveState"/> and one loop, reused for every body, so
/// a step allocates nothing.
/// </summary>
public sealed class GameMovementStep : IMotionStep
{
    /// <summary>The robot's health as the loop reads it: alive.</summary>
    private const int Health = 100;

    private readonly GameMovement _loop;
    private readonly StepWorld _world;
    private readonly MoveState _s = new();
    private readonly MovementParams _body;
    private readonly float _tickSeconds;

    private GameMovementStep(GameMovement loop, StepWorld world, MovementParams body, float tickSeconds)
    {
        _loop = loop;
        _world = world;
        _body = body;
        _tickSeconds = tickSeconds;
    }

    /// <summary>A step over <paramref name="world"/> at <paramref name="tickSeconds"/> a command.</summary>
    public static GameMovementStep Create(IMotionWorld world, float tickSeconds)
    {
        MovementParams body = TFViewVectors.Params;
        var stepWorld = new StepWorld(world);
        var loop = new GameMovement(stepWorld, Constants(body, tickSeconds))
        {
            Rules = MovementRules.Stock with { NoHl2Arms = static () => true },
            JumpHeight = body.JumpHeight,
        };
        return new GameMovementStep(loop, stepWorld, body, tickSeconds);
    }

    /// <summary>The standing eye of a body at <paramref name="scale"/>.</summary>
    public static Vector EyeOf(float scale) => TFViewVectors.Params.View * scale;

    public void Run(in StepInput input, out StepOutput output)
    {
        MoveState s = _s;
        Load(s, in input.State);

        var view = new Vector(0f, input.Yaw, 0f);
        s.MoveType = GameMovement.MoveTypeWalk;
        s.StepSize = _body.StepSize;
        s.ModelScale = input.Scale;
        s.EntityGravity = 1f;
        s.CurrentGravity = _body.Gravity;
        s.LaggedMovementValue = 1f;
        s.AllowAutoMovement = true;
        s.IsServer = true;
        s.IsAlive = true;
        s.DeadFlag = false;
        s.Health = Health;
        s.EntIndex = input.Body;
        s.FirstRunOfFunctions = true;
        s.GameCodeMovedPlayer = false;
        s.ViewAngles = view;
        s.Angles = view;
        s.OldAngles = view;
        s.ForwardMove = input.ForwardMove;
        s.SideMove = input.SideMove;
        s.UpMove = 0f;
        s.Buttons = input.Buttons;
        s.MaxSpeed = input.MaxSpeed;
        s.ClientMaxSpeed = input.MaxSpeed;
        s.FrameTime = _tickSeconds;
        s.CurTime = input.CurTime;
        s.GroundVelocity = Vector.Zero;
        s.BaseVelocity = Vector.Zero;
        s.ConstraintRadius = 0f;

        _world.Begin(input.Handle);
        _loop.ProcessMovement(s);
        s.OldButtons = input.Buttons;

        output = new StepOutput
        {
            Body = input.Body,
            Serial = input.Serial,
            Expect = input.Expect,
            Crowd = input.Crowd,
            State = Save(s),
            Struck = _world.Struck,
            Blocker = _world.Blocker,
        };
    }

    private static void Load(MoveState s, in BodyKinematics k)
    {
        s.Origin = k.Origin;
        s.Velocity = k.Velocity;
        s.Flags = k.Flags;
        s.Ducked = k.Ducked;
        s.Ducking = k.Ducking;
        s.InDuckJump = k.InDuckJump;
        s.HasGround = k.HasGround;
        s.DuckTime = k.DuckTime;
        s.DuckJumpTime = k.DuckJumpTime;
        s.JumpTime = k.JumpTime;
        s.FallVelocity = k.FallVelocity;
        s.SurfaceFriction = k.SurfaceFriction;
        s.ViewOffset = k.ViewOffset;
        s.OldButtons = k.OldButtons;
        s.WaterLevel = k.WaterLevel;
        s.WaterType = k.WaterType;
        s.OldWaterLevel = k.OldWaterLevel;
        s.StuckLast = k.StuckLast;
    }

    private static BodyKinematics Save(MoveState s) => new()
    {
        Origin = s.Origin,
        Velocity = s.Velocity,
        Flags = s.Flags,
        Ducked = s.Ducked,
        Ducking = s.Ducking,
        InDuckJump = s.InDuckJump,
        HasGround = s.HasGround,
        DuckTime = s.DuckTime,
        DuckJumpTime = s.DuckJumpTime,
        JumpTime = s.JumpTime,
        FallVelocity = s.FallVelocity,
        SurfaceFriction = s.SurfaceFriction,
        ViewOffset = s.ViewOffset,
        OldButtons = s.OldButtons,
        WaterLevel = s.WaterLevel,
        WaterType = s.WaterType,
        OldWaterLevel = s.OldWaterLevel,
        StuckLast = s.StuckLast,
    };

    /// <summary>The loop's constants from TF's body, at the robot tick.</summary>
    private static MoveConstants Constants(in MovementParams body, float tickSeconds)
    {
        MoveConstants c = MoveConstantDefaults.Stock;
        c.Friction = body.Friction;
        c.Accelerate = body.Accelerate;
        c.AirAccelerate = body.AirAccelerate;
        c.StopSpeed = body.StopSpeed;
        c.MaxVelocity = body.MaxVelocity;
        c.MaxSpeed = body.MaxSpeed;
        c.AirSpeedCap = body.AirSpeedCap;
        c.DuckSpeedScale = body.DuckSpeedScale;
        c.Optimizations = true;
        c.TickInterval = tickSeconds;
        c.View = body.View;
        c.HullMin = body.HullMin;
        c.HullMax = body.HullMax;
        c.DuckHullMin = body.DuckHullMin;
        c.DuckHullMax = body.DuckHullMax;
        c.DuckView = body.DuckView;
        c.ObsHullMin = body.ObsHullMin;
        c.ObsHullMax = body.ObsHullMax;
        c.DeadViewHeight = body.DeadViewHeight;
        c.NoHl2Arms = true;
        return c;
    }

    /// <summary>
    /// The loop's world over <see cref="IMotionWorld"/>: each trace a sweep
    /// passing through the moving body, the last non-floor stop kept as the
    /// step's blocker, no water, the default surface, and none of a player's
    /// effects (sounds, touches, fall damage).
    /// </summary>
    private sealed class StepWorld(IMotionWorld world) : IMoveWorld
    {
        /// <summary>The steepest a face can be and still be floor: stock's 0.7.</summary>
        private const float FloorNormalZ = 0.7f;

        /// <summary>The default surfaceprop's friction, as a robot's feet read every floor.</summary>
        private const float Friction = 0.8f;

        private EntityHandle _ignore;

        public bool Struck { get; private set; }

        public MotionTrace Blocker { get; private set; }

        public void Begin(EntityHandle ignore)
        {
            _ignore = ignore;
            Struck = false;
            Blocker = default;
        }

        public MoveTrace Trace(in Vector start, in Vector end, in Vector mins, in Vector maxs, uint mask, int collisionGroup)
        {
            var t = world.Sweep(start, end, mins, maxs, _ignore);
            if (!t.Hit)
                return MoveTrace.Clear(end);

            bool moving = start.X != end.X || start.Y != end.Y || start.Z != end.Z;
            if (moving && t.Normal.Z < FloorNormalZ && (t.Fraction < 1f || t.StartSolid))
            {
                Struck = true;
                Blocker = t;
            }

            return new MoveTrace
            {
                Fraction = t.Fraction,
                EndPos = t.End,
                PlaneNormal = t.Normal,
                PlaneDist = t.End.X * t.Normal.X + t.End.Y * t.Normal.Y + t.End.Z * t.Normal.Z,
                StartSolid = t.StartSolid,
                AllSolid = t.AllSolid,
                SurfaceProps = 0,
                Contents = 1,
                HasEntity = true,
                HasGround = true,
                HitWorld = t.EntityIndex == 0,
                HitEntity = t.Entity,
            };
        }

        public int PointContents(in Vector point, int slot) => 0;

        public void ResetPointContentsCache(bool optimizations)
        {
        }

        public MoveSurface Surface(int surfaceProps) => new() { Valid = true, Friction = Friction, MaxSpeedFactor = 1f, JumpFactor = 1f };

        public void ResetTouchList()
        {
        }

        public void AddToTouched(in MoveTrace trace, in Vector velocity)
        {
        }

        public void StartSound(in Vector origin, string sound)
        {
        }

        public void PlayerSetAnimation(int activity)
        {
        }

        public bool PlayerFallingDamage(float damage) => true;

        public void PlayStepSound(in Vector origin, int surfaceProps, float volume, bool force)
        {
        }

        public void UpdateStepSound(in Vector origin, in Vector velocity, int surfaceProps)
        {
        }

        public void Splash()
        {
        }

        public void RumbleEffect(byte index, byte data, byte flags)
        {
        }

        public float EngineTime() => 0f;

        public void ResetDuckLatched()
        {
        }

        public void SetPlayerSurface(int material)
        {
        }
    }
}
