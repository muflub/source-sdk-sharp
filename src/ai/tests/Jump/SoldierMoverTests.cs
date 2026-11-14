using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Nav;
using TF2.Shared;
using TF2.Shared.Movement;
using TF2.Shared.Player;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The game's movement loop under the mod's rules over the simulator's world, before any blast
/// (plan_bots_teams_tf2_part_g G.5 step 3): a Soldier runs, crouches, jumps and is stopped as
/// the mod's own numbers say (<see cref="TFViewVectors"/>, the class script,
/// <see cref="TFNavProfile"/>), and no rule the simulation runs asks the engine.
/// </summary>
public class SoldierMoverTests
{
    /// <summary>How far a run of whole ticks may be from a height worked out in continuous time.</summary>
    private const float TickSlack = 1f;

    private static Walked Run(VoxelColumns columns, JumpStart start, params JumpCommand[][] parts) => Walk(columns, start, parts);

    private static float Speed(JumpPoint p) => MathF.Sqrt(p.Velocity.X * p.Velocity.X + p.Velocity.Y * p.Velocity.Y);

    [Fact]
    public void ASoldierRunsAtHisClassScriptsSpeed()
    {
        float script = Classes()[TFClass.Soldier].MaxSpeed;
        Assert.Equal(240f, script);
        Walked run = Run(Room(), Standing(), Hold(200, InForward, forward: ForwardSpeed));
        Assert.Equal(script, Speed(run.Track[^1]), 1);
        // And he went where that speed takes him, east: the last 100 commands at full speed.
        Assert.Equal(script * 100 * TickSeconds, run.Track[^1].Origin.X - run.Track[^101].Origin.X, 1);
        Assert.Equal(-1, run.Takeoff);
    }

    [Fact]
    public void ACrouchedSoldierWalksAtAThirdOfIt()
    {
        Walked run = Run(Room(), Standing(), Hold(200, InForward | InDuck, forward: ForwardSpeed));
        Assert.True(run.Track[^1].Ducking);
        Assert.Equal(Classes()[TFClass.Soldier].MaxSpeed * TFViewVectors.Params.DuckSpeedScale, Speed(run.Track[^1]), 1);
    }

    [Fact]
    public void APlainJumpRisesTheModsJumpLessTheHalfTickOfGravityBeforeIt()
    {
        // A standing jump adds the mod's 289 to a velocity half a tick of gravity has already pulled down
        // (the loop's not-ducking arm, stock TF2's): 283 up, 50 units where the nominal height is 52.
        float g = TFViewVectors.Params.Gravity, nominal = MathF.Sqrt(2f * g * TFViewVectors.JumpHeight), v = nominal - g * TickSeconds * 0.5f;
        Assert.Equal(289f, nominal, 2);
        Walked jump = Run(Room(), Standing(), Hold(1, InJump), Hold(120));
        Assert.Equal(0, jump.Takeoff);
        Assert.True(jump.Landed);
        Assert.InRange(jump.Apex, v * v / (2f * g) - 0.1f, v * v / (2f * g) + 0.1f);
        Assert.InRange(jump.Apex, TFViewVectors.JumpHeight - 2.5f, TFViewVectors.JumpHeight);
        Assert.Equal(2f * v / g, jump.AirSeconds, 1);
    }

    [Fact]
    public void ACrouchJumpLiftsTheFeetTheNavProfilesCrouchJump()
    {
        // The nav's crouch-jump number is the nominal jump and the tuck (the hull's height less the crouched hull's).
        // Jump and duck pressed together: the duck begins, so the jump sets the full 289, and the tuck in the air lifts the feet.
        float crouchJump = TFNavProfile.ModNumbers.MaxCrouchJumpRise;
        Assert.Equal(TFViewVectors.JumpHeight + 20f, crouchJump, 3);
        Walked jump = Run(Room(), Standing(), Hold(1, InJump | InDuck), Hold(60, InDuck));
        Assert.InRange(jump.Apex, crouchJump - 0.25f, crouchJump);
    }

    [Fact]
    public void ARunningJumpClearsALedgeUnderItsHeightAndNotOneOver()
    {
        // 40 is under the standing jump's 50; 56 is over it. At a run, taken off 82 units short, the hull meets the ledge at the apex.
        static VoxelColumns Ledge(float top) => Room((x, _) => x >= 16 ? [new FreeSpan(top, 1024f)] : null);
        JumpStart running = Standing(x: 150f) with { Velocity = new Vector(240f, 0f, 0f) };
        JumpCommand[][] jump = [Hold(1, InForward | InJump, forward: ForwardSpeed), Hold(100, InForward, forward: ForwardSpeed)];
        Walked low = Run(Ledge(40f), running, jump);
        Assert.Equal(40f, low.Track[^1].Origin.Z, 0);
        Assert.True(low.Track[^1].Origin.X > 256f);
        Walked high = Run(Ledge(56f), running, jump);
        Assert.Equal(0f, high.Track[^1].Origin.Z, 0);
        Assert.True(high.Track[^1].Origin.X < 256f - 24f);
    }

    [Fact]
    public void AHeldJumpDoesNotJumpAgainOnLanding()
    {
        // 0.71 s in the air, then 90 more commands with the button still down: he stays down.
        Walked run = Run(Room(), Standing(), Hold(140, InJump));
        Assert.True(run.Landed);
        Assert.All(run.Track[(run.Landing + 1)..], p => Assert.True(p.OnGround));
    }

    [Fact]
    public void AJumpReleasedAndPressedAgainJumpsAgain()
    {
        Walked run = Run(Room(), Standing(), Hold(1, InJump), Hold(70), Hold(1, InJump), Hold(10));
        Assert.True(run.Landed);
        Assert.True(run.Landing < 71);
        Assert.False(run.Track[75].OnGround);
    }

    [Fact]
    public void ARunningSoldierIsStoppedByAWall()
    {
        Walked run = Run(Room((x, _) => x >= 16 ? [] : null), Standing(), Hold(200, InForward, forward: ForwardSpeed));
        Assert.Equal(256f - 24f - VoxelMoveWorld.DistEpsilon, run.Track[^1].Origin.X, 1);
        Assert.Equal(0f, Speed(run.Track[^1]), 1);
    }

    [Fact]
    public void HeStepsUpARiserUnderTheStepSizeWithoutLeavingTheGround()
    {
        // A 16-unit riser, under the mod's 18: the walk carries on, on the upper floor.
        Walked run = Run(Room((x, _) => x >= 16 ? [new FreeSpan(16f, 1024f)] : null), Standing(), Hold(200, InForward, forward: ForwardSpeed));
        Assert.True(TFViewVectors.Params.StepSize > 16f);
        Assert.Equal(16f, run.Track[^1].Origin.Z, 0);
        Assert.True(run.Track[^1].Origin.X > 400f);
        Assert.Equal(-1, run.Takeoff);
    }

    [Fact]
    public void HeWalksOffALedgeAndLandsOnTheFloorUnderIt()
    {
        // The west half stands 128 over the east half.
        Walked run = Run(Room((x, _) => x < 16 ? [new FreeSpan(128f, 1024f)] : null), Standing(z: 128f + VoxelMoveWorld.DistEpsilon),
                             Hold(200, InForward, forward: ForwardSpeed));
        Assert.True(run.Landed);
        Assert.Equal(128f, run.From.Z, 0);
        // The loop takes the ground from 2 units over it, and the next command sets him down.
        Assert.InRange(run.To.Z, 0f, 2f);
        Assert.Equal(0f, run.Track[^1].Origin.Z, 0);
        // A drop of 128 under the mod's gravity takes sqrt( 2 h / g ).
        Assert.Equal(MathF.Sqrt(2f * 128f / TFViewVectors.Params.Gravity), run.AirSeconds, 1);
    }

    [Fact]
    public void TheRefusingEngineRefuses()
        // The control of the facts above: a question to the rig's engine does throw.
        => Assert.Throws<InvalidOperationException>(() => NoPlayers().GetHandle(1));

    [Fact]
    public void TheModsOwnMaxSpeedArmAsksTheEngineWhichIsWhyTheSimulatorAnswersIt()
    {
        // The rule object as the factory builds it, its max-speed arm asked directly: it reads the player's handle.
        var players = new Players();
        ConVarRegistry v = ConVars();
        var rules = new TFMovementRules(Classes(), players, v.Declare<TFScoutAirDashCountConVar>(), v.Declare<TFSpyMaxCloakedSpeedConVar>(),
                                        new NoTFWeaponOwners(), new global::TF2.Shared.Econ.AttributeHooks(), v.Declare<TFClampBackSpeedConVar>(),
                                        v.Declare<TFClampBackSpeedMinConVar>(), new BlastJumpSims.RunningRound(), NoPlayers(), v.Declare<TFPlayerMovementRestartFreezeConVar>(),
                                        v.Declare<TFMovementAircurrentFrictionMultConVar>(), v.Declare<TFMovementLostFootingRestickConVar>(),
                                        v.Declare<TFMovementLostFootingFrictionConVar>(), v.Declare<TFMaxChargeSpeedConVar>(), new BlastJumpSims.NoHeldWeapons(), NoEntities(),
                                        v.Declare<TFParachuteMaxSpeedXYConVar>(), v.Declare<TFParachuteMaxSpeedZConVar>(),
                                        v.Declare<TFParachuteMaxSpeedOnfireZConVar>(), new BlastJumpSims.NoTeams(), v.Declare<TFClampAirducksConVar>(),
                                        v.Declare<TFParachuteAirControlConVar>(), v.Declare<TFDemomanChargeFrametimeScalingConVar>());
        Assert.Throws<InvalidOperationException>(() => rules.MaxSpeedScale(SoldierMover.PlayerIndex, 320f, false, 0));
    }

    private sealed class Players : ITFPlayerData
    {
        private readonly TFPlayerNetState _state = Soldier();

        private static TFPlayerNetState Soldier()
        {
            var state = new TFPlayerNetState();
            state.BindLocal();
            state.SetClass(TFClass.Soldier);
            return state;
        }

        public TFPlayerNetState? Predicted(int playerIndex) => _state;

        public TFPlayerNetState? Networked(int playerIndex) => _state;
    }

    [Fact]
    public void TheFactoryAsksTheContainerForNothingTheModsOwnRulesDoNot()
    {
        // The game builds TFMovementRules at boot, so whatever it is handed is registered; the factory may ask for no more.
        Type[] rules = [.. typeof(TFMovementRules).GetConstructors().Single().GetParameters().Select(p => p.ParameterType)];
        ParameterInfo[] asked = typeof(BlastJumpSims).GetConstructors().Single().GetParameters();
        Assert.True(asked.Length > 10, $"the control: the factory takes {asked.Length} services");
        Assert.Empty(asked.Where(p => !rules.Contains(p.ParameterType)).Select(p => p.ParameterType.Name));
    }
}
