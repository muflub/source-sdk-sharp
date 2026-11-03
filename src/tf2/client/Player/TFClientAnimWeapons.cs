//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Shared;
using TF2.Shared.Anim;

namespace TF2.Client.Player;

/// <summary>
/// The anim layer's weapon read on the client: the local player's weapon in
/// hand (the arsenal), its role <c>CTFWeaponBase::GetActivityWeaponRole</c>
/// (tf_weaponbase.cpp:4185-4205) — the script's <c>WeaponType</c>, the base
/// item's <c>anim_slot</c> row over it. The client knows no other player's
/// weapon (no networked weapon slot yet), so anyone else is stock's
/// no-weapon arm, as is a weapon the scripts do not name. No disguise weapon
/// is networked yet either.
/// </summary>
[SideService(Side.Client, As = [typeof(ITFAnimWeapons)])]
public sealed class TFClientAnimWeapons(IEngineHud hud, ILocalArsenal arsenal, TFWeaponTypes types, TFWeaponScripts scripts) : ITFAnimWeapons
{
    public TFAnimWeapon? ActiveWeapon(int clientIndex)
    {
        if (clientIndex != hud.LocalPlayerIndex || !arsenal.TryGetActive(out string name, out _, out _))
            return null;
        if (string.IsNullOrEmpty(name) || scripts.Get(name) is not { } script)
            return null;
        return new TFAnimWeapon(types.ActivityWeaponRole(name, (TFWeaponRole)(int)script.Category), script.Id);
    }

    public TFAnimWeapon? DisguiseWeapon(int playerIndex) => null;
}
