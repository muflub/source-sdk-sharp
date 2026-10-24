using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What a death is worth.
///
/// The rule these check is small and every part of it has a way of being got
/// wrong that nobody notices for a season: crediting a suicide makes killing
/// yourself the fastest way to win, not counting one as a death makes it free,
/// and scoring after the round has ended lets a kill traded over the scoreboard
/// screen decide a round that was already over.
///
/// They exercise GameRules directly rather than through the event, because the
/// event decoder needs an engine and the rule does not.
///
/// <para>
/// This was one <c>[Fact]</c> with 17 assertions running over one board in one
/// order. Each fact now builds its own board and plays only the deaths its own
/// question needs — the frag-limit ones included, where the sequence IS the
/// behaviour and is written out in full rather than inherited.
/// </para>
/// </summary>
public class ScoringTests
{
    /// <summary>
    /// A board, a rules object and the engine double behind them.
    ///
    /// The side is driven through the ENGINE the rules were handed, not through
    /// the process-wide <c>Engine.SetActiveSide</c> this test used to call. That
    /// global still exists, but the rules no longer read it — they read what
    /// they were given, which is the point of giving it to them.
    ///
    /// The board is this test's own. It used to be a process-wide static, so
    /// every suite shared one and each test began by clearing it — and any test
    /// that forgot was reading another's kills.
    /// </summary>
    private sealed class Match
    {
        public readonly ConVarRegistry ConVars = ConVarRegistry.For(TestServices.UnattachedConsole());
        public readonly RecordingEngine Host;
        public readonly PlayerDisconnectEvent Disconnects = new();
        public readonly PlayerDeathEvent Deaths = new();
        public readonly Scoreboard Board;
        public readonly GameRules Rules;

        public Match()
        {
            ConVars.RegisterAll(typeof(GameRules).Assembly);

            Host = Fake().Returns("Host.ActiveSide", Side.Server);
            Board = new Scoreboard(Host, Disconnects, new RecordingLog());

            Rules = new GameRules(new RecordingLog(), ConVars, Host,
                Fake().Ignores("Players.FindByUserId", "Players.GetUserId", "Players.GetTeam"),
                Fake().Returns("Entities.IsValid", false), Board, Deaths, new ListHooks<IDamageRule>(), new ListHooks<ILoadoutProvider>());

            Rules.ResetRound();
            Board.Reset();
        }

        public void Kill(int victim, int attacker, string weapon = "smg1")
            => Rules.ScoreDeath(new PlayerDeathArgs(UserId: victim, AttackerUserId: attacker, weapon, false));

        public void FragLimit(string value)
            => Bind(ConVars.Get<FragLimitConVar>()!, "cs_sv_fraglimit", value);
    }

    //-------------------------------------------------------------------------
    // The death event reaches the rules it was handed to, and no others.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheRulesScoreADeathRaisedOnTheirOwnEvent()
    {
        var m = new Match();
        m.Deaths.Raise(new PlayerDeathArgs(UserId: 2, AttackerUserId: 5, "smg1", false));

        Assert.True(m.Board.ScoreFor(5) == 1, "the injected death event reaches the rules' scoring");
    }

    [Fact]
    public void TwoRulesDoNotScoreEachOthersDeaths()
    {
        // The property the static event and its "current rules" pointer lacked:
        // with it, a death reached whichever rules object was built LAST,
        // anywhere in the process.
        var mine = new Match();
        var theirs = new Match();

        theirs.Deaths.Raise(new PlayerDeathArgs(UserId: 2, AttackerUserId: 5, "smg1", false));

        Assert.True(mine.Board.ScoreFor(5) == 0, "another match's death does not score here");
        Assert.True(theirs.Board.ScoreFor(5) == 1, "it scores in its own match");
    }

    [Fact]
    public void TheBoardDropsALeaverRaisedOnItsOwnEvent()
    {
        var mine = new Match();
        var theirs = new Match();
        mine.Board.Subscribe();
        theirs.Board.Subscribe();
        mine.Board.Add(5, 1, 0, 1);
        theirs.Board.Add(5, 1, 0, 1);

        mine.Disconnects.Raise(new PlayerDisconnectArgs("p", 5, "left"));

        Assert.True(mine.Board.ScoreFor(5) == 0, "the board drops a player who left, on its own event");
        Assert.True(theirs.Board.ScoreFor(5) == 1, "and another board keeps its own record");
    }

    //-------------------------------------------------------------------------
    // A kill by somebody else.
    //-------------------------------------------------------------------------

    [Fact]
    public void ACreditedKillIsWorthAPointToTheAttacker()
    {
        var m = new Match();
        m.Kill(victim: 2, attacker: 5);

        Assert.True(m.Board.ScoreFor(5) == 1, "a credited kill is worth a point to the attacker");
    }

    [Fact]
    public void ACreditedKillCountsAsAKill()
    {
        var m = new Match();
        m.Kill(victim: 2, attacker: 5);

        Assert.True(m.Board.KillsFor(5) == 1, "a credited kill counts as a kill");
    }

    [Fact]
    public void TheAttackerDoesNotTakeADeath()
    {
        var m = new Match();
        m.Kill(victim: 2, attacker: 5);

        Assert.True(m.Board.DeathsFor(5) == 0, "the attacker does not take a death");
    }

    [Fact]
    public void TheVictimTakesADeath()
    {
        var m = new Match();
        m.Kill(victim: 2, attacker: 5);

        Assert.True(m.Board.DeathsFor(2) == 1, "the victim takes a death");
    }

    [Fact]
    public void TheVictimKeepsTheirPoints()
    {
        var m = new Match();
        m.Kill(victim: 2, attacker: 5);

        Assert.True(m.Board.ScoreFor(2) == 0, "the victim keeps their points");
    }

    //-------------------------------------------------------------------------
    // A suicide.
    //
    // Stock's CMultiplayRules::PlayerKilled increments the victim's death count
    // BEFORE it looks at who scored, then takes a point off them for killing
    // themselves. Both halves matter and they are separate questions: "does not
    // credit the killer" is not "does not count".
    //-------------------------------------------------------------------------

    [Fact]
    public void ASuicideCountsAsADeath()
    {
        // The kill before it is kept, so the number stays the 2 the bulk fact
        // asserted: this says a suicide ADDS a death, not merely that a player
        // who has only ever killed themselves has one.
        var m = new Match();
        m.Kill(victim: 2, attacker: 5);
        m.Kill(victim: 2, attacker: 2, "world");

        Assert.True(m.Board.DeathsFor(2) == 2, "a suicide counts as a death");
    }

    [Fact]
    public void ASuicideCostsTheVictimAPoint()
    {
        var m = new Match();
        m.Kill(victim: 2, attacker: 5);
        m.Kill(victim: 2, attacker: 2, "world");

        Assert.True(m.Board.ScoreFor(2) == -1, "a suicide costs the victim a point");
    }

    [Fact]
    public void ASuicideIsNobodysKill()
    {
        var m = new Match();
        m.Kill(victim: 2, attacker: 5);
        m.Kill(victim: 2, attacker: 2, "world");

        Assert.True(m.Board.KillsFor(2) == 0, "a suicide is nobody's kill");
    }

    //-------------------------------------------------------------------------
    // The world.
    //-------------------------------------------------------------------------

    [Fact]
    public void AWorldDeathCountsAsADeath()
    {
        var m = new Match();
        m.Kill(victim: 7, attacker: 0, "world");

        Assert.True(m.Board.DeathsFor(7) == 1, "a world death counts as a death");
    }

    [Fact]
    public void AWorldDeathCostsTheVictimAPoint()
    {
        var m = new Match();
        m.Kill(victim: 7, attacker: 0, "world");

        Assert.True(m.Board.ScoreFor(7) == -1, "a world death costs the victim a point");
    }

    [Fact]
    public void TheWorldScoresNothingItself()
    {
        var m = new Match();
        m.Kill(victim: 7, attacker: 0, "world");

        Assert.True(m.Board.ScoreFor(0) == 0, "the world scores nothing itself");
    }

    //-------------------------------------------------------------------------
    // The client must not score. The death event fires on both sides, and a
    // subscriber that forgot to ask which side it was on would double every
    // number in this file.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheClientHalfDoesNotScore()
    {
        var m = new Match();
        m.Kill(victim: 2, attacker: 5);

        m.Host.Returns("Host.ActiveSide", Side.Client);
        m.Kill(victim: 2, attacker: 5);
        m.Host.Returns("Host.ActiveSide", Side.Server);

        Assert.True(m.Board.ScoreFor(5) == 1, "the client half does not score");
    }

    //-------------------------------------------------------------------------
    // The frag limit reads the SCORE, not the kill count.
    //-------------------------------------------------------------------------

    [Fact]
    public void OnePointIsUnderALimitOfTwo()
    {
        var m = new Match();
        m.FragLimit("2");

        m.Kill(victim: 2, attacker: 5);
        m.Rules.Think();

        Assert.True(!m.Rules.RoundOver, "one point is under a limit of two");
    }

    [Fact]
    public void TwoKillsAndASuicideIsOnePointNotTwo()
    {
        // A suicide by the leader takes them back down, so the round must not
        // end on the kill count alone.
        var m = new Match();
        m.FragLimit("2");

        m.Kill(victim: 2, attacker: 5);
        m.Kill(victim: 5, attacker: 5, "world");
        m.Kill(victim: 2, attacker: 5, "pistol");
        m.Rules.Think();

        Assert.True(m.Board.ScoreFor(5) == 1, "two kills and a suicide is one point, not two");
    }

    [Fact]
    public void TheLimitIsNotReachedOnKillsAlone()
    {
        var m = new Match();
        m.FragLimit("2");

        m.Kill(victim: 2, attacker: 5);
        m.Kill(victim: 5, attacker: 5, "world");
        m.Kill(victim: 2, attacker: 5, "pistol");
        m.Rules.Think();

        Assert.True(!m.Rules.RoundOver, "the limit is not reached on kills alone");
    }

    [Fact]
    public void TheLimitIsReachedOnTheScore()
    {
        var m = new Match();
        m.FragLimit("2");

        m.Kill(victim: 2, attacker: 5);
        m.Kill(victim: 5, attacker: 5, "world");
        m.Kill(victim: 2, attacker: 5, "pistol");
        m.Rules.Think();

        m.Kill(victim: 2, attacker: 5, "pistol");
        m.Rules.Think();

        Assert.True(m.Rules.RoundOver, "the limit is reached on the score");
    }

    //-------------------------------------------------------------------------
    // Intermission. Stock HL2MP's PlayerKilled returns early while
    // IsIntermission, which is the round-over latch here.
    //-------------------------------------------------------------------------

    [Fact]
    public void NothingScoresAfterTheRoundHasEnded()
    {
        var m = new Match();
        m.FragLimit("2");

        m.Kill(victim: 2, attacker: 5);
        m.Kill(victim: 5, attacker: 5, "world");
        m.Kill(victim: 2, attacker: 5, "pistol");
        m.Kill(victim: 2, attacker: 5, "pistol");
        m.Rules.Think();

        Assert.True(m.Rules.RoundOver, "the round has ended, or this fact is asking nothing");

        int before = m.Board.ScoreFor(5);
        m.Kill(victim: 2, attacker: 5, "pistol");

        Assert.True(m.Board.ScoreFor(5) == before, "nothing scores after the round has ended");
    }

    /// <summary>
    /// Gives a ConVar a value without an engine behind it, the way the other
    /// rule tests do.
    /// </summary>
    private static void Bind(ConVarBase convar, string name, string value)
        => convar.Bind(-1, name, value);
}
