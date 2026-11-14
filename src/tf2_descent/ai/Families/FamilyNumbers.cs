using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Level;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Robots;

namespace TF2.Descent.Ai.Families;

/// <summary>
/// The numbers a family decider reads that are not a robot's temperament (HO6): the rules' own
/// (a rocket's and a pipe's speed, a medic's beam reach, a swing's box), the embedded skill
/// curves (for the families' own parameters, <see cref="HordeTemperament"/>), and the families'
/// geometry, named here once. Built once a level from the rules.
/// </summary>
public sealed record FamilyNumbers(float RocketSpeed, float PipeSpeed, float HealRange, float MeleeHull, SkillCurves Curves)
{
    /// <summary>How near a rusher closes before it stops spreading out and goes straight in.</summary>
    public const float RushStraightUnits = 220f;

    /// <summary>How far from its target a rusher's approach point stands, and the angle between two rushers' points.</summary>
    public const float RushRingUnits = 110f, RushSpreadDeg = 50f;

    /// <summary>How far past its reach a rusher still swings (the hull's half-width: the swing's box reaches it).</summary>
    public const float SwingSlackUnits = 24f;

    /// <summary>How far off its aim a gun may point and still fire, degrees (a rocket); a swing's arc is wider.</summary>
    public const float FireToleranceDeg = 6f, SwingToleranceDeg = 50f;

    /// <summary>How far short of its aim point a shot's line may stop and still land its splash by the target.</summary>
    public const float SplashSlackUnits = 48f;

    /// <summary>How near a line of fire a pack-mate blocks it.</summary>
    public const float MateClearUnits = 40f;

    /// <summary>A medic holds this near its patient, and closes when farther; inside the beam's reach less this it heals.</summary>
    public const float MedicStandUnits = 180f, MedicReachSlack = 60f;

    /// <summary>The height above the feet a gun aims at a body (the chest), and at the feet.</summary>
    public const float ChestUnits = 40f, FeetUnits = 4f;

    /// <summary>How much higher than its target a soldier stands before it aims at the feet.</summary>
    public const float AboveUnits = 24f;

    /// <summary>The level's numbers from <paramref name="rules"/>, over the embedded curves.</summary>
    public static FamilyNumbers For(TF2DescentRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var a = rules.Attacks;
        return new FamilyNumbers((float)a.Of("rocket_launcher").Speed, (float)a.Of("grenade_launcher").Speed, (float)a.Settings.Heal.Range,
                                 (float)a.Of("melee").HullUnits, HordeSkillMap.Curves());
    }
}

/// <summary>
/// A robot's temperament as a family decider can read it (HO6): the decider is handed the
/// core's <see cref="DecisionParams"/> only, which a recording re-derives from the levels it
/// wrote; each of the ten skills drives one of them alone along a curve that moves, so a level
/// is read back from its value, and the families' own parameters (feetAimShare, crowdedShare,
/// fleeHeroUnits in descent_ai_skills.json) are read at that level. Replay-safe: nothing here
/// comes from outside the recording.
/// </summary>
public static class HordeTemperament
{
    /// <summary>The level 0 to 10 of the skill driving core parameter <paramref name="parameter"/>, whose value is <paramref name="value"/>: the level its curve is nearest at.</summary>
    public static int LevelOf(SkillCurves curves, string parameter, float value)
    {
        ArgumentNullException.ThrowIfNull(curves);
        var c = curves[parameter];
        int best = 0;
        float bestGap = float.MaxValue;
        for (int l = 0; l <= StandardSkills.Cap; l++)
        {
            float gap = MathF.Abs(c.At(l) - value);
            if (gap < bestGap)
                (best, bestGap) = (l, gap);
        }

        return best;
    }

    /// <summary>The robot's Cunning (read back from routeAltWeight).</summary>
    public static int Cunning(SkillCurves curves, in DecisionParams p) => LevelOf(curves, "routeAltWeight", p.RouteAltWeight);

    /// <summary>The robot's Nerve (read back from retreatHealthPct).</summary>
    public static int Nerve(SkillCurves curves, in DecisionParams p) => LevelOf(curves, "retreatHealthPct", p.RetreatHealthPct);

    /// <summary>A family parameter of <paramref name="name"/> at <paramref name="level"/> of its skill.</summary>
    public static float At(SkillCurves curves, string name, int level) => curves[name].At(level);
}

/// <summary>
/// The level's family deciders (plan_descent_logic 3.6, HO6): the melee rusher, the rocket
/// soldier and the medic, registered over <see cref="HordeDeciders"/>; every other family hunts
/// (<see cref="HuntDecider"/>) until its own package registers one. The game's session and a
/// replay build their deciders here, so both pick the same.
/// </summary>
public static class HordeFamilies
{
    /// <summary>The deciders of <paramref name="level"/> with the three families registered, over <paramref name="rules"/>' numbers.</summary>
    public static HordeDeciders Deciders(HordeLevel level, TF2DescentRules rules, IHordePaths? paths = null)
    {
        var deciders = HordeDeciders.For(level, paths);
        Register(deciders, FamilyNumbers.For(rules));
        return deciders;
    }

    /// <summary>Registers the three families on <paramref name="deciders"/>.</summary>
    public static void Register(HordeDeciders deciders, FamilyNumbers numbers)
    {
        ArgumentNullException.ThrowIfNull(deciders);
        deciders.Register(HordeWeapons.Melee, (setup, routes) => new RusherDecider(setup, routes, numbers));
        deciders.Register(HordeWeapons.RocketLauncher, (setup, routes) => new SoldierDecider(setup, routes, numbers));
        deciders.Register(HordeWeapons.SyringeGun, (setup, routes) => new MedicDecider(setup, routes, numbers));
    }
}
