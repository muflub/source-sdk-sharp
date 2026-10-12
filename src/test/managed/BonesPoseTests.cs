using System;
using System.Numerics;
using SourceSharp.Shared.Bones;
using Xunit;
using Vector = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for <see cref="CBoneSetup"/> (bone_setup.cpp 1922-2116,
/// 2125-2211, 2218-2286, 2333-2339, 2393-2457, 4457-4493). Expected values
/// are hand-derived from the oracle bodies; anim streams use the
/// (valid,total)-header idiom of <c>BonesExtractTests</c>. The
/// <c>anim_3wayblend</c> ConVar arrives per the port contract as the
/// <c>animThreeWayBlend</c> bool; every call passes it explicitly.
/// </summary>
public class BonesPoseTests
{
    private const float Scale = 1.0f / 32768.0f;
    private const int AllBones = unchecked((int)0xFFFFFFFF);

    private const int StudioAutoplay = 0x0008;  // studio.h:3081
    private const int StudioLocal = 0x0200;     // studio.h:3087

    private static StudioHdr Build(StudioHdrBuilder b)
    {
        if (!StudioHdr.TryCreate(b.Build(), b.Resolver, out StudioHdr? hdr) || hdr is null)
        {
            throw new InvalidOperationException("fixture hdr rejected");
        }

        return hdr;
    }

    private static short S(float value) => (short)Math.Round(value / Scale);
    private static short H(int valid, int total) => (short)(valid | (total << 8));

    /// <summary>Single masked bone carrying ANIMPOS X channel values
    /// 0,10,20,...,10*(n-1) over n frames (the Y/Z channels absent, the
    /// quaternion arm answers the bone's rest quat).</summary>
    private static void AddRamp(StudioHdrBuilder b, string name, int frames, float fps)
    {
        short[] pos0 = new short[1 + frames];
        pos0[0] = H(frames, frames);
        for (int i = 0; i < frames; i++)
        {
            pos0[1 + i] = (short)(10 * i); // raw shorts; bone PosScale 1 -> decode is exact
        }

        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = name,
            NumFrames = frames,
            Fps = fps,
            Bones =
            {
                new StudioHdrBuilder.BoneAnimDef
                {
                    Bone = 0,
                    Flags = StudioConstants.STUDIO_ANIM_ANIMPOS,
                    Pos0 = pos0,
                },
            },
        });
    }

    private static StudioHdrBuilder.BoneDef RootBone() =>
        new() { Name = "b0", Parent = -1, Pos = Vector.Zero, Flags = 0x1 }; // Pos zero: CalcBonePosition adds it as basePos to non-delta streams

    // ---- CalcPoseSingle (stock 1922-2116) -----------------------------------

    [Fact]
    public void CalcPoseSingle_SingleAnim_AtCycleDecodesThatFrame()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(RootBone());
        AddRamp(b, "a", frames: 5, fps: 30f); // frames 0..4 -> x 0,10,20,30,40
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f] });
        StudioHdr hdr = Build(b);

        Vector[] pos = [Vector.Zero];
        Quaternion[] q = [Quaternion.Identity];
        MstudioSeqDesc seqdesc = hdr.SeqDesc(0);
        bool result = CBoneSetup.CalcPoseSingle(
            hdr, pos, q, ref seqdesc, 0, 0.5f, [], AllBones, 0f, animThreeWayBlend: false);

        Assert.True(result);
        Assert.Equal(20f, pos[0].X, 3); // cycle 0.5 * (5-1) = frame 2
    }

    [Fact]
    public void CalcPoseSingle_RealtimeFlag_DerivesCycleFromFlTime()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(RootBone());
        AddRamp(b, "a", frames: 5, fps: 30f);
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "s",
            NumBlends = 1,
            AnimTable = [0],
            Weights = [1f],
            Flags = StudioConstants.STUDIO_REALTIME,
        });
        StudioHdr hdr = Build(b);

        // CPS = 30 / (5-1) = 7.5. flTime 0.35 -> cycle 0.625 -> frame 2.5
        // -> v1 = 20 (rec 3), v2 = 30 (rec 4) -> x = 25. The cycle argument
        // must be ignored on this arm.
        Vector[] pos = [Vector.Zero];
        Quaternion[] q = [Quaternion.Identity];
        MstudioSeqDesc seqdesc = hdr.SeqDesc(0);
        CBoneSetup.CalcPoseSingle(hdr, pos, q, ref seqdesc, 0, 0.0f, [], AllBones, 0.35f, false);
        Assert.Equal(25f, pos[0].X, 3);

        // flTime 0.2 -> cycle 0.5 -> exact frame 2 -> 20 (single-value arm).
        pos = [Vector.Zero];
        q = [Quaternion.Identity];
        CBoneSetup.CalcPoseSingle(hdr, pos, q, ref seqdesc, 0, 0.0f, [], AllBones, 0.2f, false);
        Assert.Equal(20f, pos[0].X, 3);
    }

    [Fact]
    public void CalcPoseSingle_CyclePoseFlag_ReadsSharedPoseParameter()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(RootBone());
        AddRamp(b, "a", frames: 5, fps: 30f);
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "cycle", Start = 0f, End = 1f });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "s",
            NumBlends = 1,
            AnimTable = [0],
            Weights = [1f],
            Flags = StudioConstants.STUDIO_CYCLEPOSE,
            CyclePoseIndex = 0,
        });
        StudioHdr hdr = Build(b);

        Vector[] pos = [Vector.Zero];
        Quaternion[] q = [Quaternion.Identity];
        MstudioSeqDesc seqdesc = hdr.SeqDesc(0);
        CBoneSetup.CalcPoseSingle(hdr, pos, q, ref seqdesc, 0, 0.0f, [0.75f], AllBones, 0f, false);
        Assert.Equal(30f, pos[0].X, 3); // cycle 0.75 -> frame 3

        pos = [Vector.Zero];
        q = [Quaternion.Identity];
        CBoneSetup.CalcPoseSingle(hdr, pos, q, ref seqdesc, 0, 0.0f, [0.5f], AllBones, 0f, false);
        Assert.Equal(20f, pos[0].X, 3);
    }

    [Fact]
    public void CalcPoseSingle_NonLoopingClampsOutOfRangeCycle()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(RootBone());
        AddRamp(b, "a", frames: 5, fps: 30f);
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f] }); // no STUDIO_LOOPING
        StudioHdr hdr = Build(b);

        Vector[] pos = [Vector.Zero];
        Quaternion[] q = [Quaternion.Identity];
        MstudioSeqDesc seqdesc = hdr.SeqDesc(0);
        CBoneSetup.CalcPoseSingle(hdr, pos, q, ref seqdesc, 0, 2.25f, [], AllBones, 0f, false);
        Assert.Equal(40f, pos[0].X, 3); // clamped to 1 -> last frame

        // looping wraps: 1.25 -> 0.25 -> frame 1 -> 10
        b = new StudioHdrBuilder();
        b.Bones.Add(RootBone());
        AddRamp(b, "a", frames: 5, fps: 30f);
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "s",
            NumBlends = 1,
            AnimTable = [0],
            Weights = [1f],
            Flags = StudioConstants.STUDIO_LOOPING,
        });
        hdr = Build(b);
        pos = [Vector.Zero];
        q = [Quaternion.Identity];
        seqdesc = hdr.SeqDesc(0);
        CBoneSetup.CalcPoseSingle(hdr, pos, q, ref seqdesc, 0, 1.25f, [], AllBones, 0f, false);
        Assert.Equal(10f, pos[0].X, 3);
    }

    [Fact]
    public void CalcPoseSingle_AllZerosAnim_ReturnsFalseAndWritesNothing()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(RootBone());
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "z", NumFrames = 1, Flags = StudioConstants.STUDIO_ALLZEROS });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f] });
        StudioHdr hdr = Build(b);

        Vector[] pos = [new Vector(-3f, -3f, -3f)];
        Quaternion[] q = [Quaternion.Identity];
        MstudioSeqDesc seqdesc = hdr.SeqDesc(0);
        bool result = CBoneSetup.CalcPoseSingle(hdr, pos, q, ref seqdesc, 0, 0.5f, [], AllBones, 0f, false);

        Assert.False(result);
        Assert.Equal(new Vector(-3f, -3f, -3f), pos[0]);
    }

    [Fact]
    public void CalcPoseSingle_OutOfRangeSequence_FallsBackToSequenceZero()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(RootBone());
        AddRamp(b, "a", frames: 5, fps: 30f);
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f] });
        StudioHdr hdr = Build(b);

        Vector[] pos = [Vector.Zero];
        Quaternion[] q = [Quaternion.Identity];
        MstudioSeqDesc seqdesc = hdr.SeqDesc(0);
        bool result = CBoneSetup.CalcPoseSingle(hdr, pos, q, ref seqdesc, 7, 0.5f, [], AllBones, 0f, false);

        Assert.True(result); // stock reassigns the seqdesc ref to SeqDesc(0)
        Assert.Equal(20f, pos[0].X, 3);
    }

    // ---- 2x2 cell: bilinear vs 3-way (the anim_3wayblend gate) --------------

    private static StudioHdr BuildTwoByTwo(out float[] poseParameter)
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(RootBone());
        // four constant-pose anims at X = 0, 100, 1000, 10000 (table index
        // y*2+x: [A00, A10, A01, A11])
        foreach ((string name, float x) cell in new (string, float)[] { ("A00", 0f), ("A10", 100f), ("A01", 1000f), ("A11", 10000f) })
        {
            b.Anims.Add(new StudioHdrBuilder.AnimDef
            {
                Name = cell.name,
                NumFrames = 1,
                Bones =
                {
                    new StudioHdrBuilder.BoneAnimDef
                    {
                        Bone = 0,
                        Flags = StudioConstants.STUDIO_ANIM_RAWPOS,
                        RawPosition = new Vector(cell.x, 0f, 0f),
                    },
                },
            });
        }

        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "p0", Start = 0f, End = 1f });
        b.PoseParams.Add(new StudioHdrBuilder.PoseParamDef { Name = "p1", Start = 0f, End = 1f });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "g",
            NumBlends = 4,
            GroupSize0 = 2,
            GroupSize1 = 2,
            AnimTable = [0, 1, 2, 3],
            Weights = [1f],
            ParamIndex0 = 0,
            ParamIndex1 = 1,
            ParamStart = [0f, 0f],
            ParamEnd = [1f, 1f],
        });

        poseParameter = [0.3f, 0.4f];
        return Build(b);
    }

    private static float RunCell(StudioHdr hdr, float[] poseParameter, bool threeWay)
    {
        Vector[] pos = [Vector.Zero];
        Quaternion[] q = [Quaternion.Identity];
        MstudioSeqDesc seqdesc = hdr.SeqDesc(0);
        CBoneSetup.CalcPoseSingle(hdr, pos, q, ref seqdesc, 0, 0f, poseParameter, AllBones, 0f, threeWay);
        return pos[0].X;
    }

    [Fact]
    public void CalcPoseSingle_MidCell_BilinearWhenThreeWayOff()
    {
        StudioHdr hdr = BuildTwoByTwo(out float[] pp);

        // ((A00,A10)@0.3 = 30) blended to ((A01,A11)@0.3 = 3700) at s1 = 0.4
        // -> 30*0.6 + 3700*0.4 = 1498
        Assert.Equal(1498f, RunCell(hdr, pp, threeWay: false), 2);
    }

    [Fact]
    public void CalcPoseSingle_MidCell_TriangleBlendWhenThreeWayOn()
    {
        StudioHdr hdr = BuildTwoByTwo(out float[] pp);

        // even cell, s0(0.3) <= s1(0.4): arm C (A11, A01, A00), weights
        // (0.3, 0.1, 0.6): A11 blended to A01 at 0.1/0.4 = 0.25 -> 7750,
        // then to A00 at 0.6 -> 7750*0.4 = 3100.
        Assert.Equal(3100f, RunCell(hdr, pp, threeWay: true), 2);

        // the gate observably changes the arm taken
        Assert.NotEqual(RunCell(hdr, pp, threeWay: false), RunCell(hdr, pp, threeWay: true));
    }

    // ---- AddSequenceLayers (stock 2125-2211) --------------------------------

    // The fixtures use ONE sequence: the shared StudioHdrBuilder currently
    // interleaves each seqdesc's payload into the table between records, so
    // only record 0 reads back correctly (reported to Main; multi-seq
    // fixtures wait on that fix). Autolayers are self-referencing
    // (ISequence 0 -> iRelativeSeq -> the same sequence); their windows are
    // chosen so AccumulatePose's own AddSequenceLayers call (stock 2449)
    // lands outside the window and the composite terminates at depth 2.

    /// <summary>One bone, one 5-frame ramp anim (x = 10 per frame), one
    /// plain sequence — the base fixture for the AccumulatePose facts.</summary>
    private static StudioHdr BuildLayeredHdr(out StudioHdrBuilder builder)
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(RootBone()); // rest pos zero; base pose set by each test
        AddRamp(b, "ramp", frames: 5, fps: 30f); // x at frame f: 10f
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "plain",
            NumBlends = 1,
            AnimTable = [0],
            Weights = [1f],
        });
        builder = b;
        return Build(b);
    }

    /// <summary>The plain hdr with one self-referencing autolayer on seq 0
    /// and a 13-frame ramp (x = 10 per frame, so cycles at k/12 land on
    /// exact frames).</summary>
    private static StudioHdr BuildSelfLayeredHdr(int layerFlags,
        float start, float peak, float tail, float end, int seqFlags = 0)
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(RootBone());
        AddRamp(b, "ramp", frames: 13, fps: 30f);
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Flags = seqFlags,
            Label = "self",
            NumBlends = 1,
            AnimTable = [0],
            Weights = [1f],
            AutoLayers =
            {
                new StudioHdrBuilder.AutoLayerDef
                {
                    ISequence = 0,
                    Flags = layerFlags,
                    Start = start,
                    Peak = peak,
                    Tail = tail,
                    End = end,
                },
            },
        });
        return Build(b);
    }

    [Fact]
    public void AddSequenceLayers_XFadeTailRamp_CompositesLayerAtRatioWeight()
    {
        // Self-layer window [0.5,0.75), peak 0.5, tail 0.6. At parent cycle
        // 0.7 the tail arm gives s = (0.75-0.7)/(0.75-0.6) = 1/3 and the
        // XFADE weight (s*flW)/(1-flW+s*flW) = 0.25 at flW 0.5. The layer
        // cycle (0.7-0.5)/0.25 = 0.8 falls outside the window, so the
        // nested AddSequenceLayers inside AccumulatePose skips: the
        // composite is exactly one SlerpBones of the base pose with the
        // scratch pose at cycle 0.8 (x = 0.8*12*10 = 96 on the 13-frame ramp).
        StudioHdr hdr = BuildSelfLayeredHdr(0x0080, 0.5f, 0.5f, 0.6f, 0.75f);
        CBoneSetup cb = new(hdr, AllBones, []);

        Vector[] pos = [new Vector(1f, 2f, 3f)];
        Quaternion[] q = [Quaternion.Identity];
        cb.AddSequenceLayers(pos, q, hdr.SeqDesc(0), 0, 0.7f, 0.5f, 0f, null, animThreeWayBlend: false);

        const float w = 1f / 6f / (1f / 2f + 1f / 6f); // = 0.25
        Assert.Equal(0.25f, w, 5); // fixture arithmetic sanity
        Assert.Equal(1f * (1 - w) + 96f * w, pos[0].X, 3);
        Assert.Equal(2f * (1 - w), pos[0].Y, 3);
        Assert.Equal(3f * (1 - w), pos[0].Z, 3);
        // a plain (non-XFADE) ramp weight flW*s = 1/6 would answer a
        // different x — the ratio arm is observable, not just "some blend".
        Assert.True(MathF.Abs(pos[0].X - (1f * (1 - 1f / 6f) + 96f / 6f)) > 1f);
    }

    [Fact]
    public void AddSequenceLayers_OutsideWindow_SkipsLayer()
    {
        StudioHdr hdr = BuildSelfLayeredHdr(0, 0.5f, 0.6f, 0.7f, 0.8f);
        CBoneSetup cb = new(hdr, AllBones, []);

        Vector[] pos = [new Vector(1f, 2f, 3f)];
        Quaternion[] q = [Quaternion.Identity];
        cb.AddSequenceLayers(pos, q, hdr.SeqDesc(0), 0, 0.2f, 1f, 0f, null, animThreeWayBlend: false);

        Assert.Equal(new Vector(1f, 2f, 3f), pos[0]); // cycle below start: skipped
    }

    [Fact]
    public void AddSequenceLayers_SplineFlag_RampsTheWeightCurve()
    {
        // Self-layer window [0.5,1), peak 0.625, tail 0.75, STUDIO_AL_SPLINE.
        // At parent cycle 0.55 the peak arm gives s = 0.4, the spline arm
        // 3*0.4^2 - 2*0.4^3 = 0.352, weight = flW * s = 0.176; the layer
        // cycle (0.55-0.5)/0.5 = 0.1 exits the window so the composite is
        // one SlerpBones with the scratch pose at cycle 0.1 (x = 12).
        StudioHdr hdr = BuildSelfLayeredHdr(0x0040, 0.5f, 0.625f, 0.75f, 1f);
        CBoneSetup cb = new(hdr, AllBones, []);

        Vector[] pos = [new Vector(1f, 2f, 3f)];
        Quaternion[] q = [Quaternion.Identity];
        cb.AddSequenceLayers(pos, q, hdr.SeqDesc(0), 0, 0.55f, 0.5f, 0f, null, animThreeWayBlend: false);

        float w = 0.5f * BoneSetupMath.SimpleSpline(0.4f);
        Assert.Equal(1f * (1 - w) + 12f * w, pos[0].X, 3);
        // linear s = 0.4 (no spline) would answer x 3.2, not 2.936 —
        // the spline arm is observable.
        Assert.True(MathF.Abs(pos[0].X - (1f * (1 - 0.2f) + 12f * 0.2f)) > 0.01f);
    }

    // ---- AddLocalLayers (stock 2218-2286) -----------------------------------

    [Fact]
    public void AddLocalLayers_LocalSequence_CompositesLayerThroughRecursion()
    {
        // The STUDIO_AL_LOCAL arm of the autolayer loop runs only for a
        // STUDIO_LOCAL sequence, and AddSequenceLayers skips those layers.
        // Window [0.5,0.75), tail 0.6: at cycle 0.7 the tail arm gives
        // s = 1/3; the call from AccumulatePose passes flWeight 1.0 so the
        // plain arm weighs s. Layer cycle (0.7-0.5)/0.25 = 0.8 lands on
        // frame 9.6 of the 13-frame ramp (x = 96), and the nested
        // AddLocalLayers there skips (0.8 >= end) — so the scratch pose is
        // the cycle-0.7 ramp pose 84 slerped toward 96 at 1/3 = 88, and the
        // base pose slerps toward that at the outer weight 0.5.
        StudioHdr hdr = BuildSelfLayeredHdr(0x1000, 0.5f, 0.5f, 0.6f, 0.75f,
            seqFlags: 0x0200); // STUDIO_LOCAL
        CBoneSetup cb = new(hdr, AllBones, []);

        Vector[] pos = [new Vector(1f, 2f, 3f)];
        Quaternion[] q = [Quaternion.Identity];
        cb.AccumulatePose(pos, q, 0, 0.7f, 0.5f, 0f, null, animThreeWayBlend: false);

        Assert.Equal(1f + (84f + (96f - 84f) / 3f - 1f) * 0.5f, pos[0].X, 3); // 44.5
        Assert.Equal(1f, pos[0].Y, 3);
        Assert.Equal(1.5f, pos[0].Z, 3);
        // Without the recursion the answer is the bare outer slerp 42.5;
        // if AddSequenceLayers stopped skipping AL_LOCAL layers a second
        // blend at 1/6 would land on top. Both numbers are >1 away.
        Assert.True(MathF.Abs(pos[0].X - 42.5f) > 1f);
        Assert.True(MathF.Abs(pos[0].X - 57.4f) > 1f);
    }

    [Fact]
    public void AddLocalLayers_NonLocalSequence_LeavesScratchUntouched()
    {
        // Same fixture, sequence without STUDIO_LOCAL: AddLocalLayers
        // returns before its loop, so the composite is the bare outer
        // slerp of the base pose with the cycle-0.7 ramp pose (84) at 0.5.
        StudioHdr hdr = BuildSelfLayeredHdr(0x1000, 0.5f, 0.5f, 0.6f, 0.75f,
            seqFlags: 0);
        CBoneSetup cb = new(hdr, AllBones, []);

        Vector[] pos = [new Vector(1f, 2f, 3f)];
        Quaternion[] q = [Quaternion.Identity];
        cb.AccumulatePose(pos, q, 0, 0.7f, 0.5f, 0f, null, animThreeWayBlend: false);

        Assert.Equal(42.5f, pos[0].X, 3);
        Assert.Equal(1f, pos[0].Y, 3);
        // the local-sequence gate is observable: a broken gate composites
        // the layer and answers 44.5.
        Assert.True(MathF.Abs(pos[0].X - 44.5f) > 1f);
    }

    [Fact]
    public void AddLocalLayers_NoBlendFlag_WeightIsSNotFlWeightTimesS()
    {
        // Direct call (AccumulatePose always passes flWeight 1.0, where
        // NOBLEND and plain agree): STUDIO_AL_NOBLEND weighs the recursion
        // by s alone. Tail arm at 0.7 -> s = 1/3, layer cycle 0.8 -> scratch
        // x = 96; pos = 1*(2/3) + 96*(1/3).
        StudioHdr hdr = BuildSelfLayeredHdr(0x1000 | 0x0200, 0.5f, 0.5f, 0.6f, 0.75f,
            seqFlags: 0x0200);
        CBoneSetup cb = new(hdr, AllBones, []);

        Vector[] pos = [new Vector(1f, 2f, 3f)];
        Quaternion[] q = [Quaternion.Identity];
        cb.AddLocalLayers(pos, q, hdr.SeqDesc(0), 0, 0.7f, 0.5f, 0f, null, animThreeWayBlend: false);

        const float w = 1f / 3f;
        Assert.Equal(1f * (1 - w) + 96f * w, pos[0].X, 3); // 32.667
        Assert.Equal(2f * (1 - w), pos[0].Y, 3);
        // the plain arm (flW*s = 1/6) answers 16.833 — NOBLEND is observable.
        Assert.True(MathF.Abs(pos[0].X - (1f * (1 - 1f / 6f) + 96f / 6f)) > 1f);
    }

    [Fact]
    public void AddLocalLayers_XFadeTail_WeightIsBlendedRatio()
    {
        // STUDIO_AL_XFADE above the tail: layerWeight =
        // (s*flW)/(1-flW+s*flW) = (1/6)/(2/3) = 0.25 at cycle 0.7, flW 0.5.
        // Recursion at layer cycle 0.8 slerps the base toward x = 96 by
        // 0.25: pos = 1*0.75 + 96*0.25.
        StudioHdr hdr = BuildSelfLayeredHdr(0x1000 | 0x0080, 0.5f, 0.5f, 0.6f, 0.75f,
            seqFlags: 0x0200);
        CBoneSetup cb = new(hdr, AllBones, []);

        Vector[] pos = [new Vector(1f, 2f, 3f)];
        Quaternion[] q = [Quaternion.Identity];
        cb.AddLocalLayers(pos, q, hdr.SeqDesc(0), 0, 0.7f, 0.5f, 0f, null, animThreeWayBlend: false);

        Assert.Equal(1f * 0.75f + 96f * 0.25f, pos[0].X, 3); // 24.75
        Assert.Equal(2f * 0.75f, pos[0].Y, 3);
        // plain arm 1/6 -> 16.833, NOBLEND arm 1/3 -> 32.667: the ratio is
        // its own point, not either linear arm.
        Assert.True(MathF.Abs(pos[0].X - (1f * (1 - 1f / 6f) + 96f / 6f)) > 1f);
        Assert.True(MathF.Abs(pos[0].X - (1f * (1 - 1f / 3f) + 96f / 3f)) > 1f);
    }

    [Fact]
    public void AddLocalLayers_SplinePeakRamp_CurvesTheWeight()
    {
        // Window [0.5,1), peak 0.625, tail 0.75: at cycle 0.55 the peak arm
        // gives s = 0.4, SPLINE curves it to SS(0.4) = 0.352, NOBLEND uses
        // it as the weight, layer cycle (0.55-0.5)/0.5 = 0.1 -> scratch x
        // 12 (and the nested AddLocalLayers skips below start there).
        // pos = 1*(1-0.352) + 12*0.352.
        StudioHdr hdr = BuildSelfLayeredHdr(0x1000 | 0x0040 | 0x0200, 0.5f, 0.625f, 0.75f, 1f,
            seqFlags: 0x0200);
        CBoneSetup cb = new(hdr, AllBones, []);

        Vector[] pos = [new Vector(1f, 2f, 3f)];
        Quaternion[] q = [Quaternion.Identity];
        cb.AddLocalLayers(pos, q, hdr.SeqDesc(0), 0, 0.55f, 0.5f, 0f, null, animThreeWayBlend: false);

        const float w = 0.352f;
        Assert.Equal(1f * (1 - w) + 12f * w, pos[0].X, 3); // 4.872
        Assert.Equal(2f * (1 - w), pos[0].Y, 3);
        // no spline (w = 0.4) -> 5.4; spline without NOBLEND (w = flW*s =
        // 0.176) -> 2.936. Both are away from 4.872.
        Assert.True(MathF.Abs(pos[0].X - (1f * 0.6f + 12f * 0.4f)) > 0.1f);
        Assert.True(MathF.Abs(pos[0].X - (1f * (1 - 0.176f) + 12f * 0.176f)) > 1f);
    }

    // ---- AccumulatePose (stock 2393-2457) ------------------------------------

    [Fact]
    public void AccumulatePose_WithZeroWeight_LeavesPoseUntouched()
    {
        StudioHdr hdr = BuildLayeredHdr(out _); // seq0: no layers, seq1 has one
        CBoneSetup cb = new(hdr, AllBones, []);

        Vector[] pos = [new Vector(1f, 2f, 3f)];
        Quaternion[] q = [Quaternion.Identity];
        cb.AccumulatePose(pos, q, 0, 0.5f, 0f, 0f, null, animThreeWayBlend: false);

        // SlerpBones early-returns at weight 0: the base pose survives.
        Assert.Equal(new Vector(1f, 2f, 3f), pos[0]);
        Assert.Equal(Quaternion.Identity, q[0]);
    }

    [Fact]
    public void AccumulatePose_NegativeSequence_IsNoOp()
    {
        StudioHdr hdr = BuildLayeredHdr(out _);
        CBoneSetup cb = new(hdr, AllBones, []);

        Vector[] pos = [new Vector(1f, 2f, 3f)];
        Quaternion[] q = [Quaternion.Identity];
        cb.AccumulatePose(pos, q, -1, 0.5f, 1f, 0f, null, animThreeWayBlend: false);

        Assert.Equal(new Vector(1f, 2f, 3f), pos[0]);
    }

    [Fact]
    public void AccumulatePose_MidWeight_SlerpsScratchOverBase()
    {
        StudioHdr hdr = BuildLayeredHdr(out _);
        CBoneSetup cb = new(hdr, AllBones, []);

        Vector[] pos = [new Vector(1f, 2f, 3f)];
        Quaternion[] q = [Quaternion.Identity];
        cb.AccumulatePose(pos, q, 0, 0.5f, 0.5f, 0f, null, animThreeWayBlend: false);

        // seq0 pose at cycle 0.5 = x 20; slerp arm at s2 = 0.5*1:
        Assert.Equal(1f * 0.5f + 20f * 0.5f, pos[0].X, 3);
        Assert.Equal(2f * 0.5f, pos[0].Y, 3);
    }

    [Fact]
    public void AccumulatePose_ClampsWeightAboveOne()
    {
        StudioHdr hdr = BuildLayeredHdr(out _);
        CBoneSetup cb = new(hdr, AllBones, []);

        Vector[] pos = [new Vector(1f, 2f, 3f)];
        Quaternion[] q = [Quaternion.Identity];
        cb.AccumulatePose(pos, q, 0, 0.5f, 2.5f, 0f, null, animThreeWayBlend: false);

        Assert.Equal(20f, pos[0].X, 3); // full copy of the seq pose
    }

    [Fact]
    public void AccumulatePose_LocalSequence_SeedsScratchFromRestPose()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(RootBone()); // rest (1,2,3), bone 0 carries the record
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b1", Parent = 0, Pos = new Vector(4f, 5f, 6f), Flags = 0x1 });
        AddRamp(b, "ramp", frames: 5, fps: 30f); // records bone 0 only
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "loc",
            NumBlends = 1,
            AnimTable = [0],
            Weights = [1f, 1f],
            Flags = StudioLocal,
        });
        StudioHdr hdr = Build(b);
        CBoneSetup cb = new(hdr, AllBones, []);

        Vector[] pos = [Vector.Zero, Vector.Zero];
        Quaternion[] q = [Quaternion.Identity, Quaternion.Identity];
        cb.AccumulatePose(pos, q, 0, 0.5f, 1f, 0f, null, animThreeWayBlend: false);

        Assert.Equal(20f, pos[0].X, 3); // the recorded bone animates
        Assert.Equal(new Vector(4f, 5f, 6f), pos[1]); // the record-less bone: rest pose
    }

    [Fact]
    public void AccumulatePose_UsesCtorBoneMask()
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(RootBone());
        AddRamp(b, "ramp", frames: 5, fps: 30f);
        // seq weight 1 but the ctor mask excludes the bone from CalcAnimation
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s", NumBlends = 1, AnimTable = [0], Weights = [1f] });
        StudioHdr hdr = Build(b);
        CBoneSetup cb = new(hdr, 0x2 /* bone flags 0x1: excluded */, new float[StudioConstants.MAXSTUDIOBONES]);

        Vector[] pos = [new Vector(1f, 2f, 3f)];
        Quaternion[] q = [Quaternion.Identity];
        cb.AccumulatePose(pos, q, 0, 0.5f, 1f, 0f, null, animThreeWayBlend: false);

        // mask gates CalcAnimation (record skipped -> rest-pose arm) and
        // SlerpBones (weight 0 -> untouched): the base pose survives.
        Assert.Equal(new Vector(1f, 2f, 3f), pos[0]);
    }

    // ---- CalcAutoplaySequences (stock 4457-4493) -----------------------------

    // Single-sequence autoplay fixture: the flag gate must read the
    // autoplay record itself, and the shared StudioHdrBuilder currently
    // misaddresses every seqdesc past record 0 (payloads interleaved;
    // reported to Main), so the autoplay sequence is record 0.
    private static StudioHdr BuildAutoplayHdr(int flags = StudioAutoplay)
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(RootBone());
        AddRamp(b, "spark", frames: 11, fps: 30f); // cps = 30/(11-1) = 3
        b.Sequences.Add(new StudioHdrBuilder.SeqDef
        {
            Label = "spark",
            NumBlends = 1,
            AnimTable = [0],
            Weights = [1f],
            Flags = flags,
        });
        return Build(b);
    }

    [Fact]
    public void CalcAutoplaySequences_WeighsInAtCycleDerivedFromRealTime()
    {
        StudioHdr hdr = BuildAutoplayHdr();
        CBoneSetup cb = new(hdr, AllBones, []);

        Vector[] pos = [new Vector(1f, 2f, 3f)];
        Quaternion[] q = [Quaternion.Identity];
        cb.CalcAutoplaySequences(pos, q, 0.5f, null, animThreeWayBlend: false);

        // autoplay cps = 30/(11-1) = 3; cycle = frac(0.5*3) = 0.5 -> frame 5
        // of the 11-frame ramp -> x = 50; weight 1 copies the scratch pose.
        Assert.Equal(50f, pos[0].X, 3);
    }

    [Fact]
    public void CalcAutoplaySequences_DifferentRealTime_MovesTheCycle()
    {
        StudioHdr hdr = BuildAutoplayHdr();
        CBoneSetup cb = new(hdr, AllBones, []);

        // flRealTime 0.25 -> cycle frac(0.75) = 0.75 -> frame 7.5 -> 75
        Vector[] pos = [new Vector(1f, 2f, 3f)];
        Quaternion[] q = [Quaternion.Identity];
        cb.CalcAutoplaySequences(pos, q, 0.25f, null, animThreeWayBlend: false);
        Assert.Equal(75f, pos[0].X, 3);
    }

    [Fact]
    public void CalcAutoplaySequences_SkipsNonAutoplaySequences()
    {
        // Same hdr without the autoplay flag: the list is empty and the
        // base pose survives.
        StudioHdr hdr = BuildAutoplayHdr(0);
        CBoneSetup cb = new(hdr, AllBones, []);

        Vector[] pos = [new Vector(1f, 2f, 3f)];
        Quaternion[] q = [Quaternion.Identity];
        cb.CalcAutoplaySequences(pos, q, 0.25f, null, animThreeWayBlend: false);

        Assert.Equal(new Vector(1f, 2f, 3f), pos[0]);
    }
    // ---- ctor / InitPose thunk (stock 2305-2308, 2333-2339) ------------------

    [Fact]
    public void CBoneSetup_Ctor_ExposesItsImmutableState_AndInitPoseThunks()
    {
        StudioHdr hdr = BuildAutoplayHdr();
        float[] poseParam = [];
        CBoneSetup cb = new(hdr, 0x1, poseParam);

        Assert.Same(hdr, cb.StudioHdr);
        Assert.Equal(0x1, cb.BoneMask);
        Assert.Same(poseParam, cb.PoseParameter);

        Vector[] pos = [new Vector(55f, 55f, 55f)];
        Quaternion[] q = [new Quaternion(9f, 9f, 9f, 9f)];
        cb.InitPose(pos, q);
        Assert.Equal(Vector.Zero, pos[0]); // rest pose through the thunk (root Pos zero)
    }
}
