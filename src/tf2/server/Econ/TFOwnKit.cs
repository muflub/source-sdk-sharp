using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Server.Econ;

/// <summary>One of an item's own attributes as its tooltip states it: the attribute's class and its value.</summary>
public readonly record struct TFOwnKitAttribute(string Class, float Value);

/// <summary>
/// One item of a player's kit as the player knows it from the loadout screen and the HUD's weapon list: the slot it
/// is carried in, its defindex and item class, the weapon row it became (its catalog kind; -1 for a worn item: a
/// shield, Gunboats) and its own attributes (the schema's statics, which the tooltip lists).
/// </summary>
public sealed record TFOwnKitItem(TFLoadoutSlot Slot, int Defindex, string ItemClass, string Weapon, int WeaponKind, IReadOnlyList<TFOwnKitAttribute> Attributes);

/// <summary>
/// What a player knows of their own kit (plan_bots_teams_tf2 P7): the seam a bot's binding reads its loadout
/// through, so the AI names no loadout class of the game's. <see cref="Version"/> changes when a new kit is handed
/// (a spawn, a resupply, a class change), so a reader can keep what it built from the last.
/// </summary>
public interface ITFOwnKit
{
    /// <summary>The player's kit, weapons in slot order then the worn items: empty before a first kit.</summary>
    IReadOnlyList<TFOwnKitItem> KitOf(int entIndex);

    /// <summary>A number that changes with every kit the player is handed.</summary>
    int Version(int entIndex);
}

/// <summary>The kit as the loadout last handed it (<see cref="TFLoadoutProvider.KitOf"/>, <see cref="TFLoadoutProvider.WornOf"/>).</summary>
[SideService(Side.Server, As = [typeof(ITFOwnKit)])]
public sealed class TFOwnKit(TFLoadoutProvider loadout, WeaponCatalog catalog) : ITFOwnKit
{
    public int Version(int entIndex) => loadout.KitVersion(entIndex);

    public IReadOnlyList<TFOwnKitItem> KitOf(int entIndex)
    {
        var items = new List<TFOwnKitItem>();
        foreach (var (weapon, entry) in loadout.KitOf(entIndex))
            items.Add(Of(entry, weapon, catalog.IndexOf(weapon)));
        items.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        foreach (LoadoutItem entry in loadout.WornOf(entIndex))
            items.Add(Of(entry, "", -1));
        return items;
    }

    /// <summary>An item as its holder knows it: its slot, numbers and its own attributes, a float's value or a stored integer's.</summary>
    public static TFOwnKitItem Of(in LoadoutItem entry, string weapon, int kind)
    {
        ItemDefinition definition = entry.Item.Definition;
        var attributes = new List<TFOwnKitAttribute>();
        foreach (AttributeValue a in definition.Attributes)
        {
            if (a.Definition.AttributeClass is { Length: > 0 } cls && a.Text is null)
                attributes.Add(new TFOwnKitAttribute(cls, a.Definition.StoredAsInteger ? a.Bits : a.AsFloat));
        }

        foreach (AttributeValue a in entry.Item.Attributes)
        {
            if (a.Definition.AttributeClass is { Length: > 0 } cls && a.Text is null)
                attributes.Add(new TFOwnKitAttribute(cls, a.Definition.StoredAsInteger ? a.Bits : a.AsFloat));
        }

        return new TFOwnKitItem(entry.Slot, definition.Defindex, definition.ItemClass ?? "", weapon, kind, attributes);
    }
}
