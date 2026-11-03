//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Server.Weapons;

/// <summary>
/// The PDA's item switch, the core arsenal's walk:
/// <c>CTFWeaponPDA::PrimaryAttack</c> (<c>shared/tf/tf_weapon_pda.cpp:89-98</c>)
/// sends the owner back to the item held before this with one call,
/// <c>pOwner-&gt;SelectLastItem()</c>; the remembered weapon, its null check
/// and the active weapon's <c>CanHolster</c> gate are
/// <c>CBasePlayer::SelectLastItem</c>'s (<c>shared/baseplayer_shared.cpp:862-871</c>)
/// — all three are <see cref="Arsenal.SwitchLast"/>, the same history the
/// <c>lastinv</c> command rides. The command carries the owner's entity index;
/// the handle comes from the players line, as the other owners ask it.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFItemSwitchEffects)])]
public sealed class TFPDAItemSwitch(Arsenal arsenal, IEnginePlayers players) : ITFItemSwitchEffects
{
    public void SelectLast(in WeaponAttackContext attack, TFWeaponScript script)
        => arsenal.SwitchLast(players.GetHandle(attack.Command.OwnerIndex));
}
