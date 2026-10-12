using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The fixture behind Part 6's Phase 5 gate.
///
/// The comparison itself is pixels and has to be live. What can be held here is
/// the property the whole comparison RESTS on: that the two runs differ in the
/// entity and in nothing else. "Same model, same origin, same frame" is the
/// gate's premise, and a premise nothing checks is one that quietly stops being
/// true — a fixture that placed the second prop a unit lower would produce a
/// difference, and it would be a difference about nothing.
/// </summary>
public class PropShotTests
{
    private static (PropShotCommand Command, RecordingEngine Engine) Probe()
    {
        var engine = new RecordingEngine { Strict = false };
        engine.Returns("Entities.Create", new EntityHandle(0x4001));
        engine.Returns("Entities.IsValid", true);
        engine.Returns("Entities.Named", new List<EntityHandle>());

        return (new PropShotCommand(new RecordingLog(), engine, engine), engine);
    }

    [Fact]
    public void BothRunsArePlacedFromTheSameConstants()
    {
        // Not "the two calls agree" — the two calls read the SAME fields, so
        // there is nothing for them to disagree about. This asserts the fields
        // exist and are the ones a driver can rely on.
        var (command, engine) = Probe();

        command.Execute("prop_physics");
        var firstOrigin = engine.Calls.Last(c => c.Member == "Entities.Teleport").ArgAs<Vector>(1);

        command.Execute("prop_physics_multiplayer");
        var secondOrigin = engine.Calls.Last(c => c.Member == "Entities.Teleport").ArgAs<Vector>(1);

        Assert.Equal(PropShot.Origin, firstOrigin);
        Assert.Equal(PropShot.Origin, secondOrigin);
    }

    [Fact]
    public void TheModelIsSetBeforeTheEntitySpawns()
    {
        // A prop that spawns without a model can be removed by its own Spawn,
        // and the driver would then be photographing an empty floor — which is
        // exactly what happened on the first live run of this gate, for a
        // different reason.
        var (command, engine) = Probe();

        command.Execute("prop_physics");

        int model = engine.Calls.ToList().FindIndex(
            c => c.Member == "Entities.SetKeyValue" && c.ArgAs<string>(1) == "model");
        int spawn = engine.Calls.ToList().FindIndex(c => c.Member == "Entities.Spawn");

        Assert.True(model >= 0, "the fixture sets a model");
        Assert.True(spawn >= 0, "the fixture spawns the entity");
        Assert.True(model < spawn, "the model is set before Spawn, not after it");
    }

    [Fact]
    public void ThePoseIsRestatedAfterSpawn()
    {
        // Spawn moves props: dropped to the floor, snapped to their own bounds,
        // settled by the solver. Whatever it did, the pose is said again last,
        // or the comparison measures the two different moves rather than the two
        // renderings.
        var (command, engine) = Probe();

        command.Execute("prop_physics");

        var calls = engine.Calls.ToList();
        int spawn = calls.FindIndex(c => c.Member == "Entities.Spawn");
        int teleport = calls.FindLastIndex(c => c.Member == "Entities.Teleport");

        Assert.True(teleport > spawn, "the pose is restated after Spawn");
    }

    [Fact]
    public void MotionIsDisabledBeforeThePoseIsFixed()
    {
        // The stock comparand is a physics prop. One still falling is at a
        // different height in every frame.
        var (command, engine) = Probe();

        command.Execute("prop_physics_multiplayer");

        var calls = engine.Calls.ToList();
        int motion = calls.FindIndex(
            c => c.Member == "Entities.FireInput" && c.ArgAs<string>(1) == "DisableMotion");
        int teleport = calls.FindLastIndex(c => c.Member == "Entities.Teleport");

        Assert.True(motion >= 0, "the fixture disables motion");
        Assert.True(motion < teleport, "motion is disabled before the pose is fixed, not after");
    }

    [Fact]
    public void ThePreviousPropIsTakenAwayBeforeTheNextArrives()
    {
        // Two props at one origin is the failure that would look most like a
        // pass: the second frame contains both, and the parts of it the first
        // frame already had match perfectly.
        var (command, engine) = Probe();
        engine.Returns("Entities.Named", new List<EntityHandle> { new(0x3001) });

        command.Execute("prop_physics");

        var calls = engine.Calls.ToList();
        int removed = calls.FindIndex(c => c.Member == "Entities.Remove");
        int created = calls.FindIndex(c => c.Member == "Entities.Create");

        Assert.True(removed >= 0, "the fixture removes what it placed last time");
        Assert.True(removed < created, "it removes the old prop before creating the new one");
    }

    [Fact]
    public void ClearRemovesAndPlacesNothing()
    {
        var (command, engine) = Probe();
        engine.Returns("Entities.Named", new List<EntityHandle> { new(0x3001) });

        command.Execute("clear");

        Assert.True(engine.Called("Entities.Remove"), "clear removes the placed prop");
        Assert.False(engine.Called("Entities.Create"), "clear places nothing");
    }

    [Fact]
    public void AnEmptyArgumentIsClearRatherThanAnEntityWithNoName()
    {
        var (_, engine) = Probe();
        var command = new PropShotCommand(new RecordingLog(), engine, engine);

        command.Execute("   ");

        Assert.False(engine.Called("Entities.Create"), "a blank argument creates nothing");
    }

    //-------------------------------------------------------------------------
    // The yaw, and why it is worth naming.
    //
    // The two hierarchies do not network angles the same way — DT_BaseEntity
    // sends m_angRotation through anglemod and then 13 bits, the standalone
    // table sends the float untouched — so a yaw the stock format cannot carry
    // puts the two props in DIFFERENT POSES before a pixel is drawn. That is a
    // confound in the gate's own premise ("same model, same origin, same
    // angles"), and the only way to hold it still is to be able to ask for a
    // yaw both formats carry exactly.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheDefaultYawIsUsedWhenNoneIsNamed()
    {
        var (command, engine) = Probe();

        command.Execute("prop_physics");

        var angles = engine.Calls.Last(c => c.Member == "Entities.Teleport").ArgAs<Vector>(2);

        Assert.Equal(PropShot.Angles, angles);
        Assert.Equal(PropShot.DefaultYaw, angles.Y);
    }

    [Fact]
    public void ANamedYawReplacesTheDefaultInBothPlacements()
    {
        // Both places, because SetAngles runs before Spawn and Teleport runs
        // after it, and a yaw honoured by only one of them is a prop that is
        // spawned at one angle and photographed at another.
        var (command, engine) = Probe();

        command.Execute("prop_physics 45");

        var set = engine.Calls.Last(c => c.Member == "Entities.SetAngles").ArgAs<Vector>(1);
        var teleported = engine.Calls.Last(c => c.Member == "Entities.Teleport").ArgAs<Vector>(2);

        Assert.Equal(45f, set.Y);
        Assert.Equal(45f, teleported.Y);
        Assert.Equal(0f, set.X);
        Assert.Equal(0f, set.Z);
    }

    [Fact]
    public void AYawThatIsNotANumberLeavesTheDefaultAlone()
    {
        // The argument comes off a console line. A driver that fat-fingers it
        // must not silently photograph a prop lying on its side, and must not
        // refuse to place one either.
        var (command, engine) = Probe();

        command.Execute("prop_physics sideways");

        var angles = engine.Calls.Last(c => c.Member == "Entities.Teleport").ArgAs<Vector>(2);

        Assert.Equal(PropShot.DefaultYaw, angles.Y);
        Assert.True(engine.Called("Entities.Create"), "the prop is still placed");
    }

    [Fact]
    public void TheClassNameIsStillTheFirstWordWhenAYawFollowsIt()
    {
        var (command, engine) = Probe();

        command.Execute("  prop_physics_multiplayer   45  ");

        Assert.Equal("prop_physics_multiplayer",
            engine.Calls.Last(c => c.Member == "Entities.Create").ArgAs<string>(0));
    }

    [Fact]
    public void ClearStillClearsWhenAYawFollowsIt()
    {
        var (command, engine) = Probe();
        engine.Returns("Entities.Named", new List<EntityHandle> { new(0x3001) });

        command.Execute("clear 45");

        Assert.True(engine.Called("Entities.Remove"), "clear removes the placed prop");
        Assert.False(engine.Called("Entities.Create"), "clear places nothing");
    }

    //-------------------------------------------------------------------------
    // Which yaws survive the stock wire format.
    //
    // The arithmetic is the engine's, restated: SendProxy_Angles runs anglemod
    // (a truncation to 1/65536 of a turn) and SendPropQAngles then writes 13
    // bits (a truncation to 1/8192). A yaw survives only if it is a whole
    // number of 8192ths of 360 degrees.
    //
    // These cases are computed by hand here and by the arithmetic there, so the
    // two cannot agree by being wrong the same way.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheGatesOwnDefaultYawIsNotCarriedExactly()
    {
        // 35 * 65536/360 is 6371.55, which truncates to 6371 and comes back as
        // 34.9969; 13 bits then take it to 34.98046875. This is the fact the
        // whole option exists for, and it is the gate's DEFAULT, so a change
        // that quietly made it exact would also make every other case here
        // meaningless.
        Assert.False(PropShot.YawIsExactlyNetworked(PropShot.DefaultYaw));
        Assert.Equal(35f, PropShot.DefaultYaw);
    }

    [Theory]
    // Whole numbers of 8192ths of a turn: 0, 1024, 2048, 512, 4096.
    [InlineData(0f)]
    [InlineData(45f)]
    [InlineData(90f)]
    [InlineData(22.5f)]
    [InlineData(180f)]
    public void AYawOnTheThirteenBitGridSurvivesBothTruncations(float yaw)
    {
        Assert.True(PropShot.YawIsExactlyNetworked(yaw), $"{yaw} should survive");
    }

    [Theory]
    // Not whole numbers of 8192ths: 35 is 796.44, 1 is 22.75, 30 is 682.67,
    // 44.9 is 1021.7.
    [InlineData(35f)]
    [InlineData(1f)]
    [InlineData(30f)]
    [InlineData(44.9f)]
    public void AYawOffTheGridDoesNot(float yaw)
    {
        Assert.False(PropShot.YawIsExactlyNetworked(yaw), $"{yaw} should not survive");
    }

    [Theory]
    // The VALUE, not the verdict, and computed by hand: 35/360 * 8192 is
    // 796.44, which truncates to 796, and 796 * 360/8192 is 34.98046875. A
    // boolean can be right for the wrong reason — a grid one step off would
    // still answer "not exact" for 35 — and these are what a divergence note
    // has to be able to quote.
    [InlineData(35f, 34.98046875f)]
    [InlineData(45f, 45f)]
    [InlineData(0f, 0f)]
    [InlineData(1f, 0.966796875f)]      // 22.75 -> 22 -> 22 * 360/8192
    [InlineData(30f, 29.970703125f)]    // 682.67 -> 682
    public void TheNetworkedYawIsTheThirteenBitGridValueBelow(float yaw, float expected)
    {
        Assert.Equal(expected, PropShot.NetworkedYaw(yaw));
    }

    [Fact]
    public void TheManagedSideCarriesTheYawUntouched()
    {
        // The other half of the divergence, and the half that makes it one:
        // the standalone table sends m_angRotation as a 32-bit float with
        // SPROP_NOSCALE, so a managed prop stands at exactly what it was told.
        // What the fixture asks for is therefore what the MANAGED prop is drawn
        // at, and NetworkedYaw above is what the STOCK one is drawn at — the
        // gap between the two is the confound.
        var (command, engine) = Probe();

        command.Execute("prop_dynamic 35");

        var angles = engine.Calls.Last(c => c.Member == "Entities.Teleport").ArgAs<Vector>(2);

        Assert.Equal(35f, angles.Y);
        Assert.NotEqual(PropShot.NetworkedYaw(35f), angles.Y);
    }
}
