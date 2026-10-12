/*
 * The standalone player's animator — the half that needs a model.
 *
 * PlayerAnimationTests pins the RULE; this pins what the rule is used for:
 * which activity a posture is asked for, that a model answering to none of
 * them is left alone rather than forced to sequence 0, that a gait is not
 * restarted twenty times a second while a one-shot is restarted once, and that
 * the strafe pose parameter is looked up once and not per update.
 *
 * Every one of those failures draws a picture that is nearly right, so none of
 * them would be reported by anything.
 */

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class PlayerAnimatorTests
{
    private const float Cap = 320f;

    /// <summary>
    /// A player wearing a model that answers to everything, standing still.
    ///
    /// <para>
    /// The engine reads are stubbed rather than ignored because each of them is
    /// something the animator genuinely depends on and a reader should be able
    /// to challenge: the model's activities, its ground speed per sequence, its
    /// pose parameters, and which way the body faces.
    /// </para>
    /// </summary>
    private static ClientSsPlayer Player(RecordingEngine engine, uint flags = 1u)
    {
        var player = new ClientSsPlayer(engine)
        {
            Handle = new EntityHandle(0x0100_0001u),
        };

        player.BindEngine(engine, engine);

        player.State.BindLocal();
        player.State.Velocity = Vector.Zero;
        player.State.MaxSpeed = Cap;
        player.State.Ducked = false;
        player.State.Flags = flags;

        return player;
    }

    private static RecordingEngine Engine(int sequence = 7, float groundSpeed = 0f,
                                          int moveYaw = 2, float bodyYaw = 0f)
    {
        var engine = Fake();

        engine.Returns("EntityRender.LookupSequenceByActivity", sequence);
        engine.Returns("EntityRender.SequenceGroundSpeed", groundSpeed);
        engine.Returns("EntityRender.LookupPoseParameter", moveYaw);
        engine.Returns("EntityRender.SetPoseParameter", 0f);
        engine.Returns("Entities.GetAngles", new Vector(0f, bodyYaw, 0f));

        return engine;
    }

    private static void Update(ClientSsPlayer player,
                               DataUpdateType type = DataUpdateType.DataTableChanged)
        => player.OnDataChanged(ReadOnlySpan<byte>.Empty, type);

    //-------------------------------------------------------------------------
    // The resolve.
    //-------------------------------------------------------------------------

    [Fact]
    public void AStandingPlayerIsPutIntoAnIdle()
    {
        var engine = Engine();
        var player = Player(engine);

        Update(player, DataUpdateType.Created);

        var asked = engine.Last("EntityRender.LookupSequenceByActivity");

        Assert.True(asked is not null, "the animator asked the model for an activity");
        Assert.True(asked!.Value.ArgAs<string>(1) == "ACT_HL2MP_IDLE_MELEE",
            "a player standing still is put into the idle this build's content actually has");
        Assert.True(player.ShownPosture == PlayerPosture.Idle, "and knows that is what it chose");
    }

    [Fact]
    public void AMovingPlayerIsPutIntoAGait()
    {
        var engine = Engine();
        var player = Player(engine);
        player.State.Velocity = new Vector(100f, 0f, 0f);

        Update(player, DataUpdateType.Created);

        Assert.True(engine.Last("EntityRender.LookupSequenceByActivity")!.Value.ArgAs<string>(1)
                    == "ACT_HL2MP_RUN_MELEE",
            "a player moving is put into a gait — HL2DM's only standing one, slowed by the rate");
    }

    [Fact]
    public void TheFirstActivityTheModelAnswersToIsTheOneUsed()
    {
        // A crouched idle is ACT_COVER_LOW on the citizens and ACT_CROUCHIDLE
        // elsewhere. A model that has only the second must still animate, and
        // this is the walk down the list that makes it.
        var engine = Fake();
        engine.Returns("EntityRender.LookupSequenceByActivity", -1);
        engine.Returns("Entities.GetAngles", Vector.Zero);

        var player = Player(engine);
        player.State.Ducked = true;

        int resolved = player.Resolve(PlayerPosture.CrouchIdle);

        Assert.True(resolved == -1, "a model that answers to none of them resolves to nothing");

        var asked = engine.AllOf("EntityRender.LookupSequenceByActivity")
                          .Select(c => c.ArgAs<string>(1)).ToList();

        Assert.True(asked.Count == PlayerAnimation.Activities(PlayerPosture.CrouchIdle).Length,
            $"every name in the crouched idle's list was tried, not just the first ({asked.Count})");
        Assert.True(asked[^1] == "ACT_IDLE",
            "and the last thing tried is the idle everything falls back to");
    }

    [Fact]
    public void AModelWithNoAnimationAtAllIsLeftAlone()
    {
        // -1 rather than sequence 0. Sequence 0 is whatever happens to be
        // first in the file — on a prop that is the only pose it has, on a
        // player model it could be a death animation.
        var engine = Fake();
        engine.Returns("EntityRender.LookupSequenceByActivity", -1);
        engine.Returns("Entities.GetAngles", Vector.Zero);

        var player = Player(engine);

        Update(player, DataUpdateType.Created);

        Assert.True(engine.CountOf("EntityRender.PlaySequence") == 0,
            "a model that answers to no activity is not forced into sequence 0");
        Assert.True(engine.CountOf("EntityRender.SetPoseParameter") == 0,
            "and is not posed either");
    }

    //-------------------------------------------------------------------------
    // The hand-over.
    //-------------------------------------------------------------------------

    [Fact]
    public void AGaitIsNeverRestartedWhileItIsRunning()
    {
        // This runs on every networked update — twenty a second or more. A
        // restart on each would leave the player taking one step forever, which
        // is a picture that looks like an animation bug and is a policy one.
        var engine = Engine();
        var player = Player(engine);
        player.State.Velocity = new Vector(100f, 0f, 0f);

        Update(player, DataUpdateType.Created);
        Update(player);
        Update(player);

        Assert.True(engine.CountOf("EntityRender.PlaySequence") == 3,
            "the rate is handed over on every update");

        foreach (var call in engine.AllOf("EntityRender.PlaySequence"))
        {
            Assert.False(call.ArgAs<bool>(3),
                "and never as a restart — the shell keeps the running clock when only the rate moves");
        }
    }

    [Fact]
    public void AJumpRestartsWhenItIsENTEREDAndNotAfterwards()
    {
        var engine = Engine();
        var player = Player(engine, flags: 0u);      // FL_ONGROUND clear: airborne
        player.State.Velocity = new Vector(100f, 0f, 40f);

        Update(player, DataUpdateType.Created);
        Update(player);
        Update(player);

        var calls = engine.AllOf("EntityRender.PlaySequence").ToList();

        Assert.True(calls.Count == 3, "the jump was handed over on each update");
        Assert.True(calls[0].ArgAs<bool>(3), "the first, which ENTERS the jump, restarts it");
        Assert.False(calls[1].ArgAs<bool>(3),
            "the second does not — a one-shot re-sent every packet never gets past its first frame");
        Assert.False(calls[2].ArgAs<bool>(3), "nor the third");
    }

    [Fact]
    public void LandingLeavesTheJumpAndTakingOffEntersItAgain()
    {
        var engine = Engine();
        var player = Player(engine, flags: 0u);
        player.State.Velocity = new Vector(100f, 0f, 0f);

        Update(player, DataUpdateType.Created);       // in the air: restart

        player.State.Flags = 1u;                      // landed
        Update(player);

        player.State.Flags = 0u;                      // jumped again
        Update(player);

        var calls = engine.AllOf("EntityRender.PlaySequence").ToList();

        Assert.True(calls[0].ArgAs<bool>(3), "the first jump restarts");
        Assert.False(calls[1].ArgAs<bool>(3), "landing into a gait does not");
        Assert.True(calls[2].ArgAs<bool>(3),
            "and a SECOND jump restarts again rather than resuming a cycle that ran out");
    }

    [Fact]
    public void TheRateComesFromTheModelAndNotFromAConstant()
    {
        // The whole reason StandaloneSequenceGroundSpeed is an ABI slot. A
        // reference speed written down in C# is true of one model and of no
        // other, and its only symptom is feet skating over the floor.
        var engine = Engine(groundSpeed: 50f);
        var player = Player(engine);
        player.State.Velocity = new Vector(100f, 0f, 0f);

        Update(player, DataUpdateType.Created);

        Assert.True(engine.Called("EntityRender.SequenceGroundSpeed"),
            "the animator asked the model what the sequence travels at");

        var played = engine.Last("EntityRender.PlaySequence");

        Assert.True(played!.Value.ArgAs<float>(2) == 2f,
            "a player at 100 in a sequence built for 50 plays it at double rate");
    }

    [Fact]
    public void AnIdleThatTravelsNowhereStillPlaysAtRateOne()
    {
        var engine = Engine(groundSpeed: 0f);
        var player = Player(engine);

        Update(player, DataUpdateType.Created);

        Assert.True(engine.Last("EntityRender.PlaySequence")!.Value.ArgAs<float>(2) == 1f,
            "a sequence with no ground speed is played at rate 1, not divided by zero");
    }

    //-------------------------------------------------------------------------
    // The strafe pose.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheStrafeDirectionIsPosedAgainstTheBodysOwnFacing()
    {
        // Body facing east (yaw 0), travelling north (+Y): a left sidestep.
        var engine = Engine(bodyYaw: 0f);
        var player = Player(engine);
        player.State.Velocity = new Vector(0f, 200f, 0f);

        Update(player, DataUpdateType.Created);

        var posed = engine.Last("EntityRender.SetPoseParameter");

        Assert.True(posed is not null, "the strafe direction was posed");
        Assert.True(posed!.Value.ArgAs<int>(1) == 2, "into the index the model gave for move_yaw");
        Assert.True(posed.Value.ArgAs<float>(2) == 90f, "as a left sidestep");
    }

    [Fact]
    public void TurningTheBodyChangesTheStrafeWithoutChangingTheTravel()
    {
        // The same travel with the body turned to face it is a forward walk.
        var engine = Engine(bodyYaw: 90f);
        var player = Player(engine);
        player.State.Velocity = new Vector(0f, 200f, 0f);

        Update(player, DataUpdateType.Created);

        Assert.True(engine.Last("EntityRender.SetPoseParameter")!.Value.ArgAs<float>(2) == 0f,
            "a player facing the way they travel is walking forwards, not sidestepping");
    }

    [Fact]
    public void ThePoseParameterIsLookedUpOnceRatherThanEveryUpdate()
    {
        // A pose parameter index is a fact about the model. This runs per
        // player per update; asking twenty times a second forever is a studio
        // header walk per player per packet.
        var engine = Engine();
        var player = Player(engine);
        player.State.Velocity = new Vector(100f, 0f, 0f);

        Update(player, DataUpdateType.Created);
        Update(player);
        Update(player);

        Assert.True(engine.CountOf("EntityRender.LookupPoseParameter") == 1,
            "move_yaw is resolved once and remembered");
        Assert.True(engine.CountOf("EntityRender.SetPoseParameter") == 3,
            "and written on every update, because the direction of travel moves");
    }

    [Fact]
    public void AModelWithNoStrafeBlendIsNotPosedAndIsNotReAsked()
    {
        // -1 is a legal answer and is remembered as readily as an index: a
        // model with no move_yaw animates perfectly well without one, and
        // re-asking forever would be a studio walk per packet for nothing.
        var engine = Engine(moveYaw: -1);
        var player = Player(engine);
        player.State.Velocity = new Vector(100f, 0f, 0f);

        Update(player, DataUpdateType.Created);
        Update(player);

        Assert.True(engine.CountOf("EntityRender.LookupPoseParameter") == 1,
            "the absent pose parameter is asked for once");
        Assert.True(engine.CountOf("EntityRender.SetPoseParameter") == 0,
            "and never written");
        Assert.True(engine.CountOf("EntityRender.PlaySequence") == 2,
            "while the gait itself is unaffected");
    }

    //-------------------------------------------------------------------------
    // The switch, which exists because cs_playseq lies without it.
    //-------------------------------------------------------------------------

    [Fact]
    public void TurningThePolicyOffLeavesTheBodyAlone()
    {
        // With it on, a pose set by hand is overwritten within a frame: the
        // animator writes on every networked update, which on a listen server
        // is most of them. cs_playseq then reports success and nothing
        // happens, which is the exact shape of failure this tree keeps
        // finding.
        var engine = Engine();
        var player = Player(engine);

        Update(player, DataUpdateType.Created);
        engine.Clear();

        player.Enabled = false;
        Update(player);
        Update(player);

        Assert.True(engine.CountOf("EntityRender.PlaySequence") == 0,
            "a disabled policy hands over no sequence");
        Assert.True(engine.CountOf("EntityRender.SetPoseParameter") == 0,
            "and writes no pose parameter");
    }

    [Fact]
    public void TurningItBackOnResumesWithoutBeingAsked()
    {
        var engine = Engine();
        var player = Player(engine);

        player.Enabled = false;
        Update(player, DataUpdateType.Created);

        player.Enabled = true;
        Update(player);

        Assert.True(engine.CountOf("EntityRender.PlaySequence") == 1,
            "the policy picks a gait again on the next update, unprompted");
    }

    [Fact]
    public void TheSwitchIsPerPlayerAndNotProcessWide()
    {
        // docs/writing-tests.md's "no globals", and not a hypothetical: three
        // tests in this tree passed only because another suite had left a
        // static filled in. A viewer freezing one player's pose must not
        // freeze everybody's.
        var engine = Engine();
        var one = Player(engine);
        var two = Player(engine);

        one.Enabled = false;

        Assert.True(two.Enabled, "disabling one player's animation leaves the other's alone");
    }

    [Fact]
    public void TheCommandWithNoArgumentReportsAndChangesNothing()
    {
        // "cs_cl_player_anim" alone is how somebody asks WHICH it is. A bare
        // command that silently turned it on would answer a question by
        // changing the answer.
        var engine = Engine();
        var player = Player(engine);
        player.Enabled = false;

        var said = PlayerAnimCommand.Apply(new ClientStandaloneEntity[] { player }, "");

        Assert.False(player.Enabled, "asking did not switch it on");
        Assert.True(said.Count == 1 && said[0].Contains("OFF"), "and it said which it is");
    }

    [Fact]
    public void TheCommandSetsEveryPlayerOnThisClient()
    {
        var engine = Engine();
        var one = Player(engine);
        var two = Player(engine);

        PlayerAnimCommand.Apply(new ClientStandaloneEntity[] { one, two }, "0");

        Assert.False(one.Enabled, "the first player stopped animating");
        Assert.False(two.Enabled, "and so did the second — that is what a person typing it means");

        PlayerAnimCommand.Apply(new ClientStandaloneEntity[] { one, two }, "1");

        Assert.True(one.Enabled, "and both come back");
        Assert.True(two.Enabled);
    }

    [Fact]
    public void TheCommandSaysSoWhenThereIsNobodyToAnimate()
    {
        // Between maps and before the player entity arrives, which every
        // caller of GetLocal has always had to survive. A command that
        // silently did nothing would read as a command that did not work.
        var said = PlayerAnimCommand.Apply(Array.Empty<ClientStandaloneEntity>(), "0");

        Assert.True(said.Count == 1 && said[0].Contains("no player"),
            "with no player it says so rather than printing nothing");
    }

    [Fact]
    public void AModelArrivingLateHasItsPoseParameterResolvedAgain()
    {
        // An entity can exist before its model does, and a lookup against no
        // model answers -1 forever after. A CREATED update is the moment to
        // ask again.
        var engine = Engine();
        var player = Player(engine);

        Update(player, DataUpdateType.Created);
        Update(player);
        Update(player, DataUpdateType.Created);

        Assert.True(engine.CountOf("EntityRender.LookupPoseParameter") == 2,
            "a fresh CREATED update re-resolves the pose parameter against the model now worn");
    }
}
