using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Rules.Sheets;

namespace TF2.Descent.Rules.Loot;

/// <summary>
/// The XP a kill pays (plan_tf2_rpg D10, 2f; plan_descent_logic RU4): the robot's XP value
/// (<see cref="RobotStats.Xp"/>: template × scale² × temperament budget × the level curve ×
/// its champion affixes, never the party) times the percent experience.json's curve gives the
/// hero's level against the monster's. Every member in range is paid in full: a party does
/// not share XP.
///
/// <para>It also bounds what a checkpoint may claim (<see cref="IKillXpBound"/>): the most a
/// kill of a template could ever pay is the template at its largest scale, at the deepest
/// monster level, with the champion affixes that raise XP most (as many as the deepest depth
/// allows, one immunity at most), at the curve's highest percent.</para>
/// </summary>
public sealed class ExperienceRules : IKillXpBound
{
    private readonly DescentContent _content;
    private readonly RobotRules _robots;

    public ExperienceRules(DescentContent content, RobotRules robots)
    {
        _content = content;
        _robots = robots;
    }

    private ExperienceSettings S => _content.Experience;

    /// <summary>The percent of a robot's XP a hero of <paramref name="heroLevel"/> is paid for a monster of <paramref name="monsterLevel"/>.</summary>
    public double LevelPct(int heroLevel, int monsterLevel)
    {
        var points = S.LevelDifference;
        int diff = monsterLevel - heroLevel;
        if (diff <= points[0].Diff) return points[0].Pct;
        for (int i = 1; i < points.Count; i++)
        {
            var (d1, p1) = points[i];
            if (diff > d1) continue;
            var (d0, p0) = points[i - 1];
            return p0 + (p1 - p0) * (diff - d0) / (d1 - d0);
        }
        return points[^1].Pct;
    }

    /// <summary>What killing <paramref name="robot"/> pays a hero of <paramref name="heroLevel"/>: its XP times the level percent, rounded.</summary>
    public long KillXp(RobotStats robot, int heroLevel) =>
        (long)Math.Round(robot.Xp * LevelPct(heroLevel, robot.Level) / 100, MidpointRounding.AwayFromZero);

    /// <summary>What a kill of a template at a scale, a depth and with affixes pays a hero of <paramref name="heroLevel"/>.</summary>
    public long KillXp(string template, double scale, int depth, IReadOnlyList<string> affixes, int heroLevel) =>
        KillXp(_robots.Stats(_content.Robots[template], scale, _robots.MonsterLevel(depth), affixes), heroLevel);

    /// <summary>
    /// The most one kill of <paramref name="template"/> (or a loot key naming it) could pay any
    /// hero (see the class summary), rounded up; 0 for a name that is no template.
    /// </summary>
    public long MostXpPerKill(string template)
    {
        var key = LootKey.Parse(template);
        if (!_content.Robots.TryGet(key.Template, out var t)) return 0;
        int level = _content.Levels.Rows.Max(d => d.MonsterLevel);
        int most = _content.Levels.Rows.Max(d => d.MaxAffixes);
        var affixes = new List<string>();
        foreach (var a in _content.Champions.Rows.OrderByDescending(a => a.XpPct).ThenBy(a => a.Id, StringComparer.Ordinal))
        {
            if (affixes.Count >= most) break;
            if (a.Immunity is not null && affixes.Any(x => _content.Champions[x].Immunity is not null)) continue;
            affixes.Add(a.Id);
        }
        double xp = _robots.Stats(t, t.ScaleMax, level, affixes).Xp;
        double pct = S.LevelDifference.Max(p => p.Pct);
        return (long)Math.Ceiling(xp * pct / 100 - 1e-9);
    }
}
