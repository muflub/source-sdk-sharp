using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ragdolls;
using SourceSharp.Shared.Bones;
using Xunit;
using static SourceSharp.Tests.Ragdolls.RagdollFixtures;
using NVector = System.Numerics.Vector3;

namespace SourceSharp.Tests.Ragdolls;

/// <summary>
/// ClientRagdolls, the client's bodies by owner: what ragdoll_library.cpp and
/// C_StandaloneEntity::BecomeRagdoll did natively (the refusals, the table,
/// the first pose, the tail the step writes, the blast push) and stock's
/// CRagdoll::Init order, over the managed solver.
/// </summary>
public class ClientRagdollsTests
{
    private static readonly EntityHandle Owner = new(0x0003_0009u);

    private sealed class Poses : IRagdollPoseSource
    {
        public StudioHdr? Hdr = Body();
        public BoneMatrix3x4[]? Bones = RagdollFixtures.Pose();
        public int Model = RagdollFixtures.Model;
        public int Asked;

        public RagdollPose Pose(EntityHandle owner)
        {
            Asked++;
            return new RagdollPose(Model, Hdr, Bones is null ? null : (BoneMatrix3x4[])Bones.Clone(), BoneMatrix3x4.Identity);
        }

        public readonly List<EntityHandle> Became = [];

        public void BecameRagdoll(EntityHandle owner) => Became.Add(owner);
    }

    private sealed class Tails : IRagdollRenderTail
    {
        public readonly List<(EntityHandle Owner, Vector Origin, Vector Mins, Vector Maxs)> Writes = [];
        public readonly List<(EntityHandle, bool)> Flags = [];
        public bool OwnerAlive = true;

        public bool Write(EntityHandle owner, Vector origin, Vector mins, Vector maxs)
        {
            if (!OwnerAlive)
                return false;
            Writes.Add((owner, origin, mins, maxs));
            return true;
        }

        public void SetRagdoll(EntityHandle owner, bool isRagdoll) => Flags.Add((owner, isRagdoll));
    }

    private sealed class Clock : IRagdollClock
    {
        public float CurTime { get; set; } = 10f;
        public int TickCount { get; set; } = 100;
    }

    private sealed class World : IRagdollWorld
    {
        public readonly List<EntityHandle> Traced = [];

        public bool TraceSolidHits(Vector start, Vector end, EntityHandle owner)
        {
            Traced.Add(owner);
            return true;
        }

        public float SleepAfterTime => 5f;
        public int Developer => 0;
    }

    private sealed class Names : IRagdollNames
    {
        private readonly Dictionary<string, nint> _held = [];
        public int Released;
        public nint NameOf(string name) => _held.TryGetValue(name, out nint p) ? p : _held[name] = 0x7000 + _held.Count;
        public IReadOnlyDictionary<string, nint> Held => _held;
        public void Adopt(string name, nint pointer) => _held[name] = pointer;
        public void ReleaseAll()
        {
            Released += _held.Count;
            _held.Clear();
        }
    }

    private sealed record Rig(ClientRagdolls Ragdolls, FakeRagdollVPhysics Fake, Poses Poses, Tails Tails, Clock Clock,
                              Names Names, RagdollFixtures.Log Log, World World);

    private static Rig Make()
    {
        var fake = new FakeRagdollVPhysics { Blocks = ThreeSolids(), Bounds = (new Vector(-8f, -8f, 0f), new Vector(8f, 8f, 64f)) };
        fake.Surfaces["flesh"] = 5;
        var log = new RagdollFixtures.Log();
        var poses = new Poses();
        var tails = new Tails();
        var clock = new Clock();
        var names = new Names();
        var world = new World();
        var ragdolls = new ClientRagdolls(fake, new RagdollSolver(fake, log, () => 0), poses, tails, clock, world, names, log);
        return new Rig(ragdolls, fake, poses, tails, clock, names, log, world);
    }

    private static Rig Fallen()
    {
        Rig rig = Make();
        Assert.True(rig.Ragdolls.Become(Owner, new Vector(0f, 100f, 0f), 1, out _));
        return rig;
    }

    // ---- becoming one ----------------------------------------------------------

    [Fact]
    public void AnEntityBecomesABodyOfItsModelsSolids()
    {
        Rig rig = Fallen();

        Assert.True(rig.Ragdolls.IsRagdoll(Owner));
        Assert.Equal(3, rig.Ragdolls.Bodies.Single().ListCount);
    }

    [Fact]
    public void ItsObjectsCarryTheOwnersGameData()
        => Assert.All(Fallen().Fake.Objects.Values,
                      o => Assert.Equal(PhysicsGameData.FromHandle(Owner), o.Overrides.GameData));

    [Fact]
    public void ItsObjectsAreNamedForTheModel()
    {
        Rig rig = Fallen();

        Assert.All(rig.Fake.Objects.Values, o => Assert.Equal(rig.Names.Held["test/body.mdl"], o.Overrides.Name));
    }

    [Fact]
    public void TheThrowIsTheForceOnTheForceBone()
    {
        Rig rig = Fallen();
        RagdollBody body = rig.Ragdolls.Bodies.Single();

        Assert.Equal(new Vector(0f, 100f, 0f), rig.Fake.Object(body.List[1].Object).ForceCenter);
    }

    [Fact]
    public void ABodyIsActivatedAwake()
    {
        Rig rig = Fallen();
        RagdollBody body = rig.Ragdolls.Bodies.Single();

        Assert.Equal(1, rig.Fake.Groups[body.Group].Activations);
        Assert.All(body.List[..3], e => Assert.True(rig.Fake.Object(e.Object).CollisionsEnabled));
    }

    [Fact]
    public void ItsCullBoxIsTheModelsRadiusCube()
    {
        Rig rig = Fallen();
        RagdollBody body = rig.Ragdolls.Bodies.Single();
        float radius = new Vector(8f, 8f, 32f).Length;

        Assert.Equal(radius, body.Radius);
        Assert.Equal(new Vector(radius, radius, radius), body.Maxs);
    }

    [Fact]
    public void ItsSettleClockStartsAtCreation()
        => Assert.Equal(10f, Fallen().Ragdolls.Bodies.Single().LastOriginChangeTime);

    [Fact]
    public void TheFirstPoseIsWrittenAtOnceAndTheShellToldAfter()
    {
        Rig rig = Fallen();

        Assert.Equal((Owner, new Vector(0f, 0f, 40f)), (rig.Tails.Writes.Single().Owner, rig.Tails.Writes.Single().Origin));
        Assert.Equal([(Owner, true)], rig.Tails.Flags);
    }

    [Fact]
    public void ThePoseSourceIsToldTheSolverOwnsThePoseOnceTheBodyExists()
    {
        Rig rig = Make();
        rig.Fake.SolidCount = 1;
        Assert.False(rig.Ragdolls.Become(Owner, Vector.Zero, -1, out _)); // refused: still animated
        rig.Fake.SolidCount = -1;

        Assert.True(rig.Ragdolls.Become(Owner, Vector.Zero, -1, out _));

        Assert.Equal([Owner], rig.Poses.Became);
    }

    [Fact]
    public void ASecondAskAnswersTheBodyItHasAndBuildsNothing()
    {
        Rig rig = Fallen();
        int objects = rig.Fake.Objects.Count;

        Assert.True(rig.Ragdolls.Become(Owner, Vector.Zero, -1, out RagdollRefusal refusal));
        Assert.Equal((objects, RagdollRefusal.None, 1), (rig.Fake.Objects.Count, refusal, rig.Poses.Asked));
    }

    [Fact]
    public void WithNoEnvironmentNothingIsBuilt()
    {
        Rig rig = Make();
        rig.Fake.Env = 0;

        Assert.False(rig.Ragdolls.Become(Owner, Vector.Zero, -1, out RagdollRefusal refusal));
        Assert.Equal(RagdollRefusal.NoEnvironment, refusal);
    }

    [Fact]
    public void AModelWithNoHeaderIsRefused()
    {
        Rig rig = Make();
        rig.Poses.Hdr = null;

        Assert.False(rig.Ragdolls.Become(Owner, Vector.Zero, -1, out RagdollRefusal refusal));
        Assert.Equal(RagdollRefusal.NoModel, refusal);
    }

    [Fact]
    public void AOneSolidModelIsARigidPropAndRefused()
    {
        Rig rig = Make();
        rig.Fake.SolidCount = 1;

        Assert.False(rig.Ragdolls.Become(Owner, Vector.Zero, -1, out RagdollRefusal refusal));
        Assert.Equal(RagdollRefusal.NotJointed, refusal);
        Assert.Empty(rig.Fake.Objects);
    }

    [Fact]
    public void AModelWithNoSequencesHasNoPoseAndIsRefused()
    {
        Rig rig = Make();
        rig.Poses.Hdr = Body(sequences: false);

        Assert.False(rig.Ragdolls.Become(Owner, Vector.Zero, -1, out RagdollRefusal refusal));
        Assert.Equal(RagdollRefusal.NotPoseable, refusal);
    }

    [Fact]
    public void APoseShorterThanTheModelIsRefused()
    {
        Rig rig = Make();
        rig.Poses.Bones = Pose()[..2];

        Assert.False(rig.Ragdolls.Become(Owner, Vector.Zero, -1, out RagdollRefusal refusal));
        Assert.Equal(RagdollRefusal.NoBones, refusal);
    }

    [Fact]
    public void ABodyTheSolverCannotBuildIsTakenApartAndSaid()
    {
        Rig rig = Make();
        rig.Fake.Blocks = [new FakeRagdollVPhysics.SolidBlock(0, "tail", "flesh"), new FakeRagdollVPhysics.SolidBlock(1, "fin", "flesh")];

        Assert.False(rig.Ragdolls.Become(Owner, Vector.Zero, -1, out RagdollRefusal refusal));
        Assert.Equal(RagdollRefusal.Solver, refusal);
        Assert.Contains("Bad ragdoll for test/body.mdl\n", rig.Log.Lines);
        Assert.False(rig.Ragdolls.IsRagdoll(Owner));
    }

    // ---- the unposed bone ---------------------------------------------------------

    [Fact]
    public void ABoneNoMaskWroteIsPlacedFromItsBindPoseUnderItsParent()
    {
        StudioHdr hdr = Body();
        BoneMatrix3x4[] bones = Pose();
        bones[3] = default;

        Assert.Equal(1, ClientRagdolls.FillUnwrittenBones(hdr, BoneMatrix3x4.Identity, bones));
        // jaw: (2, 0, 0) from the head at z 60.
        Assert.Equal(new NVector(2f, 0f, 60f), bones[3].Position);
    }

    [Fact]
    public void AWrittenBoneIsLeftAlone()
    {
        BoneMatrix3x4[] bones = Pose();
        bones[1] = Translation(7f, 7f, 7f);

        ClientRagdolls.FillUnwrittenBones(Body(), BoneMatrix3x4.Identity, bones);

        Assert.Equal(new NVector(7f, 7f, 7f), bones[1].Position);
    }

    // ---- the step --------------------------------------------------------------------

    [Fact]
    public void TheStepWritesEveryBodysPoseIntoItsTail()
    {
        Rig rig = Fallen();
        RagdollBody body = rig.Ragdolls.Bodies.Single();
        rig.Fake.SetPositionMatrix(body.List[0].Object, At(5f, 6f, 7f), false);

        Assert.Equal(1, rig.Ragdolls.StepPoses());
        Assert.Equal(new Vector(5f, 6f, 7f), rig.Tails.Writes[^1].Origin);
    }

    [Fact]
    public void AMovedBodyIsUpdatedBeforeItsPoseIsWritten()
    {
        Rig rig = Fallen();
        RagdollBody body = rig.Ragdolls.Bodies.Single();
        rig.Fake.Active.Add(body.List[2].Object);
        body.Mins = body.Maxs = Vector.Zero;

        rig.Ragdolls.StepPoses();

        Assert.Equal(1, rig.Ragdolls.LastUpdated);
        Assert.Equal(body.Radius, rig.Tails.Writes[^1].Maxs.X);
    }

    [Fact]
    public void ABodyTheStepDidNotMoveIsNotUpdated()
    {
        Rig rig = Fallen();
        rig.Ragdolls.Bodies.Single().LastOriginChangeTime = -100f;

        rig.Ragdolls.StepPoses();

        Assert.Equal(0, rig.Ragdolls.LastUpdated);
        Assert.Equal(-100f, rig.Ragdolls.Bodies.Single().LastOriginChangeTime);
    }

    [Fact]
    public void AStillBodyIsPutToSleepByTheStep()
    {
        Rig rig = Fallen();
        RagdollBody body = rig.Ragdolls.Bodies.Single();
        rig.Fake.Active.Add(body.List[0].Object);
        body.LastOrigin = new Vector(0f, 0f, 40f);
        rig.Clock.CurTime = 15f;

        rig.Ragdolls.StepPoses();

        Assert.All(body.List[..3], e => Assert.True(rig.Fake.Object(e.Object).Asleep));
    }

    [Fact]
    public void TheScanCountsABodyOnceWhateverNumberOfItsObjectsMoved()
    {
        Rig rig = Fallen();
        RagdollBody body = rig.Ragdolls.Bodies.Single();
        Assert.Equal(0, rig.Ragdolls.ScanMoved());

        rig.Fake.Active.Add(body.List[1].Object);
        rig.Fake.Active.Add(body.List[2].Object);

        Assert.Equal(1, rig.Ragdolls.ScanMoved());
    }

    [Fact]
    public void ASteadyStepAllocatesNothing()
    {
        // plan_physics_bones_perf B3: the active list, its set and the
        // separation trace are the step's own, kept from frame to frame.
        Rig rig = Fallen();
        RagdollBody body = rig.Ragdolls.Bodies.Single();
        rig.Fake.Active.Add(body.List[0].Object);
        rig.Fake.Active.Add(body.List[2].Object);
        rig.Ragdolls.StepPoses();
        rig.Clock.CurTime += 0.015f;
        rig.Ragdolls.StepPoses();

        long before = GC.GetAllocatedBytesForCurrentThread();
        rig.Clock.CurTime += 0.015f;
        rig.Ragdolls.StepPoses();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(1, rig.Ragdolls.LastUpdated);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void TheSeparationPassTracesForTheBodyBeingUpdated()
    {
        // Two bodies, each with a limb off its attachment and its group in
        // error: each body's pass traces on behalf of its own owner.
        Rig rig = Fallen();
        var second = new EntityHandle(0x0003_000Au);
        Assert.True(rig.Ragdolls.Become(second, Vector.Zero, 0, out _));

        foreach (RagdollBody body in rig.Ragdolls.Bodies)
        {
            rig.Fake.Object(body.List[0].Object).Mass = 5f;
            rig.Fake.Object(body.List[1].Object).Mass = 5f;
            rig.Fake.SetPositionMatrix(body.List[1].Object, At(30f, 0f, 50f), false);
            rig.Fake.SetPositionMatrix(body.List[2].Object, At(30f, 0f, 60f), false);
            rig.Fake.Object(body.List[1].Object).Contacts.Add((new Vector(-1f, 0f, 0f), 0x9999));
            rig.Fake.Groups[body.Group].ErrorState = true;
            rig.Fake.Object(body.List[1].Object).Asleep = false;
            rig.Fake.Active.Add(body.List[1].Object);
        }

        rig.Ragdolls.StepPoses();

        Assert.Equal(2, rig.Ragdolls.LastUpdated);
        Assert.Equal(new[] { Owner, second }.OrderBy(h => h.Value), rig.World.Traced.Distinct().OrderBy(h => h.Value));
    }

    [Fact]
    public void TheCostMeterCountsAStepOnlyWithBodiesAndAWritePerBodyWritten()
    {
        Rig rig = Make();
        rig.Ragdolls.StepPoses();
        Assert.Equal(0, rig.Ragdolls.StepBodyFrames);

        Assert.True(rig.Ragdolls.Become(Owner, new Vector(0f, 100f, 0f), 1, out _));
        rig.Ragdolls.StepPoses();
        rig.Ragdolls.StepPoses();
        rig.Ragdolls.WriteBones(Owner, new BoneMatrix3x4[64], new int[8]);
        rig.Ragdolls.WriteBones(new EntityHandle(0x0003_0042u), new BoneMatrix3x4[64], new int[8]);

        Assert.Equal(2, rig.Ragdolls.StepBodyFrames);
        Assert.Equal(1, rig.Ragdolls.Writes);
        Assert.True(rig.Ragdolls.StepTicks > 0, "a timed step has a duration");

        rig.Ragdolls.ResetCost();
        Assert.Equal(0, rig.Ragdolls.StepBodyFrames + rig.Ragdolls.Writes + rig.Ragdolls.StepTicks + rig.Ragdolls.WriteTicks);
    }

    [Fact]
    public void AnOwnerThatIsGoneIsNotCountedAsWritten()
    {
        Rig rig = Fallen();
        rig.Tails.OwnerAlive = false;

        Assert.Equal(0, rig.Ragdolls.StepPoses());
    }

    // ---- the reads ---------------------------------------------------------------------

    [Fact]
    public void ABodyMadeThisTickSaysSo()
    {
        Rig rig = Fallen();

        Assert.Equal(RagdollState.Present | RagdollState.MadeThisTick, rig.Ragdolls.State(Owner, out Vector origin));
        Assert.Equal(new Vector(0f, 0f, 40f), origin);

        rig.Clock.TickCount++;
        Assert.Equal(RagdollState.Present, rig.Ragdolls.State(Owner, out _));
    }

    [Fact]
    public void AnOwnerWithNoBodyIsNone()
        => Assert.Equal(RagdollState.None, Make().Ragdolls.State(Owner, out _));

    [Fact]
    public void ABlastPushesTheRootAndRestartsTheSleepClock()
    {
        Rig rig = Fallen();
        RagdollBody body = rig.Ragdolls.Bodies.Single();
        rig.Clock.CurTime = 33f;

        Assert.True(rig.Ragdolls.ApplyForceCenter(Owner, new Vector(1f, 2f, 3f)));
        Assert.Equal(new Vector(1f, 2f, 3f), rig.Fake.Object(body.List[0].Object).ForceCenter);
        Assert.Equal(33f, body.LastOriginChangeTime);
    }

    [Fact]
    public void ABlastOnNoBodyPushesNothing()
        => Assert.False(Make().Ragdolls.ApplyForceCenter(Owner, new Vector(1f, 2f, 3f)));

    // ---- a trace's physics bone (c_stickybolt.cpp's lookup) ------------------------

    [Fact]
    public void APhysicsBoneNamesTheElementAtThatIndex()
    {
        Rig rig = Fallen();
        RagdollBody body = rig.Ragdolls.Bodies.Single();
        Assert.True(body.ListCount == 3, $"three elements: {body.ListCount}");

        for (int i = 0; i < body.ListCount; i++)
        {
            nint element = rig.Ragdolls.ElementObject(Owner, i);
            Assert.True(element != 0 && element == body.List[i].Object, $"physicsbone {i} is list[{i}].pObject");
        }
    }

    [Fact]
    public void APhysicsBoneOutsideTheListNamesNoElement()
    {
        Rig rig = Fallen();
        Assert.True(rig.Ragdolls.ElementObject(Owner, 0) != 0, "a bone inside the list names one");
        Assert.True(rig.Ragdolls.ElementObject(Owner, -1) == 0, "-1 names none");
        Assert.True(rig.Ragdolls.ElementObject(Owner, 3) == 0, "listCount names none");
        Assert.True(Make().Ragdolls.ElementObject(Owner, 0) == 0, "nor does an owner with no body");
    }

    [Fact]
    public void TheOwnersAreTheBodiesThatStand()
    {
        Rig rig = Fallen();
        Assert.True(rig.Ragdolls.Owners.SequenceEqual([Owner]), "the fallen owner");

        rig.Ragdolls.Destroy(Owner);
        Assert.True(rig.Ragdolls.Owners.Count == 0, "and none once it is taken apart");
    }

    [Fact]
    public void TheSolversBonesAreWrittenAndListed()
    {
        Rig rig = Fallen();
        var bones = new BoneMatrix3x4[4];
        var owned = new int[RagdollBody.MaxElements];

        Assert.Equal(3, rig.Ragdolls.WriteBones(Owner, bones, owned));
        Assert.Equal([0, 1, 2], owned[..3]);
        Assert.Equal(new NVector(0f, 0f, 40f), bones[0].Position);
    }

    [Fact]
    public void NoBodyWritesNoBones()
        => Assert.Equal(-1, Make().Ragdolls.WriteBones(Owner, new BoneMatrix3x4[4], new int[4]));

    [Fact]
    public void EachBodyHasItsOwnSerial()
    {
        Rig rig = Fallen();
        uint first = rig.Ragdolls.Serial(Owner);
        rig.Ragdolls.Destroy(Owner);
        rig.Ragdolls.Become(Owner, Vector.Zero, -1, out _);

        Assert.NotEqual(0u, first);
        Assert.NotEqual(first, rig.Ragdolls.Serial(Owner));
    }

    // ---- taking apart ---------------------------------------------------------------------

    [Fact]
    public void AnOwnerThatGoesTakesItsBodyApart()
    {
        Rig rig = Fallen();

        Assert.True(rig.Ragdolls.Destroy(Owner));
        Assert.All(rig.Fake.Objects.Values, o => Assert.True(o.Destroyed && !o.CollisionsEnabled));
        Assert.False(rig.Ragdolls.IsRagdoll(Owner));
    }

    [Fact]
    public void TheLevelsEndTakesEveryBodyApartAndFreesTheNames()
    {
        Rig rig = Fallen();
        var other = new EntityHandle(0x0004_000Au);
        rig.Ragdolls.Become(other, Vector.Zero, -1, out _);

        Assert.Equal(2, rig.Ragdolls.DestroyAll());
        Assert.Equal(0, rig.Ragdolls.Count);
        Assert.All(rig.Fake.Objects.Values, o => Assert.True(o.Destroyed));
        Assert.Equal(1, rig.Names.Released);
    }

    [Fact]
    public void AfterTheEnvironmentWentABodyIsOnlyForgotten()
    {
        Rig rig = Fallen();
        rig.Fake.Env = 0;

        Assert.True(rig.Ragdolls.Destroy(Owner));
        Assert.All(rig.Fake.Objects.Values, o => Assert.False(o.Destroyed));
    }

    // ---- the reload ---------------------------------------------------------------------------

    [Fact]
    public void AReloadCarriesTheSameBodyPointerForPointer()
    {
        Rig rig = Fallen();
        RagdollBody before = rig.Ragdolls.Bodies.Single();
        before.LastOriginChangeTime = 12.5f;
        var writer = new ReloadWriter();
        rig.Ragdolls.Save(writer);

        Rig next = Make();
        next.Ragdolls.Restore(new ReloadReader(writer.ToArray()));
        RagdollBody after = next.Ragdolls.Bodies.Single();

        Assert.Equal((before.Owner, before.Serial, before.CreateTick, before.ListCount, before.Group),
                     (after.Owner, after.Serial, after.CreateTick, after.ListCount, after.Group));
        Assert.Equal(before.List[..3].Select(e => (e.Object, e.Constraint, e.ParentIndex, e.OriginParentSpace)),
                     after.List[..3].Select(e => (e.Object, e.Constraint, e.ParentIndex, e.OriginParentSpace)));
        Assert.Equal((before.Radius, 12.5f), (after.Radius, after.LastOriginChangeTime));
        Assert.Equal(rig.Names.Held, next.Names.Held);
    }

    [Fact]
    public void AfterAReloadTheNextBodyGetsANewSerial()
    {
        Rig rig = Fallen();
        var writer = new ReloadWriter();
        rig.Ragdolls.Save(writer);
        Rig next = Make();
        next.Ragdolls.Restore(new ReloadReader(writer.ToArray()));

        next.Ragdolls.Become(new EntityHandle(0x0004_000Au), Vector.Zero, -1, out _);

        Assert.NotEqual(next.Ragdolls.Serial(Owner), next.Ragdolls.Serial(new EntityHandle(0x0004_000Au)));
    }
}
