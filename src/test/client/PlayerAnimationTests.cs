using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The standalone player's animation policy.
///
/// <para>
/// Every failure this class can have looks like a picture that is nearly
/// right: a walk cycle that never ends, a sidestep drawn as a forward walk
/// played sideways, feet skating over the floor, a crouched player standing
/// up. None of them is an error, a log line or a crash, and all of them are a
/// screenshot somebody has to look at very carefully. So the rules are pinned
/// here and the photograph is left for the one thing it is good at — whether a
/// model is there at all.
/// </para>
/// </summary>
public class PlayerAnimationTests
{
    private const float Cap = 320f;    // stock's sv_maxspeed
    private static Vector Still => Vector.Zero;
    private static Vector Forward(float speed) => new(speed, 0f, 0f);

    //-------------------------------------------------------------------------
    // GroundSpeed
    //-------------------------------------------------------------------------

    [Fact]
    public void FallingIsNotAGait()
    {
        // Two dimensions, not three: a player standing on a lift going down at
        // 200 units a second is standing. Measuring the whole vector puts them
        // in a run, which is stock's Length2D and this is why it is.
        var dropping = new Vector(0f, 0f, -200f);

        Assert.True(PlayerAnimation.GroundSpeed(dropping) == 0f,
            "a player moving only downwards has no ground speed");
    }

    [Fact]
    public void GroundSpeedMeasuresBothHorizontalAxes()
    {
        Assert.True(PlayerAnimation.GroundSpeed(new Vector(3f, 4f, 99f)) == 5f,
            "ground speed is the length of the horizontal part");
    }

    //-------------------------------------------------------------------------
    // Posture
    //-------------------------------------------------------------------------

    [Fact]
    public void AStandingPlayerIsIdle()
    {
        Assert.True(PlayerAnimation.Posture(Still, onGround: true, ducked: false, Cap)
                    == PlayerPosture.Idle,
            "a player who is not moving stands in an idle");
    }

    [Fact]
    public void FrictionResidueIsStillStanding()
    {
        // The reason the threshold is not zero. A player who has let go of the
        // key carries a few hundredths of a unit a second for several ticks,
        // and at a threshold of exactly zero they walk on the spot for all of
        // them.
        Assert.True(PlayerAnimation.Posture(Forward(0.4f), onGround: true, ducked: false, Cap)
                    == PlayerPosture.Idle,
            "a player drifting slower than stock's minimum is standing still");
    }

    [Fact]
    public void MovingSlowlyIsAWalk()
    {
        Assert.True(PlayerAnimation.Posture(Forward(100f), onGround: true, ducked: false, Cap)
                    == PlayerPosture.Walk,
            "under half the speed cap is a walk");
    }

    [Fact]
    public void MovingFastIsARun()
    {
        Assert.True(PlayerAnimation.Posture(Forward(300f), onGround: true, ducked: false, Cap)
                    == PlayerPosture.Run,
            "over half the speed cap is a run");
    }

    [Fact]
    public void TheRunThresholdIsAFractionOfTheirOwnCapAndNotASpeed()
    {
        // The whole reason RunFraction is a fraction. A player slowed to a
        // third of normal is running as hard as they can, and a fixed
        // threshold would put them in a walk cycle for the entire effect.
        var slowed = 100f;

        Assert.True(PlayerAnimation.Posture(Forward(90f), onGround: true, ducked: false, slowed)
                    == PlayerPosture.Run,
            "90 units a second is a run for a player capped at 100");

        Assert.True(PlayerAnimation.Posture(Forward(90f), onGround: true, ducked: false, Cap)
                    == PlayerPosture.Walk,
            "...and a walk for a player capped at 320");
    }

    [Fact]
    public void BeingInTheAirBeatsEverythingElse()
    {
        // The order of the tests IS the rule: a player who jumps while
        // sprinting is jumping. Checking speed first would draw a run in
        // mid-air, feet pedalling.
        Assert.True(PlayerAnimation.Posture(Forward(320f), onGround: false, ducked: false, Cap)
                    == PlayerPosture.Jump,
            "a sprinting player who leaves the ground is jumping, not sprinting");

        Assert.True(PlayerAnimation.Posture(Still, onGround: false, ducked: true, Cap)
                    == PlayerPosture.Jump,
            "so is a crouched one hanging motionless in the air");
    }

    [Fact]
    public void BeingStillBeatsBeingCrouched()
    {
        // A crouched player standing still has a crouched IDLE, not a crouched
        // walk played at rate zero — which is a frozen mid-stride pose.
        Assert.True(PlayerAnimation.Posture(Still, onGround: true, ducked: true, Cap)
                    == PlayerPosture.CrouchIdle,
            "a crouched player who is not moving is in a crouched idle");
    }

    [Fact]
    public void ACrouchedPlayerHasOneGaitWhateverTheirSpeed()
    {
        Assert.True(PlayerAnimation.Posture(Forward(50f), onGround: true, ducked: true, Cap)
                    == PlayerPosture.CrouchWalk,
            "a crouched player creeping is crouch-walking");

        Assert.True(PlayerAnimation.Posture(Forward(300f), onGround: true, ducked: true, Cap)
                    == PlayerPosture.CrouchWalk,
            "and a crouched player at full tilt is still crouch-walking");
    }

    [Fact]
    public void APlayerCarriedBySomethingWithNoSpeedCapOfTheirOwnWalks()
    {
        // A cap of zero is a player who cannot move themselves. They are
        // moving anyway, so something is carrying them — and the division that
        // decides walk from run would be by zero.
        Assert.True(PlayerAnimation.Posture(Forward(200f), onGround: true, ducked: false, 0f)
                    == PlayerPosture.Walk,
            "a player with no speed of their own being carried walks rather than dividing by zero");
    }

    //-------------------------------------------------------------------------
    // Rate
    //-------------------------------------------------------------------------

    [Fact]
    public void MatchingSpeedsPlayAtTheAnimatorsOwnRate()
    {
        Assert.True(PlayerAnimation.Rate(speed: 60f, sequenceGroundSpeed: 60f) == 1f,
            "a player moving exactly as fast as the sequence travels plays it at rate 1");
    }

    [Fact]
    public void MovingFasterThanTheSequenceSpeedsItUp()
    {
        // This is the whole point of asking the model for its ground speed:
        // without it the feet skate, which reads as a physics bug rather than
        // an animation one.
        Assert.True(PlayerAnimation.Rate(speed: 120f, sequenceGroundSpeed: 60f) == 2f,
            "twice the sequence's speed plays it twice as fast");

        Assert.True(PlayerAnimation.Rate(speed: 30f, sequenceGroundSpeed: 60f) == 0.5f,
            "half its speed plays it half as fast");
    }

    [Fact]
    public void ASequenceThatDoesNotTravelPlaysAtOne()
    {
        // Zero is the ORDINARY answer here, not an error: every idle in the
        // game has a root that goes nowhere, and so does the jump. Dividing by
        // it is the bug.
        Assert.True(PlayerAnimation.Rate(speed: 0f, sequenceGroundSpeed: 0f) == 1f,
            "a standing player in an idle plays it at rate 1");

        Assert.True(PlayerAnimation.Rate(speed: 300f, sequenceGroundSpeed: 0f) == 1f,
            "and so does a player thrown across the room in one");
    }

    [Fact]
    public void TheRateIsClampedToStocksRange()
    {
        Assert.True(PlayerAnimation.Rate(speed: 100000f, sequenceGroundSpeed: 1f)
                    == PlayerAnimation.MaxRate,
            "a player launched by an explosion does not play their walk at 100000x");

        Assert.True(PlayerAnimation.Rate(speed: 0f, sequenceGroundSpeed: 60f)
                    == PlayerAnimation.MinRate,
            "and a stopped one in a travelling sequence floors rather than freezing at zero");
    }

    //-------------------------------------------------------------------------
    // MoveYaw — the pose parameter that makes a sidestep a sidestep.
    //-------------------------------------------------------------------------

    [Fact]
    public void WalkingForwardsIsZero()
    {
        // Body facing +X (yaw 0), travelling +X.
        Assert.True(PlayerAnimation.MoveYaw(Forward(200f), bodyYaw: 0f) == 0f,
            "a player travelling the way they face has a move yaw of zero");
    }

    [Fact]
    public void WalkingBackwardsIsHalfATurn()
    {
        // Body facing +X, travelling -X. The blend has to reach the backpedal
        // and not the forward walk, which is what this number selects.
        Assert.True(PlayerAnimation.MoveYaw(new Vector(-200f, 0f, 0f), bodyYaw: 0f) == 180f,
            "a player backing up has a move yaw of 180");
    }

    [Fact]
    public void StrafingIsAQuarterTurnAndTheSIGNSAYSWHICHWAY()
    {
        // The sign is the half a reader gets wrong and no screenshot argues
        // about quietly: the wrong sign draws a right sidestep for a left one,
        // which is a model crossing its legs the wrong way while moving
        // correctly.
        Assert.True(PlayerAnimation.MoveYaw(new Vector(0f, 200f, 0f), bodyYaw: 0f) == 90f,
            "travelling +Y while facing +X is a move yaw of +90");

        Assert.True(PlayerAnimation.MoveYaw(new Vector(0f, -200f, 0f), bodyYaw: 0f) == -90f,
            "and travelling -Y is -90");
    }

    [Fact]
    public void MoveYawIsRelativeToTheBodyAndNotToTheWorld()
    {
        // The whole feature. A player travelling due north is walking forwards
        // if they face north and sidestepping if they face east, and the pose
        // has to tell those apart or every strafe is drawn as a walk.
        var north = new Vector(0f, 200f, 0f);

        Assert.True(PlayerAnimation.MoveYaw(north, bodyYaw: 90f) == 0f,
            "travelling the way the body faces is forwards, whatever direction that is");

        Assert.True(PlayerAnimation.MoveYaw(north, bodyYaw: 0f) == 90f,
            "and the same travel with the body turned is a sidestep");
    }

    [Fact]
    public void MoveYawFoldsRatherThanRunningOff()
    {
        // A body at 170 travelling at -170 is 20 degrees apart, not 340.
        var travelling = new Vector(MathF.Cos(-170f * MathF.PI / 180f) * 200f,
                                    MathF.Sin(-170f * MathF.PI / 180f) * 200f, 0f);

        float yaw = PlayerAnimation.MoveYaw(travelling, bodyYaw: 170f);

        Assert.True(MathF.Abs(yaw - 20f) < 0.01f,
            $"a body at 170 travelling at -170 is 20 degrees apart, not 340 (got {yaw})");
    }

    [Fact]
    public void AStandingPlayerHasNoDirectionOfTravel()
    {
        // Stock freezes the last one, which is invisible for stock because its
        // idles do not read move_yaw. Held here it would be a stale strafe
        // leaking into the next step.
        Assert.True(PlayerAnimation.MoveYaw(Still, bodyYaw: 137f) == 0f,
            "a player who is not travelling has a move yaw of zero, not a stale one");
    }

    //-------------------------------------------------------------------------
    // Fold
    //-------------------------------------------------------------------------

    [Fact]
    public void FoldBringsAnyAngleIntoOneTurn()
    {
        Assert.True(PlayerAnimation.Fold(0f) == 0f, "zero is already in range");
        Assert.True(PlayerAnimation.Fold(180f) == 180f, "180 is in range at the top");
        Assert.True(PlayerAnimation.Fold(181f) == -179f, "181 comes round to -179");
        Assert.True(PlayerAnimation.Fold(-180f) == 180f, "-180 folds up to 180");
        Assert.True(PlayerAnimation.Fold(720f + 45f) == 45f, "two whole turns and a bit is a bit");
        Assert.True(PlayerAnimation.Fold(-720f - 45f) == -45f, "and the same the other way");
    }

    [Fact]
    public void FoldDoesNotSpinForeverOnAnAngleThatIsNotANumber()
    {
        // A pose parameter is exactly the sort of value that arrives as a NaN
        // while a model is mid-load, and a `while` loop on one never returns:
        // the game stops with no error and no log line, which is the worst
        // possible symptom.
        Assert.True(PlayerAnimation.Fold(float.NaN) == 0f, "a NaN folds to zero rather than hanging");
        Assert.True(PlayerAnimation.Fold(float.PositiveInfinity) == 0f, "so does an infinity");
        Assert.True(PlayerAnimation.Fold(float.NegativeInfinity) == 0f, "in both directions");
    }

    //-------------------------------------------------------------------------
    // Activities and restarting
    //-------------------------------------------------------------------------

    [Fact]
    public void EveryPostureEndsItsListWithAnIdleItCanFallBackTo()
    {
        // A model with no crouch animation must still animate. Standing in an
        // idle is wrong; standing in the reference pose — arms out, T-posed —
        // is broken, and is exactly what an unanimated player looks like.
        foreach (PlayerPosture posture in Enum.GetValues<PlayerPosture>())
        {
            string[] activities = PlayerAnimation.Activities(posture);

            Assert.True(activities.Length > 0, $"{posture} names at least one activity");
            Assert.True(activities[^1] == "ACT_IDLE",
                $"{posture} falls back to ACT_IDLE rather than to the reference pose");
        }
    }

    [Fact]
    public void TheDEATHMATCHActivitiesComeFirstBecauseThisBuildOnlyHasThose()
    {
        // Measured, not assumed. This installation is Source SDK Base 2013
        // MULTIPLAYER, whose content mount overrides HL2's male_shared.mdl
        // with HL2DM's — so the citizen's 106 merged sequences are the
        // deathmatch set and NOT ONE of them is ACT_IDLE, ACT_WALK or
        // ACT_RUN. cs_cl_sequences prints the table.
        //
        // A policy naming only the single-player activities resolves nothing
        // and leaves the model in its reference pose, arms out, which looks
        // exactly like an animation system nobody wrote.
        Assert.True(PlayerAnimation.Activities(PlayerPosture.Idle)[0] == "ACT_HL2MP_IDLE_MELEE",
            "an idle asks for the deathmatch idle first");
        Assert.True(PlayerAnimation.Activities(PlayerPosture.Run)[0] == "ACT_HL2MP_RUN_MELEE",
            "a run asks for the deathmatch run");
        Assert.True(PlayerAnimation.Activities(PlayerPosture.CrouchWalk)[0]
                    == "ACT_HL2MP_WALK_CROUCH_MELEE",
            "a crouch walk asks for the deathmatch crouch walk");
        Assert.True(PlayerAnimation.Activities(PlayerPosture.Jump)[0] == "ACT_HL2MP_JUMP_MELEE",
            "a jump asks for the deathmatch jump");
    }

    [Fact]
    public void TheSINGLEPLAYERActivitiesAreStillThereBelowThem()
    {
        // A model carrying the HL2 citizen set — or an installation that
        // mounts it — must animate from the same policy with no change. This
        // is what makes the list a list rather than a rename.
        Assert.Contains("ACT_WALK", PlayerAnimation.Activities(PlayerPosture.Walk));
        Assert.Contains("ACT_RUN", PlayerAnimation.Activities(PlayerPosture.Run));
        Assert.Contains("ACT_WALK_CROUCH", PlayerAnimation.Activities(PlayerPosture.CrouchWalk));
        Assert.Contains("ACT_JUMP", PlayerAnimation.Activities(PlayerPosture.Jump));
    }

    [Fact]
    public void HL2DMHasNoWalkSoAWalkAndARunAskForTheSameSequence()
    {
        // Its gaits are run_* and cwalk_* and nothing between. A walking
        // player gets the run sequence slowed down by Rate, which is what the
        // deathmatch player does — and is why the rate is measured against the
        // sequence's own ground speed rather than assumed.
        Assert.True(PlayerAnimation.Activities(PlayerPosture.Walk)[0]
                    == PlayerAnimation.Activities(PlayerPosture.Run)[0],
            "the deathmatch set has one standing gait, played at two speeds");
    }

    [Fact]
    public void ACrouchedIdleAcceptsEveryNameModelsUseForIt()
    {
        // Three spellings, all in use: HL2DM tags it ACT_HL2MP_IDLE_CROUCH_*,
        // the HL2 citizens tag it ACT_COVER_LOW, and other models use
        // ACT_CROUCHIDLE. None is wrong, and a single name leaves two thirds
        // of the models T-posed while crouched.
        string[] activities = PlayerAnimation.Activities(PlayerPosture.CrouchIdle);

        Assert.Contains("ACT_HL2MP_IDLE_CROUCH_MELEE", activities);
        Assert.Contains("ACT_CROUCHIDLE", activities);
        Assert.Contains("ACT_COVER_LOW", activities);
    }

    [Fact]
    public void OnlyTheJumpRestarts()
    {
        // A gait that restarted whenever the posture was re-decided would take
        // one step forever — the policy runs on every network update, so that
        // is twenty restarts a second.
        Assert.False(PlayerAnimation.Restarts(PlayerPosture.Walk), "a walk resumes its cycle");
        Assert.False(PlayerAnimation.Restarts(PlayerPosture.Run), "so does a run");
        Assert.False(PlayerAnimation.Restarts(PlayerPosture.Idle), "and an idle");

        // A jump is a one-shot: a second jump must play it again rather than
        // resume a cycle that has already run out.
        Assert.True(PlayerAnimation.Restarts(PlayerPosture.Jump), "a jump starts from its beginning");
    }

    //-------------------------------------------------------------------------
    // Choose — the three answers together.
    //-------------------------------------------------------------------------

    [Fact]
    public void ChooseAnswersThePostureAndTheStrafeTogether()
    {
        var pose = PlayerAnimation.Choose(new Vector(0f, 200f, 0f), bodyYaw: 0f,
                                          onGround: true, ducked: false, Cap);

        Assert.True(pose.Posture == PlayerPosture.Run, "200 of 320 units a second is over half, so a run");
        Assert.True(pose.MoveYaw == 90f, "travelling +Y while facing +X is a left sidestep");
        Assert.True(pose.PlaybackRate == 1f,
            "the rate is left at 1 until the caller has asked the model what the sequence travels at");
    }
}
