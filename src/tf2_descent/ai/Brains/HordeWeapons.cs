namespace TF2.Descent.Ai.Brains;

/// <summary>
/// A robot's weapon family as the horde's brains see it (plan_descent_logic
/// 3.6): the <see cref="SourceSharp.Ai.SelfState.ActiveWeapon"/> number a body's
/// view carries (a field the recording writes, so a decider may read it), from
/// robots.json's family id; and the range the family fights at, which a
/// decider scales by the robot's temperament (preferredRangeScale, Aggression).
/// 0 is no family. The ranges are the families' own data, not a temperament's.
/// </summary>
public static class HordeWeapons
{
    public const int None = 0, Melee = 1, Scattergun = 2, RocketLauncher = 3, GrenadeLauncher = 4, Flamethrower = 5,
                     Minigun = 6, Shotgun = 7, SyringeGun = 8, SniperRifle = 9;

    /// <summary>The family number of robots.json's family <paramref name="family"/>; <see cref="None"/> for one the table does not know.</summary>
    public static int Of(string? family) => family switch
    {
        "melee" => Melee,
        "scattergun" => Scattergun,
        "rocket_launcher" => RocketLauncher,
        "grenade_launcher" => GrenadeLauncher,
        "flamethrower" => Flamethrower,
        "minigun" => Minigun,
        "shotgun" => Shotgun,
        "syringe_gun" => SyringeGun,
        "sniper_rifle" => SniperRifle,
        _ => None,
    };

    /// <summary>The distance in units a family closes to before it stops and faces its target, at a temperament's neutral range scale of 1.</summary>
    public static float EngageRange(int family) => family switch
    {
        Melee => 64f,
        Scattergun => 320f,
        RocketLauncher => 640f,
        GrenadeLauncher => 560f,
        Flamethrower => 192f,
        Minigun => 640f,
        Shotgun => 384f,
        SyringeGun => 480f,
        SniperRifle => 1600f,
        _ => 256f,
    };
}
