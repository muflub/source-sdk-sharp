using System.Collections.Immutable;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using TF2.Descent.Rules.Loot;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Rules.Skills;

namespace TF2.Descent.Rules.Economy.Model;

/// <summary>
/// Every rules object the model reads, built over one content, so a planted content (a
/// self-test's) drives the whole model and nothing else.
/// </summary>
public sealed class ModelRules
{
    public ModelRules(DescentContent content)
    {
        Content = content;
        Heroes = new HeroRules(content);
        Robots = new RobotRules(content);
        Temperaments = new TemperamentRules(content, Robots);
        Packs = new PackRules(content, Robots, Temperaments);
        Skills = new SkillRules(content, Heroes);
        Casting = new CastRules(Skills);
        Items = new ItemRules(content, Heroes);
        Repairs = new Repairs(content, Items.Codec);
        Pricing = new Pricing(content, Items.Roller);
        Crafting = new Crafting(content, Items.Roller, Items.Codec, Repairs, Pricing);
        Inventory = new InventoryRules(content, Items);
        Loot = new LootRules(content, Robots, Items);
        Experience = new ExperienceRules(content, Robots);
        Attacks = new RobotAttackRules(content);
        Bosses = new Bosses.BossRules(content);
    }

    public DescentContent Content { get; }
    public HeroRules Heroes { get; }
    public RobotRules Robots { get; }
    public TemperamentRules Temperaments { get; }
    public PackRules Packs { get; }
    public SkillRules Skills { get; }
    public CastRules Casting { get; }
    public ItemRules Items { get; }
    public Repairs Repairs { get; }
    public Pricing Pricing { get; }
    public Crafting Crafting { get; }
    public InventoryRules Inventory { get; }
    public LootRules Loot { get; }
    public ExperienceRules Experience { get; }
    public RobotAttackRules Attacks { get; }
    public Bosses.BossRules Bosses { get; }
    public ModelSettings Model => Content.EconomyModel;
}

/// <summary>A buff a hero stands in: its stats until a time, and its conditions.</summary>
internal sealed record ModelBuff(double Until, IReadOnlyList<Contribution> Stats, IReadOnlyList<string> Conditions);

/// <summary>A weapon as the hero fires it now: damage a hit before the target, seconds a hit (refire and reload), the hit share, splash share and expected crit factor.</summary>
internal sealed record WeaponView(string Slot, string Family, string DamageType, double PerHit, double Cycle, double Hit, double Splash, double Crit)
{
    public double Dps => PerHit * Hit * Crit / Cycle;
}

/// <summary>
/// A hero's numbers now: its stats (skills, active gear and buffs), its bonus ranks, its
/// weapons, its action multiplier and crit, and the triggered upgrades the model carries out.
/// </summary>
internal sealed record HeroView(
    HeroStats Stats,
    SkillBonuses Bonuses,
    IReadOnlyList<WeaponView> Weapons,
    double ActionMultiplier,
    double Crit,
    double CritChance,
    double ElitePct,
    double DamageTakenPct,
    bool CritImmune,
    double PainPctPer10,
    double AirbornePct,
    double HealPerTarget,
    int HealMaxTargets,
    double HealPerSecondCap,
    double StompDamage,
    double SelfBlastPct);

/// <summary>
/// One hero of the model: its sheet and build, what it wears and carries, its life, mana and
/// casting state, and its tallies. Mutable: the model owns it for one run.
/// </summary>
public sealed class ModelHero
{
    internal ModelHero(ModelBuild build, HeroSheet sheet)
    {
        Build = build;
        Sheet = sheet;
    }

    public ModelBuild Build { get; }
    public HeroSheet Sheet { get; internal set; }
    public Dictionary<string, ItemInstance> Equipped { get; } = new(StringComparer.Ordinal);
    public List<ItemInstance> Backpack { get; } = [];

    /// <summary>Potions carried (belt and backpack together), by base id.</summary>
    public List<string> Potions { get; } = [];

    public long Australium { get; internal set; }
    public double Life { get; internal set; }
    public double Mana { get; internal set; }
    public int Deaths { get; internal set; }
    public int PotionsDrunk { get; internal set; }

    internal CasterState Caster { get; set; } = new();
    internal Dictionary<string, ModelBuff> Buffs { get; } = new(StringComparer.Ordinal);
    internal double BusyUntil { get; set; }
    internal double AirUntil { get; set; } = double.NegativeInfinity;
    internal bool LandPending { get; set; }
    internal double NextJump { get; set; }
    internal double PotionReady { get; set; }
    internal bool Dead { get; set; }
    internal double WeaponUses { get; set; }
    internal double HitsTaken { get; set; }
    internal HeroView? View { get; set; }

    public IReadOnlyList<EquippedItem> Gear => [.. Equipped.Select(kv => new EquippedItem(kv.Key, kv.Value))];
}

/// <summary>
/// The hero's numbers from the real rules (<see cref="HeroRules.Stats"/>,
/// <see cref="Equipment.Resolve"/>, <see cref="SkillRules"/>), as the model reads them:
/// <list type="bullet">
/// <item>Weapon hit: TF stock damage × the item's <c>mult_dmg</c> × the hero's damage
/// multiplier for the weapon's kinds (which holds <c>damage_weapon_pct</c> once, as a weapon hit); refire × <c>mult_postfiredelay</c>
/// ÷ (1 + <c>attack_speed_pct</c>), plus reload × <c>mult_reload_time</c> ÷ (1 + <c>reload_speed_pct</c>)
/// (sustained fire: clip size changes nothing); a bullet weapon's miss share × the hero's spread
/// multiplier × <c>mult_spread_scale</c>; a blast weapon's splash share × (1 +
/// <c>blast_radius_pct</c>) × <c>mult_explosion_radius</c>. A weapon that is not active fires as
/// its Stock base (no TF attributes).</item>
/// <item>Crit: expected factor 1 + chance × (multiplier × (1 + <c>crit_damage_pct</c>) − 1);
/// <c>crit_boost</c> makes the chance 1.</item>
/// <item>Gear's <c>skill_ranks_all</c> becomes bonus ranks to all class skills.</item>
/// </list>
/// </summary>
internal sealed class ModelHeroes
{
    private readonly ModelRules _r;

    public ModelHeroes(ModelRules rules) => _r = rules;

    private ModelSettings M => _r.Model;

    public HeroView View(ModelHero hero, double now)
    {
        var buffs = hero.Buffs.Values.Where(b => b.Until > now).ToList();
        var buffStats = buffs.SelectMany(b => b.Stats).ToList();
        var conditions = buffs.SelectMany(b => b.Conditions).ToHashSet(StringComparer.Ordinal);

        var bonuses = SkillBonuses.None;
        var skills = _r.Skills.Contributions(hero.Sheet, bonuses);
        var gear = hero.Gear;
        var eq = _r.Items.Equipment.Resolve(hero.Sheet, gear, [.. skills, .. buffStats]);
        int bonusAll = (int)Math.Round(eq.Contributions.Where(c => c.Source.Counts && c.Stat == "skill_ranks_all").Sum(c => c.Amount));
        if (bonusAll != 0)
        {
            bonuses = SkillBonuses.None.WithAll(bonusAll);
            skills = _r.Skills.Contributions(hero.Sheet, bonuses);
            eq = _r.Items.Equipment.Resolve(hero.Sheet, gear, [.. skills, .. buffStats]);
        }
        var all = new List<Contribution>([.. skills, .. buffStats, .. eq.Contributions]);
        var stats = _r.Heroes.Stats(hero.Sheet, all);

        double critChance = conditions.Contains("crit_boost") ? 1 : stats.CritChance;
        double crit = 1 + critChance * (M.CritMultiplier * (1 + stats.Total("crit_damage_pct") / 100) - 1);

        var weapons = new List<WeaponView>();
        foreach (var (slot, item) in hero.Equipped)
        {
            var b = _r.Content.ItemBases[item.BaseId];
            if (b.Kind != ItemKinds.Weapon || b.Family is null || M.Weapon(b.Family) is not { } w) continue;
            bool active = eq.State(slot).State == SourceState.Active;
            double Tf(string attribute, double none) =>
                active ? eq.TFAttributes.Where(a => a.Slot == slot && a.Attribute == attribute).Aggregate(none, (p, a) => p * a.Value) : none;
            var familyStats = _r.Skills.WeaponContributions(hero.Sheet, bonuses, b.Family);
            var s = familyStats.Count == 0 ? stats : _r.Heroes.Stats(hero.Sheet, [.. all, .. familyStats]);
            double mult = s.DamageMultiplier([.. w.Kinds]);
            double perHit = w.Damage * Tf("mult_dmg", 1) * mult;
            double refire = w.Interval * Tf("mult_postfiredelay", 1) / Math.Max(0.1, 1 + s.Total("attack_speed_pct") / 100);
            double reload = w.Reload * Tf("mult_reload_time", 1) / Math.Max(0.1, 1 + s.Total("reload_speed_pct") / 100);
            double miss = 1 - w.HitPct / 100;
            if (w.Kinds.Contains("bullet")) miss *= s.SpreadMultiplier * Tf("mult_spread_scale", 1);
            double splash = w.Splash ? Math.Min(1, M.SplashShare * (1 + s.Total("blast_radius_pct") / 100) * Tf("mult_explosion_radius", 1)) : 0;
            weapons.Add(new WeaponView(slot, b.Family, _r.Content.Families[b.Family].DamageType, perHit, refire + reload,
                Math.Clamp(1 - miss, 0, 1), splash, crit));
        }

        // The triggered upgrades the model carries out, at the hero's ranks.
        double pain = 0, airborne = 0, heal = 0, healCap = 0, stomp = 0;
        int healTargets = 0;
        foreach (var (id, _) in hero.Sheet.SkillRanks.Where(kv => kv.Value > 0))
        {
            if (_r.Skills.Find(id) is not { } def || !_r.Skills.OpenTo(hero.Sheet.ClassId, def)) continue;
            var resolved = _r.Skills.Resolve(hero.Sheet, stats, bonuses, id);
            foreach (var e in resolved.Effects)
            {
                if (e.Trigger == "per_10pct_health_missing")
                    pain += e.Stats.Where(x => x.Stat == "damage_all_pct").Sum(x => x.Amount);
                else if (e.Trigger == "blast_jumping")
                    airborne += e.Stats.Where(x => x.Stat == "damage_all_pct").Sum(x => x.Amount);
                else if (e.Trigger == "on_hit" && e.Params.TryGetValue("heal", out var h))
                {
                    heal += h;
                    healTargets = Math.Max(healTargets, (int)e.Params.GetValueOrDefault("maxTargets"));
                    healCap += e.Params.GetValueOrDefault("perSecondCap");
                }
                else if (e.Trigger == "blast_jump_landing" && e.Params.TryGetValue("damage", out var d))
                    stomp += d;
            }
        }

        return new HeroView(stats, bonuses, weapons, Math.Max(0, stats.DamageMultiplier("action")), crit, critChance,
            stats.Total("damage_vs_elite_pct"), stats.Total("damage_taken_pct"), conditions.Contains("crit_immune"),
            pain, airborne, heal, healTargets, healCap, stomp, stats.Total("self_blast_damage_pct"));
    }

    /// <summary>
    /// The model's gear score (equip policy and the sanity curve): a steady-state damage rate
    /// against an unresisting robot (the best weapon, splash on two more, plus the rotation's
    /// actions at the rate mana, cooldowns and cast time allow, in priority order) times the
    /// effective life against a robot of <paramref name="attackerLevel"/> (max life ÷ what armour
    /// and the mean resistance let through).
    /// </summary>
    public double Power(ModelHero hero, int attackerLevel)
    {
        var v = View(hero, 0);
        double weapon = v.Weapons.Count == 0 ? 0 : v.Weapons.Max(w => w.Dps * (1 + w.Splash * M.SplashTargets * 0.5));
        double actions = 0, mana = v.Stats.ManaRegenPerSecond, time = 1;
        foreach (var id in hero.Build.Rotation)
        {
            if (SkillRules.RealRank(hero.Sheet, id) <= 0) continue;
            var s = _r.Skills.Resolve(hero.Sheet, v.Stats, v.Bonuses, id);
            double perCast = ActionDamage(s, v) * v.Crit;
            if (perCast <= 0) continue;
            double cost = _r.Casting.ManaCost(s, v.Stats, Stages(s));
            double cast = CastTime(s);
            double rate = 1 / Math.Max(Math.Max(s.Cooldown, cast), 0.1);
            if (cost > 0) rate = Math.Min(rate, mana / cost);
            rate = Math.Min(rate, time / cast);
            mana -= rate * cost;
            time -= rate * cast;
            actions += rate * perCast;
        }
        double offence = weapon * Math.Max(0, time) + actions;
        var stats = v.Stats;
        double through = (1 - _r.Heroes.ArmourReduction(stats.Armour, attackerLevel))
                         * (1 - _r.Content.DamageTypes.Rows.Average(t => stats.Resist(t.Id)));
        return offence * stats.MaxLife / Math.Max(0.01, through);
    }

    /// <summary>A resolved action's damage a cast, before the target: its damaging effects' damage × count × charge stages × the action multiplier.</summary>
    public static double ActionDamage(ResolvedSkill s, HeroView v)
    {
        double total = 0;
        foreach (var e in s.Effects)
        {
            if (!e.Params.TryGetValue("damage", out var d) || e.Trigger is "blast_jump_landing") continue;
            total += d * Math.Max(1, e.Params.GetValueOrDefault("count", 1)) * Stages(s);
        }
        return total * v.ActionMultiplier;
    }

    /// <summary>The stages a cast readies: a charge's full stages, else 1.</summary>
    public static int Stages(ResolvedSkill s) =>
        (int)Math.Max(1, s.Effects.Where(e => e.Primitive == Primitive.Charge).Select(e => e.Params.GetValueOrDefault("stages", 1)).DefaultIfEmpty(1).Max());

    /// <summary>How long a cast keeps the hero from its weapon: the model's cast time plus a charge's time and release.</summary>
    public double CastTime(ResolvedSkill s) =>
        M.CastSeconds + s.Effects.Where(e => e.Primitive == Primitive.Charge)
            .Sum(e => e.Params.GetValueOrDefault("time") + e.Params.GetValueOrDefault("releaseTime"));
}
