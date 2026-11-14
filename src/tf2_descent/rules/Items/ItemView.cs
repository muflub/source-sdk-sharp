using System.Text.Json;
using TF2.Descent.Rules.Content;

namespace TF2.Descent.Rules.Items;

/// <summary>Who an item's view is for: its owner, or another player (L11: equipped items are public to every client).</summary>
public enum ItemViewer
{
    Owner,
    Other,
}

/// <summary>One affix as a client sees it: its row, its stat, whether TF or cursed, and its value.</summary>
public sealed record AffixView(string Id, string Name, string Stat, bool TF, bool Cursed, double Value);

/// <summary>
/// The projection of an item that may leave the server (plan_tf2_rpg 2i, D22; ruling L11).
/// An unidentified item's view carries its base, rarity and item level and nothing else: no
/// affix, no cursed stat, no unusual effect, no durability, so a client cannot read what the
/// item hides out of memory or off the wire. An identified item's view carries everything,
/// for its owner and for every other player alike (L11: an unidentified item cannot be
/// equipped, so nothing hidden is public).
/// </summary>
public sealed record ItemView(
    string BaseId,
    int Rarity,
    int ItemLevel,
    bool Identified,
    string? Unique,
    IReadOnlyList<AffixView> Affixes,
    string? UnusualEffect,
    int Durability,
    int MaxDurability,
    int Count);

/// <summary>Builds <see cref="ItemView"/>s and their bytes.</summary>
public sealed class ItemViews
{
    private readonly DescentContent _content;
    private readonly ItemRoller _roller;

    public ItemViews(DescentContent content, ItemRoller roller)
    {
        _content = content;
        _roller = roller;
    }

    /// <summary>The view <paramref name="viewer"/> may have of <paramref name="item"/>.</summary>
    public ItemView View(ItemInstance item, ItemViewer viewer)
    {
        if (!item.Identified)
            return new ItemView(item.BaseId, item.Rarity, item.ItemLevel, false, null, [], null, 0, 0, 0);
        var affixes = item.Affixes.Select(ra =>
        {
            var a = _content.Affixes[ra.Id];
            return new AffixView(a.Id, a.Name, a.Stat, a.IsTF, a.IsCursed, _roller.Value(item, ra));
        }).ToList();
        return new ItemView(item.BaseId, item.Rarity, item.ItemLevel, true, item.Unique, affixes, item.UnusualEffect,
            item.Durability, item.MaxDurability, item.Count);
    }

    /// <summary>
    /// A view's bytes, the only form an item leaves the server in. Unidentified:
    /// <c>{"base","rarity","itemLevel"}</c> and nothing more. Identified: those, then the unique,
    /// the affixes (cursed ones flagged, so a client shows them in red), the unusual effect,
    /// durability, maximum and count.
    /// </summary>
    public static byte[] Encode(ItemView v)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("base", v.BaseId);
            w.WriteNumber("rarity", v.Rarity);
            w.WriteNumber("itemLevel", v.ItemLevel);
            if (v.Identified)
            {
                w.WriteBoolean("identified", true);
                if (v.Unique is { } unique) w.WriteString("unique", unique);
                w.WriteStartArray("affixes");
                foreach (var a in v.Affixes)
                {
                    w.WriteStartObject();
                    w.WriteString("id", a.Id);
                    w.WriteString("stat", a.Stat);
                    w.WriteNumber("value", a.Value);
                    if (a.TF) w.WriteBoolean("tf", true);
                    if (a.Cursed) w.WriteBoolean("cursed", true);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                if (v.UnusualEffect is { } effect) w.WriteString("effect", effect);
                w.WriteNumber("durability", v.Durability);
                w.WriteNumber("maxDurability", v.MaxDurability);
                w.WriteNumber("count", v.Count);
            }
            w.WriteEndObject();
        }
        return buffer.ToArray();
    }
}
