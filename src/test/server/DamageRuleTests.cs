using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The mods' half of the damage question (tf2 stage 0.7 part C): the
/// <see cref="IDamageRule"/> chain, asked only where <see cref="GameRules"/>
/// deferred, first actual answer winning. The pair each fact must show is the
/// differential — the same hit answers one way with no rule and another with
/// the rule present — because a chain that is never reached fails as a pass
/// (the decision was already the one the arm reports).
/// </summary>
public class DamageRuleTests
{
    private static readonly EntityHandle Victim = new(0x0001_0001u);
    private static readonly EntityHandle Attacker = new(0x0002_0001u);
    private static readonly EntityHandle Self = new(0x0001_0001u);   // same index as Victim

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    /// <summary>A rule with an answer, a name, and a count of how often it was asked.</summary>
    private sealed class Rule(Decision answer, string name, List<string>? asked = null) : IDamageRule
    {
        public int Asks { get; private set; }
        public Decision Decide(in DamageContext hit)
        {
            Asks++;
            asked?.Add(name + ">" + hit.Victim + ">" + hit.Attacker + ">" + hit.Damage + ">" + hit.DamageType);
            return answer;
        }
    }

    private sealed class Rig
    {
        public readonly ConVarRegistry ConVars = ConVarRegistry.For(TestServices.UnattachedConsole());
        public readonly RecordingLog Log = new();
        private readonly RecordingEngine _host;
        private readonly RecordingEngine _players;

        public Rig()
        {
            ConVars.RegisterAll(typeof(GameRules).Assembly);
            _host = Fake().Returns("Host.ActiveSide", Side.Server);
            _players = Fake().Ignores("Players.FindByUserId", "Players.GetUserId", "Players.GiveItem", "Players.GiveAmmo");
            _players.Returns("Players.RespawnDelay", 3f);
        }

        /// <summary>
        /// A rules object over the rig's engine doubles and the rig's LOG: the
        /// warning for a throwing rule lands on the rules' own recorder (the
        /// dispatch that caught it is theirs), so the fact that asserts it has
        /// to read this log and not a discarded one.
        /// </summary>
        public GameRules Rules(IHooks<IDamageRule>? chain = null)
            => new(Log, ConVars, _host, _players,
                   Fake().Returns("Entities.IsValid", false),
                   new Scoreboard(_host, new PlayerDisconnectEvent(), new RecordingLog()),
                   new PlayerDeathEvent(), damageRules: chain ?? new ListHooks<IDamageRule>(), loadout: new ListHooks<ILoadoutProvider>());

        public void FriendlyFire(string value) => ConVars.Get<FriendlyFireConVar>()!.Bind(-1, "sv_friendlyfire", value);
        public void SpawnProtect(string value) => ConVars.Get<SpawnProtectConVar>()!.Bind(-1, "cs_sv_spawn_protect", value);
        public GameRules Bare() => Rules(null);   // the no-owner arm: no chain at all
    }

    //-------------------------------------------------------------------------
    [Fact]
    public void NoRulesAndTheHitDefersExactlyAsBefore()
    {
        // The no-owner arm, differential form: the same hit with no chain and
        // with an empty chain must both defer — the behaviour before any rule
        // existed, which the chain's mere presence must not shift.
        var rig = new Rig();

        Assert.True(rig.Bare().CanTakeDamage(Victim, Attacker, 10f, 0, 0f) == Decision.UseStockRule,
            "no chain: the game as it was");
        Assert.True(rig.Rules(new ListHooks<IDamageRule>()).CanTakeDamage(Victim, Attacker, 10f, 0, 0f) == Decision.UseStockRule,
            "empty chain: an assembled hook costs the answer nothing");
    }

    [Fact]
    public void ARuleThatRefusesStopsWhatTheCoreDeferred()
    {
        var rig = new Rig();

        // BEFORE: the same hit defers (the differential half of the proof).
        Assert.True(rig.Bare().CanTakeDamage(Victim, Attacker, 10f, 0, 0f) == Decision.UseStockRule);

        // AFTER: the rule's refusal is what the caller gets.
        Assert.True(rig.Rules(new ListHooks<IDamageRule>(new Rule(Decision.No, "immunity")))
                        .CanTakeDamage(Victim, Attacker, 10f, 0, 0f) == Decision.No,
            "an immunity zone refuses a hit the core allowed");
    }

    [Fact]
    public void TheSelfDamageArmReachesTheChainAndARuleCanOpenTheDoor()
    {
        // Self-damage is the core's first deferral; the chain may ALLOW it
        // there (the core refused only spawn protection and friendly fire —
        // those two arms are refused-only, and their own fact is below).
        var rig = new Rig();
        var yes = new Rule(Decision.Yes, "arena-duel");

        Assert.True(rig.Rules(new ListHooks<IDamageRule>(yes)).CanTakeDamage(Self, Self, 50f, 3, 0f) == Decision.Yes);
        Assert.Equal(1, yes.Asks);
    }

    [Fact]
    public void TheChainCanNeverOpenTheCoreOwnGuards()
    {
        // Spawn protection and friendly fire answer NO below the chain: a
        // mod installed beside the server must not lift a server's published
        // policy. Both guards, both directions, one fact each arm.
        var rig = new Rig();
        var yes = new Rule(Decision.Yes, "god-mode");

        rig.SpawnProtect("5");
        var protectedRules = rig.Rules(new ListHooks<IDamageRule>(yes));
        protectedRules.OnPlayerSpawned(Victim, 100f);
        Assert.True(protectedRules.CanTakeDamage(Victim, Attacker, 10f, 0, 101f) == Decision.No,
            "spawn protection holds a hit a YES rule tried to open");
        Assert.True(yes.Asks == 0, "the chain is not even asked once the core refused");

        rig.FriendlyFire("0");
        var teamRig = Fake().Ignores("Players.FindByUserId", "Players.GetUserId");
        teamRig.Returns("Players.GetTeam", 2);
        var ff = new GameRules(new RecordingLog(), rig.ConVars,
                               Fake().Returns("Host.ActiveSide", Side.Server), teamRig,
                               Fake().Returns("Entities.IsValid", true),
                               new Scoreboard(Fake().Returns("Host.ActiveSide", Side.Server), new PlayerDisconnectEvent(), new RecordingLog()),
                               new PlayerDeathEvent(), damageRules: new ListHooks<IDamageRule>(yes), loadout: new ListHooks<ILoadoutProvider>());
        Assert.True(ff.CanTakeDamage(Victim, Attacker, 10f, 0, 0f) == Decision.No,
            "friendly fire off holds a hit a YES rule tried to open");
    }

    [Fact]
    public void TheFirstActualAnswerWinsAndTheRestAreNotAsked()
    {
        var rig = new Rig();
        var declined = new Rule(Decision.UseStockRule, "declines");
        var refuses = new Rule(Decision.No, "refuses");
        var never = new Rule(Decision.Yes, "never");

        Assert.True(rig.Rules(new ListHooks<IDamageRule>(declined, refuses, never))
                        .CanTakeDamage(Victim, Attacker, 10f, 0, 0f) == Decision.No);
        Assert.Equal(1, declined.Asks);
        Assert.Equal(1, refuses.Asks);
        Assert.True(never.Asks == 0, "the chain stops at the first actual answer");
    }

    [Fact]
    public void AThrowingRuleIsNamedAndTreatedAsADeclination()
    {
        var rig = new Rig();
        var broken = new BrokenRule("broken");
        var refuses = new Rule(Decision.No, "refuses");

        var answer = rig.Rules(new ListHooks<IDamageRule>(broken, refuses))
                        .CanTakeDamage(Victim, Attacker, 10f, 0, 0f);

        Assert.True(answer == Decision.No, "a broken immunity zone defers like a declining one, and the chain carries on");
        Assert.Contains(rig.Log.Lines, l => l.Warning && l.Text.Contains("broken"));
    }

    private sealed class BrokenRule(string name) : IDamageRule
    {
        public Decision Decide(in DamageContext hit) => throw new InvalidOperationException(name + " rule is broken");
    }

    [Fact]
    public void TheContextCarriesTheFourValuesTheAbiActuallyHas()
    {
        var rig = new Rig();
        var asked = new List<string>();
        rig.Rules(new ListHooks<IDamageRule>(new Rule(Decision.No, "spy", asked)))
           .CanTakeDamage(Victim, Attacker, 12.5f, 7, 0f);

        Assert.Single(asked);
        Assert.True(asked[0] == $"spy>{Victim}>{Attacker}>12.5>7",
            "victim, attacker, damage, type — and nothing else the ABI does not carry");
    }
}
