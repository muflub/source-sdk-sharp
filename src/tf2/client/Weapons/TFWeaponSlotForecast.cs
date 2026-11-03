using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Client.Weapons;

/// <summary>
/// The slot binds' forecast (<see cref="IWeaponSlotForecast"/>): the catalog
/// type the local player's class equips in a loadout row, on the same index
/// the wire's <c>weaponselect</c> names. Stock's slot binds walked the
/// client's own weapon entities; ours has the ONE re-dressed ss_weapon per
/// player, so the row is read from the loadout instead — the schema's base
/// item for the class and slot (<c>CTFInventoryManager::GetBaseItemForClass</c>'s
/// answer), its <c>item_class</c> translated to the class's own weapon
/// (<see cref="TFItemClassTranslation.Translate"/>, stock's
/// TranslateWeaponEntForClass), and that name's index in the catalog both
/// sides build.
/// </summary>
[SideService(Side.Client, As = [typeof(IWeaponSlotForecast)])]
public sealed class TFWeaponSlotForecast(ItemSchema schema, TFClassTable classes, TFClientCarriers carriers,
                                         WeaponCatalog weapons, IEngineHud screen) : IWeaponSlotForecast
{
    /// <inheritdoc/>
    public int TypeAt(int slot)
    {
        int local = screen.LocalPlayerIndex;

        if (local <= 0
            || carriers.Of(local) is not { } carrier
            || carrier.State.PlayerClass is not (>= (int)TFClass.Scout and < (int)TFClass.CountAll))
        {
            return -1;
        }

        var cls = (TFClass)carrier.State.PlayerClass;

        if (SlotFor(cls, slot) is not { } row
            || schema.BaseItem(cls, row) is not { } item
            || item.ItemClass is not { Length: > 0 } itemClass)
        {
            return -1;
        }

        return weapons.IndexOf(TFItemClassTranslation.Translate(itemClass, classes[cls]));
    }

    /// <summary>
    /// The loadout row each number key names for the class — stock's
    /// <c>g_SlotsToLoadoutSlotsPerClass</c> (item_quickswitch.cpp:38), walked
    /// for the keys this build binds (slot1-slot6, the digits 1-6). The row is
    /// the class's own, not the raw digit: the engineer's and spy's keys 4 and
    /// 5 name the PDA rows, and the spy starts at Secondary because his
    /// revolver's row is a secondary item.
    /// </summary>
    private static TFLoadoutSlot? SlotFor(TFClass cls, int digit) => cls switch
    {
        TFClass.Spy => digit switch
        {
            0 => TFLoadoutSlot.Secondary,
            2 => TFLoadoutSlot.Melee,
            3 => TFLoadoutSlot.Pda,
            4 => TFLoadoutSlot.Pda2,
            5 => TFLoadoutSlot.Head,
            _ => null,
        },
        TFClass.Engineer => digit switch
        {
            0 => TFLoadoutSlot.Primary,
            1 => TFLoadoutSlot.Secondary,
            2 => TFLoadoutSlot.Melee,
            3 => TFLoadoutSlot.Pda,
            4 => TFLoadoutSlot.Pda2,
            5 => TFLoadoutSlot.Head,
            _ => null,
        },
        _ => digit switch
        {
            0 => TFLoadoutSlot.Primary,
            1 => TFLoadoutSlot.Secondary,
            2 => TFLoadoutSlot.Melee,
            5 => TFLoadoutSlot.Head,
            _ => null,
        },
    };
}
