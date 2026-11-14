using SourceSharp.Abi;
using TF2.Shared.Player;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>Where a simulated body starts: its place and speed, the view and buttons it held, and whether it stands and crouches.</summary>
public readonly record struct JumpStart(Vector Origin, Vector Velocity, Vector View, bool Ducked, bool OnGround, int Buttons = 0);

/// <summary>One usercmd as the movement reads it: the view (pitch, yaw, roll), the three moves and the buttons.</summary>
public readonly record struct JumpCommand(Vector View, float ForwardMove, float SideMove, float UpMove, int Buttons);

/// <summary>Where the body was after one command.</summary>
public readonly record struct JumpPoint(Vector Origin, Vector Velocity, bool OnGround, bool Ducking);

/// <summary>
/// A simulated Soldier's movement (plan_bots_teams_tf2_part_g G.5 step 3): the
/// game's own loop (<see cref="GameMovement"/>) under the mod's own rules,
/// one command a call, with what the server does around a command
/// (<c>ServerPlayerCommands</c>' prepare and finish) done here in the same
/// order. Made by <see cref="BlastJumpSims"/>. It holds one body's state, so
/// one mover serves one thread at a time.
/// </summary>
public sealed class SoldierMover
{
    /// <summary>The simulated player's entity index: the one the rules and the carrier are asked by.</summary>
    public const int PlayerIndex = 1;

    /// <summary>The server's <c>m_flMaxSpeed</c>, which nothing writes after the constructor's 320; the rules answer the class's speed.</summary>
    public const float ServerMaxSpeed = 320f;

    /// <summary>The health the body moves with: alive, and no rule here reads more of it.</summary>
    private const int Health = 200;

    private readonly GameMovement _loop;
    private readonly MovementRules _rules;
    private readonly SimulatedPlayers _players;
    private readonly MovementParams _body;
    private MoveState _s = new();
    private Vector _viewAngles;
    private int _commands;

    internal SoldierMover(GameMovement loop, MovementRules rules, SimulatedPlayers players, MovementParams body, float tickSeconds)
    {
        _loop = loop;
        _rules = rules;
        _players = players;
        _body = body;
        TickSeconds = tickSeconds;
    }

    /// <summary>One command's length.</summary>
    public float TickSeconds { get; }

    public Vector Origin => _s.Origin;

    public Vector Velocity => _s.Velocity;

    /// <summary>Whether the last command left it on the ground (<c>FL_ONGROUND</c>).</summary>
    public bool OnGround => (_s.Flags & GameMovement.FlOnGround) != 0;

    /// <summary>Whether it is crouched (<c>FL_DUCKING</c>): the hull is the low one.</summary>
    public bool Ducking => (_s.Flags & GameMovement.FlDucking) != 0;

    /// <summary>Where it is now, as a track holds it.</summary>
    public JumpPoint Point => new(Origin, Velocity, OnGround, Ducking);

    /// <summary>Where its eyes are: the origin and the view offset the loop left.</summary>
    public Vector EyePosition => _s.Origin + _s.ViewOffset;

    /// <summary>The view it was last commanded.</summary>
    public Vector ViewAngles => _viewAngles;

    /// <summary>Commands run since the start.</summary>
    public int Commands => _commands;

    /// <summary>The body as a spawn leaves it, put where <paramref name="start"/> says.</summary>
    public void Reset(in JumpStart start)
    {
        _players.State = SimulatedPlayers.Soldier();
        int flags = ServerCommandRules.FlClient | (start.OnGround ? GameMovement.FlOnGround : 0) | (start.Ducked ? GameMovement.FlDucking : 0);
        _s = new MoveState
        {
            // ServerPlayerMove's constructor values and its spawn.
            SurfaceFriction = 1f, EntityGravity = 1f, MaxSpeed = ServerMaxSpeed, ClientMaxSpeed = ServerMaxSpeed, MoveType = GameMovement.MoveTypeWalk,
            StepSize = _body.StepSize, AllowAutoMovement = true, LaggedMovementValue = 1f, ModelScale = 1f, IsServer = true,
            Flags = flags, Origin = start.Origin, Velocity = start.Velocity, Ducked = start.Ducked, HasGround = start.OnGround,
            // The first command's move sets the class's own eye height (the rules' eye arm).
            ViewOffset = start.Ducked ? _body.DuckView : _body.View,
            FallVelocity = start.OnGround ? 0f : MathF.Max(0f, -start.Velocity.Z),
            OldButtons = start.Buttons, Angles = start.View, EntIndex = PlayerIndex, Health = Health, IsAlive = true,
        };
        _viewAngles = start.View;
        _commands = 0;
    }

    /// <summary>Runs one command: the server's prepare, the loop, the server's finish.</summary>
    public void Step(in JumpCommand command)
    {
        MoveState s = _s;
        _commands++;

        // The prepare (ServerPlayerCommands.Prepare): the command's inputs set up. Nothing here gives a base velocity to fold.
        Vector view = ServerCommandRules.SaneViewAngles(command.View);
        var cmd = new UserCmd
        {
            Pitch = view.X, Yaw = view.Y, Roll = view.Z, ForwardMove = command.ForwardMove, SideMove = command.SideMove, UpMove = command.UpMove,
            Buttons = command.Buttons, CommandNumber = _commands,
        };
        ServerCommandRules.SetupMove(s, in cmd, view, view, _viewAngles, s.Origin, s.Origin, ServerMaxSpeed, s.OldButtons, 0f);
        _viewAngles = view;

        // The move (ServerPlayerCommands.Move): the inputs that are not the player's own words, then the loop.
        s.FrameTime = TickSeconds * s.LaggedMovementValue;
        s.CurTime = _commands * TickSeconds;
        s.DeadFlag = false;
        s.Health = Health;
        s.IsAlive = true;
        s.GroundVelocity = Vector.Zero;
        s.EntIndex = PlayerIndex;
        s.IsServer = true;
        s.CurrentGravity = _body.Gravity;

        _loop.JumpHeight = _body.JumpHeight;
        _loop.Rules = _rules;
        _loop.ProcessMovement(s);

        // The finish: the old buttons are the command's.
        s.OldButtons = command.Buttons;
    }

    /// <summary>
    /// A push between commands, as the server adds one to a player
    /// (<c>ServerPlayerMovement.ApplyVelocityImpulse</c>): checked as an
    /// entity's velocity is, added, and the ground left as it was.
    /// </summary>
    public void Push(Vector impulse)
    {
        if (impulse.X == 0f && impulse.Y == 0f && impulse.Z == 0f)
            return;
        if (EntityPhysicsMotion.CheckEntityVelocity(ref impulse) >= 0)
            _s.Velocity += impulse;
    }

    /// <summary>The shooter's own blast forgives the air ducks spent (<c>TFBlastApplier.Push</c>'s duck reset).</summary>
    public void ForgiveAirDucks() => _players.State.AirDuckedCount = 0;
}

/// <summary>
/// The carrier the mod's rules read the simulated player from: one Soldier
/// at <see cref="SoldierMover.PlayerIndex"/>, nobody else, no engine.
/// </summary>
internal sealed class SimulatedPlayers : ITFPlayerData
{
    public TFPlayerNetState State { get; set; } = Soldier();

    /// <summary>A Soldier's carrier as a spawn leaves it: no condition, no duck spent.</summary>
    public static TFPlayerNetState Soldier()
    {
        var state = new TFPlayerNetState();
        state.BindLocal();
        state.OwnerIndex = SoldierMover.PlayerIndex;
        state.SetClass(global::TF2.Shared.TFClass.Soldier);
        return state;
    }

    public TFPlayerNetState? Predicted(int playerIndex) => playerIndex == SoldierMover.PlayerIndex ? State : null;

    public TFPlayerNetState? Networked(int playerIndex) => Predicted(playerIndex);
}
