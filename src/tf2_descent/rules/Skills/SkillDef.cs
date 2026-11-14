namespace TF2.Descent.Rules.Skills;

/// <summary>
/// A skill's kind (plan_tf2_rpg 2e, R17). An <see cref="Action"/> costs mana and brings its
/// own damage; an <see cref="Upgrade"/> is a passive scoped to all weapons or one family; a
/// <see cref="Toggle"/> drains mana while on; a <see cref="Capstone"/> is a tab's one-rank
/// tier-6 skill, cast for mana as an action is.
/// </summary>
public enum SkillKind
{
    Action,
    Upgrade,
    Toggle,
    Capstone,
}

/// <summary>"+<see cref="Amount"/>(rank) of <see cref="Stat"/>": a stat an effect grants, a row of stats.json.</summary>
public sealed record StatCurve(string Stat, RankCurve Amount);

/// <summary>
/// "Needs <see cref="Count"/> of <see cref="Skills"/> at real rank <see cref="MinRank"/> or more."
/// One skill at rank 1 is the usual case; Parachute needs Jump Training 3; Chain of Command
/// needs any two of the three Orders.
/// </summary>
public sealed record Prerequisite(IReadOnlyList<string> Skills, int Count, int MinRank)
{
    public bool MetBy(Func<string, int> realRank) => Skills.Count(s => realRank(s) >= MinRank) >= Count;
}

/// <summary>
/// Diablo II's synergy: this skill gains <see cref="PerRank"/> per real rank of
/// <see cref="Skill"/>, applied to what <see cref="Scales"/> names. A name ending in
/// <c>_pct</c> raises that parameter by percent (<c>damage_pct</c>, <c>radius_pct</c>;
/// <c>potency_pct</c> is every stat amount the skill grants); any other name adds to it
/// (<c>count</c>, <c>duration</c>).
/// </summary>
public sealed record Synergy(string Skill, double PerRank, string Scales)
{
    /// <summary>What a synergy names to raise every stat amount of the skill's non-passive effects by percent.</summary>
    public const string Potency = "potency_pct";

    public bool Relative => Scales.EndsWith("_pct", StringComparison.Ordinal);

    /// <summary>The parameter it scales: <c>damage</c> for <c>damage_pct</c>, <c>count</c> for <c>count</c>.</summary>
    public string Param => Relative ? Scales[..^"_pct".Length] : Scales;
}

/// <summary>
/// One primitive of a skill's effect, as a descriptor the engine binds (RU6, HE6): which
/// <see cref="Primitive"/>, its parameters as per-rank curves, what fires it
/// (<see cref="Trigger"/>, or the cast when null), who it lands on, the stats and conditions
/// it grants, a family it is limited to (an upgrade's sub-effect: Death From Above's melee
/// crit), and a store it sizes (<see cref="Capacity"/>).
/// </summary>
public sealed record EffectDef(
    Primitive Primitive,
    string? Trigger,
    string? Target,
    string? Family,
    IReadOnlyDictionary<string, RankCurve> Params,
    IReadOnlyList<StatCurve> Stats,
    IReadOnlyList<string> Conditions,
    string? Capacity)
{
    public PrimitiveSpec Spec => Primitives.Of(Primitive);

    /// <summary>The parameter at <paramref name="rank"/>, or the primitive's default when the row does not give it.</summary>
    public double At(string param, int rank) =>
        Params.TryGetValue(param, out var c) ? c.At(rank)
        : Spec.Optional.TryGetValue(param, out var d) ? d
        : throw new ArgumentException($"'{param}' is not a parameter of {Spec.Name}", nameof(param));

    public bool DealsDamage => Spec.DealsDamage;
}

/// <summary>
/// A skill (plan_tf2_rpg 2e, Appendix A): its class tree and tab (or the shared Mercenary
/// tab), tier, prerequisites, max rank and kind, mana cost and cooldown per rank, per-rank
/// hero-level floors (the Mercenary tab's, which has no tiers), synergies, and its effect as
/// descriptors. An action declares exactly one <see cref="DamageType"/> and never a family;
/// an upgrade's <see cref="Scope"/> is <c>all</c> or exactly one weapon family (R17).
/// </summary>
public sealed record SkillDef(
    string Id,
    string Name,
    string Class,
    string Tab,
    int Tier,
    SkillKind Kind,
    int MaxRank,
    string? DamageType,
    string? Scope,
    RankCurve ManaCost,
    RankCurve Cooldown,
    RankCurve? LevelFloor,
    IReadOnlyList<Prerequisite> Prerequisites,
    IReadOnlyList<Synergy> Synergies,
    IReadOnlyList<string> Tags,
    string? Requires,
    IReadOnlyList<EffectDef> Effect) : Content.IContentRow
{
    /// <summary>The scope of an upgrade that helps every weapon.</summary>
    public const string AllWeapons = "all";

    /// <summary>Cast for mana: actions, toggles and capstones (R17's "special action").</summary>
    public bool IsCast => Kind != SkillKind.Upgrade;

    /// <summary>The family an upgrade is limited to, or null for one of every weapon (or for a cast skill).</summary>
    public string? Family => Kind == SkillKind.Upgrade && Scope is { } s && s != AllWeapons ? s : null;

    /// <summary>Whether any of its effects deals damage.</summary>
    public bool DealsDamage => Effect.Any(e => e.DealsDamage);

    public bool HasTag(string tag) => Tags.Contains(tag, StringComparer.Ordinal);
}
