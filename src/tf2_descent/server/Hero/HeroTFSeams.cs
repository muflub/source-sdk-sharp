using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;

namespace TF2.Descent.Server.Hero;

/// <summary>A TF class's own numbers, before any attribute: what a hero's stats are measured against.</summary>
public readonly record struct TFClassBase(int MaxHealth, float MaxSpeed);

/// <summary>
/// The TF class a player plays now, as its own maximum health and speed
/// (<c>health_max</c>, <c>speed_max</c>): the hero's TF attributes are the
/// difference between these and the sheet's numbers (HE3).
/// </summary>
public interface IHeroClassBase
{
    /// <summary>The player's class's numbers; null while the player has no class.</summary>
    TFClassBase? Of(EntityHandle player);
}

/// <summary>The class base over TF2's carriers and class table.</summary>
[SideService(Side.Server, As = [typeof(IHeroClassBase)])]
public sealed class TFHeroClassBase(TFPlayerCarriers carriers, TFClassTable classes) : IHeroClassBase
{
    public TFClassBase? Of(EntityHandle player)
    {
        TFClass cls = carriers.ClassOf(player);
        if (cls is <= TFClass.Undefined or >= TFClass.CountAll)
            return null;

        var data = classes[cls];
        return new TFClassBase(data.MaxHealth, data.MaxSpeed);
    }
}

/// <summary>A TF attribute definition by its hook class (<c>add_maxhealth</c>, <c>mult_player_movespeed</c>).</summary>
public interface ITFAttributeLookup
{
    /// <summary>
    /// The definition named <paramref name="preferredName"/> when the schema
    /// has it and it hooks <paramref name="attributeClass"/>, else the
    /// schema's first definition of that class; null when none hooks it.
    /// </summary>
    AttributeDefinition? Find(string attributeClass, string preferredName);
}

/// <summary>The lookup over TF2's item schema.</summary>
[SideService(Side.Server, As = [typeof(ITFAttributeLookup)])]
public sealed class SchemaAttributeLookup(ItemSchema schema) : ITFAttributeLookup
{
    public AttributeDefinition? Find(string attributeClass, string preferredName) =>
        schema.Attribute(preferredName) is { } named && named.AttributeClass == attributeClass
            ? named
            : schema.AttributesOfClass(attributeClass).FirstOrDefault();
}

/// <summary>The weapon a player holds, as the item class Descent's weapon families name.</summary>
public interface IHeroWeapons
{
    /// <summary>
    /// The item class of <paramref name="player"/>'s active weapon (its
    /// item's <c>item_class</c>, else the weapon's own class name); null with
    /// no weapon in the hands.
    /// </summary>
    string? ActiveItemClass(EntityHandle player);
}

/// <summary>The active weapon over TF2's weapon owners: the item's class, else the entity's.</summary>
[SideService(Side.Server, As = [typeof(IHeroWeapons)])]
public sealed class TFHeroWeapons(ITFWeaponOwners owners, IEnginePlayers players, IEngineEntities entities) : IHeroWeapons
{
    public string? ActiveItemClass(EntityHandle player)
    {
        if (owners.ActiveItemOf(entities.GetIndex(player)) is AttributeContainer { Item.ItemClass: { Length: > 0 } itemClass })
            return itemClass;

        string name = players.GetActiveWeapon(player);
        return name.Length > 0 ? name : null;
    }
}

/// <summary>
/// The level of whatever hurt a hero, which armour is measured against
/// (Appendix C.2: <c>armour / (armour + 50 + 10 x attackerLevel)</c>).
/// SINGLE-OWNER: the horde's (<c>Horde.RobotAttackerLevel</c>, HO8), which
/// answers a robot body's level from its roster row and 1 for anything else.
/// </summary>
public interface IAttackerLevel
{
    /// <summary>The level of <paramref name="attacker"/> (a robot body, a trap, the world).</summary>
    int LevelOf(EntityHandle attacker);
}
