using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The two things a vending machine's can and a gravity target decide on their
/// own, neither of which the engine can be asked about here.
///
/// For the can it is WHEN it may be collected. It spends its first half second
/// falling, and a trigger reports a player standing in it every tick — so both
/// ends of its life are state rather than an assumption about being called once,
/// and both are wrong in ways that only show up as a machine that empties
/// itself in a single pass.
///
/// For target_changegravity it is what a written zero means, which is the one
/// number in the entity that does not mean what it says.
/// </summary>
public class SodaAndGravityTests
{
    private static readonly EntityHandle Can = new(0x0031_0031u);
    private static readonly EntityHandle Player = new(0x0032_0032u);

    //-------------------------------------------------------------------------
    // The half second, from both ends.
    //-------------------------------------------------------------------------
    [Fact]
    public void TheCanFallsBeforeItCanBeTaken()
    {
        var (registry, can) = FreshCan();

        // Falling. A can collected here is one the machine has not finished
        // dispensing, and the machine allows exactly one at a time — so an
        // early pickup is a free run through a full machine.
        Assert.True(!can.Collectible, "a can in the air cannot be taken");

        registry.Touch(Can, Player);
        Assert.True(!can.Collectible, "and walking into it while it falls does nothing");

        // Landed. The bounce sound and the arming are the same event, because
        // the noise is the only cue the player gets that there is now something
        // to collect.
        registry.Think(Can);
        Assert.True(can.Collectible, "a can that has landed can be taken");

        registry.Touch(Can, Player);
        Assert.True(!can.Collectible, "taking it consumes it");

        // The reason the taken flag exists at all: the item shell reports a
        // touch every tick a player stands in it, so a can that only checked
        // whether it had landed would be drunk sixty-six times a second.
        registry.Touch(Can, Player);
        Assert.True(!can.Collectible, "and standing in it afterwards takes nothing more");
    }

    [Fact]
    public void ASipIsCapped()
    {
        Assert.True(ItemSodacan.Sip(50) == 51, "a sip is worth a point");

        // A full player still drinks it — see ItemSodacan.OnTouch — so the
        // arithmetic has to be safe at the cap rather than never reached there.
        Assert.True(ItemSodacan.Sip(100) == 100, "a sip does not push a full player past a hundred");
        Assert.True(ItemSodacan.Sip(150) == 150, "nor one already over it");
    }

    //-------------------------------------------------------------------------
    // target_changegravity.
    //-------------------------------------------------------------------------
    [Fact]
    public void GravityScales()
    {
        Assert.True(TargetChangeGravity.ScaleFor(0.35f) == 0.35f, "a scale the map wrote is applied as written");

        Assert.True(TargetChangeGravity.ScaleFor(2f) == 2f, "and so is one heavier than normal");

        // The one that does not mean what it says. Source reads a per-entity
        // scale of zero as "nothing was set here", so a map that wrote zero
        // meaning weightlessness has always got ordinary falling instead.
        Assert.True(TargetChangeGravity.ScaleFor(0f) == 1f, "a written zero is normal gravity, not none");

        // Left alone deliberately: falling upwards is a thing a map may want,
        // and stock passes it straight through.
        Assert.True(TargetChangeGravity.ScaleFor(-1f) == -1f, "a negative scale is passed through");
    }

    [Fact]
    public void TheGravityInputsAreBound()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(TargetChangeGravity).Assembly);
        registry.Services = Ignoring("Host.IsInitialized", "Entities.IsValid", "Entities.GetHealth");

        var target = new EntityHandle(0x0033_0033u);
        registry.KeyValue(target, EntityNames.TargetChangegravity, "gravity", "0.25");
        registry.Spawn(target, EntityNames.TargetChangegravity);

        // Read back through the registry rather than the field, because what is
        // under test is that the map's key reaches it under the name stock uses.
        var written = registry.DescribeInstances()
            .Single(i => i.Handle == target.Value)
            .Vars.Single(v => string.Equals(v.Name, "gravity", StringComparison.OrdinalIgnoreCase)).Value;

        Assert.True(written == "0.25", "the map's gravity key lands on the entity");

        // Both names as a map fires them. The activator is passed along and not
        // asserted on: applying the scale is one engine call, and there is no
        // engine here to have made it.
        Assert.True(registry.Input(target, "ChangeGrav", "", 0f, Player), "ChangeGrav is an input");
        Assert.True(registry.Input(target, "ResetGrav", "", 0f, Player), "ResetGrav is an input");

        Assert.True(!registry.Input(target, "SetGrav", "", 0f, Player), "and nothing else is");
    }

    private static (ScriptedEntityRegistry, ItemSodacan) FreshCan()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(ItemSodacan).Assembly);
        registry.Services = Ignoring("Host.IsInitialized", "Entities.IsValid", "Entities.GetHealth");
        registry.Spawn(Can, EntityNames.ItemSodacan);

        return (registry, (ItemSodacan)registry.Get(Can)!);
    }
}
