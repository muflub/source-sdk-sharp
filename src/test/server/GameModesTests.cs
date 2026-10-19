using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The mode proxy (tf2 stage 0.7, ruling U1). What each fact pins, per the
/// plan: with no mode registered the core answers with its own machine — the
/// game exactly as it was; the selector chain picks and the first non-null
/// answer wins, a throwing selector is skipped; <c>SwitchTo</c> flushes ONCE
/// (the core's bookkeeping and each mode machine, by reference), runs setup
/// before the switch's warmup, and the win check stays gated until the warmup
/// ends; an unknown id is a log line and an unchanged game; the
/// <c>game_mode_switch</c> entity's <c>SwitchTo</c> input routes to the same
/// switch; and nothing that dispatches behaviour to a mod is sealed with no
/// way in (ruling D10).
///
/// <para>
/// The rules object is the real <see cref="GameRules"/> over the engine
/// double, not a stub: the claim under test is that the proxy reaches the
/// machine the game actually runs, and a stub would only prove the proxy
/// calls what it was handed. The rules and the proxy are wired in the
/// module's order — the proxy first, then the rules over it, then the two-way
/// <see cref="GameModes.Attach"/>. The stimulus (a score at the frag limit, a
/// spawn, a death) is seeded AFTER any switch that flushes the board, since
/// the flush is itself part of what the fact is measuring.
/// </para>
/// </summary>
public class GameModesTests
{
    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private static readonly EntityHandle Player = new(0x0001_0001u);

    /// <summary>The machine under a mode: records every dispatch it receives.</summary>
    private sealed class FakeFlow(List<string> log, string id) : IRoundFlow
    {
        public int Resets { get; private set; }
        public int Thinks { get; private set; }
        public bool RoundOver { get; set; }
        public bool Throws { get; init; }
        public List<EntityHandle> Spawned { get; } = [];
        public List<PlayerDeathArgs> Deaths { get; } = [];

        public void Think(float now)
        {
            if (Throws)
                throw new InvalidOperationException($"{id} think is broken");
            Thinks++;
            log.Add($"think:{id}");
        }

        public void ResetRound()
        {
            Resets++;
            log.Add($"reset:{id}");
        }

        public void OnPlayerSpawned(EntityHandle player, float now) => Spawned.Add(player);
        public void OnPlayerKilled(PlayerDeathArgs death) => Deaths.Add(death);
    }

    private sealed class FakeScoring(List<string> log, string id) : IScoring
    {
        public int Deaths { get; private set; }
        public void OnDeath(PlayerDeathArgs death) { Deaths++; log.Add($"score:{id}"); }
        public int TeamScore(int team) => -1;
        public void ResetRound() => log.Add($"scores-reset:{id}");
    }

    private sealed class FakeSetup(List<string> log, string id) : IModeSetup
    {
        public void SetupRound(float now) => log.Add($"setup:{id}");
    }

    private sealed class Mode(string id, IRoundFlow rounds, IScoring? scoring = null, IModeSetup? setup = null) : IGameMode
    {
        public string Id { get; } = id;
        public IRoundFlow Rounds { get; } = rounds;
        public IScoring? Scoring { get; } = scoring;
        public IModeSetup? Setup { get; } = setup;
    }

    /// <summary>The same surface under its OWN type: the rig registers a mode
    /// keyed by its instance type, so a second `Mode` would replace the first
    /// in the container rather than join it.</summary>
    private sealed class OtherMode(string id, IRoundFlow rounds, IScoring? scoring = null) : IGameMode
    {
        public string Id { get; } = id;
        public IRoundFlow Rounds { get; } = rounds;
        public IScoring? Scoring { get; } = scoring;
    }
    private sealed class Selector(Func<string?> pick) : IGameModeSelector
    {
        public int Asks { get; private set; }
        public string? PickMode()
        {
            Asks++;
            return pick();
        }
    }

    private sealed class Rig
    {
        public readonly ConVarRegistry ConVars = new(TestServices.UnattachedConsole());
        public readonly List<string> Order = [];
        public readonly RecordingLog Log = new();

        // The rules get their own recorder: a delegated machine that throws is
        // named on the RULES' log (the dispatch that caught it is the rules'),
        // and a fact asserting that must read the log the message actually
        // went to, not the proxy's.
        public readonly RecordingLog RulesLog = new();

        // ONE double for both the rules' host and the proxy's clock: the same
        // object answers IEngineHost.CurrentTime and IGameClock.Now, so one
        // SetTime moves the whole conversation and the two read a consistent now.
        public readonly RecordingEngine Host = TestServices.Fake();
        public readonly PlayerDeathEvent Deaths = new();
        public readonly Scoreboard Board;
        public readonly GameRules Rules;
        public readonly ServiceContainer Services = TestServices.Services();
        public readonly GameModes Modes;

        public Rig()
        {
            // The modes' convars (SvModeSwitchWarmupConVar) are in the server
            // assembly, the same one GameRules lives in.
            ConVars.RegisterAll(typeof(GameRules).Assembly);

            Host.Returns("Host.ActiveSide", Side.Server);
            // The loadout grant the rules' spawn performs, the userid lookup
            // the log names, and the two clocks: this rig's facts read none of
            // them directly, so they are set aside rather than defaulted.
            Host.Ignores("Players.FindByUserId", "Players.GiveItem", "Players.GiveAmmo");

            Board = new Scoreboard(Host, new PlayerDisconnectEvent(), new RecordingLog());
            Services.AddInstance<IHooks<IGameModeSelector>>(new ListHooks<IGameModeSelector>());

            // The module's order: the proxy before the rules (the rules take
            // it as their mode owner), the rules before Attach (the proxy
            // takes the machine that is the round's default).
            Modes = new GameModes(Log, Host, Services, ConVars);
            Rules = new GameRules(RulesLog, ConVars, Host, Host,
                                  TestServices.Fake().Returns("Entities.IsValid", false), Board, Deaths,
                                  modes: Modes);
            Modes.Attach(Rules);
            Services.AddInstance(Modes);
        }

        /// <summary>Registers a mode the way the container hands one out.</summary>
        public void Register(IGameMode mode) => Services.AddInstance(mode.GetType(), mode);

        public void Selectors(params IGameModeSelector[] selectors)
            => Services.AddInstance<IHooks<IGameModeSelector>>(new ListHooks<IGameModeSelector>(selectors));

        public void SetTime(float now) => Host.Returns("Clock.Now", now).Returns("Host.CurrentTime", now);
        public void Warmup(float seconds) => ConVars.Get<SvModeSwitchWarmupConVar>()!.Bind(-1, "sv_mode_switch_warmup", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public void Waiting(float seconds) => ConVars.Get<MpWaitingForPlayersTimeConVar>()!.Bind(-1, "mp_waitingforplayers_time", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public void FragLimit(int n) => ConVars.Get<FragLimitConVar>()!.Bind(-1, "cs_sv_fraglimit", n.ToString());
        public void EndThink() => ((IFrameLifecycle)Modes).OnThink(0.1f);
    }

    //---------------------------------------------------------------------
    // The no-owner arm: the game exactly as it was before any mode existed.
    //---------------------------------------------------------------------

    [Fact]
    public void WithNoModeRegisteredTheCoreAnswersWithItsOwnMachine()
    {
        var rig = new Rig();

        // The proxy's own answer before anyone asks a chain: the core's mode,
        // whose machine IS the rules object — the checks that ran before any
        // mode existed, unchanged, and named so the no-owner facts measure the
        // game and not a copy.
        Assert.Equal("null", rig.Modes.Active?.Id);
        Assert.Same(rig.Rules, rig.Modes.Active!.Rounds);
        Assert.DoesNotContain(rig.Log.Lines, l => l.Text.Contains("no mode is registered"));
    }

    [Fact]
    public void AnUnknownIdIsALogLineAndAnUnchangedGame()
    {
        var rig = new Rig();
        rig.Selectors(new Selector(() => "no-such-mode"));

        Assert.Null(rig.Modes.Active);                    // the unchanged arm: no machine but the core's own
        Assert.Equal("no-such-mode", rig.Modes.AskedId);  // asked, and refused out loud
        Assert.Contains(rig.Log.Lines, l => l.Warning && l.Text.Contains("no mode is registered as 'no-such-mode'"));

        // And the game still runs: the rules' own machine answers the frame —
        // no throw, no end, because the frag limit is off.
        rig.SetTime(5f);
        rig.Rules.Think();
        Assert.False(rig.Rules.RoundOver);
    }

    [Fact]
    public void TheFirstNonNullAnswerOfTheChainWinsAndTheRestAreNotAsked()
    {
        var rig = new Rig();
        rig.Register(new Mode("alpha", new FakeFlow(rig.Order, "alpha")));

        var declined = new Selector(() => null);
        var chose = new Selector(() => "alpha");
        var neverAsked = new Selector(() => "beta");
        rig.Selectors(declined, chose, neverAsked);   // the double preserves the given order

        Assert.Equal("alpha", rig.Modes.Active?.Id);
        Assert.Equal(1, declined.Asks);
        Assert.Equal(1, chose.Asks);
        Assert.True(neverAsked.Asks == 0, "the chain stops at the first non-null answer");
    }

    [Fact]
    public void AThrowingSelectorIsSkippedAndTheChainCarriesOn()
    {
        var rig = new Rig();
        rig.Register(new Mode("chosen", new FakeFlow(rig.Order, "chosen")));

        rig.Selectors(new Selector(() => throw new InvalidOperationException("mod broken")),
                      new Selector(() => "chosen"));

        Assert.True(rig.Modes.Active?.Id == "chosen", "a broken mod must never strand the server with no mode");
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("mode selector") && l.Text.Contains("mod broken"));
    }

    //---------------------------------------------------------------------
    // The switch (D6): flush once, setup, then warm up before the round may win.
    //---------------------------------------------------------------------

    [Fact]
    public void SwitchingFlushesTheNewMachineOnceAndSetsItUpAfterTheFlush()
    {
        var rig = new Rig();
        var flow = new FakeFlow(rig.Order, "arena");
        rig.Register(new Mode("arena", flow, setup: new FakeSetup(rig.Order, "arena")));
        rig.SetTime(10f);   // default zero warmup: a straight switch

        Assert.True(rig.Modes.SwitchTo("arena"));

        // THE FLUSH, ONCE: one reset on the new mode's machine. A second one
        // (the plan's failure this fact refuses) is two round starts. The reset
        // runs before the new mode's setup, which arranges the world.
        Assert.Equal(1, flow.Resets);
        Assert.Equal(new[] { "reset:arena", "setup:arena" }, rig.Order);
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("switched to 'arena'"));
        Assert.False(rig.Modes.IsWarmingUp, "zero warmup starts the round at once");
    }

    /// <summary>
    /// The IScoring half of the flush (tf2 stage 0.7 part C): the mode's own
    /// score totals go back to zero with the machine's, once — the same
    /// "flushes once" the plan's D6 switch is measured by. A scoring object
    /// the two modes SHARE is reset once, and the reset lands with the
    /// flush, before the new mode's setup.
    /// </summary>
    [Fact]
    public void SwitchingResetsTheModesScoreTotalsOnce()
    {
        var rig = new Rig();
        var shared = new FakeScoring(rig.Order, "team");
        rig.Register(new Mode("arena", new FakeFlow(rig.Order, "arena"), scoring: shared));
        rig.Register(new OtherMode("goal", new FakeFlow(rig.Order, "goal"), scoring: shared));
        rig.SetTime(10f);

        Assert.True(rig.Modes.SwitchTo("arena"));           // previous = null: one naming
        Assert.True(rig.Modes.SwitchTo("goal"));            // previous + next, SAME object

        // Switch 2 also resets the PREVIOUS mode's machine (the rule the
        // shared-machine fact pins), and the SHARED scoring once even though
        // both modes name it: one scores-reset line per switch, not two.
        Assert.Equal(new[] { "reset:arena", "scores-reset:team",
                             "reset:arena", "reset:goal", "scores-reset:team" }, rig.Order);
        Assert.Equal(2, rig.Order.Count(l => l == "scores-reset:team"));
    }

    [Fact]
    public void SwitchingToTheSameModeFlushesItsSharedMachineOnceNotTwice()
    {
        // Reference dedup: the next mode's machine may BE the previous one's.
        // A wrapper that reset by position would flush the shared machine twice
        // — the plan's "SwitchTo flushes once", measured.
        var rig = new Rig();
        var flow = new FakeFlow(rig.Order, "arena");
        rig.Register(new Mode("arena", flow));

        rig.SetTime(1f);
        Assert.True(rig.Modes.SwitchTo("arena"));
        int afterFirst = flow.Resets;

        rig.SetTime(2f);
        Assert.True(rig.Modes.SwitchTo("arena"));   // previous IS next

        Assert.True(flow.Resets == afterFirst + 1, "each switch flushes the shared machine once, not twice");
    }

    /// <summary>A live entity that counts the in-place reset it is owed.</summary>
    private sealed class ResetProbe : ScriptedEntity
    {
        public int Resets { get; private set; }
        public override void RoundReset() => Resets++;
    }

    [Fact]
    public void ASwitchResetsEveryLiveEntityExactlyOnce()
    {
        // The in-place half of stock's CleanUpMap (the divergence row): the
        // registry the module resolves (its concrete EntityDirectory) walked
        // once per switch, each live entity's RoundReset called once — not
        // zero (the walk dropped), not twice (a walk per machine reset).
        var rig = new Rig();
        var live = new EntityDirectory(new RecordingLog());
        var probes = new[] { new ResetProbe(), new ResetProbe(), new ResetProbe() };
        for (int i = 0; i < probes.Length; i++)
            Assert.True(live.Bind(new EntityHandle(0x0003_0001u + (uint)i), probes[i]));
        rig.Services.AddInstance(live);
        rig.Register(new Mode("arena", new FakeFlow(rig.Order, "arena")));
        rig.SetTime(1f);

        Assert.True(rig.Modes.SwitchTo("arena"));
        Assert.All(probes, p => Assert.Equal(1, p.Resets));

        Assert.True(rig.Modes.SwitchTo("null"));          // a second switch walks again, once
        Assert.All(probes, p => Assert.Equal(2, p.Resets));
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("round reset: 3 entities"));
    }

    /// <summary>A live entity with per-round-looking state and NO RoundReset override: the no-op default is its answer.</summary>
    private sealed class PlainEntity : ScriptedEntity
    {
        public int Counter = 5;
    }

    [Fact]
    public void AnEntityThatDoesNotOverrideRoundResetIsUntouchedByTheWalk()
    {
        // The no-owner arm of the reset walk: an entity that never opted in
        // keeps its state and its binding, while the one beside it that did
        // opt in is reset (the stimulus reached the walk).
        var rig = new Rig();
        var live = new EntityDirectory(new RecordingLog());
        var plainHandle = new EntityHandle(0x0003_0001u);
        var plain = new PlainEntity { Counter = 42, Handle = plainHandle };    // the handle the registry gives it in game
        var probe = new ResetProbe { Handle = new EntityHandle(0x0003_0002u) };
        Assert.True(live.Bind(plainHandle, plain));
        Assert.True(live.Bind(probe.Handle, probe));
        rig.Services.AddInstance(live);
        rig.Register(new Mode("arena", new FakeFlow(rig.Order, "arena")));
        rig.SetTime(1f);

        Assert.True(rig.Modes.SwitchTo("arena"));

        Assert.Equal(1, probe.Resets);                                       // the opted-in one reset
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("round reset: 2 entities"));   // both were walked
        Assert.Equal(42, plain.Counter);                                     // the plain one kept its state
        Assert.Contains(plain, live.Snapshot());                             // and its binding
        Assert.DoesNotContain(rig.Log.Lines, l => l.Warning);                // and nothing was said about it
    }

    [Fact]
    public void AModeWithNoSetupSwitchesWithNoSetupStep()
    {
        // The no-owner arm of IModeSetup, direct: a registered mode whose
        // Setup is null switches, flushes, and prepares nothing (no step, no
        // complaint), where the setup facts above see "setup:arena" follow.
        var rig = new Rig();
        var flow = new FakeFlow(rig.Order, "arena");
        var mode = new Mode("arena", flow);
        Assert.Null(mode.Setup);
        rig.Register(mode);
        rig.SetTime(10f);

        Assert.True(rig.Modes.SwitchTo("arena"));

        Assert.Equal("arena", rig.Modes.Active?.Id);
        Assert.Equal(new[] { "reset:arena" }, rig.Order);                    // the flush, and nothing after it
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("switched to 'arena'"));
        Assert.DoesNotContain(rig.Log.Lines, l => l.Warning);                // no setup threw
        Assert.False(rig.Modes.IsWarmingUp, "zero warmup starts the round at once");
    }

    [Fact]
    public void SwitchingToTheNullModeReopensTheCoreRoundWithoutADuplicateMachine()
    {
        // NullMode's Rounds IS the rules object; the flush already runs the
        // core's bookkeeping as the core's own. The reference guard is what
        // keeps the plain call from being the core's reset a second time. The
        // observable: a round the core ended comes back (the flush ran) and the
        // board the flush clears proves it ran — a duplicate would clear an
        // already-clear board, which this cannot see, so the arm also checks
        // the reopen that only a real reset performs.
        var rig = new Rig();
        rig.Rules.EndRound();
        Assert.True(rig.Rules.RoundOver);   // before: the core thinks the round ended
        rig.Board.Add(5, 3, 1, 10);

        rig.SetTime(1f);
        Assert.True(rig.Modes.SwitchTo("null"));

        Assert.False(rig.Rules.RoundOver, "the flush reopened the core's round");
        Assert.True(rig.Board.KillsFor(5) == 0, "and cleared the core's bookkeeping");
    }

    [Fact]
    public void TheSetupAndWarmupOrderHoldsAcrossASwitch()
    {
        // Setup prepares the world (the plan: after the flush, before warmup),
        // and the round may not win until the effective wait — the max of
        // sv_mode_switch_warmup and mp_waitingforplayers_time — has passed.
        var rig = new Rig();
        var flow = new FakeFlow(rig.Order, "arena");
        rig.Register(new Mode("arena", flow, setup: new FakeSetup(rig.Order, "arena")));
        rig.Warmup(5);
        rig.SetTime(100f);

        rig.Modes.SwitchTo("arena");
        Assert.True(rig.Modes.IsWarmingUp, "five seconds wait");

        rig.SetTime(104.9f);
        rig.EndThink();
        Assert.True(rig.Modes.IsWarmingUp, "not yet");

        rig.SetTime(105f);
        rig.EndThink();
        Assert.False(rig.Modes.IsWarmingUp);
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("warmup over") && l.Text.Contains("arena"));
        Assert.True(rig.Order.SequenceEqual(new[] { "reset:arena", "setup:arena" }),
            "the flush and setup ran at the switch, before the round opened");
    }

    [Fact]
    public void WaitingForPlayersIsAWarmupEvenAtZeroSwitchWarmup()
    {
        // The plan's default: the effective wait is the max of the two convars,
        // so a server already configured to wait for players gets a switch wait
        // without sv_mode_switch_warmup being named at all.
        var rig = new Rig();
        rig.Register(new Mode("arena", new FakeFlow(rig.Order, "arena")));
        rig.Waiting(2);   // sv_mode_switch_warmup left at 0
        rig.SetTime(0f);

        rig.Modes.SwitchTo("arena");
        Assert.True(rig.Modes.IsWarmingUp, "mp_waitingforplayers_time alone is a warmup");
    }

    [Fact]
    public void ALevelChangeEndsASwitchesWarmup()
    {
        // The warmup's end is a time on the OLD map's clock; the new map's
        // clock restarts near zero, so a carried end time would gate the win
        // check (frag and time limits off) for most of the new map.
        var rig = new Rig();
        rig.Register(new Mode("arena", new FakeFlow(rig.Order, "arena")));
        rig.Warmup(30);
        rig.SetTime(1000f);

        rig.Modes.SwitchTo("arena");
        Assert.True(rig.Modes.IsWarmingUp, "the stimulus: a warmup is running");

        rig.SetTime(0.5f);                                   // the new map's clock
        ((IGameLifecycle)rig.Modes).OnLevelInit("dm_lockdown");

        Assert.False(rig.Modes.IsWarmingUp, "a new map is a new round, not the old switch's wait");
    }

    [Fact]
    public void AfterAModeOwnedRoundEndsNothingMoreScores()
    {
        // IRoundFlow.RoundOver's contract: the round "ends once, and scores
        // nothing more" — the core's own _roundOver rule (stock's
        // IsIntermission early-out in PlayerKilled), asked of the machine
        // that owns the round.
        var rig = new Rig();
        var flow = new FakeFlow(rig.Order, "arena");
        var scoring = new FakeScoring(rig.Order, "arena");
        rig.Register(new Mode("arena", flow, scoring: scoring));
        rig.SetTime(1f);
        rig.Modes.SwitchTo("arena");

        // The control: a running round scores, on all three writers.
        rig.Deaths.Raise(new PlayerDeathArgs(UserId: 7, AttackerUserId: 9, "pistol", Headshot: false));
        Assert.True(rig.Board.KillsFor(9) == 1 && flow.Deaths.Count == 1 && scoring.Deaths == 1);

        flow.RoundOver = true;                               // the mode ended its round
        rig.Deaths.Raise(new PlayerDeathArgs(UserId: 7, AttackerUserId: 9, "pistol", Headshot: false));

        Assert.True(rig.Board.KillsFor(9) == 1, "the core's board took no kill after the mode's round ended");
        Assert.True(flow.Deaths.Count == 1, "the machine's death arm was not fed");
        Assert.True(scoring.Deaths == 1, "the mode's own score write did not run");
    }

    [Fact]
    public void TwoModesWithOneIdAreAStartupRefusalNamingBoth()
    {
        // Find resolves an id to the FIRST registration, so a second mod's
        // "arena" would be silently unreachable. The module verifies the
        // registry right after the mods' scan; ids compare as Find does.
        var rig = new Rig();
        rig.Register(new Mode("arena", new FakeFlow(rig.Order, "arena")));
        rig.Register(new OtherMode("Arena", new FakeFlow(rig.Order, "other")));

        var error = Assert.Throws<InvalidOperationException>(() => rig.Modes.VerifyRegistry());

        Assert.Contains(typeof(Mode).FullName!, error.Message);
        Assert.Contains(typeof(OtherMode).FullName!, error.Message);
        Assert.Contains("arena", error.Message, StringComparison.OrdinalIgnoreCase);

        // The control: distinct ids (and the core's own "null" beside a
        // mod's, which is the documented override) verify clean.
        var clean = new Rig();
        clean.Register(new Mode("arena", new FakeFlow(clean.Order, "arena")));
        clean.Register(new OtherMode("null", new FakeFlow(clean.Order, "modnull")));
        clean.Modes.VerifyRegistry();
    }

    [Fact]
    public void TheWinCheckWaitsForTheWarmupAndThenRunsTheModesMachine()
    {
        var rig = new Rig();
        var flow = new FakeFlow(rig.Order, "arena");
        rig.Register(new Mode("arena", flow));
        rig.Warmup(30);
        rig.SetTime(10f);

        rig.Modes.SwitchTo("arena");   // warmup ends at 40

        rig.SetTime(11f);
        rig.Rules.Think();
        Assert.True(flow.Thinks == 0, "the round has not started; its win check may not run");

        rig.SetTime(41f);
        rig.EndThink();                // the wait ends
        rig.Rules.Think();
        Assert.True(flow.Thinks == 1, "the round owns its machine now");
    }

    [Fact]
    public void TheCoreChecksAreGatedByTheWarmupToo()
    {
        // The stimulus reaches the subject: a score at the frag limit ENDS the
        // round once the gate is open, and only then — the warmup's whole point
        // is that nobody wins out of it. The score is seeded after the switch
        // because the switch's flush clears the board.
        var rig = new Rig();
        rig.FragLimit(1);
        rig.Warmup(4);
        rig.SetTime(20f);

        Assert.True(rig.Modes.SwitchTo("null"));   // no mode owns the round
        rig.Board.Add(7, 1, 0, 1);                  // score at the limit, after the flush

        rig.Rules.Think();
        Assert.False(rig.Rules.RoundOver, "warming up: the round may not end out of it");

        rig.SetTime(25f);
        rig.EndThink();                             // gate open (25 >= 24)
        rig.Rules.Think();
        Assert.True(rig.Rules.RoundOver, "gate open, limit reached");
    }

    [Fact]
    public void ARefusedSwitchLeavesTheRoundAlone()
    {
        var rig = new Rig();
        var flow = new FakeFlow(rig.Order, "arena");
        rig.Register(new Mode("arena", flow));
        rig.SetTime(0f);
        rig.Modes.SwitchTo("arena");
        int resets = flow.Resets;

        Assert.False(rig.Modes.SwitchTo("ghost"));   // the unknown-id arm

        Assert.True(flow.Resets == resets, "nothing of the switch ran for a refused id");
        Assert.Equal("arena", rig.Modes.Active?.Id);  // and the game did not move
        Assert.Contains(rig.Log.Lines, l => l.Warning && l.Text.Contains("switch to 'ghost' refused"));
    }

    [Fact]
    public void AThrowingMachineIsNamedAndTheCoreDoesNotAnswerAsWell()
    {
        // Two answers to one question is the failure: when the delegated
        // machine throws, the core's own checks must NOT also run beside it.
        var rig = new Rig();
        var flow = new FakeFlow(rig.Order, "arena") { Throws = true };
        rig.Register(new Mode("arena", flow));
        rig.FragLimit(1);
        rig.SetTime(1f);
        rig.Modes.SwitchTo("arena");   // zero warmup: the gate is already open
        rig.Board.Add(7, 1, 0, 1);     // score at the core's limit, after the flush

        rig.Rules.Think();              // the throw is caught inside the dispatch

        Assert.False(rig.Rules.RoundOver, "the core's machine did not answer as well");
        Assert.Contains(rig.RulesLog.Lines, l => l.Warning && l.Text.Contains("arena") && l.Text.Contains("round think threw"));
    }

    [Fact]
    public void TheDelegatedMachineSeesTheSpawnAndTheKillAfterTheCore()
    {
        var rig = new Rig();
        var flow = new FakeFlow(rig.Order, "arena");
        var scoring = new FakeScoring(rig.Order, "arena");
        rig.Register(new Mode("arena", flow, scoring: scoring));
        rig.SetTime(1f);
        rig.Modes.SwitchTo("arena");

        rig.Rules.OnPlayerSpawned(Player, 1f);
        Assert.True(flow.Spawned.Count == 1 && flow.Spawned[0] == Player, "after the core's own loadout and protection");

        rig.Deaths.Raise(new PlayerDeathArgs(UserId: 7, AttackerUserId: 9, "pistol", Headshot: false));
        Assert.Single(flow.Deaths);                         // the round's view of the kill
        Assert.Equal(1, scoring.Deaths);                    // and the mode's extra score write
        Assert.True(rig.Board.KillsFor(9) == 1, "the core's scoreboard write still happened");
    }

    [Fact]
    public void TheMapEntityRoutesItsSwitchToInputToTheProxy()
    {
        var rig = new Rig();
        var flow = new FakeFlow(rig.Order, "arena");
        rig.Register(new Mode("arena", flow));

        // The entity reaches the proxy through the container (its constructor
        // asks for GameModes) and its input is bound through the real
        // [ScriptedEvent] binder — both halves of "a mapper wires SwitchTo
        // arena in the map" are exercised, not replaced with a direct call.
        var registry = new ScriptedEntityRegistry { Services = rig.Services };
        registry.RegisterAll(typeof(GameModeSwitch).Assembly);
        var handle = new EntityHandle(0x0002_0002u);
        registry.Spawn(handle, "game_mode_switch");

        Assert.True(registry.Input(handle, "SwitchTo", "arena", 0f, EntityHandle.None));
        Assert.Equal("arena", rig.Modes.Active?.Id);
        Assert.Equal(1, flow.Resets);
    }

    /// <summary>
    /// The D10 structure fact, over the REAL types: the mode layer is
    /// extensible by the SURFACES a mod implements, and every one of them
    /// stays an interface; and no sealed class dispatches to them except
    /// through a registration seam. Ruling U1 is that a mod can neither
    /// replace nor subclass the rules — so <see cref="GameRules"/> is sealed on
    /// purpose; the claim is that what it asks goes through the proxy
    /// (<see cref="GameModes"/>), whose answers are registrations, and that
    /// <see cref="NullMode"/>, the one class a mod varies, is subclassable.
    /// </summary>
    [Fact]
    public void TheModeLayerIsExtensibleThroughInterfacesNotSubclassing()
    {
        Assert.True(typeof(GameRules).IsSealed, "U1: the rules are neither replaceable nor subclassable");
        Assert.False(typeof(NullMode).IsSealed, "the core's mode is the base a mod varies");

        // The contract surface, read off IGameMode itself: a concern added as
        // a property is in this set the moment it exists.
        var contracts = typeof(IGameMode).GetProperties().Select(p => p.PropertyType)
                            .Select(t => Nullable.GetUnderlyingType(t) ?? t)
                            .Where(t => t != typeof(string))
                            .Append(typeof(IGameMode)).Append(typeof(IGameModeSelector))
                            .Distinct().ToArray();
        Assert.True(contracts.Length >= 7, $"the surface read as {contracts.Length} types: the walk found nothing");
        foreach (var t in contracts)
            Assert.True(t.IsInterface, $"{t.Name} must stay an interface — a sealed seam is D10 violated");

        // Every class in the server assembly that holds a contract (or the
        // proxy that answers them). The stimulus: the walk must find the
        // proxy and its known callers, or it found nothing to judge.
        var dispatchSources = contracts.Append(typeof(GameModes)).ToArray();
        var dispatchers = SealedSeams.Dispatchers(typeof(GameModes).Assembly, dispatchSources);
        Assert.Contains(typeof(GameModes), dispatchers);
        Assert.Contains(typeof(GameRules), dispatchers);
        Assert.Contains(typeof(Teams), dispatchers);

        // A sealed dispatcher must reach the contracts through a seam — the
        // proxy, an IHooks chain, or the container — never a hard-wired
        // instance a mod could not replace.
        var unseamed = SealedSeams.SealedWithoutSeam(dispatchers, [typeof(GameModes)]);
        Assert.True(unseamed.Count == 0,
            "sealed classes dispatch to the mode contracts with no seam: " +
            string.Join(", ", unseamed.Select(t => t.FullName)));

        // The seam GameRules exposes despite being sealed: it implements the
        // round-machine contract, which is how NullMode hands it back as the
        // game's own machine.
        Assert.True(typeof(IRoundFlow).IsAssignableFrom(typeof(GameRules)));
    }
}
