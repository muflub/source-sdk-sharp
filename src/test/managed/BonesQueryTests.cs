using System;
using System.Numerics;
using SourceSharp.Shared.Bones;
using Xunit;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for the query cluster (<see cref="BoneSetupQuery"/>): the
/// controller/pose-parameter encode pair and their decodes, MaxFrame/FPS over
/// the blend cells, the animation movement family and their sequence-level
/// weighted wrappers, FindRandomAttachment and BoneIndexByName. Expectations
/// are hand-derived from the stock bodies at the cited ranges (our
/// bone_setup.cpp 5099-6074), never read off the port.
/// </summary>
public class BonesQueryTests
{
    private static StudioHdr Build(StudioHdrBuilder b)
    {
        if (!StudioHdr.TryCreate(b.Build(), b.Resolver, out StudioHdr? hdr) || hdr is null)
        {
            throw new InvalidOperationException("fixture hdr rejected");
        }

        return hdr;
    }

    // ---- controllers (stock 5122-5192) --------------------------------------

    /// <summary>One controller record on bone 1 with the given arm; bone 0 is
    /// the usual root.</summary>
    private static StudioHdr ControllerHdr(
        int type, float start, float end, int inputField = 0)
    {
        var b = new StudioHdrBuilder();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = 0 });
        b.Controllers.Add(new StudioHdrBuilder.ControllerDef
        {
            Bone = 1, Type = type, Start = start, End = end, InputField = inputField,
        });
        return Build(b);
    }

    [Fact]
    public void SetController_NullHdr_ReturnsInputAndLeavesCtlUntouched()
    {
        float ctl = 7.0f; // stock assigns nothing before the hdr guard
        float value = BoneSetupQuery.SetController(null, 0, 3.25f, ref ctl);
        Assert.Equal(3.25f, value);
        Assert.Equal(7.0f, ctl);
    }

    [Fact]
    public void SetController_UnknownInputField_ZeroesCtl_ReturnsInput()
    {
        StudioHdr hdr = ControllerHdr(StudioConstants.STUDIO_X, 0f, 10f, inputField: 5);
        float ctl = 7.0f;
        float value = BoneSetupQuery.SetController(hdr, 3, 3.25f, ref ctl);
        Assert.Equal(3.25f, value);
        Assert.Equal(0.0f, ctl);
    }

    [Fact]
    public void SetController_Translational_InRange_EmitsLerpRoundTrip()
    {
        StudioHdr hdr = ControllerHdr(StudioConstants.STUDIO_X, 0f, 10f);
        float ctl = 0;
        float value = BoneSetupQuery.SetController(hdr, 0, 2.5f, ref ctl);
        Assert.Equal(0.25f, ctl);                    // (2.5 - 0) / (10 - 0)
        Assert.Equal(2.5f, value);                   // (1-0.25)*0 + 0.25*10
    }

    [Fact]
    public void SetController_Translational_ClampsAbove()
    {
        StudioHdr hdr = ControllerHdr(StudioConstants.STUDIO_X, 0f, 10f);
        float ctl = 0;
        float value = BoneSetupQuery.SetController(hdr, 0, 15f, ref ctl);
        Assert.Equal(1.0f, ctl);                     // 1.5 -> 1
        Assert.Equal(10.0f, value);                  // decode of the clamped ctl
    }

    [Fact]
    public void SetController_Translational_ClampsBelow()
    {
        StudioHdr hdr = ControllerHdr(StudioConstants.STUDIO_X, 0f, 10f);
        float ctl = 0;
        float value = BoneSetupQuery.SetController(hdr, 0, -5f, ref ctl);
        Assert.Equal(0.0f, ctl);                     // -0.5 -> 0
        Assert.Equal(0.0f, value);
    }

    /// <summary>STUDIO_XR 90..-90 (end &lt; start): the ugly-hack negates the
    /// input, the window arm leaves -45, ctl = (-45-90)/(-90-90) = 0.75, the
    /// decode (1-0.75)*90 + 0.75*(-90) = -45 is negated a second time — so
    /// the caller gets its sign back and the 0.75 encoding.</summary>
    [Fact]
    public void SetController_Rotational_InvertsWhenEndBelowStart()
    {
        StudioHdr hdr = ControllerHdr(StudioConstants.STUDIO_XR, 90f, -90f);
        float ctl = 0;
        float value = BoneSetupQuery.SetController(hdr, 0, 45f, ref ctl);
        Assert.Equal(0.75f, ctl);
        Assert.Equal(45.0f, value);
    }

    /// <summary>STUDIO_XR -20..20 is non-wrapping (start+359 &gt;= end), so
    /// 190 sits above mid+180 = 180 and gets one -360: -170, whose encoding
    /// clamps to 0 and decodes to the range floor -20.</summary>
    [Fact]
    public void SetController_Rotational_WindowArm_ShovesHighValueDown()
    {
        StudioHdr hdr = ControllerHdr(StudioConstants.STUDIO_XR, -20f, 20f);
        float ctl = 0;
        float value = BoneSetupQuery.SetController(hdr, 0, 190f, ref ctl);
        Assert.Equal(0.0f, ctl);
        Assert.Equal(-20.0f, value);
    }

    /// <summary>Mirror arm: -190 &lt; mid-180 = -180 gets +360 → 170, whose
    /// encoding 190/40 = 4.75 clamps to 1, decoding to 20.</summary>
    [Fact]
    public void SetController_Rotational_WindowArm_ShovesLowValueUp()
    {
        StudioHdr hdr = ControllerHdr(StudioConstants.STUDIO_XR, -20f, 20f);
        float ctl = 0;
        float value = BoneSetupQuery.SetController(hdr, 0, -190f, ref ctl);
        Assert.Equal(1.0f, ctl);
        Assert.Equal(20.0f, value);
    }

    /// <summary>start 0 / end 400 defeats the 359 window test, so the free
    /// loop applies: 1300 - (int)(1300/360 = 3.611)*360 = 220 (TRUNCATION —
    /// rounding to nearest would subtract 1440 and the clamp would answer 0);
    /// ctl = 220/400 = 0.55 decodes straight back.</summary>
    [Fact]
    public void SetController_Rotational_FreeLoopAbove_TruncatesRevolutions()
    {
        StudioHdr hdr = ControllerHdr(StudioConstants.STUDIO_XR, 0f, 400f);
        float ctl = 0;
        float value = BoneSetupQuery.SetController(hdr, 0, 1300f, ref ctl);
        Assert.Equal(0.55f, ctl, 5);
        Assert.Equal(220.0f, value, 4);
    }

    /// <summary>The negative arm is asymmetric by stock's own arithmetic:
    /// -361.5 + ((int)(-361.5 / -360) + 1)*360 = -361.5 + (1+1)*360 = 358.5.
    /// Drop the forced +1 and the value lands on -1.5, clamps to ctl 0 and
    /// answers 0 instead of 358.5.</summary>
    [Fact]
    public void SetController_Rotational_FreeLoopBelow_ForceAddsOneRevolution()
    {
        StudioHdr hdr = ControllerHdr(StudioConstants.STUDIO_XR, 0f, 400f);
        float ctl = 0;
        float value = BoneSetupQuery.SetController(hdr, 0, -361.5f, ref ctl);
        Assert.Equal(0.89625f, ctl, 4);              // 358.5/400
        Assert.Equal(358.5f, value, 4);
    }

    /// <summary>Stock's FindController scan keeps the FIRST record sharing an
    /// inputfield: with {0..10} first and {0..100} second, 5 encodes as 0.5,
    /// not 0.05.</summary>
    [Fact]
    public void SetController_SharedInputField_UsesFirstRecord()
    {
        var b = new StudioHdrBuilder();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = 0 });
        b.Controllers.Add(new StudioHdrBuilder.ControllerDef
        {
            Bone = 1, Type = StudioConstants.STUDIO_X, Start = 0, End = 10, InputField = 0,
        });
        b.Controllers.Add(new StudioHdrBuilder.ControllerDef
        {
            Bone = 1, Type = StudioConstants.STUDIO_X, Start = 0, End = 100, InputField = 0,
        });
        StudioHdr hdr = Build(b);
        float ctl = 0;
        BoneSetupQuery.SetController(hdr, 0, 5f, ref ctl);
        Assert.Equal(0.5f, ctl);
    }

    [Fact]
    public void GetController_MapsUnitEncodingOntoRange()
    {
        StudioHdr hdr = ControllerHdr(StudioConstants.STUDIO_X, -10f, 30f);
        Assert.Equal(-10.0f, BoneSetupQuery.GetController(hdr, 0, 0.0f));
        Assert.Equal(0.0f, BoneSetupQuery.GetController(hdr, 0, 0.25f));   // 0.25*40 - 10
        Assert.Equal(30.0f, BoneSetupQuery.GetController(hdr, 0, 1.0f));
    }

    /// <summary>Both of stock's early returns answer 0 — null hdr and no
    /// controller for the inputfield.</summary>
    [Fact]
    public void GetController_NullOrUnknown_AnswersZero()
    {
        Assert.Equal(0.0f, BoneSetupQuery.GetController(null, 0, 0.5f));
        StudioHdr hdr = ControllerHdr(StudioConstants.STUDIO_X, -10f, 30f, inputField: 5);
        Assert.Equal(0.0f, BoneSetupQuery.GetController(hdr, 3, 0.5f));
    }

    // ---- pose parameters (stock 5231-5278) ----------------------------------

    private static StudioHdr PoseHdr(float start, float end, float loop)
    {
        var b = new StudioHdrBuilder();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef
        {
            Name = "p0", Start = start, End = end, Loop = loop,
        });
        return Build(b);
    }

    [Fact]
    public void SetPoseParameter_IndexGate_ReturnsZero_LeavesCtlUntouched()
    {
        StudioHdr hdr = PoseHdr(0f, 10f, 0f);
        float ctl = 7.0f;
        Assert.Equal(0.0f, BoneSetupQuery.SetPoseParameter(hdr, -1, 5f, ref ctl));
        Assert.Equal(7.0f, ctl);
        Assert.Equal(0.0f, BoneSetupQuery.SetPoseParameter(hdr, 1, 5f, ref ctl));
        Assert.Equal(7.0f, ctl);
    }

    [Fact]
    public void SetPoseParameter_NoLoop_ClampsAboveAndDecodes()
    {
        StudioHdr hdr = PoseHdr(0f, 10f, 0f);
        float ctl = 0;
        Assert.Equal(10.0f, BoneSetupQuery.SetPoseParameter(hdr, 0, 12f, ref ctl));
        Assert.Equal(1.0f, ctl);
        Assert.Equal(0.0f, BoneSetupQuery.SetPoseParameter(hdr, 0, -2f, ref ctl));
        Assert.Equal(0.0f, ctl);
    }

    /// <summary>start 0 / end 10 / loop 10: wrap = 5 + 5 = 10, shift = 0, so
    /// the fold is a modulo about zero here — but derived from the stock
    /// formula, not assumed: 25 → 25 - 10*floor(2.5) = 5, and
    /// -0.5 → -0.5 - 10*floor(-0.05) = -0.5 + 10 = 9.5 (a fmod-style
    /// implementation would leave -0.5 un-wrapped and clamp to 0).</summary>
    [Fact]
    public void SetPoseParameter_Loop_WrapsBothDirections()
    {
        StudioHdr hdr = PoseHdr(0f, 10f, 10f);
        float ctl = 0;
        Assert.Equal(5.0f, BoneSetupQuery.SetPoseParameter(hdr, 0, 25f, ref ctl));
        Assert.Equal(0.5f, ctl);
        Assert.Equal(9.5f, BoneSetupQuery.SetPoseParameter(hdr, 0, -0.5f, ref ctl), 5f);
        Assert.Equal(0.95f, ctl, 5);
    }

    /// <summary>The window centre carries the loop term: start 0 / end 10 /
    /// loop 20 gives wrap = 15, shift = 5. 25 folds by 20*floor(30/20) = 20
    /// → 5, and -5 by 20*floor(0/20) = 0 → -5 UNWRAPPED (a modulo-about-zero
    /// would send -5 to 15). -5 then encodes to -0.25, whose clamp answers 0
    /// — the window is not [start, start+loop), and stock does not pull
    /// values into the parameter range.</summary>
    [Fact]
    public void SetPoseParameter_Loop_CentresWindowOnStockFormula()
    {
        StudioHdr hdr = PoseHdr(0f, 10f, 20f);
        float ctl = 0;
        Assert.Equal(5.0f, BoneSetupQuery.SetPoseParameter(hdr, 0, 25f, ref ctl), 4f);
        Assert.Equal(0.0f, BoneSetupQuery.SetPoseParameter(hdr, 0, -5f, ref ctl), 4f);
        Assert.Equal(0.0f, ctl); // the clamped encoding of the unwrapped -5
    }

    /// <summary>The decode is the raw lerp with no loop and no clamp:
    /// ctl 1.5 on 0..10 answers 15, and an encoded 25 decodes back to its
    /// WRAPPED value 5, not to 25.</summary>
    [Fact]
    public void GetPoseParameter_IsRawLerp_OfEncodedCtl()
    {
        StudioHdr hdr = PoseHdr(0f, 10f, 10f);
        Assert.Equal(15.0f, BoneSetupQuery.GetPoseParameter(hdr, 0, 1.5f));
        float ctl = 0;
        BoneSetupQuery.SetPoseParameter(hdr, 0, 25f, ref ctl);
        Assert.Equal(5.0f, BoneSetupQuery.GetPoseParameter(hdr, 0, ctl));
    }

    [Fact]
    public void GetPoseParameter_IndexGate_AnswersZero()
    {
        StudioHdr hdr = PoseHdr(0f, 10f, 0f);
        Assert.Equal(0.0f, BoneSetupQuery.GetPoseParameter(hdr, -1, 0.5f));
        Assert.Equal(0.0f, BoneSetupQuery.GetPoseParameter(hdr, 1, 0.5f));
    }

    // ---- MaxFrame / FPS (stock 5610-5662) ------------------------------------

    /// <summary>Base fixture: one bone, one pose parameter 0..1, then the
    /// caller adds anims and a sequence over a 2x2 blend table indexed by the
    /// parameter (ParamStart 0 → ParamEnd 1 maps the pose value onto it 1:1).</summary>
    private static StudioHdrBuilder BlendBase()
    {
        var b = new StudioHdrBuilder();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "p0", Start = 0, End = 1 });
        return b;
    }

    private static StudioHdrBuilder AddAnim(
        StudioHdrBuilder b, int numFrames, float fps, int flags = 0)
    {
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            NumFrames = numFrames, Fps = fps, Flags = flags,
        });
        return b;
    }

    private static StudioHdrBuilder AddAnimWithMovement(
        StudioHdrBuilder b, int numFrames, float fps, int flags,
        params StudioHdrBuilder.MovementDef[] moves)
    {
        var a = new StudioHdrBuilder.AnimDef
        {
            NumFrames = numFrames, Fps = fps, Flags = flags,
        };
        a.Movements.AddRange(moves);
        b.Anims.Add(a);
        return b;
    }

    private static StudioHdrBuilder AddBlendSeq(
        StudioHdrBuilder b, short[] table, float[]? weights = null)
    {
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            NumBlends = 2,
            GroupSize0 = 2,
            GroupSize1 = 2,
            AnimTable = table,
            ParamIndex0 = 0,
            ParamStart = new float[] { 0f, 0f },
            ParamEnd = new float[] { 1f, 1f },
            Weights = weights,
        });
        return b;
    }

    private static StudioHdr TwoCellHdr(
        int frames0, float fps0, int frames1, float fps1,
        int flags0 = 0, int flags1 = 0,
        StudioHdrBuilder.MovementDef[]? moves0 = null,
        StudioHdrBuilder.MovementDef[]? moves1 = null,
        float[]? seqWeights = null)
    {
        StudioHdrBuilder b = BlendBase();
        AddAnimWithMovement(b, frames0, fps0, flags0, moves0 ?? []);
        AddAnimWithMovement(b, frames1, fps1, flags1, moves1 ?? []);
        AddBlendSeq(b, new short[] { 0, 1, 0, 1 }, seqWeights);
        return Build(b);
    }

    private static readonly float[] HalfPose = [0.5f];
    private static readonly float[] LowPose = [0.0f];

    /// <summary>Blend 0.5 across cells of 10 and 11 frames:
    /// maxFrame = 10*0.5 + 11*0.5 = 10.5 → -1 → 9.5 → (int)(9.51) = 9.
    /// Rounding to nearest would answer 10.</summary>
    [Fact]
    public void MaxFrame_WeightedBlend_Truncates()
    {
        StudioHdr hdr = TwoCellHdr(10, 30f, 11, 30f);
        Assert.Equal(9, BoneSetupQuery.MaxFrame(hdr, 0, HalfPose));
    }

    /// <summary>Pure cell 0 (pose 0 → weight 1): 10 frames lose their last
    /// frame → 9.</summary>
    [Fact]
    public void MaxFrame_SingleCell_DropsLastFrame()
    {
        StudioHdr hdr = TwoCellHdr(10, 30f, 11, 30f);
        Assert.Equal(9, BoneSetupQuery.MaxFrame(hdr, 0, LowPose));
    }

    /// <summary>The &gt; 1 guard: a one-frame anim is maxFrame 1, is NOT
    /// decremented, and (int)(1.01) answers 1 — the same as the two-frame
    /// answer, which is stock's point.</summary>
    [Fact]
    public void MaxFrame_OneFrameAnim_KeepsTheFrame()
    {
        StudioHdr hdr = TwoCellHdr(1, 30f, 11, 30f);
        Assert.Equal(1, BoneSetupQuery.MaxFrame(hdr, 0, LowPose));
    }

    /// <summary>Zero-weight cells never count: at pose 0 the cells carrying
    /// the 100-frame anim (index 1 in every blend row) have weight exactly 0,
    /// so the answer comes from the 10-frame anim only.</summary>
    [Fact]
    public void MaxFrame_ZeroWeightCellExcluded()
    {
        StudioHdr hdr = TwoCellHdr(10, 30f, 100, 30f);
        Assert.Equal(9, BoneSetupQuery.MaxFrame(hdr, 0, LowPose));
    }

    /// <summary>fps is the plain weighted sum with no numframes gate:
    /// 30*0.5 + 60*0.5 = 45.</summary>
    [Fact]
    public void Fps_WeightsCellFps()
    {
        StudioHdr hdr = TwoCellHdr(10, 30f, 11, 60f);
        Assert.Equal(45.0f, BoneSetupQuery.Fps(hdr, 0, HalfPose), 4);
    }

    /// <summary>Unlike CPS/Duration, a one-frame anim still counts: cell 0 is
    /// a 1-frame anim at 24 fps and answers exactly 24, not 0.</summary>
    [Fact]
    public void Fps_OneFrameAnimStillCounts()
    {
        StudioHdr hdr = TwoCellHdr(1, 24f, 11, 60f);
        Assert.Equal(24.0f, BoneSetupQuery.Fps(hdr, 0, LowPose), 4);
    }

    // ---- AnimPosition (stock 5710-5772) ---------------------------------------

    private static MstudioAnimDesc Anim0(StudioHdr hdr) => hdr.AnimDesc(0);

    private static StudioHdrBuilder.MovementDef Move(
        int endFrame, float v0, float v1, float angle, Vector vector, Vector position)
        => new()
        {
            EndFrame = endFrame, V0 = v0, V1 = v1, Angle = angle,
            Vector = vector, Position = position,
        };

    private static StudioHdr OneMoveAnim(
        int numFrames = 11, float fps = 30f,
        StudioHdrBuilder.MovementDef[]? moves = null)
    {
        StudioHdrBuilder b = BlendBase();
        AddAnimWithMovement(b, numFrames, fps, StudioConstants.STUDIO_DELTA,
            moves ??
            [
                Move(10, 0f, 10f, 90f, new Vector(100, 0, 0), new Vector(100, 0, 0)),
            ]);
        AddBlendSeq(b, new short[] { 0, 0, 0, 0 });
        return Build(b);
    }

    [Fact]
    public void AnimPosition_NoMovements_FalseAndZeroOutputs()
    {
        StudioHdrBuilder b = BlendBase();
        AddAnim(b, 11, 30f);
        AddBlendSeq(b, new short[] { 0, 0, 0, 0 });
        StudioHdr hdr = Build(b);

        bool ok = BoneSetupQuery.AnimPosition(Anim0(hdr), 0.5f, out Vector pos, out float yaw);
        Assert.False(ok);
        Assert.Equal(Vector.Zero, pos);
        Assert.Equal(0.0f, yaw);
    }

    /// <summary>Single block (endframe 10, v0 0, v1 10, vector 100x, angle
    /// 90) at cycle 0.5 of an 11-frame anim: frame 5, f 0.5, the
    /// constant-acceleration distance d = 0*0.5 + 0.5*10*0.25 = 1.25 →
    /// (125,0,0); yaw 0*(1-0.5) + 90*0.5 = 45.</summary>
    [Fact]
    public void AnimPosition_MidBlock_UsesConstantAccelerationCurve()
    {
        StudioHdr hdr = OneMoveAnim();
        bool ok = BoneSetupQuery.AnimPosition(Anim0(hdr), 0.5f, out Vector pos, out float yaw);
        Assert.True(ok);
        Assert.Equal(125.0f, pos.X, 3);
        Assert.Equal(0.0f, pos.Y);
        Assert.Equal(45.0f, yaw, 3);
    }

    /// <summary>Cycle 1.5 folds to iLoops 1 + the 0.5 case, then adds the
    /// LAST block's settled totals once: pos = 125 + 100 = 225, yaw = 45 +
    /// 90 = 135. (These are Position/Angle, not the per-block Vector —
    /// reading the bonus off the wrong field answers 125/45 or 125+0/45.)</summary>
    [Fact]
    public void AnimPosition_LoopAddsLastBlockSettledTotals()
    {
        StudioHdr hdr = OneMoveAnim();
        bool ok = BoneSetupQuery.AnimPosition(Anim0(hdr), 1.5f, out Vector pos, out float yaw);
        Assert.True(ok);
        Assert.Equal(225.0f, pos.X, 3);
        Assert.Equal(135.0f, yaw, 3);
    }

    /// <summary>Cycle -0.25 takes the negative arm, which is a FLOOR not the
    /// truncate of the positive arm: iLoops = (int)(-0.25) - 1 = -1, folded
    /// cycle 0.75. Frame 7.5, f 0.75, d = 0.5*10*0.5625 = 2.8125 →
    /// (281.25,0,0); yaw 67.5. Then iLoops -1 SUBTRACTS one full cycle:
    /// (181.25,0,0), yaw -22.5. A truncating (int) would answer (281.25,0,0)
    /// / 67.5.</summary>
    [Fact]
    public void AnimPosition_NegativeCycle_FloorsLoopCount()
    {
        StudioHdr hdr = OneMoveAnim();
        bool ok = BoneSetupQuery.AnimPosition(Anim0(hdr), -0.25f, out Vector pos, out float yaw);
        Assert.True(ok);
        Assert.Equal(181.25f, pos.X, 3);
        Assert.Equal(-22.5f, yaw, 3);
    }

    /// <summary>Two blocks — block 0 settles at (50,0,0)/30°, block 1 carries
    /// the v0/v1 ramp to (150,0,0)/90°. Cycle 0.75 → frame 7.5 sits in block
    /// 1 at f 0.5: position 50 + 1.25*100 = 175, yaw = 30*(1-0.5) + 90*0.5 =
    /// 60. The yaw is the DECAY of the previous block's angle, not a lerp
    /// from zero — a lerp-from-zero reads 45.</summary>
    [Fact]
    public void AnimPosition_SecondBlock_DecaysPreviousAngleIntoBlockAngle()
    {
        StudioHdr hdr = OneMoveAnim(moves:
        [
            Move(5, 0f, 0f, 30f, new Vector(50, 0, 0), new Vector(50, 0, 0)),
            Move(10, 0f, 10f, 90f, new Vector(100, 0, 0), new Vector(150, 0, 0)),
        ]);
        bool ok = BoneSetupQuery.AnimPosition(Anim0(hdr), 0.75f, out Vector pos, out float yaw);
        Assert.True(ok);
        Assert.Equal(175.0f, pos.X, 3);
        Assert.Equal(60.0f, yaw, 3);
    }

    /// <summary>A frame past the last block's endframe exhausts the walk and
    /// answers FALSE — but the else-arm accumulation is still in the outputs:
    /// last block settled at (50,0,0)/30°. (Stock's fall-through; zeroing on
    /// every false arm would lose this caller-visible state.)</summary>
    [Fact]
    public void AnimPosition_PastLastBlock_FallsThroughFalseWithAccumulators()
    {
        StudioHdr hdr = OneMoveAnim(moves:
        [
            Move(5, 0f, 0f, 30f, new Vector(50, 0, 0), new Vector(50, 0, 0)),
        ]);
        bool ok = BoneSetupQuery.AnimPosition(Anim0(hdr), 0.8f, out Vector pos, out float yaw);
        Assert.False(ok);
        Assert.Equal(50.0f, pos.X, 3);
        Assert.Equal(30.0f, yaw, 3);
    }

    // ---- AnimVelocity (stock 5773-5806) ---------------------------------------

    /// <summary>Single block at cycle 0.5 of an 11-frame/30-fps anim: frame 5
    /// (fold subtracts (int)(5/10) = 0), f 0.5, LINEAR blend
    /// vel = 0*(1-f) + 10*f = 5, then per-second scaling
    /// 5 * 30 / (10 - 0) = 15 → (1500,0,0). A dt divisor of
    /// (endframe - prevframe + 1) answers 1500*10/11.</summary>
    [Fact]
    public void AnimVelocity_ScalesByBlockFrameSpan()
    {
        StudioHdr hdr = OneMoveAnim();
        bool ok = BoneSetupQuery.AnimVelocity(Anim0(hdr), 0.5f, out Vector vel);
        Assert.True(ok);
        Assert.Equal(1500.0f, vel.X, 2);
        Assert.Equal(0.0f, vel.Y);
    }

    /// <summary>Frame 7.5 falls in block 1 whose prevframe is 5: f 0.5, the
    /// blend 10*(1-0.5) + 20*0.5 = 15, and the divisor is the BLOCK span
    /// 10-5 = 3 frames, so 15 * 30 / 5 = 90 → (9000,0,0). Forgetting the
    /// prevframe (divisor 10) answers 4500.</summary>
    [Fact]
    public void AnimVelocity_SecondBlock_BlendsAndUsesPrevFrameSpan()
    {
        StudioHdr hdr = OneMoveAnim(moves:
        [
            Move(5, 0f, 0f, 30f, new Vector(50, 0, 0), new Vector(50, 0, 0)),
            Move(10, 10f, 20f, 90f, new Vector(100, 0, 0), new Vector(150, 0, 0)),
        ]);
        bool ok = BoneSetupQuery.AnimVelocity(Anim0(hdr), 0.75f, out Vector vel);
        Assert.True(ok);
        Assert.Equal(9000.0f, vel.X, 2);
    }

    /// <summary>Stock's fold is `flFrame - (int)(flFrame / (numframes - 1))`:
    /// it subtracts the whole-cycle COUNT in FRAMES, not the whole cycles.
    /// Cycle 1.5 gives frame 15, minus (int)(15/10) = 1 → frame 14, which is
    /// past the only block's endframe 10 — so the walk exhausts and the
    /// answer is FALSE with a zeroed output. A "sensible" modulo (frame 5)
    /// would answer true with (1500,0,0); no fold at all would answer true
    /// with f = 1.5 → (4500,0,0). Cycle 1.0 shows the same arithmetic one
    /// frame short of the gate: frame 10 - 1 = 9, f 0.9, vel 9 * 30/10 = 27
    /// → (2700,0,0).</summary>
    [Fact]
    public void AnimVelocity_FoldSubtractsCycleCountInFrames()
    {
        StudioHdr hdr = OneMoveAnim();
        Assert.False(BoneSetupQuery.AnimVelocity(Anim0(hdr), 1.5f, out Vector folded));
        Assert.Equal(Vector.Zero, folded);
        Assert.True(BoneSetupQuery.AnimVelocity(Anim0(hdr), 1.0f, out Vector justShort));
        Assert.Equal(2700.0f, justShort.X, 2);
    }

    /// <summary>Cycle -0.25 → frame -2.5; the fold removes (int)(-0.25) = 0,
    /// so the NEGATIVE frame survives (no loop-count arm here): f = -0.25,
    /// vel = 0*(1.25) + 10*(-0.25) = -2.5, scaled -2.5 * 30 / 10 = -7.5 →
    /// (-750,0,0).</summary>
    [Fact]
    public void AnimVelocity_NegativeCycle_KeepsUnwrappedFraction()
    {
        StudioHdr hdr = OneMoveAnim();
        Assert.True(BoneSetupQuery.AnimVelocity(Anim0(hdr), -0.25f, out Vector vel));
        Assert.Equal(-750.0f, vel.X, 2);
    }

    [Fact]
    public void AnimVelocity_NoMovements_FalseZeroOutput()
    {
        StudioHdrBuilder b = BlendBase();
        AddAnim(b, 11, 30f);
        AddBlendSeq(b, new short[] { 0, 0, 0, 0 });
        StudioHdr hdr = Build(b);

        // Stock has no nummovements gate at all here — the loop just never
        // runs. (numframes 11 keeps the fold's divisor finite.)
        Assert.False(BoneSetupQuery.AnimVelocity(Anim0(hdr), 0.5f, out Vector vel));
        Assert.Equal(Vector.Zero, vel);
    }

    // ---- AnimMovement (stock 5812-5836) ---------------------------------------

    [Fact]
    public void AnimMovement_NoMovements_FalseZeroOutputs()
    {
        StudioHdrBuilder b = BlendBase();
        AddAnim(b, 11, 30f);
        AddBlendSeq(b, new short[] { 0, 0, 0, 0 });
        StudioHdr hdr = Build(b);

        bool ok = BoneSetupQuery.AnimMovement(
            Anim0(hdr), 0.0f, 0.5f, out Vector pos, out float yaw);
        Assert.False(ok);
        Assert.Equal(Vector.Zero, pos);
        Assert.Equal(0.0f, yaw);
    }

    /// <summary>From 0.2 (pos 0.5*10*0.04*100 = 20, yaw 18) to 0.7 (pos
    /// 0.5*10*0.49*100 = 245, yaw 63): tmp = (225,0,0), yaw delta 45, rotated
    /// back by -18° → X = 225*cos18 = 213.9877, Y = -225*sin18 =
    /// -69.6364. (VectorYawRotate at NEGATIVE start yaw — a missing
    /// negation flips the Y sign; an unrotated output keeps Y at zero.)</summary>
    [Fact]
    public void AnimMovement_DerotatesDeltaByStartYaw()
    {
        StudioHdr hdr = OneMoveAnim();
        bool ok = BoneSetupQuery.AnimMovement(Anim0(hdr), 0.2f, 0.7f, out Vector pos, out float yaw);
        Assert.True(ok);
        Assert.Equal(225.0f * MathF.Cos(18.0f * MathF.PI / 180.0f), pos.X, 3);
        Assert.Equal(-225.0f * MathF.Sin(18.0f * MathF.PI / 180.0f), pos.Y, 3);
        Assert.Equal(0.0f, pos.Z);
        Assert.Equal(45.0f, yaw, 3);
    }

    /// <summary>With the from-cycle at 0 the block fraction f is 0, so the
    /// start yaw is 0 and the delta is unrotated: (245,0,0), yaw 63.</summary>
    [Fact]
    public void AnimMovement_FromCycleZero_PassesDeltaThrough()
    {
        StudioHdr hdr = OneMoveAnim();
        Assert.True(BoneSetupQuery.AnimMovement(Anim0(hdr), 0.0f, 0.7f, out Vector pos, out float yaw));
        Assert.Equal(245.0f, pos.X, 3);
        Assert.Equal(0.0f, pos.Y, 3);
        Assert.Equal(63.0f, yaw, 3);
    }

    // ---- FindAnimDistance (stock 5839-5879) -----------------------------------

    [Fact]
    public void FindAnimDistance_NonPositiveDistance_AnswersZero()
    {
        StudioHdr hdr = OneMoveAnim();
        Assert.Equal(0.0f, BoneSetupQuery.FindAnimDistance(Anim0(hdr), 0.0f));
        Assert.Equal(0.0f, BoneSetupQuery.FindAnimDistance(Anim0(hdr), -12f));
    }

    /// <summary>Block average speed (0+10)/2 = 5 covers flDist 5 exactly at
    /// the block end: a = 0.5*(10-0) = 5, b = 0, c = -5, disc 100, root1 =
    /// 10/10 = 1 → (0 + 1*10) * 0.1 = 1.0 cycle.</summary>
    [Fact]
    public void FindAnimDistance_FullBlockDistance_EndsAtOneCycle()
    {
        StudioHdr hdr = OneMoveAnim();
        Assert.Equal(1.0f, BoneSetupQuery.FindAnimDistance(Anim0(hdr), 5.0f), 5);
    }

    /// <summary>Half the block distance: root1 = sqrt(0 - 4*5*(-2.5)) / (2*5)
    /// = sqrt(50)/10 = 0.707107 → cycle root1 (block spans the whole table
    /// here), i.e. NOT the linear 0.5 — the quadratic root is the whole point
    /// of the SolveQuadratic arm.</summary>
    [Fact]
    public void FindAnimDistance_BetweenFrames_SolvesTheQuadratic()
    {
        StudioHdr hdr = OneMoveAnim();
        float root = MathF.Sqrt(50.0f) / 10.0f;
        Assert.Equal(root, BoneSetupQuery.FindAnimDistance(Anim0(hdr), 2.5f), 5);
    }

    /// <summary>flDist 3 first consumes block 0's average (0+4)/2 = 2,
    /// leaving 1 inside block 1 (span 10-5): root1 = sqrt(4*2*1)/4 =
    /// sqrt(8)/4 → (5 + root1*5) * 0.1 = 0.853553. A missing prevframe
    /// offset answers root1*0.1 = 0.0707.</summary>
    [Fact]
    public void FindAnimDistance_ConsumedBlocks_ShiftByPrevFrame()
    {
        StudioHdr hdr = OneMoveAnim(moves:
        [
            Move(5, 0f, 4f, 0f, new Vector(10, 0, 0), new Vector(10, 0, 0)),
            Move(10, 0f, 4f, 0f, new Vector(10, 0, 0), new Vector(20, 0, 0)),
        ]);
        float root = MathF.Sqrt(8.0f) / 4.0f;
        Assert.Equal((5.0f + root * 5.0f) * 0.1f, BoneSetupQuery.FindAnimDistance(Anim0(hdr), 3.0f), 4);
    }

    /// <summary>Distance beyond every block's average (5 + 2 = 7 &lt; 100)
    /// walks off the table and answers a full cycle, 1.0.</summary>
    [Fact]
    public void FindAnimDistance_DistanceBeyondMovement_AnswersOne()
    {
        StudioHdr hdr = OneMoveAnim(moves:
        [
            Move(5, 0f, 4f, 0f, new Vector(10, 0, 0), new Vector(10, 0, 0)),
            Move(10, 0f, 4f, 0f, new Vector(10, 0, 0), new Vector(20, 0, 0)),
        ]);
        Assert.Equal(1.0f, BoneSetupQuery.FindAnimDistance(Anim0(hdr), 100.0f));
    }

    // ---- the Seq trio (stock 5883-5986) ---------------------------------------

    private static StudioHdrBuilder.MovementDef[] Walk100x
        => [Move(10, 0f, 10f, 90f, new Vector(100, 0, 0), new Vector(100, 0, 0))];

    private static StudioHdrBuilder.MovementDef[] Walk50y
        => [Move(10, 0f, 10f, 0f, new Vector(0, 50, 0), new Vector(0, 50, 0))];

    /// <summary>Blend 0.5 between a walk-x anim (cycle 0→0.7 delta (245,0,0),
    /// yaw 63) and a walk-y anim (delta (0,122.5,0), yaw 0): the deltas
    /// weight by 0.5/0.5 → (122.5, 61.25, 0) and yaw 31.5.</summary>
    [Fact]
    public void SeqMovement_BlendsCellDeltasByWeight()
    {
        StudioHdr hdr = TwoCellHdr(
            11, 30f, 11, 30f,
            flags0: StudioConstants.STUDIO_DELTA, flags1: StudioConstants.STUDIO_DELTA,
            moves0: Walk100x, moves1: Walk50y);
        bool ok = BoneSetupQuery.SeqMovement(
            hdr, 0, 0.0f, 0.7f, HalfPose, out Vector pos, out float yaw);
        Assert.True(ok);
        Assert.Equal(122.5f, pos.X, 3);
        Assert.Equal(61.25f, pos.Y, 3);
        Assert.Equal(31.5f, yaw, 3);
    }

    /// <summary>The gate is stock's `if (weight[i])`: at pose 0 only cell 0
    /// (the delta anim with NO movement blocks) is live; the movement anim
    /// sits at weight-exactly-0 and must not move the answer nor set found —
    /// so a zero-weight movement cell still reports NOT FOUND with a zero
    /// delta.</summary>
    [Fact]
    public void SeqMovement_ZeroWeightMovementCell_NotFoundNoDelta()
    {
        StudioHdr hdr = TwoCellHdr(
            11, 30f, 11, 30f,
            flags0: StudioConstants.STUDIO_DELTA, flags1: StudioConstants.STUDIO_DELTA,
            moves1: Walk100x);
        bool ok = BoneSetupQuery.SeqMovement(
            hdr, 0, 0.0f, 0.7f, LowPose, out Vector pos, out float yaw);
        Assert.False(ok);
        Assert.Equal(Vector.Zero, pos);
        Assert.Equal(0.0f, yaw);
    }

    /// <summary>Companion arm of the gate fact: at pose 1 cell 1 IS the
    /// weight-1 cell and the same fixture reports its full delta.</summary>
    [Fact]
    public void SeqMovement_CellAtWeightOne_Moves()
    {
        var high = new float[] { 1.0f };
        StudioHdr hdr = TwoCellHdr(
            11, 30f, 11, 30f,
            flags0: StudioConstants.STUDIO_DELTA, flags1: StudioConstants.STUDIO_DELTA,
            moves1: Walk100x);
        bool ok = BoneSetupQuery.SeqMovement(
            hdr, 0, 0.0f, 0.7f, high, out Vector pos, out float yaw);
        Assert.True(ok);
        Assert.Equal(245.0f, pos.X, 3);
        Assert.Equal(0.0f, pos.Y, 3);
        Assert.Equal(63.0f, yaw, 3);
    }

    /// <summary>The static-prop arm: a NON-delta anim with no movement
    /// blocks counts as found (it plants the model at bone 0) while moving
    /// nothing, gated on the sequence's own bone-0 weight &gt; 0.</summary>
    [Fact]
    public void SeqMovement_NonDeltaNoMovement_WithBoneWeight_FoundWithoutMotion()
    {
        StudioHdr hdr = TwoCellHdr(11, 30f, 11, 30f, flags0: 0, seqWeights: [1.0f]);
        bool ok = BoneSetupQuery.SeqMovement(
            hdr, 0, 0.0f, 0.7f, LowPose, out Vector pos, out float yaw);
        Assert.True(ok);
        Assert.Equal(Vector.Zero, pos);
        Assert.Equal(0.0f, yaw);
    }

    /// <summary>The same fixture with the DELTA flag set answers false — the
    /// static-prop arm explicitly excludes delta anims.</summary>
    [Fact]
    public void SeqMovement_DeltaNoMovement_NotFound()
    {
        StudioHdr hdr = TwoCellHdr(11, 30f, 11, 30f,
            flags0: StudioConstants.STUDIO_DELTA, seqWeights: [1.0f]);
        Assert.False(BoneSetupQuery.SeqMovement(
            hdr, 0, 0.0f, 0.7f, LowPose, out Vector pos, out float yaw));
    }

    /// <summary>And with the sequence bone-0 weight at zero the static-prop
    /// arm closes too: found stays false.</summary>
    [Fact]
    public void SeqMovement_NonDeltaNoMovement_ZeroBoneWeight_NotFound()
    {
        StudioHdr hdr = TwoCellHdr(11, 30f, 11, 30f, flags0: 0, seqWeights: [0.0f]);
        Assert.False(BoneSetupQuery.SeqMovement(
            hdr, 0, 0.0f, 0.7f, LowPose, out Vector pos, out float yaw));
    }

    /// <summary>Velocities blend like deltas: cell 0 walks x (block v1 10 →
    /// vel 5 at f 0.5, *30/10 = 15 → (1500,0,0)), cell 1 walks y at the same
    /// rate ((0,1500*0.5,0)); weights 0.5/0.5 → (750, 375, 0).</summary>
    [Fact]
    public void SeqVelocity_BlendsCellVelocitiesByWeight()
    {
        StudioHdr hdr = TwoCellHdr(
            11, 30f, 11, 30f,
            flags0: StudioConstants.STUDIO_DELTA, flags1: StudioConstants.STUDIO_DELTA,
            moves0: Walk100x, moves1: Walk50y);
        bool ok = BoneSetupQuery.SeqVelocity(hdr, 0, 0.5f, HalfPose, out Vector vel);
        Assert.True(ok);
        Assert.Equal(750.0f, vel.X, 2);
        Assert.Equal(375.0f, vel.Y, 2);
    }

    [Fact]
    public void SeqVelocity_NoMovementCells_FalseZeroOutput()
    {
        StudioHdr hdr = TwoCellHdr(11, 30f, 11, 30f,
            flags0: StudioConstants.STUDIO_DELTA, flags1: StudioConstants.STUDIO_DELTA);
        Assert.False(BoneSetupQuery.SeqVelocity(hdr, 0, 0.5f, HalfPose, out Vector vel));
        Assert.Equal(Vector.Zero, vel);
    }

    /// <summary>At pose 0.5 cell 0 solves distance 1 to root1 = sqrt(20)/10
    /// = 0.447214 cycles; cell 1 (v0 0, v1 4, average 2 &gt;= 1) solves
    /// sqrt(8)/4 = 0.707107. Blended: 0.577160 — and there is no found flag
    /// to gate it.</summary>
    [Fact]
    public void FindSeqDistance_BlendsCellCycles()
    {
        var nearMove = new[] { Move(10, 0f, 4f, 0f, new Vector(10, 0, 0), new Vector(10, 0, 0)) };
        StudioHdr hdr = TwoCellHdr(
            11, 30f, 11, 30f,
            flags0: StudioConstants.STUDIO_DELTA, flags1: StudioConstants.STUDIO_DELTA,
            moves0: Walk100x, moves1: nearMove);
        float c0 = MathF.Sqrt(20.0f) / 10.0f;
        float c1 = MathF.Sqrt(8.0f) / 4.0f;
        Assert.Equal(0.5f * c0 + 0.5f * c1, BoneSetupQuery.FindSeqDistance(hdr, 0, HalfPose, 1.0f), 5);
    }

    /// <summary>Single live cell (pose 0 → weights 1,0,0,0, and the -1 cells
    /// in the table are never read at zero weight): the answer is cell 0's
    /// own quadratic root, sqrt(50)/10 for distance 2.5.</summary>
    [Fact]
    public void FindSeqDistance_SingleCell_PassesThroughMinusOneCellsUntouched()
    {
        StudioHdrBuilder b = BlendBase();
        AddAnimWithMovement(b, 11, 30f, StudioConstants.STUDIO_DELTA, Walk100x);
        AddBlendSeq(b, new short[] { 0, -1, -1, -1 });
        StudioHdr hdr = Build(b);
        float root = MathF.Sqrt(50.0f) / 10.0f;
        Assert.Equal(root, BoneSetupQuery.FindSeqDistance(hdr, 0, LowPose, 2.5f), 5);
    }

    // ---- FindRandomAttachment (stock 6014-6042) -------------------------------

    private static StudioHdr AttachmentHdr(params string[] names)
    {
        var b = new StudioHdrBuilder();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b0", Parent = -1 });
        foreach (string n in names)
        {
            b.Attachments.Add(new StudioHdrBuilder.AttachmentDef { Name = n, LocalBone = 0 });
        }

        return Build(b);
    }

    /// <summary>Substring containment, index-ordered candidates: "hearty"
    /// matches the two hs_hearty_* attachments, so randomIndex picks k of 2.
    /// (An index pick reads as a list-order fact: stock's
    /// <c>RandomInt(0,size-1)</c> over the scan-order list.)</summary>
    [Fact]
    public void FindRandomAttachment_PicksKthOfMatches()
    {
        StudioHdr hdr = AttachmentHdr("hs_hearty_1", "other", "hs_hearty_2");
        Assert.Equal(0, BoneSetupQuery.FindRandomAttachment(hdr, "hearty", 0));
        Assert.Equal(2, BoneSetupQuery.FindRandomAttachment(hdr, "hearty", 1));
    }

    /// <summary>The match is substring containment, not a prefix test and
    /// not a suffix test: the needle "rty_2" lives mid-name in
    /// <c>hs_hearty_2</c> only, so it is the sole candidate — a startsWith
    /// misreading finds nothing and answers -1.</summary>
    [Fact]
    public void FindRandomAttachment_MatchesSubstringAnywhere()
    {
        StudioHdr hdr = AttachmentHdr("hs_hearty_1", "hs_hearty_2");
        Assert.Equal(1, BoneSetupQuery.FindRandomAttachment(hdr, "rty_2", 0));
        Assert.Equal(-1, BoneSetupQuery.FindRandomAttachment(hdr, "rty_2", 1)); // only 1 candidate
    }

    /// <summary>stock's strstr is case-SENSITIVE: the upper-case needle finds
    /// nothing (an OrdinalIgnoreCase slip answers 0/2).</summary>
    [Fact]
    public void FindRandomAttachment_IsCaseSensitive()
    {
        StudioHdr hdr = AttachmentHdr("hs_hearty_1", "hs_hearty_2");
        Assert.Equal(-1, BoneSetupQuery.FindRandomAttachment(hdr, "HEARTY", 0));
    }

    /// <summary>Empty needle: strstr(name, "") is non-null for every name, so
    /// every attachment is a candidate and index 0 is the first record.</summary>
    [Fact]
    public void FindRandomAttachment_EmptyNeedle_MatchesEverything()
    {
        StudioHdr hdr = AttachmentHdr("alpha", "beta");
        Assert.Equal(0, BoneSetupQuery.FindRandomAttachment(hdr, "", 0));
        Assert.Equal(1, BoneSetupQuery.FindRandomAttachment(hdr, "", 1));
    }

    /// <summary>No candidate list, no pick: a name nothing contains answers
    /// -1, as do both out-of-range draws (stock's own draw cannot go out of
    /// range — the DIVERGENCE arm documented in the port).</summary>
    [Fact]
    public void FindRandomAttachment_NoMatchAndOutOfRange_AnswerMinusOne()
    {
        StudioHdr hdr = AttachmentHdr("hs_hearty_1");
        Assert.Equal(-1, BoneSetupQuery.FindRandomAttachment(hdr, "hearth", 0));
        Assert.Equal(-1, BoneSetupQuery.FindRandomAttachment(hdr, "hearty", 1));  // count is 1
        Assert.Equal(-1, BoneSetupQuery.FindRandomAttachment(hdr, "hearty", -1));
        Assert.Equal(-1, BoneSetupQuery.FindRandomAttachment(null, "hearty", 0));
    }

    // ---- BoneIndexByName (stock 6044-6074) -------------------------------------

    private static StudioHdr BoneHdr(params string[] names)
    {
        var b = new StudioHdrBuilder();
        for (int i = 0; i < names.Length; i++)
        {
            b.Bones.Add(new StudioHdrBuilder.BoneDef
            {
                Name = names[i], Parent = i == 0 ? -1 : i - 1,
            });
        }

        return Build(b);
    }

    /// <summary>The stock comparator is StrICmp: an ASCII-fold match returns
    /// the bone index.</summary>
    [Fact]
    public void BoneIndexByName_FoldsAsciiCase()
    {
        StudioHdr hdr = BoneHdr("root", "ValveBiped.Bip01_Head1", "b2");
        Assert.Equal(1, BoneSetupQuery.BoneIndexByName(hdr, "VALVEBIPED.BIP01_HEAD1"));
        Assert.Equal(1, BoneSetupQuery.BoneIndexByName(hdr, "valvebiped.bip01_head1"));
        Assert.Equal(0, BoneSetupQuery.BoneIndexByName(hdr, "ROOT"));
    }

    /// <summary>Only ASCII folds: stock's stricmp leaves non-ASCII bytes
    /// alone, so "ü" does not match "Ü" (StringComparison.OrdinalIgnoreCase
    /// would wrongly answer 1).</summary>
    [Fact]
    public void BoneIndexByName_DoesNotFoldNonAscii()
    {
        StudioHdr hdr = BoneHdr("root", "Über");
        Assert.Equal(-1, BoneSetupQuery.BoneIndexByName(hdr, "über"));
        Assert.Equal(1, BoneSetupQuery.BoneIndexByName(hdr, "ÜBER")); // the Ü stays, the rest folds
    }

    /// <summary>First index wins on a duplicated name (the scan's documented
    /// answer; the stock sorted table would hold them adjacent).</summary>
    [Fact]
    public void BoneIndexByName_DuplicateName_FirstIndex()
    {
        StudioHdr hdr = BoneHdr("root", "dup", "dup");
        Assert.Equal(1, BoneSetupQuery.BoneIndexByName(hdr, "DUP"));
    }

    [Fact]
    public void BoneIndexByName_MissAndNullHdr_AnswerMinusOne()
    {
        StudioHdr hdr = BoneHdr("root", "b1");
        Assert.Equal(-1, BoneSetupQuery.BoneIndexByName(hdr, "b10")); // prefix is not a match
        Assert.Equal(-1, BoneSetupQuery.BoneIndexByName(hdr, ""));
        Assert.Equal(-1, BoneSetupQuery.BoneIndexByName(null, "root"));
    }

    // ---- stock's double literals (bone_setup.cpp 5009-5034, 5115, 5499,
    //      5603, 5711-5716): bit-exact pins. Facts marked Characterization
    //      pin a literal where no float/double divergence was found; the
    //      others are the inputs on which the all-float port differed.

    /// <summary>Stock 5016: <c>flValue &gt; ((start + end) / 2.0) + 180</c>
    /// compares in double. For start 61.01979 / end 148.98264 the double
    /// threshold is 285.00121307…, below the float value 285.001220703125,
    /// so stock wraps by -360 (ctl clamps to 0, the decode answers start);
    /// the float threshold rounds to the value itself and skips the wrap
    /// (ctl 1, answers end).</summary>
    [Fact]
    public void SetController_WindowArm_MidpointIsStocksDoubleThreshold()
    {
        StudioHdr hdr = ControllerHdr(StudioConstants.STUDIO_XR, 61.01979064941406f, 148.98263549804688f);
        float ctl = 0.5f;
        float value = BoneSetupQuery.SetController(hdr, 0, 285.001220703125f, ref ctl);
        Assert.Equal(0.0f, ctl);
        Assert.Equal(61.01979064941406f, value);
    }

    /// <summary>Stock 5014: <c>start + 359.0 &gt;= end</c> sums in double.
    /// start 0.1f + 359.0 = 359.10000000149 is below end 359.1f
    /// (359.100006103515625), so stock takes the free-loop arm and 359.8
    /// stays (ctl 1, answers end). The float sum rounds to end exactly and
    /// takes the window arm, which shoves 359.8 down to -0.2 (ctl 0, answers
    /// start).</summary>
    [Fact]
    public void SetController_WindowGate_Start359IsStocksDoubleSum()
    {
        StudioHdr hdr = ControllerHdr(StudioConstants.STUDIO_XR, 0.1f, 359.1f);
        float ctl = 0.5f;
        float value = BoneSetupQuery.SetController(hdr, 0, 359.8f, ref ctl);
        Assert.Equal(1.0f, ctl);
        Assert.Equal(359.1f, value);
    }

    /// <summary>Stock 5034: <c>(1.0 - ctlValue) * start + ctlValue * end</c>
    /// is a double lerp (ctl * end stays float, widened for the add). On
    /// 10.3..77.7 the value 24.035837f (0x41c04965) round-trips exactly in
    /// double; the all-float lerp answers 0x41c04966.</summary>
    [Fact]
    public void SetController_Decode_IsStocksDoubleLerp()
    {
        StudioHdr hdr = ControllerHdr(StudioConstants.STUDIO_X, 10.3f, 77.7f);
        float ctl = 0;
        float value = BoneSetupQuery.SetController(
            hdr, 0, BitConverter.Int32BitsToSingle(0x41c04965), ref ctl);
        Assert.Equal(0x3e50afda, BitConverter.SingleToInt32Bits(ctl));
        Assert.Equal(0x41c04965, BitConverter.SingleToInt32Bits(value));
    }

    /// <summary>Characterization (stock 5024/5026's <c>/ 360.0</c>, <c>/
    /// -360.0</c> and <c>* 360.0</c>): one ulp either side of ±720 on the
    /// 0..400 free-loop controller. Both fold to 359.99994 (0x43b3fffe) under
    /// float and double alike; no input was found where they differ.</summary>
    [Fact]
    public void SetController_FreeLoopArm_RevolutionCast_Characterization()
    {
        StudioHdr hdr = ControllerHdr(StudioConstants.STUDIO_XR, 0f, 400f);
        float ctl = 0;
        float above = BoneSetupQuery.SetController(hdr, 0, BitConverter.Int32BitsToSingle(0x4433ffff), ref ctl);
        Assert.Equal(0x43b3fffe, BitConverter.SingleToInt32Bits(above));
        Assert.Equal(0x3f666664, BitConverter.SingleToInt32Bits(ctl));
        float below = BoneSetupQuery.SetController(hdr, 0, BitConverter.Int32BitsToSingle(unchecked((int)0xc4340001)), ref ctl);
        Assert.Equal(0x43b3fffe, BitConverter.SingleToInt32Bits(below));
    }

    /// <summary>Characterization (stock 5115: <c>wrap = (start + end) / 2.0
    /// + loop / 2.0</c>): both halves are exact, so the double sum rounds to
    /// float once, as the float sum does. -37.3..81.7 loop 360: wrap 202.2,
    /// 400.5 folds to 40.5, ctl 0x3f275e3a, decode 0x42220001.</summary>
    [Fact]
    public void SetPoseParameter_LoopWrap_DoubleCentre_Characterization()
    {
        StudioHdr hdr = PoseHdr(-37.3f, 81.7f, 360f);
        float ctl = 0;
        float value = BoneSetupQuery.SetPoseParameter(hdr, 0, 400.5f, ref ctl);
        Assert.Equal(0x3f275e3a, BitConverter.SingleToInt32Bits(ctl));
        Assert.Equal(0x42220001, BitConverter.SingleToInt32Bits(value));
    }

    /// <summary>Stock 5115: <c>flValue - loop * floor(...)</c> with a float
    /// argument resolves to <c>std::floor(float)</c>, and the multiply and
    /// subtract stay float (the game's own flags compile it to <c>floorf</c>,
    /// <c>mulss</c>, <c>subss</c> in debug and release). The double overload
    /// would widen <c>loop * k</c> and round once: on the non-integer loop
    /// 4.578557 (0x4092838b) over -2.015607..2.56295, 424.21057 folds to
    /// 0xbfcc3200 under float and 0xbfcc31fc under double.</summary>
    [Fact]
    public void SetPoseParameter_LoopWrap_FloorIsStocksFloatOverload()
    {
        StudioHdr hdr = PoseHdr(
            BitConverter.Int32BitsToSingle(unchecked((int)0xc000ffb6)),
            BitConverter.Int32BitsToSingle(0x40240760),
            BitConverter.Int32BitsToSingle(0x4092838b));
        float ctl = 0;
        float value = BoneSetupQuery.SetPoseParameter(hdr, 0, BitConverter.Int32BitsToSingle(0x43d41af4), ref ctl);
        Assert.Equal(0x3dbc03eb, BitConverter.SingleToInt32Bits(ctl));
        Assert.Equal(unchecked((int)0xbfcc3200), BitConverter.SingleToInt32Bits(value));
    }

    /// <summary>Stock 5499: <c>return (maxFrame + 0.01)</c> — a double sum,
    /// truncated to int. Pose 0.99 across 10/11 frames: weights 0.00999999f
    /// / 0.99f sum to 10.99f, less one is 9.98999977f; + 0.01 in double is
    /// 9.99999977 and truncates to 9, while the float sum rounds to exactly
    /// 10.0f and answers 10.</summary>
    [Fact]
    public void MaxFrame_PlusOneHundredth_IsStocksDoubleSum()
    {
        StudioHdr hdr = TwoCellHdr(10, 30f, 11, 30f);
        Assert.Equal(9, BoneSetupQuery.MaxFrame(hdr, 0, [0.99f]));
    }

    /// <summary>Stock 5603: <c>d = v0 * f + 0.5 * (v1 - v0) * f * f</c> — the
    /// 0.5 literal carries the curve term in double (v0 * f stays float,
    /// widened for the add). One block, endframe 1 of a 2-frame anim, so f
    /// is the cycle: v0 14.487257, v1 107.1764, f 0x3ebb3b94 gives
    /// d = 0x4137ed37; the all-float chain answers 0x4137ed38.</summary>
    [Fact]
    public void AnimPosition_Distance_IsStocksDoubleCurveTerm()
    {
        StudioHdr hdr = OneMoveAnim(numFrames: 2, moves:
        [
            Move(1, 14.48725700378418f, 107.17639923095703f, 0f, new Vector(1, 0, 0), new Vector(1, 0, 0)),
        ]);
        bool ok = BoneSetupQuery.AnimPosition(
            Anim0(hdr), BitConverter.Int32BitsToSingle(0x3ebb3b94), out Vector pos, out _);
        Assert.True(ok);
        Assert.Equal(0x4137ed37, BitConverter.SingleToInt32Bits(pos.X));
    }

    /// <summary>Characterization (stock 5711 <c>(v0 + v1) * 0.5</c>, 5714
    /// <c>0.5 * (v1 - v0)</c> into float parameters, 5716 <c>cpf = 1.0 /
    /// (numframes - 1)</c>): halving is exact and the int reciprocal showed
    /// no divergence for n in [2, 4000]. A 7-frame anim makes cpf the
    /// inexact 1/6.</summary>
    [Fact]
    public void FindAnimDistance_CyclesPerFrame_Characterization()
    {
        StudioHdr hdr = OneMoveAnim(numFrames: 7, moves:
        [
            Move(6, 3f, 10f, 0f, new Vector(1, 0, 0), new Vector(6.5f, 0, 0)),
        ]);
        // 0x3f0e563f = 0.5560035f, as observed before and after the mirror.
        Assert.Equal(0x3f0e563f, BitConverter.SingleToInt32Bits(
            BoneSetupQuery.FindAnimDistance(Anim0(hdr), 2.75f)));
    }
}
