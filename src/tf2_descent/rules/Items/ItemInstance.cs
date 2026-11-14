using System.Collections.Immutable;
using SourceSharp.Host.Contracts;

namespace TF2.Descent.Rules.Items;

/// <summary>An affix on an item: its row and where in its range it rolled, 0..<see cref="ItemSettings.RollScale"/>.</summary>
public sealed record RolledAffix(string Id, int Roll);

/// <summary>The crafting operations an item's history records (D24).</summary>
public static class ItemOps
{
    /// <summary>One affix's roll was re-rolled; <see cref="ItemEvent.Index"/> names it.</summary>
    public const string Reroll = "reroll";

    /// <summary>The rarity was raised one step and affixes added to fill it.</summary>
    public const string Upgrade = "upgrade";
}

/// <summary>One crafted change to an item, with the seed that made it: the host replays it.</summary>
public sealed record ItemEvent(string Op, int Index, ulong Seed);

/// <summary>
/// An item (plan_tf2_rpg 2b). Two halves:
/// <list type="bullet">
/// <item>The rolled half (base, item level, rarity, tier, unique, affixes, unusual effect) is
/// what <see cref="ItemRoller.Roll"/> gives for <see cref="Context"/> and <see cref="Seed"/>,
/// followed by each crafted change in <see cref="History"/>, each with its own seed: the host
/// replays both (<c>IGameRules.Replays</c>).</item>
/// <item>The mutable half (identified, durability, maximum durability, wear toward the next
/// point, re-roll count, stack count) changes in play, inside bounds the replay checks.</item>
/// </list>
/// Positive affixes come first, then cursed ones, in the order they rolled.
/// </summary>
public sealed record ItemInstance
{
    public required string BaseId { get; init; }
    public required ItemRollContext Context { get; init; }
    public ulong Seed { get; init; }
    public int ItemLevel { get; init; } = 1;

    /// <summary>The rarity's index in rarities.json: 0 Stock .. 4 Australium as shipped.</summary>
    public int Rarity { get; init; }

    /// <summary>The tier the roll was asked for (the reserve's tier); the rarity may be higher (promoted to Unusual).</summary>
    public int Tier { get; init; }

    /// <summary>The Australium unique this is, by id; null for a rolled item.</summary>
    public string? Unique { get; init; }

    public ImmutableArray<RolledAffix> Affixes { get; init; } = [];
    public string? UnusualEffect { get; init; }

    public bool Identified { get; init; }

    /// <summary>Current durability; 0 is broken (2h). Gear only; 0 for anything that does not wear.</summary>
    public int Durability { get; init; }

    /// <summary>Maximum durability; only a field repair lowers it, and at 0 gear is unusable (L7 as amended).</summary>
    public int MaxDurability { get; init; }

    /// <summary>Uses (weapons) or hits (armour) counted toward the next point of wear.</summary>
    public int WearUses { get; init; }

    public int RerollCount { get; init; }

    /// <summary>How many in this stack (metal, dossiers); 1 for gear.</summary>
    public int Count { get; init; } = 1;

    public ImmutableArray<ItemEvent> History { get; init; } = [];
}
