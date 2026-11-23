using System.Collections.Immutable;
using TF2.Server.Econ;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>One slot of an archetype's loadout: the defindexes it prefers there, first first (plan_bots_teams_tf2 C.3.1).</summary>
public sealed record TFArchetypeSlot(TFLoadoutSlot Slot, ImmutableArray<int> Prefer);

/// <summary>
/// A way of playing a class (plan_bots_teams_tf2 C.3.1, P7 step 3): a name the bot's play reads, a weight for the
/// seeded draw, and the loadout that follows from it, a preference list per slot. A slot it does not name is the
/// seeded draw's, as before. A preferred item stands for its reskins: any works row of the same item class with no
/// attributes of its own (<see cref="TFArchetypeChoice"/>).
/// </summary>
public sealed record TFArchetype(string Name, float Weight, ImmutableArray<TFArchetypeSlot> Slots)
{
    /// <summary>The defindexes the archetype prefers in <paramref name="slot"/>; empty for a slot it leaves to the draw.</summary>
    public ImmutableArray<int> Prefer(TFLoadoutSlot slot)
    {
        foreach (TFArchetypeSlot s in Slots.IsDefault ? [] : Slots)
        {
            if (s.Slot == slot)
                return s.Prefer.IsDefault ? [] : s.Prefer;
        }

        return [];
    }
}

/// <summary>
/// A class's archetypes, its own file's (plan_bots_teams_tf2 C.3.1; the coordinator's shape ruling of 2026-11-20: C# in
/// each class's file, not a shared data file). A bot draws one per class by its seed, weighted, the same seed the same
/// archetype; a pin names one.
/// </summary>
public sealed record TFArchetypeTable(TFClass Class, ImmutableArray<TFArchetype> Rows)
{
    /// <summary>Mixed into the bot's seed for its archetype's draw, a stream apart from its decider's.</summary>
    public const ulong DrawSalt = 0x4152_4348UL;

    /// <summary>A class with none: every slot the seeded draw's, the class's play unchanged.</summary>
    public static TFArchetypeTable None(TFClass playerClass) => new(playerClass, []);

    /// <summary>The archetype the bot with <paramref name="seed"/> draws for the class, by weight; null for a table with none.</summary>
    public TFArchetype? Draw(ulong seed)
    {
        ImmutableArray<TFArchetype> rows = Rows.IsDefault ? [] : Rows;
        float total = 0f;
        foreach (TFArchetype a in rows)
            total += Math.Max(0f, a.Weight);
        if (rows.Length == 0 || total <= 0f)
            return null;
        float at = new AiRandom(AiRandom.SeedFor(seed ^ DrawSalt, (int)Class)).NextFloat() * total;
        foreach (TFArchetype a in rows)
        {
            at -= Math.Max(0f, a.Weight);
            if (at < 0f && a.Weight > 0f)
                return a;
        }

        return rows[^1];
    }

    /// <summary>The archetype called <paramref name="name"/> (any case), or null.</summary>
    public TFArchetype? Named(string name)
    {
        foreach (TFArchetype a in Rows.IsDefault ? [] : Rows)
        {
            if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                return a;
        }

        return null;
    }

    /// <summary>The archetype the bot plays: <paramref name="pinned"/> when the table has it, else its seed's draw.</summary>
    public TFArchetype? For(ulong seed, string? pinned) => pinned is { Length: > 0 } && Named(pinned) is { } named ? named : Draw(seed);
}

/// <summary>
/// The loadout that follows from an archetype (plan_bots_teams_tf2 C.3.1, P7 step 3): for a slot it names, its first
/// preferred defindex that is among the works rows offered, or one of that item's reskins (a works row of the same item
/// class with no attributes, when the preferred one has none: they play the same), drawn by the seed; -1 when it names
/// no offered item there, and the seeded draw stands.
/// </summary>
public static class TFArchetypeChoice
{
    public static int Choose(TFArchetype? archetype, ulong seed, TFLoadoutSlot slot, IReadOnlyList<BotWorksItem> works)
    {
        if (archetype is null)
            return -1;
        foreach (int want in archetype.Prefer(slot))
        {
            BotWorksItem? row = null;
            foreach (BotWorksItem w in works)
            {
                if (w.Defindex == want)
                    row = w;
            }

            if (row is not { } found)
                continue;
            if (found.Attributes.Length != 0)
                return found.Defindex;
            var same = new List<int>();
            foreach (BotWorksItem w in works)
            {
                if (w.ItemClass == found.ItemClass && w.Attributes.Length == 0 && !w.Flagged)
                    same.Add(w.Defindex);
            }

            return same.Count == 0 ? found.Defindex : same[new AiRandom(AiRandom.SeedFor(seed ^ TFArchetypeTable.DrawSalt, 100 + (int)slot)).NextInt(same.Count)];
        }

        return -1;
    }
}
