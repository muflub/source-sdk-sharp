using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Robots;

//-----------------------------------------------------------------------------
// robot_conditions.json
//-----------------------------------------------------------------------------

/// <summary>
/// What a skill's condition word does to a robot (plan_descent_logic HO8, E9): the status.json
/// row it puts on the robot (<c>emp_stun</c> and <c>stun</c> are the stun, <c>slowed</c> the slow,
/// <c>mini_crit_marked</c> the mark), or, for <c>knock_up</c>, the upward speed it throws the robot
/// with, in units a second. A word this table does not name does nothing to a robot.
/// </summary>
public sealed record RobotConditionRow(string Id, string? Status, double KnockUp) : IContentRow;

public sealed class RobotConditionTable : RowTable<RobotConditionRow>
{
    public override string Name => ContentTables.RobotConditions;

    protected override RobotConditionRow ReadRow(JsonFields f) =>
        new(f.Str("id"), f.StrOr("status", null), f.NumOr("knockUp", 0));

    protected override void ValidateRow(DescentContent content, RobotConditionRow row, ContentErrors errors)
    {
        if (row.Status is { } status)
            ContentChecks.Reference<StatusRow>(content, errors, Name, row.Id, "status", status, ContentTables.Status);
        ContentChecks.Range(errors, Name, row.Id, "knockUp", row.KnockUp, 0, 5000);
        if ((row.Status is null) == (row.KnockUp <= 0))
            errors.Add(Name, row.Id, ContentRule.Malformed, "a condition names a status or a knock-up speed, one of the two");
    }
}

/// <summary>
/// A skill's conditions on robots (plan_descent_logic HO8): which robots a target word is for,
/// what each word does (robot_conditions.json), the knock-up a word gives, and the debuff that
/// lowers what a robot deals (Maggots!). Pure; the server carries it out.
/// </summary>
public sealed class RobotConditionRules
{
    /// <summary>The stat a debuff lowers a robot's damage by, in percent (Maggots!' -10 to -30).</summary>
    public const string OutgoingStat = "damage_all_pct";

    private readonly DescentContent _content;

    public RobotConditionRules(DescentContent content) => _content = content;

    /// <summary>The row of <paramref name="word"/>; null for a word that does nothing to a robot.</summary>
    public RobotConditionRow? Of(string word) => _content.RobotConditions.TryGet(word, out var row) ? row : null;

    /// <summary>
    /// Whether an effect aimed at <paramref name="target"/> is for a robot: <c>enemies</c> are every
    /// robot, <c>enemy_minions</c> every robot but a boss (champions and giants are minions: EMP
    /// Charge stuns them), <c>enemy_bosses</c> only the level's boss. Any other word is for none.
    /// </summary>
    public static bool Admits(string target, bool boss) => target switch
    {
        "enemies" => true,
        "enemy_minions" => !boss,
        "enemy_bosses" => boss,
        _ => false,
    };

    /// <summary>
    /// The speed a knock-up throws a robot with: straight up, the word's speed less the robot's
    /// knockback resistance (the share a blast's push loses, R16).
    /// </summary>
    public static Vec3 KnockUp(RobotConditionRow row, double knockbackResistPct) =>
        new(0, 0, row.KnockUp * (1 - Math.Clamp(knockbackResistPct, 0, 100) / 100));

    /// <summary>The change a debuff's stats make to what a robot deals, in percent: its <see cref="OutgoingStat"/>s, summed.</summary>
    public static double OutgoingPct(IReadOnlyList<StatAmount> stats)
    {
        double pct = 0;
        foreach (var s in stats)
            if (s.Stat == OutgoingStat)
                pct += s.Amount;
        return pct;
    }

    /// <summary>
    /// A second debuff meets the first: one that has ended gives way; else the stronger
    /// reduction stands and the later end. Answers the debuff in force after it.
    /// </summary>
    public static (double Pct, long End) Weaken(double pct, long end, double newPct, long newEnd, long now) =>
        end <= now ? (newPct, newEnd) : (Math.Min(pct, newPct), Math.Max(end, newEnd));

    /// <summary>What a robot deals under a debuff of <paramref name="pct"/> ending at <paramref name="end"/>, as a multiplier at <paramref name="now"/>.</summary>
    public static double OutgoingMultiplier(double pct, long end, long now) =>
        end > now ? Math.Max(0, 1 + pct / 100) : 1;
}
