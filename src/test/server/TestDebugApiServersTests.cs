using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Two debug API servers started in one test process listen on two sockets
/// (TestDebugApiServers). From the public constructor both would bind
/// {dir}/{pid}.sock, and the second Start deleted the first one's socket or
/// lost the bind: "the test server could not listen" in parallel classes.
/// </summary>
public class TestDebugApiServersTests
{
    [Fact]
    public void Two_started_servers_in_one_process_listen_on_two_sockets()
    {
        using DebugApiServer a = new DebugApiServer(["-debugapisocket"], TestDebugHooks.Server(), TestDebugApiServers.OwnSocket());
        using DebugApiServer b = new DebugApiServer(["-debugapisocket"], TestDebugHooks.Server(), TestDebugApiServers.OwnSocket());
        Assert.True(a.Start(), "the first test server could not listen");
        Assert.True(b.Start(), "the second test server could not listen");

        Assert.NotEqual(a.Address, b.Address);
        Assert.True(File.Exists(a.Address), $"the first server's socket {a.Address} is gone");
        Assert.True(File.Exists(b.Address), $"the second server's socket {b.Address} is gone");
        Assert.True(a.Address!.Length < 108 && b.Address!.Length < 108, "a socket path is past sun_path's limit");
    }
}
