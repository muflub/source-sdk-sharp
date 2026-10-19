/*
 * The client's animation host — the machine wired to a real entity.
 *
 * Hl2PosturePolicyTests pins the RULE and AnimStateMachineTests the MACHINE;
 * this pins what the client does with them: which activity a posture is
 * asked for, that a model answering to none of them is left alone rather
 * than forced to sequence 0, that stock's restart edge (the ACTIVITY change,
 * stock's ResetSequence — entering a gait AND landing into one) is what the
 * hand-over follows, that the pose dials are looked up once and written
 * every drive, and that the eyes come from the viewer's own angles for the
 * local player and from the body for everyone else.
 *
 * Every one of those failures draws a picture that is nearly right, so none
 * of them would be reported by anything.
 *
 * Phase 0.10: these facts used to hold the client's own PlayerAnimation
 * statics through a hand-written animator. The animator is now one call into
 * the shared machine, so the drives are the same drives and the assertions
 * follow the machine's (stock's) answers — including where stock differs from
 * the old client policy (the restart edge, the eyes): the divergence is
 * named in the facts that watch it.
 */

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared.Anim;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class PlayerAnimatorTests
{
    private const float Cap = 320f;
    private const uint HandleRaw = 0x0100_0001u;

    /// <summary>
    /// A player wearing a model that answers to everything, standing still.
    ///
    /// <para>
    /// The engine reads are stubbed rather than ignored because each of them is
    /// something the animator genuinely depends on and a reader should be able
    /// to challenge: the model's activities, its ground speed per sequence, its
    /// pose parameters, which way the body faces — and the side's clock, whose
    /// Now the machine's drives advance, and the local-player index the eyes
    /// are decided against.
    /// </para>
    /// </summary>
    private static ClientSsPlayer Player(RecordingEngine engine, uint flags = 1u,
                                         ViewReadout? view = null, AnimStateLayers? layers = null)
    {
        var player = new ClientSsPlayer(engine, clock: engine, view: view, screen: engine, layers: layers)
        {
            Handle = new EntityHandle(HandleRaw),
        };

        player.BindEngine(engine, engine);
        player.BindIndex((int)(HandleRaw & 0x7FF));   // index 1

        player.State.BindLocal();
        player.State.Velocity = Vector.Zero;
        player.State.MaxSpeed = Cap;
        player.State.Ducked = false;
        player.State.Flags = flags;
        player.State.Alive = true;                    // stock's Update gate

        return player;
    }

    /// <summary>
    /// The dials a stock HL2 model carries, in the order the machine asks
    /// them (SetupPoseParameters: move_x, move_y, body_pitch, body_yaw,
    /// move_yaw, move_scale). The answer-per-call dial so the fake can hand
    /// six DIFFERENT indexes and a re-ask after OnNewModel answers the same.
    /// </summary>
    private static void Dials(RecordingEngine engine, params int[] indexes)
    {
        int asked = 0;
        engine.ReturnsEachCall("EntityRender.LookupPoseParameter",
            () => indexes[asked++ % indexes.Length]);
    }

    /// <summary>The 9-way model: every dial named (the blend the machine's
    /// nine-way arm drives). The default under Engine().</summary>
    private static void NineWayDials(RecordingEngine engine) => Dials(engine, 0, 1, 3, 4, 2, 5);

    /// <summary>The 8-way model: only move_yaw exists, so the machine poses
    /// the polar blend on index 2 and the pose writes are legible.</summary>
    private static void EightWayDials(RecordingEngine engine) => Dials(engine, -1, -1, -1, -1, 2, -1);

    /// <summary>The stub's answers as a NAME -> sequence table, so a fact can
    /// make the jump and the gait different sequences (the restart edge reads
    /// activities, and one answer for every name hides it). The ask is read
    /// back from the call the fake has already recorded.</summary>
    private static void Activities(RecordingEngine engine, params (string Name, int Sequence)[] map)
    {
        engine.ReturnsEachCall("EntityRender.LookupSequenceByActivity", () =>
        {
            for (int i = engine.Calls.Count - 1; i >= 0; i--)
            {
                if (engine.Calls[i].Member == "EntityRender.LookupSequenceByActivity")
                {
                    string asked = engine.Calls[i].ArgAs<string>(1);
                    foreach (var (name, sequence) in map)
                        if (name == asked)
                            return sequence;
                    return -1;        // a stock ACT_MP_ name: nobody has it; the HL2 layer answers after
                }
            }
            return -1;
        });
    }

    private static RecordingEngine Engine(int sequence = 7, float groundSpeed = 0f,
                                          int moveYaw = 2, float bodyYaw = 0f)
    {
        var engine = Fake();

        engine.Returns("EntityRender.LookupSequenceByActivity", sequence);
        engine.Returns("EntityRender.SequenceGroundSpeed", groundSpeed);
        engine.Returns("EntityRender.LookupPoseParameter", moveYaw);
        engine.Returns("EntityRender.SetPoseParameter", 0f);
        engine.Returns("EntityRender.GetPoseParameter", 0f);
        engine.Returns("EntityRender.IsDormant", false);
        engine.Returns("EntityRender.GetModelName", "models/humans/group03/male_01.mdl");
        engine.Returns("Entities.GetAngles", new Vector(0f, bodyYaw, 0f));
        engine.Returns("Hud.LocalPlayerIndex", 0);    // remote: the eyes ARE the body

        // The machine's clock: a real elapsed time between drives (stock's
        // EstimateYaw ignores a zero frame time, so the first drive only
        // resolves the dials and the second poses from them).
        double t = 5.0;
        engine.ReturnsEachCall("Clock.Now", () => (float)(t += 0.05));

        // The six stock dials, so the pose-parameter facts can name which
        // one was written.
        NineWayDials(engine);

        return engine;
    }

    private static void Update(ClientSsPlayer player,
                               DataUpdateType type = DataUpdateType.DataTableChanged)
        => player.OnDataChanged(ReadOnlySpan<byte>.Empty, type);

    /// <summary>Every value ever written to pose-parameter index 2 (move_yaw).</summary>
    private static List<float> MoveYaws(RecordingEngine engine)
        => engine.AllOf("EntityRender.SetPoseParameter")
                 .Where(c => c.ArgAs<int>(1) == 2)
                 .Select(c => c.ArgAs<float>(2)).ToList();

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
        // The LAST ask of the drive is the HL2 layer's — the machine first
        // asks for the stock ACT_MP name and the chain layer replaces the
        // answer (that two-step is the machine's own facts).
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

        var player = new ClientSsPlayer(engine) { Handle = new EntityHandle(HandleRaw) };
        player.BindEngine(engine, engine);
        player.State.BindLocal();
        player.State.MaxSpeed = Cap;
        player.State.Ducked = true;

        int resolved = player.Resolve(PlayerPosture.CrouchIdle);

        Assert.True(resolved == -1, "a model that answers to none of them resolves to nothing");

        var asked = engine.AllOf("EntityRender.LookupSequenceByActivity")
                          .Select(c => c.ArgAs<string>(1)).ToList();

        Assert.True(asked.Count == Hl2PostureLayer.Activities(PlayerPosture.CrouchIdle).Length,
            $"every name in the crouched idle's list was tried, not just the first ({asked.Count})");
        Assert.True(asked[^1] == "ACT_IDLE",
            "and the last thing tried is the idle everything falls back to");
    }

    [Fact]
    public void AModelWithNoAnimationAtAllIsLeftAlone()
    {
        // -1 rather than sequence 0. Sequence 0 is whatever happens to be
        // first in the file — on a prop that is the only pose it has, on a
        // player model it could be a death animation. Stock still POSES the
        // blend to zero with nothing to play (the pose half of Update runs
        // after the sequence half, unconditionally), so what is pinned is the
        // hand-over and the absence of the strafe, not every pose write.
        var engine = Engine();
        engine.Returns("EntityRender.LookupSequenceByActivity", -1);

        var player = Player(engine);

        Update(player, DataUpdateType.Created);

        Assert.True(engine.CountOf("EntityRender.PlaySequence") == 0,
            "a model that answers to no activity is not forced into sequence 0");
        Assert.True(MoveYaws(engine).Count == 0,
            "and its strafe is never written");
    }

    //-------------------------------------------------------------------------
    // The hand-over.
    //-------------------------------------------------------------------------

    [Fact]
    public void AGaitIsHandedOverEveryDriveAndRestartedOnlyOnTheEnter()
    {
        // This runs on every networked update — twenty a second or more. A
        // restart on each would leave the player taking one step forever,
        // which is a picture that looks like an animation bug and is a policy
        // one. Entering the gait restarts once (stock's ResetSequence at the
        // activity change); while it runs, only the rate moves.
        var engine = Engine();
        var player = Player(engine);
        player.State.Velocity = new Vector(100f, 0f, 0f);

        Update(player, DataUpdateType.Created);
        Update(player);
        Update(player);

        var calls = engine.AllOf("EntityRender.PlaySequence").ToList();

        Assert.True(calls.Count == 3, "the rate is handed over on every update");
        Assert.True(calls[0].ArgAs<bool>(3), "the first, which ENTERS the gait, restarts it");
        Assert.False(calls[1].ArgAs<bool>(3),
            "the rest do not — the shell keeps the running clock when only the rate moves");
        Assert.False(calls[2].ArgAs<bool>(3), "and so on, every drive");
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
    public void LandingIntoAGaitRestartsWithStocksEdgeAndNotTheOldPolicies()
    {
        // DIVERGENCE, pinned on both sides: the old client animator restarted
        // ONLY a posture change INTO the jump; stock's ComputeMainSequence
        // (:1180,1206) hands a new gait over with ResetSequence — a restart —
        // whenever the ACTIVITY changes, landing out of a jump included. The
        // machine is stock's, so the landing restarts. (The same edge on the
        // machine alone is AnimStateMachineTests'
        // JumpThenLandRunsTheJumpSequenceThenTheGaitWithTheOneRestart.)
        var engine = Engine();
        Activities(engine, ("ACT_HL2MP_RUN_MELEE", 7), ("ACT_HL2MP_JUMP_MELEE", 9));

        var player = Player(engine, flags: 0u);
        player.State.Velocity = new Vector(100f, 0f, 0f);

        Update(player, DataUpdateType.Created);       // in the air: restart

        player.State.Flags = 1u;                      // landed
        Update(player);

        player.State.Flags = 0u;                      // jumped again
        Update(player);

        var calls = engine.AllOf("EntityRender.PlaySequence").ToList();

        Assert.True(calls[0].ArgAs<bool>(3), "the first jump restarts");
        Assert.True(calls[1].ArgAs<bool>(3),
            "landing into a gait restarts it — stock's activity edge, the old policy's divergence");
        Assert.True(calls[2].ArgAs<bool>(3),
            "and a SECOND jump restarts again rather than resuming a cycle that ran out");
    }

    [Fact]
    public void TheRateComesFromTheModelAndNotFromAConstant()
    {
        // The whole reason StandaloneSequenceGroundSpeed is an ABI slot. A
        // reference speed written down in C# is true of one model and of no
        // other, and its only symptom is feet skating over the floor.
        //
        // Two drives, and the second is the honest one: the first cannot
        // measure — the machine's pose dials are not resolved until it has
        // run, and the measure refuses to guess without them (stock's
        // ordering, kept by the machine and its own facts).
        var engine = Engine(groundSpeed: 50f);
        var player = Player(engine);
        player.State.Velocity = new Vector(100f, 0f, 0f);

        Update(player, DataUpdateType.Created);

        Assert.True(engine.Called("EntityRender.SequenceGroundSpeed"),
            "the animator asked the model what the sequence travels at");

        Update(player);

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
        Update(player);

        Assert.True(engine.Last("EntityRender.PlaySequence")!.Value.ArgAs<float>(2) == 1f,
            "a sequence with no ground speed is played at rate 1, not divided by zero");
    }

    //-------------------------------------------------------------------------
    // The strafe pose.
    //
    // The machine poses move_yaw from stock's 8-way blend arm (the value's
    // stock-formula facts are AnimStateMachineTests'); what is pinned here is
    // the client's wiring: the drive writes the dial the model gave an index
    // for, against the eyes the host chose. The dials are the 8-way model so
    // the writes are legible and the nine-way box is out of the way.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheStrafeDirectionIsPosedAgainstTheBodysOwnFacing()
    {
        // Body facing east (yaw 0), travelling north (+Y): a left sidestep.
        // (A REMOTE player's eyes ARE their body, so the machine's
        // eye-minus-travel rule reduces to the old policy's
        // travel-minus-body; the local player's eyes are the fact after.)
        // Two drives: the first resolves the dials and the machine's
        // EstimateYaw skips a zero elapsed time; the second poses.
        var engine = Engine(bodyYaw: 0f);
        EightWayDials(engine);
        var player = Player(engine);
        player.State.Velocity = new Vector(0f, 200f, 0f);

        Update(player, DataUpdateType.Created);
        Update(player);

        var posed = MoveYaws(engine);

        Assert.True(posed.Count >= 1, "the strafe direction was posed");
        Assert.True(posed[^1] == 90f, $"as a left sidestep (got {posed[^1]})");
    }

    [Fact]
    public void TurningTheBodyChangesTheStrafeWithoutChangingTheTravel()
    {
        // The same travel with the body turned to face it is a forward walk.
        var engine = Engine(bodyYaw: 90f);
        EightWayDials(engine);
        var player = Player(engine);
        player.State.Velocity = new Vector(0f, 200f, 0f);

        Update(player, DataUpdateType.Created);
        Update(player);

        Assert.True(MoveYaws(engine)[^1] == 0f,
            "a player facing the way they travel is walking forwards, not sidestepping");
    }

    [Fact]
    public void ARemotePlayerStandingAfterASmallTurnHasNoTorsoTwist()
    {
        // The client draws the model's root at the body's facing (no
        // render-angle arm), and a remote player's eyes ARE that facing. A
        // standing turn under 45 degrees leaves stock's feet behind; posed
        // against the feet the torso would turn ten degrees past the eyes.
        // Posed against the drawn root it reads zero, as before the cutover.
        var engine = Engine(bodyYaw: 0f);
        var player = Player(engine);

        Update(player, DataUpdateType.Created);
        Update(player);
        engine.Returns("Entities.GetAngles", new Vector(0f, 10f, 0f));
        Update(player);

        var bodyYaw = engine.AllOf("EntityRender.SetPoseParameter")
                            .Where(c => c.ArgAs<int>(1) == 4)          // body_yaw in NineWayDials
                            .Select(c => c.ArgAs<float>(2)).ToList();

        Assert.True(bodyYaw.Count >= 1, "the torso dial was written");
        Assert.True(bodyYaw[^1] == 0f, $"and it is zero after the small turn (got {bodyYaw[^1]})");
    }

    [Fact]
    public void TheLocalPlayerPosesAgainstTheViewAnglesAndNotTheBody()
    {
        // The machine's difference from the old policy, measured at the host:
        // stock poses the strafe against the EYES, and the client's eyes for
        // its OWN player are the prediction's published view angles
        // (ViewReadout). Body facing east, LOOKING north, travelling north:
        // eyes and travel agree, so the strafe is ZERO — a forward run —
        // where the body-only rule above said ninety for the same picture.
        var engine = Engine(bodyYaw: 0f);
        EightWayDials(engine);
        engine.Returns("Hud.LocalPlayerIndex", 1);       // this player IS the viewer

        var view = new ViewReadout();
        view.PublishAngles(pitch: 0f, yaw: 90f);

        var player = Player(engine, view: view);
        player.State.Velocity = new Vector(0f, 200f, 0f);

        Update(player, DataUpdateType.Created);
        Update(player);

        Assert.True(MoveYaws(engine)[^1] == 0f,
            "the local player strafes against the eyes the viewer actually has");
    }

    [Fact]
    public void ThePoseParametersAreLookedUpOnceRatherThanEveryUpdate()
    {
        // A pose parameter index is a fact about the model. This runs per
        // player per update; asking twenty times a second forever is a studio
        // header walk per player per packet.
        var engine = Engine();
        EightWayDials(engine);
        var player = Player(engine);
        player.State.Velocity = new Vector(100f, 0f, 0f);

        Update(player, DataUpdateType.Created);
        Update(player);
        Update(player);

        Assert.True(engine.CountOf("EntityRender.LookupPoseParameter") == 6,
            "all six stock dials resolved once, and only once, across three drives");
        Assert.True(MoveYaws(engine).Count == 3,
            "the strafe is written on every drive (the first freezes the estimate at zero and the rest pose it)");
    }

    [Fact]
    public void AModelWithNoStrafeBlendIsNotPosedAndIsNotReAsked()
    {
        // -1 is a legal answer and is remembered as readily as an index: a
        // model with no move_yaw animates perfectly well without one, and
        // re-asking forever would be a studio walk per packet for nothing.
        // With none of the six dials named, every write the machine attempts
        // lands on -1, which the target guards at the crossing.
        var engine = Engine(moveYaw: -1);
        Dials(engine, -1, -1, -1, -1, -1, -1);
        var player = Player(engine);
        player.State.Velocity = new Vector(100f, 0f, 0f);

        Update(player, DataUpdateType.Created);
        Update(player);

        Assert.True(engine.CountOf("EntityRender.LookupPoseParameter") == 6,
            "the absent pose parameters are asked for once, all six, together");
        Assert.True(engine.CountOf("EntityRender.SetPoseParameter") == 0,
            "and none is ever written");
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
    public void AModelArrivingLateHasItsPoseParametersResolvedAgain()
    {
        // An entity can exist before its model does, and a lookup against no
        // model answers -1 forever after. A CREATED update is the moment to
        // ask again — stock's OnNewModel, which the host fires on the first
        // update after an entity appears (and re-fires on a later CREATED).
        var engine = Engine();
        var player = Player(engine);

        Update(player, DataUpdateType.Created);
        Update(player);
        Update(player, DataUpdateType.Created);

        Assert.True(engine.CountOf("EntityRender.LookupPoseParameter") == 12,
            "a fresh CREATED update re-resolves all six dials against the model now worn");
    }

    [Fact]
    public void APlayerWithoutTheCompositionsClockStaysPut()
    {
        // The composition's absence must mean "nothing is driven", not a half
        // machine: a player built without the side's clock has no machine, and
        // an update on it may not reach the engine with anything.
        var engine = Engine();
        var player = new ClientSsPlayer(engine) { Handle = new EntityHandle(HandleRaw) };
        player.BindEngine(engine, engine);
        player.State.BindLocal();
        player.State.MaxSpeed = Cap;
        player.State.Alive = true;

        Update(player, DataUpdateType.Created);

        Assert.True(engine.CountOf("EntityRender.PlaySequence") == 0,
            "without the composition's clock the body is not touched at all");
    }

    //-------------------------------------------------------------------------
    // The layer chain's composition: the client asks the container's
    // IAnimStateLayer services in LayerOrder around the HL2 layer (1000),
    // exactly as the server's copy does, so the predicted body is the pose
    // the server chose.
    //-------------------------------------------------------------------------

    private const int ModSequence = 9;

    /// <summary>A mod's layer: names <see cref="ModSequence"/> when it claims, and counts its asks.</summary>
    private abstract class ModLayer(int order, bool claims) : IAnimStateLayer
    {
        public int MainAsks;
        public int LayerOrder => order;

        public bool TryOverrideMainSequence(AnimStateContext context, AnimMainSequence main)
        {
            MainAsks++;
            if (!claims)
                return false;

            main.Activity = "ACT_MOD_LAYER";
            main.Sequence = ModSequence;
            return true;
        }

        public bool TryOverrideGesture(AnimStateContext context, AnimGesture gesture) => false;
        public bool TryHandleAnimEvent(AnimStateContext context, PlayerAnimEvent animEvent, int data) => false;
    }

    private sealed class BeforeHl2(bool claims) : ModLayer(Hl2PostureLayer.LayerOrderDefault - 500, claims);
    private sealed class AfterHl2(bool claims) : ModLayer(Hl2PostureLayer.LayerOrderDefault + 500, claims);

    /// <summary>The composition as ClientModule builds it: the container's layers, collected by type.</summary>
    private static AnimStateLayers Composition(params IAnimStateLayer[] layers)
    {
        var container = new ServiceContainer();
        foreach (var layer in layers)
            container.AddInstance(layer.GetType(), layer);
        return new AnimStateLayers(() => container.ResolveAll<IAnimStateLayer>());
    }

    [Fact]
    public void AContainerLayerOrderedBeforeTheHl2LayerDecidesTheClientsGait()
    {
        var engine = Engine();
        var before = new BeforeHl2(claims: true);
        var player = Player(engine, layers: Composition(before));

        Update(player, DataUpdateType.Created);

        Assert.True(before.MainAsks > 0, "the container's layer was asked on the client's machine");
        Assert.True(player.ShownSequence == ModSequence,
            $"its answer, not the HL2 layer's idle (7), is the body's sequence ({player.ShownSequence})");
    }

    [Fact]
    public void AContainerLayerOrderedAfterTheHl2LayerIsNotAskedWhatTheHl2LayerAnswered()
    {
        var engine = Engine();
        var before = new BeforeHl2(claims: false);
        var after = new AfterHl2(claims: true);
        var player = Player(engine, layers: Composition(after, before));

        Update(player, DataUpdateType.Created);
        Update(player);

        Assert.True(before.MainAsks == 2, $"the declining layer under the HL2 layer is asked once per drive ({before.MainAsks})");
        Assert.True(after.MainAsks == 0, $"the layer above the HL2 layer is never reached: HL2 answered first ({after.MainAsks})");
        Assert.True(player.ShownSequence == 7, $"so the HL2 layer's idle is the body's sequence ({player.ShownSequence})");
    }

    [Fact]
    public void WithNoLayerRegisteredTheClientsChainIsTheHl2LayerAlone()
    {
        var engine = Engine();
        var player = Player(engine, layers: Composition());

        Update(player, DataUpdateType.Created);

        var layers = player.AnimStateOrNull!.Layers;

        Assert.True(layers.Count == 1 && layers[0] is Hl2PostureLayer,
            $"an empty container composes the HL2 layer alone ({string.Join(",", layers.Select(l => l.GetType().Name))})");
        Assert.True(player.ShownSequence == 7 && player.ShownPosture == PlayerPosture.Idle,
            $"and the body stands in the HL2 layer's idle ({player.ShownSequence}, {player.ShownPosture})");
    }
}
