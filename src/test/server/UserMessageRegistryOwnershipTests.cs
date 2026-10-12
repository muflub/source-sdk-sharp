using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// That the user message registry is SourceSharp's, and since
/// plan_side_isolation P2 a managed one: neither side compiles a native list.
///
/// <para>
/// <c>game/shared/usermessages.cpp</c> built its singleton from a FILE-STATIC
/// CONSTRUCTOR, which ran the haptics' registration before <c>DLLInit</c>;
/// Phase 5a step 3 replaced it with shell_usermessage_registry.cpp, and P2
/// moved the registry into C# (UserMessageRegistry) and evicted that file and
/// shell_usermessages.cpp. What each side compiles now is a file of
/// forwarders: shell_usermessage_send.cpp on the server,
/// shell_usermessages_client.cpp on the client. Those are the proofs of read;
/// asserted over the built SET, <see cref="CMakeGraph.CompiledSources"/> of
/// <c>server_sourcesharp</c> and <c>client_sourcesharp</c>.
/// </para>
/// </summary>
public class UserMessageRegistryOwnershipTests
{
    private const string Stock = "game/shared/usermessages.cpp";
    private const string ClientForwarders = "sourcesharp/game/client/shell_usermessages_client.cpp";
    private const string ServerForwarders = "sourcesharp/game/server/shell_usermessage_send.cpp";

    // The native registry, its send path and the client's dispatch, evicted.
    private static readonly string[] Evicted =
    [
        "shell_usermessage_registry.cpp",
        "shell_usermessages.cpp",
    ];

    private static IReadOnlySet<string> Client => CMakeGraph.CompiledSources(CMakeGraph.Client);
    private static IReadOnlySet<string> Server => CMakeGraph.CompiledSources(CMakeGraph.Server);

    [Fact]
    public void TheClientCompilesNoNativeUserMessageList()
    {
        var built = Client;

        // Proof of read: an absence over a list that was not read passes for the
        // wrong reason, so the same set must hold the forwarders.
        Assert.Contains(ClientForwarders, built);

        Assert.DoesNotContain(Stock, built);
        foreach (string file in Evicted)
            Assert.DoesNotContain(built, f => f.EndsWith("/" + file, StringComparison.Ordinal));
    }

    [Fact]
    public void TheServerCompilesNoNativeUserMessageList()
    {
        var built = Server;

        Assert.Contains(ServerForwarders, built);

        Assert.DoesNotContain(Stock, built);
        foreach (string file in Evicted)
            Assert.DoesNotContain(built, f => f.EndsWith("/" + file, StringComparison.Ordinal));
    }

    [Fact]
    public void NeitherSideStillCompilesAStockPerGameMessageList()
    {
        // hl2_usermessages.cpp and tf_usermessages.cpp are the two files that
        // define RegisterUserMessages() upstream, and both of them call
        // RegisterScriptMessages() and RegisterHapticMessages(). One of them
        // arriving would put a native registrar back beside C#'s.
        var client = Client;
        var server = Server;

        // Proof of read for the absences below.
        Assert.Contains(ClientForwarders, client);
        Assert.Contains(ServerForwarders, server);

        foreach (string list in new[] { "hl2_usermessages.cpp", "tf_usermessages.cpp", "sdk_usermessages.cpp" })
        {
            Assert.True(!client.Any(f => f.EndsWith("/" + list, StringComparison.OrdinalIgnoreCase)),
                $"the client compiles {list}, which defines a second RegisterUserMessages()");
            Assert.True(!server.Any(f => f.EndsWith("/" + list, StringComparison.OrdinalIgnoreCase)),
                $"the server compiles {list}, which defines a second RegisterUserMessages()");
        }
    }
}
