using SourceSharp.Abi;

using TF2.Descent.Ai.Roster;

namespace TF2.Descent.Server.Horde;

/// <summary>
/// One hit on a lent robot body, as the body took it (plan_descent_logic 3.8):
/// the robot and its body, who hit it, the hit as dealt (after TF's modify
/// rules: crit, falloff, the attacker's attributes), its engine damage bits and
/// the Descent damage type they map to, the life the rules took
/// (<see cref="TF2.Descent.Rules.Robots.RobotRules.DamageTaken"/>: resistance
/// and immunity), the life left, whether it killed, and where it landed: the
/// hit group the trace stopped on, the point, the force the blast or bullet
/// carried (the push stock gives a player, sized by the body's bounds), and
/// whether the robot is immune to the hit's type (it took nothing: IMMUNE).
/// <see cref="Dealt"/> is a status tick's own amount for a status's damage tick,
/// and <see cref="DamageType"/> a skill's declared type for a skill's hit.
/// </summary>
public readonly record struct RobotHit(
    RobotId Robot,
    int Body,
    EntityHandle Attacker,
    float Dealt,
    DamageType Bits,
    string DamageType,
    double Taken,
    double LifeLeft,
    bool Killed,
    HitGroup HitGroup,
    Vector Position,
    Vector Force,
    bool Immune = false);

/// <summary>
/// THE SEAM WHERE A ROBOT'S HITS ARE HANDED ON: once a hit on a lent, living
/// body, after the roster's life has taken it, whatever it took (an immune
/// hit takes nothing and is still a hit). The later packages are its readers:
/// HO8 (IMMUNE, status effects from hits, the blast push into motion), HO10
/// (XP, loot, the bestiary count on a kill), HO5 (a hit as an event for the
/// brain and a threat entry).
/// </summary>
public interface IRobotHitSink
{
    void OnRobotHit(in RobotHit hit);
}

/// <summary>
/// The first member of the sink's chain (<c>Motion.RobotHitChain</c>): counts
/// the hits and keeps the last, for the dev command and the live proofs.
/// Changes nothing.
/// </summary>
[SideService(Side.Server)]
public sealed class RobotHitTally : IRobotHitSink
{
    public int Hits { get; private set; }

    public int Kills { get; private set; }

    public RobotHit? Last { get; private set; }

    public void OnRobotHit(in RobotHit hit)
    {
        Hits++;
        if (hit.Killed)
            Kills++;
        Last = hit;
    }
}
