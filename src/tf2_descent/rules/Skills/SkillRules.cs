using System.Collections.Immutable;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;

namespace TF2.Descent.Rules.Skills;

/// <summary>Why buying a rank was refused.</summary>
public enum BuyRefusal
{
    UnknownSkill,
    /// <summary>Another class's tree: a hero buys from its own class's tabs and the shared tab only.</summary>
    OtherClass,
    /// <summary>The hero is below the tier's level, or the rank's level floor (shared tab).</summary>
    BelowLevel,
    /// <summary>A prerequisite is not held at its rank.</summary>
    Prerequisite,
    AtMaxRank,
    NoPoints,
}

/// <summary>A buy: the sheet after it (unchanged when refused), and why it was refused.</summary>
public sealed record BuyResult(HeroSheet Sheet, BuyRefusal? Refusal, string? Detail = null)
{
    public bool Ok => Refusal is null;
}

/// <summary>
/// Bonus ranks from gear (A.1: "+1 to a skill / tab / all Soldier skills"): per skill id, per
/// tab id, and to every skill of the hero's class. They add to a skill the hero has a real
/// point in, up to <see cref="SkillSettings.BonusRankCap"/> past max rank; they never unlock
/// a skill. They reach the class's tabs only: the shared tab's ranks are capacity and
/// economy, bought with points alone.
/// </summary>
public sealed record SkillBonuses(
    ImmutableSortedDictionary<string, int> Skills,
    ImmutableSortedDictionary<string, int> Tabs,
    int All)
{
    public static SkillBonuses None => new(
        ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal),
        ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal), 0);

    public SkillBonuses WithSkill(string id, int ranks) => this with { Skills = Skills.SetItem(id, Get(Skills, id) + ranks) };

    public SkillBonuses WithTab(string id, int ranks) => this with { Tabs = Tabs.SetItem(id, Get(Tabs, id) + ranks) };

    public SkillBonuses WithAll(int ranks) => this with { All = All + ranks };

    private static int Get(ImmutableSortedDictionary<string, int> d, string id) => d.TryGetValue(id, out var v) ? v : 0;

    public int For(SkillDef skill) => Get(Skills, skill.Id) + Get(Tabs, skill.Tab) + All;
}

/// <summary>One primitive at a rank: every parameter's number, the stats and conditions it grants.</summary>
public sealed record ResolvedEffect(
    Primitive Primitive,
    string? Trigger,
    string? Target,
    string? Family,
    IReadOnlyDictionary<string, double> Params,
    IReadOnlyList<StatAmount> Stats,
    IReadOnlyList<string> Conditions,
    string? Capacity)
{
    public double this[string param] => Params[param];
}

/// <summary>
/// A skill or spell as the binder fires it: the rank it acts at (real plus bonus), what one
/// cast costs and how long until the next, and each effect with its numbers resolved
/// (synergies, the action damage formula, Chain of Command on Orders).
/// </summary>
public sealed record ResolvedSkill(
    string Id,
    int RealRank,
    int Rank,
    double ManaCost,
    double ManaPerStage,
    double Cooldown,
    IReadOnlyList<ResolvedEffect> Effects)
{
    /// <summary>The first effect that deals damage, or null.</summary>
    public ResolvedEffect? Damaging => Effects.FirstOrDefault(e => e.Params.ContainsKey("damage"));
}

/// <summary>
/// The skill tree's rules (plan_tf2_rpg 2e, Appendix A; plan_descent_logic RU6), pure over
/// the content: buying ranks, bonus ranks, resolving a skill's numbers at a rank (the action
/// damage formula, synergies), which upgrades apply to a weapon family, and the passive
/// upgrades' contributions to the hero sheet. Every number is the content's.
/// </summary>
public sealed class SkillRules
{
    private readonly DescentContent _content;
    private readonly HeroRules _heroes;

    public SkillRules(DescentContent content, HeroRules heroes)
    {
        _content = content;
        _heroes = heroes;
    }

    private SkillSettings S => _content.SkillSettings;

    public HeroRules Heroes => _heroes;

    public DescentContent Content => _content;

    public SkillDef? Find(string id) => _content.Skills.TryGet(id, out var s) ? s : null;

    public SkillDef Skill(string id) => Find(id) ?? throw new ArgumentException($"'{id}' is not a skill", nameof(id));

    /// <summary>Whether a hero of <paramref name="classId"/> may hold <paramref name="skill"/>: its own class's, or the shared tab's.</summary>
    public bool OpenTo(string classId, SkillDef skill) => skill.Class == classId || skill.Class == S.SharedClass;

    /// <summary>The skills a hero of <paramref name="classId"/> may buy, in table order.</summary>
    public IReadOnlyList<SkillDef> TreeOf(string classId) => [.. _content.Skills.Rows.Where(s => OpenTo(classId, s))];

    /// <summary>The ranks bought with points.</summary>
    public static int RealRank(HeroSheet sheet, string id) => sheet.SkillRanks.TryGetValue(id, out var r) ? r : 0;

    /// <summary>The hero level rank <paramref name="rank"/> of <paramref name="skill"/> needs: its tier's level, or the shared tab's per-rank floor.</summary>
    public int LevelFor(SkillDef skill, int rank) =>
        skill.LevelFloor is { } floor ? (int)Math.Ceiling(floor.At(rank)) : S.TierLevel(skill.Tier);

    //-------------------------------------------------------------------------
    // Buying
    //-------------------------------------------------------------------------

    /// <summary>
    /// Buys one rank of <paramref name="skillId"/> with a skill point: refused for another
    /// class's tree, below the tier's level (or the rank's floor), without every prerequisite
    /// at its rank (real ranks), at max rank, or with no point.
    /// </summary>
    public BuyResult Buy(HeroSheet sheet, string skillId)
    {
        if (Find(skillId) is not { } skill) return new(sheet, BuyRefusal.UnknownSkill, $"'{skillId}' is not a skill");
        if (!OpenTo(sheet.ClassId, skill))
            return new(sheet, BuyRefusal.OtherClass, $"'{skill.Id}' is a {skill.Class} skill");
        int rank = RealRank(sheet, skillId);
        if (rank >= skill.MaxRank) return new(sheet, BuyRefusal.AtMaxRank, $"'{skill.Id}' is at its max rank {skill.MaxRank}");
        int level = LevelFor(skill, rank + 1);
        if (sheet.Level < level) return new(sheet, BuyRefusal.BelowLevel, $"rank {rank + 1} of '{skill.Id}' needs hero level {level}");
        foreach (var p in skill.Prerequisites)
            if (!p.MetBy(id => RealRank(sheet, id)))
                return new(sheet, BuyRefusal.Prerequisite,
                    $"'{skill.Id}' needs {p.Count} of {string.Join(", ", p.Skills)} at rank {p.MinRank}");
        if (sheet.SkillPoints <= 0) return new(sheet, BuyRefusal.NoPoints, "no skill point to spend");
        return new(sheet with
        {
            SkillRanks = sheet.SkillRanks.SetItem(skillId, rank + 1),
            SkillPoints = sheet.SkillPoints - 1,
        }, null);
    }

    //-------------------------------------------------------------------------
    // Ranks
    //-------------------------------------------------------------------------

    /// <summary>
    /// The rank a skill acts at: real ranks plus gear's bonus ranks, never past max rank + the
    /// bonus cap, and 0 while no real point is in it (A.1). The shared tab takes no bonus.
    /// </summary>
    public int EffectiveRank(HeroSheet sheet, SkillBonuses bonuses, string skillId)
    {
        var skill = Skill(skillId);
        int real = RealRank(sheet, skillId);
        if (real <= 0) return 0;
        if (skill.Class == S.SharedClass) return real;
        return Math.Min(real + Math.Max(0, bonuses.For(skill)), skill.MaxRank + S.BonusRankCap);
    }

    //-------------------------------------------------------------------------
    // Damage
    //-------------------------------------------------------------------------

    /// <summary>
    /// The action damage formula (2e, A.1): base · (1 + perHeroLevel · heroLevel) ·
    /// (1 + attribute / divisor), 0.04 and 50 as shipped. It takes no weapon: an action deals
    /// the same with anything or nothing in hand (R17).
    /// </summary>
    public double ActionDamage(double baseDamage, int heroLevel, double attributeNow) =>
        baseDamage * (1 + S.DamagePerHeroLevel * heroLevel) * (1 + attributeNow / S.DamageAttributeDivisor);

    /// <summary>The attribute a skill's damage scales with: its tab's, or the tab's override for its damage type (A.1).</summary>
    public string? DamageAttribute(SkillDef skill) =>
        _content.SkillTabs.TryGet(skill.Tab, out var tab) ? tab.AttributeFor(skill.DamageType) : null;

    /// <summary>
    /// The damage of <paramref name="skillId"/>'s first damaging effect for this hero: base at
    /// its effective rank, raised by its synergies, then the formula with the tab attribute's
    /// Now. Inputs are the sheet, its stats and gear's bonus ranks: no weapon.
    /// </summary>
    public double Damage(HeroSheet sheet, HeroStats stats, SkillBonuses bonuses, string skillId) =>
        Resolve(sheet, stats, bonuses, skillId).Damaging?["damage"] ?? 0;

    //-------------------------------------------------------------------------
    // Resolving
    //-------------------------------------------------------------------------

    /// <summary>A skill at the hero's effective rank, every number resolved; throws when the hero has no real rank in it.</summary>
    public ResolvedSkill Resolve(HeroSheet sheet, HeroStats stats, SkillBonuses bonuses, string skillId)
    {
        var skill = Skill(skillId);
        int rank = EffectiveRank(sheet, bonuses, skillId);
        if (rank <= 0) throw new InvalidOperationException($"the hero has no rank in '{skillId}'");
        bool order = skill.HasTag("order");
        return ResolveAt(skill.Id, skill.Effect, skill.Synergies, skill.ManaCost, skill.Cooldown, RealRank(sheet, skillId), rank,
            sheet, stats, DamageAttribute(skill),
            durationPct: order ? stats.Total(S.OrderDurationStat) : 0,
            radiusPct: order ? stats.Total(S.OrderRadiusStat) : 0,
            cooldownPct: order ? stats.Total(S.OrderCooldownStat) : 0);
    }

    /// <summary>
    /// Resolves effects at a rank (shared with spells): each parameter at the rank (or its
    /// default), raised by synergies where the row writes it (<c>_pct</c> by percent, others
    /// added), whole parameters floored, <c>damage</c> through the action formula, duration and
    /// radius by the Orders' stats, and stat amounts of non-passive effects by potency.
    /// </summary>
    internal ResolvedSkill ResolveAt(string id, IReadOnlyList<EffectDef> effects, IReadOnlyList<Synergy> synergies,
        RankCurve mana, RankCurve cooldown, int realRank, int rank, HeroSheet sheet, HeroStats stats, string? attribute,
        double durationPct = 0, double radiusPct = 0, double cooldownPct = 0)
    {
        double Syn(Synergy s) => s.PerRank * RealRank(sheet, s.Skill);
        double potency = synergies.Where(s => s.Scales == Synergy.Potency).Sum(Syn);
        double attributeNow = attribute is null ? 0 : stats.Now(attribute);
        double manaPerStage = 0;

        var resolved = new List<ResolvedEffect>();
        foreach (var e in effects)
        {
            var ps = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var p in e.Spec.Required.Concat(e.Spec.Optional.Keys))
            {
                double v = e.At(p, rank);
                if (e.Params.ContainsKey(p))
                {
                    double pct = synergies.Where(s => s.Relative && s.Param == p).Sum(Syn);
                    double add = synergies.Where(s => !s.Relative && s.Scales != Synergy.Potency && s.Param == p).Sum(Syn);
                    v = v * (1 + pct / 100) + add;
                }
                if (Primitives.Whole.Contains(p)) v = Math.Floor(v);
                if (p == "damage") v = ActionDamage(v, sheet.Level, attributeNow);
                if (p == "duration") v *= 1 + durationPct / 100;
                if (p == "radius") v *= 1 + radiusPct / 100;
                ps[p] = v;
            }
            if (e.Primitive == Primitive.Charge) manaPerStage += ps["manaPerStage"];
            double scale = e.Primitive == Primitive.Passive ? 1 : 1 + potency / 100;
            var st = e.Stats.Select(s => new StatAmount(s.Stat, s.Amount.At(rank) * scale)).ToList();
            resolved.Add(new ResolvedEffect(e.Primitive, e.Trigger, e.Target, e.Family, ps, st, e.Conditions, e.Capacity));
        }
        double cd = Math.Max(0, cooldown.At(rank) * (1 + cooldownPct / 100));
        return new ResolvedSkill(id, realRank, rank, mana.At(rank), manaPerStage, cd, resolved);
    }

    //-------------------------------------------------------------------------
    // Weapon families and the hero sheet
    //-------------------------------------------------------------------------

    /// <summary>
    /// Whether an upgrade applies to a weapon of <paramref name="familyId"/>: one scoped to all
    /// weapons applies to every family, one scoped to a family to that family and no other. A
    /// cast skill never applies to a weapon.
    /// </summary>
    public static bool Applies(SkillDef skill, string familyId) =>
        skill.Kind == SkillKind.Upgrade && (skill.Family is null || skill.Family == familyId);

    /// <summary>
    /// The passive stats of every skill held whose effect is not limited to a family: what the
    /// hero sheet adds to Now (<see cref="HeroRules.Stats"/>), as skill contributions. Basic
    /// Training's max life, Leech's leech and Second Wind's regeneration come in here.
    /// </summary>
    public IReadOnlyList<Contribution> Contributions(HeroSheet sheet, SkillBonuses bonuses) =>
        PassiveContributions(sheet, bonuses, skill => skill.Family is null);

    /// <summary>
    /// The passive stats of the upgrades scoped to <paramref name="familyId"/>: what a held
    /// weapon of that family adds (Rocket Specialist with a rocket launcher in hand). Empty
    /// for any family no held upgrade names; all-weapons upgrades are already in
    /// <see cref="Contributions"/>.
    /// </summary>
    public IReadOnlyList<Contribution> WeaponContributions(HeroSheet sheet, SkillBonuses bonuses, string familyId) =>
        PassiveContributions(sheet, bonuses, skill => skill.Family is not null && Applies(skill, familyId));

    private List<Contribution> PassiveContributions(HeroSheet sheet, SkillBonuses bonuses, Func<SkillDef, bool> which)
    {
        var list = new List<Contribution>();
        foreach (var (id, real) in sheet.SkillRanks)
        {
            if (real <= 0 || Find(id) is not { } skill || !OpenTo(sheet.ClassId, skill) || !which(skill)) continue;
            int rank = EffectiveRank(sheet, bonuses, id);
            var source = new ContributionSource(SourceKind.Skill, id);
            foreach (var e in skill.Effect.Where(e => e.Primitive == Primitive.Passive && e.Trigger is null))
                foreach (var s in e.Stats)
                    list.Add(new Contribution(s.Stat, s.Amount.At(rank), source));
        }
        return list;
    }

    /// <summary>The hero's stats with its skills' passive contributions and any others (gear, buffs).</summary>
    public HeroStats Stats(HeroSheet sheet, SkillBonuses bonuses, IReadOnlyList<Contribution>? others = null) =>
        _heroes.Stats(sheet, [.. Contributions(sheet, bonuses), .. others ?? []]);

    /// <summary>A resolved effect's stats as contributions from a buff (an aura or buff the hero stands in).</summary>
    public static IReadOnlyList<Contribution> AsBuff(string sourceId, ResolvedEffect effect) =>
        [.. effect.Stats.Select(s => new Contribution(s.Stat, s.Amount, new ContributionSource(SourceKind.Buff, sourceId)))];

    /// <summary>Whether the hero holds a real rank in a skill whose passive carries <paramref name="condition"/>.</summary>
    public bool HoldsPassive(HeroSheet sheet, string condition) =>
        sheet.SkillRanks.Any(kv => kv.Value > 0 && Find(kv.Key) is { } s && OpenTo(sheet.ClassId, s)
            && s.Effect.Any(e => e.Primitive == Primitive.Passive && e.Conditions.Contains(condition)));
}
