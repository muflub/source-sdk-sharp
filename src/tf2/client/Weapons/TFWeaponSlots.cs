//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Shared;

using TF2.Client.Player;
using TF2.Shared;

namespace TF2.Client.Weapons;

/// <summary>
/// The hud's slot table: <c>CBaseCombatWeapon::GetSlot</c>'s script bucket and
/// position for every row, and <c>CTFWeaponBuilder</c>'s override for the
/// builder family — a builder's place is the <c>objects.txt</c> row of the
/// object it builds (<c>GetSlot</c>/<c>GetPosition</c>,
/// tf_weapon_builder.cpp:1154-1165), not its own bucket. That override is the
/// spy's slot 2: his sapper is OBJ_ATTACHMENT_SAPPER, whose
/// <c>SelectionSlot 1</c> lifts <c>tf_weapon_builder</c> out of bucket 5 into
/// the second key, leaving the sixth empty as stock's hud has it.
///
/// <para>
/// The builder builds the class's first buildable — stock's
/// <c>ManageBuilderWeapons</c> retargets the one default builder over the
/// class's objects as it walks them (tf_player.cpp:4309-4316), and the
/// <c>SelectionSlot</c> that decides is the per-class buildables' answer; the
/// spy's single sapper is the case the slot keys meet, and the engineer's
/// first buildable is the sentry, whose row is his script bucket.
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(IWeaponSlots)])]
public sealed class TFWeaponSlots(WeaponCatalog weapons, WeaponScriptDatabase scripts,
                                  TFClientCarriers carriers, IEngineHud screen, TFClassTable classes)
    : IWeaponSlots
{
    private int[] _slot = [];
    private int[] _position = [];

    // The class the table was filled for: the builder's override is the local
    // class's, so a class change re-reads every row.
    private TFClass _class;

    public int Count => weapons.Count;

    public int Slot(int weaponType) => Row(weaponType) ? _slot[weaponType] : -1;

    public int Position(int weaponType) => Row(weaponType) ? _position[weaponType] : -1;

    private bool Row(int weaponType)
    {
        if (weaponType < 0 || weaponType >= weapons.Count)
            return false;

        TFClass? local = LocalClass();
        if (_slot.Length != weapons.Count || local != _class)
            Fill(local);

        return true;
    }

    private void Fill(TFClass? local)
    {
        _class = local ?? TFClass.Undefined;
        _slot = new int[weapons.Count];
        _position = new int[weapons.Count];

        // The class's first buildable, where the builder's override reads its
        // objects.txt row; none for a class that builds nothing.
        TFObjectType? built = null;
        if (local is { } cls)
        {
            foreach (var buildable in classes[cls].Buildables)
            {
                if (buildable == TFObjectType.Last)
                    continue;
                built = buildable;
                break;
            }
        }

        for (int t = 0; t < _slot.Length; t++)
        {
            WeaponScript? script = weapons.ByIndex(t) is { } type ? scripts.ReadScript(type.Name) : null;
            (int slot, int position) = script is null ? (-1, -1) : (script.Slot, script.Position);

            if (built is { } objectBuilt && IsBuilder(weapons.ByIndex(t)?.Name))
                (slot, position) = Selection(objectBuilt);

            _slot[t] = slot;
            _position[t] = position;
        }
    }

    /// <summary>The local player's class, as the forecast reads it; null without one.</summary>
    private TFClass? LocalClass()
    {
        int local = screen.LocalPlayerIndex;

        if (local <= 0
            || carriers.Of(local) is not { } carrier
            || carrier.State.PlayerClass is not (>= (int)TFClass.Scout and < (int)TFClass.CountAll))
        {
            return null;
        }

        return (TFClass)carrier.State.PlayerClass;
    }

    /// <summary>The builder family: the classes <c>CTFWeaponBuilder</c> covers, the builder and the sapper derived from it.</summary>
    private static bool IsBuilder(string? weapon) => weapon is Builder or TFWeaponTypes.Sapper;

    /// <summary><c>TF_WEAPON_BUILDER</c> as the rows spell it.</summary>
    private const string Builder = "tf_weapon_builder";

    /// <summary>
    /// The shipped <c>objects.txt</c>'s <c>SelectionSlot</c>/<c>SelectionPosition</c>
    /// per buildable — the three engineer objects in the fifth bucket, the
    /// sapper alone in the second.
    /// </summary>
    private static (int Slot, int Position) Selection(TFObjectType type) => type switch
    {
        TFObjectType.Dispenser => (5, 1),
        TFObjectType.Teleporter => (5, 2),
        TFObjectType.Sentrygun => (5, 0),
        TFObjectType.AttachmentSapper => (1, 0),
        _ => (0, 0),
    };
}
