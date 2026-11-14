using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Content;

namespace TF2.Descent.Server.Skills;

/// <summary>What a status or threat on a robot came to.</summary>
public enum RobotStatusAnswer
{
    /// <summary>The robot carries it now.</summary>
    Applied,
    /// <summary>The target is no robot the horde knows, or one the effect's target word excludes.</summary>
    NotARobot,
    /// <summary>Nothing the robot can carry: no horde this level, or a word (a stat) that does nothing to a robot.</summary>
    NotAvailable,
    /// <summary>The robot is immune to the status's damage type (a fire-immune robot does not burn).</summary>
    Immune,
}

/// <summary>
/// THE HORDE'S SEAM for what a skill does to a robot (plan_descent_logic
/// HE6, E9): a debuff's condition (<c>emp_stun</c>, <c>slowed</c>,
/// <c>mini_crit_marked</c>, <c>burn</c>, <c>stun</c>, <c>knock_up</c>) becomes a
/// Descent status effect on the robot's roster row, and a taunt
/// (<c>threat</c>) an entry in its threat table. TF conditions stay on
/// heroes (E9); robots do not carry them.
///
/// <para>
/// SINGLE-OWNER: one registration, the horde's over its roster
/// (<c>Horde.RobotStatusEffects</c>, HO8).
/// </para>
/// </summary>
public interface IRobotStatusSink
{
    /// <summary>
    /// <paramref name="condition"/> (one of the rules' condition words) on
    /// <paramref name="robot"/> for <paramref name="seconds"/>, from the
    /// <paramref name="hero"/>'s skill <paramref name="skillId"/>. <paramref name="target"/>
    /// is the effect's target word: <c>enemy_minions</c> and <c>enemy_bosses</c>
    /// name which robots it is for (EMP Charge stuns the one and slows the
    /// other), <c>enemies</c> all; the sink answers <see cref="RobotStatusAnswer.NotARobot"/>
    /// for a robot the word excludes.
    /// </summary>
    RobotStatusAnswer Apply(EntityHandle robot, string condition, double seconds, EntityHandle hero, string skillId,
                            string target);

    /// <summary><paramref name="robot"/> targets <paramref name="hero"/> for <paramref name="seconds"/> (a taunt).</summary>
    RobotStatusAnswer Threaten(EntityHandle robot, EntityHandle hero, double seconds, string skillId);

    /// <summary>
    /// A debuff's <paramref name="stats"/> on <paramref name="robot"/> for <paramref name="seconds"/>
    /// (Maggots!' <c>damage_all_pct</c>: what the robot deals), from the
    /// <paramref name="hero"/>'s skill <paramref name="skillId"/>, for the robots
    /// <paramref name="target"/> names.
    /// </summary>
    RobotStatusAnswer Weaken(EntityHandle robot, IReadOnlyList<StatAmount> stats, double seconds, EntityHandle hero, string skillId,
                             string target);
}
