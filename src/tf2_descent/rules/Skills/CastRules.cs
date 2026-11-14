using System.Collections.Immutable;
using TF2.Descent.Rules.Hero;

namespace TF2.Descent.Rules.Skills;

/// <summary>Why a cast was refused.</summary>
public enum CastRefusal
{
    UnknownSkill,
    /// <summary>The hero has no real rank in it (bonus ranks never unlock a skill).</summary>
    NotLearned,
    /// <summary>An upgrade is passive: nothing to cast.</summary>
    NotCast,
    NoMana,
    OnCooldown,
    /// <summary>The skill requires the hero to be BlastJumping (Rain of Freedom).</summary>
    NeedsBlastJumping,
    /// <summary>A spell whose Smarts requirement at its rank is not met by Now (R27): inactive, not forgotten.</summary>
    Inactive,
}

/// <summary>
/// What a caster carries between casts: mana, the clock, when each skill is ready again,
/// the Orders in force (id to the time each ends), the toggles that are on, and whether the
/// hero is BlastJumping.
/// </summary>
public sealed record CasterState
{
    public double Mana { get; init; }
    public double Now { get; init; }
    public ImmutableSortedDictionary<string, double> ReadyAt { get; init; } = ImmutableSortedDictionary<string, double>.Empty.WithComparers(StringComparer.Ordinal);
    public ImmutableSortedDictionary<string, double> Orders { get; init; } = ImmutableSortedDictionary<string, double>.Empty.WithComparers(StringComparer.Ordinal);
    public ImmutableSortedSet<string> Toggles { get; init; } = ImmutableSortedSet.Create<string>(StringComparer.Ordinal);
    public bool BlastJumping { get; init; }

    /// <summary>The Orders in force now.</summary>
    public IReadOnlyList<string> ActiveOrders => [.. Orders.Where(o => o.Value > Now).Select(o => o.Key)];

    /// <summary>Seconds until <paramref name="id"/> is ready (0 when it is).</summary>
    public double CooldownLeft(string id) => ReadyAt.TryGetValue(id, out var t) ? Math.Max(0, t - Now) : 0;

    /// <summary>The state <paramref name="seconds"/> later, nothing else changed.</summary>
    public CasterState After(double seconds) => this with { Now = Now + seconds };
}

/// <summary>A cast: the caster after it (unchanged when refused), the skill as resolved, the mana spent, and the Orders it ended and began.</summary>
public sealed record CastResult(
    CasterState State,
    CastRefusal? Refusal,
    ResolvedSkill? Skill,
    double ManaSpent,
    IReadOnlyList<string> OrdersEnded,
    IReadOnlyList<string> OrdersBegun)
{
    public bool Ok => Refusal is null;

    internal static CastResult Refused(CasterState state, CastRefusal why) => new(state, why, null, 0, [], []);
}

/// <summary>
/// Casting, as pure rules (plan_tf2_rpg 2e, A.4; RU6): mana spent and refused when short,
/// cooldowns, the BlastJumping requirement, toggles and their drain, and one Order at a time
/// until a skill whose passive lets Orders stack (Supreme Commander).
/// </summary>
public sealed class CastRules
{
    private readonly SkillRules _skills;

    public CastRules(SkillRules skills) => _skills = skills;

    /// <summary>
    /// What a cast of <paramref name="skill"/> costs: (mana cost + mana a stage x stages), raised
    /// or cut by the hero's mana-cost stat (Screamin' Eagle's half, gear's −%), never below 0.
    /// </summary>
    public double ManaCost(ResolvedSkill skill, HeroStats stats, int stages) =>
        Math.Max(0, (skill.ManaCost + skill.ManaPerStage * stages) * (1 + stats.Total(_skills.Content.SkillSettings.ManaCostStat) / 100));

    /// <summary>
    /// Casts <paramref name="skillId"/>. Refused, with the caster unchanged, for an unknown
    /// skill, one with no real rank, an upgrade, a cooldown still running, a BlastJumping
    /// requirement unmet, or too little mana. A toggle that is on turns off for nothing. A
    /// charge spends its mana per stage readied (<paramref name="stages"/>, at most the rank's;
    /// all of them when null). An Order ends the Orders in force unless the hero holds a
    /// passive that lets them stack; a cast that issues every Order begins each one held.
    /// </summary>
    public CastResult Cast(HeroSheet sheet, HeroStats stats, SkillBonuses bonuses, CasterState state, string skillId, int? stages = null)
    {
        if (_skills.Find(skillId) is not { } skill) return CastResult.Refused(state, CastRefusal.UnknownSkill);
        if (!skill.IsCast) return CastResult.Refused(state, CastRefusal.NotCast);
        if (_skills.EffectiveRank(sheet, bonuses, skillId) <= 0) return CastResult.Refused(state, CastRefusal.NotLearned);

        if (skill.Kind == SkillKind.Toggle && state.Toggles.Contains(skillId))
            return new CastResult(state with { Toggles = state.Toggles.Remove(skillId) }, null, null, 0, [], []);
        if (state.CooldownLeft(skillId) > 0) return CastResult.Refused(state, CastRefusal.OnCooldown);
        if (skill.Requires == "blast_jumping" && !state.BlastJumping) return CastResult.Refused(state, CastRefusal.NeedsBlastJumping);

        var resolved = _skills.Resolve(sheet, stats, bonuses, skillId);
        return Spend(sheet, stats, state, skill, resolved, stages);
    }

    private CastResult Spend(HeroSheet sheet, HeroStats stats, CasterState state, SkillDef skill, ResolvedSkill resolved, int? stages)
    {
        int full = (int)resolved.Effects.Where(e => e.Primitive == Primitive.Charge).Select(e => e["stages"]).DefaultIfEmpty(0).Max();
        int readied = Math.Clamp(stages ?? full, 0, full);
        double cost = ManaCost(resolved, stats, readied);
        if (state.Mana < cost) return CastResult.Refused(state, CastRefusal.NoMana);

        var after = state with { Mana = state.Mana - cost };
        if (resolved.Cooldown > 0)
            after = after with { ReadyAt = after.ReadyAt.SetItem(skill.Id, state.Now + resolved.Cooldown) };
        if (skill.Kind == SkillKind.Toggle)
            after = after with { Toggles = after.Toggles.Add(skill.Id) };

        var ended = new List<string>();
        var begun = new List<string>();
        if (skill.HasTag("order"))
        {
            if (!_skills.HoldsPassive(sheet, "orders_stack"))
                ended.AddRange(state.ActiveOrders.Where(o => o != skill.Id));
            after = after with { Orders = after.Orders.RemoveRange(ended).SetItem(skill.Id, state.Now + OrderDuration(resolved)) };
            begun.Add(skill.Id);
        }
        foreach (var e in resolved.Effects.Where(e => e.Conditions.Contains("issue_all_orders")))
            foreach (var order in _skills.Content.Skills.Rows.Where(s => s.HasTag("order") && SkillRules.RealRank(sheet, s.Id) > 0))
            {
                after = after with { Orders = after.Orders.SetItem(order.Id, state.Now + e["duration"]) };
                if (!begun.Contains(order.Id)) begun.Add(order.Id);
            }
        return new CastResult(after, null, resolved, cost, ended, begun);
    }

    /// <summary>How long an Order lasts: its longest timed effect.</summary>
    private static double OrderDuration(ResolvedSkill order) =>
        order.Effects.Where(e => e.Params.ContainsKey("duration")).Select(e => e["duration"]).DefaultIfEmpty(0).Max();

    /// <summary>
    /// <paramref name="seconds"/> of every toggle that is on: each drains its mana a second.
    /// When the mana cannot pay it all, the mana runs out and every toggle turns off.
    /// </summary>
    public CasterState Drain(HeroSheet sheet, HeroStats stats, SkillBonuses bonuses, CasterState state, double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        double drain = 0;
        foreach (var id in state.Toggles)
            if (_skills.Find(id) is not null && _skills.EffectiveRank(sheet, bonuses, id) > 0)
                drain += _skills.Resolve(sheet, stats, bonuses, id).Effects.Where(e => e.Primitive == Primitive.Toggle).Sum(e => e["drain"]);
        double cost = drain * seconds;
        if (cost <= state.Mana) return state with { Mana = state.Mana - cost, Now = state.Now + seconds };
        return state with { Mana = 0, Toggles = state.Toggles.Clear(), Now = state.Now + seconds };
    }
}
