
using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Skills;
using TF2.Descent.Server.Skills;
using TF2.Server.Projectiles;
using TF2.Shared;

using Xunit;

namespace TF2.Descent.Tests.Skills;

/// <summary>
/// One fact per effect primitive's binder (plan_descent_logic HE6), against
/// the fake world: what each asks of the engine, with the numbers the rules
/// resolved.
/// </summary>
public sealed class SkillBinderTests
{
    private static readonly EntityHandle Robot = new(0x0004_0000u | 77);

    /// <summary>An effect as the rules would resolve it, with the primitive's defaults and <paramref name="numbers"/> over them.</summary>
    private static ResolvedEffect Effect(Primitive primitive, string? target, IReadOnlyDictionary<string, double> numbers,
                                         IReadOnlyList<string>? conditions = null, IReadOnlyList<StatAmount>? stats = null)
    {
        var spec = Primitives.Of(primitive);
        var ps = spec.Optional.ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var r in spec.Required)
            ps[r] = 0;
        foreach (var (k, v) in numbers)
            ps[k] = v;
        return new ResolvedEffect(primitive, null, target, null, ps, stats ?? [], conditions ?? [], null);
    }

    private static SkillCast Cast(SkillRig rig, string skillId, IReadOnlyList<ResolvedEffect> effects, int index = SkillRig.Index)
    {
        var def = rig.Skills.Skill(skillId);
        var skill = new ResolvedSkill(skillId, 1, 1, 0, 0, 0, effects);
        return new SkillCast(index, rig.Hero(index).Player, def, skill, 0, rig.World.Tick, []);
    }

    private static SkillCaster Caster(SkillRig rig, int index = SkillRig.Index) => rig.Casters.Of(index)!;

    //-------------------------------------------------------------------------

    [Theory]
    [InlineData("tf_weapon_rocketlauncher")]
    [InlineData("tf_weapon_shovel")]
    [InlineData(null)]
    public void A_skill_projectile_is_spawned_with_the_rules_damage_and_the_heros_team_whatever_is_held(string? weapon)
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 12);
        rig.Learn("patriot_missile");
        rig.Slot(1, "patriot_missile");
        rig.World.Weapon[hero.Player] = weapon;
        rig.Run(1);

        rig.Runtime.Press(SkillRig.Index, 1);

        var shot = Assert.Single(rig.World.Spawned);
        Assert.Equal(TFProjectileEntityNames.Rocket, shot.ClassName);
        Assert.Equal(hero.Player, shot.Owner);
        Assert.Equal((int)TFTeam.Red, shot.Team);
        Assert.Equal("descent_skill_patriot_missile", shot.WeaponName);
        Assert.Equal(41.44f, shot.Damage, 2); // 20 x (1 + 0.04 x 12) x (1 + aim 20 / 50)
        Assert.Equal((float)rig.RulesDamage("patriot_missile"), shot.Damage, 4);
    }

    [Fact]
    public void A_hitscan_hurts_what_the_line_hits_with_the_rules_damage_as_the_skills_weapon()
    {
        var rig = new SkillRig();
        rig.Join(level: 2);
        rig.World.NextTrace = new TraceResult(true, 0.5f, new Vector(200, 0, 64), default, Robot);

        rig.Binders.Of(Primitive.Hitscan)!.Fire(Caster(rig),
            Cast(rig, "patriot_missile", []), Effect(Primitive.Hitscan, "enemies", new Dictionary<string, double> { ["damage"] = 33, ["range"] = 900 }));

        var hurt = Assert.Single(rig.World.Hurts);
        Assert.Equal((Robot, rig.Hero().Player, 33f, DamageType.Bullet, "descent_skill_patriot_missile"), hurt);
    }

    [Fact]
    public void An_area_burst_blasts_each_shell_after_its_delay_and_spares_nothing_early()
    {
        var rig = new SkillRig();
        rig.Join(level: 2);
        int cast = rig.World.Tick;
        var burst = Effect(Primitive.AreaBurst, "enemies", new Dictionary<string, double>
            { ["damage"] = 60, ["radius"] = 110, ["delay"] = 3, ["count"] = 5, ["area"] = 400 });

        rig.Binders.Of(Primitive.AreaBurst)!.Fire(Caster(rig), Cast(rig, "patriot_missile", [burst]), burst);
        rig.Run(rig.Binders.Ticks(3) - 1);
        int early = rig.World.Blasts.Count;
        rig.Run(1);

        Assert.Equal(0, early);
        Assert.Equal(5, rig.World.Blasts.Count);
        Assert.All(rig.World.Blasts, b => Assert.Equal((60f, 110f, cast + 200), (b.Damage, b.Radius, b.Tick)));
        Assert.Equal(5, rig.World.Blasts.Select(b => b.At).Distinct().Count());
    }

    [Fact]
    public void An_aura_reaches_a_party_member_in_radius_and_not_one_outside_and_ends_on_time()
    {
        var rig = new SkillRig();
        var caster = rig.Join(level: 2);
        var near = rig.Join(4);
        var far = rig.Join(5);
        rig.World.Origins[near.Player] = new Vector(300, 0, 0);
        rig.World.Origins[far.Player] = new Vector(600, 0, 0);
        rig.Learn("battle_orders");
        rig.Slot(1, "battle_orders");
        var aura = rig.Skills.Resolve(caster.Sheet!, caster.Stats!, SkillBonuses.None, "battle_orders").Effects.Single();

        rig.Runtime.Press(SkillRig.Index, 1);
        double nearDuring = near.Stats!.Total("damage_all_pct");
        double farDuring = far.Stats!.Total("damage_all_pct");
        rig.Run(rig.Binders.Ticks(aura["duration"]) - 1, 4);
        double nearLastTick = near.Stats!.Total("damage_all_pct");
        rig.Run(1, 4);

        Assert.Equal(450, aura["radius"]);
        Assert.Equal(15, nearDuring, 6);
        Assert.Equal(15, caster.Stats!.Total("damage_all_pct"), 6);
        Assert.Equal(0, farDuring, 6);
        Assert.Equal(15, nearLastTick, 6);
        Assert.Equal(0, near.Stats!.Total("damage_all_pct"), 6);
    }

    [Fact]
    public void A_buff_puts_its_conditions_on_the_hero_as_TF_conditions_for_its_duration()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 2);
        var buff = Effect(Primitive.Buff, "self", new Dictionary<string, double> { ["duration"] = 8 },
                          ["crit_boost", "no_ammo"], [new StatAmount("mana_cost_pct", -50)]);

        rig.Binders.Of(Primitive.Buff)!.Fire(Caster(rig), Cast(rig, "patriot_missile", [buff]), buff);

        Assert.Equal([(hero.Player, TFCond.Critboosted, 8f)], rig.World.Added);
        Assert.Equal(-50, hero.Stats!.Total("mana_cost_pct"), 6);
    }

    [Fact]
    public void A_debuff_goes_to_the_robot_sink_for_a_non_player_and_as_a_TF_condition_on_an_enemy_player()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 2);
        var enemy = new EntityHandle(0x0001_0000u | 9);
        var friend = rig.Join(4);
        rig.World.Team[enemy] = (int)TFTeam.Blue;
        rig.World.Origins[Robot] = new Vector(100, 0, 0);
        rig.World.Origins[enemy] = new Vector(0, 100, 0);
        rig.World.Hurtable.AddRange([Robot, enemy, friend.Player]);
        var debuff = Effect(Primitive.Debuff, "enemies", new Dictionary<string, double> { ["duration"] = 4, ["radius"] = 600 },
                            ["mini_crit_marked"]);

        rig.Binders.Of(Primitive.Debuff)!.Fire(Caster(rig), Cast(rig, "patriot_missile", [debuff]), debuff);

        Assert.Equal([(Robot, "mini_crit_marked", 4.0, "patriot_missile", "enemies")], rig.Robots.Applied);
        Assert.Equal([(enemy, TFCond.Markedfordeath, 4f)], rig.World.Added);
        Assert.Contains("mini_crit_marked on #77 for 4s: NotAvailable", rig.Heroes.Log.Text);
        Assert.DoesNotContain(rig.World.Added, a => a.Player == friend.Player || a.Player == hero.Player);
    }

    [Fact]
    public void A_dash_pushes_the_hero_along_the_aim_and_a_blast_jumping_dash_is_a_rocket_jump()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 2);
        var dash = Effect(Primitive.Dash, "self", new Dictionary<string, double> { ["impulse"] = 800 }, ["blast_jumping"]);

        rig.Binders.Of(Primitive.Dash)!.Fire(Caster(rig), Cast(rig, "patriot_missile", [dash]), dash);

        Assert.Equal([(hero.Player, new Vector(800, 0, 0))], rig.World.Pushes);
        Assert.Equal([hero.Player], rig.World.BlastJumps);
    }

    [Fact]
    public void A_heal_gives_its_amount_and_share_of_max_life_over_its_seconds_and_cleanses()
    {
        var rig = new SkillRig();
        var hero = rig.Join(level: 2);
        var heal = Effect(Primitive.Heal, "self", new Dictionary<string, double> { ["amount"] = 20, ["pctMaxLife"] = 10, ["duration"] = 4 },
                          ["cleanse_burn"]);

        rig.Binders.Of(Primitive.Heal)!.Fire(Caster(rig), Cast(rig, "patriot_missile", [heal]), heal);
        rig.RunSeconds(4);

        Assert.Equal(4, rig.World.Heals.Count);
        Assert.Equal(20 + 0.1 * 200, rig.World.Heals.Sum(h => h.Amount), 3);
        Assert.Equal([(hero.Player, TFCond.Burning)], rig.World.Removed);
    }

    [Fact]
    public void A_threat_asks_the_robot_sink_for_each_robot_in_radius_and_no_player()
    {
        var rig = new SkillRig();
        rig.Join(level: 2);
        var enemy = new EntityHandle(0x0001_0000u | 9);
        rig.World.Team[enemy] = (int)TFTeam.Blue;
        rig.World.Origins[Robot] = new Vector(100, 0, 0);
        rig.World.Hurtable.AddRange([Robot, enemy]);
        var threat = Effect(Primitive.Threat, "enemies", new Dictionary<string, double> { ["radius"] = 600, ["duration"] = 4 });

        rig.Binders.Of(Primitive.Threat)!.Fire(Caster(rig), Cast(rig, "patriot_missile", [threat]), threat);

        Assert.Equal([(Robot, 4.0, "patriot_missile")], rig.Robots.Threats);
    }

    [Fact]
    public void Summon_has_no_binder_and_no_shipped_skill_or_spell_summons()
    {
        var rig = new SkillRig();
        rig.Join(level: 2);

        Assert.Null(rig.Binders.Of(Primitive.Summon));
        Assert.DoesNotContain(rig.Heroes.Rules.Content.Skills.Rows, s => s.Effect.Any(e => e.Primitive == Primitive.Summon));
    }

    [Fact]
    public void A_projectiles_on_hit_debuff_rides_its_hits_not_its_cast()
    {
        var rig = new SkillRig();
        rig.Join(level: 12);
        rig.Learn("patriot_missile");
        rig.Learn("emp_charge");
        rig.Slot(1, "emp_charge");

        rig.Runtime.Press(SkillRig.Index, 1);
        rig.RunSeconds(2);
        rig.Runtime.Release(SkillRig.Index, 1);

        var cast = Caster(rig).LastCast["emp_charge"];
        Assert.Equal(["emp_stun", "slowed"], cast.OnHit.SelectMany(e => e.Conditions));
        Assert.Empty(rig.Robots.Applied);
        foreach (var effect in cast.OnHit)
            rig.Binders.Afflict(cast, effect, Robot);
        Assert.Equal([("emp_stun", "enemy_minions"), ("slowed", "enemy_bosses")], rig.Robots.Applied.Select(a => (a.Condition, a.Target)));
    }
}
