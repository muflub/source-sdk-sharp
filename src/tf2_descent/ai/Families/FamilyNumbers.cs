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
    // HO12: the rules' numbers the remaining families read (robot_attacks.json).

    /// <summary>The pull on a lobbed pipe, units a second squared: what the demoman solves its arc with.</summary>
    public float PipeGravity { get; init; } = 320f;

    /// <summary>The flame's reach and its cone's half-angle.</summary>
    public float FlameRange { get; init; } = 300f;

    public float FlameConeDeg { get; init; } = 15f;

    /// <summary>How fast a heavy may turn its minigun, degrees a second (0: as fast as the body).</summary>
    public float MinigunTurnRate { get; init; } = 100f;

    /// <summary>How near its target a sentry buster starts its spin.</summary>
    public float BusterTrigger { get; init; } = 100f;

    /// <summary>The knife's cone behind its victim, the half-angle about the victim's back.</summary>
    public float BackstabConeDeg { get; init; } = 90f;

    /// <summary>A scout's, a shotgunner's and a sniper's band before temperament: their attacks' reach caps them.</summary>
    public const float ScoutBandUnits = 320f, GunnerBandUnits = 384f;

    /// <summary>How far off the hero's chest a scattergun or a shotgun may point and still fire (a spread of pellets).</summary>
    public const float SpreadToleranceDeg = 8f;

    /// <summary>How far off a sniper may point and still take its shot.</summary>
    public const float SniperToleranceDeg = 1.5f;

    /// <summary>How far off a heavy may point and still pour on fire: suppression, not marksmanship.</summary>
    public const float SuppressToleranceDeg = 12f;

    /// <summary>A sniper aims this high above the feet: the head.</summary>
    public const float HeadUnits = 62f;

    /// <summary>A pyro closes to this share of its flame's reach before it stops closing and burns.</summary>
    public const float BurnShare = 0.55f;

    /// <summary>How far short of a hero's chest a line of fire at him may stop and still have reached him (it meets his hull first; found live, 2026-11-11: a buster beside its hero read its line as blocked).</summary>
    public const float HullReachUnits = 64f;

    /// <summary>A rushed sniper runs for this long, then, if still rushed, holds and shoots for as long (cornered), then runs again.</summary>
    public const float SniperRelocateSeconds = 2.5f;

    /// <summary>A burning pyro stops pressing in this near its hero (two hulls apart, where it still has its hero in its cone).</summary>
    public const float PyroStandUnits = 80f;

    /// <summary>How far a lob's launch line must run clear past its rise (the lintel test), and its least.</summary>
    public const float LobClearSlackUnits = 24f, LobLeastClearUnits = 96f;

    /// <summary>A spy stalks to a point this far behind its hero, closes in from this near, and stands this far behind to stab.</summary>
    public const float SpyStalkRingUnits = 220f, SpyCloseUnits = 320f, SpyBackUnits = 40f;

    /// <summary>A spy uncloaks (its telegraph) this near its hero.</summary>
    public const float SpyDecloakUnits = 200f;

    /// <summary>A spy going round its hero steps this far round the ring at a time, degrees, at this radius.</summary>
    public const float SpyFlankStepDeg = 70f, SpyFlankRingUnits = 110f;
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
                                 (float)a.Of("melee").HullUnits, HordeSkillMap.Curves())
        {
            PipeGravity = (float)a.Of("grenade_launcher").Gravity,
            FlameRange = (float)a.Of("flamethrower").RangeUnits,
            FlameConeDeg = (float)a.Of("flamethrower").ConeDegrees,
            MinigunTurnRate = (float)a.Of("minigun").TurnDegreesPerSecond,
            BusterTrigger = (float)a.Of("sentry_buster").TriggerUnits,
            BackstabConeDeg = (float)a.Of("knife").BackstabConeDegrees,
        };
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
/// The level's family deciders (plan_descent_logic 3.6, HO6, HO12): one for every family a robot
/// fights with, registered over <see cref="HordeDeciders"/> (the melee rusher, the rocket
/// soldier and the medic of HO6; the scout, the pyro, the demoman, the heavy, the shotgunner, the
/// sniper, the spy and the sentry buster of HO12). The game's session and a replay build their
/// deciders here, so both pick the same.
/// </summary>
public static class HordeFamilies
{
    /// <summary>The deciders of <paramref name="level"/> with every family registered, over <paramref name="rules"/>' numbers.</summary>
    public static HordeDeciders Deciders(HordeLevel level, TF2DescentRules rules, IHordePaths? paths = null)
    {
        var deciders = HordeDeciders.For(level, paths);
        var numbers = FamilyNumbers.For(rules);
        Register(deciders, numbers);
        Boss.BossFamilies.Register(deciders, rules, numbers);   // HO13: the bosses and the Tank, over the families
        return deciders;
    }

    /// <summary>Registers every family on <paramref name="deciders"/>.</summary>
    public static void Register(HordeDeciders deciders, FamilyNumbers numbers)
    {
        ArgumentNullException.ThrowIfNull(deciders);
        deciders.Register(HordeWeapons.Melee, (setup, routes) => new RusherDecider(setup, routes, numbers));
        deciders.Register(HordeWeapons.RocketLauncher, (setup, routes) => new SoldierDecider(setup, routes, numbers));
        deciders.Register(HordeWeapons.SyringeGun, (setup, routes) => new MedicDecider(setup, routes, numbers));
        deciders.Register(HordeWeapons.Scattergun, (setup, routes) => new ScoutDecider(setup, routes, numbers));
        deciders.Register(HordeWeapons.Flamethrower, (setup, routes) => new PyroDecider(setup, routes, numbers));
        deciders.Register(HordeWeapons.GrenadeLauncher, (setup, routes) => new DemomanDecider(setup, routes, numbers));
        deciders.Register(HordeWeapons.Minigun, (setup, routes) => new HeavyDecider(setup, routes, numbers));
        deciders.Register(HordeWeapons.Shotgun, (setup, routes) => new GunnerDecider(setup, routes, numbers));
        deciders.Register(HordeWeapons.SniperRifle, (setup, routes) => new SniperDecider(setup, routes, numbers));
        deciders.Register(HordeWeapons.Knife, (setup, routes) => new SpyDecider(setup, routes, numbers));
        deciders.Register(HordeWeapons.SentryBuster, (setup, routes) => new BusterDecider(setup, routes, numbers));
    }
}
