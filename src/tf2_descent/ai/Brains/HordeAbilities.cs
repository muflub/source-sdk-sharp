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
}
