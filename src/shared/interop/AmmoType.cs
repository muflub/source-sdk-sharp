namespace SourceSharp.Abi;

/// <summary>
/// An ammo type the mod defines.
///
/// Carry limit is the interesting field: it is a game-balance number, which is
/// why these are declared by the game rules rather than by the weapon or the
/// player.
/// </summary>
public readonly record struct AmmoType(string Name, DamageType DamageType, int MaxCarry)
{
    /// <summary>
    /// Damage per bullet.
    ///
    /// On the ammo rather than on the weapon, which is how stock does it:
    /// FireBullets asks the ammo definition unless a shot carries an override,
    /// and HL2 sets these through its sk_plr_dmg_* convars. Two weapons sharing
    /// an ammo type therefore hit equally hard, and differ by rate of fire,
    /// spread and pellet count.
    /// </summary>
    public float Damage { get; init; }

    /// <summary>Tracer effect index; 0 is none.</summary>
    public int TracerType { get; init; }

    /// <summary>Physics impulse applied on hit.</summary>
    public float DamageForce { get; init; }
}
