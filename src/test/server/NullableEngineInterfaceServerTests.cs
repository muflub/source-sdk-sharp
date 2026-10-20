using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's product assemblies hold no nullable engine interface outside
/// the acquisition layer (<see cref="NullableEngineInterfaceCensus"/>).
/// </summary>
public class NullableEngineInterfaceServerTests
{
    [Fact]
    public void TheServerHoldsTakesAndAnswersNoNullableEngineInterface()
    {
        var (nullable, mentions) = NullableEngineInterfaceCensus.Scan(NullableEngineInterfaceCensus.TypesOf(
            typeof(ServerModule).Assembly, typeof(EngineServer).Assembly));

        Assert.True(mentions > 30, $"the scan saw {mentions} members naming an engine interface");
        Assert.True(nullable.Count == 0, $"{nullable.Count}:\n" + string.Join("\n", nullable));
    }

    // The positive arm: the census sees a nullable engine interface at the
    // depths it walks, and passes the non-nullable spelling of each.
    private sealed class Planted(Func<IFwdEngineServer?> later, IFwdEngineServer now)
    {
        public IFwdEngineServer? Held = null;
        public IFwdEngineServer Kept = now;
        public Func<IFwdEngineServer?> Later => later;
        public ISaveMemory? Memory() => null;
        public void Take(IUniformRandom? random) { }
    }

    [Fact]
    public void TheCensusFindsEachPlantedNullableAndNoneOfTheNonNullable()
    {
        var (nullable, mentions) = NullableEngineInterfaceCensus.Scan([typeof(Planted)]);

        string owner = typeof(Planted).FullName!;
        Assert.Equal(["..ctor(later)", ".<later>P (field)", ".Held (field)", ".Later (property)", ".Memory (return)",
                      ".Take(random)", ".get_Later (return)"],
                     nullable.Select(n => n.Replace(owner, "")).Order(StringComparer.Ordinal));
        Assert.True(mentions > nullable.Count, "the non-nullable members were seen and passed");
    }
}
