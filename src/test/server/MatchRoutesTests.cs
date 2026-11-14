using System.Text.Json;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>/match</c>, what the web UI's Match card reads (plan_bots_teams_tf2 D.3):
/// the mode and its settings, the vote on now with its counts, the next map
/// (a voted one included) and the candidates.
/// </summary>
public class MatchRoutesTests
{
    private sealed class Console : IServerConsole
    {
        public string Execute(string command) => "";
        public void Quit(int exitCode) { }
    }

    private static (MatchRoutes Routes, EndOfMapVoteTests.Rig Rig, Intermission Intermission) Make()
    {
        var rig = new EndOfMapVoteTests.Rig();
        rig.Engine.ReturnsEachCall("Console.FindConVarValue", () => rig.Engine.Calls[^1].Args[0] is "mp_fraglimit" ? "20" : null);
        var intermission = new Intermission(new RecordingLog(), rig.ConVars.Declare<MpChatTimeConVar>(), rig.NextLevel, rig.ConVars.Declare<MapCycleFileConVar>(),
                                            new ScriptedEntityRegistry(TestEngines.Server()), rig.Engine, rig.Engine, rig.Engine, new Console(), rig.Files);
        intermission.LevelInit("a", 0f);
        var writes = new ModeSettingWrites(rig.Catalog, rig.Engine, new RecordingLog());
        var routes = new MatchRoutes(rig.Catalog, writes, rig.Office, rig.Engine, intermission, rig.NextLevel, rig.Candidates);
        return (routes, rig, intermission);
    }

    private static JsonElement Ask(MatchRoutes routes)
    {
        var reply = routes.Handle(MatchRoutes.Path, new Dictionary<string, string>());
        Assert.Equal(200, reply.Status);
        return JsonDocument.Parse(reply.Json).RootElement;
    }

    [Fact]
    public void TheRouteIsListed()
        => Assert.Equal(MatchRoutes.Path, Assert.Single(Make().Routes.Routes).Path);

    [Fact]
    public void ItNamesTheLevelTheModeAndItsSettingsWithTheirValues()
    {
        var match = Ask(Make().Routes);

        Assert.Equal("a", match.GetProperty("map").GetString());
        Assert.Equal(GameModes.NullModeId, match.GetProperty("mode").GetProperty("id").GetString());
        Assert.False(match.GetProperty("mode").GetProperty("teamplay").GetBoolean());
        var frags = match.GetProperty("settings").EnumerateArray().Single(s => s.GetProperty("key").GetString() == "mp_fraglimit");
        Assert.Equal("20", frags.GetProperty("value").GetString());
    }

    [Fact]
    public void WithNoVoteTheVoteIsNull()
        => Assert.Equal(JsonValueKind.Null, Ask(Make().Routes).GetProperty("vote").ValueKind);

    [Fact]
    public void TheVoteOnNowIsItsKindChoicesAndCounts()
    {
        var (routes, rig, intermission) = Make();
        rig.EndOfMap.OnIntermission("a", "b", 100f);
        rig.Vote.Cast(new EntityHandle(0x0080_0001u), 2, 101f);

        var vote = Ask(routes).GetProperty("vote");

        Assert.Equal(("NextMap", "Voting", 0), (vote.GetProperty("kind").GetString(), vote.GetProperty("phase").GetString(), vote.GetProperty("caller").GetInt32()));
        Assert.Equal(["b", "c", "d"], vote.GetProperty("options").EnumerateArray().Select(o => o.GetString()!));
        Assert.Equal([0, 1, 0], vote.GetProperty("counts").EnumerateArray().Select(c => c.GetInt32()));
        Assert.Equal(1, vote.GetProperty("voters").GetInt32());
    }

    [Fact]
    public void TheNextMapIsTheCyclesAndThenTheVotedOne()
    {
        var (routes, rig, _) = Make();
        Assert.Equal(("a", false), (Ask(routes).GetProperty("nextMap").GetString(), Ask(routes).GetProperty("nextLevelSet").GetBoolean()));

        rig.NextLevel.SetValue("c");

        var match = Ask(routes);
        Assert.Equal(("c", true), (match.GetProperty("nextMap").GetString(), match.GetProperty("nextLevelSet").GetBoolean()));
    }

    [Fact]
    public void TheCandidatesAreListedWithTheirNavState()
    {
        var candidates = Ask(Make().Routes).GetProperty("candidates").EnumerateArray().ToList();

        Assert.Equal(["a", "b", "c", "d"], candidates.Select(c => c.GetProperty("map").GetString()!));
        Assert.All(candidates, c => Assert.False(c.GetProperty("hasNav").GetBoolean()));
    }
}
