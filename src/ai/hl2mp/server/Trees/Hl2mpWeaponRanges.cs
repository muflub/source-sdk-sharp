namespace SourceSharp.Ai.Hl2mp.Trees;

/// <summary>How an HL2MP weapon is fought with (plan_hl2mp_ai §5.4, §10's Aggression): its family, the range it is effective to, the range a bot keeps, and how it is fired.</summary>
public readonly record struct WeaponUse(string Family, float EffectiveRange, float PreferredRange, FireStyle Style, int Rank, bool Melee, AimPoint Aim = AimPoint.Shoulder);

/// <summary>
/// How high a player's model shows each aim point over its feet (§3.4.1, ruling 2026-11-03), as a
/// client draws it: <c>cs_sv_model_heights</c>, idle, on Combine Super Soldier, Police, male_07 and
/// female_06 in both stances, and five bots' models standing. The upper arms' joints are 50 to 57 u
/// standing and 36.1 to 36.8 u crouched; a head's hitbox runs about 54 to 68 u standing and 33 to 48 u
/// crouched. The shoulder point sits at the low end of those arms, 50 and 32, because a crosshair's
/// wander goes both ways: at the joints' own 54, half of it passed over a shoulder and beside the head
/// (§12.3), losing Medium hits the user likes and trading them for headshots at three times the damage.
/// </summary>
public static class Hl2mpAimHeights
{
    /// <summary>Standing, the height a place is aimed at when nobody is there to see.</summary>
    public const float ShoulderStanding = 50f;

    public static StanceHeight Shoulder => new(ShoulderStanding, 32f);

    public static StanceHeight Body => new(48f, 24f);

    public static StanceHeight Head => new(61f, 41f);

    public static StanceHeight Of(AimPoint point) => point switch
    {
        AimPoint.Shoulder => Shoulder,
        AimPoint.Head => Head,
        _ => Body,
    };
}

/// <summary>
/// The HL2MP weapons as a player fights with them, by class: the hitscan
/// guns' spread makes their reach, the shotgun's pellets keep it close, the
/// crossbow and the RPG reach across a map, the melee weapons touch. Rank
/// orders them for a bot choosing what to hold (higher is better in a fight
/// at the weapon's own range). A blast is aimed low, where a miss still lands by the body, and a swing
/// at the chest as ever; the guns at the shoulders.
/// </summary>
public static class Hl2mpWeaponRanges
{
    public static WeaponUse For(string weaponClass) => weaponClass switch
    {
        "weapon_crowbar" => new("melee", 72f, 48f, FireStyle.Spray, 1, true, AimPoint.Body),
        "weapon_stunstick" => new("melee", 72f, 48f, FireStyle.Spray, 2, true, AimPoint.Body),
        "weapon_physcannon" => new("physgun", 160f, 128f, FireStyle.Tap, 0, false),
        "weapon_pistol" => new("pistol", 1024f, 384f, FireStyle.Tap, 3, false),
        "weapon_357" => new("pistol", 1600f, 512f, FireStyle.Tap, 6, false),
        "weapon_smg1" => new("smg", 1024f, 384f, FireStyle.Burst, 5, false),
        "weapon_ar2" => new("rifle", 1600f, 512f, FireStyle.Burst, 8, false),
        "weapon_shotgun" => new("shotgun", 400f, 160f, FireStyle.Tap, 7, false),
        "weapon_crossbow" => new("sniper", 3000f, 1024f, FireStyle.Tap, 6, false),
        "weapon_rpg" => new("rocket", 3000f, 768f, FireStyle.Tap, 7, false, AimPoint.Body),
        "weapon_frag" => new("grenade", 700f, 450f, FireStyle.Tap, 2, false, AimPoint.Body),
        "weapon_slam" => new("explosive", 400f, 256f, FireStyle.Tap, 1, false, AimPoint.Body),
        _ => new("unknown", 512f, 256f, FireStyle.Tap, 0, false),
    };
}
