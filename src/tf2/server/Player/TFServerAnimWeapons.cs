//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Shared;
using TF2.Shared.Anim;

namespace TF2.Server.Player;

/// <summary>
/// The anim layer's weapon read on the server: the player's active weapon by
/// the engine's answer, its role the script's <c>WeaponType</c>
/// (<c>CTFWeaponBase::GetActivityWeaponRole</c>'s base arm, tf_weaponbase.cpp,
/// <c>m_iWeaponType</c>). A weapon the scripts do not name, or no weapon, is
/// stock's no-weapon arm (null). The disguise weapon is the client's view arm
/// (<see cref="ITFAnimWeapons.DisguiseWeapon"/>): the server answers none.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFAnimWeapons)])]
public sealed class TFServerAnimWeapons(IEnginePlayers players, TFWeaponScripts scripts) : ITFAnimWeapons
{
    public TFAnimWeapon? ActiveWeapon(int playerIndex)
    {
        var handle = players.GetHandle(playerIndex);
        if (handle == EntityHandle.None)
            return null;
        string name = players.GetActiveWeapon(handle);
        if (string.IsNullOrEmpty(name) || scripts.Get(name) is not { } script)
            return null;
        return new TFAnimWeapon((TFWeaponRole)(int)script.Category, script.Id);
    }

    public TFAnimWeapon? DisguiseWeapon(int playerIndex) => null;
}
