using System.Text;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// mp_autoteambalance 2's balancer (CTFAutobalance, tf_autobalance.cpp:77-618):
/// the watch, the dead candidates' instant move, the ask to the best
/// candidate over the resource scores, and the window's forced move.
/// </summary>
public sealed class TFAutobalanceTests
{
    private sealed class FixedState(RoundState state) : ITeamRoundState
    {
        public RoundState State { get; set; } = state;
        public bool InWaitingForPlayers => false;
        public bool InSetup => false;
        public float MapResetTime => 0f;
        public float StalemateStartTime => -1f;
        public TFTeam WinningTeam => TFTeam.Unassigned;
    }

    /// <summary>The resource's score rows, set per player index.</summary>
    private sealed class FakeStats : ITFPlayerStats
    {
        public readonly Dictionary<int, int> Scores = new();

        public int Score(int playerIndex) => Scores.TryGetValue(playerIndex, out int score) ? score : 0;

        public void Increment(EntityHandle player, TFStat stat, int amount = 1) => throw new NotSupportedException();
        public int Round(int playerIndex, TFStat stat) => throw new NotSupportedException();
        public int Accumulated(int playerIndex, TFStat stat) => throw new NotSupportedException();
        public int RoundScore(int playerIndex) => throw new NotSupportedException();
        public void ResetPlayer(EntityHandle player) => Scores.Remove(player.EntryIndex);
    }

    /// <summary>Every TextMsg, decoded.</summary>
    private sealed class MessageSink : IUserMessageSender
    {
        public readonly List<(int Player, string Key, string Payload)> Sent = new();

        public void Send(string key, ReadOnlySpan<byte> payload, int player = 0, bool reliable = true)
            => Sent.Add((player, key, Encoding.UTF8.GetString(payload.ToArray())));
    }

    private sealed class Backend : IConVarBackend
    {
        private readonly List<string> _names = [];
        private readonly List<string> _values = [];

        public int Register(string name, string defaultValue, string help, int flags, float? min, float? max)
        {
            _names.Add(name);
            _values.Add(defaultValue);
            return _names.Count - 1;
        }

        public void Set(int id, string value) => _values[id] = value;
        public string Get(int id) => _values[id];
        public string? ValueOf(string name) => _names.Contains(name) ? _values[_names.IndexOf(name)] : null;
    }

    /// <summary>The machine over fakes: a running round, the clock at 100 s.</summary>
    private sealed class Rig
    {
        public readonly FakeClock Clock = new() { Now = 100f };
        public readonly FakeSettings Settings = new() { AutoTeamBalance = 2 };
        public readonly FakeRoster Roster;
        public readonly FakeAnnouncer Announcer = new();
        public readonly RecordingLog Log = new();
        public readonly FixedState Rounds = new(RoundState.RoundRunning);
        public readonly FakeStats Stats = new();
        public readonly MessageSink Messages = new();
        public readonly TFAutobalance Balance;

        public Rig()
        {
            Roster = new FakeRoster(Clock);
            var convars = new ConVarRegistry(new Backend());
            convars.RegisterTypes([typeof(TFAutobalanceDeadCandidatesMaxTimeConVar),
                                   typeof(TFAutobalanceForceCandidatesMaxTimeConVar),
                                   typeof(TFAutobalanceXpBonusConVar)]);
            Balance = new TFAutobalance(Settings, Roster, Announcer, Clock, Log, Rounds, Stats, Messages, convars);
        }

        public void Think() => ((IFrameLifecycle)Balance).OnThink(0.015f);

        /// <summary>Thinks every 0.1 s for <paramref name="seconds"/>.</summary>
        public void Run(float seconds)
        {
            for (float end = Clock.Now + seconds; Clock.Now < end; Clock.Now += 0.1f)
                Think();
        }

        /// <summary>Three reds and a blue; the reds' scores 10, 10, 2, so the
        /// dead-arm pick (and the ask) names reds[2] first (the score closest
        /// to the average the switch asks for).</summary>
        public (EntityHandle A, EntityHandle B, EntityHandle C, EntityHandle Blue) Unbalanced()
        {
            var a = Roster.Add(TFTeam.Red);
            var b = Roster.Add(TFTeam.Red);
            var c = Roster.Add(TFTeam.Red);
            var blue = Roster.Add(TFTeam.Blue);
            Stats.Scores[1] = 10;
            Stats.Scores[2] = 10;
            Stats.Scores[3] = 2;
            return (a, b, c, blue);
        }
    }

    [Fact]
    public void mp_autoteambalance_0_and_1_leave_the_ask_machine_inactive()
    {
        foreach (int mode in new[] { 0, 1 })
        {
            var rig = new Rig();
            rig.Settings.AutoTeamBalance = mode;
            rig.Unbalanced();

            rig.Run(30f);

            Assert.Empty(rig.Roster.Moves);
            Assert.DoesNotContain(rig.Messages.Sent, m => m.Payload.Contains(TFMessages.AutoBalanceTeamChangePending));
        }
    }

    [Fact]
    public void Nothing_is_asked_outside_a_running_round()
    {
        var rig = new Rig();
        rig.Unbalanced();
        rig.Rounds.State = RoundState.Preround;

        rig.Run(30f);

        Assert.Empty(rig.Roster.Moves);
        Assert.Empty(rig.Messages.Sent);
    }

    [Fact]
    public void A_dead_candidate_is_moved_at_once_and_told_what_happened()
    {
        var rig = new Rig();
        var (_, _, c, _) = rig.Unbalanced();
        rig.Roster.Kill(c);

        rig.Run(1f);

        var move = Assert.Single(rig.Roster.Moves);
        Assert.Equal(c, move.Player);
        Assert.Equal(TFTeam.Blue, move.Team);
        Assert.Single(rig.Roster.Respawns);
        Assert.Equal(1, rig.Announcer.Count("team_balanced"));
        var done = Assert.Single(rig.Messages.Sent, m => m.Payload.Contains(TFMessages.AutoBalanceTeamChangeDone));
        Assert.Equal(3, done.Player);   // the moved player, nobody else
        Assert.Contains(TFMessages.AutoBalanceTeamChangeDone, done.Payload);
        Assert.Contains("#TF_BlueTeam_Name", done.Payload);
        Assert.Contains("500", done.Payload);
    }

    [Fact]
    public void Once_balanced_the_machine_stands_down()
    {
        var rig = new Rig();
        var (_, _, c, _) = rig.Unbalanced();
        rig.Roster.Kill(c);

        rig.Run(1f);
        Assert.Single(rig.Roster.Moves);

        rig.Run(30f);

        Assert.Single(rig.Roster.Moves);
        Assert.DoesNotContain(rig.Messages.Sent, m => m.Payload.Contains(TFMessages.AutoBalanceTeamChangePending));
    }

    [Fact]
    public void The_ask_goes_to_the_candidate_whose_resource_score_is_closest_to_the_average_needed()
    {
        var rig = new Rig();
        rig.Unbalanced();

        // Monitor finds the candidates; the 15 s dead-candidate window passes
        // with everyone alive; the setup then asks the best one.
        rig.Run(0.3f);
        rig.Run(16f);

        var asks = rig.Messages.Sent.Where(m => m.Payload.Contains(TFMessages.AutoBalanceTeamChangePending)).ToList();
        Assert.NotEmpty(asks);
        // Stock re-sends every frame of the window; all of them name the best
        // candidate and nobody else.
        Assert.All(asks, m => Assert.Equal(3, m.Player));
        Assert.Contains("#TF_BlueTeam_Name", asks[0].Payload);
        Assert.Contains("5", asks[0].Payload);   // tf_autobalance_force_candidates_maxtime's default
        Assert.Empty(rig.Roster.Moves);
    }

    [Fact]
    public void The_asked_candidate_is_force_moved_when_the_window_runs_out()
    {
        var rig = new Rig();
        rig.Unbalanced();

        rig.Run(0.3f);
        Assert.Empty(rig.Roster.Moves);

        rig.Run(16f);
        Assert.Empty(rig.Roster.Moves);

        rig.Run(6f);

        var move = Assert.Single(rig.Roster.Moves);
        Assert.Equal(TFTeam.Blue, move.Team);
        Assert.Equal(1, rig.Announcer.Count("team_balanced"));
    }

    [Fact]
    public void A_candidate_who_moved_themselves_is_not_force_moved()
    {
        var rig = new Rig();
        var (_, _, c, blue) = rig.Unbalanced();

        rig.Run(0.3f);
        rig.Run(16f);

        // The volunteer: joins the light team on their own.
        rig.Roster.ChangeTeam(c, TFTeam.Blue);

        rig.Run(6f);

        // The volunteer's own join, nobody forced (a force always respawns).
        Assert.Single(rig.Roster.Moves);
        Assert.Empty(rig.Roster.Respawns);
    }

    [Fact]
    public void A_level_init_puts_the_machine_back_to_its_floor()
    {
        var rig = new Rig();
        rig.Unbalanced();
        rig.Run(16.3f);

        ((IGameLifecycle)rig.Balance).OnLevelInit("ctf_2fort");
        Assert.Empty(rig.Roster.Moves);
        rig.Messages.Sent.Clear();

        // Back to watching: the imbalance has to be seen again before anything moves.
        rig.Run(0.3f);
        Assert.Empty(rig.Messages.Sent);
        Assert.Empty(rig.Roster.Moves);

        rig.Run(21f);
        Assert.Single(rig.Roster.Moves);
    }
}
