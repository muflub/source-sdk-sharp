//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Client.Ui;

/// <summary>
/// <c>CTFWeaponBase::UberChargeAmmoPerShot</c> (tf_weaponbase.h:292-299): the
/// weapon item's <c>ubercharge_ammo</c> attribute over 100 — the uberspend
/// one attack makes, priced off the carried item as stock's attrib hook walks
/// the weapon (<c>CALL_ATTRIB_HOOK_FLOAT</c> over the item view). The HUD's
/// two gates read it: the ammo panel refuses a weapon that spends it
/// (tf_hud_ammostatus.cpp:150-151) and the charge meter's syringe arm wants
/// one that does (tf_hud_mediccharge.cpp:163-167).
/// </summary>
internal static class TFUberSpend
{
    private const string Attribute = "ubercharge_ammo";

    internal static float PerShot(AttributeHooks hooks, ITFWeaponOwners owners, int playerIndex, string weapon)
        => hooks.Float(0f, Attribute, owners.ItemOf(playerIndex, weapon)) * 0.01f;
}
