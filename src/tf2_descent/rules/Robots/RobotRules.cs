using System.Collections.Immutable;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Furniture;

namespace TF2.Descent.Rules.Robots;

/// <summary>
/// A robot's numbers once its scale, level, champion affixes and party are known (2f, R16,
/// R25, D11). <see cref="Resist"/> is percent per damage type: 100 for the one type a champion
/// affix makes it immune to, else the template's plus its affixes', clamped to the table's
/// bounds. <see cref="LootQuality"/> is a multiplier on the template's, following life.
/// </summary>
public sealed record RobotStats(
    double Scale,
    int Level,
    int Budget,
    double Life,
    double Damage,
    double Speed,
    double Reach,
    double KnockbackResistPct,
    double Xp,
    double LootQuality,
    ImmutableSortedDictionary<string, double> Resist,
    string? Immunity)
{
    /// <summary>The threat metric the dominance facts use: life times damage (a robot's staying power by its hitting power).</summary>
    public double Threat => Life * Damage;
}

/// <summary>
/// Robots on paper (plan_tf2_rpg 2f; plan_descent_logic RU7): scale rolls, the one curve from
/// scale to stats, monster level by depth, the level curve, champion affixes, party scaling,
/// resistances and immunity. Pure over robots.json, champions.json, levels.json and
/// robot_rules.json.
/// </summary>
public sealed class RobotRules
{
    private readonly DescentContent _content;

    public RobotRules(DescentContent content) => _content = content;

    private RobotRulesSettings S => _content.RobotRules;

    public RobotTemplate Template(string id) => _content.Robots[id];

    //-------------------------------------------------------------------------
    // Depth
    //-------------------------------------------------------------------------

    /// <summary>The depth's row; a depth above the deepest reads the deepest, below 1 reads depth 1 (the town and the host's probes).</summary>
    public DepthRow Depth(int depth)
    {
        var rows = _content.Levels.Rows;
        return rows[Math.Clamp(depth, 1, rows.Count) - 1];
    }

    /// <summary>The monster level of a depth (levels.json).</summary>
    public int MonsterLevel(int depth) => Depth(depth).MonsterLevel;

    //-------------------------------------------------------------------------
    // Scale and budget
    //-------------------------------------------------------------------------

    /// <summary>The scale a robot rolls in its template's range (R16), from the pack seed and its index; a boss's is fixed.</summary>
    public static double RollScale(RobotTemplate t, ulong packSeed, int index)
    {
        if (t.ScaleMin == t.ScaleMax) return t.ScaleMin;
        var roll = Seeded.From(packSeed, "scale", index, t.Id);
        return t.ScaleMin + (t.ScaleMax - t.ScaleMin) * roll.NextDouble();
    }

    /// <summary>The temperament budget a scale buys (B.11): round(40 × scale), halves up. Size is the only thing that moves it.</summary>
    public int Budget(double scale) =>
        (int)Math.Round(S.Temperament.BudgetPerScale * scale, MidpointRounding.AwayFromZero);

    //-------------------------------------------------------------------------
    // Stats
    //-------------------------------------------------------------------------

    /// <summary>
    /// The robot's numbers. Life is template × scale^2 × the level curve × each affix × party
    /// (D11: +60 % a member past the first); damage is template × the level curve × each affix;
    /// speed is template × scale^-½ × each affix; reach template × scale; knockback resistance
    /// 10 % a unit of scale plus the affixes', capped; XP template × scale^2 × (budget ÷ the
    /// budget at scale 1) × the level curve × each affix, so it follows life and temperament
    /// and not the party. A second immunity affix throws: no robot is immune to two types.
    /// </summary>
    public RobotStats Stats(RobotTemplate t, double scale, int level, IReadOnlyList<string> affixes, int partySize = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        var c = S.Scale;
        var rows = affixes.Select(a => _content.Champions[a]).ToList();
        var immunities = rows.Where(a => a.Immunity is not null).Select(a => a.Immunity!).Distinct(StringComparer.Ordinal).ToList();
        if (immunities.Count > 1)
            throw new ArgumentException($"affixes {string.Join(", ", affixes)} make a robot immune to {string.Join(" and ", immunities)}: one immunity at most", nameof(affixes));

        int budget = Budget(scale);
        double levels = level - 1;
        double Product(Func<ChampionAffix, double> pct) => rows.Aggregate(1.0, (p, a) => p * (1 + pct(a) / 100));
        int party = Math.Clamp(partySize, 1, S.Party.MaxMembers);

        double lifeCurve = Math.Pow(scale, c.LifeExponent);
        double life = t.Life * lifeCurve * Math.Pow(1 + S.Level.LifePerLevelPct / 100, levels) * Product(a => a.LifePct)
                      * (1 + S.Party.LifePerExtraMemberPct / 100 * (party - 1));
        double damage = t.Damage * Math.Pow(1 + S.Level.DamagePerLevelPct / 100, levels) * Product(a => a.DamagePct);
        double speed = t.Speed * Math.Pow(scale, c.SpeedExponent) * Product(a => a.SpeedPct);
        double reach = t.Reach * Math.Pow(scale, c.ReachExponent);
        double kb = Math.Min(c.KnockbackResistMax, c.KnockbackResistPerScale * scale + rows.Sum(a => a.KnockbackResistPct));
        double brains = Math.Pow((double)budget / Budget(1.0), c.XpBudgetExponent);
        double xp = t.Xp * lifeCurve * brains * Math.Pow(1 + S.Level.XpPerLevelPct / 100, levels) * Product(a => a.XpPct);

        var resist = ImmutableSortedDictionary.CreateBuilder<string, double>(StringComparer.Ordinal);
        foreach (var type in _content.DamageTypes.Rows)
        {
            if (immunities.Count == 1 && immunities[0] == type.Id)
            {
                resist[type.Id] = S.Resist.Immune;
                continue;
            }
            double sum = t.ResistTo(type.Id) + rows.Sum(a => a.Resist.TryGetValue(type.Id, out var r) ? r : 0);
            resist[type.Id] = Math.Clamp(sum, S.Resist.Min, S.Resist.Max);
        }
        return new RobotStats(scale, level, budget, life, damage, speed, reach, kb, xp, lifeCurve * brains, resist.ToImmutable(),
            immunities.Count == 1 ? immunities[0] : null);
    }

    /// <summary>Damage of <paramref name="type"/> after the robot's resistance, applied once: nothing to the type it is immune to.</summary>
    public static double DamageTaken(RobotStats robot, string type, double amount) =>
        robot.Resist.TryGetValue(type, out var r) ? amount * (1 - r / 100) : throw new ArgumentException($"'{type}' is not a damage type", nameof(type));
}
