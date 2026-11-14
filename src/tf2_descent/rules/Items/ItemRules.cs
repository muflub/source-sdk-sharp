using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;

namespace TF2.Descent.Rules.Items;

/// <summary>
/// The items' rules as one object (plan_descent_logic RU3): the roll, the codec, the views and
/// equipment over one content. <see cref="TF2DescentRules"/> forwards the item members of
/// <c>IGameRules</c> here; the server and the loot package call it directly.
/// </summary>
public sealed class ItemRules
{
    private readonly DescentContent _content;

    public ItemRules(DescentContent content, HeroRules heroes)
    {
        _content = content;
        Roller = new ItemRoller(content);
        Codec = new ItemCodec();
        Views = new ItemViews(content, Roller);
        Equipment = new Equipment(content, heroes, Roller);
        Durability = new Durability(content, Roller);
    }

    public ItemRoller Roller { get; }
    public ItemCodec Codec { get; }
    public ItemViews Views { get; }
    public Equipment Equipment { get; }

    /// <summary>Wear by use and by hit (RU8).</summary>
    public Durability Durability { get; }

    /// <summary>The rarity names in tier order (D5): <c>IGameRules.Tiers</c>.</summary>
    public IReadOnlyList<string> Tiers => [.. _content.Rarities.Rows.Select(r => r.Name)];

    /// <summary>Roll an item for a context: <c>IGameRules.RollItem</c>.</summary>
    public RolledItem Roll(ItemRollContext ctx, ulong seed) => Codec.ToRolled(Roller.Roll(ctx, seed));

    /// <summary>
    /// <c>IGameRules.Replays</c>: true when the item's columns agree with its own bytes and those
    /// bytes are what its context, seed and history give (<see cref="ItemRoller.Replays"/>). A
    /// schema 1 item replays by the formula that rolled it; bytes that do not decode never replay.
    /// </summary>
    public bool Replays(RolledItem item)
    {
        if (item.SchemaVersion == ItemCodec.Schema1)
        {
            try
            {
                var bytes = LegacyItems.Decode(item.Instance);
                return bytes with { Instance = item.Instance } == item with { Instance = item.Instance } && LegacyItems.Replays(item);
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }
        ItemInstance instance;
        try
        {
            instance = Codec.Instance(item);
        }
        catch (InvalidDataException)
        {
            return false;
        }
        return item.Seed == instance.Seed && item.BaseType == instance.BaseId && item.Rarity == instance.Rarity
               && item.ItemLevel == instance.ItemLevel && item.Count == instance.Count && item.Identified == instance.Identified
               && item.Tier == instance.Tier && item.Context == instance.Context
               && Roller.Replays(instance);
    }
}
