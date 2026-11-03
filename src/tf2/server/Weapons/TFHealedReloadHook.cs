//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Server.Medigun;
using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Weapons;

/// <summary>
/// SetReloadTimer's while-healed arm, the chain's link after the king buff's
/// (<c>tf_weaponbase.cpp:2313-2317</c>): exactly one healer keeping the
/// shooter up, and the shooter's own attribute walk carries
/// <c>mult_reload_time_while_healed</c> into the reload. The count is the
/// medigun lane's — server state, so a client's replay of the command runs
/// the unscaled clock and the next command the server answers corrects it
/// (the client holds no healer list; docs/plans/tf2/deferred.tsv).
/// </summary>
[SideService(Side.Server)]
public sealed class TFHealedReloadHook(ITFPlayerHealing? healing, AttributeHooks attributes) : ITFWeaponHook
{
    /// <summary>After the king buff's reload arm (215); stock scales it last of the live arms.</summary>
    public const int Order = 1_000_000 - 1_000 + 216;

    public int HookOrder => Order;

    public float ModifyReloadTime(in TFWeaponHookContext weapon, float duration)
    {
        if (healing is null || weapon.OwnerIndex <= 0 || healing.NumHealers(weapon.OwnerIndex) != 1)
            return duration;
        return duration * attributes.Float(1f, "mult_reload_time_while_healed", weapon.Manager);
    }
}
