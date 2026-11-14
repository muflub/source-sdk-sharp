using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Skills;
using Xunit;

namespace TF2.Descent.Tests.Skills;

/// <summary>
/// Appendix A's numbers and edges, generated from the JSON (plan_tf2_rpg 2e gate; RU6 step
/// 6): one case per skill row for its rank-1 and max-rank numbers, one case per prerequisite
/// edge and per synergy edge, each checked against <see cref="Expected"/> and
/// <see cref="ExpectedEdges"/>, which are written here from the plan's Appendix A (never read
/// back from the shipped JSON, or they could not fail). A skill or edge added to the JSON
/// without a row here fails <see cref="Every_skill_row_has_an_expectation_and_every_expectation_a_row"/>
/// and <see cref="The_edges_are_exactly_appendix_As"/>.
/// </summary>
public sealed class AppendixATests
{
    private static readonly TF2DescentRules Rules = new();
    private static SkillRules Skills => Rules.Skills;
    private static HeroRules Heroes => Rules.Heroes;
    private static DescentContent Content => Rules.Content;

    /// <summary>One number at rank 1 and at max rank: "mana", "cooldown", "floor", "primitive.param", or "stat:id".</summary>
    public sealed record Number(string Key, double Rank1, double Max);

    public sealed record Expectation(int MaxRank, Number[] Numbers);

    private static Number N(string key, double rank1, double max) => new(key, rank1, max);

    private static Number C(string key, double value) => new(key, value, value);

    /// <summary>Appendix A, A.2 to A.5, as written there (rank 1 → max rank; "x %/rank" is x and 5x).</summary>
    private static Dictionary<string, Expectation> Expected => new()
    {
        // A.2 Ordnance
        ["patriot_missile"] = new(5, [C("mana", 4), C("cooldown", 0), N("projectile.damage", 20, 60), C("projectile.radius", 110)]),
        ["marksmanship"] = new(5, [N("stat:damage_weapon_pct", 3, 15), N("stat:spread_pct", -4, -20), N("stat:projectile_speed_pct", 3, 15)]),
        ["salvo"] = new(5, [C("cooldown", 6), N("charge.stages", 3, 5), C("charge.manaPerStage", 3), C("charge.releaseTime", 0.6), C("projectile.spreadDeg", 3)]),
        ["field_dressing"] = new(5, [N("triggered.heal", 1, 3), C("triggered.maxTargets", 3), C("triggered.perSecondCap", 10)]),
        ["emp_charge"] = new(5, [C("mana", 15), C("cooldown", 8), C("charge.time", 2), N("projectile.damage", 45, 110), N("debuff.duration", 1, 2)]),
        ["rocket_specialist"] = new(5, [N("stat:reload_speed_pct", 5, 25), N("stat:blast_radius_pct", 3, 15), N("stat:projectile_speed_pct", 3, 15),
            N("stat:clip_size", 0, 2), N("triggered.duration", 0, 0.5)]),
        ["rain_of_freedom"] = new(5, [C("mana", 18), C("cooldown", 12), N("projectile.count", 6, 10), N("projectile.damage", 12, 30), C("projectile.duration", 1.5)]),
        ["killer_instinct"] = new(5, [N("stat:crit_chance_pct", 2, 10), N("stat:crit_damage_pct", 5, 25)]),
        ["fire_mission"] = new(5, [C("mana", 30), C("cooldown", 25), N("area_burst.damage", 60, 140), C("area_burst.delay", 3), C("area_burst.count", 5), C("area_burst.area", 400)]),
        ["screamin_eagle"] = new(1, [C("mana", 50), C("cooldown", 90), C("buff.duration", 8), C("stat:mana_cost_pct", -50)]),
        // A.3 Airborne
        ["jump_training"] = new(5, [N("stat:self_blast_damage_pct", -15, -75), N("stat:jump_force_pct", 3, 15)]),
        ["boot_camp"] = new(5, [N("stat:move_speed_pct", 2, 10), N("stat:deploy_speed_pct", 6, 30)]),
        ["stomp"] = new(5, [N("area_burst.radius", 120, 240), N("area_burst.damage", 30, 90)]),
        ["double_time"] = new(5, [C("mana", 6), C("cooldown", 8), C("buff.radius", 300), C("buff.duration", 4), N("stat:move_speed_pct", 20, 40)]),
        ["death_from_above"] = new(5, [N("stat:damage_all_pct", 20, 60), C("triggered.manaRefund", 10)]),
        ["pain_tolerance"] = new(5, [N("stat:damage_all_pct", 2, 6), N("stat:move_speed_pct", 1, 3)]),
        ["parachute"] = new(5, [N("toggle.drain", 2, 1), N("stat:damage_all_pct", 5, 25)]),
        ["trench_fighter"] = new(5, [N("stat:damage_weapon_pct", 8, 40), N("stat:attack_speed_pct", 3, 15), N("triggered.pctMaxLife", 2, 6)]),
        ["rocket_leap"] = new(5, [C("mana", 12), N("cooldown", 10, 6), N("area_burst.damage", 25, 70)]),
        ["human_rocket"] = new(1, [C("mana", 40), C("cooldown", 45), C("area_burst.damage", 200)]),
        // A.4 Command
        ["battle_orders"] = new(5, [C("mana", 20), C("cooldown", 30), N("aura.duration", 8, 12), C("aura.radius", 450), N("stat:damage_all_pct", 15, 35)]),
        ["basic_training"] = new(5, [N("stat:max_life_pct", 3, 15), N("stat:resist_all_pct", 2, 10)]),
        ["dig_in"] = new(5, [C("mana", 20), C("cooldown", 30), N("aura.duration", 8, 12), N("stat:damage_taken_pct", -15, -35)]),
        ["maggots"] = new(5, [C("mana", 8), C("cooldown", 12), C("threat.radius", 600), C("threat.duration", 4), N("stat:damage_all_pct", -10, -30)]),
        ["forward_march"] = new(5, [C("mana", 20), C("cooldown", 30), N("aura.duration", 8, 12), N("stat:life_leech_pct", 10, 30),
            C("stat:move_speed_pct", 15), N("stat:life_regen_hp", 1, 3)]),
        ["art_of_war"] = new(5, [N("stat:damage_vs_elite_pct", 4, 20)]),
        ["chain_of_command"] = new(5, [N("stat:order_duration_pct", 10, 50), N("stat:order_radius_pct", 8, 40), N("stat:order_cooldown_pct", -4, -20)]),
        ["rally_cry"] = new(5, [C("mana", 22), C("cooldown", 40), N("heal.pctMaxLife", 10, 30), C("heal.duration", 4)]),
        ["juggle_doctrine"] = new(5, [N("triggered.duration", 3, 5)]),
        ["supreme_commander"] = new(1, [C("mana", 60), C("cooldown", 120), C("buff.duration", 10)]),
        // A.5 Mercenary
        ["deep_pockets"] = new(7, [N("floor", 1, 25)]),
        ["storage_plan"] = new(5, [N("floor", 1, 21)]),
        ["second_wind"] = new(5, [N("floor", 3, 15), N("stat:life_regen_pct", 0.3, 1.5)]),
        ["leech"] = new(5, [N("floor", 3, 15), N("stat:life_leech_pct", 1, 5)]),
        ["clear_head"] = new(5, [N("floor", 3, 15), N("stat:mana_regen_pct", 8, 40)]),
        ["siphon"] = new(5, [N("floor", 3, 15), N("stat:mana_leech_pct", 0.5, 2.5)]),
        ["scavenger"] = new(5, [N("floor", 3, 15), N("stat:item_drop_pct", 6, 30), N("stat:australium_find_pct", 4, 20)]),
        ["haggler"] = new(5, [N("floor", 3, 15), N("stat:price_buy_pct", -4, -20), N("stat:price_sell_pct", 4, 20)]),
        ["field_rations"] = new(5, [N("floor", 3, 15), N("stat:potion_restore_pct", 8, 40), N("stat:potion_cooldown_pct", -6, -30)]),
    };

    /// <summary>Appendix A's "needs" column and synergy lines, as edges.</summary>
    private static HashSet<string> ExpectedEdges =>
    [
        "salvo needs patriot_missile at rank 1",
        "emp_charge needs patriot_missile at rank 1",
        "rocket_specialist needs marksmanship at rank 1",
        "rain_of_freedom needs salvo at rank 1",
        "killer_instinct needs marksmanship at rank 1",
        "fire_mission needs rain_of_freedom at rank 1",
        "screamin_eagle needs fire_mission at rank 1",
        "screamin_eagle needs killer_instinct at rank 1",
        "stomp needs jump_training at rank 1",
        "double_time needs boot_camp at rank 1",
        "death_from_above needs jump_training at rank 1",
        "pain_tolerance needs boot_camp at rank 1",
        "parachute needs jump_training at rank 3",
        "trench_fighter needs pain_tolerance at rank 1",
        "rocket_leap needs stomp at rank 1",
        "human_rocket needs rocket_leap at rank 1",
        "human_rocket needs death_from_above at rank 1",
        "dig_in needs basic_training at rank 1",
        "maggots needs basic_training at rank 1",
        "forward_march needs battle_orders at rank 1",
        "chain_of_command needs battle_orders at rank 1 (any 2 of 3)",
        "chain_of_command needs dig_in at rank 1 (any 2 of 3)",
        "chain_of_command needs forward_march at rank 1 (any 2 of 3)",
        "rally_cry needs dig_in at rank 1",
        "juggle_doctrine needs art_of_war at rank 1",
        "supreme_commander needs chain_of_command at rank 1",
        "supreme_commander needs forward_march at rank 1",
        "patriot_missile +4 damage_pct per rank of marksmanship",
        "rain_of_freedom +0.5 count per rank of jump_training",
        "fire_mission +5 damage_pct per rank of salvo",
        "death_from_above +4 potency_pct per rank of stomp",
        "death_from_above +10 bonusDamagePct per rank of trench_fighter",
        "stomp +8 radius_pct per rank of jump_training",
        "human_rocket +5 damage_pct per rank of rocket_leap",
        "battle_orders +3 potency_pct per rank of dig_in",
        "battle_orders +3 potency_pct per rank of forward_march",
        "dig_in +3 potency_pct per rank of battle_orders",
        "dig_in +3 potency_pct per rank of forward_march",
        "forward_march +3 potency_pct per rank of battle_orders",
        "forward_march +3 potency_pct per rank of dig_in",
        "juggle_doctrine +0.5 duration per rank of marksmanship",
    ];

    //-------------------------------------------------------------------------
    // Generated from the JSON
    //-------------------------------------------------------------------------

    private static DescentContent Shipped() => SkillContent.Shipped();

    private static TheoryData<string> Data(IEnumerable<string> rows)
    {
        var data = new TheoryData<string>();
        foreach (var r in rows)
            data.Add(r);
        return data;
    }

    public static TheoryData<string> SkillRows() => Data(Shipped().Skills.Rows.Select(s => s.Id));

    private static IEnumerable<(SkillDef From, Prerequisite P, string To)> PrerequisiteEdges(DescentContent c) =>
        c.Skills.Rows.SelectMany(s => s.Prerequisites.SelectMany(p => p.Skills.Select(to => (s, p, to))));

    private static string Edge(SkillDef from, Prerequisite p, string to) =>
        $"{from.Id} needs {to} at rank {p.MinRank}" + (p.Skills.Count > 1 ? $" (any {p.Count} of {p.Skills.Count})" : "");

    private static string Edge(SkillDef from, Synergy s) => $"{from.Id} +{s.PerRank} {s.Scales} per rank of {s.Skill}";

    public static TheoryData<string> PrerequisiteEdgeRows() => Data(PrerequisiteEdges(Shipped()).Select(e => Edge(e.From, e.P, e.To)));

    public static TheoryData<string> SynergyEdgeRows() =>
        Data(Shipped().Skills.Rows.SelectMany(s => s.Synergies.Select(y => Edge(s, y))));

    /// <summary>The number a key names in a skill's row, at a rank.</summary>
    private static double Read(SkillDef skill, string key, int rank)
    {
        if (key == "mana") return skill.ManaCost.At(rank);
        if (key == "cooldown") return skill.Cooldown.At(rank);
        if (key == "floor") return skill.LevelFloor!.At(rank);
        if (key.StartsWith("stat:", StringComparison.Ordinal))
        {
            var stat = key[5..];
            var curve = skill.Effect.SelectMany(e => e.Stats).FirstOrDefault(s => s.Stat == stat)
                        ?? throw new Xunit.Sdk.XunitException($"{skill.Id} grants no {stat}");
            return curve.Amount.At(rank);
        }
        var dot = key.IndexOf('.');
        string primitive = key[..dot], param = key[(dot + 1)..];
        var effect = skill.Effect.FirstOrDefault(e => e.Spec.Name == primitive && e.Params.ContainsKey(param))
                     ?? throw new Xunit.Sdk.XunitException($"{skill.Id} has no {primitive} that writes {param}");
        return effect.At(param, rank);
    }

    [Theory]
    [MemberData(nameof(SkillRows))]
    public void A_skill_has_appendix_As_rank_one_and_max_rank_numbers(string id)
    {
        var skill = Content.Skills[id];
        Assert.True(Expected.TryGetValue(id, out var expected), $"'{id}' has no row in Appendix A's expectations");

        Assert.Equal(expected!.MaxRank, skill.MaxRank);
        Assert.NotEmpty(expected.Numbers);
        foreach (var n in expected.Numbers)
        {
            Assert.True(Math.Abs(n.Rank1 - Read(skill, n.Key, 1)) < 1e-9, $"{id} {n.Key} at rank 1: {Read(skill, n.Key, 1)}, Appendix A says {n.Rank1}");
            Assert.True(Math.Abs(n.Max - Read(skill, n.Key, skill.MaxRank)) < 1e-9,
                $"{id} {n.Key} at rank {skill.MaxRank}: {Read(skill, n.Key, skill.MaxRank)}, Appendix A says {n.Max}");
        }
    }

    [Fact]
    public void Every_skill_row_has_an_expectation_and_every_expectation_a_row()
    {
        var rows = Content.Skills.Rows.Select(s => s.Id).Order().ToList();

        Assert.Equal(39, rows.Count);
        Assert.Equal(Expected.Keys.Order(), rows);
    }

    [Fact]
    public void The_edges_are_exactly_appendix_As()
    {
        var edges = PrerequisiteEdges(Content).Select(e => Edge(e.From, e.P, e.To))
            .Concat(Content.Skills.Rows.SelectMany(s => s.Synergies.Select(y => Edge(s, y)))).ToHashSet();

        Assert.Equal(ExpectedEdges.Order(), edges.Order());
    }

    //-------------------------------------------------------------------------
    // Each edge, in play
    //-------------------------------------------------------------------------

    /// <summary>A level 30 Soldier holding <paramref name="ids"/> at the given ranks, each bought with its prerequisites first.</summary>
    private static HeroSheet Holding(params (string Id, int Rank)[] ids)
    {
        var hero = SkillRulesTests.AtLevel("soldier", 30);
        foreach (var (id, rank) in ids)
            hero = Raise(hero, id, rank);
        return hero;
    }

    private static HeroSheet Raise(HeroSheet hero, string id, int rank)
    {
        var skill = Content.Skills[id];
        foreach (var p in skill.Prerequisites)
            foreach (var need in p.Skills.Take(p.Count))
                hero = Raise(hero, need, p.MinRank);
        while (SkillRules.RealRank(hero, id) < rank)
        {
            var r = Skills.Buy(hero, id);
            Assert.True(r.Ok, $"buying {id}: {r.Refusal} {r.Detail}");
            hero = r.Sheet;
        }
        return hero;
    }

    [Theory]
    [MemberData(nameof(PrerequisiteEdgeRows))]
    public void A_prerequisite_edge_gates_the_purchase(string edge)
    {
        Assert.Contains(edge, ExpectedEdges);
        var (from, p, to) = PrerequisiteEdges(Content).Single(e => Edge(e.From, e.P, e.To) == edge);
        var others = p.Skills.Where(s => s != to).Take(p.Count - 1).ToList();
        var rest = from.Prerequisites.Where(q => q != p).SelectMany(q => q.Skills.Take(q.Count).Select(s => (s, q.MinRank))).ToList();

        var hold = new List<(string, int)>(rest);
        hold.AddRange(others.Select(s => (s, p.MinRank)));
        if (p.MinRank > 1) hold.Add((to, p.MinRank - 1));
        var without = Holding([.. hold]);
        Assert.True(SkillRules.RealRank(without, to) < p.MinRank, "the control holds the prerequisite already");
        Assert.Equal(BuyRefusal.Prerequisite, Skills.Buy(without, from.Id).Refusal);
        Assert.True(Skills.Buy(Raise(without, to, p.MinRank), from.Id).Ok);
    }

    [Theory]
    [MemberData(nameof(SynergyEdgeRows))]
    public void A_synergy_edge_raises_what_it_scales_by_the_other_skills_ranks(string edge)
    {
        Assert.Contains(edge, ExpectedEdges);
        var (from, syn) = Content.Skills.Rows.SelectMany(s => s.Synergies.Select(y => (s, y))).Single(e => Edge(e.s, e.y) == edge);
        var other = Content.Skills[syn.Skill];

        var low = Holding((from.Id, 1));
        foreach (var p in other.Prerequisites)   // the other skill's own prerequisites first, so only its ranks differ
            foreach (var need in p.Skills.Take(p.Count))
                low = Raise(low, need, p.MinRank);
        int lowRank = SkillRules.RealRank(low, other.Id);
        var high = Raise(low, other.Id, other.MaxRank);
        Assert.True(other.MaxRank > lowRank, "the other skill cannot rise");

        double before = Measure(low, from.Id, syn), after = Measure(high, from.Id, syn);
        if (syn.Relative)
        {
            // Percent synergies on one thing add: the skill's other synergies of this kind stay as they were.
            double rest = from.Synergies.Where(y => y != syn && y.Scales == syn.Scales).Sum(y => y.PerRank * SkillRules.RealRank(low, y.Skill));
            Assert.Equal(rest, from.Synergies.Where(y => y != syn && y.Scales == syn.Scales).Sum(y => y.PerRank * SkillRules.RealRank(high, y.Skill)));
            Assert.Equal((1 + (rest + syn.PerRank * other.MaxRank) / 100) / (1 + (rest + syn.PerRank * lowRank) / 100), after / before, 9);
        }
        else
        {
            double raw = Read(from, from.Effect.First(e => e.Params.ContainsKey(syn.Param)).Spec.Name + "." + syn.Param, 1);
            double Whole(double v) => Primitives.Whole.Contains(syn.Param) ? Math.Floor(v) : v;
            Assert.Equal(Whole(raw + syn.PerRank * other.MaxRank) - Whole(raw + syn.PerRank * lowRank), after - before, 9);
        }
    }

    /// <summary>What a synergy scales, resolved: the first written parameter, or the first non-passive stat for potency.</summary>
    private static double Measure(HeroSheet hero, string id, Synergy syn)
    {
        var resolved = Skills.Resolve(hero, Skills.Stats(hero, SkillBonuses.None), SkillBonuses.None, id);
        var def = Content.Skills[id];
        if (syn.Scales == Synergy.Potency)
        {
            int i = def.Effect.ToList().FindIndex(e => e.Primitive != Primitive.Passive && e.Stats.Count > 0);
            return resolved.Effects[i].Stats[0].Amount;
        }
        int at = def.Effect.ToList().FindIndex(e => e.Params.ContainsKey(syn.Param));
        return resolved.Effects[at][syn.Param];
    }
}
