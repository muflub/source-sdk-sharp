using TF2.Descent.Shared;
using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// A mod's messages take their keys' places in key order beside the core's
/// (plan_side_isolation P2b): the id is the key's ordinal position and the
/// SsUserMessages table carries it to each client, so a mod loaded beside the
/// core neither depends on nor is depended on by the assembly scan's order.
/// </summary>
public sealed class MessageOrderTests
{
    [Fact]
    public void With_TF2Descent_loaded_every_id_is_its_keys_place_in_key_order()
    {
        // Loaded, so the scan sees it: what loading the tf2_descent mod does.
        Assert.NotNull(typeof(NoticeMessage).Assembly);

        // The SERVER's view, from the server's game assembly (which carries the
        // shared source since plan_side_isolation P7). This process also loads
        // TF2.Descent.Client and SourceSharp.Client, whose copies of the same
        // shared source the game never loads beside the server's (each side has
        // its own load context); a server anchor's scan keeps to its side
        // (LoadedAssemblies.All), and the filter says so again here.
        var server = typeof(SourceSharp.Server.ServerModule).Assembly;
        var serverSide = LoadedAssemblies.Types(server, new RecordingLog())
            .Where(t => t.Assembly.GetName().Name != "TF2.Descent.Client");

        var registry = new UserMessageRegistry(new RecordingLog(), receives: false);
        registry.Register(serverSide, server);
        var names = registry.Messages.Select(m => m.Name).ToList();

        Assert.Contains("SourceSharp:Reconnect", names);
        Assert.Contains("TF2.Descent:notice", names);
        Assert.Contains("TF2.Descent:instance", names);
        Assert.Contains("TF2.Descent:next_level", names);
        Assert.Contains("TF2.Descent:reauth", names);
        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.Equal(Enumerable.Range(0, names.Count), registry.Messages.Select(m => m.Index));
    }
}
