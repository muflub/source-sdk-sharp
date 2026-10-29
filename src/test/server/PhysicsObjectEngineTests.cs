using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A CONSTRAINT, A SPRING AND A MOTION CONTROLLER TALK TO THE ENGINE THEY WERE
/// MADE WITH (plan_abi_hotreload D8).
///
/// <para>
/// All three used to reach the static <c>Engine</c>, which follows whichever
/// side is active, so an object made by one game instance would have been
/// driven, validated and destroyed in another's table. Each is now handed its
/// side's physics area by whoever made it — an entity, from its constructor —
/// and every later call goes to that area and nowhere else.
/// </para>
/// </summary>
public class PhysicsObjectEngineTests
{
    private static readonly EntityHandle A = new(0x2A0001);
    private static readonly EntityHandle B = new(0x2B0002);

    private static RecordingEngine Engine(uint created)
        => new RecordingEngine()
            .Returns("Constraints.Create", created)
            .Returns("Constraints.IsValid", true)
            .Returns("Springs.Create", created)
            .Returns("Springs.IsValid", true)
            .Returns("MotionControllers.Create", created);

    [Fact]
    public void AConstraintIsMadeDrivenAndDestroyedInItsOwnEnginesTable()
    {
        var mine = Engine(7u);
        var other = Engine(9u);

        Constraint? joint = Constraint.Fixed(new RecordingJointTables(mine), A, B);

        Assert.NotNull(joint);
        Assert.True(joint!.IsValid);
        joint.SetHingeVelocity(90f, 1f);
        joint.Destroy();

        Assert.Equal(1, mine.CountOf("Constraints.Create"));
        Assert.Equal(1, mine.CountOf("Constraints.IsValid"));
        Assert.Equal(7u, mine.Last("Constraints.SetHingeVelocity")!.Value.ArgAs<uint>(0));
        Assert.Equal(7u, mine.Last("Constraints.Destroy")!.Value.ArgAs<uint>(0));
        Assert.Empty(other.Calls);
    }

    [Fact]
    public void ASpringIsMadeTunedAndDestroyedInItsOwnEnginesTable()
    {
        var mine = Engine(5u);
        var other = Engine(6u);

        Spring? spring = Spring.Make(new RecordingJointTables(mine), A, B, Vector.Zero, new Vector(0, 0, 64),
                                     constant: 50f, damping: 1f, relativeDamping: 0f,
                                     naturalLength: 64f);

        Assert.NotNull(spring);
        Assert.True(spring!.IsValid);
        spring.SetLength(32f);
        spring.Destroy();

        Assert.Equal(1, mine.CountOf("Springs.Create"));
        Assert.Equal(5u, mine.Last("Springs.SetLength")!.Value.ArgAs<uint>(0));
        Assert.Equal(5u, mine.Last("Springs.Destroy")!.Value.ArgAs<uint>(0));
        Assert.Empty(other.Calls);
    }

    [Fact]
    public void AMotionControllerIsMadeDrivenAndDestroyedInItsOwnEnginesTable()
    {
        var mine = Engine(3u);
        var other = Engine(4u);

        MotionController? controller = MotionController.Unattached(new RecordingJointTables(mine), MotionControllerKind.Medium);

        Assert.NotNull(controller);
        controller!.AttachObject(A);
        controller.SetActive(true);
        controller.Destroy();

        Assert.Equal(EntityHandle.None, mine.Last("MotionControllers.Create")!.Value.ArgAs<EntityHandle>(0));
        Assert.Equal(3u, mine.Last("MotionControllers.Attach")!.Value.ArgAs<uint>(0));
        Assert.Equal(3u, mine.Last("MotionControllers.SetActive")!.Value.ArgAs<uint>(0));
        Assert.Equal(3u, mine.Last("MotionControllers.Destroy")!.Value.ArgAs<uint>(0));
        Assert.Empty(other.Calls);
    }

    /// <summary>
    /// A zero id is the engine refusing, and the factory hands back nothing to
    /// drive — so no later call can reach a table for an object that was never
    /// made.
    /// </summary>
    [Fact]
    public void AnObjectTheEngineRefusedIsNotMade()
    {
        var engine = Engine(0u);

        Assert.Null(Constraint.Fixed(new RecordingJointTables(engine), A, B));
        Assert.Null(Spring.Make(new RecordingJointTables(engine), A, B, Vector.Zero, Vector.Zero, 1f, 1f, 0f, 0f));
        Assert.Null(MotionController.Attach(new RecordingJointTables(engine), A, MotionControllerKind.ConstantForce));

        Assert.Equal(1, engine.CountOf("Constraints.Create"));
        Assert.Equal(1, engine.CountOf("Springs.Create"));
        Assert.Equal(1, engine.CountOf("MotionControllers.Create"));
    }

    /// <summary>
    /// THE ENTITY THAT MAKES THE OBJECT HANDS IT ITS OWN SIDE'S AREA. Two
    /// containers, two engines: a trigger_vphysics_motion spawned in each makes
    /// its medium in its own container's table, and each engine hears exactly
    /// one creation — its own entity's.
    /// </summary>
    [Fact]
    public void AMediumIsMadeInItsOwnContainersEngine()
    {
        var mine = SpawnMedium(21u);
        var other = SpawnMedium(22u);

        Assert.Equal(1, mine.CountOf("MotionControllers.Create"));
        Assert.Equal(1, other.CountOf("MotionControllers.Create"));
        Assert.All(mine.AllOf("MotionControllers.SetMedium"), c => Assert.Equal(21u, c.ArgAs<uint>(0)));
        Assert.All(other.AllOf("MotionControllers.SetMedium"), c => Assert.Equal(22u, c.ArgAs<uint>(0)));
        Assert.True(mine.Called("MotionControllers.SetMedium"), "the medium was never tuned");
    }

    private static RecordingEngine SpawnMedium(uint id)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TriggerVPhysicsMotion).Assembly);
        var services = TestServices.Services(out var engine);
        services.AddInstance<IEntityDirectory>(registry);
        registry.Services = services;
        engine.Ignores("Clock.Now", "Networking.EntityGetState", "Networking.EntityGetState.moveType")
              .Returns("MotionControllers.Create", id);

        registry.Spawn(new EntityHandle(1), EntityNames.TriggerVphysicsMotion);
        return engine;
    }
}
