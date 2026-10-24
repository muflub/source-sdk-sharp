using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The server's product assemblies take their hooks by constructor and never
/// hold, take or look up a null one (<see cref="HooksCensus"/>).
/// </summary>
public class HooksCensusServerTests
{
    private static readonly System.Reflection.Assembly[] Product =
        [typeof(ServerModule).Assembly, typeof(EngineServer).Assembly];

    [Fact]
    public void TheServerHoldsTakesAndAnswersNoNullableHooks()
    {
        var (nullable, mentions) = HooksCensus.Nullable(NullableEngineInterfaceCensus.TypesOf(Product));

        Assert.True(mentions > 10, $"the scan saw {mentions} members naming IHooks");
        Assert.True(nullable.Count == 0, $"{nullable.Count}:\n" + string.Join("\n", nullable));
    }

    [Fact]
    public void TheServerLooksUpNoHooksThroughANullableResolve()
    {
        string[] resolvers = HooksCensus.Resolvers(Product);

        Assert.True(resolvers.Length == 0, $"{resolvers.Length}:\n" + string.Join("\n", resolvers));
    }

    // The positive arm: the census sees a nullable IHooks at the depths it
    // walks and a Resolve of one in IL, and passes the non-nullable spelling
    // and a Require.
    private interface IPlantedHook;

    private sealed class Planted(IHooks<IPlantedHook>? later, IHooks<IPlantedHook> now, IServiceLocator services)
    {
        public IHooks<IPlantedHook>? Held = null;
        public IHooks<IPlantedHook> Kept = now;
        public IHooks<IPlantedHook>? Later => later;
        public bool Looked() => services.Resolve<IHooks<IPlantedHook>>() is not null;
        public IHooks<IPlantedHook> Required() => services.Require<IHooks<IPlantedHook>>();
    }

    [Fact]
    public void TheCensusFindsEachPlantedNullableAndLookUp()
    {
        var (nullable, mentions) = HooksCensus.Nullable([typeof(Planted)]);

        string owner = typeof(Planted).FullName!;
        Assert.Equal(["..ctor(later)", ".<later>P (field)", ".Held (field)", ".Later (property)", ".get_Later (return)"],
                     nullable.Select(n => n.Replace(owner, "")).Order(StringComparer.Ordinal));
        Assert.True(mentions > nullable.Count, "the non-nullable members were seen and passed");

        Assert.Contains(typeof(HooksCensusServerTests).FullName!,
                        HooksCensus.Resolvers(typeof(HooksCensusServerTests).Assembly));
    }
}
