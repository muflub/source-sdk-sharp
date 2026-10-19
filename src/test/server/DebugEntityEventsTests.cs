using SourceSharp.Abi;
using SourceSharp.Server;
using Debug.Api.Server.Interfaces;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>The server's IDebugEntityEvents (G4): one handler per subscription over its registry's OutputFired.</summary>
public class DebugEntityEventsTests
{
    private static readonly EntityHandle Source = new(0x0004_0021u);

    private static ReloadFixtureEntity Entity()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(DebugEntityEventsTests).Assembly);
        registry.Services = Services();
        registry.KeyValue(Source, "reload_fixture", "speed", "1");
        return (ReloadFixtureEntity)registry.Get(Source)!;
    }

    private static DebugEntityEvents Events(ReloadFixtureEntity entity)
        => new(ScriptedEntityRegistry.UnattachedEngine().Require<IEngineEntities>(), entity.Owner!);

    [Fact]
    public void A_subscriber_is_told_each_output_with_its_entity()
    {
        var entity = Entity();
        var seen = new List<DebugOutputFired>();

        using (Events(entity).Subscribe(seen.Add))
            entity.OnFirst.Fire("v");

        Assert.Equal([new DebugOutputFired(Source.Value, -1, "reload_fixture", "OnFirst", "v")], seen);
    }

    [Fact]
    public void Ending_one_subscription_leaves_the_other()
    {
        var entity = Entity();
        var events = Events(entity);
        var a = new List<string>();
        var b = new List<string>();

        var subA = events.Subscribe(f => a.Add(f.Value ?? ""));
        using var subB = events.Subscribe(f => b.Add(f.Value ?? ""));

        entity.OnFirst.Fire("1");
        subA.Dispose();
        subA.Dispose();
        entity.OnFirst.Fire("2");

        Assert.Equal(["1"], a);
        Assert.Equal(["1", "2"], b);
    }

    /// <summary>
    /// Statics to zero: a firing reaches the dev API of ITS registry only. Two
    /// game instances in one process, each with a subscriber: an output of the
    /// first is told to the first and never to the second.
    /// </summary>
    [Fact]
    public void A_firing_is_told_only_to_its_own_registrys_subscribers()
    {
        var mine = Entity();
        var other = Entity();
        var seenMine = new List<string>();
        var seenOther = new List<string>();

        using var a = Events(mine).Subscribe(f => seenMine.Add(f.Value ?? ""));
        using var b = Events(other).Subscribe(f => seenOther.Add(f.Value ?? ""));

        mine.OnFirst.Fire("m");

        Assert.Equal(["m"], seenMine);   // the stimulus reached its own
        Assert.Empty(seenOther);
    }
}
