using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Shared;
using TF2.Shared.Anim;
using TF2.Shared.Econ;
using TF2.Shared.Movement;
using TF2.Shared.Player;
using TF2.Shared.Rules;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// Makes blast-jump simulators (plan_bots_teams_tf2_part_g G.5 step 3) over
/// the mod's own movement: the constants are <see cref="TFViewVectors"/>',
/// and the rules are a <see cref="TFMovementRules"/> of the simulator's own,
/// built from the same class table, convars and attribute hooks the game's
/// is, over a carrier that holds one simulated Soldier
/// (<see cref="SimulatedPlayers"/>) and seams that answer nothing: nobody
/// holds a weapon, the round runs, nobody is on a team.
///
/// <para>
/// <b>The one arm not the rule object's.</b> Its max-speed arm reads the
/// engine (the player's handle, observer mode and team,
/// <c>TFMovementRules.Inputs</c>), which a simulated player has none of and a
/// worker thread may not ask. So that arm is answered here, by the same
/// <see cref="TFMaxSpeed.Calculate"/> over the simulated carrier's inputs;
/// the engine seams are handed to the rule object only because its
/// constructor takes them, and no arm a simulation runs asks them
/// (the Jump facts hold that: their rig's engine seams throw).
/// </para>
///
/// <para>
/// <see cref="Create"/> and <see cref="Mover(VoxelColumns, float)"/> are called
/// on the game's thread (they read the class table, which loads on first
/// use); what they return then runs on any one thread.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class BlastJumpSims(TFClassTable classes, AttributeHooks hooks, IEnginePlayers enginePlayers, IEngineEntities entities,
                                  TFScoutAirDashCountConVar dashCount, TFSpyMaxCloakedSpeedConVar cloakedCap,
                                  TFClampBackSpeedConVar backSpeed, TFClampBackSpeedMinConVar backSpeedMin,
                                  TFPlayerMovementRestartFreezeConVar restartFreeze,
                                  TFMovementAircurrentFrictionMultConVar airCurrentFriction,
                                  TFMovementLostFootingRestickConVar lostFootingRestick,
                                  TFMovementLostFootingFrictionConVar lostFootingFriction, TFMaxChargeSpeedConVar chargeSpeed,
                                  TFParachuteMaxSpeedXYConVar parachuteXY, TFParachuteMaxSpeedZConVar parachuteZ,
                                  TFParachuteMaxSpeedOnfireZConVar parachuteOnfireZ, TFClampAirducksConVar clampAirducks,
                                  TFParachuteAirControlConVar parachuteAirControl, TFDemomanChargeFrametimeScalingConVar chargeScaling)
{
    /// <summary>The server's tick, when a caller names none.</summary>
    public const float TickSeconds = 0.015f;

    /// <summary>
    /// A simulator over <paramref name="columns"/> firing <paramref name="launcher"/>,
    /// with a body and a rule object of its own.
    /// </summary>
    public BlastJumpSim Create(VoxelColumns columns, JumpLauncher launcher, float tickSeconds = TickSeconds)
    {
        MovementParams body = TFViewVectors.Params;
        var world = new VoxelMoveWorld(columns);

        // The boxes a blast measures its shooter by are the body's hulls, the ones the server gives a TF player standing and ducked.
        return new BlastJumpSim(world, Mover(world, tickSeconds), launcher, body.HullMin, body.HullMax, body.DuckHullMin, body.DuckHullMax,
                                body.Gravity);
    }

    /// <summary>A simulated Soldier's movement alone over <paramref name="columns"/>, with a rule object of its own.</summary>
    public SoldierMover Mover(VoxelColumns columns, float tickSeconds = TickSeconds) => Mover(new VoxelMoveWorld(columns), tickSeconds);

    private SoldierMover Mover(VoxelMoveWorld world, float tickSeconds)
    {
        MovementParams body = TFViewVectors.Params;
        var players = new SimulatedPlayers();
        var rules = new TFMovementRules(classes, players, dashCount, cloakedCap, new NoTFWeaponOwners(), hooks, backSpeed, backSpeedMin,
                                        new RunningRound(), enginePlayers, restartFreeze, airCurrentFriction, lostFootingRestick,
                                        lostFootingFriction, chargeSpeed, new NoHeldWeapons(), entities, parachuteXY, parachuteZ,
                                        parachuteOnfireZ, new NoTeams(), clampAirducks, parachuteAirControl, chargeScaling);

        // The pack the game's loop takes, built the way the side builds its own (one owner of movement: this rule object).
        MovementRules pack = new MovementRulesResolver(new ServiceContainer().AddInstance<IMovementRules>(rules)).Rules with
        {
            MaxSpeed = (index, maxSpeed, _, _) => players.Predicted(index) is { } state
                ? TFMaxSpeed.Calculate(classes, TFMoveInputs.Of(state), cloakedCap.Value, hooks, null, static _ => null, index, null)
                : maxSpeed,
        };

        // The class table loads on first use: here, on the caller's thread, not in a flight.
        _ = classes[TFClass.Soldier];

        var loop = new GameMovement(world, Constants(body, tickSeconds));
        return new SoldierMover(loop, pack, players, body, tickSeconds);
    }

    /// <summary>The loop's constants as the server reads them each frame (<c>ServerMoveConstants</c>), from the mod's parameters.</summary>
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
        c.Optimizations = ServerMoveConstants.Optimizations;
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
        return c;
    }

    /// <summary>The round the simulated player plays in: running, nobody the winner.</summary>
    internal sealed class RunningRound : ITFRoundStateView
    {
        public RoundState State => RoundState.RoundRunning;

        public TFTeam Winner => TFTeam.Unassigned;
    }

    /// <summary>Nothing in hand: no weapon changes how the simulated player moves.</summary>
    internal sealed class NoHeldWeapons : ITFAnimWeapons
    {
        public TFAnimWeapon? ActiveWeapon(int playerIndex) => null;

        public TFAnimWeapon? DisguiseWeapon(int playerIndex) => null;
    }

    /// <summary>Nobody on a team: the solid mask is the base one, and the world here has no team's clips.</summary>
    internal sealed class NoTeams : ITFPlayerTeam
    {
        public TFTeam Of(int playerIndex) => TFTeam.Unassigned;
    }
}
