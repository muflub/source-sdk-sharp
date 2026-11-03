using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The engine's string tables are a service of the composition (the game's
/// seams read the same tables the message of the day writes): a server whose
/// container cannot be acquired still has a directory, one that answers no
/// tables.
/// </summary>
public sealed class StringTableServiceTests
{
    [Fact]
    public void A_composition_has_a_directory_even_without_a_container()
    {
        var engine = TestSides.Create(Side.Server);
        var module = new ServerModule(new ServerModuleEngine(engine));
        module.ComposeEngineAreas();

        var tables = module.Services.Require<INetworkStringTableDirectory>();

        Assert.Null(tables.Find("Materials"));
    }
}
