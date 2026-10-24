using SourceSharp.Abi;
using SourceSharp.Client;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's product assemblies take their hooks by constructor and never
/// hold, take or look up a null one (<see cref="HooksCensus"/>; the
/// census's own positive arm is the server's).
/// </summary>
public class HooksCensusClientTests
{
    private static readonly System.Reflection.Assembly[] Product =
        [typeof(ClientModule).Assembly, typeof(EngineClient).Assembly, typeof(SourceSharp.Client.Vgui.UiComposition).Assembly];

    [Fact]
    public void TheClientHoldsTakesAndAnswersNoNullableHooks()
    {
        var (nullable, mentions) = HooksCensus.Nullable(NullableEngineInterfaceCensus.TypesOf(Product));

        Assert.True(mentions > 2, $"the scan saw {mentions} members naming IHooks");
        Assert.True(nullable.Count == 0, $"{nullable.Count}:\n" + string.Join("\n", nullable));
    }

    [Fact]
    public void TheClientLooksUpNoHooksThroughANullableResolve()
    {
        string[] resolvers = HooksCensus.Resolvers(Product);

        Assert.True(resolvers.Length == 0, $"{resolvers.Length}:\n" + string.Join("\n", resolvers));
    }
}
