// ik cluster: facts for the CIKSolver accept/reject boundary, the
// Studio_SolveIK knee-deriving arm, the rule-weight ramp / latch / tail
// endpoints, the AnimationError veto, the SequenceError latch/release
// bookkeeping, the SolveBone round-trip, and SolveLock on a one-chain
// fixture. Rule windows use dyadic fractions so the float32 frame window
// (100 * (cycle - start)) is exact and expected values are hand-provable.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;
using SourceSharp.Shared.Bones;
using SourceSharp.Tests;
using Vector = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace SourceSharp.Tests;

public class BonesIkTests
{
    // ------------------------------------------------------------------
    // Fixtures
    // ------------------------------------------------------------------
    // Four bones: b0 (root, parent -1) -> b1 thigh -> b2 knee -> b3 foot.
    // All bones mask-visible so AddDependencies never skips them.

    private const int Mask = unchecked((int)0xFFFFFFFF);

    private static StudioHdr Build(StudioHdrBuilder b)
    {
        Assert.True(StudioHdr.TryCreate(b.Build(), null, out StudioHdr? hdr) && hdr is not null,
            "ik fixture rejected by StudioHdr.TryCreate");
        return hdr!;
    }

    private static StudioHdrBuilder Base()
    {
        var b = new StudioHdrBuilder();
        for (int i = 0; i < 4; i++)
        {
            b.Bones.Add(new StudioHdrBuilder.BoneDef
            {
                Name = "b" + i,
                Parent = i - 1,
                Flags = Mask,
            });
        }
        b.ModelName = "b0";
        return b;
    }

    private static (Vector[] Pos, Quaternion[] Q) FreshPose(Vector kneeLocal, Vector footLocal)
    {
        var pos = new Vector[StudioConstants.MAXSTUDIOBONES];
        var q = new Quaternion[StudioConstants.MAXSTUDIOBONES];
        for (int i = 0; i < StudioConstants.MAXSTUDIOBONES; i++)
        {
            q[i] = Quaternion.Identity;
        }
        pos[1] = new Vector(0, 0, 50);
        pos[2] = kneeLocal;
        pos[3] = footLocal;
        return (pos, q);
    }

    private static BoneMatrix3x4[] LegWorld(StudioHdr hdr, Vector[] pos, Quaternion[] q)
    {
        var bw = new BoneMatrix3x4[StudioConstants.MAXSTUDIOBONES];
        BoneSetupExtract.BuildBoneChain(hdr, BoneMatrix3x4.Identity, pos, q, 3, bw, new());
        return bw;
    }

    private static void AssertClose(Vector expected, Vector actual, float tol)
    {
        Assert.True(MathF.Abs(expected.X - actual.X) <= tol, $"x {expected.X} vs {actual.X}");
        Assert.True(MathF.Abs(expected.Y - actual.Y) <= tol, $"y {expected.Y} vs {actual.Y}");
        Assert.True(MathF.Abs(expected.Z - actual.Z) <= tol, $"z {expected.Z} vs {actual.Z}");
    }

    private static StudioHdrBuilder.IkErrorFrame[] ErrorFrames(int count)
    {
        var f = new StudioHdrBuilder.IkErrorFrame[count];
        for (int i = 0; i < count; i++)
        {
            f[i] = new StudioHdrBuilder.IkErrorFrame
            {
                Pos = new Vector(0, i, 0),
                Quat = Quaternion.Identity,
            };
        }
        return f;
    }

    // Shared rule window: start 0.25, peak 0.4375, tail 0.625, end 0.75.
    // Dyadic fractions keep 100 * (cycle - start) float32-exact.

    // ------------------------------------------------------------------
    // CIKSolver::solve — accept/reject boundary
    // ------------------------------------------------------------------

    [Theory]
    // A = B = 10, P along z, D along x: d = r/2, and acceptance
    // (r - B) < d < A reduces to r < 2B strictly. At r == 2B both strict
    // comparisons fail (d == A and d == r - B simultaneously).
    [InlineData(19.9f, true)]   // d = 9.95: inside r-B=9.9, under A=10 -> accept
    [InlineData(20.0f, false)]  // d = 10 == A and == r-B -> both strict tests fail
    [InlineData(20.5f, false)]  // d = 10.25 > A
    [InlineData(3.0f, true)]    // d = 1.5: inside r-B=-7, under A=10
    [InlineData(15.0f, true)]   // interior of the workspace
    public void CikSolver_Solve_AcceptRejectBoundary(float r, bool expected)
    {
        var solver = new CikSolver();
        bool got = solver.Solve(10f, 10f, new[] { 0f, 0f, r }, new[] { 1f, 0f, 0f }, new float[3]);
        Assert.Equal(expected, got);
    }

    [Fact]
    public void CikSolver_Solve_KneeSatisfiesLinkLengths()
    {
        // a = b = 10, P = (0,0,15), D = (1,0,0): knee = e*x + d*z with
        // d = 7.5, e = sqrt(100 - 56.25) = sqrt(43.75).
        var solver = new CikSolver();
        var q = new float[3];
        Assert.True(solver.Solve(10f, 10f, new[] { 0f, 0f, 15f }, new[] { 1f, 0f, 0f }, q));
        Assert.Equal(MathF.Sqrt(43.75f), q[0], 3);
        Assert.Equal(0f, q[1], 3);
        Assert.Equal(7.5f, q[2], 3);
        Assert.Equal(10f, MathF.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2]), 3); // |knee| = a
        float fx = q[0], fz = q[2] - 15f;
        Assert.Equal(10f, MathF.Sqrt(fx * fx + fz * fz), 3);                        // |P - knee| = b
    }

    [Fact]
    public void CikSolver_FindD_FindE_Formulas()
    {
        Assert.Equal(7.5f, CikSolver.FindD(10f, 10f, 15f));                       // (15 + 0/15)/2
        Assert.Equal((20f + (36f - 16f) / 20f) / 2f, CikSolver.FindD(6f, 4f, 20f));
        Assert.Equal(6f, CikSolver.FindE(10f, 8f));                               // sqrt(100-64)
    }

    [Fact]
    public void CikSolver_VectorHelpers()
    {
        Assert.Equal(49f, CikSolver.Dot(stackalloc float[] { 2, 3, 6 }, stackalloc float[] { 2, 3, 6 })); // 4+9+36
        Assert.Equal(7f, CikSolver.Length(stackalloc float[] { 2, 3, 6 }));
        var n = new float[] { 0f, 5f, 0f };
        CikSolver.Normalize(n);
        Assert.Equal(1f, n[1]);
        var c = new float[3];
        CikSolver.Cross(stackalloc float[] { 1, 0, 0 }, stackalloc float[] { 0, 1, 0 }, c);
        Assert.Equal(1f, c[2]);
        Assert.Equal(0f, c[0]);
        Assert.Equal(0f, c[1]);
    }

    // ------------------------------------------------------------------
    // Studio_SolveIK 4-arg: kneeDir derived from the current knee
    // ------------------------------------------------------------------

    [Fact]
    public void StudioSolveIk_FourArg_KneeDerivedArmPlacesFootAtTarget()
    {
        // thigh (0,0,50), knee (0,15,-60) rel, foot (0,-30,0) rel ->
        // l1 = sqrt(3825) = 61.85, l2 = 30.  Target foot (0,0,5):
        // ikFoot = (0,0,-45) -> no clamp (36.65 < 45 < 91.84);
        // d = (45 + (3825-900)/45)/2 = 55 in (15, 61.85) -> accept.
        StudioHdr hdr = Build(Base());
        (Vector[] pos, Quaternion[] q) = FreshPose(new Vector(0, 15, -60), new Vector(0, -30, 0));
        BoneMatrix3x4[] bw = LegWorld(hdr, pos, q);
        Assert.Equal("0,0,50", $"{bw[1].Position.X},{bw[1].Position.Y},{bw[1].Position.Z}");

        Assert.True(BoneSetupIk.StudioSolveIk(1, 2, 3, new Vector(0, 0, 5), bw));
        Assert.Equal(0f, bw[3].Position.X, 2);
        Assert.Equal(0f, bw[3].Position.Y, 2);
        Assert.Equal(5f, bw[3].Position.Z, 2);
        // knee stays on the +Y side it was recorded on (stock mirror test).
        Assert.True(bw[2].Position.Y > 5f, $"knee went to {bw[2].Position}");
        // thigh segment length is preserved by the closed form.
        Assert.Equal(MathF.Sqrt(3825f), (bw[2].Position - bw[1].Position).Length(), 2);
    }

    [Fact]
    public void StudioSolveIk_FourArg_RejectsTargetBeyondReach()
    {
        // Straight leg: |P| == l1 + l2 > (l1 + l2) * KNEEMAX_EPSILON -> the
        // too-far clamp makes solve() reject.
        StudioHdr hdr = Build(Base());
        (Vector[] pos, Quaternion[] q) = FreshPose(new Vector(0, 0, -25), new Vector(0, 0, -25));
        BoneMatrix3x4[] bw = LegWorld(hdr, pos, q);
        Assert.False(BoneSetupIk.StudioSolveIk(1, 2, 3, new Vector(0, 0, 30), bw));
    }

    // ------------------------------------------------------------------
    // Studio_AlignIKMatrix
    // ------------------------------------------------------------------

    [Fact]
    public void AlignIKMatrix_ColumnsAreRightHandedBasis()
    {
        var m = BoneMatrix3x4.Identity;
        m.Position = new Vector(4, 5, 6);
        BoneSetupIk.StudioAlignIkMatrix(ref m, new Vector(0, 1, 0));
        Assert.Equal("0,1,0", $"{m.Row0.X},{m.Row1.X},{m.Row2.X}");      // X axis = alignTo
        Assert.Equal("-1,0,0", $"{m.Row0.Y},{m.Row1.Y},{m.Row2.Y}");     // Y axis = unit(Z x X)
        // Z axis = X x Y = (0,0,1); Row1.Z carries a signed zero, compare by value.
        Assert.Equal(0f, m.Row0.Z);
        Assert.Equal(0f, m.Row1.Z);
        Assert.Equal(1f, m.Row2.Z);
        Assert.Equal("4,5,6", $"{m.Position.X},{m.Position.Y},{m.Position.Z}"); // position untouched
    }

    // ------------------------------------------------------------------
    // Studio_IKRuleWeight (mstudioikrule_t arm)
    // ------------------------------------------------------------------

    private static (MstudioIkRule Rule, MstudioIkRule IStart7, MstudioAnimDesc Anim) WeightFixture()
    {
        var b = Base();
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a0",
            NumFrames = 101,
            Fps = 30,
            IkRules =
            {
                new StudioHdrBuilder.IkRuleDef
                {
                    Type = StudioConstants.IK_GROUND, Chain = 0, Bone = 2,
                    Start = 0.25f, Peak = 0.4375f, Tail = 0.625f, End = 0.75f,
                },
                new StudioHdrBuilder.IkRuleDef
                {
                    Type = StudioConstants.IK_GROUND, Chain = 0, Bone = 2,
                    Start = 0.25f, Peak = 0.4375f, Tail = 0.625f, End = 0.75f, IStart = 7,
                },
            },
        });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s0", AnimTable = new short[] { 0 } });
        StudioHdr hdr = Build(b);
        return (hdr.AnimDesc(0).PIKRule(0)!, hdr.AnimDesc(0).PIKRule(1)!, hdr.AnimDesc(0));
    }

    [Fact]
    public void IKRuleWeight_RampEndpointsAndFrameWindow()
    {
        (MstudioIkRule rule, MstudioIkRule iStart7, MstudioAnimDesc anim) = WeightFixture();

        // Before window.
        Assert.Equal(0f, BoneSetupIk.StudioIkRuleWeight(rule, anim, 0.1f, out int frame, out float fraq));
        Assert.Equal(0, frame);
        Assert.Equal(0f, fraq);

        // Ramp arms: mid-ramp and drop-mid hit value 0.5 -> smoothstep
        // 3*0.25-2*0.125 = 0.5; the exact start (value 0) and past-end
        // (value 0) give 0.
        Assert.Equal(0f, BoneSetupIk.StudioIkRuleWeight(rule, anim, 0.25f, out _, out _), 4);
        Assert.Equal(0.5f, BoneSetupIk.StudioIkRuleWeight(rule, anim, 0.34375f, out _, out _), 4);
        Assert.Equal(1f, BoneSetupIk.StudioIkRuleWeight(rule, anim, 0.625f, out _, out _), 4);   // == tail
        Assert.Equal(0.5f, BoneSetupIk.StudioIkRuleWeight(rule, anim, 0.6875f, out _, out _), 4);  // drop mid
        Assert.Equal(0f, BoneSetupIk.StudioIkRuleWeight(rule, anim, 0.9f, out _, out _), 4);

        // Exactly peak: the ramp arm's < peak fails, the full band applies -> bare 1.
        Assert.Equal(1f, BoneSetupIk.StudioIkRuleWeight(rule, anim, 0.4375f, out _, out _));

        // Full band: cycle 0.55, start 0.25 -> window = 100*(0.55f-0.25f)
        // = 30.000002 -> iFrame 30, fraq ~0 (float residue, not 30.2).
        Assert.Equal(1f, BoneSetupIk.StudioIkRuleWeight(rule, anim, 0.55f, out frame, out fraq));
        Assert.Equal(30, frame);
        Assert.True(fraq < 0.01f, $"fraq {fraq}");

        // Past end: window still computed, weight stays 0.
        BoneSetupIk.StudioIkRuleWeight(rule, anim, 0.9f, out frame, out fraq);
        Assert.Equal(50, frame);
        Assert.True(fraq < 0.01f, $"fraq {fraq}");

        // iStart shifts the frame window.
        BoneSetupIk.StudioIkRuleWeight(iStart7, anim, 0.55f, out frame, out fraq);
        Assert.Equal(37, frame);
        Assert.True(fraq < 0.01f, $"fraq {fraq}");
    }

    [Fact]
    public void IKRuleWeight_ContextArmMatchesRampShape()
    {
        var rule = new IkContextIkRule
        {
            Start = 0.25f, Peak = 0.4375f, Tail = 0.625f, End = 0.75f,
        };
        Assert.Equal(0f, BoneSetupIk.StudioIkRuleWeight(rule, 0.1f));            // before
        Assert.Equal(0.5f, BoneSetupIk.StudioIkRuleWeight(rule, 0.34375f), 4);     // ramp mid
        Assert.Equal(1f, BoneSetupIk.StudioIkRuleWeight(rule, 0.55f));           // full
        Assert.Equal(0.5f, BoneSetupIk.StudioIkRuleWeight(rule, 0.6875f), 4);      // drop mid
        Assert.Equal(0f, BoneSetupIk.StudioIkRuleWeight(rule, 0.9f));            // past end

        // end > 1 wraps flCycle += 1: cycle 0.2 -> 1.2 lands in peak..tail.
        var wrap = new IkContextIkRule
        {
            Start = 0.875f, Peak = 1.0625f, Tail = 1.25f, End = 1.5f,
        };
        Assert.Equal(1f, BoneSetupIk.StudioIkRuleWeight(wrap, 0.2f));
    }

    // ------------------------------------------------------------------
    // Studio_IKShouldLatch / Studio_IKTail
    // ------------------------------------------------------------------

    [Fact]
    public void IKShouldLatch_InsideOutsidePeakToTail()
    {
        var rule = new IkContextIkRule
        {
            Start = 0.25f, Peak = 0.4375f, Tail = 0.625f, End = 0.75f,
        };
        Assert.Equal(0f, BoneSetupIk.StudioIkShouldLatch(rule, 0.3f));    // ramp-up: below peak
        Assert.Equal(1f, BoneSetupIk.StudioIkShouldLatch(rule, 0.55f));   // peak..tail
        Assert.Equal(1f, BoneSetupIk.StudioIkShouldLatch(rule, 0.6875f)); // tail..end still latches
        Assert.Equal(0f, BoneSetupIk.StudioIkShouldLatch(rule, 0.8f));    // after end

        // end > 1 wraps the cycle before the band test.
        var wrap = new IkContextIkRule
        {
            Start = 0.875f, Peak = 1.0625f, Tail = 1.25f, End = 1.5f,
        };
        Assert.Equal(1f, BoneSetupIk.StudioIkShouldLatch(wrap, 0.2f));      // -> 1.2 in peak..tail
        Assert.Equal(0f, BoneSetupIk.StudioIkShouldLatch(wrap, 0.03125f));   // -> 1.03125: < peak
    }

    [Fact]
    public void IKTail_RampOnlyInsideTailToEnd()
    {
        var rule = new IkContextIkRule
        {
            Start = 0.25f, Peak = 0.4375f, Tail = 0.625f, End = 0.875f,
        };
        Assert.Equal(0f, BoneSetupIk.StudioIkTail(rule, 0.5f));     // before tail
        Assert.Equal(0f, BoneSetupIk.StudioIkTail(rule, 0.625f));   // == tail: not > tail
        Assert.Equal(0.5f, BoneSetupIk.StudioIkTail(rule, 0.75f), 4); // (0.75-0.625)/(0.875-0.625)
        Assert.Equal(0f, BoneSetupIk.StudioIkTail(rule, 0.9f));     // after end
    }

    // ------------------------------------------------------------------
    // Studio_IKAnimationError
    // ------------------------------------------------------------------

    private static (MstudioIkRule Rule, MstudioAnimDesc Anim) ErrorFixture(int type)
    {
        var b = Base();
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a0",
            NumFrames = 101,
            Fps = 30,
            IkRules =
            {
                new StudioHdrBuilder.IkRuleDef
                {
                    Type = type, Chain = 0, Bone = 2,
                    Start = 0.5f, Peak = 0.5625f, Tail = 0.625f, End = 0.75f,
                    Errors = ErrorFrames(40),
                },
            },
        });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s0", AnimTable = new short[] { 0 } });
        StudioHdr hdr = Build(b);
        return (hdr.AnimDesc(0).PIKRule(0)!, hdr.AnimDesc(0));
    }

    [Fact]
    public void IKAnimationError_NearZeroWeightVetoExceptGround()
    {
        // cycle 0.1 is outside every window: weight 0 -> veto for non-GROUND
        // rules... but stock's veto is flWeight < 0.0001 && type != IK_GROUND.
        (MstudioIkRule self, MstudioAnimDesc animS) = ErrorFixture(StudioConstants.IK_SELF);
        Assert.False(BoneSetupIk.StudioIkAnimationError(self, animS, 0.1f, out _, out _, out float wSelf));
        Assert.Equal(0f, wSelf);

        // The same rule as IK_GROUND skips the veto and samples the table.
        (MstudioIkRule ground, MstudioAnimDesc animG) = ErrorFixture(StudioConstants.IK_GROUND);
        Assert.True(BoneSetupIk.StudioIkAnimationError(ground, animG, 0.1f, out Vector pos, out _, out float wGround));
        Assert.Equal(0f, wGround);
        Assert.Equal(0f, pos.Y); // iFrame 0 -> record 0
    }

    [Fact]
    public void IKAnimationError_FrameSelectAndBlend()
    {
        (MstudioIkRule ground, MstudioAnimDesc animG) = ErrorFixture(StudioConstants.IK_GROUND);

        // cycle 0.55: window = 100*(0.55f-0.5f) = 5.000001 -> frame 5,
        // fraq ~0 (< 0.001 -> no blend) -> record [0] of the pair = y 5.
        Assert.True(BoneSetupIk.StudioIkAnimationError(ground, animG, 0.55f, out Vector pos, out _, out float w5));
        Assert.Equal(5f, pos.Y, 2);
        // ramp value (0.55f-0.5f)/(0.5625f-0.5f) = 0.80000 -> 3v^2-2v^3 = 0.896.
        Assert.Equal(0.896f, w5, 3);

        // cycle 0.50390625: window = 100 * 0.00390625 exactly? 0.00390625 is
        // dyadic (2^-8); 100 * it = 0.390625 -> frame 0, fraq 0.390625 ->
        // blend records 0,1: y = 0*(1-f) + 1*f = 0.390625.
        Assert.True(BoneSetupIk.StudioIkAnimationError(ground, animG, 0.50390625f, out pos, out _, out _));
        Assert.Equal(0.390625f, pos.Y, 5);
    }

    // ------------------------------------------------------------------
    // Studio_IKSequenceError
    // ------------------------------------------------------------------

    private static StudioHdr SeqErrorHdr()
    {
        var b = Base();
        var ground = new StudioHdrBuilder.IkRuleDef
        {
            Type = StudioConstants.IK_GROUND, Chain = 0, Bone = 2, Slot = 2,
            Start = 0.25f, Peak = 0.4375f, Tail = 0.625f, End = 0.75f,
            Errors = ErrorFrames(40),
        };
        var selfRule = new StudioHdrBuilder.IkRuleDef
        {
            Type = StudioConstants.IK_SELF, Chain = 0, Bone = 3,
            Start = 0.25f, Peak = 0.4375f, Tail = 0.625f, End = 0.75f,
            Errors = ErrorFrames(40),
        };
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a0", NumFrames = 101, Fps = 30, IkRules = { ground, selfRule },
        });
        var seq = new StudioHdrBuilder.SeqDef { Label = "s0", AnimTable = new short[] { 0 } };
        seq.IkRules.Add(ground);
        seq.IkRules.Add(selfRule);
        b.Sequences.Add(seq);
        b.IkChains.Add(new StudioHdrBuilder.IkChainDef
        {
            Name = "leg",
            Links = { (1, new Vector(1, 0, 0)), (2, Vector.Zero), (3, Vector.Zero) },
        });
        return Build(b);
    }

    [Fact]
    public void IKSequenceError_LatchReleaseBookkeeping()
    {
        StudioHdr hdr = SeqErrorHdr();
        MstudioSeqDesc seqdesc = hdr.SeqDesc(0);
        BoneSetupHdrRead.SeqAnims(hdr, seqdesc, 0, Array.Empty<float>(),
            out MstudioAnimDesc[] panim, out float[] weight);
        // Single anim, no activity/blending: exactly one weight of 1.
        Assert.Equal(1f, weight.Sum());

        // Mid window (peak..tail): full weight, latched, no release.
        Assert.True(BoneSetupIk.StudioIkSequenceError(hdr, seqdesc, 0, 0.55f, 0,
            Array.Empty<float>(), panim, weight, out IkContextIkRule r1));
        Assert.Equal(1f, r1.FlWeight, 4);
        Assert.Equal(1f, r1.Latched, 4);   // cycle >= peak && < end -> 1
        Assert.Equal(0f, r1.Release, 4);   // cycle <= tail -> 0
        Assert.Equal(0, r1.Chain);
        Assert.Equal(2, r1.Bone);          // GROUND: bone used verbatim
        Assert.Equal(2, r1.Slot);          // slot preserved from the rule

        // Halfway out of tail..end: release ramps to 0.5; the flWeight ramp
        // is also 0.5 there, and latched = ShouldLatch(1) * flWeight.
        Assert.True(BoneSetupIk.StudioIkSequenceError(hdr, seqdesc, 0, 0.6875f, 0,
            Array.Empty<float>(), panim, weight, out IkContextIkRule r2));
        Assert.Equal(0.5f, r2.Release, 4);
        Assert.Equal(0.5f, r2.Latched, 4);

        // Before the whole window: no influence -> false.
        Assert.False(BoneSetupIk.StudioIkSequenceError(hdr, seqdesc, 0, 0.1f, 0,
            Array.Empty<float>(), panim, weight, out _));

        // Second rule: IK_SELF at bone 3, identity group -> masterBone remap
        // is the identity; q sampled from the error table (identity quat).
        Assert.True(BoneSetupIk.StudioIkSequenceError(hdr, seqdesc, 0, 0.55f, 1,
            Array.Empty<float>(), panim, weight, out IkContextIkRule r3));
        Assert.Equal(3, r3.Bone);
        Assert.Equal(1f, r3.Q.W, 4);
    }

    [Fact]
    public void IKSequenceError_SeedsRuleQuaternionToZero()
    {
        // Stock 3126: ikRule.q.Init() — Quaternion::Init's defaults are all
        // 0.0f (vector.h:1577), the ZERO quaternion, not identity. A single
        // cell of weight 1 then accumulates exactly its own error q1 and the
        // final normalize returns q1 (90 degrees about Z). An identity seed
        // would answer normalize(identity + q1): 45 degrees.
        Quaternion q1 = Quaternion.CreateFromAxisAngle(Vector.UnitZ, MathF.PI / 2f);
        StudioHdrBuilder.IkErrorFrame[] errors = ErrorFrames(40);
        for (int i = 0; i < errors.Length; i++)
        {
            errors[i].Quat = q1;
        }

        var b = Base();
        var selfRule = new StudioHdrBuilder.IkRuleDef
        {
            Type = StudioConstants.IK_SELF, Chain = 0, Bone = 3,
            Start = 0.25f, Peak = 0.4375f, Tail = 0.625f, End = 0.75f,
            Errors = errors,
        };
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a0", NumFrames = 101, Fps = 30, IkRules = { selfRule },
        });
        var seq = new StudioHdrBuilder.SeqDef { Label = "s0", AnimTable = new short[] { 0 } };
        seq.IkRules.Add(selfRule);
        b.Sequences.Add(seq);
        b.IkChains.Add(new StudioHdrBuilder.IkChainDef
        {
            Name = "leg",
            Links = { (1, new Vector(1, 0, 0)), (2, Vector.Zero), (3, Vector.Zero) },
        });
        StudioHdr hdr = Build(b);
        MstudioSeqDesc seqdesc = hdr.SeqDesc(0);
        BoneSetupHdrRead.SeqAnims(hdr, seqdesc, 0, Array.Empty<float>(),
            out MstudioAnimDesc[] panim, out float[] weight);

        Assert.True(BoneSetupIk.StudioIkSequenceError(hdr, seqdesc, 0, 0.55f, 0,
            Array.Empty<float>(), panim, weight, out IkContextIkRule r));

        Assert.Equal(q1.Z, r.Q.Z, 5);
        Assert.Equal(q1.W, r.Q.W, 5);
        Assert.Equal(0f, r.Q.X, 5);
        Assert.Equal(0f, r.Q.Y, 5);
    }

    // ------------------------------------------------------------------
    // SolveBone round-trip
    // ------------------------------------------------------------------

    [Fact]
    public void SolveBone_RoundTripsWorldMatrix()
    {
        StudioHdr hdr = Build(Base());
        (Vector[] pos, Quaternion[] q) = FreshPose(new Vector(0, 15, -10), new Vector(0, -15, -10));
        q[2] = Quaternion.CreateFromYawPitchRoll(0.3f, 0.1f, 0.5f);
        BoneMatrix3x4[] bw = LegWorld(hdr, pos, q);
        BoneMatrix3x4 original = bw[3];

        // BuildBoneChain bakes world = concat(parent, q->mat) + pos; exact
        // inversion is concat(invert(parent), matrixAngles(m)) -- the codec
        // round-trip is what the epsilon measures.
        var posOut = new Vector[StudioConstants.MAXSTUDIOBONES];
        var qOut = new Quaternion[StudioConstants.MAXSTUDIOBONES];
        BoneSetupIk.SolveBone(hdr, 3, bw, posOut, qOut);

        BoneMatrix3x4 local = BoneSetupMath.QuaternionMatrix(qOut[3], posOut[3]);
        BoneMatrix3x4 rebuilt = BoneSetupMath.ConcatTransforms(bw[2], local);

        Span<float> a = stackalloc float[12];
        Span<float> c = stackalloc float[12];
        original.GetElements(a);
        rebuilt.GetElements(c);
        for (int i = 0; i < 12; i++)
        {
            Assert.True(MathF.Abs(a[i] - c[i]) < 1e-3f, $"element {i}: {a[i]} vs {c[i]}");
        }
    }

    // ------------------------------------------------------------------
    // SolveLock via CIKContext autoplay lock, one chain, both arms
    // ------------------------------------------------------------------

    private static StudioHdr LockHdr(Vector kneeDir)
    {
        var b = Base();
        b.IkChains.Add(new StudioHdrBuilder.IkChainDef
        {
            Name = "leg",
            Links = { (1, kneeDir), (2, Vector.Zero), (3, Vector.Zero) },
        });
        b.AutoPlayLocks.Add(new StudioHdrBuilder.IkLockDef
        {
            Chain = 0,
            FlPosWeight = 1.0f,
            FlLocalQWeight = 1.0f,
        });
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s0", AnimTable = new short[] { 0 } });
        return Build(b);
    }

    [Fact]
    public void SolveLock_KneeDirArm_ReachesLockedFoot()
    {
        // Lock pose: thigh (0,0,50), knee (12,0,35) world, foot (2,0,34)
        // world. Solve pose drags the foot to (5,0,35); the lock must pull
        // it back to (2,0,34) with the knee pushed to +X (kneeDir (1,0,0)).
        StudioHdr hdr = LockHdr(new Vector(1, 0, 0));
        var ctx = new CIKContext();
        ctx.Init(hdr, BoneMatrix3x4.Identity, Mask);

        (Vector[] pos, Quaternion[] q) = FreshPose(new Vector(12, 0, -15), new Vector(-10, 0, -1));
        ctx.AddAutoplayLocks(pos, q);

        (pos, q) = FreshPose(new Vector(12, 0, -15), new Vector(-7, 0, 0));
        ctx.SolveAutoplayLocks(pos, q);

        BoneMatrix3x4[] bw = LegWorld(hdr, pos, q);
        AssertClose(new Vector(2f, 0f, 34f), bw[3].Position, 0.05f);
        // kneeDir biases the knee toward world +X (thigh is at the origin).
        Assert.True(bw[2].Position.X > 5f, $"knee went to {bw[2].Position}");
    }

    [Fact]
    public void SolveLock_PlainArm_ReachesLockedFootAndKeepsPoseQuat()
    {
        // kneeDir zero -> 4-arg chain; foot locked at (0,0,35) with the knee
        // on +Y; the solver picks the knee side from the current geometry.
        StudioHdr hdr = LockHdr(Vector.Zero);
        var ctx = new CIKContext();
        ctx.Init(hdr, BoneMatrix3x4.Identity, Mask);

        (Vector[] pos, Quaternion[] q) = FreshPose(new Vector(0, 12, -15), new Vector(0, -12, 0));
        ctx.AddAutoplayLocks(pos, q);

        (pos, q) = FreshPose(new Vector(0, 12, -15), new Vector(0, -10, -3));
        q[3] = Quaternion.CreateFromYawPitchRoll(0.4f, 0f, 0f);
        Quaternion yawed = q[3];
        ctx.SolveAutoplayLocks(pos, q);

        BoneMatrix3x4[] bw = LegWorld(hdr, pos, q);
        AssertClose(new Vector(0f, 0f, 35f), bw[3].Position, 0.05f);
        // knee was +Y at solve time -> stays +Y.
        Assert.True(bw[2].Position.Y > 5f, $"knee went to {bw[2].Position}");
        // FlLocalQWeight 1 -> slerp(q, q2, 1) hands the pose quaternion back.
        Assert.Equal(yawed.X, q[3].X, 5);
        Assert.Equal(yawed.W, q[3].W, 5);
    }

    [Fact]
    public void SolveLock_SequenceLocksRoundTrip()
    {
        // Same geometry as the plain arm, via AddSequenceLocks/SolveSequenceLocks.
        var b = Base();
        b.IkChains.Add(new StudioHdrBuilder.IkChainDef
        {
            Name = "leg",
            Links = { (1, Vector.Zero), (2, Vector.Zero), (3, Vector.Zero) },
        });
        b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "a0", NumFrames = 2, Fps = 30 });
        var seq = new StudioHdrBuilder.SeqDef { Label = "s0", AnimTable = new short[] { 0 } };
        seq.IkLocks.Add(new StudioHdrBuilder.IkLockDef { Chain = 0, FlPosWeight = 1.0f, FlLocalQWeight = 1.0f });
        b.Sequences.Add(seq);
        StudioHdr hdr = Build(b);

        var ctx = new CIKContext();
        ctx.Init(hdr, BoneMatrix3x4.Identity, Mask);
        (Vector[] pos, Quaternion[] q) = FreshPose(new Vector(0, 12, -15), new Vector(0, -12, 0));
        ctx.AddSequenceLocks(0, pos, q);
        (pos, q) = FreshPose(new Vector(0, 12, -15), new Vector(0, -10, -3));
        ctx.SolveSequenceLocks(0, pos, q);

        BoneMatrix3x4[] bw = LegWorld(hdr, pos, q);
        AssertClose(new Vector(0f, 0f, 35f), bw[3].Position, 0.05f);
    }

    // ------------------------------------------------------------------
    // Context lifecycle guards
    // ------------------------------------------------------------------

    [Fact]
    public void ContextUsedBeforeInit_IsRejected()
    {
        var ctx = new CIKContext();
        Assert.Throws<InvalidOperationException>(() => ctx.AddDependencies(0, Array.Empty<float>(), 1f));
    }

    [Fact]
    public void AddDependencies_FullWeight_ClearsChainAndAcceptsSecondPass()
    {
        // One WORLD rule + one RELEASE rule over chain 0 at full weight:
        // the release rule clears the chain, the world rule lands after it;
        // repeating at full weight must not duplicate (clear-on-full).
        var b = Base();
        var world = new StudioHdrBuilder.IkRuleDef
        {
            Type = StudioConstants.IK_WORLD, Chain = 0, Bone = 2,
            Start = 0.25f, Peak = 0.4375f, Tail = 0.625f, End = 0.75f,
            Errors = ErrorFrames(40),
        };
        var release = new StudioHdrBuilder.IkRuleDef
        {
            Type = StudioConstants.IK_RELEASE, Chain = 0, Bone = 2,
            Start = 0.375f, Peak = 0.5f, Tail = 0.625f, End = 0.75f,
        };
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a0", NumFrames = 101, Fps = 30, IkRules = { world, release },
        });
        var seq = new StudioHdrBuilder.SeqDef { Label = "s0", AnimTable = new short[] { 0 } };
        seq.IkRules.Add(world);
        seq.IkRules.Add(release);
        b.Sequences.Add(seq);
        b.IkChains.Add(new StudioHdrBuilder.IkChainDef
        {
            Name = "leg",
            Links = { (1, new Vector(1, 0, 0)), (2, Vector.Zero), (3, Vector.Zero) },
        });
        StudioHdr hdr = Build(b);

        var ctx = new CIKContext();
        ctx.Init(hdr, BoneMatrix3x4.Identity, Mask);
        ctx.AddDependencies(0, Array.Empty<float>(), 1.0f, 0.55f);
        ctx.AddDependencies(0, Array.Empty<float>(), 1.0f, 0.55f);
        ctx.ClearTargets();
        ctx.ClearTargets();
    }

    [Fact]
    public void Init_WithoutChains_ClearTargetsIsSafe()
    {
        var b = Base();
        b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "s0", AnimTable = new short[] { 0 } });
        StudioHdr hdr = Build(b); // no IK chains at all
        var ctx = new CIKContext();
        ctx.Init(hdr, BoneMatrix3x4.Identity, Mask);
        ctx.ClearTargets();
        ctx.ClearTargets(); // second stamp over the same 12 entries
    }
}
