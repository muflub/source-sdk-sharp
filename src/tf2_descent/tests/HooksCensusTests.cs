extern alias client;
using SourceSharp.Tests;
using TF2.Descent.Rules;
using TF2.Descent.Server;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// The mod's assemblies follow the core's hooks rule (<see cref="HooksCensus"/>):
/// each service and message takes the hooks it dispatches to by
/// constructor, as the side's registrar registered them, and never holds,
/// takes or looks up a null one. The census's positive arm is the core's
/// (<c>HooksCensusServerTests</c>).
/// </summary>
public class HooksCensusTests
{
    private static readonly System.Reflection.Assembly[] Product =
    [
        typeof(HostLink).Assembly,
        typeof(client::TF2.Descent.Client.TF2DescentClientRegistrar).Assembly,
        typeof(TF2DescentRules).Assembly,
    ];

    [Fact]
    public void TheModHoldsTakesAndAnswersNoNullableHooks()
    {
        var (nullable, mentions) = HooksCensus.Nullable(NullableEngineInterfaceCensus.TypesOf(Product));

        Assert.True(mentions > 10, $"the scan saw {mentions} members naming IHooks");
        Assert.True(nullable.Count == 0, $"{nullable.Count}:\n" + string.Join("\n", nullable));
    }

    [Fact]
    public void TheModLooksUpNoHooksThroughANullableResolve()
    {
        string[] resolvers = HooksCensus.Resolvers(Product);

        Assert.True(resolvers.Length == 0, $"{resolvers.Length}:\n" + string.Join("\n", resolvers));
    }
}
