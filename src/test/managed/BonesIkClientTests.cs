// ik client cluster: facts for the CLIENT_DLL CIKTarget members and the
// CIKContext client members (UpdateTargets, AutoIKRelease,
// SolveDependencies, AddAllLocks, SolveAllLocks). Expectations are
// hand-derived from the stock C++ in our bone_setup.cpp (the relocated
// CLIENT_DLL copies); the derivation is written into each fact's
// doc-comment. Dyadic/special angles are used so the float32 expectations
// are hand-provable; where they are not (transcendentals of non-dyadic
// slopes), the tolerance is stated with its reason.
using System;
using System.Numerics;
using Xunit;
using SourceSharp.Shared.Bones;
using SourceSharp.Tests;
using Vector = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace SourceSharp.Tests;

public class BonesIkClientTests
{
    private const int Mask = unchecked((int)0xFFFFFFFF);
    private const float SqrtHalf = 0.70710677f; // f32(sqrt(0.5))

    private static void AssertClose(Vector expected, Vector actual, float tol)
    {
        Assert.True(MathF.Abs(expected.X - actual.X) <= tol, $"x {expected.X} vs {actual.X}");
        Assert.True(MathF.Abs(expected.Y - actual.Y) <= tol, $"y {expected.Y} vs {actual.Y}");
        Assert.True(MathF.Abs(expected.Z - actual.Z) <= tol, $"z {expected.Z} vs {actual.Z}");
    }

    private static void AssertQuat(Quaternion expected, Quaternion actual, float tol)
    {
        Assert.True(MathF.Abs(expected.X - actual.X) <= tol, $"x {expected.X} vs {actual.X}");
        Assert.True(MathF.Abs(expected.Y - actual.Y) <= tol, $"y {expected.Y} vs {actual.Y}");
        Assert.True(MathF.Abs(expected.Z - actual.Z) <= tol, $"z {expected.Z} vs {actual.Z}");
        Assert.True(MathF.Abs(expected.W - actual.W) <= tol, $"w {expected.W} vs {actual.W}");
    }

    // ------------------------------------------------------------------
    // CIKTarget members (stock oracle 3525-3691; our 3607-3780)
    // ------------------------------------------------------------------

    /// <summary>SetOwner (3525-3530): the three stores are verbatim copies
    /// into the latched block — entindex, absOrigin, absAngles.</summary>
    [Fact]
    public void CikTarget_SetOwner_StoresEntIndexOriginAngles()
    {
        var t = new CikTarget();
        t.SetOwner(42, new Vector(1, 2, 3), new BoneAngle(10, 20, 30));

        Assert.Equal(42, t.GetOwner());
        Assert.Equal(42, t.LatchedOwner);
        AssertClose(new Vector(1, 2, 3), t.LatchedAbsOrigin, 0f);
        Assert.Equal(10f, t.LatchedAbsAngles.Pitch);
        Assert.Equal(20f, t.LatchedAbsAngles.Yaw);
        Assert.Equal(30f, t.LatchedAbsAngles.Roll);
    }

    /// <summary>ClearOwner (3536-3539): only latched.owner = -1; the
    /// absOrigin/absAngles stamps survive (stock touches one field).</summary>
    [Fact]
    public void CikTarget_ClearOwner_SetsOwnerMinusOneOnly()
    {
        var t = new CikTarget();
        t.SetOwner(42, new Vector(1, 2, 3), new BoneAngle(10, 20, 30));
        t.ClearOwner();

        Assert.Equal(-1, t.GetOwner());
        AssertClose(new Vector(1, 2, 3), t.LatchedAbsOrigin, 0f);
        Assert.Equal(20f, t.LatchedAbsAngles.Yaw);
    }

    /// <summary>UpdateOwner (3554-3568) early-out: stock's guard is exact
    /// component equality on pos and the QAngle memberwise compare, so the
    /// same transform re-presented leaves latched.pos/q untouched.</summary>
    [Fact]
    public void CikTarget_UpdateOwner_SameTransform_EarlyOut()
    {
        var t = new CikTarget();
        t.SetOwner(7, new Vector(1, 2, 3), new BoneAngle(10, 20, 30));
        t.LatchedPos = new Vector(5, 5, 5);
        t.LatchedQ = new Quaternion(0, 0, 0.5f, 0.8660254f);

        t.UpdateOwner(7, new Vector(1, 2, 3), new BoneAngle(10, 20, 30));

        AssertClose(new Vector(5, 5, 5), t.LatchedPos, 0f);
        AssertQuat(new Quaternion(0, 0, 0.5f, 0.8660254f), t.LatchedQ, 0f);
        // And the entindex is NOT stored by UpdateOwner (only SetOwner does).
        Assert.Equal(7, t.GetOwner());
    }

    /// <summary>UpdateOwner (3554-3568) rebase: latched pose
    /// (q=identity, pos=(10,20,30)) recorded under an owner at (0,100,0)
    /// yaw 90, re-presented with the owner at the origin yaw 0.
    /// in1 = AngleMatrix((0,0,0),(0,0,0)) = identity. out1 =
    /// AngleIMatrix((0,90,0),(0,100,0)) (mathlib_base.cpp:1247-1268): the
    /// TRANSPOSED forward basis — rows (0,1,0)/(-1,0,0)/(0,0,1) (forward is
    /// (0,-1,0)/(1,0,0)/(0,0,1)) — then translation -VectorRotate(p, that
    /// basis) = -(100,0,0). tmp2 = out1 * QM(identity,(10,20,30)):
    /// rotation the IM rows, translation rotate-basis((10,20,30)) - (100,0,0)
    /// = (20,-10,30)-(100,0,0) = (-80,-10,30). in1 identity leaves it;
    /// MatrixAnglesQuaternion: trace 0+0+1+1 = 2 > 1+eps ->
    /// q = (R2.Y-R1.Z, R0.Z-R2.X, R1.X-R0.Y, 2) = (0,0,-2,2) -> normalised
    /// (0,0,-SqrtHalf,SqrtHalf). Closed form of stock's
    /// in*out*latched at the identity owner: latched_new =
    /// R_oldᵀ·latched_pos - R_oldᵀ·p_old, and the IM row-dots ARE R_oldᵀ:
    /// R_oldᵀ(10,20,30) = (20,-10,30), minus (100,0,0). A port that keeps
    /// the FORWARD basis (the old bug: reuse AngleMatrix's rows, negate
    /// VectorRotate over them) produces (80,10,30) with a +90° quaternion:
    /// the latched contact pose re-expressed on a moving, turning owner
    /// (bone_setup.cpp:3554-3568) lands mirrored.
    /// est.* is not touched by stock.</summary>
    [Fact]
    public void CikTarget_UpdateOwner_MovingOwner_RebasesLatchedPose()
    {
        var t = new CikTarget();
        t.SetOwner(7, new Vector(0, 100, 0), new BoneAngle(0, 90, 0));
        t.LatchedPos = new Vector(10, 20, 30);
        t.LatchedQ = Quaternion.Identity;
        t.EstPos = new Vector(111, 111, 111); // stock never touches est here

        t.UpdateOwner(9, Vector.Zero, new BoneAngle(0, 0, 0));

        AssertClose(new Vector(-80, -10, 30), t.LatchedPos, 1e-4f);
        AssertQuat(new Quaternion(0, 0, -SqrtHalf, SqrtHalf), t.LatchedQ, 1e-6f);
        AssertClose(new Vector(111, 111, 111), t.EstPos, 0f);
        // Owner bookkeeping is unchanged (still the SetOwner stamps).
        Assert.Equal(7, t.GetOwner());
        AssertClose(new Vector(0, 100, 0), t.LatchedAbsOrigin, 0f);
    }

    /// <summary>SetPos (3575-3578): one store into est.pos.</summary>
    [Fact]
    public void CikTarget_SetPos_StoresEstPos()
    {
        var t = new CikTarget();
        t.SetPos(new Vector(4, -5, 60));
        AssertClose(new Vector(4, -5, 60), t.EstPos, 0f);
    }

    /// <summary>SetAngles (3584-3587): AngleQuaternion of (pitch 0, yaw 90,
    /// roll 0): sy = sin45 = cy, sp = sr = 0, cp = cr = 1, so
    /// x = y = 0 and (z, w) = (sy, cy) = (√2/2, √2/2) — a +90° turn about
    /// Z (Source's up axis), exactly as a yaw is supposed to read.</summary>
    [Fact]
    public void CikTarget_SetAngles_Yaw90_StoresAngleQuaternion()
    {
        var t = new CikTarget();
        t.SetAngles(new BoneAngle(0, 90, 0));
        AssertQuat(new Quaternion(0, 0, SqrtHalf, SqrtHalf), t.EstQ, 1e-6f);
    }

    /// <summary>SetQuaternion (3593-3596): verbatim store into est.q.</summary>
    [Fact]
    public void CikTarget_SetQuaternion_StoresEstQ()
    {
        var t = new CikTarget();
        t.SetQuaternion(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f));
        AssertQuat(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f), t.EstQ, 0f);
    }

    /// <summary>SetNormal (3603-3618): est.q identity -> m1 = identity,
    /// right = column 1 = (0,1,0). normal (0,-√2/2,√2/2) (a 45° slope):
    /// forward = right × normal = (√2/2,0,0); right = normal × forward =
    /// (0,½,½) (stock does NOT renormalise). Basis columns X=(√2/2,0,0),
    /// Y=(0,½,½), Z=(0,-√2/2,√2/2) give rows Row0=(√2/2,0,0),
    /// Row1=(0,½,-√2/2), Row2=(0,½,√2/2); trace = 1+√2/2+½+√2/2+1-1 =
    /// 2.9142 > 1+eps -> q = (½+√2/2, 0, 0, 2.9142) unnormalised; norm =
    /// sqrt(1.2071² + 2.9142²) = 3.1543 -> (0.38268, 0, 0, 0.92388) =
    /// (sin 22.5°, 0, 0, cos 22.5°): the foot tilts half the slope.</summary>
    [Fact]
    public void CikTarget_SetNormal_Slope45_TiltsFootHalfTheSlope()
    {
        var t = new CikTarget();
        t.SetQuaternion(Quaternion.Identity);
        t.SetNormal(new Vector(0, -SqrtHalf, SqrtHalf));

        float h = (float)Math.PI / 8.0f; // 22.5 degrees
        AssertQuat(new Quaternion(MathF.Sin(h), 0, 0, MathF.Cos(h)), t.EstQ, 1e-5f);
    }

    /// <summary>SetPosWithNormalOffset (3626-3645): disc-edge estimate.
    /// flat (z 1 > 0.9999): early return, est.pos = pos.
    /// 30° slope (z 0.8660254): tan = sqrt(1-0.75)/0.866 = 0.57735, so
    /// z = 30 - 10*0.57735 = 24.2265 (f32 residue of 0.8660254² ≈ 0.75
    /// sets the 1e-3 tolerance).
    /// clamp arms: z 0.7070 and 0.5 are both <= 0.707 -> z -= radius.</summary>
    [Theory]
    [InlineData(1.0f, 30f)]        // flat: untouched
    [InlineData(0.8660254f, 24.2265f)] // 30° slope: radius * tan(30°) off
    [InlineData(0.7070f, 20f)]     // just under the 0.707 clamp: full radius
    [InlineData(0.5f, 20f)]        // steep: full radius
    public void CikTarget_SetPosWithNormalOffset_DiscEdgeEstimate(float nz, float expectedZ)
    {
        var t = new CikTarget();
        t.EstRadius = 10f;
        t.SetPosWithNormalOffset(new Vector(1, 2, 30), new Vector(0, 0, nz));

        Assert.Equal(1f, t.EstPos.X);
        Assert.Equal(2f, t.EstPos.Y);
        Assert.True(MathF.Abs(t.EstPos.Z - expectedZ) <= 1e-3f,
            $"z {t.EstPos.Z} vs {expectedZ}");
    }

    /// <summary>SetOnWorld (3651-3654) + IsActive (3660-3663): the flag is a
    /// plain store; active is the strict test est.flWeight > 0.</summary>
    [Fact]
    public void CikTarget_SetOnWorldAndIsActive()
    {
        var t = new CikTarget();
        Assert.False(t.IsActive());          // memset-zero flWeight
        t.SetOnWorld();
        Assert.True(t.EstOnWorld);
        t.SetOnWorld(false);
        Assert.False(t.EstOnWorld);

        t.EstFlWeight = 0.0001f;
        Assert.True(t.IsActive());
        t.EstFlWeight = 0f;
        Assert.False(t.IsActive());
        t.EstFlWeight = -1f;
        Assert.False(t.IsActive());          // strict >, not >=
    }

    /// <summary>IKFailed (3669-3678): latched deltas Init (zero), latched
    /// pos/q collapse onto the ideal, est.latched/est.flWeight go to 0 and
    /// est.onWorld false. Stock's Init()s and these six stores are all it
    /// does — est.pos/est.q keep their values (no reset there).</summary>
    [Fact]
    public void CikTarget_IKFailed_ResetsLatchedToIdealAndDropsInfluence()
    {
        var t = new CikTarget();
        t.IdealPos = new Vector(7, 8, 9);
        t.IdealQ = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
        t.LatchedPos = new Vector(1, 1, 1);
        t.LatchedQ = new Quaternion(0.5f, 0, 0, 0.8660254f);
        t.LatchedDeltaPos = new Vector(3, 4, 5);
        t.LatchedDeltaQ = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
        t.EstLatched = 1f;
        t.EstFlWeight = 0.5f;
        t.EstOnWorld = true;
        t.EstPos = new Vector(5, 5, 5);
        t.EstQ = new Quaternion(0.2f, 0, 0, 0.98f);

        t.IKFailed();

        Assert.Equal(0f, t.LatchedDeltaPos.X);
        Assert.Equal(0f, t.LatchedDeltaPos.Y);
        Assert.Equal(0f, t.LatchedDeltaPos.Z);
        Assert.Equal(0f, t.LatchedDeltaQ.X);
        Assert.Equal(0f, t.LatchedDeltaQ.Y);
        Assert.Equal(0f, t.LatchedDeltaQ.Z);
        Assert.Equal(0f, t.LatchedDeltaQ.W);
        AssertClose(t.IdealPos, t.LatchedPos, 0f);
        AssertQuat(t.IdealQ, t.LatchedQ, 0f);
        Assert.Equal(0f, t.EstLatched);
        Assert.Equal(0f, t.EstFlWeight);
        Assert.False(t.EstOnWorld);
        // stock leaves these alone:
        AssertClose(new Vector(5, 5, 5), t.EstPos, 0f);
    }

    /// <summary>MoveReferenceFrame (3685-3691): est.pos, latched.pos,
    /// offset.pos and ideal.pos each get -= deltaPos; deltaAngles is an
    /// unused parameter and no quaternion moves.</summary>
    [Fact]
    public void CikTarget_MoveReferenceFrame_TranslatesFourPositions()
    {
        var t = new CikTarget();
        t.EstPos = new Vector(10, 10, 10);
        t.LatchedPos = new Vector(20, 20, 20);
        t.OffsetPos = new Vector(30, 30, 30);
        t.IdealPos = new Vector(40, 40, 40);
        t.IdealQ = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);

        t.MoveReferenceFrame(new Vector(5, -3, 10), new BoneAngle(1, 2, 3));

        AssertClose(new Vector(5, 13, 0), t.EstPos, 0f);
        AssertClose(new Vector(15, 23, 10), t.LatchedPos, 0f);
        AssertClose(new Vector(25, 33, 20), t.OffsetPos, 0f);
        AssertClose(new Vector(35, 43, 30), t.IdealPos, 0f);
        AssertQuat(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f), t.IdealQ, 0f);
    }

    // ------------------------------------------------------------------
    // CIKContext::UpdateTargets (stock oracle 3821-3942)
    // ------------------------------------------------------------------
    // Leg fixture: b0 root -> b1 thigh -> b2 knee -> b3 foot, all bones
    // mask-visible. The pose puts the thigh at (0,0,50), the knee at
    // (0,15,-10) (local (0,15,-60), d1 = sqrt(3825) = 61.8465844), the foot
    // at (0,-15,-10) (local (0,-30,0), d2 = 30). One chain (links
    // bone1/bone2/bone3), one GROUND hdr rule at slot 2 with the shared
    // dyadic window (start .25, peak .4375, tail .625, end .75): cycle 0.55
    // is mid full-band (flWeight 1, ShouldLatch 1 -> latched 1, release 0);
    // cycle 0.6875 is mid ramp-out (flWeight 0.5, latched 0.5, release 0.5).

    private static StudioHdrBuilder.IkErrorFrame[] FlatFrames(Vector pos)
    {
        var f = new StudioHdrBuilder.IkErrorFrame[101];
        for (int i = 0; i < f.Length; i++)
        {
            f[i] = new StudioHdrBuilder.IkErrorFrame { Pos = pos, Quat = Quaternion.Identity };
        }
        return f;
    }

    private static StudioHdr GroundHdr(
        Vector? errorPos = null, float height = 0f, float floor = 0f, float radius = 0f,
        Vector? kneeDir = null, int footFlags = Mask, bool twoChains = false,
        int ruleType = StudioConstants.IK_GROUND, int ruleBone = 2)
    {
        var b = new StudioHdrBuilder();
        for (int i = 0; i < 4; i++)
        {
            b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b" + i, Parent = i - 1, Flags = Mask });
        }
        b.Bones[3].Flags = footFlags; // chain foot; 0 exercises the boneMask gates
        b.ModelName = "b0";
        var rule = new StudioHdrBuilder.IkRuleDef
        {
            Type = ruleType, Chain = 0, Bone = ruleBone, Slot = 2,
            Start = 0.25f, Peak = 0.4375f, Tail = 0.625f, End = 0.75f,
            Height = height, Floor = floor, Radius = radius,
            // Uniform error table (one record per frame; every cycle in
            // [0,1) maps inside it, and blending identical rows is exact).
            Errors = FlatFrames(errorPos ?? Vector.Zero),
        };
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a0", NumFrames = 101, Fps = 30, IkRules = { rule },
        });
        var seq = new StudioHdrBuilder.SeqDef { Label = "s0", AnimTable = new short[] { 0 } };
        seq.IkRules.Add(rule);
        b.Sequences.Add(seq);
        b.IkChains.Add(new StudioHdrBuilder.IkChainDef
        {
            Name = "leg",
            Links = { (1, kneeDir ?? Vector.Zero), (2, Vector.Zero), (3, Vector.Zero) },
        });
        if (twoChains)
        {
            // Same three bones: AddAllLocks/SolveAllLocks walk one lock slot
            // per chain, so the second slot reaches chain 1 (the synthetic
            // lock's chain is the slot index).
            b.IkChains.Add(new StudioHdrBuilder.IkChainDef
            {
                Name = "leg2",
                Links = { (1, Vector.Zero), (2, Vector.Zero), (3, Vector.Zero) },
            });
        }
        Assert.True(StudioHdr.TryCreate(b.Build(), b.Resolver, out StudioHdr? hdr) && hdr is not null,
            "ik-client ground fixture rejected");
        return hdr!;
    }

    /// <summary>One IK_SELF rule (Bone -1 so the world target hangs off
    /// m_rootxform, error rows at <c>selfErrorPos</c>) plus one IK_RELEASE
    /// rule on the same chain/window, for the SolveDependencies
    /// blend-back fact.</summary>
    private static StudioHdr SelfReleaseHdr(Vector selfErrorPos)
    {
        var b = new StudioHdrBuilder();
        for (int i = 0; i < 4; i++)
        {
            b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "b" + i, Parent = i - 1, Flags = Mask });
        }
        b.ModelName = "b0";
        var self = new StudioHdrBuilder.IkRuleDef
        {
            Type = StudioConstants.IK_SELF, Chain = 0, Bone = -1, Slot = 2,
            Start = 0.25f, Peak = 0.4375f, Tail = 0.625f, End = 0.75f,
            Errors = FlatFrames(selfErrorPos),
        };
        var release = new StudioHdrBuilder.IkRuleDef
        {
            Type = StudioConstants.IK_RELEASE, Chain = 0, Bone = 2, Slot = 2,
            Start = 0.25f, Peak = 0.4375f, Tail = 0.625f, End = 0.75f,
        };
        b.Anims.Add(new StudioHdrBuilder.AnimDef
        {
            Name = "a0", NumFrames = 101, Fps = 30, IkRules = { self, release },
        });
        var seq = new StudioHdrBuilder.SeqDef { Label = "s0", AnimTable = new short[] { 0 } };
        seq.IkRules.Add(self);
        seq.IkRules.Add(release);
        b.Sequences.Add(seq);
        b.IkChains.Add(new StudioHdrBuilder.IkChainDef
        {
            Name = "leg", Links = { (1, Vector.Zero), (2, Vector.Zero), (3, Vector.Zero) },
        });
        Assert.True(StudioHdr.TryCreate(b.Build(), b.Resolver, out StudioHdr? hdr) && hdr is not null,
            "ik-client self+release fixture rejected");
        return hdr!;
    }

    private static (Vector[] Pos, Quaternion[] Q) LegPose()
    {
        var pos = new Vector[StudioConstants.MAXSTUDIOBONES];
        var q = new Quaternion[StudioConstants.MAXSTUDIOBONES];
        for (int i = 0; i < StudioConstants.MAXSTUDIOBONES; i++)
        {
            q[i] = Quaternion.Identity;
        }
        pos[1] = new Vector(0, 0, 50);
        pos[2] = new Vector(0, 15, -60);
        pos[3] = new Vector(0, -30, 0);
        return (pos, q);
    }

    private static BoneMatrix3x4[] FreshWorld() => new BoneMatrix3x4[StudioConstants.MAXSTUDIOBONES];

    /// <summary>Per-frame stamps (3826-3836): every target re-enters the
    /// rule fold with flWeight 0, latched 1, release 1, height/floor/
    /// radius 0 and a cleared offset. Slot 5 carries no rule, so nothing
    /// refills it — the dirty pre-state proving the stamps took.</summary>
    [Fact]
    public void UpdateTargets_PerFrameStamps_OnUnruledSlot()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(), BoneMatrix3x4.Identity, Mask);
        ctx.AddDependencies(0, Array.Empty<float>(), 1f, 0.55f);

        CikTarget t5 = ctx.TargetAt(5);
        t5.EstFlWeight = 0.3f;
        t5.EstLatched = 0.2f;
        t5.EstRelease = 0.9f;
        t5.EstHeight = 7f;
        t5.EstFloor = 9f;
        t5.EstRadius = 2f;
        t5.OffsetPos = new Vector(1, 1, 1);
        t5.OffsetQ = new Quaternion(0.5f, 0.5f, 0.5f, 0.5f);

        (Vector[] pos, Quaternion[] q) = LegPose();
        ctx.UpdateTargets(pos, q, FreshWorld(), new BoneSetupExtract.BoneBitList());

        Assert.Equal(0f, t5.EstFlWeight);
        Assert.Equal(1f, t5.EstLatched);
        Assert.Equal(1f, t5.EstRelease);
        Assert.Equal(0f, t5.EstHeight);
        Assert.Equal(0f, t5.EstFloor);
        Assert.Equal(0f, t5.EstRadius);
        Assert.Equal(0f, t5.OffsetPos.X);
        Assert.Equal(0f, t5.OffsetPos.Y);
        Assert.Equal(0f, t5.OffsetPos.Z);
        Assert.Equal(0f, t5.OffsetQ.W);
    }

    /// <summary>Pick arm (3867-3877) + the GROUND tail (3888-3892): the
    /// GROUND rule at cycle 0.55 has flRuleWeight 1 -> the fold OVERWRITES:
    /// offset = rule.q/pos (uniform error frames (0,0,-45)), height/floor/
    /// radius = rule 5/8/3, est.latched = latched*flRuleWeight = 1*1,
    /// release 0, flWeight 1*1. The tail zeroes latched.deltaPos.z (pre-set
    /// (3,4,5) -> (3,4,0)) and stamps est.pos.z = floor + m_rootxform[2][3]
    /// — which the target loop then overwrites: worldFootpad = bw[foot] *
    /// invert(QM(identity,(0,0,-45))) = foot (0,-15,-10) + (0,0,45) =
    /// (0,-15,35), so ideal = est = (0,-15,35) (rotation identity).</summary>
    [Fact]
    public void UpdateTargets_GroundRuleFullWeight_PickArmAndGroundTail()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(new Vector(0, 0, -45), height: 5f, floor: 8f, radius: 3f),
            BoneMatrix3x4.Identity, Mask, flTime: 10f, iFramecounter: 0);
        ctx.AddDependencies(0, Array.Empty<float>(), 1f, 0.55f);

        CikTarget t = ctx.TargetAt(2);
        t.LatchedDeltaPos = new Vector(3, 4, 5);

        (Vector[] pos, Quaternion[] q) = LegPose();
        ctx.UpdateTargets(pos, q, FreshWorld(), new BoneSetupExtract.BoneBitList());

        Assert.Equal(0, t.Chain);
        Assert.Equal(StudioConstants.IK_GROUND, t.Type);
        Assert.Null(t.OffsetPAttachmentName);
        AssertClose(new Vector(0, 0, -45), t.OffsetPos, 1e-4f);
        AssertQuat(Quaternion.Identity, t.OffsetQ, 1e-6f);
        Assert.Equal(5f, t.EstHeight);
        Assert.Equal(8f, t.EstFloor);
        Assert.Equal(3f, t.EstRadius);
        Assert.Equal(1f, t.EstLatched);
        Assert.Equal(0f, t.EstRelease);
        Assert.Equal(1f, t.EstFlWeight);
        Assert.True(t.LatchedBNeedsLatch);          // est.latched == 1
        Assert.Equal(0f, t.LatchedDeltaPos.Z);      // the GROUND tail
        Assert.Equal(3f, t.LatchedDeltaPos.X);
        Assert.Equal(4f, t.LatchedDeltaPos.Y);
        AssertClose(new Vector(0, -15, 35), t.EstPos, 1e-3f);
        AssertClose(new Vector(0, -15, 35), t.IdealPos, 1e-3f);
    }

    /// <summary>Blend arm (3878-3886): the same hdr rule stored TWICE at
    /// flRuleWeight 0.5 (two AddDependencies passes; 0.5*1 = 0.5 never hits
    /// the >0.999 chain-clear). Fold 1 takes the overwrite arm (est.flWeight
    /// was 0): flWeight = 1*0.5, latched = 1*0.5. Fold 1 is followed by
    /// fold 2 (est.flWeight 0.5 != 0, flRuleWeight 0.5 != 1): the slerp/lerp
    /// arm — flWeight = Lerp(0.5, 0.5, 1) = 0.75, release = Lerp(0.5,0,0) =
    /// 0 — and the quirk: est.latched is NOT lerped (that line is commented
    /// out in stock); it is min(est.latched, rule.latched) = min(0.5, 1) =
    /// 0.5. A Lerp would have given 0.75.</summary>
    [Fact]
    public void UpdateTargets_TwoHalfWeightRules_BlendArmAndLatchedMinQuirk()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(), BoneMatrix3x4.Identity, Mask, flTime: 10f, iFramecounter: 0);
        ctx.AddDependencies(0, Array.Empty<float>(), 0.5f, 0.55f);
        ctx.AddDependencies(0, Array.Empty<float>(), 0.5f, 0.55f);

        (Vector[] pos, Quaternion[] q) = LegPose();
        ctx.UpdateTargets(pos, q, FreshWorld(), new BoneSetupExtract.BoneBitList());

        CikTarget t = ctx.TargetAt(2);
        Assert.Equal(0.75f, t.EstFlWeight, 5);
        Assert.Equal(0.5f, t.EstLatched, 5);   // min(0.5, 1), not Lerp -> 0.75
        Assert.Equal(0f, t.EstRelease, 5);
    }

    /// <summary>Trace bookkeeping (3925-3938), unlatched target with a
    /// zero-offset GROUND rule: est = ideal = foot world (0,-15,-10); hip
    /// (0,0,50), knee (0,15,-10). d1 = |(0,15,-60)| = sqrt(3825), d2 = 30.
    /// footDir = (est.pos - hip)/|…| = (0,-15,-60)/sqrt(3825). closest =
    /// hip + dir*(|d1-d2|*1.01 = 32.165); farthest = hip +
    /// dir*((d1+d2)*0.99 = 90.928); lowest = hip + (0,0,-1)*90.928.
    /// (hipToFoot = |est - hip| = sqrt(3825) = d1 here.)</summary>
    [Fact]
    public void UpdateTargets_TraceBookkeeping_ClosestFarthestLowest()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(), BoneMatrix3x4.Identity, Mask, flTime: 10f, iFramecounter: 0);
        ctx.AddDependencies(0, Array.Empty<float>(), 1f, 0.55f);

        (Vector[] pos, Quaternion[] q) = LegPose();
        ctx.UpdateTargets(pos, q, FreshWorld(), new BoneSetupExtract.BoneBitList());

        CikTarget t = ctx.TargetAt(2);
        const float d1 = 61.8465844f; // sqrt(3825)
        AssertClose(new Vector(0, 0, 50), t.TraceHip, 1e-4f);
        AssertClose(new Vector(0, 15, -10), t.TraceKnee, 1e-4f);
        Assert.Equal(d1, t.TraceHipToKnee, 3);
        Assert.Equal(30f, t.TraceKneeToFoot, 3);
        Assert.Equal(d1, t.TraceHipToFoot, 3); // est == foot here

        var dir = new Vector(0, -15f / d1, -60f / d1);
        AssertClose(new Vector(0, 0, 50) + dir * (MathF.Abs(d1 - 30f) * 1.01f), t.TraceClosest, 2e-2f);
        AssertClose(new Vector(0, 0, 50) + dir * ((d1 + 30f) * 0.99f), t.TraceFarthest, 2e-2f);
        AssertClose(new Vector(0, 0, 50 - (d1 + 30f) * 0.99f), t.TraceLowest, 2e-2f);
    }

    /// <summary>Full-latch arm (3941-3953): the staleness gate is open
    /// (latched.iFramecounter == m_iFramecounter == 0, bHasLatch pre-set as
    /// SolveDependencies would leave it), est.latched == 1. ideal =
    /// (0,-15,-10) (zero offset); latched.pos (0,-15,-5) differs so
    /// deltaPos = latched - est = (0,0,5); deltaQ = QuaternionSM(-1,
    /// identity, identity) = normalize(scale(identity,-1)*identity) =
    /// (0,0,0,1). est freezes onto the latch: est.pos = (0,-15,-5);
    /// influence := latched = 1; iFramecounter re-stamped to fc.</summary>
    [Fact]
    public void UpdateTargets_FullLatch_FreezeAndDeltaTracking()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(), BoneMatrix3x4.Identity, Mask, flTime: 10f, iFramecounter: 0);
        ctx.AddDependencies(0, Array.Empty<float>(), 1f, 0.55f);

        CikTarget t = ctx.TargetAt(2);
        t.LatchedBHasLatch = true;
        t.LatchedInfluence = 0f;
        t.LatchedIFramecounter = 0;
        t.LatchedPos = new Vector(0, -15, -5);
        t.LatchedQ = Quaternion.Identity;

        (Vector[] pos, Quaternion[] q) = LegPose();
        ctx.UpdateTargets(pos, q, FreshWorld(), new BoneSetupExtract.BoneBitList());

        Assert.True(t.LatchedBHasLatch);
        AssertClose(new Vector(0, -15, -5), t.EstPos, 1e-3f);   // frozen on the latch
        AssertClose(new Vector(0, 0, 5), t.LatchedDeltaPos, 1e-3f);
        AssertQuat(Quaternion.Identity, t.LatchedDeltaQ, 1e-5f);
        Assert.Equal(1f, t.LatchedInfluence, 5);
        Assert.Equal(0, t.LatchedIFramecounter);
        Assert.True(t.LatchedBNeedsLatch);
    }

    /// <summary>Delta accumulation across two calls: call 1 (est.latched 1)
    /// COMPUTES deltaPos = latched - ideal = (2,0,0) and freezes est on the
    /// latch (the offset rides X: the GROUND fold tail zeroes delta Z every
    /// frame, 3906). Call 2 runs the same frame at cycle 0.6875 (est.latched
    /// = rule latched 0.5 * flRuleWeight 1): the decay arm scales the
    /// call-1-computed delta by (1 - (1-0.5)/1) = 0.5 and pushes est to
    /// ideal + scaled delta = (1,-15,-10). If the full-latch arm failed to
    /// store deltaPos, call 2 would see a zero delta and land the foot back
    /// on the ideal (0,-15,-10).</summary>
    [Fact]
    public void UpdateTargets_DeltaAccumulatesAcrossTwoCalls()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(), BoneMatrix3x4.Identity, Mask, flTime: 10f, iFramecounter: 0);
        ctx.AddDependencies(0, Array.Empty<float>(), 1f, 0.55f);

        CikTarget t = ctx.TargetAt(2);
        t.LatchedBHasLatch = true;
        t.LatchedInfluence = 0f;
        t.LatchedIFramecounter = 0;
        t.LatchedPos = new Vector(2, -15, -10);
        t.LatchedQ = Quaternion.Identity;

        (Vector[] pos, Quaternion[] q) = LegPose();
        ctx.UpdateTargets(pos, q, FreshWorld(), new BoneSetupExtract.BoneBitList());
        AssertClose(new Vector(2, 0, 0), t.LatchedDeltaPos, 1e-3f);    // computed by call 1

        // Same frame, ramp-out cycle: est.latched 0.5, influence 1 from call 1.
        ctx.AddDependencies(0, Array.Empty<float>(), 1f, 0.6875f);
        ctx.UpdateTargets(pos, q, FreshWorld(), new BoneSetupExtract.BoneBitList());

        Assert.Equal(0.5f, t.EstLatched, 5);
        AssertClose(new Vector(1, 0, 0), t.LatchedDeltaPos, 1e-3f);    // 0.5 * call-1 delta
        AssertClose(new Vector(1, -15, -10), t.EstPos, 1e-3f);         // ideal + scaled delta
        Assert.Equal(0.5f, t.LatchedInfluence, 5);
    }

    /// <summary>The staleness gate (3964-3971): with fc = 5 the latch
    /// survives only latched.iFramecounter in [fc-1, fc]; -9999 (Clear-
    /// Targets), fc-2 and fc+1 all fire it (bHasLatch false, influence 0),
    /// and a negative framecounter fires it for ANY stored counter. When
    /// the latch survives (est.latched 1) the freeze keeps est.pos at the
    /// latched (0,-15,-5); when it fires, est = ideal = (0,-15,-10).</summary>
    [Theory]
    [InlineData(5, 4, true)]      // fc-1: inside the window
    [InlineData(5, 5, true)]      // fc: inside
    [InlineData(5, 3, false)]     // fc-2: stale
    [InlineData(5, 6, false)]     // fc+1: from the future
    [InlineData(5, -9999, false)] // ClearTargets stamp
    [InlineData(-1, 5, false)]    // negative framecounter kills any latch
    public void UpdateTargets_StalenessGate(int fc, int latchedFc, bool keepLatch)
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(), BoneMatrix3x4.Identity, Mask, flTime: 10f, iFramecounter: fc);
        ctx.AddDependencies(0, Array.Empty<float>(), 1f, 0.55f);

        CikTarget t = ctx.TargetAt(2);
        t.LatchedBHasLatch = true;
        t.LatchedInfluence = 1f;
        t.LatchedIFramecounter = latchedFc;
        t.LatchedPos = new Vector(0, -15, -5);
        t.LatchedQ = Quaternion.Identity;

        (Vector[] pos, Quaternion[] q) = LegPose();
        ctx.UpdateTargets(pos, q, FreshWorld(), new BoneSetupExtract.BoneBitList());

        Assert.Equal(keepLatch, t.LatchedBHasLatch);
        Assert.Equal(keepLatch ? 1f : 0f, t.LatchedInfluence);
        AssertClose(keepLatch ? new Vector(0, -15, -5) : new Vector(0, -15, -10), t.EstPos, 1e-3f);
        Assert.Equal(fc, t.LatchedIFramecounter); // stamped in every arm
    }

    /// <summary>Error bookkeeping (3905-3935): slot 2 is latched 200 units
    /// above the foot — the full-latch arm computes deltaPos = (0,0,200),
    /// so d4 = |foot + delta - hip| = |(0,-15,140)| = 140.80 and d4*0.95 =
    /// 133.76 > d1+d2 = 91.85 with est.latched 1 > 0.2 -> the too-far arm
    /// stamps error.flTime = m_flTime (10). The twist arm agrees it is quiet:
    /// |deltaQ.w|*2-1 = 1 >= 0.707. Slot 5 carries the same far latched
    /// pose but its est.flWeight is 0, so the whole body (and its arms) is
    /// skipped — its flTime keeps its sentinel.</summary>
    [Fact]
    public void UpdateTargets_TooFarLatch_StampsErrorFlTime()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(), BoneMatrix3x4.Identity, Mask, flTime: 10f, iFramecounter: 0);
        ctx.AddDependencies(0, Array.Empty<float>(), 1f, 0.55f);

        CikTarget t = ctx.TargetAt(2);
        t.LatchedBHasLatch = true;
        t.LatchedInfluence = 1f;
        t.LatchedIFramecounter = 0;
        t.LatchedPos = new Vector(0, -15, 190);
        t.LatchedQ = Quaternion.Identity;
        t.ErrorFlTime = -5f;

        CikTarget t5 = ctx.TargetAt(5);
        t5.LatchedBHasLatch = true;
        t5.LatchedIFramecounter = 0;
        t5.LatchedPos = new Vector(0, -15, 190);
        t5.ErrorFlTime = -5f;

        (Vector[] pos, Quaternion[] q) = LegPose();
        ctx.UpdateTargets(pos, q, FreshWorld(), new BoneSetupExtract.BoneBitList());

        Assert.Equal(10f, t.ErrorFlTime);   // stamped by the too-far arm
        Assert.Equal(-5f, t5.ErrorFlTime);  // inactive target: arms never run
    }

    // ------------------------------------------------------------------
    // AutoIKRelease (stock oracle 4059-4152; our 755-827)
    // ------------------------------------------------------------------

    /// <summary>Entry + ramp-up (4067-4096): with flTime 10 and target 2's
    /// error.flTime 9.9, dt = 0.0999999 &lt; 0.5 opens the gate. Not yet
    /// bInError: ramp resets to 0, flErrorTime := flTime (9.9), bInError
    /// true. ft = 10 - 9.9 = 0.0999999 &lt; 0.25 -> ramp = min(0 + ft*4, 1)
    /// = 0.3999996. ramp &gt; 0 builds the release rule: chain = target
    /// chain 0, bone 0, type IK_RELEASE, slot = target index 2, flWeight =
    /// SimpleSpline(0.3999996) = 3x^2-2x^3 = 0.35200, flRuleWeight 1, and
    /// latched = 0 (dt &lt; 0.25). Chain 0's foot bone 3 is in the mask ->
    /// appended; tail stamps flErrorTime = 10. Target 5 (flTime 0, dt 10)
    /// never enters: its dirty ramp/flErrorTime survive.</summary>
    [Fact]
    public void AutoIKRelease_YoungError_EntersRampAndInsertsRule()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(), BoneMatrix3x4.Identity, Mask, flTime: 10f, iFramecounter: 0);

        CikTarget t = ctx.TargetAt(2);
        t.ErrorFlTime = 9.9f;
        t.ErrorRamp = 0.7f;        // dirty: entry must zero it first
        t.ErrorFlErrorTime = 9.5f;  // dirty: entry overwrites with flTime

        ctx.AutoIKRelease();

        Assert.True(t.ErrorBInError);
        Assert.Equal(0.4f, t.ErrorRamp, 4);
        Assert.Equal(10f, t.ErrorFlErrorTime, 4); // tail stamp after the rule
        Assert.Equal(1, ctx.IkChainRuleCount(0));
        IkContextIkRule r = ctx.IkChainRuleAt(0, 0);
        Assert.Equal(StudioConstants.IK_RELEASE, r.Type);
        Assert.Equal(0, r.Chain);
        Assert.Equal(0, r.Bone);
        Assert.Equal(2, r.Slot);
        Assert.Equal(0.352f, r.FlWeight, 4);
        Assert.Equal(1f, r.FlRuleWeight, 5);
        Assert.Equal(0f, r.Latched);  // dt < 0.25 arm

        CikTarget t5 = ctx.TargetAt(5);
        t5.ErrorRamp = 0.7f;
        t5.ErrorFlErrorTime = 9.5f;
        ctx.AutoIKRelease();          // flTime 0 for t5 -> still skipped
        Assert.Equal(0.7f, t5.ErrorRamp, 5);
        Assert.Equal(9.5f, t5.ErrorFlErrorTime, 5);
        Assert.False(t5.ErrorBInError);
        Assert.Equal(2, ctx.IkChainRuleCount(0)); // only target 2 re-added (ramp clamped at...)
    }

    /// <summary>The gate (4067-4069): bInError false and dt = 10 - 9 = 1.0
    /// >= 0.5 -> the target is untouched: no ramp move, no flag, no rule,
    /// no flErrorTime stamp (all live inside the gate).</summary>
    [Fact]
    public void AutoIKRelease_StaleError_SkipsTarget()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(), BoneMatrix3x4.Identity, Mask, flTime: 10f, iFramecounter: 0);

        CikTarget t = ctx.TargetAt(2);
        t.ErrorFlTime = 9.0f;
        t.ErrorRamp = 0.7f;
        t.ErrorFlErrorTime = 9.5f;

        ctx.AutoIKRelease();

        Assert.False(t.ErrorBInError);
        Assert.Equal(0.7f, t.ErrorRamp, 5);
        Assert.Equal(9.5f, t.ErrorFlErrorTime, 5);
        Assert.Equal(0, ctx.IkChainRuleCount(0));
    }

    /// <summary>Decay to zero (4077-4085, 4145-4148): already bInError so
    /// the entry block is skipped and the dirty ramp 0.7 is kept. dt =
    /// 10 - 9.3 = 0.7 >= 0.25 (decay arm); ft = 10 - 9.8 = 0.1999998, so
    /// ramp = max(0.7 - 0.7999992, 0) = 0. ramp == 0 takes the else:
    /// bInError = false, no rule — and the tail still stamps flErrorTime
    /// = 10.</summary>
    [Fact]
    public void AutoIKRelease_SustainedError_RampDecaysToZeroClearsError()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(), BoneMatrix3x4.Identity, Mask, flTime: 10f, iFramecounter: 0);

        CikTarget t = ctx.TargetAt(2);
        t.ErrorBInError = true;
        t.ErrorFlTime = 9.3f;
        t.ErrorRamp = 0.7f;
        t.ErrorFlErrorTime = 9.8f;

        ctx.AutoIKRelease();

        Assert.Equal(0f, t.ErrorRamp);
        Assert.False(t.ErrorBInError);
        Assert.Equal(10f, t.ErrorFlErrorTime, 4);
        Assert.Equal(0, ctx.IkChainRuleCount(0));
    }

    /// <summary>Mid-decay keeps the rule and latches it (4084, 4094-4096):
    /// bInError true, dt = 0.7 (decay arm, so latched = flWeight, not 0).
    /// ft = 10 - 9.95 = 0.0500002 -> ramp = max(0.3 - 0.2000008, 0) =
    /// 0.0999992 > 0; flWeight = SimpleSpline(0.0999992) = 0.02800; dt >=
    /// 0.25 so latched = flWeight = 0.02800. bInError stays true (only the
    /// ramp==0 arm clears it) and the tail stamps flErrorTime = 10.</summary>
    [Fact]
    public void AutoIKRelease_MidDecay_InsertsLatchedReleaseRule()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(), BoneMatrix3x4.Identity, Mask, flTime: 10f, iFramecounter: 0);

        CikTarget t = ctx.TargetAt(2);
        t.ErrorBInError = true;
        t.ErrorFlTime = 9.3f;
        t.ErrorRamp = 0.3f;
        t.ErrorFlErrorTime = 9.95f;

        ctx.AutoIKRelease();

        Assert.Equal(0.1f, t.ErrorRamp, 4);
        Assert.True(t.ErrorBInError);
        Assert.Equal(10f, t.ErrorFlErrorTime, 4);
        Assert.Equal(1, ctx.IkChainRuleCount(0));
        IkContextIkRule r = ctx.IkChainRuleAt(0, 0);
        Assert.Equal(0.028f, r.FlWeight, 4);
        Assert.Equal(0.028f, r.Latched, 4);
    }

    /// <summary>The boneMask gate (4111-4114): the ramp-up arm runs first
    /// (ramp = 0.5 + 0.3999996 = 0.8999996 > 0, rule built), then the
    /// chain's foot bone 3 is outside the mask -> bInError = false and
    /// stock's <c>continue</c> — so NO rule is appended AND the tail
    /// flErrorTime stamp is skipped (stays 9.9). The fixture masks the foot
    /// bone itself out (flags 0).</summary>
    [Fact]
    public void AutoIKRelease_MaskedFoot_ClearsErrorAndSkipsRule()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(footFlags: 0), BoneMatrix3x4.Identity, Mask, flTime: 10f, iFramecounter: 0);

        CikTarget t = ctx.TargetAt(2);
        t.ErrorBInError = true;
        t.ErrorFlTime = 9.9f;
        t.ErrorRamp = 0.5f;
        t.ErrorFlErrorTime = 9.9f;

        ctx.AutoIKRelease();

        Assert.Equal(0.9f, t.ErrorRamp, 4);   // ramped before the gate hit
        Assert.False(t.ErrorBInError);
        Assert.Equal(9.9f, t.ErrorFlErrorTime, 5); // continue skipped the stamp
        Assert.Equal(0, ctx.IkChainRuleCount(0));
    }

    // ------------------------------------------------------------------
    // SolveDependencies (stock oracle 4158-4376; our 1100-1279)
    // Two-bone leg from LegPose: hip (0,0,50), knee (0,15,-10), foot
    // (0,-15,-10); l1 = |knee-hip| = sqrt(3825) = 61.847,
    // l2 = |foot-knee| = 30, l1+l2 = 91.847,
    // minDist = max(|l1-l2|*1.15, min*0.15) = 36.62.
    // ------------------------------------------------------------------

    /// <summary>IK_SELF with Bone -1 (chain arm runs): the seed is the pose
    /// foot (0,-15,-10) with weight 0. The rule (error rows (0,10,-20),
    /// flWeight 1 at cycle 0.55, flRuleWeight 0.5) gives flWeight = 0.5:
    /// F = 0*(1-0.5)+0.5 = 0.5, Pos = (0,-15,-10)*0.5 + (0,10,-20)*0.5 =
    /// (0,-2.5,-15). Targets are inactive (est.flWeight 0 — no
    /// UpdateTargets). The solver takes the knee-deriving chain overload
    /// (no recorded kneeDir): ikKneeDir = normalise((0,15,-60) -
    /// (0,-15,-60)) = (0,1,0); ikFoot = (0,-2.5,-65), |ikFoot| = 65.048 is
    /// inside the annulus (36.62 < 65.05 < 91.83), and
    /// FindD = (65.048 + 2925/65.048)/2 = 55.01 satisfies
    /// rr-b = 35.05 < d < a = 61.85, so the foot lands at
    /// ikFoot + hip = (0,-2.5,-15) and the quaternion slam (chainResult.Q
    /// = slerp(id,id,.5) = id) leaves the rotation identity.</summary>
    [Fact]
    public void SolveDeps_SelfRule_BlendsChainTargetAndSolves()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(errorPos: new Vector(0, 10, -20),
            ruleType: StudioConstants.IK_SELF, ruleBone: -1),
            BoneMatrix3x4.Identity, Mask, flTime: 10f, iFramecounter: 0);
        ctx.AddDependencies(0, Array.Empty<float>(), 0.5f, 0.55f);
        Assert.Equal(1, ctx.IkChainRuleCount(0));

        (Vector[] pos, Quaternion[] q) = LegPose();
        BoneMatrix3x4[] world = FreshWorld();
        ctx.SolveDependencies(pos, q, world, new BoneSetupExtract.BoneBitList());

        AssertClose(new Vector(0, -2.5f, -15), world[3].Position, 1e-3f);
        AssertClose(new Vector(1, 0, 0), new Vector(world[3].Row0.X, world[3].Row0.Y, world[3].Row0.Z), 1e-3f);
        AssertClose(new Vector(0, 1, 0), new Vector(world[3].Row1.X, world[3].Row1.Y, world[3].Row1.Z), 1e-3f);
        // The knee is re-solved: |knee-hip| = l1 = sqrt(3825) (Solve emits
        // d*X + e*Y with d^2+e^2 = a^2), and it moved off the pose knee.
        Assert.True(MathF.Abs((world[2].Position - world[1].Position).Length() - 61.84659f) <= 1e-2f);
        Assert.True((world[2].Position - new Vector(0, 15, -10)).Length() > 1f);
    }

    /// <summary>The weight early-out (4377-4381 region, our 1249-1252): a
    /// SELF rule whose window (start .25, end .75) excludes cycle 0.1 is
    /// rejected by Studio_IKSequenceError (flWeight <= 0.001, non-GROUND
    /// has no looping arm), so the chain list stays empty, the seed weight
    /// 0 skips the solver, and the foot keeps its pose world position
    /// (0,-15,-10) written by the init BuildBoneChain.</summary>
    [Fact]
    public void SolveDeps_ZeroWeightRule_EarlyOutKeepsPose()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(errorPos: new Vector(0, 10, -20),
            ruleType: StudioConstants.IK_SELF, ruleBone: -1),
            BoneMatrix3x4.Identity, Mask, flTime: 10f, iFramecounter: 0);
        ctx.AddDependencies(0, Array.Empty<float>(), 1f, 0.1f);
        Assert.Equal(0, ctx.IkChainRuleCount(0));

        (Vector[] pos, Quaternion[] q) = LegPose();
        BoneMatrix3x4[] world = FreshWorld();
        ctx.SolveDependencies(pos, q, world, new BoneSetupExtract.BoneBitList());

        AssertClose(new Vector(0, -15, -10), world[3].Position, 1e-3f);
    }

    /// <summary>A recorded kneeDir on link 0 routes the chain overload into
    /// the 6-arg solver with the CURRENT knee position and the knee
    /// direction rotated into world (identity hip here leaves (0,1,0)).
    /// SELF at full weight (flRuleWeight 1 -> product 1 clears the chain
    /// and blends fully): Pos = (0,10,-20); ikFoot = (0,10,-70),
    /// |ikFoot| = 70.71 inside the annulus; the knee target is exaggerated
    /// to (0, 15 + 9184.7, -60), whose direction still passes the accept
    /// test (FindD = 56.04 < a = 61.85, > rr-b = 40.71), so the foot lands
    /// exactly on the target. |knee - hip| = sqrt(3825) holds whatever the
    /// exaggeration (Solve emits the thigh-length knee).</summary>
    [Fact]
    public void SolveDeps_RecordedKneeDir_SolvesViaKneeTarget()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(errorPos: new Vector(0, 10, -20), kneeDir: new Vector(0, 1, 0),
            ruleType: StudioConstants.IK_SELF, ruleBone: -1),
            BoneMatrix3x4.Identity, Mask, flTime: 10f, iFramecounter: 0);
        ctx.AddDependencies(0, Array.Empty<float>(), 1f, 0.55f);
        Assert.Equal(1, ctx.IkChainRuleCount(0));

        (Vector[] pos, Quaternion[] q) = LegPose();
        BoneMatrix3x4[] world = FreshWorld();
        ctx.SolveDependencies(pos, q, world, new BoneSetupExtract.BoneBitList());

        AssertClose(new Vector(0, 10, -20), world[3].Position, 1e-3f);
        Assert.True(MathF.Abs((world[2].Position - world[1].Position).Length() - 61.84659f) <= 1e-2f);
        Assert.True((world[2].Position - new Vector(0, 15, -10)).Length() > 1f); // knee solved
    }

    /// <summary>IK_RELEASE blends the accumulated target back toward the
    /// pose (4296-4310 region, our 1186-1200). SELF first (flRuleWeight
    /// 0.5): F 0.5, Pos (0,-2.5,-15). Then RELEASE (same window, so its
    /// stored flWeight is 1, *flRuleWeight 0.5 = 0.5): p2 re-reads the
    /// (memoised, unchanged) pose foot (0,-15,-10):
    /// Pos = (0,-2.5,-15)*0.5 + (0,-15,-10)*0.5 = (0,-8.75,-12.5);
    /// F = 0.5*(1-0.5)+0.5 = 0.75. The solver lands the foot on the blended
    /// point (|ikFoot| = |(0,-8.75,-62.5)| = 63.11 in the annulus;
    /// FindD = 54.73 passes), a quarter of the way from the pose toward the
    /// SELF error instead of the halfway no-release position
    /// (0,-2.5,-15).</summary>
    [Fact]
    public void SolveDeps_ReleaseRule_BlendsBackTowardPose()
    {
        var ctx = new CIKContext();
        ctx.Init(SelfReleaseHdr(new Vector(0, 10, -20)),
            BoneMatrix3x4.Identity, Mask, flTime: 10f, iFramecounter: 0);
        ctx.AddDependencies(0, Array.Empty<float>(), 0.5f, 0.55f);
        Assert.Equal(2, ctx.IkChainRuleCount(0)); // both rules under .999

        (Vector[] pos, Quaternion[] q) = LegPose();
        BoneMatrix3x4[] world = FreshWorld();
        ctx.SolveDependencies(pos, q, world, new BoneSetupExtract.BoneBitList());

        AssertClose(new Vector(0, -8.75f, -12.5f), world[3].Position, 1e-3f);
    }

    // ------------------------------------------------------------------
    // AddAllLocks / SolveAllLocks (stock oracle 4432-4512; our 1287-1422)
    // ------------------------------------------------------------------

    /// <summary>AddAllLocks (4432-4480): one IK_WORLD lock per chain,
    /// chain = slot = i, seeded with the current world pose of the chain's
    /// end bone — foot (0,-15,-10) with identity quaternion — plus the knee
    /// bookkeeping (KneeDir (0,1,0) rotated by the identity hip, KneePos =
    /// knee world (0,15,-10)) because the chain records a kneeDir. A second
    /// call composes: LockCount grows to 2 and the second slot carries the
    /// same fields (ikOffset append).</summary>
    [Fact]
    public void AddAllLocks_SeedsWorldLockPerChainWithKnee()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(kneeDir: new Vector(0, 1, 0)), BoneMatrix3x4.Identity, Mask);
        (Vector[] pos, Quaternion[] q) = LegPose();

        ctx.AddAllLocks(pos, q);
        Assert.Equal(1, ctx.LockCount);
        var lk = ctx.LockAt(0);
        Assert.Equal(StudioConstants.IK_WORLD, lk.Type);
        Assert.Equal(0, lk.Chain);
        Assert.Equal(0, lk.Slot);
        AssertClose(new Vector(0, -15, -10), lk.Pos, 1e-3f);
        AssertQuat(Quaternion.Identity, lk.Q, 1e-6f);
        AssertClose(new Vector(0, 1, 0), lk.KneeDir, 1e-3f);
        AssertClose(new Vector(0, 15, -10), lk.KneePos, 1e-3f);

        ctx.AddAllLocks(pos, q);
        Assert.Equal(2, ctx.LockCount);
        var lk1 = ctx.LockAt(1);
        AssertClose(new Vector(0, -15, -10), lk1.Pos, 1e-3f);
        AssertClose(new Vector(0, 1, 0), lk1.KneeDir, 1e-3f);
    }

    /// <summary>The boneMask gate (4444-4448): a chain whose foot bone is
    /// outside the mask keeps its appended slot at the memset-zero rule —
    /// type 0 (not IK_WORLD), zero pos, and a ZERO quaternion (not
    /// identity): the seed loop writes nothing for masked chains.</summary>
    [Fact]
    public void AddAllLocks_MaskExcludedFoot_LeavesSlotZeroed()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(footFlags: 0), BoneMatrix3x4.Identity, Mask);
        (Vector[] pos, Quaternion[] q) = LegPose();

        ctx.AddAllLocks(pos, q);

        Assert.Equal(1, ctx.LockCount);
        var lk = ctx.LockAt(0);
        Assert.Equal(0, lk.Type);
        AssertClose(Vector.Zero, lk.Pos, 0f);
        Assert.Equal(0f, lk.Q.W); // zeroed, NOT identity
    }

    /// <summary>SolveAllLocks (4489-4502 + SolveLock 4402-4451): every slot is
    /// solved against a synthetic lock whose chain is the SLOT INDEX (the
    /// two-chain fixture proves slot 1 reaches chain 1), full position
    /// weight, zero local-Q weight. Stock rents its boneToWorld from
    /// g_MatrixPool internally, so the observable is pos/q — the solve
    /// back-computes them via SolveBone; rebuilding the chain through
    /// BuildBoneChain must then land the foot on the lock. The pose puts
    /// the foot at world (0,-15,10) (z +20 off the lock); both lock slots
    /// say (0,-15,-10), so after solving every slot the foot matrix sits
    /// exactly on the lock position with the lock's identity quaternion
    /// (position blend p3 = p1*(1-1) + lockPos*1; |ikFoot| = |(0,-15,-60)|
    /// = 61.85 in the annulus; FindD = 47.38 passes rr-b = 31.85 < d < a =
    /// 61.85; the second slot re-solves the same bones to the same target,
    /// idempotent).</summary>
    [Fact]
    public void SolveAllLocks_MovesEverySlotToItsLock()
    {
        var ctx = new CIKContext();
        ctx.Init(GroundHdr(twoChains: true), BoneMatrix3x4.Identity, Mask);
        (Vector[] posePos, Quaternion[] poseQ) = LegPose();
        ctx.AddAllLocks(posePos, poseQ);
        Assert.Equal(2, ctx.LockCount);
        AssertClose(new Vector(0, -15, -10), ctx.LockAt(0).Pos, 1e-3f);
        AssertClose(new Vector(0, -15, -10), ctx.LockAt(1).Pos, 1e-3f);

        (Vector[] pos, Quaternion[] q) = LegPose();
        pos[3] = new Vector(0, -30, 20); // foot world would be (0,-15,10)
        ctx.SolveAllLocks(pos, q);

        BoneMatrix3x4[] world = FreshWorld();
        ctx.BuildBoneChain(pos, q, world, 3);
        AssertClose(new Vector(0, -15, -10), world[3].Position, 1e-3f);
        AssertClose(new Vector(1, 0, 0), new Vector(world[3].Row0.X, world[3].Row0.Y, world[3].Row0.Z), 1e-3f);
        AssertClose(new Vector(0, 1, 0), new Vector(world[3].Row1.X, world[3].Row1.Y, world[3].Row1.Z), 1e-3f);
        AssertClose(new Vector(0, 0, 1), new Vector(world[3].Row2.X, world[3].Row2.Y, world[3].Row2.Z), 1e-3f);
    }


}
