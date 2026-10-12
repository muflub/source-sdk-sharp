using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ragdolls;
using SourceSharp.Shared.Bones;
using Xunit;
using static SourceSharp.Tests.Ragdolls.RagdollFixtures;

namespace SourceSharp.Tests.Ragdolls;

/// <summary>
/// ragdoll_shared.cpp's RagdollCreate / RagdollCreateObjects / RagdollAddSolid /
/// RagdollAddConstraint and the two key handlers, as RagdollSolver carries
/// them (plan_physics_bones_native_zero Phase 3), against stock's own lines.
/// </summary>
public class RagdollCreateTests
{
    [Fact]
    public void EachSolidBecomesOneElementPlacedAtItsBone()
    {
        var (body, _, fake) = Built();

        Assert.Equal(3, body.ListCount);
        Assert.Equal([0, 1, 2], body.BoneIndex[..3]);
        Assert.Equal(new Vector(0f, 0f, 50f), fake.Object(body.List[1].Object).Position);
        Assert.True(fake.Object(body.List[1].Object).LastTeleport, "SetPositionMatrix( bones[bone], true )");
    }

    [Fact]
    public void ElementsAreNumberedInListOrder() => Assert.Equal([0, 1, 2], Indices(Built()));

    private static int[] Indices((RagdollBody Body, RagdollSolver Solver, FakeRagdollVPhysics Fake) built)
        => built.Body.List[..built.Body.ListCount].Select(e => (int)built.Fake.Object(e.Object).GameIndex).ToArray();

    [Fact]
    public void TheRootHasNoParentAndAChildNamesItsParent()
    {
        var (body, _, _) = Built();

        Assert.Equal([-1, 0, 1], body.List[..3].Select(e => e.ParentIndex).ToArray());
    }

    [Fact]
    public void ASolidWhoseBoneTheModelLacksIsSkippedAndSaid()
    {
        var (solver, fake, log) = Solver();
        fake.Blocks = [new FakeRagdollVPhysics.SolidBlock(0, "pelvis", "flesh"), new FakeRagdollVPhysics.SolidBlock(1, "tail", "flesh")];
        var body = new RagdollBody();

        Assert.True(solver.Create(body, Params(Body(), fake), fake.Env));
        Assert.Equal(1, body.ListCount);
        Assert.Contains(log.Lines, l => l == "CRagdollProp::CreateObjects:  Couldn't Lookup Bone tail\n");
    }

    [Fact]
    public void MoreSolidsThanTheElementLimitBuildNothing()
    {
        var (solver, fake, _) = Solver();
        fake.Blocks = ThreeSolids();
        fake.SolidCount = RagdollBody.MaxElements + 1;
        var body = new RagdollBody();

        Assert.False(solver.Create(body, Params(Body(), fake), fake.Env));
        Assert.Empty(fake.Objects);
        Assert.Empty(fake.Groups);
    }

    [Fact]
    public void ABodyWithNoElementIsRefused()
    {
        var (solver, fake, _) = Solver();
        fake.Blocks = [new FakeRagdollVPhysics.SolidBlock(0, "tail", "flesh")];

        Assert.False(solver.Create(new RagdollBody(), Params(Body(), fake), fake.Env));
    }

    [Fact]
    public void EverySolidIsReleasedAndTheParserDestroyed()
    {
        var (_, _, fake) = Built();

        Assert.Equal(3, fake.SolidsReleased);
        Assert.Equal(fake.ParsersCreated, fake.ParsersDestroyed);
    }

    [Fact]
    public void TheSolidParamsCarryTheOwnerTheNameAndTheInertiaLimit()
    {
        var (body, _, fake) = Built();
        RagdollObjectOverrides o = fake.Object(body.List[0].Object).Overrides;

        Assert.Equal(new RagdollObjectOverrides(null, 0.1f, unchecked((nint)0x8000_0000_0000_1235UL), 0x3300), o);
    }

    [Fact]
    public void AStatueWeighsAThousand()
    {
        var (solver, fake, _) = Solver();
        fake.Blocks = ThreeSolids();
        var body = new RagdollBody();
        RagdollParams p = Params(Body(), fake);
        p.FixedConstraints = true;

        solver.Create(body, p, fake.Env);

        Assert.All(body.List[..3], e => Assert.Equal(1000f, fake.Object(e.Object).Overrides.Mass));
    }

    [Fact]
    public void AnUnknownSurfacePropFallsBackToDefault()
    {
        var (solver, fake, _) = Solver();
        fake.Blocks = [new FakeRagdollVPhysics.SolidBlock(0, "pelvis", "nosuch")];
        fake.Surfaces["default"] = 9;
        var body = new RagdollBody();

        solver.Create(body, Params(Body(), fake), fake.Env);

        Assert.Equal(9, fake.Object(body.List[0].Object).SurfaceIndex);
    }

    [Fact]
    public void EachSolidIsMadeFromItsOwnCollide()
    {
        var (body, _, fake) = Built();

        Assert.Equal((nint)0x5002, fake.Object(body.List[2].Object).Collide);
    }

    // ---- constraints -----------------------------------------------------------

    [Fact]
    public void AJointIsMadeBetweenChildAndParentInTheBodysGroup()
    {
        var (body, _, fake) = Built();
        var joint = fake.Constraints[body.List[1].Constraint];

        Assert.Equal((body.List[1].Object, body.List[0].Object, body.Group),
                     (joint.Reference, joint.Attached, joint.Group));
    }

    [Fact]
    public void TheJointsAttachedFrameIsTheBoneToBoneTransform()
    {
        var (body, _, fake) = Built();
        StudioHdr hdr = Body();
        BoneMatrix3x4 expected = BoneSetupAdj.CalcBoneToBoneTransform(hdr, 2, 1);
        var joint = fake.Constraints[body.List[2].Constraint];

        Assert.Equal(expected.Position.Z, joint.Ragdoll.ConstraintToAttached[2, 3]);
        Assert.Equal(new Vector(expected.Position.X, expected.Position.Y, expected.Position.Z), body.List[2].OriginParentSpace);
    }

    [Fact]
    public void TheJointsReferenceFrameIsIdentity()
    {
        var (body, _, fake) = Built();
        Float3x4 reference = fake.Constraints[body.List[1].Constraint].Ragdoll.ConstraintToReference;

        Assert.Equal((1f, 0f, 1f, 1f), (reference[0, 0], reference[0, 3], reference[1, 1], reference[2, 2]));
    }

    [Fact]
    public void TheJointFrictionScaleScalesEveryAxisTorque()
    {
        var (solver, fake, _) = Solver();
        fake.Blocks = ThreeSolids();
        var body = new RagdollBody();
        RagdollParams p = Params(Body(), fake);
        p.JointFrictionScale = 0.5f;

        solver.Create(body, p, fake.Env);
        var joint = fake.Constraints[body.List[1].Constraint].Ragdoll;

        Assert.Equal((5f, 5f, 5f), (joint.Axis0.Torque, joint.Axis1.Torque, joint.Axis2.Torque));
    }

    [Fact]
    public void ABogusJointOnItselfMakesNoConstraint()
    {
        var (solver, fake, _) = Solver();
        fake.Blocks = [new FakeRagdollVPhysics.SolidBlock(0, "pelvis", "flesh"), new FakeRagdollVPhysics.ConstraintBlock(Joint(0, 0))];
        var body = new RagdollBody();

        solver.Create(body, Params(Body(), fake), fake.Env);

        Assert.Empty(fake.Constraints);
        Assert.Equal(-1, body.List[0].ParentIndex);
    }

    [Fact]
    public void AStatuesJointsAreFixedToTheirCurrentRelativePlace()
    {
        var (solver, fake, _) = Solver();
        fake.Blocks = ThreeSolids();
        var body = new RagdollBody();
        RagdollParams p = Params(Body(), fake);
        p.FixedConstraints = true;

        solver.Create(body, p, fake.Env);
        var joint = fake.Constraints[body.List[1].Constraint];

        // BuildObjectRelativeXform( child, parent ): the parent (z 40) in the child's (z 50) space.
        Assert.True(joint.Fixed);
        Assert.Equal(-10f, joint.FixedParams.AttachedRefXform[2, 3]);
    }

    // ---- the force -------------------------------------------------------------

    [Fact]
    public void TheForceBoneIsPushedAtItsCentre()
    {
        var (solver, fake, _) = Solver();
        fake.Blocks = ThreeSolids();
        var body = new RagdollBody();
        RagdollParams p = Params(Body(), fake);
        p.ForceBoneIndex = 1;
        p.ForceVector = new Vector(100f, 0f, 0f);

        solver.Create(body, p, fake.Env);

        Assert.Equal(new Vector(100f, 0f, 0f), fake.Object(body.List[1].Object).ForceCenter);
    }

    [Fact]
    public void TheOtherElementsArePushedByTheirShareOfTheMassAtTheForceBone()
    {
        var (solver, fake, _) = Solver();
        fake.Blocks = ThreeSolids();
        fake.MassOf = s => s.Name == "pelvis" ? 6f : 2f;
        var body = new RagdollBody();
        RagdollParams p = Params(Body(), fake);
        p.ForceBoneIndex = 1;
        p.ForceVector = new Vector(100f, 0f, 0f);

        solver.Create(body, p, fake.Env);
        var pelvis = fake.Object(body.List[0].Object).ForceOffsets.Single();

        // scale = mass / totalMass, in float, then times the force, as stock.
        Assert.Equal((new Vector(6f / 10f * 100f, 0f, 0f), new Vector(0f, 0f, 50f)), pelvis);
        Assert.Empty(fake.Object(body.List[1].Object).ForceOffsets);
    }

    [Fact]
    public void WithNoForceBoneAndNoForcePositionNothingIsPushed()
    {
        var (body, _, fake) = Built();

        Assert.All(body.List[..3], e => Assert.Empty(fake.Object(e.Object).ForceOffsets));
        Assert.All(body.List[..3], e => Assert.Equal(Vector.Zero, fake.Object(e.Object).ForceCenter));
    }

    [Fact]
    public void EveryElementIsMarkedPartOfARagdoll()
    {
        var (body, _, fake) = Built();

        Assert.All(body.List[..3], e => Assert.Equal(RagdollSolver.PartOfRagdoll,
                                                    fake.Object(e.Object).GameFlags & RagdollSolver.PartOfRagdoll));
    }

    // ---- the custom blocks -----------------------------------------------------

    [Fact]
    public void ACollisionPairEnablesThosetwoElements()
    {
        var (solver, fake, _) = Solver();
        fake.Blocks = [.. ThreeSolids(), new FakeRagdollVPhysics.CustomBlock("collisionrules", [("collisionpair", "0,2")])];

        solver.Create(new RagdollBody(), Params(Body(), fake), fake.Env);

        Assert.Equal([(0, 2)], fake.Sets[Model].Enabled);
        Assert.Equal(3, fake.Sets[Model].MaxElements);
    }

    [Fact]
    public void SelfCollisionsOffIgnoresEveryPairAfterIt()
    {
        var (solver, fake, _) = Solver();
        fake.Blocks = [.. ThreeSolids(),
            new FakeRagdollVPhysics.CustomBlock("collisionrules", [("selfcollisions", "0"), ("collisionpair", "0,2")])];

        solver.Create(new RagdollBody(), Params(Body(), fake), fake.Env);

        Assert.Empty(fake.Sets[Model].Enabled);
    }

    [Fact]
    public void ACollisionPairReadsEachIndexAsAtoiDoes()
    {
        var fake = new FakeRagdollVPhysics();
        nint set = fake.FindOrCreateCollisionSet(3, 4);
        var rules = new RagdollCollisionRules(fake, set);

        rules.ParseKeyValue("collisionpair", " 3x, 1");
        rules.ParseKeyValue("collisionpair", "4");

        Assert.Equal([(3, 1), (4, 0)], fake.SetById(set).Enabled);
    }

    [Fact]
    public void TheAnimatedFrictionKeysFillTheBodysFriction()
    {
        var (solver, fake, _) = Solver();
        fake.Blocks = [.. ThreeSolids(), new FakeRagdollVPhysics.CustomBlock("animatedfriction",
            [("animfrictionmin", "10"), ("animfrictionmax", "50"), ("animfrictiontimein", "0.5"),
             ("animfrictiontimeout", "1.5f"), ("animfrictiontimehold", "2")])];
        var body = new RagdollBody();

        solver.Create(body, Params(Body(), fake), fake.Env);

        Assert.Equal((10, 50, 0.5f, 1.5f, 2f),
                     (body.AnimFriction.MinAnimatedFriction, body.AnimFriction.MaxAnimatedFriction,
                      body.AnimFriction.FrictionTimeIn, body.AnimFriction.FrictionTimeOut, body.AnimFriction.FrictionTimeHold));
    }

    [Fact]
    public void AnUnknownBlockIsSkipped()
    {
        var (solver, fake, _) = Solver();
        fake.Blocks = [new FakeRagdollVPhysics.CustomBlock("editparams", [("rootname", "pelvis")]), .. ThreeSolids()];
        var body = new RagdollBody();

        Assert.True(solver.Create(body, Params(Body(), fake), fake.Env));
        Assert.Equal(3, body.ListCount);
    }

    [Fact]
    public void TheGroupIsMadeWithStocksDefaults()
    {
        var (body, _, fake) = Built();

        Assert.Equal((0, 15, 3.0f), (fake.Groups[body.Group].Params.AdditionalIterations,
                                     fake.Groups[body.Group].Params.MinErrorTicks,
                                     fake.Groups[body.Group].Params.ErrorTolerance));
    }
}
