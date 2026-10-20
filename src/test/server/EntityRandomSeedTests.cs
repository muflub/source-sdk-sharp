using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Entity-owned random streams are seeded explicitly and differently per
/// instance, from the side's <see cref="IRandomSeeds"/>.
///
/// <para>
/// logic_case, logic_timer and ambient_generic each drew from a static
/// <see cref="Random"/> shared by the whole process; then, briefly, from an
/// unseeded stream each. Neither is what was wanted: two cases built back to
/// back must never replay the same pattern, and that has to be a property of
/// how they are seeded rather than of what the unseeded constructor happens to
/// do. A fixed-seed source reproduces its seeds, which keeps tests
/// deterministic.
/// </para>
/// </summary>
public class EntityRandomSeedTests
{
    /// <summary>A seed source that counts the seeds it hands out.</summary>
    private sealed class CountingSeeds : IRandomSeeds
    {
        private readonly RandomSeeds _inner = new(7);

        public int Handed { get; private set; }

        public int Next()
        {
            Handed++;
            return _inner.Next();
        }
    }

    [Fact]
    public void ASeedSourceSeededTheSameWayTwiceReproducesItsSeeds()
    {
        var first = new RandomSeeds(20260929);
        var second = new RandomSeeds(20260929);

        int[] a = Enumerable.Range(0, 20).Select(_ => first.Next()).ToArray();
        int[] b = Enumerable.Range(0, 20).Select(_ => second.Next()).ToArray();

        Assert.Equal(a, b);
        Assert.True(a.Distinct().Count() > 1, "and the seeds it hands out are not all one seed");
    }

    [Fact]
    public void EachEntityDrawsOneSeedWhenItIsBuilt()
    {
        var seeds = new CountingSeeds();
        var engine = new RecordingEngine();

        _ = new LogicCase(seeds);
        Assert.Equal(1, seeds.Handed);

        _ = new LogicTimer(seeds);
        Assert.Equal(2, seeds.Handed);

        _ = new AmbientGeneric(engine, engine, seeds);
        Assert.Equal(3, seeds.Handed);
    }

    private static readonly EntityHandle First = new(0x0071_0071u);
    private static readonly EntityHandle Second = new(0x0072_0072u);

    private static LogicCase SixteenCases(ScriptedEntityRegistry registry, EntityHandle handle)
    {
        registry.Spawn(handle, EntityNames.LogicCase);

        for (int i = 1; i <= 16; i++)
            registry.KeyValue(handle, EntityNames.LogicCase, $"Case{i:00}", $"value{i}");

        return (LogicCase)registry.Get(handle)!;
    }

    /// <summary>Which case each of <paramref name="picks"/> PickRandom inputs fired.</summary>
    private static int[] Picks(ScriptedEntityRegistry registry, EntityHandle handle, LogicCase c, int picks)
    {
        Output[] outputs =
        {
            c.OnCase01, c.OnCase02, c.OnCase03, c.OnCase04, c.OnCase05, c.OnCase06, c.OnCase07, c.OnCase08,
            c.OnCase09, c.OnCase10, c.OnCase11, c.OnCase12, c.OnCase13, c.OnCase14, c.OnCase15, c.OnCase16,
        };

        var fired = new int[picks];

        for (int p = 0; p < picks; p++)
        {
            int[] before = outputs.Select(o => o.FireCount).ToArray();
            registry.Input(handle, "PickRandom", "", 0f, EntityHandle.None);
            fired[p] = Array.FindIndex(outputs, o => o.FireCount != before[Array.IndexOf(outputs, o)]);
        }

        return fired;
    }

    [Fact]
    public void TwoCasesBuiltBackToBackPickDifferently()
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(LogicCase).Assembly);
        registry.Services = Services();

        var first = SixteenCases(registry, First);
        var second = SixteenCases(registry, Second);

        int[] a = Picks(registry, First, first, 50);
        int[] b = Picks(registry, Second, second, 50);

        Assert.DoesNotContain(-1, a);
        Assert.DoesNotContain(-1, b);
        Assert.NotEqual(a, b);
    }
}
