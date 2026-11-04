extern alias client;

using System.Linq;
using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A class takes the convars it reads by constructor (user ruling 2026-11-03):
/// the registry answers no generic lookup, and nothing but the composition and
/// the two listed walks reads its whole list, which is how a lookup would come
/// back under another name. It was a scan on every call, and hid what each
/// class depended on.
/// </summary>
public class ConVarLookupCensusTests
{
    // Each walks every declaration for what no class can name: a console name
    // (MovementTuning.Find, for a map's tuning) and a tag (ServerTags).
    private static readonly string[] Waived =
    [
        "SourceSharp.Server.MovementTuning", "SourceSharp.Server.ServerTags",
    ];

    private static readonly string[] Roots =
    [
        "SourceSharp.Client.ClientModule", "SourceSharp.Server.ServerModule",
    ];

    private static string[] GenericLookups(Type registry)
        => registry.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                               | BindingFlags.Static | BindingFlags.DeclaredOnly)
                   .Where(m => m.IsGenericMethodDefinition
                               && m.GetGenericArguments()
                                   .Any(a => a.GetGenericParameterConstraints().Any(c => c.Name == nameof(ConVarBase))))
                   .Select(m => m.Name)
                   .ToArray();

    [Fact]
    public void TheServersRegistryHasNoGenericLookup()
        => Assert.Empty(GenericLookups(typeof(ConVarRegistry)));

    [Fact]
    public void TheClientsRegistryHasNoGenericLookup()
        => Assert.Empty(GenericLookups(typeof(client::SourceSharp.Abi.ConVarRegistry)));

    [Fact]
    public void OnlyTheRootsAndTheWaivedWalksReadTheRegistrysList()
    {
        string[] callers = IlCallCensus.Callers(
            m => m.DeclaringType?.FullName == "SourceSharp.Abi.ConVarRegistry" && m.Name == "get_All",
            typeof(ServerModule).Assembly, typeof(client::SourceSharp.Client.ClientModule).Assembly,
            typeof(ServerGameDll).Assembly, typeof(SourceSharp.Ai.Hl2mp.Senses.Hl2mpAiSession).Assembly);

        Assert.Equal(Roots.Concat(Waived).Order(StringComparer.Ordinal), callers);
    }
}
