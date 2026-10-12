using SourceSharp.Abi;
using SourceSharp.Client.Ragdolls;
using Xunit;
using static SourceSharp.Tests.Ragdolls.RagdollFixtures;

namespace SourceSharp.Tests.Ragdolls;

/// <summary>
/// game/client/ragdoll.cpp's CRagdoll: BuildRagdollBounds, VPhysicsUpdate,
/// CheckSettleStationaryRagdoll and PhysForceRagdollToSleep, as RagdollSolver
/// carries them. On this hierarchy none of it ran before the port (the
/// solver's game data names no C_BaseEntity, so VPhysicsUpdate was never
/// called); these facts are stock's.
/// </summary>
public class RagdollSettleTests
{
    private const float SleepAfter = 5f;

    private static (RagdollBody Body, RagdollSolver Solver, FakeRagdollVPhysics Fake) Awake()
    {
        var built = Built();
        built.Solver.Activate(built.Body, built.Fake.Collide(Model), Model);
        built.Solver.BuildBounds(built.Body, new Vector(-10f, -10f, 0f), new Vector(10f, 10f, 70f));
        return built;
    }

    [Fact]
    public void TheRadiusIsHalfTheModelsDiagonal()
    {
        var (body, solver, _) = Built();

        solver.BuildBounds(body, new Vector(0f, 0f, 0f), new Vector(6f, 0f, 8f));

        Assert.Equal(5f, body.Radius);
        Assert.Equal((new Vector(-5f, -5f, -5f), new Vector(5f, 5f, 5f)), (body.Mins, body.Maxs));
    }

    [Fact]
    public void TheOriginIsTheRootElements()
    {
        var (body, solver, _) = Built();

        Assert.Equal(new Vector(0f, 0f, 40f), solver.Origin(body));
    }

    [Fact]
    public void AnUpdateRunsOncePerClockValue()
    {
        var (body, solver, fake) = Awake();
        solver.VPhysicsUpdate(body, 1f, SleepAfter, (_, _) => false, 0);
        fake.Groups[body.Group].ErrorState = true;

        solver.VPhysicsUpdate(body, 1f, SleepAfter, (_, _) => false, 0);

        Assert.Equal(0, fake.Groups[body.Group].ErrorCleared);
    }

    [Fact]
    public void AnAwakeBodyIsCulledByItsRadiusCube()
    {
        var (body, solver, _) = Awake();
        body.Mins = body.Maxs = Vector.Zero;

        solver.VPhysicsUpdate(body, 1f, SleepAfter, (_, _) => false, 0);

        Assert.Equal((new Vector(-body.Radius, -body.Radius, -body.Radius), new Vector(body.Radius, body.Radius, body.Radius)),
                     (body.Mins, body.Maxs));
    }

    [Fact]
    public void ASleepingBodyIsCulledByItsExactBoxAboutTheOrigin()
    {
        var (body, solver, fake) = Built();
        foreach (RagdollElement e in body.List[..3])
            fake.Object(e.Object).Aabb = (new Vector(-1f, -1f, -1f), new Vector(1f, 1f, 1f));

        solver.VPhysicsUpdate(body, 1f, SleepAfter, (_, _) => false, 0);

        Assert.True(body.AllAsleep);
        Assert.Equal((new Vector(-1f, -1f, -1f), new Vector(1f, 1f, 21f)), (body.Mins, body.Maxs));
    }

    [Fact]
    public void AnAwakeBodyInErrorIsSeparated()
    {
        var (body, solver, fake) = Awake();
        fake.Groups[body.Group].ErrorState = true;

        solver.VPhysicsUpdate(body, 1f, SleepAfter, (_, _) => false, 0);

        Assert.Equal(1, fake.Groups[body.Group].ErrorCleared);
    }

    [Fact]
    public void MovingMoreThanAUnitRestartsTheSettleClock()
    {
        var (body, solver, fake) = Awake();
        body.LastOrigin = new Vector(0f, 0f, 38.5f);
        body.LastOriginChangeTime = 1f;

        solver.CheckSettleStationary(body, 20f, SleepAfter);

        Assert.Equal((20f, new Vector(0f, 0f, 40f)), (body.LastOriginChangeTime, body.LastOrigin));
        Assert.All(body.List[..3], e => Assert.Equal(0, fake.Object(e.Object).Sleeps));
    }

    [Fact]
    public void ABodyStillForTheSleepTimeIsPutToSleep()
    {
        var (body, solver, fake) = Awake();
        body.LastOrigin = new Vector(0f, 0f, 40f);
        body.LastOriginChangeTime = 1f;
        var spine = fake.Object(body.List[1].Object);
        spine.Velocity = new Vector(3f, 0f, 0f);
        spine.Contacts.Add((new Vector(1f, 0f, 0f), 0x9999));

        solver.CheckSettleStationary(body, 6f, SleepAfter);

        Assert.All(body.List[..3], e => Assert.True(fake.Object(e.Object).Asleep));
        Assert.Equal((Vector.Zero, 1), (spine.Velocity, spine.FrictionCleared));
    }

    [Fact]
    public void ABodyStillForLessThanTheSleepTimeIsLeftAwake()
    {
        var (body, solver, fake) = Awake();
        body.LastOrigin = new Vector(0f, 0f, 40f);
        body.LastOriginChangeTime = 1f;

        solver.CheckSettleStationary(body, 5.9f, SleepAfter);

        Assert.All(body.List[..3], e => Assert.False(fake.Object(e.Object).Asleep));
    }

    [Fact]
    public void ABodyAlreadyAsleepIsNotForcedAgain()
    {
        var (body, solver, fake) = Awake();
        body.LastOrigin = new Vector(0f, 0f, 40f);
        body.LastOriginChangeTime = 1f;
        body.AllAsleep = true;

        solver.CheckSettleStationary(body, 60f, SleepAfter);

        Assert.All(body.List[..3], e => Assert.Equal(0, fake.Object(e.Object).Sleeps));
    }

    [Fact]
    public void TheSettleIsAskedOnEveryUpdate()
    {
        var (body, solver, fake) = Awake();
        body.LastOrigin = new Vector(0f, 0f, 40f);
        body.LastOriginChangeTime = 1f;

        solver.VPhysicsUpdate(body, 7f, SleepAfter, (_, _) => false, 0);

        Assert.All(body.List[..3], e => Assert.Equal(1, fake.Object(e.Object).Sleeps));
    }
}
