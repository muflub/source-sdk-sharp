using TF2.Descent.Rules.Robots;

namespace TF2.Descent.Server.Horde.Attacks;

/// <summary>
/// What a robot's attack sounds like and which of the model's sequences it plays (HO7): TF2's
/// own weapon sounds by family, and the player animations' attack activity the robot models
/// carry (plan 3.11 row 2). A presentation table beside <see cref="RobotLooks"/>, not a number
/// the rules own. A family whose TF sound loops (the minigun's spin, the flamethrower's roar)
/// plays none: a loop started for one attack would need a stop no attack sends.
/// </summary>
public static class RobotAttackLooks
{
    /// <summary>The sound an attack of <paramref name="t"/>'s family makes when it starts (a swing, a shot, a launch); null for none.</summary>
    public static string? FireSound(RobotTemplate t) => t.Family switch
    {
        "melee" => t.Class == "spy" ? "Weapon_Knife.Miss" : "Weapon_Fist.Miss",
        "scattergun" => "Weapon_Scatter_Gun.Single",
        "shotgun" => "Weapon_Shotgun.Single",
        "syringe_gun" => "Weapon_SyringeGun.Single",
        "sniper_rifle" => "Weapon_SniperRifle.Single",
        "rocket_launcher" => "Weapon_RPG.Single",
        "grenade_launcher" => "Weapon_GrenadeLauncher.Single",
        _ => null,
    };

    /// <summary>The sound a melee swing makes on what it hit; null for a family that does not swing.</summary>
    public static string? HitSound(RobotTemplate t) => t.Family == "melee" ? (t.Class == "spy" ? "Weapon_Knife.HitFlesh" : "Weapon_Fist.HitFlesh") : null;

    /// <summary>The player animations' standing attack activity for what the template holds.</summary>
    public static string AttackActivity(RobotTemplate t) => t.Family == "melee" ? "ACT_MP_ATTACK_STAND_MELEE" : "ACT_MP_ATTACK_STAND_PRIMARYFIRE";

    /// <summary>The activity to try when the model has none for <see cref="AttackActivity"/> (the gun's plain attack); null for none.</summary>
    public static string? AttackActivityElse(RobotTemplate t) => t.Family == "melee" ? null : "ACT_MP_ATTACK_STAND_PRIMARY";
}
