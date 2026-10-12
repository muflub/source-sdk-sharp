using SourceSharp.Abi;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// /entity/input's activator (plan_host_sdk §11, hosting P7): the player
/// slot an input is fired as. A malformed one is refused before anything
/// reaches the game thread.
/// </summary>
public class FireInputActivatorTests
{
    private static HttpReply Reply(string path)
    {
        using var server = DebugApiServer.ForTest(TestDebugHooks.Server());
        HttpSocketServer.TryParse($"GET {path} HTTP/1.1\r\n\r\n", out var request);
        return server.Route(request);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("one")]
    public void An_activator_that_is_not_a_player_slot_is_refused(string activator)
    {
        string body = Reply($"/entity/input?target=transition&input=Transition&activator={activator}").Body;

        Assert.Contains("activator is a player slot", body);
    }
}
