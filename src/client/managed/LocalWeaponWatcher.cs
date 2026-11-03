namespace SourceSharp.Client;

/// <summary>
/// A side service that watches the local player's weapon entity. The arsenal
/// re-resolves the local pair every frame, and when it adopts a weapon entity
/// it was not holding before — the first one at a join, each respawn kit's
/// fresh entity — it calls <see cref="LocalWeaponSpawned"/>. This is the
/// client's half of <c>CBaseCombatWeapon</c>'s constructor
/// (basecombatweapon_shared.cpp:103-106) refilling a fresh weapon's crit
/// bucket: the engine's entity create is otherwise invisible to client code,
/// so the arsenal's adoption is the spawn the client gets.
/// </summary>
public interface ILocalWeaponWatcher
{
    /// <summary>
    /// Weapon entity <paramref name="weaponIndex"/> is the local player's,
    /// as a fresh entity. The arsenal reports the handle's entry index, the
    /// number the replayed commands carry into a behaviour's per-weapon
    /// state.
    /// </summary>
    void LocalWeaponSpawned(int weaponIndex);
}