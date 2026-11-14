using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// A player's score survives a rejoin (<see cref="RejoinScores"/>, plan A.6,
/// ruled 2026-11-02): a leaving human's frags and deaths come back on their
/// next connect in the same match; the last 128 departures are kept, one per
/// person; a new match (map, mode switch, restart) clears them; bots are never
/// kept; a team's own total does not gain the restored frags.
/// </summary>
public class RejoinScoresTests
{
    private sealed class Clients : IConnectedClients
    {
        public readonly Dictionary<int, ulong> Steam = new();
        public readonly Dictionary<int, string> Names = new();
        public readonly Dictionary<int, int> UserIds = new();
        public float? AvgLatency(int entIndex) => null;
        public ulong SteamId64(int entIndex) => Steam.GetValueOrDefault(entIndex);
        public string? Address(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => name == "name" ? Names.GetValueOrDefault(entIndex) : null;
        public int UserId(int entIndex) => UserIds.GetValueOrDefault(entIndex, -1);
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) { }
        public void Reconnect(int entIndex) { }
    }

    /// <summary>A mod's own totals: one number per player index.</summary>
    private sealed class ModTotals : IRejoinScoreHook
    {
        public readonly Dictionary<int, int> Score = new();
        public object? Capture(EntityHandle player) => Score.TryGetValue(player.EntryIndex, out int s) ? s : null;
        public void Restore(EntityHandle player, object state) => Score[player.EntryIndex] = (int)state;
    }

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Rig
    {
        public readonly Clients Clients = new();
        public readonly RecordingEngine Engine = Fake();
        public readonly Players Roster;
        public readonly Scoreboard Board;
        public readonly RecordingLog Log = new();
        public readonly ModTotals Mod = new();
        public readonly RejoinScores Rejoin;
        /// <summary>Which player entity each slot holds now: a reconnect is a new entity in the slot.</summary>
        public readonly Dictionary<int, EntityHandle> Slots = new();
        private uint _serial = 1;

        public Rig(params EntityHandle[] bots) : this(null, bots) { }

        public Rig(Scoreboard? board, params EntityHandle[] bots)
        {
            Engine.Returns("Host.ActiveSide", Side.Server)
                  .ReturnsEachCall("Players.GetHandle",
                      () => Slots.TryGetValue((int)Engine.Calls[^1].Args[0]!, out var h) ? h : EntityHandle.None);
            Roster = new Players(new RecordingLog(), Fake(), Fake(), Fake(), NoButtons);
            Board = board ?? new Scoreboard(Engine, new PlayerDisconnectEvent(), new RecordingLog());
            Rejoin = new RejoinScores(Clients, Engine, Roster, Board, new TeamDeathmatchTests.BotRoster(bots), Log,
                                      new ListHooks<IRejoinScoreHook>(Mod));
        }

        /// <summary>A client in <paramref name="slot"/>: a fresh entity, its userid, its SteamID (0 for none) and name.</summary>
        public EntityHandle Connect(int slot, ulong steam, string name = "player", int? userId = null, bool active = true)
        {
            var handle = new EntityHandle((uint)slot | (_serial++ << 16));
            Slots[slot] = handle;
            Clients.Steam[slot] = steam;
            Clients.Names[slot] = name;
            Clients.UserIds[slot] = userId ?? (int)_serial + 100;
            if (active)
                ((IClientLifecycle)Rejoin).OnClientActive(slot);
            return handle;
        }

        public void Score(EntityHandle player, int frags, int deaths)
        {
            Roster[player].Kills = frags;
            Roster[player].Deaths = deaths;
        }

        public void Leave(int slot) => ((IClientLifecycle)Rejoin).OnClientDisconnected(slot);
    }

    [Fact]
    public void LeavingAndRejoiningRestoresTheScore()
    {
        var rig = new Rig();
        var first = rig.Connect(1, steam: 76561198000000001);
        rig.Score(first, frags: 7, deaths: 3);
        rig.Leave(1);

        var again = rig.Connect(4, steam: 76561198000000001, userId: 55);

        Assert.Equal((7, 3), (rig.Roster[again].Kills, rig.Roster[again].Deaths));
        Assert.Equal(7, rig.Board.ScoreFor(55));
        Assert.Equal(0, rig.Rejoin.Count);
    }

    [Fact]
    public void ANewPlayerGetsNothing()
    {
        var rig = new Rig();
        var first = rig.Connect(1, steam: 11);
        rig.Score(first, frags: 7, deaths: 3);
        rig.Leave(1);

        var stranger = rig.Connect(2, steam: 22);

        Assert.False(rig.Roster.Knows(stranger), "nobody's score was written onto a different person");
    }

    [Fact]
    public void The129thDepartureDropsTheOldest()
    {
        var rig = new Rig();
        for (ulong id = 1; id <= RejoinScores.Capacity + 1; id++)
        {
            var player = rig.Connect(1, steam: id);
            rig.Score(player, frags: (int)id, deaths: 0);
            rig.Leave(1);
        }

        Assert.Equal(RejoinScores.Capacity, rig.Rejoin.Count);
        Assert.False(rig.Roster.Knows(rig.Connect(2, steam: 1)), "the first to leave was dropped");
        Assert.Equal(2, rig.Roster[rig.Connect(3, steam: 2)].Kills);
    }

    [Fact]
    public void LeavingTwiceKeepsTheLatest()
    {
        var rig = new Rig();
        var a = rig.Connect(1, steam: 9);
        rig.Score(a, frags: 1, deaths: 1);
        rig.Leave(1);
        // Back, and gone again before they were ever in the game: two departures, one person.
        var b = rig.Connect(1, steam: 9, active: false);
        rig.Score(b, frags: 12, deaths: 4);
        rig.Leave(1);

        Assert.Equal(1, rig.Rejoin.Count);

        var c = rig.Connect(1, steam: 9);

        Assert.Equal((12, 4), (rig.Roster[c].Kills, rig.Roster[c].Deaths));
    }

    [Fact]
    public void ANewMapClearsTheTable()
    {
        var rig = new Rig();
        var a = rig.Connect(1, steam: 9);
        rig.Score(a, frags: 5, deaths: 0);
        rig.Leave(1);

        ((IGameLifecycle)rig.Rejoin).OnLevelInit("dm_lockdown");

        Assert.Equal(0, rig.Rejoin.Count);
        Assert.False(rig.Roster.Knows(rig.Connect(1, steam: 9)));
    }

    [Fact]
    public void ABotIsNeverKept()
    {
        var bot = new EntityHandle(1u | (1u << 16));
        var rig = new Rig(bot);
        rig.Connect(1, steam: 0, name: "Bot01");
        rig.Score(bot, frags: 5, deaths: 0);

        rig.Leave(1);

        Assert.Equal(0, rig.Rejoin.Count);
    }

    [Fact]
    public void AClientWithNoSteamIdIsKeyedByNameAndTheRestoreSaysSo()
    {
        var rig = new Rig();
        var a = rig.Connect(1, steam: 0, name: "lanplayer");
        rig.Score(a, frags: 3, deaths: 2);
        rig.Leave(1);

        var b = rig.Connect(2, steam: 0, name: "lanplayer");

        Assert.Equal(3, rig.Roster[b].Kills);
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("keyed by name 'lanplayer'"));
    }

    [Fact]
    public void AModsOwnTotalsComeBackThroughItsHook()
    {
        var rig = new Rig();
        var a = rig.Connect(1, steam: 9);
        rig.Score(a, frags: 1, deaths: 0);
        rig.Mod.Score[1] = 40;
        rig.Leave(1);
        rig.Mod.Score.Clear();   // the mod forgets its leaver, as TF2's stats do

        rig.Connect(6, steam: 9);

        Assert.Equal(40, rig.Mod.Score[6]);
    }

    [Fact]
    public void AModeSwitchClearsTheTable()
    {
        var tdm = new TeamDeathmatchTests.Rig();
        var rig = new Rig();
        tdm.Services.AddInstance(rig.Rejoin);
        var a = rig.Connect(1, steam: 9);
        rig.Score(a, frags: 5, deaths: 0);
        rig.Leave(1);

        tdm.Tdm();

        Assert.Equal(0, rig.Rejoin.Count);
    }

    [Fact]
    public void AMatchRestartClearsTheTable()
    {
        var rig = new Rig();
        var convars = ConVarRegistry.For(UnattachedConsole());
        convars.RegisterAll(typeof(GameRules).Assembly);
        var host = Fake().Returns("Host.ActiveSide", Side.Server).Returns("Host.CurrentTime", 1f);
        var rules = new GameRules(new RecordingLog(), convars.Holder<GameRulesConVars>(), host, host,
                                  Fake(), rig.Board, new PlayerDeathEvent(), rejoin: rig.Rejoin);
        var a = rig.Connect(1, steam: 9);
        rig.Score(a, frags: 5, deaths: 0);
        rig.Leave(1);

        rules.RestartGame();

        Assert.Equal(0, rig.Rejoin.Count);
    }

    /// <summary>
    /// Team deathmatch's team total is its own counter: the restored frags
    /// come back on the player's row and the scoreboard, never on the team.
    /// </summary>
    [Fact]
    public void ARestoreDoesNotChangeATeamTotal()
    {
        var tdm = new TeamDeathmatchTests.Rig().Tdm();
        tdm.Kill(victim: 3, attacker: 1);
        tdm.Kill(victim: 3, attacker: 1);
        int before = tdm.Mode.Scoring!.TeamScore(TeamDeathmatchMode.TeamCombine);
        var rig = new Rig(tdm.Board);
        var a = rig.Connect(1, steam: 9);
        rig.Score(a, frags: 2, deaths: 0);
        rig.Leave(1);

        var b = rig.Connect(1, steam: 9, userId: 77);

        Assert.Equal(2, rig.Roster[b].Kills);
        Assert.Equal(2, tdm.Board.ScoreFor(77));
        Assert.Equal(before, tdm.Mode.Scoring.TeamScore(TeamDeathmatchMode.TeamCombine));
        Assert.Equal(2, before);
    }
}
