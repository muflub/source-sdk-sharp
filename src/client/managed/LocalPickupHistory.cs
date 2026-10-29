//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

namespace SourceSharp.Client;

/// <summary>
/// What feeds the pickup history, as stock's client feeds CHudHistoryResource:
/// a weapon the local player now carries (CBaseHudWeaponSelection::
/// OnWeaponPickup), a pool that rose since the last update (C_BasePlayer::
/// PostDataUpdate's m_iOldAmmo walk, unless the weapon that fires it has
/// ITEM_FLAG_NOAMMOPICKUPS), and the server's ItemPickup and AmmoDenied.
/// </summary>
public sealed class LocalPickupHistory : IClientLevelHook, IOrderedHook
{
    public LocalPickupHistory(PickupHistory history, IEngineHud screen, IClientStandalones standalones,
                              WeaponCatalog weapons, AmmoCatalog ammo, WeaponScriptDatabase scripts)
    {
        _history = history;
        _screen = screen;
        _standalones = standalones;
        _weapons = weapons;
        _ammo = ammo;
        _scripts = scripts;
    }

    private readonly PickupHistory _history;
    private readonly IEngineHud _screen;
    private readonly IClientStandalones _standalones;
    private readonly WeaponCatalog _weapons;
    private readonly AmmoCatalog _ammo;
    private readonly WeaponScriptDatabase _scripts;

    /// <summary>The weapons carried at the last look, as the server's bits.</summary>
    private uint _carried;

    /// <summary>m_iOldAmmo: each pool at the last look, by wire index.</summary>
    private readonly int[] _oldAmmo = new int[PlayerNetState.MaxAmmoSlots];

    private ClientSsPlayer? Local => _standalones.GetByIndex(_screen.LocalPlayerIndex) as ClientSsPlayer;

    /// <summary>The weapons the local player carries that have nothing to fire (the history draws them in caution).</summary>
    public uint EmptyWeapons => Local?.State.EmptyWeapons ?? 0u;

    public void Subscribe(IUserMessageEvent<string> items, IUserMessageEvent<int> denied)
    {
        items.Fired += OnItemPickup;
        denied.Fired += OnAmmoDenied;
    }

    /// <summary>Its place among the client's level hooks (the ropes are 0).</summary>
    public int HookOrder => 1;

    /// <summary>
    /// A new level: the HUD's VidInit runs ResetHUD, which empties the history,
    /// and the new level's local player starts with no weapons or ammo seen
    /// (a new C_BasePlayer, its m_iOldAmmo zero), so what it spawns with is
    /// listed again.
    /// </summary>
    void IClientLevelHook.OnLevelInit(string mapName)
    {
        _history.Reset();
        _carried = 0;
        Array.Clear(_oldAmmo);
    }

    /// <summary>MsgFunc_ItemPickup: only an item with an icon is listed.</summary>
    private void OnItemPickup(string item)
    {
        if (PickupIcons.Item(item) is { } icon)
            _history.AddItem(icon);
    }

    /// <summary>MsgFunc_AmmoDenied.</summary>
    private void OnAmmoDenied(int ammoType) => _history.AmmoDenied(ammoType, AmmoIcon(ammoType));

    /// <summary>Each frame: what arrived since the last, then the history's own expiry.</summary>
    public void Think()
    {
        if (Local is { } player)
        {
            uint carried = player.State.CarriedWeapons;
            uint added = carried & ~_carried;
            for (int type = 0; added != 0 && type < 32; type++)
            {
                if ((added & (1u << type)) == 0)
                    continue;

                added &= ~(1u << type);
                if (_weapons.ByIndex(type) is { } weapon)
                {
                    WeaponScript? script = _scripts.ReadScript(weapon.Name);
                    _history.AddWeapon(type, PickupIcons.Weapon(weapon.Name),
                                       script is not null && script.Flags.HasFlag(WeaponItemFlags.Exhaustible));
                }
            }

            _carried = carried;

            for (int ammo = 1; ammo < _oldAmmo.Length; ammo++)
            {
                int count = player.State.Ammo[ammo];
                if (count > _oldAmmo[ammo] && !NoAmmoPickups(ammo))
                    _history.AddAmmo(ammo, count - _oldAmmo[ammo], AmmoIcon(ammo));
                _oldAmmo[ammo] = count;
            }
        }

        _history.Think();
    }

    /// <summary>The ammo row's icon for a wire index (from one).</summary>
    private string AmmoIcon(int ammoType)
        => ammoType >= 1 && ammoType <= _ammo.Count ? PickupIcons.Ammo(_ammo.All[ammoType - 1].Name) : string.Empty;

    /// <summary>
    /// gWR.GetWeaponFromAmmo: the first weapon whose primary or secondary
    /// ammo it is; that weapon's ITEM_FLAG_NOAMMOPICKUPS keeps it out.
    /// </summary>
    private bool NoAmmoPickups(int ammoType)
    {
        if (ammoType < 1 || ammoType > _ammo.Count)
            return false;

        string name = _ammo.All[ammoType - 1].Name;
        for (int t = 0; t < _weapons.Count; t++)
        {
            if (_weapons.ByIndex(t) is not { } weapon || _scripts.ReadScript(weapon.Name) is not { } script)
                continue;

            if (string.Equals(script.Ammo1, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(script.Ammo2, name, StringComparison.OrdinalIgnoreCase))
                return script.Flags.HasFlag(WeaponItemFlags.NoAmmoPickups);
        }

        return false;
    }
}

/// <summary>
/// The icons the history draws, from the UI's icon set (original art): a
/// weapon's kill-feed icon, an ammo type's, and the three items stock's
/// hud_textures gives an icon. An ammo or item without one is not drawn, as
/// stock skips a row whose icon is missing.
/// </summary>
public static class PickupIcons
{
    public static string Weapon(string weapon)
        => weapon.StartsWith("weapon_", StringComparison.Ordinal) ? weapon["weapon_".Length..] : weapon;

    public static string Ammo(string ammo) => ammo.ToLowerInvariant() switch
    {
        "pistol" => "ammo_pistol",
        "smg1" => "ammo_smg1",
        "357" => "ammo_357",
        "buckshot" => "ammo_buckshot",
        "ar2" => "ammo_ar2",
        "grenade" => "frag",
        "smg1_grenade" => "ammo_smg1_grenade",
        "ar2altfire" => "combine_ball",
        "rpg_round" => "ammo_rpg_round",
        "xbowbolt" => "ammo_xbowbolt",
        "slam" => "slam",
        _ => string.Empty,
    };

    public static string? Item(string item) => item switch
    {
        "item_battery" or "item_healthkit" or "item_healthvial" => item,
        _ => null,
    };
}
