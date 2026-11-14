using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
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
/// re-dressed ss_weapon per player, so the candidates are, first, the weapons
/// the player carries — the published selection mask
/// (<see cref="PlayerNetState.CarriedWeapons"/>, the bits the server's
/// <c>PublishSelection</c> lifted from its carry): the carried type whose hud
/// slot (<see cref="TFWeaponSlots"/>, the builder's objects.txt override
/// included) is the key's, lowest position first, is the type the key names,
/// exactly the entity walk's answer and the equipped unlock's too. The mask
/// is the server's word, though: until its first publish lands it is zero and
/// says nothing, so the empty mask falls back to the class's loadout rows —
/// each the schema's base item
/// (<c>CTFInventoryManager::GetBaseItemForClass</c>'s answer), its
/// <c>item_class</c> translated to the class's own weapon
/// (<see cref="TFItemClassTranslation.Translate"/>, stock's
/// TranslateWeaponEntForClass) — the kit the player is about to receive.
/// </summary>
[SideService(Side.Client, As = [typeof(IWeaponSlotForecast)])]
public sealed class TFWeaponSlotForecast(ItemSchema schema, TFClassTable classes, TFClientCarriers carriers,
                                         WeaponCatalog weapons, IEngineHud screen, IWeaponSlots slots,
                                         IClientStandalones standalones) : IWeaponSlotForecast
{
    private readonly IWeaponSlots _slots = slots;
    private readonly IClientStandalones _standalones = standalones;

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

        // The carried walk: the published mask's weapons, the key's slot, the
        // server's carry — the local player's own entity, as LocalWeaponSelection
        // reads it. PlayerNetState.WeaponMaskBits is the mask's width — a type
        // at or past it has no bit to read.
        UInt128 carried = _standalones.GetByIndex(local) is ClientSsPlayer held
            ? held.State.CarriedMask
            : default;
        if (carried != 0)
        {
            int best = -1;
            int bestPosition = int.MaxValue;
            int types = Math.Min(weapons.Count, PlayerNetState.WeaponMaskBits);
            for (int type = 0; type < types; type++)
            {
                // VisibleInWeaponSelection (weapon_selection.cpp:637): the spy's
                // watch and the engineer's builder have a bucket and no key.
                if ((carried & ((UInt128)1 << type)) == 0 || _slots.Slot(type) != slot || !_slots.VisibleInSelection(type))
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

        // No published mask yet: the loadout walk, the kit the server is about
        // to grant.
        int fallback = -1;
        int fallbackPosition = int.MaxValue;
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
            if (position < fallbackPosition)
            {
                fallbackPosition = position;
                fallback = type;
            }
        }

        return fallback;
    }
}
