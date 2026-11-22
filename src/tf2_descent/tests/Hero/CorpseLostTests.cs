using SourceSharp.Abi;

using TF2.Descent.Server.Items;

using Xunit;

using static TF2.Descent.Tests.Hero.DownedRig;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// A corpse whose level is gone (plan_tf2_rpg R5, D6, 7b, Phase 10): unhosted, one process holds
/// one map, so a level change ends the level the corpse lay on for good; its items go to the Lost
/// &amp; Found in town, which gives them back for D6's fee. No item is left on a corpse nobody can
/// stand beside.
/// </summary>
public sealed class CorpseLostTests
{
    private static readonly Vector There = new(100, 200, 0);

    /// <summary>Every item id the hero carries, sorted.</summary>
    private static long[] Carried(DownedRig rig, int index) =>
        [.. rig.Ledger.Find(rig.Key(index))!.Inventory.Items.Select(h => h.Id).Order()];

    /// <summary>A hero who carried his kit died at <see cref="There"/>, stood again, and the level changed (to the town).</summary>
    private static (DownedRig Rig, long[] Before) DiedAndLeft()
    {
        var rig = new DownedRig();
        long[] before = Carried(rig, A);
        Assert.NotEmpty(before);
        rig.Clock.Origins[PlayerOf(A)] = There;
        rig.Hit(A, 500);
        rig.RunTo(1200);
        rig.Clock.Respawn(PlayerOf(A));
        Assert.Empty(Carried(rig, A));
        Assert.NotNull(rig.Graves.Of(rig.Key(A)));

        rig.Graves.OnLevelInit("tf2_descent_town");
        return (rig, before);
    }

    private static void Give(DownedRig rig, int index, long australium)
    {
        var held = rig.Ledger.Find(rig.Key(index))!;
        rig.Ledger.Replace(held with { Inventory = held.Inventory with { Australium = australium } });
    }

    [Fact]
    public async Task Every_item_a_hero_died_with_comes_back_from_the_lost_and_found_after_the_level_changed()
    {
        var (rig, before) = DiedAndLeft();
        Give(rig, A, 1_000_000);

        var answer = await rig.Ledger.Reclaim(rig.Key(A));

        Assert.True(answer.Ok, answer.Refusal);
        Assert.Equal(before, Carried(rig, A));
        Assert.Null(rig.Ledger.LostAndFound(rig.Key(A)));
    }

    [Fact]
    public void A_level_change_leaves_no_item_on_a_corpse_without_a_gravestone()
    {
        var (rig, before) = DiedAndLeft();

        Assert.Empty(rig.Graves.All);
        Assert.Null(rig.Ledger.Corpse(rig.Key(A)));
        Assert.Equal(before, rig.Ledger.LostAndFound(rig.Key(A))!.Items.Order());
    }

    [Fact]
    public async Task A_reclaim_pays_the_rules_fee_and_gives_back_the_australium_the_corpse_held()
    {
        var rig = new DownedRig();
        Give(rig, A, 40);
        rig.Hit(A, 500);
        rig.RunTo(1200);
        rig.Clock.Respawn(PlayerOf(A));
        rig.Graves.OnLevelInit("tf2_descent_town");
        long fee = rig.Ledger.ReclaimFee(rig.Key(A))!.Value;
        Assert.True(fee > 0, "the control: the kit has no vendor value, so the fee is nothing");
        Assert.Equal(40, rig.Ledger.LostAndFound(rig.Key(A))!.Australium);
        Give(rig, A, fee);

        Assert.True((await rig.Ledger.Reclaim(rig.Key(A))).Ok);

        Assert.Equal(40, rig.Ledger.Find(rig.Key(A))!.Inventory.Australium);
    }

    [Fact]
    public async Task A_hero_who_cannot_pay_the_fee_reclaims_nothing_and_the_lost_and_found_keeps_it()
    {
        var (rig, before) = DiedAndLeft();
        long fee = rig.Ledger.ReclaimFee(rig.Key(A))!.Value;
        Give(rig, A, fee - 1);

        var answer = await rig.Ledger.Reclaim(rig.Key(A));

        Assert.False(answer.Ok);
        Assert.Equal(LedgerRefusals.LostAndFoundFee, answer.Refusal);
        Assert.Empty(Carried(rig, A));
        Assert.Equal(fee - 1, rig.Ledger.Find(rig.Key(A))!.Inventory.Australium);
        Assert.Equal(before, rig.Ledger.LostAndFound(rig.Key(A))!.Items.Order());
    }

    [Fact]
    public void A_hardcore_heros_lost_corpse_never_reaches_the_lost_and_found()
    {
        var rig = new DownedRig();
        var held = rig.Ledger.Find(rig.Key(A))!;
        rig.Ledger.Replace(held with { Sheet = held.Sheet with { Hardcore = true } });
        rig.Hit(A, 500);
        rig.RunTo(1200);

        rig.Graves.OnLevelInit("tf2_descent_town");

        Assert.Null(rig.Ledger.Corpse(rig.Key(A)));
        Assert.Null(rig.Ledger.LostAndFound(rig.Key(A)));
    }

    [Fact]
    public void A_hero_whose_corpse_went_to_the_lost_and_found_is_told_when_he_stands_on_the_next_level()
    {
        var (rig, _) = DiedAndLeft();
        int before = Notices(rig).Count;

        rig.Graves.Think();
        rig.Graves.Think();

        Assert.Single(Notices(rig).Skip(before), n => n.Contains("Lost and Found", StringComparison.Ordinal));
    }

    private static List<string> Notices(DownedRig rig) =>
        [.. rig.Messages.Calls.Where(c => c.Member == "UserMessages.Send" && (string)c.Arg(0)! == "TF2.Descent:notice")
            .Select(c => System.Text.Encoding.UTF8.GetString((byte[])c.Arg(4)!))];
}
