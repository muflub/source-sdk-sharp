/*
 * The generic animation state machine (phase 0.10) — the ported
 * CMultiPlayerAnimState over a fake IAnimTarget, plus its HL2 chain layer.
 *
 * The pose-parameter facts are derived BY HAND from the stock formulas
 * (each cites its line in game/shared/Multiplayer/multiplayer_animstate.cpp);
 * the gesture facts pin the slot rules and the stock refusals; the chain
 * facts make a fake layer's consultation ORDER and CONSUMPTION observable;
 * the allocation fact MEASURES that a steady drive allocates nothing, with a
 * control arm whose own allocations gate the claim.
 */

using System;
using System.Collections.Generic;
using SourceSharp.Shared.Anim;
using Xunit;

using Vector = SourceSharp.Abi.Vector;

namespace SourceSharp.Tests;

public class AnimStateMachineTests
{
    /// <summary>Pose parameter indices the fake's model declares.</summary>
    private const int MoveX = 0, MoveY = 1, BodyYaw = 2, BodyPitch = 3, MoveYaw = 4, MoveScale = 5;

    /// <summary>
    /// The fake: pose writes and sequence hand-overs recorded per drive; the
    /// model's answers fixed per test. The target, not the machine, is what
    /// these facts read back.
    /// </summary>
    private sealed class FakeTarget : IAnimTarget
    {
        public Vector Velocity;
        public float BodyYawValue = 0f;
        public float MaxSpeedValue = 320f;
        public bool OnGroundValue = true;
        public bool DuckedValue { get; set; }
        public int WaterLevelValue { get; set; }
        public bool AliveValue = true;
        public bool ShouldUpdateValue = true;
        public bool HasModelValue = true;
        public bool RootFollowsFeetValue = true;    // stock's arm unless a fact says otherwise
        public LegAnimType Leg = LegAnimType.EightWay;
        public PlayerPosture? Posture { get; set; }

        /// <summary>Every (index, value) pose write of the drives since cleared.</summary>
        public readonly List<(int Index, float Value)> Pose = new();

        /// <summary>Every PlaySequence since cleared.</summary>
        public readonly List<(int Sequence, float Rate, bool Restart)> Played = new();

        /// <summary>Every layer push since cleared (slot with sequence −1 = a clear).</summary>
        public readonly List<(int Slot, int Seq, float Weight, float Rate, float Cycle, bool Restart)> Layers = new();

        public readonly Dictionary<int, float> PoseValues = new();
        public readonly Dictionary<string, int> Sequences = new(StringComparer.Ordinal);
        public readonly Dictionary<int, string> Activities = new();
        public readonly Dictionary<int, float> GroundSpeeds = new();
        public readonly Dictionary<int, (float Duration, bool Looping)> Sequenced = new();
        public readonly List<string> PoseLookups = new();

        public int CurrentSeq = -1;

        Vector IAnimTarget.Velocity => Velocity;
        float IAnimTarget.BodyYaw => BodyYawValue;
        float IAnimTarget.MaxSpeed => MaxSpeedValue;
        bool IAnimTarget.OnGround => OnGroundValue;
        bool IAnimTarget.Ducked => DuckedValue;
        int IAnimTarget.WaterLevel => WaterLevelValue;
        bool IAnimTarget.Alive => AliveValue;
        bool IAnimTarget.ShouldUpdateAnimState => ShouldUpdateValue;
        LegAnimType IAnimTarget.LegAnimType => Leg;
        bool IAnimTarget.HasModel => HasModelValue;
        bool IAnimTarget.RootFollowsFeet => RootFollowsFeetValue;
        int IAnimTarget.CurrentSequence => CurrentSeq;

        public int LookupPoseParameter(string name)
        {
            PoseLookups.Add(name);
            return name switch
            {
                "move_x" => MoveX,
                "move_y" => MoveY,
                "body_yaw" => BodyYaw,
                "body_pitch" => BodyPitch,
                "move_yaw" => MoveYaw,
                "move_scale" => MoveScale,
                _ => -1,
            };
        }

        public float SetPoseParameter(int index, float value)
        {
            Pose.Add((index, value));
            if (index >= 0)
                PoseValues[index] = value;
            return value;
        }

        public float GetPoseParameter(int index) => PoseValues.TryGetValue(index, out float v) ? v : 0f;

        public int SelectWeightedSequence(string activity) =>
            Sequences.TryGetValue(activity, out int s) ? s : -1;

        public float SequenceGroundSpeed(int sequence) =>
            GroundSpeeds.TryGetValue(sequence, out float g) ? g : float.NaN;

        public float SequenceDuration(int sequence) =>
            Sequenced.TryGetValue(sequence, out var d) ? d.Duration : 0f;

        public bool SequenceLooping(int sequence) =>
            Sequenced.TryGetValue(sequence, out var d) && d.Looping;

        public float SequenceCycle(int sequence) => float.NaN;

        public string SequenceActivity(int sequence) =>
            Activities.TryGetValue(sequence, out string? a) ? a : string.Empty;

        public void PlaySequence(int sequence, float playbackRate, bool restart)
        {
            Played.Add((sequence, playbackRate, restart));
            CurrentSeq = sequence;
        }

        public void SetGestureLayer(int slot, int sequence, float weight, float playbackRate,
                                    float cycle, int order, bool restart) =>
            Layers.Add((slot, sequence, weight, playbackRate, cycle, restart));

        public void ClearGestureLayer(int slot) => Layers.Add((slot, -1, 0f, 1f, 0f, true));
    }

    private const float Step = 0.1f;

    private static (FakeTarget Target, MultiplayerAnimState State) Rig(
        AnimStateLayerChain? chain = null, AnimStateParams? prms = null)
    {
        var t = new FakeTarget();
        return (t, new MultiplayerAnimState(t, prms, chain));
    }

    /// <summary>A fake model: activity name → sequence, both directions.</summary>
    private static FakeTarget ModelledTarget(params (string Activity, int Sequence)[] model)
    {
        var (t, _) = Rig();
        foreach (var (activity, sequence) in model)
        {
            t.Sequences[activity] = sequence;
            t.Activities[sequence] = activity;
        }

        return t;
    }

    private static FakeTarget WithModel(FakeTarget t, params (string, int)[] model)
    {
        foreach (var (activity, sequence) in model)
        {
            t.Sequences[activity] = sequence;
            t.Activities[sequence] = activity;
        }

        return t;
    }

    private static MultiplayerAnimState DrivenBy(FakeTarget t, params IAnimStateLayer[] layers) =>
        new(t, AnimStateParams.Hl2Defaults(), new AnimStateLayerChain(layers));

    //-------------------------------------------------------------------------
    // The pose-parameter table, derived by hand from the stock formulas.
    //-------------------------------------------------------------------------

    [Fact]
    public void EyesAtTheTravelBearingPoseAStrafeOfZero()
    {
        // Stock ComputePoseParam_MoveYaw (:1566), 8-way arm:
        //   EstimateYaw (:1649 moving branch) = clamp(atan2(velY,velX),±180);
        //   flYaw = AngleNormalize(-(eyeYaw − estimateYaw)).
        // Travel NORTH (+Y): EstimateYaw = atan2(200,0) = 90; eyes AT that
        // bearing (eyeYaw 90) makes the strafe 0. Read off the formulas.
        var (t, s) = Rig();
        t.Velocity = new Vector(0f, 200f, 0f);

        s.Update(eyeYaw: 90f, eyePitch: 0f, now: 1f, frameTime: Step);

        Assert.True(t.Pose.Exists(p => p.Index == MoveYaw && p.Value == 0f),
            "looking where you travel is a forward run: strafe 0");
    }

    [Fact]
    public void ASidestepPosesNinetyDegreesOfMoveYaw()
    {
        // Travel NORTH (+Y), eyes EAST (+X): EstimateYaw = 90, flYaw =
        // −(0 − 90) = 90 — a strafe of ninety between the look and the
        // travel: the dial the blend reads, the ninety the side-step needs.
        var (t, s) = Rig();
        t.Velocity = new Vector(0f, 200f, 0f);

        s.Update(0f, 0f, 1f, Step);

        Assert.True(t.Pose.Exists(p => p.Index == MoveYaw && p.Value == 90f),
            "looking across your travel poses ninety of strafe");
    }

    [Fact]
    public void StandingZeroesTheNineWayDialsAndFreezesTheEightWayOnes()
    {
        var (t, s) = Rig();
        t.Velocity = new Vector(0f, 200f, 0f);
        s.Update(0f, 0f, 1f, Step);                       // strafe once
        Assert.True(t.Pose.Exists(p => p.Index == MoveYaw && p.Value == 90f),
            "the strafe was posed while moving (the stimulus took)");

        t.Velocity = new Vector(0f, 0f, 0f);
        t.Pose.Clear();
        s.Update(0f, 0f, 1.1f, Step);                     // then stop

        Assert.True(t.Pose.Exists(p => p.Index == MoveX && p.Value == 0f) &&
                    t.Pose.Exists(p => p.Index == MoveY && p.Value == 0f),
            "the 9-way dials go to zero when the player stops (stock's still-arm)");
        Assert.True(t.Pose.TrueForAll(p => p.Index != MoveYaw) && t.PoseValues[MoveYaw] == 90f,
            "and the 8-way strafe is neither rewritten nor zeroed — it freezes");
    }

    [Fact]
    public void TheTorsoReadsTheEyePitchNegated()
    {
        // ComputePoseParam_AimPitch (:1689): SetPoseParameter(body_pitch,
        // −eyePitch). Looking 30 DOWN (pitch −30) poses the torso +30.
        var (t, s) = Rig();
        s.Update(0f, -30f, 1f, Step);

        Assert.True(t.Pose.Exists(p => p.Index == BodyPitch && p.Value == 30f),
            "body_pitch is the negated eye pitch, stock's one-line rule");
    }

    [Fact]
    public void TheFirstStandingDriveInitialisesTheFeetWithTheEyes()
    {
        // ComputePoseParam_AimYaw (:1723): LastAimTurnTime ≤ 0 → goal AND
        // current jump to the eyes, no convergence.
        var (t, s) = Rig();
        s.Update(90f, 0f, 1f, Step);

        Assert.True(s.CurrentFeetYaw == 90f && s.RenderYaw == 90f,
            "the first drive puts the feet under the eyes outright");
    }

    [Fact]
    public void AQuickStandingTurnIsClampedFortyFiveAndThenSnapsHome()
    {
        // The standing arm (:1737): the goal 90 away from the feet is pulled
        // 45 toward the eyes (stock's own TODO-marked per-turn cap), then
        // ConvergeYawAngles (:1793): |delta| 45 → scale 45/60 = 0.75,
        // flYaw = 720 · 0.1 · 0.75 = 54 > 45 → the snap arm lands the feet
        // ON the clamped goal in this one fast frame.
        var (t, s) = Rig();
        s.Update(90f, 0f, 1f, Step);          // init: feet 90
        s.Update(0f, 0f, 1.1f, Step);          // stand, eyes to 0

        Assert.True(s.CurrentFeetYaw == 45f,
            "the feet go to the 45-clamped goal, not to the eyes");
    }

    [Fact]
    public void ASlowStandingTurnCreepsAtTheConfiguredRate()
    {
        // Eyes swung to −160 while the feet are at 0: the clamp moves the
        // goal to −45; the convergence moves the feet by rate · dt ·
        // scale(45/60) = 100 · 0.05 · 0.75 = 3.75 — the ramp, not the snap
        // (the previous fact's frame was the snap arm at rate 720).
        var prms = AnimStateParams.Hl2Defaults();
        prms.BodyYawRate = 100f;
        var (t, s) = Rig(prms: prms);
        s.Update(0f, 0f, 1f, 0.05f);           // init feet 0
        s.Update(-160f, 0f, 1.05f, 0.05f);     // stand; eyes far away → clamp, then ramp

        Assert.True(MathF.Abs(s.CurrentFeetYaw - -3.75f) < 1e-4f,
            $"one slow frame turns the feet by rate·dt·scale = −3.75 (got {s.CurrentFeetYaw})");
    }

    [Fact]
    public void StandingKeepsTheFeetAndTurnsTheTorsoInstead()
    {
        // A SMALL eye turn while standing: |goal − eye| = 10 ≤ 45 leaves the
        // goal (= the feet) where it is, so nothing converges at all — the
        // torso dial (body_yaw = −(eye − feet), :1780) carries the look.
        // (bMoving is the 3D LENGTH > 1, :1710 — stock's quirk kept.)
        var (t, s) = Rig();
        s.Update(0f, 0f, 1f, Step);
        s.Update(10f, 0f, 1.1f, Step);

        Assert.True(s.CurrentFeetYaw == 0f, "the feet did not move for a small standing look");
        Assert.True(t.Pose.Exists(p => p.Index == BodyYaw && p.Value == -10f),
            "the torso takes the −10 dial instead (−(eye − feet))");
    }

    [Fact]
    public void ARootDrawnAtTheBodyPosesNoTorsoTwistWhenTheBodyFacesTheEyes()
    {
        // The same small standing turn as above, on a side that draws the
        // model's root at the body's facing (no render-angle arm) and whose
        // body faces the eyes — a remote player. Stock's −(eye − feet) would
        // be −10 here, and on THIS root that turns the torso a second time;
        // posed against the drawn root the torso already faces the eyes.
        var (t, s) = Rig();
        t.RootFollowsFeetValue = false;
        s.Update(0f, 0f, 1f, Step);
        t.BodyYawValue = 10f;
        s.Update(10f, 0f, 1.1f, Step);

        var last = t.Pose.FindLast(p => p.Index == BodyYaw);
        Assert.True(s.CurrentFeetYaw == 0f, "the feet still keep stock's standing rule");
        Assert.True(last.Index == BodyYaw && last.Value == 0f,
            $"and the torso dial is zero, not −10: the root already faces the eyes (got {last.Value})");
    }

    [Fact]
    public void ARootDrawnAtTheBodyPosesTheTorsoFromTheBodyToTheEyes()
    {
        // Eyes 20 off a body drawn at 0 (the viewer's own player, whose eyes
        // are the view angles): the dial carries exactly that 20, whatever
        // the feet did.
        var (t, s) = Rig();
        t.RootFollowsFeetValue = false;
        t.BodyYawValue = 0f;
        s.Update(40f, 0f, 1f, Step);          // init: feet under the eyes at 40
        s.Update(20f, 0f, 1.1f, Step);

        var last = t.Pose.FindLast(p => p.Index == BodyYaw);
        Assert.True(last.Index == BodyYaw && last.Value == -20f,
            $"the dial is −(eye − body) = −20, not −(eye − feet) = 20 (got {last.Value})");
    }

    [Fact]
    public void MovingFeetFollowTheEyesExactly()
    {
        // bMoving (:1713): goal = eyes, and one fast frame snaps onto them
        // (720 · 0.1 · scale(37/60) = 44.4 > 37).
        var (t, s) = Rig();
        t.Velocity = new Vector(300f, 0f, 0f);
        s.Update(37f, 0f, 1f, Step);

        Assert.True(s.CurrentFeetYaw == 37f && s.RenderYaw == 37f,
            "while moving the feet are the eyes, and the render yaw is the feet");
    }

    [Fact]
    public void PoseParametersAreLookedUpOnceAcrossManyDrives()
    {
        var (t, s) = Rig();
        for (int i = 0; i < 20; i++)
            s.Update(i, 0f, 1f + i * Step, Step);

        Assert.True(t.PoseLookups.Count == 6,
            $"six names, asked once each — not sixty times ({t.PoseLookups.Count})");
    }

    [Fact]
    public void SnapYawToQuantisesLikeStocksBoundaries()
    {
        // Stock's file-local SnapYawTo (:1486): <23→0, <67→45, <113→90,
        // <157→135, else 180 — and the sign survives.
        Assert.True(MultiplayerAnimState.SnapYawTo(22.9f) == 0f, "22.9 snaps to 0");
        Assert.True(MultiplayerAnimState.SnapYawTo(23f) == 45f, "23 crosses into 45");
        Assert.True(MultiplayerAnimState.SnapYawTo(66.9f) == 45f && MultiplayerAnimState.SnapYawTo(67f) == 90f,
            "67 is the 90 boundary");
        Assert.True(MultiplayerAnimState.SnapYawTo(112.9f) == 90f && MultiplayerAnimState.SnapYawTo(113f) == 135f,
            "113 is the 135 boundary");
        Assert.True(MultiplayerAnimState.SnapYawTo(157f) == 180f, "157 is the 180 boundary");
        Assert.True(MultiplayerAnimState.SnapYawTo(-90f) == -90f, "and it is symmetric");
    }

    [Fact]
    public void SlamMoveYawRoutesTheStrafeThroughTheQuantiser()
    {
        // mp_slammoveyaw's arm (:1595): the strafe is snapped BEFORE the
        // blend writes it. Travel at bearing 30° with the eyes at 0: the raw
        // strafe is −(0 − 30) = 30; with the slam it is the authored 45.
        var (t, s) = Rig();
        t.Velocity = new Vector(MathF.Cos(30f * MathF.PI / 180f) * 300f,
                                MathF.Sin(30f * MathF.PI / 180f) * 300f, 0f);

        s.Update(0f, 0f, 1f, Step);
        Assert.True(t.Pose.Exists(p => p.Index == MoveYaw && MathF.Abs(p.Value - 30f) < 1e-3f),
            "without the slam the strafe is the raw bearing");

        s.SlamMoveYaw = true;
        t.Pose.Clear();
        s.Update(0f, 0f, 1.1f, Step);
        Assert.True(t.Pose.Exists(p => p.Index == MoveYaw && p.Value == 45f),
            "with it, the same travel poses the authored 45");
    }

    //-------------------------------------------------------------------------
    // Sequences: the chain, the HL2 layer, the hand-over edges.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheHl2LayerChoosesTheDeathmatchIdleForAStandingPlayer()
    {
        var t = ModelledTarget(("ACT_HL2MP_IDLE_MELEE", 11));
        var s = DrivenBy(t, new Hl2PostureLayer(t));

        s.Update(0f, 0f, 1f, Step);

        Assert.True(t.Played.Count == 1 && t.Played[0].Sequence == 11 && t.Played[0].Restart,
            "standing still plays the idle this content has, entered with a restart (the first hand-over is an activity change)");
        Assert.True(t.Posture == PlayerPosture.Idle, "and the posture is read back off the target");
        Assert.True(s.CurrentMainActivity == MpActivities.StandIdle,
            "while the stock activity name is what the machine stores");
    }

    [Fact]
    public void TheHl2LayerKeepsTheSequenceAndRateAcrossFramesAndRestartsOnlyTheEnter()
    {
        // Gait hand-overs every drive (the rate is the machine's and the
        // shell's clock follows it), WITHOUT a restart once the activity
        // holds; the ENTER of each gait restarts.
        var t = ModelledTarget(("ACT_HL2MP_RUN_MELEE", 3), ("ACT_HL2MP_JUMP_MELEE", 9));
        var s = DrivenBy(t, new Hl2PostureLayer(t));
        t.Velocity = new Vector(300f, 0f, 0f);

        s.Update(0f, 0f, 1f, Step);
        s.Update(0f, 0f, 1.1f, Step);
        Assert.True(t.Played.Count == 2 && t.Played[0].Restart && !t.Played[1].Restart,
            "the run enters with a restart and the next frame re-hands without one — a walk that restarted every frame would step in place");

        t.OnGroundValue = false;
        t.Played.Clear();
        s.Update(0f, 0f, 1.2f, Step);
        Assert.True(t.Played.Count == 1 && t.Played[0].Sequence == 9 && t.Played[0].Restart,
            "entering the air enters the jump sequence with a restart (the activity changed)");

        t.Played.Clear();
        s.Update(0f, 0f, 1.3f, Step);
        Assert.True(t.Played.Count == 1 && !t.Played[0].Restart,
            "and the second jump frame does not — stock's ResetSequence edge, not a per-frame restart");
    }

    [Fact]
    public void AModelThatAnswersToNoActivityIsLeftAlone()
    {
        // The client's animator's honesty — no reference pose, no sequence
        // 0 — now the shared machine's, through the HL2 layer's -1. (With NO
        // layer the STOCK fallback plays sequence 0, stock's :1189 — the
        // separate fact below.)
        var t = ModelledTarget();
        var s = DrivenBy(t, new Hl2PostureLayer(t));
        t.Velocity = new Vector(300f, 0f, 0f);

        s.Update(0f, 0f, 1f, Step);

        Assert.True(t.Played.Count == 0,
            "nothing resolves, nothing plays: the body is left alone");
    }

    [Fact]
    public void WithoutALayerStocksFallbackPlaysSequenceZero()
    {
        // The no-owner arm keeps stock's answer, refusal-free: an activity
        // the model has nothing for becomes SEQUENCE 0 (:1189-1192) — the
        // reason the HL2 layer (which answers -1 and is left alone) is the
        // chain every real player registers.
        var (t, s) = Rig();
        s.Update(0f, 0f, 1f, Step);

        Assert.True(t.Played.Count == 1 && t.Played[0].Sequence == 0,
            "the chain declined; stock's own fallback stands, and it is sequence 0");
    }

    [Fact]
    public void TheRateIsSpeedOverTheAuthoredGroundSpeedClampedLikeStocks()
    {
        // CalcMovementPlaybackRate (:1070, the LIVE HL2 form): speed over
        // the sequence's OWN ground speed, clamped 0.01..10; and a zero
        // ground speed plays at 1 — base_playeranimstate.cpp:519's live rule
        // (the 0.01 arm at :1080 was never called; divergence row). The
        // FIRST drive plays at 1 whatever the velocity: its pose dials are
        // not resolved yet and the measure refuses to guess (GetCurrentMax
        // GroundSpeed's −1 arm, :606).
        var t = ModelledTarget(("ACT_HL2MP_RUN_MELEE", 3));
        t.GroundSpeeds[3] = 50f;
        var s = DrivenBy(t, new Hl2PostureLayer(t));
        t.Velocity = new Vector(100f, 0f, 0f);

        s.Update(0f, 0f, 1f, Step);             // dials unresolved: rate 1
        Assert.True(t.Played[0].Rate == 1f, "the first drive does not measure, it plays at one");

        s.Update(0f, 0f, 1.1f, Step);            // dials in: 100 / 50
        Assert.True(t.Played[1].Rate == 2f, "100 in a sequence built for 50 plays at double");

        t.Velocity = new Vector(4000f, 0f, 0f);
        s.Update(0f, 0f, 1.2f, Step);
        Assert.True(t.Played[2].Rate == 10f, "and the clamp holds at ten");

        // The FLOOR, which the ceiling arm cannot see: an arm that clamped
        // at 0.05 rather than the stock 0.01 survived every other fact.
        t.GroundSpeeds[3] = 500f;
        t.Velocity = new Vector(0.6f, 0f, 0f);    // moving (>0.5), ratio 0.0012
        s.Update(0f, 0f, 1.25f, Step);
        Assert.True(t.Played[3].Rate == 0.01f,
            $"and the clamp holds at the stock floor too, not above it ({t.Played[3].Rate})");

        t.Velocity = new Vector(100f, 0f, 0f);
        t.GroundSpeeds[3] = 0f;                   // an idle-style sequence that travels nowhere
        s.Update(0f, 0f, 1.3f, Step);
        Assert.True(t.Played[4].Rate == 1f, "a sequence that travels nowhere plays at one");
    }

    /// <summary>A chain layer that records its asks; the answers are per-test.</summary>
    private sealed class OverrideLayer : IAnimStateLayer
    {
        private readonly int _order;
        public readonly List<string> Asks = new();
        public Func<AnimStateContext, AnimMainSequence, bool>? Main;
        public Func<AnimStateContext, AnimGesture, bool>? Gesture;
        public Func<AnimStateContext, PlayerAnimEvent, int, bool>? AnimEvent;

        public OverrideLayer(int order) => _order = order;
        public int LayerOrder => _order;

        public bool TryOverrideMainSequence(AnimStateContext context, AnimMainSequence main)
        {
            Asks.Add("main");
            return Main?.Invoke(context, main) ?? false;
        }

        public bool TryOverrideGesture(AnimStateContext context, AnimGesture gesture)
        {
            Asks.Add("gesture");
            return Gesture?.Invoke(context, gesture) ?? false;
        }

        public bool TryHandleAnimEvent(AnimStateContext context, PlayerAnimEvent animEvent, int data)
        {
            Asks.Add($"event:{animEvent}");
            return AnimEvent?.Invoke(context, animEvent, data) ?? false;
        }
    }

    [Fact]
    public void ALayerIsConsultedInChainOrderAndTheFirstYesWins()
    {
        // The plan's consultation fact: layers are asked LOW order first, a
        // declining layer hands the decision DOWN, and once one answers, no
        // lower layer is asked for that drive.
        var t = ModelledTarget(("ACT_DIESIMPLE", 42));
        var first = new OverrideLayer(10);
        first.Main = (ctx, m) => false;                       // declines
        var second = new OverrideLayer(20);
        second.Main = (ctx, m) => { m.Activity = "ACT_DIESIMPLE"; m.Sequence = 42; return true; };
        var hl2 = new OverrideLayer(30);                      // must NOT be asked
        hl2.Main = (ctx, m) => { m.Sequence = 7; m.Activity = "ACT_DIESIMPLE"; return true; };

        var s = DrivenBy(t, first, second, hl2);
        s.Update(0f, 0f, 1f, Step);

        Assert.True(first.Asks.Count == 1 && second.Asks.Count == 1 && hl2.Asks.Count == 0,
            "asked in order: declined, then the first yes, and nobody below the yes");
        Assert.True(t.Played[0].Sequence == 42, "the first YES is the answer that plays");
    }

    [Fact]
    public void AChainOrderTieIsRefusedAndNamesTheCollision()
    {
        var a = new OverrideLayer(3);
        var b = new OverrideLayer(3);

        var thrown = Record.Exception(() =>
            new AnimStateLayerChain(new IAnimStateLayer[] { a, b }));

        Assert.True(thrown is Abi.StageCollisionException,
            $"two layers claiming one order is a startup failure, not a coin flip (got {thrown?.GetType().Name})");
        Assert.True(thrown!.Message.Contains("LayerOrder"),
            "and the refusal names the property to fix");
    }

    [Fact]
    public void TheEventChainRunsBeforeTheStockSwitchAndCanConsume()
    {
        // Stock's ATTACK_PRIMARY would restart GESTURE_SLOT_ATTACK_AND_
        // RELOAD; a layer that eats the event consumes it (the stock arm is
        // not reached), and a declining layer still sees the event on its
        // way to the switch. This fake model answers to no attack activity,
        // so the stock arm fills nothing either — what makes the two arms
        // distinguishable is the ask list: consumed events ask ONCE and
        // never fall through; declined ones ask and continue.
        var t = ModelledTarget();
        var eater = new OverrideLayer(10);
        eater.AnimEvent = (ctx, ev, data) => ev == PlayerAnimEvent.AttackPrimary;

        var s = DrivenBy(t, eater);
        t.Layers.Clear();   // a fresh machine mirrors its empty slots (stock's
                            // InitGestureSlots half); ModelledTarget also built
                            // one over this fake — the facts count pushes from
                            // the event under test, not construction.
        Assert.True(s.DoAnimationEvent(PlayerAnimEvent.AttackPrimary), "consumed");
        Assert.True(eater.Asks.Count == 1 && t.Layers.Count == 0,
            $"the consumed event was asked once and the slot stayed empty (asks {eater.Asks.Count}, pushes {t.Layers.Count})");

        eater.AnimEvent = (ctx, ev, data) => false;           // declines now
        Assert.True(s.DoAnimationEvent(PlayerAnimEvent.AttackPrimary), "the core handled it");
        Assert.True(eater.Asks.Count == 2, "declined events still pass the layer on their way to the stock switch");
    }

    [Fact]
    public void TheStockGestureArmsRefuseSequenceZeroAndFlinchFallsBackToChest()
    {
        var t = ModelledTarget(("ACT_MP_GESTURE_FLINCH_CHEST", 8), ("ACT_MP_GESTURE_FLINCH_HEAD", 0));
        var s = DrivenBy(t);
        t.Layers.Clear();   // see the event fact: baseline past the ctor's slot mirrors

        s.DoAnimationEvent(PlayerAnimEvent.FlinchHead);
        Assert.True(t.Layers.Count == 0 && !s.IsGestureSlotActive(GestureSlot.Flinch),
            $"sequence 0 is not a gesture — stock's `<= 0` refusal (:631) survives (pushes {t.Layers.Count}, active {s.IsGestureSlotActive(GestureSlot.Flinch)})");

        s.DoAnimationEvent(PlayerAnimEvent.FlinchLeftArm);    // model lacks LEFTARM
        Assert.True(t.Layers.Count == 1 && t.Layers[0].Slot == GestureSlot.Flinch &&
                    t.Layers[0].Seq == 8,
            "an unavailable flinch falls back to CHEST, stock's PlayFlinchGesture rule (:371)");
        Assert.True(s.IsGestureSlotActive(GestureSlot.Flinch), "and the slot says it is playing");
    }

    [Fact]
    public void RestartingTheSameGestureResetsItsCycleAndASecondGestureReplacesTheOld()
    {
        // RELOAD, not FLINCH, carries the restart-same-gesture fact: an
        // ACTIVE FLINCH SLOT IGNORES a second flinch event (stock's
        // PlayFlinchGesture early return, :372 — its own fact below), while
        // the reload chain re-drives the same slot from the event switch.
        var t = ModelledTarget(("ACT_MP_RELOAD_STAND", 21), ("ACT_MP_RELOAD_STAND_LOOP", 22));
        var s = DrivenBy(t);
        t.Layers.Clear();   // baseline past the ctor's slot mirrors (see the event fact)

        s.DoAnimationEvent(PlayerAnimEvent.Reload);
        t.Layers.Clear();
        s.DoAnimationEvent(PlayerAnimEvent.Reload);         // same activity again

        // (the stock reload arms push TWICE per event — RestartGesture's
        // restart push and the arm's own rate-refresh PushGestureLayer.)
        Assert.True(t.Layers.Count == 2 && t.Layers[^1].Cycle == 0f && t.Layers[^1].Seq == 21,
            $"the same gesture RESTARTS its cycle rather than stacking (stock's :611 IsGestureSlotPlaying arm; pushes {t.Layers.Count})");

        s.DoAnimationEvent(PlayerAnimEvent.ReloadLoop);      // now the loop activity
        Assert.True(s.GestureSequence(GestureSlot.AttackAndReload) == 22,
            "a different activity on the same named slot REPLACES it (the outgoing cycle run to 1, stock's client arm)");
    }

    [Fact]
    public void AnActiveFlinchSlotIgnoresASecondFlinchEvent()
    {
        // Stock's PlayFlinchGesture early return (:372): one flinch at a
        // time — a second event while a flinch plays is not even started,
        // let alone restarted.
        var t = ModelledTarget(("ACT_MP_GESTURE_FLINCH_CHEST", 8));
        var s = DrivenBy(t);
        s.DoAnimationEvent(PlayerAnimEvent.FlinchChest);
        t.Layers.Clear();

        s.DoAnimationEvent(PlayerAnimEvent.FlinchHead);

        Assert.True(t.Layers.Count == 0 && s.GestureSequence(GestureSlot.Flinch) == 8,
            "the second flinch left the first playing, untouched");
    }
    [Fact]
    public void JumpThenLandRunsTheJumpSequenceThenTheGaitWithTheOneRestart()
    {
        // DoAnimationEvent(JUMP) sets the stock flag + restart request; the
        // next drive's HandleJumping claims ACT_MP_JUMP and the HL2 layer
        // resolves the jump sequence; past the 0.2 s grace with FL_ONGROUND
        // back, the flag clears AND the gait comes back on that same drive
        // with a restart (HandleJumping's own RestartMainSequence, :825).
        var t = ModelledTarget(("ACT_HL2MP_RUN_MELEE", 3), ("ACT_HL2MP_JUMP_MELEE", 9));
        var s = DrivenBy(t, new Hl2PostureLayer(t));
        t.Velocity = new Vector(300f, 0f, 0f);

        s.Update(0f, 0f, 1.0f, Step);
        Assert.True(t.Played[^1].Sequence == 3, "running first");

        s.DoAnimationEvent(PlayerAnimEvent.Jump);
        Assert.True(s.Jumping, "the event raises the stock flag");

        t.OnGroundValue = false;
        t.Played.Clear();
        s.Update(0f, 0f, 1.1f, Step);
        Assert.True(t.Played[^1].Sequence == 9 && t.Played[^1].Restart,
            "the jump sequence plays, restarted once (the first jump frame's ResetAnimation)");

        t.OnGroundValue = true;
        t.Played.Clear();
        s.Update(0f, 0f, 1.35f, Step);         // past the 0.2 s grace
        Assert.True(!s.Jumping, "the landing clears the flag after stock's grace");
        Assert.True(t.Played[^1].Sequence == 3 && t.Played[^1].Restart,
            "and the gait comes back on the SAME drive, restarted — stock's landing ResetMainSequence");

        t.Played.Clear();
        s.Update(0f, 0f, 1.4f, Step);
        Assert.True(t.Played[^1].Sequence == 3 && !t.Played[^1].Restart,
            "and then it simply runs");
    }

    [Fact]
    public void AGestureLayerIsConsultedForActiveSlotsAndCanKillThem()
    {
        var t = ModelledTarget(("ACT_MP_GESTURE_FLINCH_CHEST", 8));
        t.Sequenced[8] = (1f, false);                          // one second, no loop
        var killer = new OverrideLayer(10);
        killer.Gesture = (ctx, g) => { g.Decision = AnimGestureDecision.Kill; return true; };

        var s = DrivenBy(t, killer);
        s.DoAnimationEvent(PlayerAnimEvent.FlinchChest);
        Assert.True(s.IsGestureSlotActive(GestureSlot.Flinch), "filled first");

        s.Update(0f, 0f, 1f, Step);
        Assert.True(!s.IsGestureSlotActive(GestureSlot.Flinch),
            "the layer's Kill decision took the slot down (stock's autokill arm, layer-side)");
        Assert.True(killer.Asks.Contains("gesture"), "and it was asked while the slot was active");
    }

    [Fact]
    public void TheGestureAutokillsAfterOneCycleOfItsOwnClock()
    {
        var t = ModelledTarget(("ACT_MP_GESTURE_FLINCH_CHEST", 8));
        t.Sequenced[8] = (1f, false);
        var s = DrivenBy(t);

        s.DoAnimationEvent(PlayerAnimEvent.FlinchChest);
        s.Update(0f, 0f, 1.5f, 0.5f);
        Assert.True(s.IsGestureSlotActive(GestureSlot.Flinch) && s.GestureCycle(GestureSlot.Flinch) == 0.5f,
            "half a second of a one-second sequence is half a cycle (frametime / duration — stock's client advance :1289)");

        s.Update(0f, 0f, 2.1f, 0.6f);
        Assert.True(!s.IsGestureSlotActive(GestureSlot.Flinch),
            "past one whole cycle the auto-kill slot resets, stock's :1307 arm");
    }

    [Fact]
    public void ClearingTheStateClearsEveryFilledSlotOnTheMirror()
    {
        var t = ModelledTarget(("ACT_MP_GESTURE_FLINCH_CHEST", 8));
        var s = DrivenBy(t);
        s.DoAnimationEvent(PlayerAnimEvent.FlinchChest);
        int fillPushes = t.Layers.Count;
        t.Layers.Clear();

        s.ClearAnimationState();

        Assert.True(fillPushes >= 1, "the fill push happened first (the stimulus was real)");
        Assert.True(t.Layers.Count == GestureSlot.Count,
            $"stock's ResetGestureSlots empties all {GestureSlot.Count} slots and the target sees each clear");
        Assert.True(t.Layers.Exists(p => p.Slot == GestureSlot.Flinch && p.Seq == -1),
            "including the one that was playing");
    }

    //-------------------------------------------------------------------------
    // Zero allocation per Update, measured.
    //-------------------------------------------------------------------------

    [Fact]
    public void AThousandSteadyUpdatesAllocateNothing()
    {
        var t = ModelledTarget(("ACT_HL2MP_RUN_MELEE", 3), ("ACT_HL2MP_JUMP_MELEE", 9));
        t.GroundSpeeds[3] = 300f;
        t.Velocity = new Vector(300f, 0f, 0f);
        var s = DrivenBy(t, new Hl2PostureLayer(t));

        // Warm up the chain and the JIT first: the claim is about steady
        // drives, not about the first one ever.
        for (int warm = 0; warm < 50; warm++)
            s.Update(0f, 0f, 1f + warm * 0.05f, 0.05f);

        // The fewest bytes across three thousand-drive runs: a tier-up on this
        // thread lands in one run, a per-drive allocation in all three
        // (SteadyAllocation; a single run read 4752 bytes on Windows CI).
        int drive = 0;
        long perThousand = SteadyAllocation.LeastAllocated(() =>
        {
            for (int n = 0; n < 1000; n++, drive++)
            {
                // The fake's recorders are cleared every drive: the fact is
                // about the MACHINE's scratch, and letting the fake's lists
                // grow unboundedly would bill the harness for the fake.
                t.Pose.Clear();
                t.Played.Clear();
                t.Layers.Clear();
                s.Update(0f, 0f, 4f + drive * 0.05f, 0.05f);
            }
        }, warm: 0, samples: 3);

        // The positive arm, so a harness that stopped measuring cannot pass:
        // a hundred trivial lists MUST register on the same meter.
        long controlBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            _ = new List<float> { i };
        long control = GC.GetAllocatedBytesForCurrentThread() - controlBefore;

        Assert.True(control > 1000, $"the meter works: {control} bytes for a hundred lists");
        Assert.True(perThousand >= 0 && perThousand < 1000,
            $"a thousand drives cost {perThousand} bytes — the scratch must be reused, not re-made");
    }
}
