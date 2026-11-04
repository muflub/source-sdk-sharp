//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ui;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Client.Ui;

/// <summary>
/// The weapon selection's reads, stock's <c>CHudWeaponSelection</c> over the
/// base's state: the draw gate is the core selection's own
/// (<see cref="WeaponSelectionHud.Showing"/>, the buckets arm of
/// <c>ShouldDrawInternal</c>, tf_hud_weaponselection.cpp:315-349); the
/// selected slot is the selection's weapon's (<c>iActiveSlot</c>, :707); each
/// card is the slot's last held weapon, as the item panel is set
/// (<c>SetItem</c> per position, :551-576, the last visible winning), with
/// the item's inventory image the picture and the script's art for the
/// player's team (<c>GetSpriteInactive</c>, or <c>GetSpriteActive</c> on
/// BLU, :806-813 — one texture either way) its stand-in, and the printname
/// localised; and the note is <c>CanBeSelected</c>'s false
/// (:745-752). A slot whose later positions carry weapons the hud does not
/// name shows the last named one: stock's panel is set per position and the
/// unnamed weapon's item has no card of its own.
///
/// <para>
/// The hud's timeouts ride the composition too: stock's element answers
/// <c>OnThink</c> itself with 2.5 s and 3.0 s
/// (tf_hud_weaponselection.cpp:40-41, :259-267) where the core selection
/// state carries the HL2 pair its OnThink was ported with — the TF numbers
/// are set on it once, here. The fastswitch pair keeps the core's: this hud
/// draws the buckets (hud_fastswitch 0, TF2's default), and the mode is read
/// fresh every think either way.
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(ITFWeaponSelect)])]
public sealed class TFWeaponSelectUi : ITFWeaponSelect
{
    /// <summary>Stock's SELECTION_TIMEOUT_THRESHOLD and
    /// SELECTION_FADEOUT_TIME (tf_hud_weaponselection.cpp:40-41).</summary>
    internal const float SelectionTimeout = 2.5f, SelectionFadeoutTime = 3.0f;

    private readonly WeaponSelectionHud _selection;
    private readonly WeaponCatalog _weapons;
    private readonly TFWeaponScripts _scripts;
    private readonly TFWeaponTypes _types;
    private readonly ITFLocalPlayer _player;

    public TFWeaponSelectUi(WeaponSelectionHud selection, WeaponCatalog weapons, TFWeaponScripts scripts,
                            IUiLocalisation localise, ITFLocalPlayer player, TFWeaponTypes types)
    {
        _selection = selection;
        _weapons = weapons;
        _scripts = scripts;
        _types = types;
        _player = player;

        // The hud's OnThink pair, over the core state's HL2 ones: the element
        // answers them itself in stock, and this hud's state is the one
        // ticker both huds read.
        selection.BucketTimeout = SelectionTimeout;
        selection.BucketFadeout = SelectionFadeoutTime;
    }

    public bool Showing => _selection.Showing;

    public int SelectedSlot => _selection.Selected >= 0 ? _selection.SlotOf(_selection.Selected) : -1;

    public bool SlotHasWeapon(int slot) => _selection.SlotHasWeapon(slot);

    public int Team => _player.Team;

    public TFSelectionWeapon WeaponInSlot(int slot)
    {
        int position = _selection.LastPositionInSlot(slot);
        int type = position >= 0 ? _selection.WeaponInSlot(slot, position) : -1;
        if (_weapons.ByIndex(type) is not { } weapon)
            return default;

        var script = _scripts.Get(weapon.Name);

        // The panel is set to the weapon's item (SetItem,
        // GetAttributeContainer->GetItem, tf_hud_weaponselection.cpp:560):
        // its name is the card's label and its inventory image the picture,
        // the schema's backpack art.
        var item = _types.BaseItemFor(weapon.Name);
        var icon = Team == 3 ? script?.IconActive ?? script?.IconInactive : script?.IconInactive;
        return new TFSelectionWeapon(
            icon?.File ?? "",
            icon?.X ?? 0,
            icon?.Y ?? 0,
            icon?.Wide ?? 0,
            icon?.Tall ?? 0,
            NameOf(weapon.Name, script, item),
            _selection.CanBeSelected(type),
            item?.InventoryImage ?? "");
    }

    // The card's label as the panel's item carries it: the item's name —
    // stock's panel is set per item (SetItem, GetAttributeContainer
    // ->GetItem, tf_hud_weaponselection.cpp:560), whose name is the item
    // definition's — else the script's printname verbatim, a #token the
    // screen's own localiser resolves, stock's Find with the raw string as
    // fallback — else the class name bare (the core WeaponNames' fallback).
    private static string NameOf(string className, TFWeaponScript? script, ItemDefinition? item)
    {
        string? itemName = item?.ItemName;
        if (!string.IsNullOrEmpty(itemName))
            return itemName;
        string token = script?.Base.PrintName ?? "";
        return token.Length > 0 ? token
            : className.StartsWith("weapon_", StringComparison.Ordinal) ? className["weapon_".Length..] : className;
    }
}
