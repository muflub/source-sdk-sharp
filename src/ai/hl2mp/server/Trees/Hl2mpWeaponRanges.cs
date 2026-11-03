namespace SourceSharp.Ai.Hl2mp.Trees;

/// <summary>How an HL2MP weapon is fought with (plan_hl2mp_ai §5.4, §10's Aggression): its family, the range it is effective to, the range a bot keeps, and how it is fired.</summary>
public readonly record struct WeaponUse(string Family, float EffectiveRange, float PreferredRange, FireStyle Style, int Rank, bool Melee);

/// <summary>
/// The HL2MP weapons as a player fights with them, by class: the hitscan
/// guns' spread makes their reach, the shotgun's pellets keep it close, the
/// crossbow and the RPG reach across a map, the melee weapons touch. Rank
/// orders them for a bot choosing what to hold (higher is better in a fight
/// at the weapon's own range).
/// </summary>
public static class Hl2mpWeaponRanges
{
    public static WeaponUse For(string weaponClass) => weaponClass switch
    {
        "weapon_crowbar" => new("melee", 72f, 48f, FireStyle.Spray, 1, true),
        "weapon_stunstick" => new("melee", 72f, 48f, FireStyle.Spray, 2, true),
        "weapon_physcannon" => new("physgun", 160f, 128f, FireStyle.Tap, 0, false),
        "weapon_pistol" => new("pistol", 1024f, 384f, FireStyle.Tap, 3, false),
        "weapon_357" => new("pistol", 1600f, 512f, FireStyle.Tap, 6, false),
        "weapon_smg1" => new("smg", 1024f, 384f, FireStyle.Burst, 5, false),
        "weapon_ar2" => new("rifle", 1600f, 512f, FireStyle.Burst, 8, false),
        "weapon_shotgun" => new("shotgun", 400f, 160f, FireStyle.Tap, 7, false),
        "weapon_crossbow" => new("sniper", 3000f, 1024f, FireStyle.Tap, 6, false),
        "weapon_rpg" => new("rocket", 3000f, 768f, FireStyle.Tap, 7, false),
        "weapon_frag" => new("grenade", 700f, 450f, FireStyle.Tap, 2, false),
        "weapon_slam" => new("explosive", 400f, 256f, FireStyle.Tap, 1, false),
        _ => new("unknown", 512f, 256f, FireStyle.Tap, 0, false),
    };
}
