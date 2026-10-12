using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ragdolls;
using SourceSharp.Shared.Bones;
using Xunit;
using static SourceSharp.Tests.Ragdolls.RagdollFixtures;
using NVector = System.Numerics.Vector3;

namespace SourceSharp.Tests.Ragdolls;

/// <summary>
/// ragdoll_shared.cpp's RagdollActivate / RagdollSetupCollisions /
/// RagdollDestroy / RagdollGetBoneMatrix / RagdollComputeExactBbox /
/// RagdollIsAsleep / RagdollApplyAnimationAsVelocity / RagdollSolveSeparation,
/// and physics_shared.cpp's PhysHasContactWithOtherInDirection and
/// PhysForceClearVelocity, as RagdollSolver carries them.
/// </summary>
public class RagdollMotionTests
{
    private static RagdollCollide Collide(FakeRagdollVPhysics fake) => fake.Collide(Model);

    // ---- activate ------------------------------------------------------------

    [Fact]
    public void ActivatingMarksEveryElementOneOfAMultiObjectEntity()
    {
        var (body, solver, fake) = Built();

        solver.Activate(body, Collide(fake), Model);

        Assert.All(body.List[..3], e => Assert.Equal(RagdollSolver.MultiObjectEntity,
                                                    fake.Object(e.Object).GameFlags & RagdollSolver.MultiObjectEntity));
    }

    [Fact]
    public void ActivatingTurnsCollisionsOnAndWakesEachElement()
    {
        var (body, solver, fake) = Built();

        solver.Activate(body, Collide(fake), Model);

        Assert.All(body.List[..3], e => Assert.True(fake.Object(e.Object).CollisionsEnabled && fake.Object(e.Object).Wakes == 1));
        Assert.Equal(1, fake.Groups[body.Group].Activations);
    }

    [Fact]
    public void ActivatingWithoutAWakePutsTheGroupBackToSleep()
    {
        var (body, solver, fake) = Built();

        solver.Activate(body, Collide(fake), Model, forceWake: false);

        Assert.All(body.List[..3], e => Assert.True(fake.Object(e.Object).Asleep && fake.Object(e.Object).Wakes == 0));
    }

    [Fact]
    public void WithNoRulesEveryPairCollidesButAChildAndItsParent()
    {
        var (body, solver, fake) = Built();

        solver.Activate(body, Collide(fake), Model);

        Assert.Equal([(0, 1), (0, 2), (1, 2)], fake.Sets[Model].Enabled);
        Assert.Equal([(1, 0), (2, 1)], fake.Sets[Model].Disabled);
    }

    [Fact]
    public void AModelsExistingSetIsLeftAsItIs()
    {
        var (body, solver, fake) = Built();
        fake.FindOrCreateCollisionSet(Model, 3);

        solver.Activate(body, Collide(fake), Model);

        Assert.Empty(fake.Sets[Model].Enabled);
        Assert.Empty(fake.Sets[Model].Disabled);
    }

    [Fact]
    public void ARulesBlockTakesTheDefaultsPlace()
    {
        var (body, solver, fake) = Built();
        fake.Blocks = [.. ThreeSolids(), new FakeRagdollVPhysics.CustomBlock("collisionrules", [("collisionpair", "0,1")])];

        solver.Activate(body, Collide(fake), Model);

        Assert.Equal([(0, 1)], fake.Sets[Model].Enabled);
        Assert.Empty(fake.Sets[Model].Disabled);
    }

    // ---- destroy -------------------------------------------------------------

    [Fact]
    public void DestroyingReleasesEveryJointObjectAndTheGroup()
    {
        var (body, solver, fake) = Built();
        nint group = body.Group;

        solver.Destroy(body, fake.Env);

        Assert.All(fake.Constraints.Values, c => Assert.True(c.Destroyed));
        Assert.All(fake.Objects.Values, o => Assert.True(o.Destroyed));
        Assert.True(fake.Groups[group].Destroyed);
        Assert.Equal((0, (nint)0), (body.ListCount, body.Group));
    }

    [Fact]
    public void BeforeDestroyEveryElementStopsColliding()
    {
        var (body, solver, fake) = Built();
        solver.Activate(body, Collide(fake), Model);

        solver.PrepareForDestroy(body);

        Assert.All(body.List[..3], e => Assert.False(fake.Object(e.Object).CollisionsEnabled));
    }

    // ---- bones ---------------------------------------------------------------

    [Fact]
    public void AnElementsBoneIsItsObjectsMatrix()
    {
        var (body, solver, fake) = Built();
        fake.SetPositionMatrix(body.List[0].Object, At(1f, 2f, 3f), false);
        var bones = new BoneMatrix3x4[4];

        Assert.True(solver.GetBoneMatrix(body, bones, 0));
        Assert.Equal(new NVector(1f, 2f, 3f), bones[0].Position);
    }

    [Fact]
    public void AChildIsHeldRigidlyAtItsParentsAttachment()
    {
        var (body, solver, fake) = Built();
        fake.SetPositionMatrix(body.List[1].Object, At(9f, 9f, 9f), false);
        var bones = new BoneMatrix3x4[4];

        solver.GetBoneMatrix(body, bones, 0);
        solver.GetBoneMatrix(body, bones, 1);

        // parent at z 40, originParentSpace = bone-to-bone translation (z 10).
        Assert.Equal(new NVector(0f, 0f, 40f) + new NVector(body.List[1].OriginParentSpace.X,
                         body.List[1].OriginParentSpace.Y, body.List[1].OriginParentSpace.Z), bones[1].Position);
    }

    [Fact]
    public void AStretchyBodyKeepsTheChildsOwnPlace()
    {
        var (body, solver, fake) = Built();
        body.AllowStretch = true;
        fake.SetPositionMatrix(body.List[1].Object, At(9f, 9f, 9f), false);
        var bones = new BoneMatrix3x4[4];

        solver.GetBoneMatrix(body, bones, 0);
        solver.GetBoneMatrix(body, bones, 1);

        Assert.Equal(new NVector(9f, 9f, 9f), bones[1].Position);
    }

    [Fact]
    public void AnElementWithNoBoneWritesNothing()
    {
        var (body, solver, _) = Built();
        body.BoneIndex[2] = -1;

        Assert.False(solver.GetBoneMatrix(body, new BoneMatrix3x4[4], 2));
    }

    // ---- bounds and sleep -------------------------------------------------------

    [Fact]
    public void TheExactBoxSpansTheOriginAndEveryElementsBox()
    {
        var (body, solver, fake) = Built();
        foreach (RagdollElement e in body.List[..3])
            fake.Object(e.Object).Aabb = (new Vector(-1f, -1f, -1f), new Vector(1f, 1f, 1f));

        var (mins, maxs) = solver.ComputeExactBbox(body, new Vector(0f, 0f, 45f));

        Assert.Equal((new Vector(-1f, -1f, 39f), new Vector(1f, 1f, 61f)), (mins, maxs));
    }

    [Fact]
    public void ABodyIsAsleepOnlyWhenEveryElementIs()
    {
        var (body, solver, fake) = Built();

        Assert.True(solver.IsAsleep(body));
        fake.Object(body.List[2].Object).Asleep = false;
        Assert.False(solver.IsAsleep(body));
    }

    // ---- animation as velocity ---------------------------------------------------

    [Fact]
    public void TwoPosesGiveEachElementItsBonesVelocity()
    {
        var (body, solver, fake) = Built();
        BoneMatrix3x4[] prev = Pose();
        BoneMatrix3x4[] next = Pose();
        next[1] = Translation(4f, 0f, 50f);

        solver.ApplyAnimationAsVelocity(body, prev, next, 0.5f);

        Assert.Equal(new Vector(8f, 0f, 0f), fake.Object(body.List[1].Object).AddedVelocity);
        Assert.Equal(Vector.Zero, fake.Object(body.List[0].Object).AddedVelocity);
    }

    [Fact]
    public void TheAngularVelocityIsHandedOverInTheElementsSpace()
    {
        var (body, solver, fake) = Built();
        BoneMatrix3x4[] prev = Pose();
        BoneMatrix3x4[] next = Pose();
        next[0] = BoneSetupMath.AngleMatrix(new BoneAngle(0f, 90f, 0f), new NVector(0f, 0f, 40f));

        solver.ApplyAnimationAsVelocity(body, prev, next, 1f);
        BoneSetupAdj.CalcBoneDerivatives(out _, out NVector angVel, prev[0], next[0], 1f);

        // The fake's WorldToLocalVector doubles: the local value is what was added.
        Assert.Equal(new Vector(angVel.X * 2f, angVel.Y * 2f, angVel.Z * 2f), fake.Object(body.List[0].Object).AddedAngular);
    }

    [Fact]
    public void OneMatrixPerElementIsReadAsAVelocityInBoneSpace()
    {
        var (body, solver, fake) = Built();
        BoneMatrix3x4[] perElement = [Translation(-3f, 0f, 0f), Translation(0f, 0f, 0f), Translation(0f, 0f, 0f)];

        solver.ApplyAnimationAsVelocity(body, perElement);

        // The inverse of a translation by -3 is +3, handed over through LocalToWorldVector.
        Assert.Equal(new Vector(3f, 0f, 0f), fake.Object(body.List[0].Object).AddedVelocity);
    }

    // ---- separation --------------------------------------------------------------

    private static (RagdollBody Body, RagdollSolver Solver, FakeRagdollVPhysics Fake) Separated(float childMass, float parentMass)
    {
        var built = Built();
        var (body, _, fake) = built;
        fake.Object(body.List[0].Object).Mass = parentMass;
        fake.Object(body.List[1].Object).Mass = childMass;
        // The spine flies 30 units off its attachment on the pelvis.
        fake.SetPositionMatrix(body.List[1].Object, At(30f, 0f, 50f), false);
        fake.SetPositionMatrix(body.List[2].Object, At(30f, 0f, 60f), false);
        return built;
    }

    [Fact]
    public void ALightChildOffAHeavyParentIsPutBackAtItsAttachment()
    {
        var (body, solver, fake) = Separated(childMass: 1f, parentMass: 10f);
        fake.Object(body.List[0].Object).Velocity = new Vector(0f, 7f, 0f);

        int fixedCount = solver.SolveSeparation(body, (_, _) => false, 0);
        var spine = fake.Object(body.List[1].Object);

        Assert.True(fixedCount >= 1);
        Assert.Equal(new Vector(0f, 0f, 40f) + body.List[1].OriginParentSpace, spine.Position);
        Assert.Equal((new Vector(0f, 7f, 0f), true), (spine.Velocity, spine.LastTeleport));
    }

    [Fact]
    public void AChildOfAFixedParentIsFixedToo()
    {
        var (body, solver, _) = Separated(childMass: 1f, parentMass: 10f);

        Assert.Equal(2, solver.SolveSeparation(body, (_, _) => false, 0));
    }

    [Fact]
    public void ASeparatedChildPressedIntoAWallIsFixedWhenTheLineIsBlocked()
    {
        var (body, solver, fake) = Separated(childMass: 5f, parentMass: 5f);
        fake.Object(body.List[1].Object).Contacts.Add((new Vector(-1f, 0f, 0f), 0x9999));
        Vector? traced = null;

        int fixedCount = solver.SolveSeparation(body, (start, end) => { traced ??= start; return true; }, 0);

        // The spine, and the head because its parent was fixed.
        Assert.Equal(2, fixedCount);
        Assert.Equal(new Vector(0f, 0f, 40f) + body.List[1].OriginParentSpace, traced);
    }

    [Fact]
    public void ASeparatedChildWithAClearLineIsLeftToTheSolver()
    {
        var (body, solver, fake) = Separated(childMass: 5f, parentMass: 5f);
        fake.Object(body.List[1].Object).Contacts.Add((new Vector(-1f, 0f, 0f), 0x9999));

        Assert.Equal(0, solver.SolveSeparation(body, (_, _) => false, 0));
    }

    [Fact]
    public void WithNothingToFixTheGroupsErrorIsCleared()
    {
        var (body, solver, fake) = Built();
        fake.Groups[body.Group].ErrorState = true;

        Assert.Equal(0, solver.SolveSeparation(body, (_, _) => true, 0));
        Assert.Equal(1, fake.Groups[body.Group].ErrorCleared);
    }

    [Fact]
    public void AContactWithItsOwnBodyIsNotAContactWithAnother()
    {
        var (body, solver, fake) = Built();
        fake.Object(body.List[1].Object).Contacts.Add((new Vector(1f, 0f, 0f), body.List[0].Object));

        Assert.False(solver.HasContactWithOtherInDirection(body.List[1].Object, new Vector(1f, 0f, 0f)));
        Assert.Equal(0, fake.SnapshotsOpen);
    }

    [Fact]
    public void AContactFacingTheDirectionIsAContact()
    {
        var (body, solver, fake) = Built();
        fake.Object(body.List[1].Object).Contacts.Add((new Vector(0f, 1f, 0f), 0x9999));
        fake.Object(body.List[1].Object).Contacts.Add((new Vector(1f, 0f, 0f), 0x9999));

        Assert.True(solver.HasContactWithOtherInDirection(body.List[1].Object, new Vector(1f, 0f, 0f)));
        Assert.False(solver.HasContactWithOtherInDirection(body.List[1].Object, new Vector(-1f, 0f, 0f)));
    }

    [Fact]
    public void ClearingVelocityClearsTheContactsStrainToo()
    {
        var (body, solver, fake) = Built();
        var spine = fake.Object(body.List[1].Object);
        spine.Velocity = new Vector(5f, 5f, 5f);
        spine.Contacts.Add((new Vector(1f, 0f, 0f), 0x9999));
        spine.Contacts.Add((new Vector(0f, 1f, 0f), 0x9999));

        solver.ForceClearVelocity(spine.Id);

        Assert.Equal((Vector.Zero, 2, 2, 0), (spine.Velocity, spine.FrictionCleared, spine.FrictionRecomputed, fake.SnapshotsOpen));
    }
}
