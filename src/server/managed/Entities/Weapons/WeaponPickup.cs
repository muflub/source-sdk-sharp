using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A weapon lying in the world — every weapon_* classname a map can place.
///
/// <para>
/// ONE CLASS, A NAME PER ROW, and the names come from the same table as everything
/// else about a weapon: each attribute below must have a row in
/// <see cref="WeaponTypes.All"/>, which is where the model on the ground and
/// the clip it hands over are declared. WeaponTests pins the two lists to each
/// other.
/// </para>
///
/// <para>
/// This replaces the native shell's registration path outright. The old
/// CSourceSharpWeapon was both the pickup on the ground AND the thing in the
/// hands; on the hierarchy those are two different things — this Item, and
/// <see cref="SsWeapon"/> — because they always were: one is walked into and
/// one is aimed.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.WeaponCrowbar,  ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponPistol,   ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.Weapon357,      ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponSmg1,     ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponAr2,      ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponShotgun,  ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponCrossbow, ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponRpg,      ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponFrag,     ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponStunstick, ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponSlam,     ScriptedEntityKind.Item)]
public sealed class WeaponPickup : PickupItem
{
    private readonly Arsenal _arsenal;

    // This side's weapon table, injected: the pickup for a weapon has to read
    // its world model from the SAME list the server gave from, and that list
    // now carries the mods' rows — a static here would stop at the core rows
    // and dress a mod weapon in a pistol's model.
    private readonly WeaponCatalog _weapons;

    public WeaponPickup(Arsenal arsenal, WeaponCatalog? weapons = null)
    {
        _arsenal = arsenal;
        _weapons = weapons ?? WeaponCatalog.Stock();
    }

    /// <summary>The w_ model, from the weapon's own row.</summary>
    protected override string Model
        => _weapons.For(ClassName)?.WorldModel ?? "models/weapons/w_pistol.mdl";

    /// <summary>
    /// HL2MP's weapon respawn cadence rather than the items' 20 — a deathmatch
    /// where the guns stay gone is a deathmatch decided by spawn order.
    /// </summary>
    protected override float RespawnDelay => 20f;

    protected override string Describe() => ClassName;

    /// <summary>
    /// Hands the weapon over. False for a player who already owns one of these
    /// AND has nothing to top up — the item stays for somebody it can help,
    /// which is how every Source pickup behaves.
    /// </summary>
    protected override bool TryGive(EntityHandle player)
        => _arsenal.GiveWeapon(player, ClassName);
}
