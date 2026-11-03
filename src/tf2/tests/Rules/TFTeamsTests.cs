using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server;
using TF2.Server.Modes;
using TF2.Server.Rules;
using TF2.Tests.Buildings;
using TF2.Tests.Modes;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// TF2's teams (stage2 2.1): the auto pick (<c>GetAutoTeam</c>), the join
/// refusal (<c>WouldChangeUnbalanceTeams</c>), the forced balance
/// (<c>BalanceTeams</c>) and the <c>jointeam</c> names.
/// </summary>
public sealed class TFTeamsTests
{
    private sealed class FixedState(RoundState state) : ITeamRoundState
    {
        public RoundState State => state;
        public bool InWaitingForPlayers => false;
        public bool InSetup => false;
        public float MapResetTime => 0f;
        public float StalemateStartTime => -1f;
        public TFTeam WinningTeam => TFTeam.Unassigned;
    }

    /// <summary>The core's team roster as far as the jointeam word reaches it: the joins asked for.</summary>
    private sealed class JoinRecorder : ITeams
    {
        public readonly List<int> Joined = new();

        /// <summary>The player's team as the same-team return asks it (0 = unassigned).</summary>
        public int TeamAnswer;

        public int Team(EntityHandle player) => TeamAnswer;
        public IReadOnlyList<EntityHandle> Players(int team) => [];
        public int Count(int team) => 0;

        public JoinTeamResult Join(EntityHandle player, int requested)
        {
            Joined.Add(requested);
            return JoinTeamResult.Playing;
        }

        public EntityHandle SelectSpawn(EntityHandle player) => EntityHandle.None;
    }

    /// <summary>The panels these facts don't watch sink here.</summary>
    private sealed class PanelSink : IUserMessageSender
    {
        public void Send(string key, ReadOnlySpan<byte> payload, int player = 0, bool reliable = true)
        {
        }
    }

    /// <summary>The bots by handle; everyone else is a human.</summary>
    private sealed class KnownBots : IBotPlayers
    {
        public readonly HashSet<EntityHandle> Known = [];

        public bool IsBot(EntityHandle player) => Known.Contains(player);

        public EntityHandle Add(string name, IReadOnlyList<KeyValuePair<string, string>> userInfo, out string refusal)
            => throw new NotSupportedException();

        public bool Kick(EntityHandle bot, string reason) => throw new NotSupportedException();

        public IReadOnlyList<BotPlayer> Bots => [];
    }

    private static TFTeams Policy(RoundRig rig, RoundState state = RoundState.RoundRunning, ITFTeamRoles? roles = null)
        => new(rig.Balance, rig.Roster, rig.Slot, new FixedState(state), new RandomSeeds(7), rig.Settings,
               roles ?? new TFTeamRoles());

    private static void Fill(RoundRig rig, int red, int blue)
    {
        for (int i = 0; i < red; i++)
            rig.Roster.Add(TFTeam.Red);
        for (int i = 0; i < blue; i++)
            rig.Roster.Add(TFTeam.Blue);
    }

    /// <summary>The carrier's class answer with one engineer among soldiers.</summary>
    private sealed class OneEngineer : TF2.Server.Econ.ITFPlayerClasses
    {
        public EntityHandle Engineer;

        public TFClass ClassOf(EntityHandle player) => player == Engineer ? TFClass.Engineer : TFClass.Soldier;
    }

    // ---- CalculateTeamBalanceScore's class component ---------------------------

    [Fact]
    public void An_engineer_scores_120_below_a_same_tenure_player()
    {
        var classes = new OneEngineer();
        var rig = new RoundRig(classes: classes);
        Fill(rig, red: 2, blue: 1);
        var reds = rig.Roster.Players(TFTeam.Red);
        classes.Engineer = reds[0];
        rig.Balance.NoteSeen(reds[0], now: 10f);
        rig.Balance.NoteSeen(reds[1], now: 10f);

        Assert.Equal(rig.Balance.BalanceScore(reds[1]) - 120, rig.Balance.BalanceScore(reds[0]));
    }

    [Fact]
    public void The_balancer_leaves_the_engineer_for_another_candidate()
    {
        var classes = new OneEngineer();
        var rig = new RoundRig(classes: classes);
        Fill(rig, red: 3, blue: 1);
        var reds = rig.Roster.Players(TFTeam.Red);
        classes.Engineer = reds[0];
        foreach (var red in reds)
            rig.Balance.NoteSeen(red, now: 10f);

        rig.Balance.BalanceTeams(requireDead: false);

        Assert.Equal(reds[1], Assert.Single(rig.Roster.Moves).Player);
    }

    [Fact]
    public void A_non_engineer_is_not_discounted()
    {
        var classes = new OneEngineer();
        var rig = new RoundRig(classes: classes);
        Fill(rig, red: 2, blue: 1);
        var reds = rig.Roster.Players(TFTeam.Red);
        rig.Balance.NoteSeen(reds[0], now: 10f);
        rig.Balance.NoteSeen(reds[1], now: 10f);

        Assert.Equal(rig.Balance.BalanceScore(reds[1]), rig.Balance.BalanceScore(reds[0]));
    }

    // ---- GetAutoTeam ---------------------------------------------------------

    [Fact]
    public void Auto_join_picks_the_smaller_team()
    {
        var rig = new RoundRig();
        Fill(rig, red: 2, blue: 1);
        Assert.Equal(TFTeam.Blue, Policy(rig).AutoTeam());

        Fill(rig, red: 0, blue: 2);   // now 2 v 3
        Assert.Equal(TFTeam.Red, Policy(rig).AutoTeam());
    }

    [Fact]
    public void Auto_join_on_even_teams_sends_the_player_to_the_attackers_where_the_mode_has_them()
    {
        var rig = new RoundRig();
        Fill(rig, red: 1, blue: 1);
        rig.Mode.EvenTeamsJoinAttackers = true;

        Assert.Equal(TFTeam.Blue, Policy(rig).AutoTeam(preferred: TFTeam.Red));
    }

    /// <summary>Stock's other A/D arm (tf_player.cpp:6129, :6173): the map named RED
    /// the defenders, so an even game sends the new player to the attackers.</summary>
    [Fact]
    public void EvenTeamsJoinBlueWhenRedDefends()
    {
        var rig = new RoundRig();
        Fill(rig, red: 1, blue: 1);
        var roles = new TFTeamRoles();
        roles.SetRole(TFTeam.Red, TFTeamRole.Defenders);   // the map's SetRedTeamRole 1
        rig.Mode.EvenTeamsJoinAttackers = false;

        Assert.Equal(TFTeam.Blue, Policy(rig, roles: roles).AutoTeam(preferred: TFTeam.Red));
    }

    [Fact]
    public void Auto_join_on_even_teams_honours_a_preference_and_else_tosses_a_coin()
    {
        var rig = new RoundRig();
        Fill(rig, red: 1, blue: 1);
        var policy = Policy(rig);

        Assert.Equal(TFTeam.Red, policy.AutoTeam(preferred: TFTeam.Red));

        var picks = Enumerable.Range(0, 64).Select(_ => policy.AutoTeam()).ToHashSet();
        Assert.Equal(new HashSet<TFTeam> { TFTeam.Red, TFTeam.Blue }, picks);
    }

    // ---- WouldChangeUnbalanceTeams -------------------------------------------

    [Fact]
    public void A_join_that_would_unbalance_past_the_limit_is_refused_with_the_reason()
    {
        var rig = new RoundRig();
        Fill(rig, red: 2, blue: 1);
        var joiner = rig.Roster.Add(TFTeam.Unassigned);

        Assert.False(Policy(rig).MayJoin(joiner, (int)TFTeam.Red, out string why));
        Assert.Equal(TFTeams.UnbalancedReason, why);
    }

    [Fact]
    public void A_join_inside_the_limit_is_accepted()
    {
        var rig = new RoundRig();
        Fill(rig, red: 2, blue: 1);
        var joiner = rig.Roster.Add(TFTeam.Unassigned);

        Assert.True(Policy(rig).MayJoin(joiner, (int)TFTeam.Blue, out _));
    }

    [Fact]
    public void Leaving_a_team_counts_against_it_when_judging_the_balance()
    {
        // Red 2, Blue 2; a red player switching makes blue 3 v red 1: refused.
        var rig = new RoundRig();
        Fill(rig, red: 1, blue: 2);
        var mover = rig.Roster.Add(TFTeam.Red);

        Assert.False(Policy(rig).MayJoin(mover, (int)TFTeam.Blue, out _));
    }

    [Fact]
    public void A_spectator_join_and_mp_teams_unbalance_limit_0_are_never_refused_for_balance()
    {
        var rig = new RoundRig();
        Fill(rig, red: 3, blue: 0);
        var joiner = rig.Roster.Add(TFTeam.Unassigned);

        Assert.True(Policy(rig).MayJoin(joiner, (int)TFTeam.Spectator, out _));

        rig.Settings.TeamsUnbalanceLimit = 0;
        Assert.True(Policy(rig).MayJoin(joiner, (int)TFTeam.Red, out _));
    }

    [Fact]
    public void No_team_change_is_taken_once_the_game_is_over()
    {
        var rig = new RoundRig();
        var joiner = rig.Roster.Add(TFTeam.Unassigned);

        Assert.False(Policy(rig, RoundState.GameOver).MayJoin(joiner, (int)TFTeam.Red, out string why));
        Assert.Equal(TFTeams.GameOverReason, why);
    }

    [Fact]
    public void Under_a_mode_that_is_not_TF2_s_every_join_is_the_core_s()
    {
        var rig = new RoundRig();
        Fill(rig, red: 3, blue: 0);
        var joiner = rig.Roster.Add(TFTeam.Unassigned);
        rig.Slot.Current = null;

        Assert.True(Policy(rig, RoundState.GameOver).MayJoin(joiner, (int)TFTeam.Red, out _));
    }

    // ---- BalanceTeams ----------------------------------------------------------

    [Fact]
    public void Autobalance_moves_exactly_one_player_to_the_lighter_team_and_announces_it()
    {
        var rig = new RoundRig();
        Fill(rig, red: 3, blue: 1);

        rig.Balance.BalanceTeams(requireDead: false);

        var move = Assert.Single(rig.Roster.Moves);
        Assert.Equal(TFTeam.Blue, move.Team);
        Assert.Equal(1, rig.Announcer.Count("team_balanced"));
        Assert.Equal(2, rig.Roster.Players(TFTeam.Red).Count);
        Assert.Equal(2, rig.Roster.Players(TFTeam.Blue).Count);
    }

    [Fact]
    public void Teams_inside_the_limit_are_left_alone()
    {
        var rig = new RoundRig();
        Fill(rig, red: 2, blue: 1);

        rig.Balance.BalanceTeams(requireDead: false);

        Assert.Empty(rig.Roster.Moves);
    }

    [Fact]
    public void During_a_round_only_a_dead_player_is_moved()
    {
        var rig = new RoundRig();
        Fill(rig, red: 3, blue: 1);

        rig.Balance.BalanceTeams(requireDead: true);
        Assert.Empty(rig.Roster.Moves);

        var dead = rig.Roster.Players(TFTeam.Red)[2];
        rig.Roster.Kill(dead);
        rig.Balance.BalanceTeams(requireDead: true);

        Assert.Equal(dead, Assert.Single(rig.Roster.Moves).Player);
    }

    [Fact]
    public void The_newest_player_is_moved_first()
    {
        // CalculateTeamBalanceScore: minus the seconds on the server.
        var rig = new RoundRig();
        Fill(rig, red: 3, blue: 1);
        var reds = rig.Roster.Players(TFTeam.Red);
        rig.Balance.NoteSeen(reds[0], now: 10f);
        rig.Balance.NoteSeen(reds[1], now: 20f);
        rig.Balance.NoteSeen(reds[2], now: 90f);

        rig.Balance.BalanceTeams(requireDead: false);

        Assert.Equal(reds[2], Assert.Single(rig.Roster.Moves).Player);
    }

    [Fact]
    public void The_warning_comes_after_a_second_of_unbalanced_teams()
    {
        var rig = new RoundRig();
        Fill(rig, red: 3, blue: 1);
        rig.Settings.AutoTeamBalance = 1;

        // Alive players during a round: nobody moves, the warning is timed.
        rig.Balance.BalanceTeams(requireDead: true);
        Assert.Equal(0, rig.Announcer.Count("balance_warning"));

        rig.Clock.Now += 1.1f;
        rig.Balance.BalanceTeams(requireDead: true);
        Assert.Equal(1, rig.Announcer.Count("balance_warning 5"));

        rig.Clock.Now += 1.1f;
        rig.Balance.BalanceTeams(requireDead: true);
        Assert.Equal(1, rig.Announcer.Count("balance_warning"));
    }

    [Fact]
    public void mp_autoteambalance_0_and_2_leave_the_teams_to_their_players()
    {
        foreach (int mode in new[] { 0, 2 })
        {
            var rig = new RoundRig();
            rig.Settings.AutoTeamBalance = mode;
            Fill(rig, red: 3, blue: 1);

            rig.Balance.BalanceTeams(requireDead: false);

            Assert.Empty(rig.Roster.Moves);
        }
    }

    [Fact]
    public void Nothing_moves_in_the_minute_after_a_round_starts_its_PREROUND()
    {
        var rig = new RoundRig();
        Fill(rig, red: 3, blue: 1);
        rig.Balance.StartBalancingAt = rig.Clock.Now + 60f;

        rig.Balance.BalanceTeams(requireDead: false);

        Assert.Empty(rig.Roster.Moves);
    }

    [Fact]
    public void Nothing_moves_during_a_round_with_under_a_minute_on_the_HUD_timer()
    {
        var rig = new RoundRig();
        Fill(rig, red: 3, blue: 1);
        rig.Roster.Kill(rig.Roster.Players(TFTeam.Red)[0]);
        rig.Timers.Active = new FakeTimer { TimeRemaining = 59f };

        rig.Balance.BalanceTeams(requireDead: true);
        Assert.Empty(rig.Roster.Moves);

        rig.Timers.Active = new FakeTimer { TimeRemaining = 61f };
        rig.Balance.BalanceTeams(requireDead: true);
        Assert.Single(rig.Roster.Moves);
    }

    [Fact]
    public void The_round_machine_balances_on_entering_PREROUND()
    {
        var rig = new RoundRig();
        Fill(rig, red: 3, blue: 1);

        Assert.True(rig.RunUntil(() => rig.Flow.State == RoundState.Preround));

        Assert.Single(rig.Roster.Moves);
    }

    // ---- jointeam --------------------------------------------------------------

    [Theory]
    [InlineData("red", TFTeam.Red)]
    [InlineData("BLUE", TFTeam.Blue)]
    [InlineData("spectate", TFTeam.Spectator)]
    [InlineData("Spectator", TFTeam.Spectator)]
    [InlineData("nonsense", TFTeam.Red)]   // stock's default
    public void A_jointeam_name_joins_its_team(string name, TFTeam team)
    {
        var rig = new RoundRig();
        var core = new JoinRecorder();
        var command = new TFJoinTeamCommand(core, Policy(rig), new PanelSink());

        Assert.True(command.Handle(new EntityHandle(1u), "jointeam", [name]));

        Assert.Equal(new[] { (int)team }, core.Joined);
    }

    [Fact]
    public void jointeam_auto_joins_the_smaller_team()
    {
        var rig = new RoundRig();
        Fill(rig, red: 2, blue: 1);
        var core = new JoinRecorder();

        Assert.True(new TFJoinTeamCommand(core, Policy(rig), new PanelSink()).Handle(new EntityHandle(1u), "jointeam", ["auto"]));

        Assert.Equal(new[] { (int)TFTeam.Blue }, core.Joined);
    }

    [Fact]
    public void jointeam_unassigned_is_claimed_and_joins_nothing()
    {
        var rig = new RoundRig();
        var core = new JoinRecorder();

        Assert.True(new TFJoinTeamCommand(core, Policy(rig), new PanelSink()).Handle(new EntityHandle(1u), "jointeam", ["unassigned"]));

        Assert.Empty(core.Joined);
    }

    [Fact]
    public void A_numbered_jointeam_and_another_word_are_left_to_the_core()
    {
        var rig = new RoundRig();
        var core = new JoinRecorder();
        var command = new TFJoinTeamCommand(core, Policy(rig), new PanelSink());

        Assert.False(command.Handle(new EntityHandle(1u), "jointeam", ["3"]));
        Assert.False(command.Handle(new EntityHandle(1u), "joinclass", ["heavy"]));
        Assert.Empty(core.Joined);
    }

    // ---- mp_humans_must_join_team ----------------------------------------------

    /// <summary>The humans'-team convar over a stored backend, set to <paramref name="value"/>.</summary>
    private static ConVarRegistry HumansTeam(string value)
    {
        var convars = new ConVarRegistry(new StoredConVarBackend());
        convars.RegisterTypes([typeof(MpHumansMustJoinTeamConVar)]);
        convars.Get<MpHumansMustJoinTeamConVar>()!.SetValue(value);
        return convars;
    }

    /// <summary>
    /// The humans' team (tf_player.cpp:6280-6289): a named pick is overridden
    /// by <c>mp_humans_must_join_team</c>, the override the join.
    /// </summary>
    [Fact]
    public void The_humans_team_overrides_a_named_pick()
    {
        var rig = new RoundRig();
        var core = new JoinRecorder();
        var convars = HumansTeam("blue");
        var command = new TFJoinTeamCommand(core, Policy(rig), new PanelSink(),
                                            convars.Get<MpHumansMustJoinTeamConVar>());

        Assert.True(command.Handle(new EntityHandle(1u), "jointeam", ["red"]));

        Assert.Equal(new[] { (int)TFTeam.Blue }, core.Joined);
    }

    [Fact]
    public void The_humans_team_any_leaves_the_pick()
    {
        var rig = new RoundRig();
        var core = new JoinRecorder();
        var convars = HumansTeam("any");
        var command = new TFJoinTeamCommand(core, Policy(rig), new PanelSink(),
                                            convars.Get<MpHumansMustJoinTeamConVar>());

        Assert.True(command.Handle(new EntityHandle(1u), "jointeam", ["red"]));

        Assert.Equal(new[] { (int)TFTeam.Red }, core.Joined);
    }

    /// <summary>The override's IsBot arm (tf_player.cpp:6281): a bot's pick is
    /// never forced to the humans' team.</summary>
    [Fact]
    public void ABotsPickIsNotForcedToTheHumansTeam()
    {
        var rig = new RoundRig();
        var core = new JoinRecorder();
        var convars = HumansTeam("blue");
        var bots = new KnownBots();
        var command = new TFJoinTeamCommand(core, Policy(rig), new PanelSink(),
                                            convars.Get<MpHumansMustJoinTeamConVar>(), bots);
        var player = new EntityHandle(1u);
        bots.Known.Add(player);

        Assert.True(command.Handle(player, "jointeam", ["red"]));

        Assert.Equal(new[] { (int)TFTeam.Red }, core.Joined);
    }

    [Fact]
    public void The_humans_team_spares_the_spectator_gate()
    {
        var rig = new RoundRig();
        var core = new JoinRecorder();
        var convars = HumansTeam("red");
        var command = new TFJoinTeamCommand(core, Policy(rig), new PanelSink(),
                                            convars.Get<MpHumansMustJoinTeamConVar>());

        Assert.True(command.Handle(new EntityHandle(1u), "jointeam", ["spectate"]));

        Assert.Equal(new[] { (int)TFTeam.Spectator }, core.Joined);
    }

    /// <summary>The override stands ahead of the same-team return (:6280 before :6414): a
    /// pick it sends home is the return, no join and no panel.</summary>
    [Fact]
    public void A_pick_the_override_sends_home_is_the_same_team_return()
    {
        var rig = new RoundRig();
        var core = new JoinRecorder { TeamAnswer = (int)TFTeam.Blue };
        var convars = HumansTeam("blue");
        var command = new TFJoinTeamCommand(core, Policy(rig), new PanelSink(),
                                            convars.Get<MpHumansMustJoinTeamConVar>());

        Assert.True(command.Handle(new EntityHandle(1u), "jointeam", ["blue"]));

        Assert.Empty(core.Joined);
    }

    // ---- MaySpectate (mp_spectators_restricted, tf_player.cpp:6364-6381) ------

    [Fact]
    public void Spectating_is_free_while_the_restriction_is_off()
    {
        var rig = new RoundRig();
        Fill(rig, red: 1, blue: 3);
        var leaver = rig.Roster.Add(TFTeam.Red);

        Assert.True(Policy(rig).MaySpectate(leaver));
    }

    [Fact]
    public void A_player_off_the_playing_teams_spectates_under_the_restriction()
    {
        var rig = new RoundRig();
        Fill(rig, red: 1, blue: 3);
        rig.Settings.SpectatorsRestricted = true;
        var newcomer = rig.Roster.Add(TFTeam.Unassigned);

        Assert.True(Policy(rig).MaySpectate(newcomer));
    }

    [Fact]
    public void The_smaller_team_s_player_may_not_leave_once_the_gap_reaches_the_limit()
    {
        // Red 1, Blue 3: a red leaving makes 0 v 3, past the limit of 1.
        var rig = new RoundRig();
        Fill(rig, red: 1, blue: 3);
        rig.Settings.SpectatorsRestricted = true;
        var leaver = rig.Roster.Add(TFTeam.Red);

        Assert.False(Policy(rig).MaySpectate(leaver));
    }

    [Fact]
    public void The_larger_team_s_player_may_leave_under_the_restriction()
    {
        var rig = new RoundRig();
        Fill(rig, red: 1, blue: 3);
        rig.Settings.SpectatorsRestricted = true;
        var leaver = rig.Roster.Players(TFTeam.Blue)[0];

        Assert.True(Policy(rig).MaySpectate(leaver));
    }

    /// <summary>The sends these facts watch for.</summary>
    private sealed class RecordingSink : IUserMessageSender
    {
        public readonly List<(string Key, int Player)> Sent = new();

        public void Send(string key, ReadOnlySpan<byte> payload, int player = 0, bool reliable = true)
            => Sent.Add((key, player));
    }

    [Fact]
    public void A_refused_spectate_prints_the_token_and_joins_nothing()
    {
        var rig = new RoundRig();
        Fill(rig, red: 1, blue: 3);
        rig.Settings.SpectatorsRestricted = true;
        var leaver = rig.Roster.Add(TFTeam.Red);
        var core = new JoinRecorder();
        var sink = new RecordingSink();

        Assert.True(new TFJoinTeamCommand(core, Policy(rig), sink).Handle(leaver, "jointeam", ["spectate"]));

        Assert.Empty(core.Joined);
        Assert.Equal(("TextMsg", leaver.EntryIndex), Assert.Single(sink.Sent));
    }

    [Fact]
    public void An_unrestricted_spectate_join_still_reaches_the_core()
    {
        var rig = new RoundRig();
        Fill(rig, red: 1, blue: 3);
        var leaver = rig.Roster.Add(TFTeam.Red);
        var core = new JoinRecorder();
        var sink = new RecordingSink();

        Assert.True(new TFJoinTeamCommand(core, Policy(rig), sink).Handle(leaver, "jointeam", ["spectate"]));

        Assert.Equal(new[] { (int)TFTeam.Spectator }, core.Joined);
        Assert.DoesNotContain(sink.Sent, s => s.Key == "TextMsg");
    }

    // ---- the active mode's team answers (tf2ai F40) ------------------------------------

    private static readonly EntityHandle Talker = new(0x010001);
    private static readonly EntityHandle Listener = new(0x010002);

    private sealed class ModeSelector : IGameModeSelector
    {
        public string? PickMode() => CtfMode.ModeId;
    }

    private sealed class Clients : IConnectedClients
    {
        public float? AvgLatency(int entIndex) => null;
        public ulong SteamId64(int entIndex) => 0;
        public string? Address(int entIndex) => null;
        public string? ClientConVar(int entIndex, string name) => null;
        public int UserId(int entIndex) => 0;
        public void Kick(int entIndex, string reason) { }
        public void ClientCommand(int entIndex, string command) { }
        public void Reconnect(int entIndex) { }
    }

    /// <summary>
    /// The relay over the recording engine, both players on red, with the mode
    /// proxy built the way the module builds it: the TF mode registered as the
    /// scan registers it, and the selector chain naming it, so the relay's
    /// teammate ask reads the active mode's Teamplay answer for real.
    /// </summary>
    private static HostSay TeamRelay()
    {
        var engine = RecordingEngine.Default();
        engine.Strict = false;                                  // as the mode-proxy rigs run it
        engine.Returns("Players.GetTeam", (int)TFTeam.Red);     // both players are on red
        var log = new RecordingLog();
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(SsPlayer).Assembly);
        var services = new ServiceContainer();
        registry.Services = services;
        var convars = ConVarRegistry.For(TestEngines.Server().Engine.Require<IEngineConsole>());
        convars.RegisterAll(typeof(GameRules).Assembly);       // the modes' convars

        var tf = new TFModeRig();
        services.AddInstance<IHooks<IGameModeSelector>>(new ListHooks<IGameModeSelector>(new ModeSelector()));
        services.AddInstance(tf.Ctf.GetType(), tf.Ctf);
        var modes = new GameModes(log, engine, services, convars);

        var say = new HostSay(log, engine, engine, registry, engine, new Clients(), engine, engine);
        say.AttachModes(modes);
        return say;
    }

    /// <summary>Stock TF is a team game: the mode says so itself.</summary>
    [Fact]
    public void ATFModeIsTeamplay()
    {
        Assert.True(new TFModeRig().Ctf.Teamplay, "TF plays with teams");
    }

    /// <summary>Under an active TF mode a team line reaches the same team.</summary>
    [Fact]
    public void SayTeamReachesATeammateUnderATFMode()
    {
        var say = TeamRelay();

        Assert.True(say.CanHear(Listener, Talker, teamOnly: true), "the two red players hear each other");
    }
}
