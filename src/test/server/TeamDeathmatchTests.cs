using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// HL2DM's team deathmatch (plan_bots_teams_tf2 A.3 item 1): the <c>tdm</c>
/// mode's team score, stock <c>CHL2MP_Player::Event_Killed</c>'s arithmetic
/// (hl2mp_player.cpp:1323-1334), and the frag limit held against a team's
/// score (hl2mp_gamerules.cpp:323-331). The rules and the proxy are the real
/// ones, wired in the module's order, with the mode registered as the module
/// registers it.
/// </summary>
public class TeamDeathmatchTests
{
    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    /// <summary>
    /// The team service over the rig's own table: what the engine double's
    /// <c>Players.GetTeam</c> answers too, so a move is seen by every reader.
    /// </summary>
    internal sealed class TableTeams(Dictionary<EntityHandle, int> table) : ITeams, ITeamAssignment
    {
        public List<(EntityHandle Player, int Team)> Assigned { get; } = [];
        public int Team(EntityHandle player) => table.TryGetValue(player, out int t) ? t : -1;
        public IReadOnlyList<EntityHandle> Players(int team) => [.. table.Where(p => p.Value == team).Select(p => p.Key)];
        public int Count(int team) => Players(team).Count;
        public JoinTeamResult Join(EntityHandle player, int requested) => throw new NotSupportedException();
        public EntityHandle SelectSpawn(EntityHandle player) => EntityHandle.None;

        public void Assign(EntityHandle player, int team)
        {
            Assigned.Add((player, team));
            table[player] = team;
        }
    }

    internal sealed class Bots(params EntityHandle[] bots) : IBotPlayers
    {
        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal)
            => throw new NotSupportedException();
        public bool IsBot(EntityHandle player) => bots.Contains(player);
        public bool Kick(EntityHandle bot, string reason) => false;
        public IReadOnlyList<BotPlayer> Bots => [];
    }

    internal static readonly EntityHandle CombineA = new(0x0001_0001u);
    internal static readonly EntityHandle CombineB = new(0x0001_0002u);
    internal static readonly EntityHandle RebelA = new(0x0001_0003u);
    internal static readonly EntityHandle Fresh = new(0x0001_0004u);
    internal static readonly EntityHandle Bot = new(0x0001_0005u);

    /// <summary>
    /// The proxy, the rules and the mode over one engine double: userids 1-3
    /// are the three players, each on the team <see cref="Teams"/> says.
    /// </summary>
    internal sealed class Rig
    {
        public readonly ConVarRegistry ConVars = ConVarRegistry.For(UnattachedConsole());
        public readonly RecordingEngine Host = Fake();
        public readonly RecordingLog Log = new();
        public readonly Scoreboard Board;
        public readonly PlayerDeathEvent Deaths = new();
        public readonly GameRules Rules;
        public readonly ServiceContainer Services = TestServices.Services();
        public readonly GameModes Modes;
        public readonly TeamDeathmatchMode Mode;
        public readonly SpawnPoints Spawns;
        public readonly TableTeams TeamTable;
        public readonly Bots BotList = new(Bot);
        public readonly Dictionary<EntityHandle, int> Teams = new()
        {
            [CombineA] = TeamDeathmatchMode.TeamCombine,
            [CombineB] = TeamDeathmatchMode.TeamCombine,
            [RebelA] = TeamDeathmatchMode.TeamRebels,
        };

        public Rig()
        {
            ConVars.RegisterAll(typeof(GameRules).Assembly);
            Host.Returns("Host.ActiveSide", Side.Server)
                .Ignores("Players.GiveItem", "Players.GiveAmmo");
            Host.ReturnsEachCall("Players.FindByUserId", () => Host.Calls[^1].Args[0] switch
            {
                1 => CombineA,
                2 => CombineB,
                3 => RebelA,
                _ => EntityHandle.None,
            });
            Host.ReturnsEachCall("Players.GetTeam",
                () => Host.Calls[^1].Args[0] is EntityHandle h && Teams.TryGetValue(h, out int t) ? t : -1);

            Board = new Scoreboard(Host, new PlayerDisconnectEvent(), new RecordingLog());
            Services.AddInstance<IHooks<IGameModeSelector>>(new ListHooks<IGameModeSelector>());

            Modes = new GameModes(Log, Host, Services, ConVars.Declare<SvModeSwitchWarmupConVar>(),
                                  ConVars.Declare<MpWaitingForPlayersTimeConVar>());
            Rules = new GameRules(Log, ConVars.Holder<GameRulesConVars>(), Host, Host,
                                  Fake().Returns("Entities.IsValid", false), Board, Deaths, modes: Modes);
            Modes.Attach(Rules);
            Services.AddInstance(Modes);
            Spawns = new SpawnPoints(Host, Host, Host, Host, ConVars.Declared<Hl2mpSpawnFragFallbackRadiusConVar>(),
                                     new Random(1));
            TeamTable = new TableTeams(Teams);
            Mode = new TeamDeathmatchMode(Modes, Host, Spawns, TeamTable, TeamTable, Host,
                                          ConVars.Declared<TeamsUnbalanceLimitConVar>(), Host, BotList, new Random(1));
            Services.AddInstance(Mode);
        }

        public void SetTime(float now) => Host.Returns("Clock.Now", now).Returns("Host.CurrentTime", now);

        public void FragLimit(int n) => ConVars.Declared<MpFragLimitConVar>().Bind(-1, "mp_fraglimit", n.ToString());

        /// <summary>Switches to tdm, as <c>sv_mode tdm</c> does.</summary>
        public Rig Tdm()
        {
            SetTime(1f);
            Assert.True(Modes.SwitchTo(TeamDeathmatchMode.ModeId), "the registry knows 'tdm'");
            return this;
        }

        /// <summary>A kill as the engine's player_death reports it.</summary>
        public void Kill(int victim, int attacker) => Deaths.Raise(new PlayerDeathArgs(victim, attacker, "smg1", false));
    }

    //-------------------------------------------------------------------------
    // The mode.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheModeIsTdmAndPlaysWithTeams()
    {
        var rig = new Rig().Tdm();

        Assert.Equal("tdm", rig.Modes.ActiveId);
        Assert.True(rig.Modes.Active!.Teamplay, "team deathmatch is stock's IsTeamplay() true");
        Assert.NotSame(rig.Rules, rig.Modes.Active.Rounds);
    }

    //-------------------------------------------------------------------------
    // The team score (Event_Killed's AddScore).
    //-------------------------------------------------------------------------

    [Fact]
    public void AKillScoresAPointForTheAttackersTeam()
    {
        var rig = new Rig().Tdm();

        rig.Kill(victim: 3, attacker: 1);

        Assert.Equal((1, 0), (rig.Mode.Scoring!.TeamScore(2), rig.Mode.Scoring.TeamScore(3)));
    }

    [Fact]
    public void ASuicideCostsTheTeamAPoint()
    {
        var rig = new Rig().Tdm();

        rig.Kill(victim: 3, attacker: 3);

        Assert.Equal(-1, rig.Mode.Scoring!.TeamScore(3));
    }

    [Fact]
    public void ADeathToTheWorldScoresForNeitherTeam()
    {
        var rig = new Rig().Tdm();

        rig.Kill(victim: 3, attacker: 0);

        Assert.Equal((0, 0), (rig.Mode.Scoring!.TeamScore(2), rig.Mode.Scoring.TeamScore(3)));
    }

    /// <summary>
    /// Stock's own arithmetic, ported as stock (no divergence): the team gains
    /// a point for a teamkill (Event_Killed adds for any attacker that is not
    /// the victim) while the killer's points fall by one
    /// (<c>CTeamplayRules::IPointsForKill</c>, teamplay_gamerules.cpp:415-423).
    /// </summary>
    [Fact]
    public void ATeamkillScoresTheTeamAndCostsTheKillerAPoint()
    {
        var rig = new Rig().Tdm();

        rig.Kill(victim: 2, attacker: 1);

        Assert.Equal(1, rig.Mode.Scoring!.TeamScore(2));
        Assert.Equal(-1, rig.Board.ScoreFor(1));
    }

    [Fact]
    public void UnderTheCoresRulesATeamkillIsAnyOtherKill()
    {
        var rig = new Rig();   // the core's "null": no teamplay, no teammates

        rig.Kill(victim: 2, attacker: 1);

        Assert.Equal(1, rig.Board.ScoreFor(1));
    }

    [Fact]
    public void ATeamkillCostsTheKillerAFragOnTheScoreboardRow()
    {
        var rig = new Rig().Tdm();
        var entities = Fake().Ignores("Entities.GetEyeAngles").Returns("Entities.IsPlayer", true);
        var players = new Players(new RecordingLog(), entities,
                                  rig.Host.Ignores("Players.IsAlive", "Players.DropActiveWeapon"), Fake(), NoButtons);
        players.AttachModes(rig.Modes);

        players.OnKilled(CombineB, CombineA, now: 1f);
        players.OnKilled(CombineA, RebelA, now: 2f);

        Assert.Equal((-1, 1), (players[CombineA].Kills, players[RebelA].Kills));
    }

    [Fact]
    public void TheTeamsScoresGoBackToZeroWithTheRound()
    {
        var rig = new Rig().Tdm();
        rig.Kill(victim: 3, attacker: 1);

        Assert.True(rig.Modes.SwitchTo("tdm"));

        Assert.Equal(0, rig.Mode.Scoring!.TeamScore(2));
    }

    [Fact]
    public void TheSharedTeamsNeverScoreAndOthersAreNoTeam()
    {
        var scoring = new TeamDeathmatchScoring(Fake());

        Assert.Equal((0, 0, -1), (scoring.TeamScore(0), scoring.TeamScore(1), scoring.TeamScore(4)));
    }

    //-------------------------------------------------------------------------
    // The frag limit is a team's score.
    //-------------------------------------------------------------------------

    [Fact]
    public void APlayerAtTheFragLimitDoesNotEndATeamRound()
    {
        var rig = new Rig().Tdm();
        rig.FragLimit(3);
        rig.Board.Add(1, 5, 0, 5);   // one player well past the limit, on a team with nothing

        rig.SetTime(2f);
        rig.Rules.Think();

        Assert.False(rig.Rules.RoundOver, "under teamplay the frag limit is a team's score, never a player's");
    }

    [Fact]
    public void ATeamAtTheFragLimitEndsTheRound()
    {
        var rig = new Rig().Tdm();
        rig.FragLimit(2);
        rig.Kill(victim: 1, attacker: 3);
        rig.Kill(victim: 2, attacker: 3);

        rig.SetTime(2f);
        rig.Rules.Think();

        Assert.True(rig.Rules.RoundOver, "Rebels reached the frag limit of 2");
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("frag limit reached: team Rebels on 2 of 2"));
    }

    [Fact]
    public void UnderTheCoresRulesAPlayerAtTheFragLimitStillEndsTheRound()
    {
        var rig = new Rig();
        rig.FragLimit(3);
        rig.Board.Add(1, 3, 0, 3);

        rig.SetTime(2f);
        rig.Rules.Think();

        Assert.True(rig.Rules.RoundOver, "deathmatch's frag limit is unchanged: the best player's score");
    }

    //-------------------------------------------------------------------------
    // The players: set up on a team, back to Unassigned on the way out.
    //-------------------------------------------------------------------------

    [Fact]
    public void SwitchingToTdmPutsEveryUnassignedPlayerOnATeam()
    {
        var rig = new Rig();
        rig.Teams[Fresh] = 0;

        rig.Tdm();

        Assert.Contains(rig.TeamTable.Assigned, a => a.Player == Fresh && a.Team == TeamDeathmatchMode.TeamRebels);
        Assert.DoesNotContain(rig.TeamTable.Assigned, a => a.Player == CombineA);
    }

    [Fact]
    public void LeavingTdmForTheCoresRulesPutsBothTeamsBackOnUnassigned()
    {
        var rig = new Rig().Tdm();

        Assert.True(rig.Modes.SwitchTo(GameModes.NullModeId));

        Assert.Equal([0, 0, 0], new[] { CombineA, CombineB, RebelA }.Select(p => rig.Teams[p]));
    }

    [Fact]
    public void ARestartOfTdmMovesNobody()
    {
        var rig = new Rig().Tdm();
        rig.TeamTable.Assigned.Clear();

        Assert.True(rig.Modes.SwitchTo(TeamDeathmatchMode.ModeId));

        Assert.Empty(rig.TeamTable.Assigned);
    }

    /// <summary>Stock's Spawn order: PickDefaultSpawnTeam, then the spawn point for the new team.</summary>
    [Fact]
    public void AnUnassignedPlayerIsPutOnATeamAsTheySpawn()
    {
        var rig = new Rig().Tdm();
        rig.Teams[Fresh] = 0;
        rig.Host.Returns("Entities.FindAllByClassname", Array.Empty<EntityHandle>());

        rig.Mode.Spawns!.SelectSpawn(Fresh);

        Assert.Equal(TeamDeathmatchMode.TeamRebels, rig.Teams[Fresh]);
    }

    //-------------------------------------------------------------------------
    // The team menu on a first spawn, and the change-team key.
    //-------------------------------------------------------------------------

    private static List<VguiMenu> Menus(RecordingEngine engine)
        => [.. engine.AllOf("UserMessages.Send").Where(c => (string)c.Args[0]! == "VGUIMenu").Select(c =>
           {
               var reader = new UserMessageReader((byte[])c.Args[4]!);
               return VguiMenu.Read(ref reader);
           })];

    [Fact]
    public void AFirstSpawnUnderTdmShowsTheTeamMenuOnce()
    {
        var rig = new Rig().Tdm();

        rig.Mode.Rounds.OnPlayerSpawned(CombineA, 2f);
        rig.Mode.Rounds.OnPlayerSpawned(CombineA, 9f);

        var menu = Assert.Single(Menus(rig.Host));
        Assert.Equal(("team", true), (menu.Name, menu.Show));
        Assert.Equal(CombineA.EntryIndex, (int)rig.Host.AllOf("UserMessages.Send").Single().Args[2]!);
    }

    [Fact]
    public void ABotIsNeverShownTheTeamMenu()
    {
        var rig = new Rig().Tdm();

        rig.Mode.Rounds.OnPlayerSpawned(Bot, 2f);

        Assert.Empty(Menus(rig.Host));
    }

    [Fact]
    public void ANewRoundSetUpShowsTheMenuAgain()
    {
        var rig = new Rig().Tdm();
        rig.Mode.Rounds.OnPlayerSpawned(CombineA, 2f);

        Assert.True(rig.Modes.SwitchTo(TeamDeathmatchMode.ModeId));
        rig.Mode.Rounds.OnPlayerSpawned(CombineA, 3f);

        Assert.Equal(2, Menus(rig.Host).Count);
    }

    [Fact]
    public void ChangeteamUnderTdmShowsTheTeamMenu()
    {
        var rig = new Rig().Tdm();
        var command = new TeamMenuCommand(rig.Modes, rig.Host);

        Assert.True(command.Handle(RebelA, "changeteam", []));

        Assert.Equal("team", Assert.Single(Menus(rig.Host)).Name);
    }

    [Fact]
    public void ChangeteamUnderTheCoresRulesIsNotClaimed()
    {
        var rig = new Rig();
        var command = new TeamMenuCommand(rig.Modes, rig.Host);

        Assert.False(command.Handle(RebelA, "changeteam", []), "deathmatch has no teams: the word falls through");
        Assert.Empty(Menus(rig.Host));
    }

    //-------------------------------------------------------------------------
    // The pick at level init: mp_teamplay.
    //-------------------------------------------------------------------------

    private sealed class Selector(string? id) : IGameModeSelector
    {
        public string? PickMode() => id;
    }

    [Fact]
    public void MpTeamplayOnePicksTdmAtLevelInit()
    {
        var rig = new Rig();
        rig.ConVars.Declared<TeamPlayConVar>().Bind(-1, "mp_teamplay", "1");
        rig.Services.AddInstance(new TeamplayModeSelector(rig.ConVars.Declared<TeamPlayConVar>()));

        ((IGameLifecycle)rig.Modes).OnLevelInit("dm_lockdown");

        Assert.Equal("tdm", rig.Modes.ActiveId);
    }

    [Fact]
    public void MpTeamplayZeroLeavesTheCoresRules()
    {
        var rig = new Rig();
        rig.Services.AddInstance(new TeamplayModeSelector(rig.ConVars.Declared<TeamPlayConVar>()));

        ((IGameLifecycle)rig.Modes).OnLevelInit("dm_lockdown");

        Assert.Equal(GameModes.NullModeId, rig.Modes.ActiveId);
    }

    [Fact]
    public void AModsSelectorAnswersBeforeMpTeamplay()
    {
        var rig = new Rig();
        rig.ConVars.Declared<TeamPlayConVar>().Bind(-1, "mp_teamplay", "1");
        rig.Services.AddInstance(new TeamplayModeSelector(rig.ConVars.Declared<TeamPlayConVar>()));
        rig.Services.AddInstance<IHooks<IGameModeSelector>>(new ListHooks<IGameModeSelector>(new Selector("null")));

        ((IGameLifecycle)rig.Modes).OnLevelInit("dm_lockdown");

        Assert.Equal(GameModes.NullModeId, rig.Modes.ActiveId);
    }

    [Fact]
    public void ARestartZeroesTheTeamsScores()
    {
        var rig = new Rig().Tdm();
        rig.Kill(victim: 3, attacker: 1);

        rig.Rules.RestartGame();

        Assert.Equal(0, rig.Mode.Scoring!.TeamScore(2));
    }
}
