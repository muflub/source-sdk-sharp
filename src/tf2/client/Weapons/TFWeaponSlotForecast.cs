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
/// type at the HUD slot a number key names, on the same index the wire's
/// <c>weaponselect</c> carries. Stock's slot binds walked the client's own
/// weapon entities for one whose <c>GetSlot()</c> is the key's digit minus
/// one (weapon_selection.cpp's <c>SelectWeaponSlot</c>); ours has the ONE
/// re-dressed ss_weapon per player, so the candidates are the class's loadout
/// rows instead — each the schema's base item
/// (<c>CTFInventoryManager::GetBaseItemForClass</c>'s answer), its
/// <c>item_class</c> translated to the class's own weapon
/// (<see cref="TFItemClassTranslation.Translate"/>, stock's
/// TranslateWeaponEntForClass) — and the row whose hud slot
/// (<see cref="TFWeaponSlots"/>, the builder's objects.txt override included)
/// is the key's, lowest position first, is the type the key names.
/// </summary>
[SideService(Side.Client, As = [typeof(IWeaponSlotForecast)])]
public sealed class TFWeaponSlotForecast(ItemSchema schema, TFClassTable classes, TFClientCarriers carriers,
                                         WeaponCatalog weapons, IEngineHud screen, IWeaponSlots slots) : IWeaponSlotForecast
{
    private readonly IWeaponSlots _slots = slots;

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

        int best = -1;
        int bestPosition = int.MaxValue;
        for (var row = TFLoadoutSlot.Primary; row <= TFLoadoutSlot.Pda2; row++)
        {
            if (schema.BaseItem(cls, row) is not { } item
                || item.ItemClass is not { Length: > 0 } itemClass)
            {
                continue;
            }

            int type = weapons.IndexOf(TFItemClassTranslation.Translate(itemClass, classes[cls]));
            if (type < 0 || _slots.Slot(type) != slot)
                continue;

            int position = _slots.Position(type);
            if (position < bestPosition)
            {
                bestPosition = position;
                best = type;
            }
        }

        return best;
    }
}
