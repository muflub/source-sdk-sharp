//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Projectiles;
using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Medigun;

/// <summary>
/// The Quick-Fix shares its patient's blast jump (<c>CTFPlayer::OnTakeDamage</c>,
/// <c>tf_player.cpp:9797-9829</c>): every medic healing the jumper with a
/// Quick-Fix out (the active medigun's <c>set_weapon_mode</c> 2,
/// <c>MEDIGUN_QUICKFIX</c>) is let off the ground and thrown along the
/// patient's velocity at its speed, at most 900.
/// </summary>
[SideService(Side.Server)]
public sealed class TFQuickFixJumpShare(ITFPlayerHealing healing, IEnginePlayers players, IEngineEntities entities,
                                        TFWeaponScripts scripts, ITFWeaponOwners owners, AttributeHooks hooks)
    : ITFSelfBlastHook
{
    /// <summary>The shared throw's ceiling (<c>MIN( flForce, 900.f )</c>, :9822).</summary>
    public const float MaxForce = 900f;

    public void SelfBlastJumped(EntityHandle player)
    {
        Vector velocity = entities.GetVelocity(player);
        float force = MathF.Min(velocity.Length, MaxForce);              // :9821-9822
        Vector impulse = velocity.Normalized * force;                    // :9823-9824

        foreach (int medicIndex in healing.HealerIndexes(player.EntryIndex))
        {
            EntityHandle medic = players.GetHandle(medicIndex);           // ToTFPlayer (:9803)
            if (medic == EntityHandle.None || !entities.IsPlayer(medic))
                continue;

            // GetActiveTFWeapon as a CWeaponMedigun of type MEDIGUN_QUICKFIX (:9808-9809).
            if (scripts.GetQuiet(players.GetActiveWeapon(medic)) is not { Id: TFWeaponId.Medigun }
                || hooks.Int(0, "set_weapon_mode", owners.ActiveItemOf(medicIndex)) != TFPlayerHealing.QuickFixWeaponMode)
                continue;

            entities.ApplyVelocityImpulse(medic, impulse, clearGround: true);   // :9825-9826
        }
    }
}
