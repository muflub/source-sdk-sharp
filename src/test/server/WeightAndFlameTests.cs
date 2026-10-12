using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The weight button's latch.
///
/// Weighing the plate needs crates in a world, which a test has no way to make,
/// so what is asked here is the half that decides: given a weight, does the
/// plate press once, let go once, and stay quiet in between. That is where this
/// entity can be wrong without anything complaining — a latch that re-fires
/// every tick looks to a mapper like a working button wired to a broken door,
/// and a latch that never releases looks like a crate that never left.
///
/// The flame is named in this file's title and is deliberately absent from it:
/// entityflame stayed native, and the reasoning is in the report rather than in
/// a test of something that did not move.
/// </summary>
public class WeightAndFlameTests
{

    private static FuncWeightButton Spawn(uint index, string weightToActivate)
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(FuncWeightButton).Assembly);
        registry.Services = Services();

        var handle = new EntityHandle(index);

        registry.KeyValue(handle, EntityNames.FuncWeightButton, "WeightToActivate", weightToActivate);
        registry.Spawn(handle, EntityNames.FuncWeightButton);

        return (FuncWeightButton)registry.Get(handle)!;
    }

    [Fact]
    public void PlatePressesOnceUnderLoad()
    {
        var plate = Spawn(0x0041_0001u, "100");

        plate.Weigh(40f);
        Assert.True(plate.OnPressed.FireCount == 0, "a plate under too little weight stays up");

        plate.Weigh(150f);
        Assert.True(plate.OnPressed.FireCount == 1, "enough weight presses it");

        // The think runs every tick, so this is the difference between one
        // output and sixty-six a second.
        plate.Weigh(150f);
        plate.Weigh(400f);
        Assert.True(plate.OnPressed.FireCount == 1, "more weight on a pressed plate changes nothing");
        Assert.True(plate.OnReleased.FireCount == 0, "and does not report a release");
    }

    [Fact]
    public void PlateReleasesWhenTheLoadGoes()
    {
        var plate = Spawn(0x0041_0002u, "100");

        plate.Weigh(150f);
        plate.Weigh(10f);

        Assert.True(plate.OnReleased.FireCount == 1, "taking the weight off releases it");

        plate.Weigh(0f);
        Assert.True(plate.OnReleased.FireCount == 1, "an empty plate does not keep releasing");

        plate.Weigh(150f);
        Assert.True(plate.OnPressed.FireCount == 2, "and it can be pressed again");
    }

    [Fact]
    public void PlateIgnoresExactlyItsThreshold()
    {
        var plate = Spawn(0x0041_0003u, "100");

        plate.Weigh(100f);
        Assert.True(plate.OnPressed.FireCount == 0, "weight equal to the threshold does not press");

        plate.Weigh(101f);
        plate.Weigh(100f);
        Assert.True(plate.OnReleased.FireCount == 0, "and does not release a pressed plate either");
    }

    [Fact]
    public void PlateWithNoThresholdAnswersAnyWeight()
    {
        // Stock leaves the keyvalue at zero when a mapper omits it, which makes
        // the plate answer to anything with a mass at all. Worth pinning: it is
        // the behaviour a map with an unset key gets.
        var plate = Spawn(0x0041_0004u, "0");

        plate.Weigh(0f);
        Assert.True(plate.OnPressed.FireCount == 0, "an unkeyed plate is not pressed by nothing");

        plate.Weigh(1f);
        Assert.True(plate.OnPressed.FireCount == 1, "an unkeyed plate is pressed by anything");
    }
}
