using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Client.Ui;

// The TF2 scoreboard from fakes (the ui-screen loop's sample step): the
// states the +showscores screen is ever in, built through the same container
// the game uses. Every #TF2_Scoreboard_* token lives in
// game/mod_tf2/resource/tf2_english.txt.

public static class TFScoreboardSamples
{
    public static UiScreen Build(UiSampleServices s, FakeTFScoreboard? source = null,
                                 ITFScoreboardClasses? classes = null, FakeRoundTimer? timer = null,
                                 FakeTextureLibrary? textures = null, ITFScoreboardDominations? dominations = null,
                                 ITFScoreboardDetails? details = null)
    {
        s.Add<IScoreboardSource>(source ?? FakeTFScoreboard.Teams(6));
        s.Add<ITFScoreboardClasses>(classes ?? new FakeTFScoreboardClasses());
        s.Add<ITFScoreboardDominations>(dominations ?? new FakeTFScoreboardDominations());
        s.Add<ITFScoreboardDetails>(details ?? new FakeTFScoreboardDetails());
        s.Add<ITFRoundTimer>(timer ?? new FakeRoundTimer
        {
            Present = true, ShowTimeRemaining = true, SecondsLeft = 371,
            ServerTimeLimit = 1200, ServerTimeLeft = 371,
        });
        s.Add<IUiTextureLibrary>(textures ?? new FakeTextureLibrary());   // no art by default: the fallback draws
        return s.Create<TFScoreboard>();
    }

    /// <summary>The stock picture: twelve players, the server-time box
    /// between the bands.</summary>
    [UiSample("two teams and the clock")]
    private static UiScreen TwoTeams(UiSampleServices s) => Build(s);

    /// <summary>Stock's UpdateSpectatorList line at the lists' feet.</summary>
    [UiSample("two teams with spectators")]
    private static UiScreen WithSpectators(UiSampleServices s) => Build(s, FakeTFScoreboard.Teams(5, spectators: 3));

    /// <summary>No map time limit (UpdateServerTimeLeft's first arm): the
    /// box names it.</summary>
    [UiSample("no round timer")]
    private static UiScreen NoTimer(UiSampleServices s) => Build(s, timer: new FakeRoundTimer());

    /// <summary>A limit past the hour: the h:mm:ss count
    /// (tf_clientscoreboard.cpp:2098-2107's wide arm).</summary>
    [UiSample("server time in hours")]
    private static UiScreen HoursTimer(UiSampleServices s) =>
        Build(s, timer: new FakeRoundTimer { ServerTimeLimit = 7200, ServerTimeLeft = 4500 });

    /// <summary>The token pair's singular arm (TF_ScoreBoard_Player,
    /// tf_english.txt:59): one head per band.</summary>
    [UiSample("one player each")]
    private static UiScreen OneEach(UiSampleServices s) => Build(s, FakeTFScoreboard.Teams(1));

    /// <summary>A fresh server: the bands, no rows (stock's empty lists).</summary>
    [UiSample("empty teams")]
    private static UiScreen EmptyTeams(UiSampleServices s) => Build(s, FakeTFScoreboard.Teams(0));

    /// <summary>A spectator sees every row's class (the :1467 gate's arm).</summary>
    [UiSample("spectating")]
    private static UiScreen Spectating(UiSampleServices s) =>
        Build(s, FakeTFScoreboard.Teams(4, spectators: 1, localTeam: 1));

    /// <summary>The nemesis column with its art answered: index 2 and the
    /// dead index 4 dominate the local player (the nemesis pair), index 9
    /// and the dead index 12 are dominated — and the count column draws
    /// beside it (tf_clientscoreboard.cpp:1507-1519).</summary>
    [UiSample("nemesis pairs with art")]
    private static UiScreen NemesisPairs(UiSampleServices s) =>
        Build(s, textures: new FakeTextureLibrary(
            "hud/leaderboard_nemesis", "hud/leaderboard_nemesis_d",
            "hud/leaderboard_dominated", "hud/leaderboard_dominated_d",
            "hud/leaderboard_dom1", "hud/leaderboard_dom2", "hud/leaderboard_dom3",
            "hud/leaderboard_dom16", "hud/leaderboard_dom16_d"));
}

/// <summary>
/// Fixed scoreboard data in TF2's shape: BLU and RED (the numbers of
/// tf_shareddefs.h), the first RED row the local player, one bot, some dead,
/// and spectators on team 1.
/// </summary>
public sealed class FakeTFScoreboard : IScoreboardSource
{
    private readonly ScoreboardTeam[] _teams;
    private readonly ScoreboardPlayer[] _players;

    public FakeTFScoreboard(string server, string map, ScoreboardTeam[] teams, ScoreboardPlayer[] players)
    {
        ServerName = server;
        MapName = map;
        MaxPlayers = 24;
        _teams = teams;
        _players = players;
    }

    public string ServerName { get; }
    public string MapName { get; }
    public int MaxPlayers { get; }
    public int TeamCount => _teams.Length;
    public ScoreboardTeam Team(int i) => _teams[i];
    public int PlayerCount => _players.Length;
    public ScoreboardPlayer Player(int i) => _players[i];

    private static readonly IReadOnlyList<string> Roster =
    [
        "xXx_Headcrab_Slayer_From_Ravenholm_xXx", "Gordon", "Alyx", "barney.calhoun", "Dr. Kleiner",
        "Odessa Cubbage", "Vortigaunt", "Father Grigori", "Mossman", "Eli", "Lamarr", "Colonel Cubbage",
        "Uriah", "Magnusson",
    ];

    /// <summary>
    /// BLU and RED of <paramref name="perTeam"/> rows each, the first RED row
    /// local, every fifth a bot, every fourth dead, spectators after them.
    /// </summary>
    public static FakeTFScoreboard Teams(int perTeam, int spectators = 0, int localTeam = 2,
                                         string server = "SourceSharp TF2 | ctf_2fort 24/7", string map = "ctf_2fort")
    {
        var players = new List<ScoreboardPlayer>();
        int frags = 21;
        for (int t = 0; t < 2; t++)
        {
            int number = t == 0 ? 3 : 2;
            for (int k = 0; k < perTeam; k++)
            {
                int i = players.Count;
                bool bot = i % 5 == 4;
                players.Add(new ScoreboardPlayer(
                    Index: i + 1,
                    Name: bot ? $"BOT {Roster[i % Roster.Count]}" : Roster[i % Roster.Count],
                    Team: number,
                    Frags: frags,
                    Deaths: (i * 7) % 19,
                    Ping: bot ? 0 : 18 + ((i * 37) % 160),
                    Alive: i % 4 != 3,
                    Bot: bot,
                    Local: perTeam > 0 && number == localTeam && i % perTeam == 0));
                frags = frags > 3 ? frags - 3 : 0;
            }
        }
        for (int s = 0; s < spectators; s++)
        {
            int i = players.Count;
            players.Add(new ScoreboardPlayer(
                Index: i + 1, Name: Roster[(i + 2) % Roster.Count], Team: 1,
                Frags: 0, Deaths: 0, Ping: 40 + i * 11, Alive: true, Bot: false, Local: localTeam == 1 && s == 0));
        }

        // Deterministic sums, spelled out: BLU 21+15+9+3, RED 18+12+6.
        var teams = new[]
        {
            new ScoreboardTeam(3, "BLU", perTeam > 0 ? 21 + 15 + 9 + 3 : 0),
            new ScoreboardTeam(2, "RED", perTeam > 0 ? 18 + 12 + 6 : 0),
        };
        return new FakeTFScoreboard(server, map, teams, [.. players]);
    }
}

/// <summary>The class column's data: a name per row index, rotating over
/// the nine classes.</summary>
public sealed class FakeTFScoreboardClasses : ITFScoreboardClasses
{
    private static readonly IReadOnlyList<string> Classes =
        ["Scout", "Soldier", "Pyro", "Demoman", "Heavy", "Engineer", "Medic", "Sniper", "Spy"];

    public string ClassNameOf(int playerIndex)
        => playerIndex >= 1 ? Classes[(playerIndex - 1) % Classes.Count] : "";
}

/// <summary>The domination column's data: index % 4 dominations (so one arm
/// of every four rows stands empty), except indices 6 and 8, which hold more
/// than the icon table's span — the rows that exercise the clamp, one alive
/// and one dead. The pairwise marks against the local player: index 2 and
/// the dead index 4 dominate them (the nemesis arms, alive and dead), index
/// 9 and the dead index 12 are dominated (the dominated arms).</summary>
public sealed class FakeTFScoreboardDominations : ITFScoreboardDominations
{
    public int DominationsOf(int playerIndex)
        => playerIndex is 6 or 8 ? 20 : playerIndex >= 1 ? playerIndex % 4 : 0;

    public bool DominatesLocal(int playerIndex) => playerIndex is 2 or 4;

    public bool DominatedByLocal(int playerIndex) => playerIndex is 9 or 12;
}

/// <summary>The details grid's data: a count per row derived from the player
/// index, so every cell reads and the green arms show (the grid's counts are
/// the round's, one local row shown).</summary>
public sealed class FakeTFScoreboardDetails : ITFScoreboardDetails
{
    public TFScoreboardPlayerDetails Of(int playerIndex)
        => playerIndex >= 1
            ? new TFScoreboardPlayerDetails(
                Kills: 21, Assists: 3 + playerIndex % 5, Destruction: playerIndex % 3, Captures: playerIndex % 2,
                Defenses: playerIndex % 4, Dominations: playerIndex % 6, Revenge: playerIndex % 3, Healing: 3400,
                Invulns: playerIndex % 2, Teleports: playerIndex % 7, Headshots: playerIndex % 5,
                Backstabs: playerIndex % 4, Bonus: playerIndex % 3,
                Support: 340 * (playerIndex % 3) + 25 * (playerIndex % 2), Damage: 3400 + 137 * playerIndex)
            : default;
}
