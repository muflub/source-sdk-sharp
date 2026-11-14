namespace TF2.Descent.Ai.Brains;

/// <summary>
/// A robot's weapon family as the horde's brains see it (plan_descent_logic
/// 3.6): the <see cref="SourceSharp.Ai.SelfState.ActiveWeapon"/> number a body's
/// view carries (a field the recording writes, so a decider may read it), from
/// robots.json's attack (its family's row, or the row it names: the spy's knife,
/// the sentry buster's blast, HO12); and the range the family fights at, which a
/// decider scales by the robot's temperament (preferredRangeScale, Aggression).
/// 0 is no family. The ranges are the families' own data, not a temperament's.
/// </summary>
public static class HordeWeapons
{
    public const int None = 0, Melee = 1, Scattergun = 2, RocketLauncher = 3, GrenadeLauncher = 4, Flamethrower = 5,
                     Minigun = 6, Shotgun = 7, SyringeGun = 8, SniperRifle = 9, Knife = 10, SentryBuster = 11;

    /// <summary>HO13: each boss fights with an attack row of its own (Sir Nukesalot's cannon, Major Crits' rockets, Captain Punch's fists), and the Tank rides its track.</summary>
    public const int NukeCannon = 12, CritRockets = 13, BossFists = 14, TankTrack = 15;

    /// <summary>Family numbers in use, and one for none: 0 to <see cref="Count"/> - 1.</summary>
    public const int Count = 16;

    /// <summary>The family number of robot_attacks.json's row <paramref name="family"/> (a template's <c>Attack</c>); <see cref="None"/> for one the table does not know.</summary>
    public static int Of(string? family) => family switch
    {
        "knife" => Knife,
        "sentry_buster" => SentryBuster,
        "melee" => Melee,
        "scattergun" => Scattergun,
        "rocket_launcher" => RocketLauncher,
        "grenade_launcher" => GrenadeLauncher,
        "flamethrower" => Flamethrower,
        "minigun" => Minigun,
        "shotgun" => Shotgun,
        "syringe_gun" => SyringeGun,
        "sniper_rifle" => SniperRifle,
        "nuke_cannon" => NukeCannon,
        "crit_rockets" => CritRockets,
        "boss_fists" => BossFists,
        "tank_track" => TankTrack,
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
        Knife => 64f,
        SentryBuster => 100f,
        NukeCannon => 560f,
        CritRockets => 640f,
        BossFists => 64f,
        TankTrack => 0f,
        _ => 256f,
    };

    /// <summary>The family's name, as robot_attacks.json spells its row.</summary>
    public static string NameOf(int family) => family switch
    {
        Melee => "melee",
        Scattergun => "scattergun",
        RocketLauncher => "rocket_launcher",
        GrenadeLauncher => "grenade_launcher",
        Flamethrower => "flamethrower",
        Minigun => "minigun",
        Shotgun => "shotgun",
        SyringeGun => "syringe_gun",
        SniperRifle => "sniper_rifle",
        Knife => "knife",
        SentryBuster => "sentry_buster",
        NukeCannon => "nuke_cannon",
        CritRockets => "crit_rockets",
        BossFists => "boss_fists",
        TankTrack => "tank_track",
        _ => "no family",
    };
}
