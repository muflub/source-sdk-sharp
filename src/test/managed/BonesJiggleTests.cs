using System;
using System.Buffers.Binary;
using System.Numerics;
using SourceSharp.Shared.Bones;
using Xunit;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Tests;

/// <summary>
/// Facts for <see cref="JiggleBones"/>, the managed port of CJiggleBones
/// (public/jigglebones.cpp, tier3.a). Expectations are derived by hand from
/// the stock body over goal matrices whose axes are the world's, so each
/// integration step is a short sum. Stock has no gtest for it.
/// </summary>
public class BonesJiggleTests
{
    private const float T0 = 10.0f;

    /// <summary>Goal with left = x, up = y, forward = z at <paramref name="at"/>.</summary>
    private static BoneMatrix3x4 Goal(Vector at)
    {
        BoneMatrix3x4 m = BoneMatrix3x4.Identity;
        m.Position = at;
        return m;
    }

    /// <summary>Goal with left = y, up = z, forward = x (a bone lying flat).</summary>
    private static BoneMatrix3x4 FlatGoal() => new()
    {
        Row0 = new Vector4(0f, 0f, 1f, 0f),
        Row1 = new Vector4(1f, 0f, 0f, 0f),
        Row2 = new Vector4(0f, 1f, 0f, 0f),
    };

    private static Vector Forward(in BoneMatrix3x4 m) => new(m.Row0.Z, m.Row1.Z, m.Row2.Z);

    private static BoneMatrix3x4 Step(JiggleBones jiggle, float t, in JiggleBoneParams p, in BoneMatrix3x4 goal,
                                      float cutoff = JiggleBones.DefaultFramerateCutoff)
    {
        BoneMatrix3x4 bone = default;
        jiggle.BuildJiggleTransformations(0, t, p, goal, ref bone, cutoff);
        return bone;
    }

    // ---- state -------------------------------------------------------------

    [Fact]
    public void TheSameBoneGetsTheSameSpring()
    {
        JiggleBones jiggle = new();
        JiggleData a = jiggle.GetJiggleData(3, T0, Vector.Zero, Vector.UnitZ);
        Assert.Same(a, jiggle.GetJiggleData(3, T0 + 1f, Vector.One, Vector.One));
    }

    [Fact]
    public void AnotherBoneGetsItsOwnSpring()
    {
        JiggleBones jiggle = new();
        JiggleData a = jiggle.GetJiggleData(3, T0, Vector.Zero, Vector.UnitZ);
        Assert.NotSame(a, jiggle.GetJiggleData(4, T0, Vector.Zero, Vector.UnitZ));
    }

    [Fact]
    public void ANewSpringStartsAtRestAtTheGoal()
    {
        JiggleData d = new JiggleBones().GetJiggleData(3, T0, new Vector(1f, 2f, 3f), new Vector(1f, 2f, 13f));
        Assert.Equal((3, T0, new Vector(1f, 2f, 3f), new Vector(1f, 2f, 13f), Vector.Zero, new Vector(0f, 0f, 1f), new Vector(1f, 2f, 3f)),
                     (d.Bone, d.LastUpdate, d.BasePos, d.TipPos, d.TipVel, d.BoingDir, d.LastBoingPos));
    }

    [Fact]
    public void ANewSpringJigglesForAtLeastSixteenFrames()
    {
        JiggleData d = new JiggleBones().GetJiggleData(3, T0, Vector.Zero, Vector.UnitZ);
        Assert.Equal((0, 16), (d.UseGoalMatrixCount, d.UseJiggleBoneCount));
    }

    [Fact]
    public void InitLeavesTheUseCountersAlone()
    {
        JiggleData d = new() { UseGoalMatrixCount = 5, UseJiggleBoneCount = 7, TipVel = Vector.One };
        d.Init(1, T0, Vector.Zero, Vector.UnitZ);
        Assert.Equal((5, 7, Vector.Zero), (d.UseGoalMatrixCount, d.UseJiggleBoneCount, d.TipVel));
    }

    // ---- the integrator ----------------------------------------------------

    [Fact]
    public void ABoneWithNoFlexDrawsTheGoal()
    {
        BoneMatrix3x4 goal = Goal(new Vector(1f, 2f, 3f));
        Assert.Equal(goal, Step(new JiggleBones(), T0, new JiggleBoneParams { Length = 10f }, goal));
    }

    [Fact]
    public void ARigidTipDroopsUnderItsMass()
    {
        // First frame: deltaT 0 is clamped to 0.001. Gravity tipMass 100 ->
        // tipVel.z = -0.1, tipPos = (10, 0, -0.0001): the forward axis tips
        // down by 1e-5.
        JiggleBoneParams p = new() { Flags = JiggleBoneParams.IsRigid, Length = 10f, TipMass = 100f };
        Vector forward = Forward(Step(new JiggleBones(), T0, p, FlatGoal()));
        Assert.Equal(-1e-5f, forward.Z, 1e-7f);
    }

    [Fact]
    public void TheBoneStaysAtTheGoalBase()
    {
        JiggleBoneParams p = new() { Flags = JiggleBoneParams.IsRigid, Length = 10f, TipMass = 100f };
        BoneMatrix3x4 goal = FlatGoal();
        goal.Position = new Vector(5f, 6f, 7f);
        Assert.Equal(new Vector(5f, 6f, 7f), Step(new JiggleBones(), T0, p, goal).Position);
    }

    [Fact]
    public void ALengthConstraintHoldsTheTipAtLength()
    {
        JiggleBones jiggle = new();
        JiggleBoneParams p = new()
        {
            Flags = JiggleBoneParams.IsRigid | JiggleBoneParams.HasLengthConstraint,
            Length = 10f,
            TipMass = 1000f,
        };
        for (int i = 0; i < 20; i++)
        {
            Step(jiggle, T0 + 0.01f * i, p, FlatGoal());
        }

        JiggleData d = jiggle.GetJiggleData(0, T0, Vector.Zero, Vector.Zero);
        Assert.Equal(10f, d.TipPos.Length(), 1e-4f);
    }

    [Fact]
    public void AYawLimitClipsTheTipOntoTheLimitPlane()
    {
        // The tip is seeded at 90 degrees of yaw; the limit is 0.5 rad, so the
        // tip is projected onto the limit's up/forward plane: forward =
        // (sin 0.5, 0, cos 0.5) and the velocity is zeroed.
        JiggleBones jiggle = new();
        JiggleData d = jiggle.GetJiggleData(0, T0, Vector.Zero, new Vector(0f, 0f, 10f));
        d.TipPos = new Vector(10f, 0f, 1f);
        d.TipVel = new Vector(0f, 0f, 0f);
        JiggleBoneParams p = new()
        {
            Flags = JiggleBoneParams.IsRigid | JiggleBoneParams.HasYawConstraint,
            Length = 10f,
            MinYaw = -0.5f,
            MaxYaw = 0.5f,
        };
        Vector forward = Forward(Step(jiggle, T0, p, Goal(Vector.Zero)));
        Assert.True(Vector.Distance(new Vector(MathF.Sin(0.5f), 0f, MathF.Cos(0.5f)), forward) < 1e-5f, $"forward {forward}");
    }

    [Fact]
    public void AFlexibleTipIsPulledTowardTheGoalTip()
    {
        // Goal tip (0, 0, 10); the tip seeded 1 to the left: localError.x = -1,
        // yaw stiffness 100 -> tipAccel.x = -100 over the clamped 0.001 step.
        JiggleBones jiggle = new();
        JiggleData d = jiggle.GetJiggleData(0, T0, Vector.Zero, new Vector(0f, 0f, 10f));
        d.TipPos = new Vector(1f, 0f, 10f);
        JiggleBoneParams p = new() { Flags = JiggleBoneParams.IsFlexible, Length = 10f, YawStiffness = 100f };
        Step(jiggle, T0, p, Goal(Vector.Zero));
        float vel = -100f * 0.001f;
        Assert.Equal((vel, 1f + vel * 0.001f), (d.TipVel.X, d.TipPos.X));
    }

    [Fact]
    public void APitchLimitClipsTheTipOntoTheLimitPlane()
    {
        JiggleBones jiggle = new();
        JiggleData d = jiggle.GetJiggleData(0, T0, Vector.Zero, new Vector(0f, 0f, 10f));
        d.TipPos = new Vector(0f, 10f, 1f);
        JiggleBoneParams p = new()
        {
            Flags = JiggleBoneParams.IsRigid | JiggleBoneParams.HasPitchConstraint,
            Length = 10f,
            MinPitch = -0.5f,
            MaxPitch = 0.5f,
        };
        Vector forward = Forward(Step(jiggle, T0, p, Goal(Vector.Zero)));
        Assert.True(Vector.Distance(new Vector(0f, MathF.Sin(0.5f), MathF.Cos(0.5f)), forward) < 1e-5f, $"forward {forward}");
    }

    [Fact]
    public void AnAngleLimitPullsTheTipBackInsideTheCone()
    {
        // Seeded 1.47 rad off the goal axis; the limit is 0.1 rad.
        JiggleBones jiggle = new();
        JiggleData d = jiggle.GetJiggleData(0, T0, Vector.Zero, new Vector(0f, 0f, 10f));
        d.TipPos = new Vector(10f, 0f, 1f);
        JiggleBoneParams p = new() { Flags = JiggleBoneParams.IsRigid | JiggleBoneParams.HasAngleConstraint, Length = 10f, AngleLimit = 0.1f };
        Vector forward = Forward(Step(jiggle, T0, p, Goal(Vector.Zero)));
        float angle = MathF.Acos(forward.Z);
        Assert.True(angle > 0f && angle < 0.1f, $"angle {angle}");
    }

    [Theory]
    [InlineData(false, 1f)]
    [InlineData(true, -1f)]
    public void AFlippedCoordinateSystemBuildsLeftTheOtherWay(bool flipped, float leftY)
    {
        // Flat goal, tip at rest along x: left = up x forward (or forward x up).
        JiggleBoneParams p = new() { Flags = JiggleBoneParams.IsRigid, Length = 10f };
        BoneMatrix3x4 bone = default;
        new JiggleBones().BuildJiggleTransformations(0, T0, p, FlatGoal(), ref bone, JiggleBones.DefaultFramerateCutoff, flipped);
        Assert.Equal(new Vector(0f, leftY, 0f), new Vector(bone.Row0.X, bone.Row1.X, bone.Row2.X));
    }

    [Fact]
    public void ABaseSpringIsClampedToItsBox()
    {
        // At rest at the origin; the goal jumps 5 along left, the spring has
        // no stiffness, so the base lags and is clamped to left -1: base at
        // (4, 0, 0), drawn with the goal's own orientation.
        JiggleBones jiggle = new();
        JiggleBoneParams p = new()
        {
            Flags = JiggleBoneParams.HasBaseSpring,
            Length = 10f,
            BaseMinLeft = -1f,
            BaseMaxLeft = 1f,
            BaseMinUp = -1f,
            BaseMaxUp = 1f,
            BaseMinForward = -1f,
            BaseMaxForward = 1f,
        };
        Step(jiggle, T0, p, Goal(Vector.Zero));
        BoneMatrix3x4 bone = Step(jiggle, T0 + 0.01f, p, Goal(new Vector(5f, 0f, 0f)));
        Assert.Equal(Goal(new Vector(4f, 0f, 0f)), bone);
    }

    [Fact]
    public void ABoingSquashesAcrossAndStretchesAlongItsAxis()
    {
        // No impact yet: boingDir is the initial +z, boingTime 0.001 with no
        // damping or frequency -> flex = amplitude 0.5: diag(1.5, 1.5, 0.5).
        JiggleBoneParams p = new() { Flags = JiggleBoneParams.IsBoing, Length = 10f, BoingAmplitude = 0.5f };
        BoneMatrix3x4 bone = Step(new JiggleBones(), T0, p, Goal(new Vector(1f, 2f, 3f)));
        BoneMatrix3x4 expected = new()
        {
            Row0 = new Vector4(1.5f, 0f, 0f, 1f),
            Row1 = new Vector4(0f, 1.5f, 0f, 2f),
            Row2 = new Vector4(0f, 0f, 0.5f, 3f),
        };
        Assert.Equal(expected, bone);
    }

    [Fact]
    public void AFullyDampedBoingDrawsTheGoal()
    {
        JiggleBoneParams p = new() { Flags = JiggleBoneParams.IsBoing, Length = 10f, BoingAmplitude = 0.5f, BoingDampingRate = 1000f };
        BoneMatrix3x4 goal = Goal(new Vector(1f, 2f, 3f));
        Assert.Equal(goal, Step(new JiggleBones(), T0, p, goal));
    }

    [Fact]
    public void AnImpactReAimsTheBoingAgainstTheMotion()
    {
        // 0.6 s at rest (boingTime past the 0.5 s re-boing guard), then the
        // goal moves 1 along +x in 0.01 s: speed 100 > 5 and 100 differs from
        // the resting speed by more than the impact speed -> boingDir = -x.
        JiggleBones jiggle = new();
        JiggleBoneParams p = new() { Flags = JiggleBoneParams.IsBoing, Length = 10f, BoingImpactSpeed = 10f, BoingImpactAngle = -2f };
        float t = T0;
        for (int i = 0; i <= 60; i++)
        {
            t = T0 + 0.01f * i;
            Step(jiggle, t, p, Goal(Vector.Zero));
        }

        Step(jiggle, t + 0.01f, p, Goal(new Vector(1f, 0f, 0f)));
        JiggleData d = jiggle.GetJiggleData(0, t, Vector.Zero, Vector.Zero);
        Assert.Equal((new Vector(-1f, 0f, 0f), 0f), (d.BoingDir, d.BoingTime));
    }

    // ---- the frame-rate cutoff and re-init ---------------------------------

    [Fact]
    public void BelowTheCutoffTheFirstSixteenFramesStillJiggle()
    {
        JiggleBones jiggle = new();
        JiggleBoneParams p = new() { Flags = JiggleBoneParams.IsRigid, Length = 10f, TipMass = 100f };
        BoneMatrix3x4 goal = FlatGoal();
        BoneMatrix3x4 bone = default;
        for (int i = 0; i < 16; i++)
        {
            bone = Step(jiggle, T0 + 0.01f * i, p, goal, cutoff: 0f);
        }

        Assert.NotEqual(goal, bone);
    }

    [Fact]
    public void BelowTheCutoffTheSeventeenthFrameDrawsTheGoal()
    {
        JiggleBones jiggle = new();
        JiggleBoneParams p = new() { Flags = JiggleBoneParams.IsRigid, Length = 10f, TipMass = 100f };
        BoneMatrix3x4 goal = FlatGoal();
        BoneMatrix3x4 bone = default;
        for (int i = 0; i < 17; i++)
        {
            bone = Step(jiggle, T0 + 0.01f * i, p, goal, cutoff: 0f);
        }

        Assert.Equal(goal, bone);
    }

    [Fact]
    public void AGapOverHalfASecondRestartsTheSpring()
    {
        JiggleBones jiggle = new();
        JiggleBoneParams p = new() { Flags = JiggleBoneParams.IsRigid, Length = 10f, TipMass = 100f };
        for (int i = 0; i < 5; i++)
        {
            Step(jiggle, T0 + 0.01f * i, p, FlatGoal());
        }

        // re-init at T0+1 puts the spring back at rest (velocity zero, clock
        // T0+1); the frame's clamped 0.001 step then gives tipVel.z = -100 *
        // 0.001 = -0.1 from rest, not on top of five frames of fall.
        Step(jiggle, T0 + 1f, p, FlatGoal());
        JiggleData d = jiggle.GetJiggleData(0, T0, Vector.Zero, Vector.Zero);
        Assert.Equal((T0 + 1f, -0.1f), (d.LastUpdate, d.TipVel.Z));
    }

    // ---- reading the record ------------------------------------------------

    private static StudioHdr JiggleModel(int procType, float[] record)
    {
        StudioHdrBuilder b = new();
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "root", Parent = -1, Flags = 0x100 });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "tail", Parent = 0, Flags = 0x100 | ProceduralBoneRule.BoneAlwaysProcedural });
        byte[] built = b.Build();
        byte[] image = new byte[built.Length + 4 * record.Length + 4];
        built.CopyTo(image, 0);
        int at = (built.Length + 3) & ~3;
        for (int i = 0; i < record.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(image.AsSpan(at + 4 * i, 4), record[i]);
        }

        int boneIndex = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(160, 4));
        int bone1 = boneIndex + 216;
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(bone1 + 164, 4), procType);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(bone1 + 168, 4), at - bone1);
        Assert.True(StudioHdr.TryCreate(image, b.Resolver, out StudioHdr? hdr) && hdr is not null);
        return hdr!;
    }

    private static float[] Record()
    {
        float[] r = new float[35];
        for (int i = 1; i < r.Length; i++)
        {
            r[i] = i + 0.5f; // field i (after flags) holds i + 0.5
        }

        r[0] = BitConverter.Int32BitsToSingle(JiggleBoneParams.IsFlexible | JiggleBoneParams.IsBoing);
        return r;
    }

    [Fact]
    public void TheRecordIsReadFieldForField()
    {
        Assert.True(JiggleBoneParams.TryRead(JiggleModel(ProceduralBoneRule.ProcJiggle, Record()), 1, out JiggleBoneParams p));
        Assert.Equal((JiggleBoneParams.IsFlexible | JiggleBoneParams.IsBoing, 1.5f, 2.5f, 9.5f, 10.5f, 18.5f, 29.5f, 34.5f),
                     (p.Flags, p.Length, p.TipMass, p.AngleLimit, p.MinYaw, p.BaseMass, p.BaseForwardFriction, p.BoingAmplitude));
    }

    [Fact]
    public void ABoneThatIsNotAJiggleBoneHasNoRecord()
    {
        Assert.False(JiggleBoneParams.TryRead(JiggleModel(ProceduralBoneRule.ProcQuatInterp, Record()), 1, out _));
    }

    [Fact]
    public void ABoneOutOfRangeHasNoRecord()
    {
        Assert.False(JiggleBoneParams.TryRead(JiggleModel(ProceduralBoneRule.ProcJiggle, Record()), 2, out _));
    }
}
