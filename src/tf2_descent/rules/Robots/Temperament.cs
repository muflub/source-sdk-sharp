using System.Collections.Immutable;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Furniture;

namespace TF2.Descent.Rules.Robots;

/// <summary>
/// A robot's temperament (R20, B.11): a level 0 to 10 for each AI skill, in robot_rules.json's
/// skill order, summing to its budget; its signature skills; and its style, the style words
/// of its top two skills ("Deadeye Lone").
/// </summary>
public sealed record Temperament(ImmutableArray<int> Levels, int Budget, ImmutableArray<string> Signatures, string Style)
{
    public int Sum => Levels.Sum();
}

/// <summary>
/// The temperament roll (plan_tf2_rpg B.11), a pure function of (pack seed, robot index,
/// template, scale): the budget is round(40 × scale) and nothing else moves it; two or three
/// signature skills are loaded to 7..10 first, chosen by the template's weights, and the rest
/// is scattered a point at a time by those weights. A cap never loses a point: what it refuses
/// spills to the next-weighted skill with room. A boss's temperament is authored.
/// </summary>
public sealed class TemperamentRules
{
    private readonly DescentContent _content;
    private readonly RobotRules _robots;

    public TemperamentRules(DescentContent content, RobotRules robots)
    {
        _content = content;
        _robots = robots;
    }

    private TemperamentSettings S => _content.RobotRules.Temperament;

    public IReadOnlyList<string> Skills => [.. S.Skills.Select(s => s.Id)];

    public int Index(string skill)
    {
        for (int i = 0; i < S.Skills.Count; i++)
            if (S.Skills[i].Id == skill) return i;
        throw new KeyNotFoundException($"'{skill}' is not an AI skill");
    }

    /// <summary>The roll. <paramref name="attempt"/> is the pack's re-roll count (0 first): a re-roll is a fresh, still seeded, roll.</summary>
    public Temperament Roll(ulong packSeed, int robotIndex, RobotTemplate t, double scale, int attempt = 0)
    {
        int n = S.Skills.Count;
        int budget = _robots.Budget(scale);
        if (t.FixedTemperament is { } authored)
        {
            var fixedLevels = S.Skills.Select(s => authored.TryGetValue(s.Id, out var v) ? v : 0).ToImmutableArray();
            return Make(fixedLevels, budget, [], t);
        }

        var limit = new int[n];
        var weight = new double[n];
        for (int i = 0; i < n; i++)
        {
            limit[i] = t.Cap(S.Skills[i].Id, S.SkillMax);
            weight[i] = t.Weight(S.Skills[i].Id);
        }
        var levels = new int[n];
        int left = budget;
        var roll = Seeded.From(packSeed, "temperament", robotIndex, t.Id, attempt);

        // Signatures: chosen by weight, without replacement, among skills with room.
        int count = S.SignatureCountMin + roll.Next(S.SignatureCountMax - S.SignatureCountMin + 1);
        var signatures = new List<int>();
        for (int k = 0; k < count; k++)
        {
            var open = Enumerable.Range(0, n).Where(i => !signatures.Contains(i) && limit[i] > 0).ToList();
            if (open.Count == 0) break;
            signatures.Add(open[PickWeighted(ref roll, open, weight)]);
        }
        foreach (int s in signatures)
        {
            int target = S.SignatureLevelMin + roll.Next(S.SignatureLevelMax - S.SignatureLevelMin + 1);
            left -= Put(levels, limit, weight, s, Math.Min(target, left));
        }

        // The rest, a point at a time, by weight, to skills with room.
        while (left > 0)
        {
            var open = Enumerable.Range(0, n).Where(i => levels[i] < limit[i]).ToList();
            if (open.Count == 0)
                throw new InvalidOperationException($"template '{t.Id}' caps hold fewer than {budget} points");   // the loader refuses such a template
            levels[open[PickWeighted(ref roll, open, weight)]]++;
            left--;
        }
        return Make([.. levels], budget, [.. signatures.Select(i => S.Skills[i].Id)], t);
    }

    /// <summary>
    /// Gives <paramref name="points"/> to <paramref name="skill"/>; what its cap refuses spills
    /// to the next-weighted skill with room (weight descending, then skill order). Returns the
    /// points placed, which is all of them while any skill has room.
    /// </summary>
    private static int Put(int[] levels, int[] limit, double[] weight, int skill, int points)
    {
        int placed = 0;
        int take = Math.Min(points, limit[skill] - levels[skill]);
        levels[skill] += take;
        placed += take;
        foreach (int i in Enumerable.Range(0, levels.Length).OrderByDescending(i => weight[i]).ThenBy(i => i))
        {
            if (placed == points) break;
            if (i == skill) continue;
            int more = Math.Min(points - placed, limit[i] - levels[i]);
            levels[i] += more;
            placed += more;
        }
        return placed;
    }

    /// <summary>An index into <paramref name="open"/> by the skills' weights; uniform when none of them weighs anything.</summary>
    private static int PickWeighted(ref Seeded roll, List<int> open, double[] weight)
    {
        var w = open.Select(i => weight[i]).ToList();
        return w.Sum() > 0 ? roll.Pick(w) : roll.Next(open.Count);
    }

    private Temperament Make(ImmutableArray<int> levels, int budget, ImmutableArray<string> signatures, RobotTemplate t) =>
        new(levels, budget, signatures, Style(levels, t));

    /// <summary>The style words of the top two skills: the higher level first, ties to the heavier weight, then skill order.</summary>
    public string Style(ImmutableArray<int> levels, RobotTemplate t)
    {
        var top = Enumerable.Range(0, levels.Length)
            .OrderByDescending(i => levels[i]).ThenByDescending(i => t.Weight(S.Skills[i].Id)).ThenBy(i => i)
            .Take(2).Select(i => S.Skills[i].Style);
        return string.Join(" ", top);
    }
}
