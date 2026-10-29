using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The constraint and spring tables (plan_physics_bones_native_zero 2.4), held
/// to what shell_constraints.cpp's SourceSharp_CreateConstraint and its
/// siblings did at 4202f899b: which end is the world, what each kind measures,
/// the motors, the break and the teardown.
/// </summary>
public class PhysicsConstraintsTests
{
    private static readonly EntityHandle A = new(0x0001_0010);
    private static readonly EntityHandle B = new(0x0001_0011);
    private const nint ObjA = 0x2000;
    private const nint ObjB = 0x3000;

    private static (PhysicsConstraints Table, FakeVPhysicsJoints V, FakeJointWorld W, RecordingLog Log, List<PhysicsEvent> Posted) Make()
    {
        var v = new FakeVPhysicsJoints();
        var w = new FakeJointWorld();
        w.Objects[A] = ObjA;
        w.Objects[B] = ObjB;
        v.Add(ObjA);
        v.Add(ObjB);
        v.Add(w.World);
        var log = new RecordingLog();
        var posted = new List<PhysicsEvent>();
        return (new PhysicsConstraints(new PhysicsJointTable(), v, w, log, posted.Add), v, w, log, posted);
    }

    private static float[] Fixed(float force = 0f, float torque = 0f) => [force, torque];

    private static float[] Hinge(Vector point, Vector axis, float friction) =>
        [0f, 0f, point.X, point.Y, point.Z, axis.X, axis.Y, axis.Z, friction];

    private static float[] WorldHinge(Vector axis, bool assumeWorldGeometry = true) =>
        [0f, 0f, 0f, 0f, 0f, axis.X, axis.Y, axis.Z, 0f, assumeWorldGeometry ? 1f : 0f];

    /// <summary>Turned a quarter about x: world z is the body's local y.</summary>
    private static Float3x4 QuarterAboutX()
    {
        var m = Float3x4.Identity;
        m[1, 1] = 0f; m[1, 2] = -1f;
        m[2, 1] = 1f; m[2, 2] = 0f;
        return m;
    }

    private static IEngineConstraints C(PhysicsConstraints t) => t;

    private static IEngineSprings S(PhysicsConstraints t) => t;

    [Fact]
    public void AnUnknownKindIsRefusedAndNamed()
    {
        var (t, v, _, log, _) = Make();
        Assert.Equal(0u, C(t).Create(A, B, 7, new float[20]));
        Assert.True(v.Calls.Count == 0 && log.Text.Contains("unknown kind 7"), "kind 7 is past Ragdoll: refused before anything is made");
    }

    [Fact]
    public void AShortParameterBlockIsRefusedRatherThanReadOffTheEnd()
    {
        var (t, v, _, log, _) = Make();
        Assert.Equal(0u, C(t).Create(A, B, (int)ConstraintKind.Hinge, new float[8]));
        Assert.True(v.Calls.Count == 0 && log.Text.Contains("needs 9 parameters, got 8"), "a hinge needs nine");
    }

    [Fact]
    public void WithNoEnvironmentNothingIsMade()
    {
        var (t, v, w, _, _) = Make();
        w.Env = 0;
        Assert.Equal(0u, C(t).Create(A, B, (int)ConstraintKind.Fixed, Fixed()));
        Assert.Empty(v.Calls);
    }

    [Fact]
    public void BothEndsOnOneObjectIsRefused()
    {
        var (t, v, _, log, _) = Make();
        Assert.Equal(0u, C(t).Create(A, A, (int)ConstraintKind.Fixed, Fixed()));
        Assert.True(v.Calls.Count == 0 && log.Text.Contains("both ends are the same object"));
    }

    [Fact]
    public void AnEntityWithNoPhysicsAgainstTheWorldIsTheWorldTwiceAndRefused()
    {
        var (t, v, w, log, _) = Make();
        var bare = new EntityHandle(0x0001_0020);
        w.Objects[bare] = 0; // live, no physics

        Assert.Equal(0u, C(t).Create(bare, EntityHandle.None, (int)ConstraintKind.Fixed, Fixed()));
        Assert.True(v.Calls.Count == 0 && log.Text.Contains("both ends are the same object"),
                    "the bare end becomes the world, which the other end already is");
    }

    [Fact]
    public void AnUnattachedSecondEndIsTheWorldInSecondPlaceForAFixedJoint()
    {
        var (t, v, w, _, _) = Make();
        uint id = C(t).Create(A, EntityHandle.None, (int)ConstraintKind.Fixed, Fixed());
        Assert.NotEqual(0u, id);
        Assert.Equal((ObjA, w.World), v.LastEnds);
    }

    [Fact]
    public void FixingToTheWorldMarksTheAttachedObjectConstraintStatic()
    {
        var (t, v, w, _, _) = Make();
        v.Of(ObjB).GameFlags = 0x0100;
        C(t).Create(EntityHandle.None, B, (int)ConstraintKind.Fixed, Fixed());
        Assert.Equal((w.World, ObjB), v.LastEnds);
        Assert.Equal((ushort)0x0102, v.Of(ObjB).GameFlags);
    }

    [Fact]
    public void AFixedJointHoldsTheAttachedObjectInTheReferencesSpace()
    {
        var (t, v, _, _, _) = Make();
        v.Of(ObjA).Pose = FakeVPhysicsJoints.At(new Vector(10f, 0f, 0f));
        v.Of(ObjB).Pose = FakeVPhysicsJoints.At(new Vector(10f, 5f, 2f));

        C(t).Create(A, B, (int)ConstraintKind.Fixed, Fixed(force: 300f, torque: 40f));

        var p = (ConstraintFixedParams)v.LastBlock!;
        Assert.True(p.AttachedRefXform[0, 3] == 0f && p.AttachedRefXform[1, 3] == 5f && p.AttachedRefXform[2, 3] == 2f,
                    "BuildObjectRelativeXform: B sits at (0,5,2) from A");
        Assert.True(p.Constraint.ForceLimit == 300f && p.Constraint.TorqueLimit == 40f && p.Constraint.Strength == 1f);
    }

    [Fact]
    public void AHingeWithNoAxisIsRefused()
    {
        var (t, v, _, log, _) = Make();
        Assert.Equal(0u, C(t).Create(A, B, (int)ConstraintKind.Hinge, Hinge(Vector.Zero, Vector.Zero, 0f)));
        Assert.True(v.Calls.Count == 0 && log.Text.Contains("no axis direction"));
    }

    [Fact]
    public void AHingeToTheWorldPutsTheWorldFirstAndItsEndsFollow()
    {
        var (t, v, w, _, posted) = Make();
        uint id = C(t).Create(A, EntityHandle.None, (int)ConstraintKind.Hinge,
                              Hinge(new Vector(1f, 2f, 3f), new Vector(0f, 0f, 1f), 0.5f));

        Assert.Equal((w.World, ObjA), v.LastEnds);

        // The ends swapped with the objects: the break names A second.
        t.Broken(v.Ends.Keys.Single());
        Assert.True(posted.Single().Entity0 == EntityHandle.None.Value && posted.Single().Entity1 == A.Value
                    && posted.Single().Object0 == id);
    }

    [Fact]
    public void HingeFrictionIsInStocksThousands()
    {
        var (t, v, _, _, _) = Make();
        C(t).Create(A, B, (int)ConstraintKind.Hinge, Hinge(new Vector(1f, 2f, 3f), new Vector(0f, 1f, 0f), 0.25f));
        var p = (ConstraintHingeParams)v.LastBlock!;
        Assert.True(p.HingeAxis.Torque == 250f && p.HingeAxis.MinRotation == 0f && p.HingeAxis.AngularVelocity == 0f);
        Assert.True(p.WorldPosition.Y == 2f && p.WorldAxisDirection.Y == 1f);
    }

    [Fact]
    public void ABallSocketIsLocalToEachObjectAndCannotBreakByTurning()
    {
        var (t, v, _, _, _) = Make();
        v.Of(ObjA).Pose = FakeVPhysicsJoints.At(new Vector(1f, 0f, 0f));
        v.Of(ObjB).Pose = FakeVPhysicsJoints.At(new Vector(0f, 4f, 0f));

        C(t).Create(A, B, (int)ConstraintKind.BallSocket, [100f, 50f, 5f, 5f, 5f]);

        var p = (ConstraintBallsocketParams)v.LastBlock!;
        Assert.True(p.ConstraintPosition0.X == 4f && p.ConstraintPosition1.Y == 1f, "the anchor in each object's space");
        Assert.True(p.Constraint.ForceLimit == 100f && p.Constraint.TorqueLimit == 0f, "the torque limit is dropped");
    }

    [Fact]
    public void ALengthIsMeasuredAsStrungPlusTheExtra()
    {
        var (t, v, _, _, _) = Make();
        C(t).Create(A, B, (int)ConstraintKind.Length, [0f, 0f, 0f, 0f, 0f, 3f, 4f, 0f, 2f, 10f]);
        var p = (ConstraintLengthParams)v.LastBlock!;
        Assert.True(p.TotalLength == 15f && p.MinLength == 2f, "|(0,0,0)-(3,4,0)| = 5, plus 10 extra; the minimum is given");
    }

    /// <summary>
    /// Stock has no guard here (CPhysSlideConstraint::CreateConstraint), and
    /// native's could never fire: it compared the engine's VectorNormalize
    /// with 0, and the SSE body answers sqrt(1e-10) for a zero vector.
    /// </summary>
    [Fact]
    public void ASlideWithBothEndsOnOnePointIsStillMadeAsStocksIs()
    {
        var (t, v, _, log, _) = Make();
        Assert.NotEqual(0u, C(t).Create(A, B, (int)ConstraintKind.Sliding, [0f, 0f, 1f, 1f, 1f, 1f, 1f, 1f, 0f, 0f]));
        Assert.True(v.Calls.Single() == "CreateSliding" && log.Lines.Count == 0);
    }

    [Fact]
    public void ASlidesLimitsAreProjectedWidenedToTheObjectAndMadeRelative()
    {
        var (t, v, _, _, _) = Make();
        v.Of(ObjB).Pose = FakeVPhysicsJoints.At(new Vector(0f, 0f, 120f));

        // Axis from z=0 to z=100, the object above the top end, limits on.
        C(t).Create(A, B, (int)ConstraintKind.Sliding, [0f, 0f, 0f, 0f, 0f, 0f, 0f, 100f, 0.5f, 1f]);

        var p = (ConstraintSlidingParams)v.LastBlock!;
        Assert.True(MathF.Abs(p.SlideAxisRef.Z - 1f) < 1e-6f, "the axis, snapped to +z, in A's (identity) frame");
        Assert.True(MathF.Abs(p.LimitMin - -120f) < 1e-3f && MathF.Abs(p.LimitMax) < 1e-3f,
                    "the range [0,100] widened to 120, then measured from 120");
        Assert.Equal(0.5f, p.Friction);
    }

    [Fact]
    public void ASlideWithoutLimitsLeavesThemZero()
    {
        var (t, v, _, _, _) = Make();
        C(t).Create(A, B, (int)ConstraintKind.Sliding, [0f, 0f, 0f, 0f, 0f, 0f, 0f, 100f, 0f, 0f]);
        var p = (ConstraintSlidingParams)v.LastBlock!;
        Assert.True(p.LimitMin == 0f && p.LimitMax == 0f);
    }

    [Fact]
    public void APulleysRopeIsMeasuredWithTheGearOnTheSecondSpan()
    {
        var (t, v, _, _, _) = Make();
        v.Of(ObjA).Pose = FakeVPhysicsJoints.At(new Vector(0f, 0f, 0f));
        v.Of(ObjB).Pose = FakeVPhysicsJoints.At(new Vector(100f, 0f, 0f));

        // Pulleys 10 above each object, offsets zero, 5 extra rope, gear 2, rigid.
        C(t).Create(A, B, (int)ConstraintKind.Pulley,
                    [0f, 0f, 0f, 0f, 10f, 100f, 0f, 10f, 0f, 0f, 0f, 0f, 0f, 0f, 5f, 2f, 1f]);

        var p = (ConstraintPulleyParams)v.LastBlock!;
        Assert.True(p.TotalLength == 5f + 10f + 10f * 2f && p.GearRatio == 2f && p.IsRigid == 1);
    }

    [Fact]
    public void APulleyGearOfZeroKeepsStocksOne()
    {
        var (t, v, _, _, _) = Make();
        C(t).Create(A, B, (int)ConstraintKind.Pulley, new float[17]);
        var p = (ConstraintPulleyParams)v.LastBlock!;
        Assert.True(p.GearRatio == 1f && p.IsRigid == 0);
    }

    [Fact]
    public void ARagdollJointCarriesItsFrameItsAxesAndItsFlags()
    {
        var (t, v, _, _, _) = Make();
        v.Of(ObjA).Pose = FakeVPhysicsJoints.At(new Vector(0f, 0f, 10f));

        C(t).Create(A, B, (int)ConstraintKind.Ragdoll,
                    [0f, 0f, 0f, 0f, 20f, 0f, 0f, 0f, -10f, 10f, -20f, 20f, -30f, 30f, 1f, 2f, 3f, 1f, 0f]);

        var p = (ConstraintRagdollParams)v.LastBlock!;
        Assert.True(p.ConstraintToReference[2, 3] == 10f, "the joint at z=20 is 10 above A");
        Assert.True(p.ConstraintToAttached[2, 3] == 20f, "and 20 above B at the origin");
        Assert.True(p.Axis0.MinRotation == -10f && p.Axis1.MaxRotation == 20f && p.Axis2.Torque == 3f);
        Assert.True(p.OnlyAngularLimits == 1 && p.IsActive == 0);
    }

    [Fact]
    public void DestroyDeactivatesThenDestroysAndTheIdGoesStale()
    {
        var (t, v, _, _, _) = Make();
        uint id = C(t).Create(A, B, (int)ConstraintKind.Fixed, Fixed());
        nint made = v.Ends.Keys.Single();

        C(t).Destroy(id);
        C(t).Destroy(id);

        Assert.Equal(new[] { "CreateFixed", $"Deactivate {made:x}", $"DestroyConstraint {made:x}" }, v.Calls);
        Assert.False(C(t).IsValid(id));
        Assert.Equal(0, C(t).Count());
    }

    [Fact]
    public void TwoStaticEndsAreRefusedAndNamed()
    {
        var (t, v, _, log, _) = Make();
        v.Of(ObjA).Static = true;
        v.Of(ObjB).Static = true;

        Assert.Equal(0u, C(t).Create(A, B, (int)ConstraintKind.Fixed, Fixed()));
        Assert.Contains("two static objects", log.Text);
        Assert.DoesNotContain("CreateFixed", v.Calls);

        v.Of(ObjB).Static = false;
        Assert.NotEqual(0u, C(t).Create(A, B, (int)ConstraintKind.Fixed, Fixed()));
    }

    [Fact]
    public void TwoShadowedEndsAreRefusedAndNamed()
    {
        var (t, v, _, log, _) = Make();
        v.Of(ObjA).Shadowed = true;
        v.Of(ObjB).Shadowed = true;

        Assert.Equal(0u, C(t).Create(A, B, (int)ConstraintKind.Fixed, Fixed()));
        Assert.Contains("two shadow objects", log.Text);
    }

    [Fact]
    public void DeactivateKeepsTheJointAndClearsTheStaticFlagOnBothEnds()
    {
        var (t, v, _, _, _) = Make();
        uint id = C(t).Create(EntityHandle.None, B, (int)ConstraintKind.Fixed, Fixed());
        nint made = v.Ends.Keys.Single();
        Assert.NotEqual(0, v.Of(ObjB).GameFlags & PhysicsConstraints.ConstraintStaticFlag);
        v.Of(ObjB).GameFlags |= 0x0100;

        C(t).Deactivate(id);

        Assert.Contains($"Deactivate {made:x}", v.Calls);
        Assert.Equal(0x0100, v.Of(ObjB).GameFlags);
        Assert.True(C(t).IsValid(id), "the joint stays, to be turned on again");
        Assert.DoesNotContain(v.Calls, c => c.StartsWith("DestroyConstraint"));
    }

    [Fact]
    public void ActivateHoldsAgainAndWakesBothEnds()
    {
        var (t, v, _, _, _) = Make();
        uint id = C(t).Create(A, B, (int)ConstraintKind.Fixed, Fixed());
        nint made = v.Ends.Keys.Single();

        C(t).Activate(id);

        Assert.Contains($"Activate {made:x}", v.Calls);
        Assert.Equal((1, 1), (v.Of(ObjA).Wakes, v.Of(ObjB).Wakes));
    }

    [Fact]
    public void DestroyClearsTheStaticFlagItsJointSet()
    {
        var (t, v, _, _, _) = Make();
        uint id = C(t).Create(EntityHandle.None, B, (int)ConstraintKind.Fixed, Fixed());

        C(t).Destroy(id);

        Assert.Equal(0, v.Of(ObjB).GameFlags & PhysicsConstraints.ConstraintStaticFlag);
    }

    [Fact]
    public void AMotorIsNotAppliedToAnythingButAHinge()
    {
        var (t, v, _, _, _) = Make();
        uint id = C(t).Create(A, B, (int)ConstraintKind.Fixed, Fixed());
        C(t).SetHingeVelocity(id, 90f, 1f);
        Assert.Empty(v.AngularMotors);
    }

    [Fact]
    public void ALinearMotorsForceIsTheAverageMovingMassPerTick()
    {
        var (t, v, w, _, _) = Make();
        v.Of(ObjA).Mass = 10f;
        v.Of(ObjB).Mass = 30f;
        uint id = C(t).Create(A, B, (int)ConstraintKind.Sliding, [0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f]);

        C(t).SetLinearMotor(id, 2f, 3f);

        // avg mass 20, speed 2, load 3, 1/0.015 per tick.
        var (speed, max) = v.LinearMotors.Values.Single();
        Assert.True(speed == 2f && max == 2f * 3f * 20f * (1f / w.Tick));
        Assert.True(v.Of(ObjA).Wakes == 1 && v.Of(ObjB).Wakes == 1);
    }

    [Fact]
    public void ALinearMotorAgainstTheWorldKeepsStocksKilogramWhenOnlyTheAttachedEndMoves()
    {
        var (t, v, w, _, _) = Make();
        v.Of(w.World).Moveable = false;
        v.Of(ObjB).Mass = 30f;
        uint id = C(t).Create(EntityHandle.None, B, (int)ConstraintKind.Sliding, [0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f, 0f]);

        C(t).SetLinearMotor(id, 1f, 0f);

        // massLoad starts at 1 and the attached mass is ADDED: 31, over one mass.
        Assert.Equal(1f * 1f * 31f * (1f / w.Tick), v.LinearMotors.Values.Single().Max);
    }

    [Fact]
    public void AHingeVelocitysTorqueIsTheAverageMovingInertiaTimesTheLoadScaleSquaredPerTick()
    {
        var (t, v, w, _, _) = Make();
        v.Of(ObjA).Inertia = new Vector(0f, 3f, 4f);   // 5
        v.Of(ObjB).Inertia = new Vector(0f, 0f, 15f);  // 15
        uint id = C(t).Create(A, B, (int)ConstraintKind.Hinge, Hinge(Vector.Zero, new Vector(0f, 0f, 1f), 0f));

        C(t).SetHingeVelocity(id, 30f, 2f);

        // avg inertia 10, speed 30, load 2 twice, 1/0.015 per tick.
        Assert.Equal((30f, 30f * 2f * 10f * 2f * (1f / w.Tick)), v.AngularMotors.Values.Single());
        Assert.True(v.Of(ObjA).Wakes == 1 && v.Of(ObjB).Wakes == 1);
    }

    [Fact]
    public void AHingeVelocityAgainstTheWorldKeepsStocksOneAndALoadScaleOfZeroIsOne()
    {
        var (t, v, w, _, _) = Make();
        v.Of(w.World).Moveable = false;
        v.Of(ObjB).Inertia = new Vector(0f, 0f, 15f);
        uint id = C(t).Create(EntityHandle.None, B, (int)ConstraintKind.Hinge, Hinge(Vector.Zero, new Vector(0f, 0f, 1f), 0f));

        C(t).SetHingeVelocity(id, 10f, 0f);

        // massLoad starts at 1 and the attached inertia is ADDED: 16, over one.
        Assert.Equal(10f * 16f * (1f / w.Tick), v.AngularMotors.Values.Single().Max);
        Assert.Equal(0, v.Of(w.World).Wakes);
    }

    [Fact]
    public void ABreakIsPostedWithTheIdAndBothEnds()
    {
        var (t, v, _, _, posted) = Make();
        uint id = C(t).Create(A, B, (int)ConstraintKind.Fixed, Fixed());

        t.Broken(v.Ends.Keys.Single());

        var ev = posted.Single();
        Assert.True(ev.Kind == PhysicsEventKind.ConstraintBroken && ev.Object0 == id
                    && ev.Entity0 == A.Value && ev.Entity1 == B.Value && ev.Flags == 2u);
    }

    [Fact]
    public void ABreakOfAConstraintThisTableDidNotMakeTellsNobody()
    {
        var (t, _, _, _, posted) = Make();
        C(t).Create(A, B, (int)ConstraintKind.Fixed, Fixed());
        t.Broken(0x9999);
        Assert.Empty(posted);
    }

    [Fact]
    public void LevelShutdownDropsEveryJointWithoutTouchingVphysics()
    {
        var (t, v, _, _, _) = Make();
        C(t).Create(A, B, (int)ConstraintKind.Fixed, Fixed());
        S(t).Create(A, B, Vector.Zero, new Vector(0f, 0f, 1f), 1f, 1f, 0f, 1f, false);
        int calls = v.Calls.Count;

        t.LevelShutdown();

        Assert.True(C(t).Count() == 0 && S(t).Count() == 0 && v.Calls.Count == calls);
    }

    [Fact]
    public void ASpringsEndpointsAreMatchedToTheNearerObject()
    {
        var (t, v, _, _, _) = Make();
        v.Of(ObjB).Pose = FakeVPhysicsJoints.At(new Vector(0f, 0f, 100f));

        // start is near B, end near A: handed the wrong way round, so swapped.
        S(t).Create(A, B, new Vector(0f, 0f, 99f), new Vector(0f, 0f, 1f), 50f, 2f, 0.5f, 64f, true);

        var p = (SpringParams)v.LastBlock!;
        Assert.True(p.StartPosition.Z == 1f && p.EndPosition.Z == 99f, "swapped so the end is nearer B");
        Assert.True(p.Constant == 50f && p.Damping == 2f && p.RelativeDamping == 0.5f && p.NaturalLength == 64f
                    && p.OnlyStretch == 1 && p.UseLocalPositions == 0);
    }

    [Fact]
    public void ASpringIdIsNotAConstraintId()
    {
        var (t, v, _, _, _) = Make();
        uint spring = S(t).Create(A, B, Vector.Zero, new Vector(0f, 0f, 1f), 1f, 1f, 0f, 1f, false);

        Assert.True(S(t).IsValid(spring) && !C(t).IsValid(spring));
        C(t).Destroy(spring);
        Assert.DoesNotContain(v.Calls, c => c.StartsWith("Deactivate"));
    }

    [Fact]
    public void ASpringsThreeNumbersAreSetOnItAndItIsDestroyedOnce()
    {
        var (t, v, _, _, _) = Make();
        uint id = S(t).Create(A, B, Vector.Zero, new Vector(0f, 0f, 1f), 1f, 1f, 0f, 1f, false);
        nint made = v.Ends.Keys.Single();

        S(t).SetConstant(id, 7f);
        S(t).SetDamping(id, 8f);
        S(t).SetLength(id, 9f);
        S(t).Destroy(id);
        S(t).Destroy(id);

        Assert.True(v.SpringConstants[made] == 7f && v.SpringDampings[made] == 8f && v.SpringLengths[made] == 9f);
        Assert.Single(v.Calls, c => c == $"DestroySpring {made:x}");
        Assert.False(S(t).IsValid(id));
    }

    [Fact]
    public void AReloadCarriesEachJointUnderTheIdItHad()
    {
        var (t, v, w, log, _) = Make();
        uint joint = C(t).Create(A, B, (int)ConstraintKind.Hinge, Hinge(Vector.Zero, new Vector(0f, 0f, 1f), 0f));
        uint spring = S(t).Create(A, B, Vector.Zero, new Vector(0f, 0f, 1f), 1f, 1f, 0f, 1f, false);

        var record = new ReloadRecord("joints");
        ((ISaveCustom)t).OnSave(new SaveRecordAccess(record, "joints", log));

        var next = new PhysicsConstraints(new PhysicsJointTable(), v, w, log, _ => { });
        ((ISaveCustom)next).OnRestore(new SaveRecordAccess(record, "joints", log));

        Assert.True(((IEngineConstraints)next).IsValid(joint) && ((IEngineSprings)next).IsValid(spring));
        ((IEngineConstraints)next).SetHingeVelocity(joint, 45f, 1f);
        Assert.Equal(45f, v.AngularMotors.Values.Single().Speed);
    }

    //-------------------------------------------------------------------------
    // A world hinge (CPhysHinge::CreateConstraint, IsWorldHinge, Deactivate).
    //-------------------------------------------------------------------------

    [Fact]
    public void AWorldHingeHingesItsObjectAboutTheLocalAxisItLiesOnBeforeTheJointIsMade()
    {
        var (t, v, _, _, _) = Make();
        v.Of(ObjA).Pose = QuarterAboutX();

        C(t).Create(A, EntityHandle.None, (int)ConstraintKind.Hinge, WorldHinge(new Vector(0f, 0f, 1f)));

        Assert.Equal(1, v.Of(ObjA).HingedAxis);
        Assert.Equal(new[] { "BecomeHinged 1", "CreateHinge" }, v.Calls);
    }

    [Fact]
    public void AHingeToTheWorldWithoutTheFlagLeavesItsObjectFree()
    {
        var (t, v, _, _, _) = Make();

        C(t).Create(A, EntityHandle.None, (int)ConstraintKind.Hinge, WorldHinge(new Vector(0f, 0f, 1f), assumeWorldGeometry: false));

        Assert.Equal(-1, v.Of(ObjA).HingedAxis);
    }

    [Fact]
    public void AHingeBetweenTwoObjectsIsNeverAWorldHinge()
    {
        var (t, v, _, _, _) = Make();

        C(t).Create(A, B, (int)ConstraintKind.Hinge, WorldHinge(new Vector(0f, 0f, 1f)));

        Assert.True(v.Of(ObjA).HingedAxis == -1 && v.Of(ObjB).HingedAxis == -1);
    }

    [Fact]
    public void AWorldHingeAcrossTheObjectsAxesLeavesItFree()
    {
        var (t, v, _, _, _) = Make();

        // Forty-five degrees off: no local axis is the hinge's.
        C(t).Create(A, EntityHandle.None, (int)ConstraintKind.Hinge, WorldHinge(new Vector(0f, 0.70710677f, 0.70710677f)));

        Assert.Equal(-1, v.Of(ObjA).HingedAxis);
    }

    [Fact]
    public void TakingAWorldHingeDownFreesItsObjectFirst()
    {
        var (t, v, _, _, _) = Make();
        uint id = C(t).Create(A, EntityHandle.None, (int)ConstraintKind.Hinge, WorldHinge(new Vector(0f, 0f, 1f)));
        nint made = v.Ends.Keys.Single();
        v.Calls.Clear();

        C(t).Destroy(id);

        Assert.Equal(new[] { "RemoveHinged", $"Deactivate {made:x}", $"DestroyConstraint {made:x}" }, v.Calls);
        Assert.Equal(-1, v.Of(ObjA).HingedAxis);
    }

    [Fact]
    public void AReloadRemembersAWorldHinge()
    {
        var (t, v, w, log, _) = Make();
        uint id = C(t).Create(A, EntityHandle.None, (int)ConstraintKind.Hinge, WorldHinge(new Vector(0f, 0f, 1f)));

        var record = new ReloadRecord("joints");
        ((ISaveCustom)t).OnSave(new SaveRecordAccess(record, "joints", log));
        var next = new PhysicsConstraints(new PhysicsJointTable(), v, w, log, _ => { });
        ((ISaveCustom)next).OnRestore(new SaveRecordAccess(record, "joints", log));
        v.Calls.Clear();

        ((IEngineConstraints)next).Destroy(id);

        Assert.Equal("RemoveHinged", v.Calls.First());
    }

    [Theory]
    [InlineData(0f, -1f, 0f, 1)]
    [InlineData(0f, 0f, 1f, 2)]
    [InlineData(1f, 1f, 0f, -1)]
    [InlineData(0.5f, 0f, 0f, -1)]
    [InlineData(1f, 0.5f, 0f, -1)]
    [InlineData(0f, 0f, 0f, -1)]
    public void UnitAxisIndexNamesTheOneUnitComponent(float x, float y, float z, int expected)
        => Assert.Equal(expected, PhysicsConstraints.UnitAxisIndex(new Vector(x, y, z)));
}
