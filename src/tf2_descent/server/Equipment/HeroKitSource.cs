using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Econ;
using TF2.Shared;

using TFItem = TF2.Server.Econ.ItemInstance;

namespace TF2.Descent.Server.Gear;

/// <summary>
/// TF2 Descent's item source (<see cref="ITFItemSource"/>): a hero's weapon slots are his
/// equipped Descent items (<see cref="HeroEquipment.KitAnswer(int, TFClass, TFLoadoutSlot)"/>),
/// and everyone else's, and every other slot, is TF2's own answer. It keeps what it last
/// answered each player's weapon slots, which is what TF2 last built that kit from: the
/// settle (<see cref="HeroKitSettle"/>) refreshes the kit only when the answer now differs.
/// </summary>
[SideService(Side.Server)]
public sealed class HeroKitSource(HeroEquipment equipment) : ITFItemSource, IOrderedHook
{
    /// <summary>After TF2's own source (999,061), in TF2 Descent's equipment block.</summary>
    public const int Order = ModStages.First + 400;

    private readonly Dictionary<(int Player, TFLoadoutSlot Slot), TFItem?> _answered = new();

    public int HookOrder => Order;

    public TFSlotItem ItemInLoadout(int entIndex, TFClass cls, TFLoadoutSlot slot)
    {
        var answer = equipment.KitAnswer(entIndex, cls, slot);
        if (HeroEquipment.EquipmentSlotOf(slot) is not null)
        {
            if (answer.HasOpinion)
                _answered[(entIndex, slot)] = answer.Item;
            else
                _answered.Remove((entIndex, slot));
        }
        return answer;
    }

    /// <summary>
    /// True when what the player's weapon slots would be answered now is not what they were
    /// last answered (an item in a slot is compared as a kit compares it,
    /// <see cref="TFItem.SameAs"/>).
    /// </summary>
    public bool Differs(int entIndex, TFClass cls)
    {
        for (var slot = TFLoadoutSlot.Primary; slot <= TFLoadoutSlot.Melee; slot++)
        {
            var now = equipment.KitAnswer(entIndex, cls, slot);
            bool had = _answered.TryGetValue((entIndex, slot), out var was);
            if (now.HasOpinion != had)
                return true;
            if (!now.HasOpinion)
                continue;
            if (now.Item is null != was is null)
                return true;
            if (now.Item is { } item && !item.SameAs(was!))
                return true;
        }
        return false;
    }

    /// <summary>The player left: nothing was answered for the next one in the slot.</summary>
    public void Forget(int entIndex)
    {
        for (var slot = TFLoadoutSlot.Primary; slot <= TFLoadoutSlot.Melee; slot++)
            _answered.Remove((entIndex, slot));
    }
}
