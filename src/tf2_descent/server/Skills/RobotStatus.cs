using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Descent.Server.Skills;

/// <summary>What a status or threat on a robot came to.</summary>
public enum RobotStatusAnswer
{
    /// <summary>The robot carries it now.</summary>
    Applied,
    /// <summary>The target is no robot the horde knows.</summary>
    NotARobot,
    /// <summary>Robots carry no status effects in this build: the horde has not implemented the sink (track HO).</summary>
    NotAvailable,
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
/// SINGLE-OWNER: one registration. Until the horde exists (HO1, HO3) the
/// registered sink is <see cref="NoRobotStatus"/>, which answers
/// <see cref="RobotStatusAnswer.NotAvailable"/> to everything; the horde
/// replaces it with one over its roster.
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
}

/// <summary>The sink while robots do not exist: nothing is available.</summary>
[SideService(Side.Server, As = [typeof(IRobotStatusSink)])]
public sealed class NoRobotStatus : IRobotStatusSink
{
    public RobotStatusAnswer Apply(EntityHandle robot, string condition, double seconds, EntityHandle hero, string skillId,
                                   string target) =>
        RobotStatusAnswer.NotAvailable;

    public RobotStatusAnswer Threaten(EntityHandle robot, EntityHandle hero, double seconds, string skillId) =>
        RobotStatusAnswer.NotAvailable;
}
