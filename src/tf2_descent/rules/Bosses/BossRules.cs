using System.Collections.Immutable;

using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;

namespace TF2.Descent.Rules.Bosses;

/// <summary>
/// The bosses on paper (plan_tf2_rpg D13; mine plan D4, D11; plan_descent_logic HO13), pure over
/// bosses.json: which boss holds a depth's arena, which phase a boss is in at a life, what its
/// phases add, what the Tank shrugs, and the camps a hero's boss kills open.
/// </summary>
public sealed class BossRules
{
    private readonly DescentContent _content;

    public BossRules(DescentContent content) => _content = content;

    public BossSettings Settings => _content.Bosses;

    /// <summary>The script of <paramref name="template"/>; null for a robot that is no boss.</summary>
    public BossScript? Of(string template) => Settings.Of(template);

    /// <summary>The boss whose arena is at <paramref name="depth"/>; null for a depth with none.</summary>
    public BossScript? AtDepth(int depth) => Settings.AtDepth(depth);

    /// <summary>
    /// The phase a boss is in at <paramref name="life"/> of <paramref name="most"/>: the last of its
    /// script's phases whose threshold its life share (in percent) is at or below; 0 at full life.
    /// </summary>
    public static int PhaseAt(BossScript script, double life, double most)
    {
        ArgumentNullException.ThrowIfNull(script);
        double pct = most > 0 ? Math.Clamp(life / most, 0, 1) * 100 : 100;
        int phase = 0;
        for (int i = 1; i < script.Phases.Count; i++)
            if (pct <= script.Phases[i].At + 1e-9)
                phase = i;
        return phase;
    }

    /// <summary>
    /// The phase at <paramref name="life"/> of <paramref name="most"/> as the boss's brain sees it:
    /// each rounded up to a whole point, as the horde's view carries them, so the server and the
    /// brain agree at a threshold.
    /// </summary>
    public static int PhaseSeen(BossScript script, double life, double most) => PhaseAt(script, Math.Ceiling(life), Math.Ceiling(most));

    /// <summary>
    /// The phase a boss is in after <paramref name="reached"/>: a phase once entered holds, so a
    /// boss healed back over a threshold does not leave it (and does not summon its adds again).
    /// </summary>
    public static int Phase(BossScript script, double life, double most, int reached) => Math.Max(reached, PhaseAt(script, life, most));

    /// <summary>The adds phase <paramref name="phase"/> of <paramref name="script"/> summons as it begins.</summary>
    public static IReadOnlyList<BossAdd> AddsOf(BossScript script, int phase) =>
        (uint)phase < (uint)script.Phases.Count ? script.Phases[phase].Adds : [];

    /// <summary>The special phase <paramref name="phase"/> asks, null for none.</summary>
    public BossSpecial? SpecialOf(BossScript script, int phase) =>
        (uint)phase < (uint)script.Phases.Count && script.Phases[phase].Special.Length > 0 ? Settings.Special(script.Phases[phase].Special) : null;

    /// <summary>Whether <paramref name="template"/> shrugs condition word <paramref name="condition"/> (the Tank's immunities).</summary>
    public bool Shrugs(string template, string condition) =>
        template == Settings.Tank.Template && Settings.Tank.Immune.Contains(condition, StringComparer.Ordinal);

    /// <summary>Whether <paramref name="template"/> is the Tank.</summary>
    public bool IsTank(string template) => template == Settings.Tank.Template;

    /// <summary>
    /// The boss depths a hero who has killed the bosses <paramref name="bossKills"/> (template ids,
    /// the sheet's <see cref="HeroSheet.BossKills"/>) has cleared: what opens the camps
    /// (<see cref="TownDescents.Destinations"/>). An id that is no boss counts for nothing.
    /// </summary>
    public IReadOnlyList<int> DepthsKilled(IEnumerable<string> bossKills)
    {
        ArgumentNullException.ThrowIfNull(bossKills);
        var depths = new SortedSet<int>();
        foreach (string id in bossKills)
            if (Of(id) is { } b)
                depths.Add(b.Depth);
        return [.. depths];
    }

    /// <summary>
    /// A boss kill recorded on a hero's sheet (mine plan D4): <paramref name="template"/> joins its
    /// <see cref="HeroSheet.BossKills"/>. Once and only once: a second kill of a boss the sheet
    /// already holds leaves the sheet as it is (<paramref name="added"/> false), as does a robot
    /// that is no boss.
    /// </summary>
    public HeroSheet RecordKill(HeroSheet sheet, string template, out bool added)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        added = Of(template) is not null && !sheet.BossKills.Contains(template);
        return added ? sheet with { BossKills = sheet.BossKills.Add(template) } : sheet;
    }

    /// <summary><paramref name="seconds"/> as whole server ticks, at least one.</summary>
    public static long Ticks(double seconds, double ticksPerSecond) =>
        Math.Max(1, (long)Math.Round(seconds * ticksPerSecond, MidpointRounding.AwayFromZero));

    /// <summary>Every boss template id, shallowest first.</summary>
    public ImmutableArray<string> Ids => [.. Settings.Bosses.OrderBy(b => b.Depth).Select(b => b.Id)];
}
