using System.Collections.Immutable;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Items;

namespace TF2.Descent.Rules.Inventory;

/// <summary>A side of a trade.</summary>
public enum TradeSide
{
    A,
    B,
}

/// <summary>What one side offers: backpack slots and Australium.</summary>
public sealed record TradeOffer(ImmutableArray<int> Slots, long Australium)
{
    public static TradeOffer Nothing { get; } = new([], 0);
}

/// <summary>
/// A two-phase trade (R8, plan_tf2_rpg 2d): both sides offer, both lock, both confirm, and
/// only then do the offers swap, all at once. Changing an offer unlocks both sides and clears
/// both confirmations, so nobody confirms an offer they have not seen locked.
/// </summary>
public sealed record Trade(TradeOffer A, TradeOffer B, bool LockedA, bool LockedB, bool ConfirmedA, bool ConfirmedB, bool Done)
{
    public static Trade Open { get; } = new(TradeOffer.Nothing, TradeOffer.Nothing, false, false, false, false, false);

    public TradeOffer Of(TradeSide side) => side == TradeSide.A ? A : B;
}

/// <summary>Why a trade step was refused.</summary>
public static class TradeRefusals
{
    public const string Done = "trade_done";
    public const string NotLocked = "not_locked";
    public const string NoItem = "no_item";
    public const string Equipped = "equipped";
    public const string NotEnoughAustralium = "not_enough_australium";
    public const string NoCapacityA = "no_capacity_a";
    public const string NoCapacityB = "no_capacity_b";
    public const string BadAustralium = "bad_australium";
    public const string Duplicate = "duplicate_item";
}

/// <summary>A trade step: the trade and both inventories after it, or why it was refused (nothing changed).</summary>
public sealed record TradeStepResult(Trade Trade, HeroInventory A, HeroInventory B, string? Refusal)
{
    public bool Ok => Refusal is null;
}

/// <summary>The trade's steps (see <see cref="Trade"/>), pure over the trade and the two inventories.</summary>
public sealed class Trades
{
    private readonly InventoryRules _inventory;

    public Trades(InventoryRules inventory) => _inventory = inventory;

    /// <summary>Sets one side's offer: both sides unlock and both confirmations clear.</summary>
    public Trade Offer(Trade trade, TradeSide side, TradeOffer offer)
    {
        if (trade.Done) return trade;
        return side == TradeSide.A
            ? trade with { A = offer, LockedA = false, LockedB = false, ConfirmedA = false, ConfirmedB = false }
            : trade with { B = offer, LockedA = false, LockedB = false, ConfirmedA = false, ConfirmedB = false };
    }

    /// <summary>Locks one side's view of the two offers.</summary>
    public Trade Lock(Trade trade, TradeSide side) =>
        trade.Done ? trade : side == TradeSide.A ? trade with { LockedA = true } : trade with { LockedB = true };

    /// <summary>
    /// Confirms for one side; refused until both have locked. When both have confirmed, the
    /// offers swap atomically: each side's offered items leave its backpack and fill the other's
    /// free slots below capacity, and the Australium moves; refused, changing nothing, when an
    /// offered slot is empty, a side lacks the Australium it offered, or a side has no room.
    /// </summary>
    public TradeStepResult Confirm(Trade trade, TradeSide side, HeroInventory a, HeroSheet sheetA, HeroInventory b, HeroSheet sheetB)
    {
        if (trade.Done) return new(trade, a, b, TradeRefusals.Done);
        if (!trade.LockedA || !trade.LockedB) return new(trade, a, b, TradeRefusals.NotLocked);
        var confirmed = side == TradeSide.A ? trade with { ConfirmedA = true } : trade with { ConfirmedB = true };
        if (!confirmed.ConfirmedA || !confirmed.ConfirmedB) return new(confirmed, a, b, null);

        if (Check(trade.A, a) is { } whyA) return new(confirmed with { ConfirmedA = false, ConfirmedB = false }, a, b, whyA);
        if (Check(trade.B, b) is { } whyB) return new(confirmed with { ConfirmedA = false, ConfirmedB = false }, a, b, whyB);
        var outA = trade.A.Slots.Select(s => a.Backpack[s]).ToList();
        var outB = trade.B.Slots.Select(s => b.Backpack[s]).ToList();
        var leftA = a with { Backpack = a.Backpack.RemoveRange(trade.A.Slots), Australium = a.Australium - trade.A.Australium + trade.B.Australium };
        var leftB = b with { Backpack = b.Backpack.RemoveRange(trade.B.Slots), Australium = b.Australium - trade.B.Australium + trade.A.Australium };
        if (_inventory.FreeSlots(leftA, sheetA) < outB.Count) return new(confirmed with { ConfirmedA = false, ConfirmedB = false }, a, b, TradeRefusals.NoCapacityA);
        if (_inventory.FreeSlots(leftB, sheetB) < outA.Count) return new(confirmed with { ConfirmedA = false, ConfirmedB = false }, a, b, TradeRefusals.NoCapacityB);
        var newA = Fill(leftA, sheetA, outB);
        var newB = Fill(leftB, sheetB, outA);
        return new(confirmed with { Done = true }, newA, newB, null);
    }

    private static string? Check(TradeOffer offer, HeroInventory side)
    {
        if (offer.Australium < 0) return TradeRefusals.BadAustralium;
        if (offer.Slots.Distinct().Count() != offer.Slots.Length) return TradeRefusals.Duplicate;
        if (offer.Slots.Any(s => !side.Backpack.ContainsKey(s))) return TradeRefusals.NoItem;
        if (side.Australium < offer.Australium) return TradeRefusals.NotEnoughAustralium;
        return null;
    }

    private HeroInventory Fill(HeroInventory hero, HeroSheet sheet, IEnumerable<Held> items)
    {
        foreach (var item in items)
            hero = hero with { Backpack = hero.Backpack.Add(_inventory.FreeSlot(hero, sheet), item) };
        return hero;
    }

    /// <summary>
    /// <c>IGameRules.ApplyTrade</c>: the host's last check before it swaps a confirmed trade.
    /// Refused when Australium offered is negative, an item is offered twice, or a side lacks
    /// the free slots for what it receives net of what it gives.
    /// </summary>
    public static TradeResult Apply(TradeState t)
    {
        if (t.AustraliumA < 0 || t.AustraliumB < 0) return new(false, TradeRefusals.BadAustralium);
        if (Twice(t.OfferA) || Twice(t.OfferB) || t.OfferA.Any(x => t.OfferB.Any(y => y.Instance.AsSpan().SequenceEqual(x.Instance))))
            return new(false, TradeRefusals.Duplicate);
        if (t.OfferB.Count - t.OfferA.Count > t.FreeSlotsA) return new(false, TradeRefusals.NoCapacityA);
        if (t.OfferA.Count - t.OfferB.Count > t.FreeSlotsB) return new(false, TradeRefusals.NoCapacityB);
        return new(true);
    }

    private static bool Twice(IReadOnlyList<RolledItem> offer) =>
        offer.Select(i => Convert.ToHexString(i.Instance)).Distinct().Count() != offer.Count;
}
