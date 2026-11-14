using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The end-of-map vote (plan_bots_teams_tf2 D.3, stock TF2's choices mode): the
/// server calls a choice of the next map when the round ends, the intermission
/// waits for it, and its winner is the map the level changes to.
/// </summary>
public class EndOfMapVoteTests
{
    private static readonly EntityHandle Ctrl = new(0x0071_0071u);

    private static EntityHandle Slot(int slot) => new((uint)(0x0080_0000 | slot));

    internal sealed class Level : ILevelInfo
    {
        public string? Map => "a";
        public bool IsReady => true;
    }

    internal sealed class Rig
    {
        public readonly VoteOffice Office = new();
        public readonly RecordingEngine Engine;
        public readonly ServerVotes Votes;
        public readonly EndOfMapVote EndOfMap;
        public readonly NextLevelConVar NextLevel;
        public readonly ConVarRegistry ConVars;
        public readonly GameModeCatalogTests.Bsps Files = new();
        public readonly GameModeCatalog Catalog;
        public readonly MapCandidates Candidates;

        public VoteController Vote => Office.Current!;

        /// <summary>Slots 1..<paramref name="humans"/> humans; the cycle a, b, c, d installed.</summary>
        public Rig(int humans = 1, string choicesMode = "1", string nextLevel = "", string cycle = "a\nb\nc\nd\n")
        {
            var convars = new Dictionary<string, string>
            {
                ["sv_vote_issue_nextlevel_choicesmode"] = choicesMode,
                ["nextlevel"] = nextLevel,
                ["mapcyclefile"] = "mapcycle.txt",
                ["sv_vote_command_delay"] = "2",
            };
            var registry = ConVars = new ConVarRegistry(new NamedConVars(convars));
            var services = Services(out Engine);
            Engine.Ignores("Entities.FireInput", "Host.RandomFloat", "Console.FindConVarValue", "Console.ExecuteServerCommand");
            Engine.ReturnsEachCall("Players.GetHandle", () => Engine.Calls[^1].Args[0] is int slot && slot <= humans ? Slot(slot) : EntityHandle.None);
            Engine.ReturnsEachCall("Entities.IsValid", () => Engine.Calls[^1].Args[0] is EntityHandle h && h != EntityHandle.None && h != Ctrl);
            Engine.Returns("Players.GetTeam", 0);
            services.AddInstance(new VoteConVars(registry.Declare<AllowVotesConVar>(), registry.Declare<VoteTimerDurationConVar>(),
                                                 registry.Declare<VoteCreationTimerConVar>(), registry.Declare<VoteHolderMayVoteNoConVar>(),
                                                 registry.Declare<VoteEarlyFinishConVar>(), registry.Declare<VoteQuorumRatioConVar>(),
                                                 registry.Declare<VoteCommandDelayConVar>(), registry.Declare<VoteAllowSpectatorsConVar>(),
                                                 registry.Declare<VoteMinVotersConVar>(), registry.Declare<VoteFailureTimerConVar>()));
            services.AddInstance<IVoteIndices>(new VoteIndices());
            services.AddInstance(Office);
            var entities = new ScriptedEntityRegistry(TestEngines.Server());
            entities.RegisterAll(typeof(VoteController).Assembly);
            entities.Services = services;
            entities.Spawn(Ctrl, EntityNames.VoteController);

            var files = Files;
            files.Files["cfg/mapcycle.txt"] = System.Text.Encoding.UTF8.GetBytes(cycle);
            foreach (string map in new[] { "a", "b", "c", "d" })
                files.Files[$"maps/{map}.bsp"] = GameModeCatalogTests.Bsp("info_player_deathmatch");
            var catalog = Catalog = new GameModeCatalog(new TeamDeathmatchTests.Rig().Modes, new BspEntityClasses(files), new Level());
            NextLevel = registry.Declare<NextLevelConVar>();
            var candidates = Candidates = new MapCandidates(files, registry.Declare<MapCycleFileConVar>(), catalog, registry.Declare<SvModeNextConVar>());
            Votes = new ServerVotes(Office, Engine, Engine, Engine, Engine, Engine, log: new RecordingLog());
            EndOfMap = new EndOfMapVote(Votes, Office, candidates, NextLevel, registry.Declare<VoteIssueNextLevelAllowedConVar>(),
                                        registry.Declare<VoteIssueNextLevelChoicesModeConVar>(), new RecordingLog());
        }

        public IEnumerable<string> ServerCommands
            => Engine.AllOf("Console.ExecuteServerCommand").Select(c => (string)c.Args[0]!);
    }

    [Fact]
    public void TheBallotLeavesOutTheMapPlaying()
        => Assert.Equal(["b", "c"], EndOfMapVote.Ballot(["a", "b", "c", "a"], "a", "b"));

    [Fact]
    public void TheBallotIsTheFirstFiveFromTheNextMapWrapped()
        => Assert.Equal(["c", "d", "e", "f", "g"], EndOfMapVote.Ballot(["a", "b", "c", "d", "e", "f", "g"], "a", "c"));

    [Fact]
    public void TheBallotWrapsToTheMapsBeforeTheNextOne()
        => Assert.Equal(["d", "b", "c"], EndOfMapVote.Ballot(["a", "b", "c", "d"], "a", "d"));

    [Fact]
    public void TheRoundsEndCallsAServerChoiceOfTheNextMap()
    {
        var rig = new Rig();

        rig.EndOfMap.OnIntermission("a", "b", 100f);

        Assert.True(rig.Vote.IsVoteActive);
        Assert.False(rig.Vote.IsYesNoVote);
        Assert.Equal(["b", "c", "d"], rig.Vote.Options);
        Assert.Equal(EntityHandle.None, rig.Vote.Holder);
        Assert.Equal((VoteKind.NextMap, 0, ""), (rig.Office.Kind, rig.Office.Caller, rig.Office.Detail));
        Assert.True(rig.EndOfMap.HoldsLevel(100f));
    }

    [Fact]
    public void ItsWinnerSetsTheNextLevelAndThenTheLevelIsLetGo()
    {
        var rig = new Rig();
        rig.EndOfMap.OnIntermission("a", "b", 100f);

        Assert.Equal(VoteCast.Ok, rig.Vote.Cast(Slot(1), 2, 101f));
        rig.Vote.Tick(118f);
        Assert.Equal(VoteOutcome.Passed, rig.Vote.Outcome);
        Assert.True(rig.EndOfMap.HoldsLevel(118f));
        rig.Vote.Tick(121f);

        Assert.Equal(["nextlevel c"], rig.ServerCommands);
        Assert.False(rig.EndOfMap.HoldsLevel(121f));
    }

    [Fact]
    public void ATenthOfTheVotersDecidesIt()
    {
        var rig = new Rig(humans: 10);
        rig.EndOfMap.OnIntermission("a", "b", 100f);

        rig.Vote.Cast(Slot(3), 3, 101f);
        rig.Vote.Tick(118f);

        Assert.Equal(10, rig.Vote.PotentialVoters);
        Assert.Equal(VoteOutcome.Passed, rig.Vote.Outcome);
    }

    [Fact]
    public void NobodyVotingLeavesTheCycleToChoose()
    {
        var rig = new Rig();
        rig.EndOfMap.OnIntermission("a", "b", 100f);

        rig.Vote.Tick(118f);
        rig.Vote.Tick(125f);

        Assert.Equal(VoteOutcome.Failed, rig.Vote.Outcome);
        Assert.Empty(rig.ServerCommands);
        Assert.False(rig.EndOfMap.HoldsLevel(118f));
    }

    [Fact]
    public void NoVoteWhileTheNextLevelIsSet()
    {
        var rig = new Rig(nextLevel: "d");

        rig.EndOfMap.OnIntermission("a", "d", 100f);

        Assert.False(rig.Vote.IsVoteActive);
        Assert.False(rig.EndOfMap.HoldsLevel(100f));
    }

    [Fact]
    public void NoVoteWithChoicesModeOff()
    {
        var rig = new Rig(choicesMode: "0");

        rig.EndOfMap.OnIntermission("a", "b", 100f);

        Assert.False(rig.Vote.IsVoteActive);
    }

    [Fact]
    public void NoVoteWithFewerThanTwoMapsToChoose()
    {
        var rig = new Rig(cycle: "a\nb\n");

        rig.EndOfMap.OnIntermission("a", "b", 100f);

        Assert.False(rig.Vote.IsVoteActive);
    }

    [Fact]
    public void TheServerCallsNoVoteOverOneRunning()
    {
        var rig = new Rig();
        rig.Vote.StartVote(Slot(1), new VoteIssue("Restart", "", "mp_restartgame 1"), 100f);

        Assert.Equal("a vote is in progress", rig.Votes.CallAsServer(NextLevelVoteIssue.Choice(["b", "c"])));
        rig.EndOfMap.OnIntermission("a", "b", 100f);

        Assert.Equal("Restart", rig.Vote.ActiveIssue!.Value.Name);
        Assert.False(rig.EndOfMap.HoldsLevel(100f));
    }

    [Fact]
    public void AnAdminsCancelEndsTheVoteWithNoResult()
    {
        var rig = new Rig();
        rig.EndOfMap.OnIntermission("a", "b", 100f);
        rig.Vote.Cast(Slot(1), 1, 101f);

        Assert.True(rig.Votes.Running);
        Assert.Null(rig.Votes.Cancel());
        rig.Vote.Tick(130f);

        Assert.False(rig.Vote.IsVoteActive);
        Assert.Empty(rig.ServerCommands);
        Assert.False(rig.EndOfMap.HoldsLevel(130f));
        Assert.Equal("no vote is running", rig.Votes.Cancel());
    }

    [Fact]
    public void TheServersCallEarnsNoCooldown()
    {
        var rig = new Rig();
        rig.Vote.StartVote(EntityHandle.None, new VoteIssue("NextMap", "nextmap", "", Options: ["a", "b"]), 100f);

        Assert.True(rig.Vote.CanCallVote(EntityHandle.None, 101f, out float wait));
        Assert.Equal(0f, wait);
    }

    [Fact]
    public void AnIssuesOwnQuorumJudgesItsVote()
    {
        var withOwn = new Rig(humans: 10);
        withOwn.Vote.StartVote(Slot(1), new VoteIssue("Restart", "", "mp_restartgame 1", QuorumRatio: 0.1f), 100f);
        withOwn.Vote.Tick(118f);
        var withServers = new Rig(humans: 10);
        withServers.Vote.StartVote(Slot(1), new VoteIssue("Restart", "", "mp_restartgame 1"), 100f);
        withServers.Vote.Tick(118f);

        Assert.Equal(VoteOutcome.Passed, withOwn.Vote.Outcome);
        Assert.Equal(VoteOutcome.Failed, withServers.Vote.Outcome);
    }
}
