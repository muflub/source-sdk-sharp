namespace TF2.Descent.Ai.Brains;

/// <summary>
/// The horde's abilities by row (the core's <c>ActionKind.Ability</c> carries the row in
/// <c>ActionIntent.WeaponSlot</c>, so the core names no game's verb): what a decider asks of
/// its body besides firing its weapon.
/// </summary>
public static class HordeAbilities
{
    /// <summary>A medic's heal beam on the pack-mate its look names (HO6).</summary>
    public const int Heal = 1;

    /// <summary>A spy's cloak on (HO12): its body fades from the heroes' eyes until it uncloaks or attacks.</summary>
    public const int Cloak = 2;

    /// <summary>A spy's cloak off (HO12): the uncloak's sound and fade-in, its telegraph before a stab.</summary>
    public const int Decloak = 3;

    /// <summary>A boss's special (HO13): the windup of its phase's big attack; the ask's <c>OneShotSeq</c> is the phase.</summary>
    public const int BossSpecial = 4;

    /// <summary>A boss calling its phase's adds (HO13); the ask's <c>OneShotSeq</c> is the phase.</summary>
    public const int BossSummon = 5;

    /// <summary>The Tank stopped at a station of its track (HO13); the ask's <c>OneShotSeq</c> is the station, the last its final.</summary>
    public const int TankStation = 6;

    /// <summary>The Tank has drilled its last station: the roof comes down (HO13).</summary>
    public const int TankRoof = 7;
}
