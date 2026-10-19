using SourceSharp.Abi;
using Debug.Api.Client.Interfaces;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Golden for the client dev API's answers (plan_devapi_interfaces P3),
/// written before they moved behind Client.Interface: the status reply the
/// server proxies at /client/{slot}/status, and the stamp the hello carries.
/// </summary>
public class DebugApiClientGoldenTests
{
    internal static string StatusJson(BuildInfo build, Crossings crossings)
        => $"{{\"side\":\"client\",\"build\":{DebugApiJson.Build(build)},\"crossings\":{DebugApiJson.Crossings(crossings)}}}";

    [Fact]
    public void The_status_answer_is_the_clients_build_and_the_crossings()
    {
        // The client's own state (statics to zero): its build and counters,
        // handed to its hooks, as its composition hands them.
        var build = new BuildInfo();
        build.SetManaged(DebugApiGoldenBuildTests.KnownHalf, Array.Empty<string>());
        var crossings = new Crossings();
        crossings.Count(CrossingOp.ClientFrame);

        string expected = StatusJson(build, crossings);
        Assert.Contains("\"managed\":{\"known\":true,\"hash\":\"abc12345\"", expected, StringComparison.Ordinal);
        // P3: through the client's IDebugClientBuild.
        using var client = new DebugChannelClient("127.0.0.1",
            TestDebugHooks.Client(ring: new PredictionErrorRing(8), crossings: crossings, build: build));
        Assert.Equal(expected, client.Answer(DebugChannelQuery.Status));
        Assert.Equal("{\"error\":\"no such question\"}", client.Answer((DebugChannelQuery)99));
    }

    /// <summary>The recorded body, from src/test/managed/Golden/DebugApi (plan_client_novalve §4.4).</summary>
    /// <summary>
    /// The status counts the user messages received, so a live check can see a
    /// Damage message arrive; with none received the answer is unchanged.
    /// </summary>
    [Fact]
    public void The_status_answer_counts_the_user_messages_received()
    {
        var registry = new UserMessageRegistry(new RecordingLog());
        registry.RegisterAll(TestServices.ClientGame);
        int damage = registry.IdOf("Damage");
        Assert.True(damage >= 0, "Damage is registered");

        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(messages: registry));
        Assert.DoesNotContain("userMessages", client.Answer(DebugChannelQuery.Status), StringComparison.Ordinal);

        registry.Dispatch(damage, new byte[18]);
        registry.Dispatch(damage, new byte[18]);

        Assert.Contains(",\"userMessages\":{\"Damage\":2}}", client.Answer(DebugChannelQuery.Status), StringComparison.Ordinal);
    }

    /// <summary>The status counts the game events the client's registry dispatched (player_death's live check reads it).</summary>
    [Fact]
    public void The_status_answer_counts_the_game_events_received()
    {
        var events = new GameEventRegistry(TestServices.Fake(), new RecordingLog());
        events.Subscribe("player_death", _ => { });
        using var client = new DebugChannelClient("127.0.0.1", TestDebugHooks.Client(events: events));
        Assert.DoesNotContain("gameEvents", client.Answer(DebugChannelQuery.Status), StringComparison.Ordinal);

        events.Dispatch("player_death", IntPtr.Zero);
        events.Dispatch("player_hurt", IntPtr.Zero);   // claimed by nobody: not counted

        Assert.Contains(",\"gameEvents\":{\"player_death\":1}}", client.Answer(DebugChannelQuery.Status), StringComparison.Ordinal);
    }

    internal static string Golden(string name)
    {
        string path = RepoLayout.Repo("src", "test", "managed", "Golden", "DebugApi", $"client-{name}.json");
        Assert.True(File.Exists(path), $"{path} is missing; a golden that read nothing would pass");
        return File.ReadAllText(path).TrimEnd('\n');
    }

    /// <summary>A client channel over the given hooks, never dialled: Answer is called directly.</summary>
    internal static DebugChannelClient Channel(IDebugClientHooks hooks) => new("127.0.0.1", hooks);

    private sealed class FixedFrame(DebugClientFrame frame) : IDebugClientFrame
    {
        public DebugClientFrame Read() => frame;
    }

    [Fact]
    public void The_frame_answer_renders_exactly()
    {
        var frame = new DebugClientFrame(
            Frames: 1234, Commands: 567, Stage: "RenderEnd",
            Stages: [new DebugCount("Start", 1300), new DebugCount("RenderEnd", 1299)],
            WindowFrames: 60, FrameMsMin: 15.5, FrameMsAvg: 16.6666666, FrameMsMax: 33.25,
            Time: 42.5f, PoseKnown: true, Eye: new DebugClientVec3(1.5f, -2f, 64f), Pitch: 10f, Yaw: -90f);

        using var client = Channel(TestDebugHooks.Client(frame: new FixedFrame(frame)));
        Assert.Equal(Golden("frame"), client.Answer(DebugChannelQuery.Frame));
    }
}
