using SourceSharp.Abi;

using TF2.Server.Player;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The player's runtime attribute list (<c>CTFPlayer::AddCustomAttribute</c>
/// and friends): the value lands on the player manager's own list where the
/// hooks walk it, the name resolves through the schema, the expiry clock
/// drops it, and an unknown name is stock's silent early return.
/// </summary>
public sealed class TFPlayerAttributesTests
{
    private const string HookClass = "mult_crit_chance";

    private static readonly EntityHandle Player = new(0x0001_0000u | PlayerRig.Index);

    private (PlayerRig Rig, TFPlayerAttributes Attributes) RigWith(float now = 5f)
    {
        var rig = new PlayerRig();
        rig.Engine.Returns("Clock.Now", now);
        rig.Extra.Add(new TFPlayerAttributes(rig.Carriers, rig.Items, rig.Schema, rig.Engine));
        rig.Join();

        var manager = new AttributeContainer();
        rig.Items.SetManager(PlayerRig.Index, manager);
        var attributes = rig.Carriers.Part<TFPlayerAttributes>(PlayerRig.Index)!;
        return (rig, attributes);
    }

    private static AttributeHooks Hooks() => new();

    private static float Walked(PlayerRig rig, float value = 4f, string? hookClass = HookClass)
        => Hooks().Float(value, hookClass, rig.Items.ManagerOf(PlayerRig.Index));

    [Fact]
    public void AnAddedAttributeIsOnTheManagerTheHooksWalk()
    {
        var (rig, attributes) = RigWith();

        attributes.Add("crit mod disabled", 0.5f);

        Assert.Equal(2f, Walked(rig));
    }

    [Fact]
    public void AReaddReplacesTheValueAndKeepsOneEntry()
    {
        var (rig, attributes) = RigWith();
        var manager = (rig.Items.ManagerOf(PlayerRig.Index) as AttributeContainer)!;

        attributes.Add("crit mod disabled", 0.5f);
        attributes.Add("crit mod disabled", 0.25f);

        Assert.Equal(1, manager.Attributes.Length);
        Assert.Equal(0.25f * 4f, Walked(rig));
    }

    [Fact]
    public void AnUnknownNameIsStocksSilentEarlyReturn()
    {
        var (rig, attributes) = RigWith();
        var manager = (rig.Items.ManagerOf(PlayerRig.Index) as AttributeContainer)!;

        attributes.Add("no such attribute", 0.5f);

        Assert.Equal(0, manager.Attributes.Length);
        Assert.Equal(4f, Walked(rig));
    }

    [Fact]
    public void RemoveTakesTheEntryOffTheManager()
    {
        var (rig, attributes) = RigWith();
        var manager = (rig.Items.ManagerOf(PlayerRig.Index) as AttributeContainer)!;

        attributes.Add("crit mod disabled", 0.5f);
        attributes.Remove("crit mod disabled");

        Assert.Equal(0, manager.Attributes.Length);
        Assert.Equal(4f, Walked(rig));
    }

    [Fact]
    public void RemovingANameTheMapNeverHeldLeavesTheManagerAlone()
    {
        var (rig, attributes) = RigWith();
        var manager = (rig.Items.ManagerOf(PlayerRig.Index) as AttributeContainer)!;

        manager.Set(AttributeValue.OfFloat(rig.Schema.Attribute("crit mod disabled")!, 0.5f));
        attributes.Remove("crit mod disabled");

        Assert.Equal(1, manager.Attributes.Length);
    }

    [Fact]
    public void AExpiredDurationLeavesAtTheTickAfterItsTime()
    {
        var (rig, attributes) = RigWith(now: 5f);

        attributes.Add("crit mod disabled", 0.5f, duration: 3f);
        attributes.Tick(8f);
        Assert.Equal(2f, Walked(rig));   // 8 > 8 is false: stock's strict compare keeps it

        attributes.Tick(9f);
        Assert.Equal(4f, Walked(rig));
    }

    [Fact]
    public void APermanentDurationOutlivesAnyTick()
    {
        var (rig, attributes) = RigWith(now: 5f);

        attributes.Add("crit mod disabled", 0.5f);
        attributes.Tick(1_000f);

        Assert.Equal(2f, Walked(rig));
    }

    [Fact]
    public void TheExpiryWalksOneNamePerPassAsStocksRestartLoopDoes()
    {
        var (rig, attributes) = RigWith(now: 5f);

        attributes.Add("crit mod disabled", 0.5f, duration: 1f);
        attributes.Tick(7f);

        Assert.Equal(4f, Walked(rig));
    }
}
