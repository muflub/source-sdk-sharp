using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The player's own damage path asks the rules (tf2 0.7 fix): stock's
/// <c>CBasePlayer::OnTakeDamage</c> asks <c>FPlayerCanTakeDamage</c> before
/// the armour takes anything, and a "no" refuses the hit. Before this, the
/// managed decision — spawn protection, friendly fire and the mods'
/// <see cref="IDamageRule"/> chain — had no live caller at all.
/// </summary>
public class PlayerDamageRulesTests
{
    private static readonly EntityHandle Victim = new(0x0001_0003u);
    private static readonly EntityHandle Attacker = new(0x0002_0001u);

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Refuses : IDamageRule
    {
        public int Asks { get; private set; }
        public Decision Decide(in DamageContext hit) { Asks++; return Decision.No; }
    }

    /// <summary>The real rules over doubles: every player on <paramref name="team"/>, friendly fire as given.</summary>
    internal static GameRules Rules(int team, string friendlyFire = "1", IDamageRule? rule = null)
    {
        var convars = new ConVarRegistry(UnattachedConsole());
        convars.RegisterAll(typeof(GameRules).Assembly);
        convars.Get<FriendlyFireConVar>()!.Bind(-1, "cs_sv_friendly_fire", friendlyFire);

        var host = Fake().Returns("Host.ActiveSide", Side.Server);
        var players = Fake().Ignores("Players.FindByUserId", "Players.GetUserId", "Players.GiveItem", "Players.GiveAmmo")
                            .Returns("Players.GetTeam", team)
                            .Returns("Players.RespawnDelay", 3f);

        return new GameRules(new RecordingLog(), convars, host, players,
                             Fake().Returns("Entities.IsValid", true),
                             new Scoreboard(host, new PlayerDisconnectEvent(), new RecordingLog()),
                             new PlayerDeathEvent(),
                             damageRules: rule is null ? null : new ListHooks<IDamageRule>(rule));
    }

    internal static SsPlayer Player(IGameRules? rules)
    {
        var engine = Fake();
        engine.Returns("Entities.GetHealth", 100).Returns("Entities.GetIndex", 3)
              .Returns("Entities.IsValid", true).Returns("Entities.GetOrigin", new Vector(0f, 0f, 0f));
        var player = new SsPlayer(engine, engine, Impacts(engine), Fluid(), messages: engine, rules: rules)
            { Handle = Victim };
        player.BindAreas(Areas(engine));
        player.State.BindLocal();
        return player;
    }

    //-------------------------------------------------------------------------
    // Who is a teammate: only players on the same GAME team.
    //-------------------------------------------------------------------------

    [Fact]
    public void FriendlyFireOffLetsTwoUnassignedPlayersHurtEachOther()
    {
        var rules = Rules(team: 0, friendlyFire: "0");

        Assert.True(rules.CanTakeDamage(Victim, Attacker, 10f, (int)DamageType.Bullet, 0f) == Decision.UseStockRule,
            "two deathmatch players on Unassigned are not teammates (stock HL2MP DM: GR_NOTTEAMMATE), "
          + "so friendly fire off must not refuse the hit");
    }

    [Fact]
    public void FriendlyFireOffRefusesTwoPlayersOnTeamTwo()
    {
        var rules = Rules(team: 2, friendlyFire: "0");

        Assert.True(rules.CanTakeDamage(Victim, Attacker, 10f, (int)DamageType.Bullet, 0f) == Decision.No,
            "two players on the same game team are teammates: friendly fire off refuses the hit");
    }

    [Fact]
    public void OnlyAGameTeamMakesTeammates()
    {
        Assert.False(GameRules.Teammates(0, 0), "Unassigned is not a side");
        Assert.False(GameRules.Teammates(1, 1), "nor is Spectator");
        Assert.False(GameRules.Teammates(-1, -1), "nor 'not a player'");
        Assert.False(GameRules.Teammates(2, 3), "two game teams are enemies");
        Assert.True(GameRules.Teammates(3, 3), "the same game team is");
    }

    //-------------------------------------------------------------------------
    // The player's damage path reaches the decision.
    //-------------------------------------------------------------------------

    [Fact]
    public void ARefusingRuleStopsAHitOnThePlayersDamagePath()
    {
        var rule = new Refuses();
        var player = Player(Rules(team: 0, rule: rule));

        int taken = player.OnDamagedAt(Attacker, 25f, DamageType.Bullet, default, default);

        Assert.True(rule.Asks == 1, $"the player's own damage asked the rules' chain once (asked {rule.Asks})");
        Assert.True(taken == 0, $"and the refusal stopped the hit: the shell subtracts {taken}, not 25");
    }

    [Fact]
    public void WithNoRefusalTheHitLandsWhole()
    {
        var withRules = Player(Rules(team: 0)).OnDamagedAt(Attacker, 25f, DamageType.Bullet, default, default);
        var bare = Player(null).OnDamagedAt(Attacker, 25f, DamageType.Bullet, default, default);

        Assert.True(withRules == 25, $"defaults (friendly fire on, no protection, no rule) land the hit whole, not {withRules}");
        Assert.True(bare == 25, $"exactly as a player with no rules at all does, not {bare}");
    }
}
