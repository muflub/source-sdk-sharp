using System.Collections.Immutable;
using SourceSharp.Host.Contracts;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Economy;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using Xunit;
using static TF2.Descent.Tests.Items.ItemFixtures;

namespace TF2.Descent.Tests.Inventory;

/// <summary>The two-phase trade (R8, plan_tf2_rpg 2d): offer, both lock, both confirm, an atomic swap.</summary>
public sealed class TradeTests
{
    private static readonly TF2DescentRules R = NewRules();
    private static Trades T => R.Trades;
    private static InventoryRules Inv => R.Inventory;
    private static readonly StashState NoStash = new();

    private static HeroSheet Sheet() => Soldier(R);

    private static HeroInventory With(int count, long first, long australium = 0)
    {
        var hero = new HeroInventory { Australium = australium };
        for (int i = 0; i < count; i++)
            hero = Inv.Pickup(hero, NoStash, Sheet(),
                new Held(first + i, R.ItemRules.Roller.Roll(Ctx(0, 10, table: "base:shovel", source: "craft"), (ulong)(first + i)))).Hero;
        return hero;
    }

    private static Trade Agreed(TradeOffer a, TradeOffer b)
    {
        var t = T.Offer(T.Offer(Trade.Open, TradeSide.A, a), TradeSide.B, b);
        return T.Lock(T.Lock(t, TradeSide.A), TradeSide.B);
    }

    [Fact]
    public void Both_confirm_after_both_lock_and_the_offers_swap_at_once()
    {
        var a = With(2, 100, australium: 50);
        var b = With(1, 200);
        var t = Agreed(new TradeOffer([0, 1], 30), new TradeOffer([0], 0));

        var first = T.Confirm(t, TradeSide.A, a, Sheet(), b, Sheet());
        Assert.True(first.Ok);
        Assert.False(first.Trade.Done);
        Assert.Same(a, first.A);                                      // nothing moves on one confirmation
        var done = T.Confirm(first.Trade, TradeSide.B, a, Sheet(), b, Sheet());

        Assert.True(done.Trade.Done);
        Assert.Equal([200L], done.A.Items.Select(h => h.Id));
        Assert.Equal([100L, 101L], done.B.Items.Select(h => h.Id).Order());
        Assert.Equal((20L, 30L), (done.A.Australium, done.B.Australium));
    }

    [Fact]
    public void Confirming_before_both_lock_is_refused()
    {
        var t = T.Lock(T.Offer(Trade.Open, TradeSide.A, new TradeOffer([0], 0)), TradeSide.A);

        Assert.Equal(TradeRefusals.NotLocked, T.Confirm(t, TradeSide.A, With(1, 1), Sheet(), With(0, 9), Sheet()).Refusal);
    }

    [Fact]
    public void Changing_an_offer_unlocks_both_sides_and_clears_both_confirmations()
    {
        var t = Agreed(new TradeOffer([0], 0), TradeOffer.Nothing);
        t = T.Confirm(t, TradeSide.A, With(1, 1), Sheet(), With(0, 9), Sheet()).Trade;
        Assert.True(t.LockedA && t.LockedB && t.ConfirmedA);

        foreach (var side in new[] { TradeSide.A, TradeSide.B })
        {
            var changed = T.Offer(t, side, new TradeOffer([], 5));

            Assert.Equal(5, changed.Of(side).Australium);
            Assert.Equal((false, false, false, false), (changed.LockedA, changed.LockedB, changed.ConfirmedA, changed.ConfirmedB));
        }
    }

    [Fact]
    public void A_side_without_room_refuses_the_swap_and_nothing_moves()
    {
        var a = With(3, 100);
        var b = With(11, 200);                                        // 12 slots, 11 used: room for 1, gives 1
        var t = Agreed(new TradeOffer([0, 1, 2], 0), new TradeOffer([0], 0));
        t = T.Confirm(t, TradeSide.A, a, Sheet(), b, Sheet()).Trade;

        var r = T.Confirm(t, TradeSide.B, a, Sheet(), b, Sheet());

        Assert.Equal(TradeRefusals.NoCapacityB, r.Refusal);
        Assert.Same(a, r.A);
        Assert.Same(b, r.B);
        Assert.False(r.Trade.Done);
    }

    [Fact]
    public void A_side_that_offers_more_australium_than_it_holds_is_refused()
    {
        var t = Agreed(new TradeOffer([], 60), TradeOffer.Nothing);
        t = T.Confirm(t, TradeSide.A, With(0, 1, australium: 50), Sheet(), With(0, 9), Sheet()).Trade;

        Assert.Equal(TradeRefusals.NotEnoughAustralium, T.Confirm(t, TradeSide.B, With(0, 1, australium: 50), Sheet(), With(0, 9), Sheet()).Refusal);
    }

    [Fact]
    public void The_hosts_last_check_refuses_no_room_negative_australium_and_an_item_offered_twice()
    {
        var item = R.RollItem(Ctx(0), 1);
        var other = R.RollItem(Ctx(0), 2);
        var sheet = R.NewCharacter("soldier", "a", false);
        TradeState State(IReadOnlyList<RolledItem> offerA, IReadOnlyList<RolledItem> offerB, long auA = 0, int freeA = 5, int freeB = 5) =>
            new(sheet, sheet, offerA, offerB, auA, 0, freeA, freeB);

        Assert.True(R.ApplyTrade(State([item], [other])).Ok);
        Assert.Equal(TradeRefusals.NoCapacityA, R.ApplyTrade(State([], [item, other], freeA: 1)).Reason);
        Assert.Equal(TradeRefusals.BadAustralium, R.ApplyTrade(State([item], [], auA: -1)).Reason);
        Assert.Equal(TradeRefusals.Duplicate, R.ApplyTrade(State([item, item], [])).Reason);
    }
}
