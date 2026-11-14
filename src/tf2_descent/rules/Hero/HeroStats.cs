using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Hero;

/// <summary>Current life and mana.</summary>
public readonly record struct Vitals(double Life, double Mana);

/// <summary>
/// A hero's derived numbers (plan_tf2_rpg 2a, Appendix C.2): Base and Now of each
/// attribute, every stat's total, and what the sheet shows beside them. Everything reads
/// Now. Built by <see cref="HeroRules.Stats"/>; the numbers come from the content.
/// Percent stats are summed in percent; the derived chances, resistances and leech are
/// fractions (0.75 = 75 %). Life and mana are not rounded here: TF's integer health is the
/// server's to round.
/// </summary>
public sealed class HeroStats
{
    private readonly HeroSettings _s;
    private readonly IReadOnlyDictionary<string, double> _totals;
    private readonly IReadOnlyDictionary<string, int> _base;
    private readonly IReadOnlyDictionary<string, double> _now;

    internal HeroStats(HeroSettings settings, IReadOnlyDictionary<string, int> baseValues,
        IReadOnlyDictionary<string, double> now, IReadOnlyDictionary<string, double> totals)
    {
        _s = settings;
        _base = baseValues;
        _now = now;
        _totals = totals;
    }

    public int Base(string attribute) => _base.TryGetValue(attribute, out var v) ? v : 0;

    /// <summary>Base plus every counting contribution to the attribute.</summary>
    public double Now(string attribute) => _now.TryGetValue(attribute, out var v) ? v : 0;

    /// <summary>A stat's total: the class's, what attribute points buy, and every counting contribution.</summary>
    public double Total(string stat) => _totals.TryGetValue(stat, out var v) ? v : 0;

    private double Pool(PoolStats p) => Math.Max(0, Total(p.Stat) * (1 + Total(p.PercentStat) / 100));

    public double MaxLife => Pool(_s.Life);
    public double MaxMana => Pool(_s.Mana);
    public double MoveSpeed => Pool(_s.Speed);

    public double Armour => Total(_s.ArmourStat);

    private double Clamped(ClampedStat c) => Math.Clamp(c.Base + Total(c.Stat), c.Min, c.Max);

    public double CritChance => Clamped(_s.Crit) / 100;
    public double KnockbackResist => Clamped(_s.KnockbackResist) / 100;
    public double SpreadMultiplier => 1 + Clamped(_s.Spread) / 100;

    /// <summary>
    /// The damage multiplier of a hit of the given kinds (a rocket is blast and
    /// projectile): one plus the all-damage stat and each kind's stat, added, never below 0.
    /// A hit of any weapon kind is also a weapon hit (<see cref="WeaponHit"/>): the weapon
    /// stat joins it once, however many weapon kinds it is. An action's hit never takes it.
    /// </summary>
    public double DamageMultiplier(params string[] kinds)
    {
        var counted = new HashSet<string>(kinds, StringComparer.Ordinal);
        if (kinds.Any(k => _s.WeaponHit.Kinds.Contains(k, StringComparer.Ordinal)))
            counted.Add(_s.WeaponHit.Kind);
        double pct = Total(_s.DamageAllStat);
        foreach (var k in counted)
            pct += Total(Kind(_s.DamageKinds, k, "damage kind").Stat);
        return Math.Max(0, 1 + pct / 100);
    }

    /// <summary>A resistance (fire, blast, bullet, shock) as a fraction, between the floor and the cap (75 %).</summary>
    public double Resist(string id) =>
        Math.Clamp(Total(_s.ResistAllStat) + Total(Kind(_s.Resistances, id, "resistance").Stat), _s.ResistFloor, _s.ResistCap) / 100;

    private static KindStat Kind(IReadOnlyList<KindStat> kinds, string id, string what) =>
        kinds.FirstOrDefault(k => k.Id == id) ?? throw new ArgumentException($"'{id}' is not a {what}", nameof(id));

    /// <summary>A sustain rate in percent: (base + stat) raised by the bonus stat, between 0 and its cap.</summary>
    private double Rate(SustainRule r) =>
        Math.Clamp((r.Base + Total(r.Stat)) * (1 + (r.BonusStat is null ? 0 : Total(r.BonusStat)) / 100), 0, r.Cap);

    /// <summary>Life regenerated per second, as a percent of max life (capped: 3 %).</summary>
    public double LifeRegenRate => Rate(_s.LifeRegen);
    /// <summary>
    /// Life regenerated per second: the percent total of max life plus the flat stat's HP a
    /// second (none when nothing grants it), between 0 and the cap's share of max life. With
    /// no flat amount this is <see cref="LifeRegenRate"/> of max life, as before.
    /// </summary>
    public double LifeRegenPerSecond
    {
        get
        {
            var r = _s.LifeRegen;
            double flat = r.FlatStat is null ? 0 : Total(r.FlatStat);
            double pct = (r.Base + Total(r.Stat)) * (1 + (r.BonusStat is null ? 0 : Total(r.BonusStat)) / 100);
            return Math.Clamp(pct / 100 * MaxLife + flat, 0, r.Cap / 100 * MaxLife);
        }
    }

    /// <summary>Mana regenerated per second, as a percent of max mana (capped).</summary>
    public double ManaRegenRate => Rate(_s.ManaRegen);
    public double ManaRegenPerSecond => ManaRegenRate / 100 * MaxMana;

    /// <summary>The fraction of damage dealt that returns as life (capped: 15 %).</summary>
    public double LifeLeech => Rate(_s.LifeLeech) / 100;

    /// <summary>The fraction of damage dealt that returns as mana (capped).</summary>
    public double ManaLeech => Rate(_s.ManaLeech) / 100;

    /// <summary>
    /// Life healed by a hit: leech times the damage dealt after the target's resistances,
    /// and no more than the life the target had, so overkill and an immune target heal nothing extra.
    /// </summary>
    public double LifeFromHit(double damageDealt, double targetLifeBefore) => LifeLeech * Dealt(damageDealt, targetLifeBefore);

    public double ManaFromHit(double damageDealt, double targetLifeBefore) => ManaLeech * Dealt(damageDealt, targetLifeBefore);

    private static double Dealt(double damage, double targetLife) => Math.Max(0, Math.Min(damage, targetLife));

    /// <summary>Full life and mana.</summary>
    public Vitals Full => new(MaxLife, MaxMana);
}
