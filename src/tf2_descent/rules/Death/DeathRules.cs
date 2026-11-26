using System.Collections.Immutable;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;

namespace TF2.Descent.Rules.Death;

/// <summary>What a hero carries when it dies: equipped items, the backpack, the belt (item ids) and its Australium.</summary>
public sealed record CarriedGear(ImmutableArray<string> Equipped, ImmutableArray<string> Backpack, ImmutableArray<string> Belt, long Australium)
{
    public static CarriedGear Empty { get; } = new([], [], [], 0);

    /// <summary>Every item id carried, equipped first, then the backpack, then the belt.</summary>
    public IEnumerable<string> Items => Equipped.Concat(Backpack).Concat(Belt);
}

/// <summary>
/// A dead hero's corpse (R5, D6): what it holds and whose it is. A hardcore hero's corpse
/// (D7) is lootable by nobody and never reaches the Lost &amp; Found: its gear is lost. A corpse
/// holds items only: the Australium stays with the hero (the user, 2026-11-22, plan L26).
/// </summary>
public sealed record Corpse(string Owner, bool Hardcore, ImmutableArray<string> Items);

/// <summary>A death: the corpse, and what the hero keeps (his Australium, as shipped).</summary>
public sealed record DeathResult(Corpse Corpse, CarriedGear Kept);

/// <summary>What the Lost &amp; Found holds for an owner once the corpse's level server is gone (R5): items only.</summary>
public sealed record LostAndFoundEntry(string Owner, ImmutableArray<string> Items);

/// <summary>
/// The death rules that are not the downed state (plan_tf2_rpg 2g; D6, D7, R5): what falls
/// to the corpse, who may loot it, when the Lost &amp; Found takes it and what it charges, and
/// what hardcore death does to the sheet. Pure over death.json. The vendor value the fee
/// is a share of is an input: pricing is the items package's.
/// </summary>
public sealed class DeathRules
{
    private readonly DescentContent _content;

    public DeathRules(DescentContent content) => _content = content;

    private DeathSettings S => _content.Death;

    /// <summary>
    /// The corpse a death leaves and what the hero keeps. Each category the table drops
    /// (equipped, backpack, belt) moves to the corpse whole; one it does not stays with the
    /// hero. The Australium always stays with the hero: it never goes to the corpse or the
    /// Lost &amp; Found (the user, 2026-11-22, plan L26).
    /// </summary>
    public DeathResult Die(string owner, bool hardcore, CarriedGear gear)
    {
        var d = S.Drops;
        var dropped = ImmutableArray.CreateBuilder<string>();
        if (d.Equipped) dropped.AddRange(gear.Equipped);
        if (d.Backpack) dropped.AddRange(gear.Backpack);
        if (d.Belt) dropped.AddRange(gear.Belt);
        var kept = new CarriedGear(
            d.Equipped ? [] : gear.Equipped,
            d.Backpack ? [] : gear.Backpack,
            d.Belt ? [] : gear.Belt,
            gear.Australium);
        return new DeathResult(new Corpse(owner, hardcore, dropped.ToImmutable()), kept);
    }

    /// <summary>Only the owner loots a corpse (D6); nobody loots a hardcore one (D7).</summary>
    public static bool CanLoot(Corpse corpse, string looter) =>
        !corpse.Hardcore && string.Equals(corpse.Owner, looter, StringComparison.Ordinal);

    /// <summary>Whether the corpse has outlived its emptied instance and goes to the Lost &amp; Found (D6: 15 minutes).</summary>
    public bool Expired(double secondsSinceInstanceEmptied) => secondsSinceInstanceEmptied >= S.CorpseLingerSeconds;

    /// <summary>
    /// What the Lost &amp; Found takes from a corpse whose server is gone (R5), or null for a
    /// hardcore corpse (D7: its gear is lost) or an empty one.
    /// </summary>
    public static LostAndFoundEntry? ToLostAndFound(Corpse corpse) =>
        corpse.Hardcore || corpse.Items.IsEmpty
            ? null
            : new LostAndFoundEntry(corpse.Owner, corpse.Items);

    /// <summary>
    /// The fee to reclaim from the Lost &amp; Found (D6): the table's share (25 %) of the
    /// contents' vendor value, rounded to the nearest whole Australium, halves up.
    /// </summary>
    public long LostAndFoundFee(long contentsVendorValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(contentsVendorValue);
        return (long)Math.Round(contentsVendorValue * S.LostAndFoundFeePct / 100.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The sheet after a death (D7): a hardcore hero is fallen and never rises; a softcore
    /// one is unchanged (it runs back to its corpse, R5).
    /// </summary>
    public static HeroSheet AfterDeath(HeroSheet sheet) => sheet.Hardcore ? sheet with { Fallen = true } : sheet;

    /// <summary>A fallen hero cannot be played (D7).</summary>
    public static bool Playable(HeroSheet sheet) => !sheet.Fallen;
}
