using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The mods' damage modifiers (tf2 stage 2.7): <see cref="IDamageModifier"/>,
/// folded by <see cref="GameRules.ModifyDamage"/> and asked by the player's
/// own damage path after the rules allowed the hit. Each fact is a
/// differential: the same hit with and without the modifier.
/// </summary>
public class DamageModifierTests
{
    private static readonly EntityHandle Attacker = new(0x0002_0001u);

    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Scale(float factor, List<string>? seen = null, string name = "") : IDamageModifier
    {
        public int Asks { get; private set; }
        public float Modify(in DamageContext hit, float damage)
        {
            Asks++;
            seen?.Add($"{name}:{hit.Damage}:{damage}");
            return damage * factor;
        }
    }

    private sealed class Throws : IDamageModifier
    {
        public float Modify(in DamageContext hit, float damage) => throw new InvalidOperationException("broken");
    }

    private sealed class Refuses : IDamageRule
    {
        public Decision Decide(in DamageContext hit) => Decision.No;
    }

    private static GameRules Rules(RecordingLog log, IDamageRule? rule = null, params IDamageModifier[] modifiers)
    {
        var convars = ConVarRegistry.For(UnattachedConsole());
        convars.RegisterAll(typeof(GameRules).Assembly);
        var host = Fake().Returns("Host.ActiveSide", Side.Server);
        var players = Fake().Ignores("Players.FindByUserId", "Players.GetUserId", "Players.GiveItem", "Players.GiveAmmo")
                            .Returns("Players.GetTeam", 0)
                            .Returns("Players.RespawnDelay", 3f);
        return new GameRules(log, convars.Holder<GameRulesConVars>(), host, players,
                             Fake().Returns("Entities.IsValid", true),
                             new Scoreboard(host, new PlayerDisconnectEvent(), new RecordingLog()),
                             new PlayerDeathEvent(),
                             damageRules: rule is null ? null : new ListHooks<IDamageRule>(rule),
                             damageModifiers: modifiers.Length == 0 ? null : new ListHooks<IDamageModifier>(modifiers));
    }

    [Fact]
    public void A_modifier_scales_the_damage_the_player_takes()
    {
        int bare = PlayerDamageRulesTests.Player(Rules(new RecordingLog())).OnDamagedAt(Attacker, 20f, DamageType.Bullet, default, default);
        int scaled = PlayerDamageRulesTests.Player(Rules(new RecordingLog(), null, new Scale(3f)))
                                           .OnDamagedAt(Attacker, 20f, DamageType.Bullet, default, default);

        Assert.True(bare == 20, $"with no modifier the hit lands as dealt (took {bare})");
        Assert.True(scaled == 60, $"a x3 modifier makes the player take 60 (took {scaled})");
    }

    [Fact]
    public void Modifiers_fold_in_order_each_handed_the_running_damage_and_the_hit_as_dealt()
    {
        var seen = new List<string>();
        var rules = Rules(new RecordingLog(), null, new Scale(2f, seen, "a"), new Scale(0.5f, seen, "b"), new Scale(3f, seen, "c"));

        float damage = rules.ModifyDamage(new EntityHandle(0x0001_0003u), Attacker, 10f, (int)DamageType.Bullet);

        Assert.Equal(30f, damage);
        Assert.Equal(["a:10:10", "b:10:20", "c:10:10"], seen);
    }

    [Fact]
    public void A_throwing_modifier_is_named_and_the_running_damage_is_kept()
    {
        var log = new RecordingLog();
        var rules = Rules(log, null, new Scale(2f), new Throws(), new Scale(2f));

        float damage = rules.ModifyDamage(new EntityHandle(0x0001_0003u), Attacker, 10f, (int)DamageType.Bullet);

        Assert.Equal(40f, damage);
        Assert.Contains(log.Lines, l => l.Warning && l.Text.Contains(typeof(Throws).FullName!, StringComparison.Ordinal));
    }

    [Fact]
    public void A_refused_hit_never_reaches_the_modifiers()
    {
        var modifier = new Scale(3f);
        var allowed = new Scale(3f);
        PlayerDamageRulesTests.Player(Rules(new RecordingLog(), new Refuses(), modifier))
                              .OnDamagedAt(Attacker, 20f, DamageType.Bullet, default, default);
        PlayerDamageRulesTests.Player(Rules(new RecordingLog(), null, allowed))
                              .OnDamagedAt(Attacker, 20f, DamageType.Bullet, default, default);

        Assert.True(allowed.Asks == 1, $"an allowed hit asks the modifier once (asked {allowed.Asks})");
        Assert.True(modifier.Asks == 0, $"a refused hit is not modified (asked {modifier.Asks})");
    }

    [Fact]
    public void A_modifier_that_answers_zero_or_less_leaves_the_player_unhurt()
    {
        int zero = PlayerDamageRulesTests.Player(Rules(new RecordingLog(), null, new Scale(0f)))
                                         .OnDamagedAt(Attacker, 20f, DamageType.Bullet, default, default);
        int negative = PlayerDamageRulesTests.Player(Rules(new RecordingLog(), null, new Scale(-1f)))
                                             .OnDamagedAt(Attacker, 20f, DamageType.Bullet, default, default);

        Assert.Equal((0, 0), (zero, negative));
    }
}
