using SourceSharp.Abi;

using TF2.Descent.Rules.Death;
using TF2.Descent.Server.Items;

using Xunit;

using static TF2.Descent.Tests.Hero.DownedRig;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// What a death costs and the walk back (plan_descent_logic WO6, WO5; plan_tf2_rpg 2g, D6, D7,
/// D21, Phase 10): the rules' drop goes to the corpse through the in-memory ledger, the sheet's
/// audit takes the change, the corpse stands where he fell, and his own use within 96 units takes
/// back all that fits.
/// </summary>
public sealed class HeroDeathTests
{
    private static readonly Vector There = new(100, 200, 0);

    /// <summary>Every item id the hero carries, sorted.</summary>
    private static long[] Carried(DownedRig rig, int index) =>
        [.. rig.Ledger.Find(rig.Key(index))!.Inventory.Items.Select(h => h.Id).Order()];

    private static DownedRig DeadAt(Vector at, params int[] heroes)
    {
        var rig = new DownedRig(heroes);
        rig.Clock.Origins[PlayerOf(A)] = at;
        rig.Hit(A, 500);
        rig.RunTo(1200);
        return rig;
    }

    [Fact]
    public void A_death_puts_everything_carried_on_the_corpse_as_the_rules_drop_it()
    {
        var rig = new DownedRig(A);
        var held = rig.Ledger.Find(rig.Key(A))!;
        var gear = new CarriedGear([.. held.Inventory.Equipped.Values.Select(h => h.Id.ToString())],
                                   [.. held.Inventory.Backpack.Values.Select(h => h.Id.ToString())],
                                   [.. held.Inventory.Belt.Values.Select(h => h.Id.ToString())], held.Inventory.Australium);
        var expected = rig.Heroes.Rules.Deaths.Die(rig.Key(A), false, gear).Corpse;

        rig.Hit(A, 500);

        var corpse = rig.Ledger.Corpse(rig.Key(A))!;
        Assert.NotEmpty(expected.Items);
        Assert.Equal(expected.Items.Order(), corpse.Items.Select(i => i.ToString()).Order());
        Assert.Equal(expected.Australium, corpse.Australium);
        Assert.Empty(rig.Ledger.Find(rig.Key(A))!.Inventory.Items);
    }

    [Fact]
    public void A_death_costs_no_xp_and_the_sheets_audit_takes_it()
    {
        var rig = new DownedRig(A);
        long xp = rig.Hero(A).Sheet!.Xp;

        rig.Hit(A, 500);

        Assert.Equal(xp, rig.Hero(A).Sheet!.Xp);
        Assert.Contains("sheet audit ok", rig.Costs.LastCosts);
    }

    [Fact]
    public void A_death_moves_the_carried_australium_to_the_corpse()
    {
        var rig = new DownedRig(A);
        var held = rig.Ledger.Find(rig.Key(A))!;
        rig.Ledger.Replace(held with { Inventory = held.Inventory with { Australium = 75 } });

        rig.Hit(A, 500);

        Assert.Equal((75L, 0L), (rig.Ledger.Corpse(rig.Key(A))!.Australium, rig.Ledger.Find(rig.Key(A))!.Inventory.Australium));
    }

    [Fact]
    public void A_hardcore_death_leaves_the_hero_fallen_and_held_dead()
    {
        var rig = new DownedRig(A);
        Assert.True(rig.Heroes.Sheets.Apply(A, s => s with { Hardcore = true }).Ok);

        rig.Hit(A, 500);

        Assert.True(rig.Hero(A).Sheet!.Fallen);
        Assert.False(rig.Costs.MayRespawn(PlayerOf(A), A));
    }

    [Fact]
    public void A_softcore_hero_may_respawn()
    {
        var rig = new DownedRig(A);

        rig.Hit(A, 500);

        Assert.True(rig.Costs.MayRespawn(PlayerOf(A), A));
    }

    [Fact]
    public void The_corpse_stands_where_the_hero_fell_holding_his_items()
    {
        var rig = DeadAt(There);

        var grave = Assert.Single(rig.Corpses.Standing);
        Assert.Equal(There, grave.Value.At);
        Assert.Equal(A, grave.Value.State.OwnerIndex);
        Assert.Equal(rig.Ledger.Corpse(rig.Key(A))!.Items.Count, grave.Value.State.Items);
    }

    [Fact]
    public async Task His_use_within_96_units_takes_his_items_back_and_the_empty_corpse_goes()
    {
        var rig = new DownedRig();
        long[] before = Carried(rig, A);
        rig.Clock.Origins[PlayerOf(A)] = There;
        rig.Hit(A, 500);
        rig.RunTo(1200);
        rig.Clock.Respawn(PlayerOf(A));
        rig.Clock.Origins[PlayerOf(A)] = There + new Vector(90, 0, 0);

        string done = await rig.Graves.Use(A);

        Assert.StartsWith("recovered", done);
        Assert.Equal(before, Carried(rig, A));
        Assert.Empty(rig.Corpses.Standing);
        Assert.Null(rig.Graves.Of(rig.Key(A)));
    }

    [Fact]
    public void Item_ids_are_conserved_between_the_hero_and_his_corpse()
    {
        var rig = new DownedRig();
        long[] before = Carried(rig, A);

        rig.Hit(A, 500);
        rig.RunTo(1200);

        long[] after = [.. Carried(rig, A).Concat(rig.Ledger.Corpse(rig.Key(A))!.Items).Order()];
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task A_use_from_beyond_96_units_takes_nothing()
    {
        var rig = DeadAt(There);
        rig.Clock.Respawn(PlayerOf(A));
        rig.Clock.Origins[PlayerOf(A)] = There + new Vector(100, 0, 0);

        Assert.Equal("no corpse in reach", await rig.Graves.Use(A));
        Assert.Single(rig.Corpses.Standing);
    }

    [Fact]
    public async Task Another_hero_cannot_loot_the_corpse()
    {
        var rig = DeadAt(There);
        rig.Clock.Origins[PlayerOf(B)] = There;
        int had = Carried(rig, B).Length;

        Assert.Equal("refused: player 3's body", await rig.Graves.Use(B));
        Assert.Equal(had, Carried(rig, B).Length);
        Assert.NotEmpty(rig.Ledger.Corpse(rig.Key(A))!.Items);
    }

    [Fact]
    public async Task What_does_not_fit_stays_on_the_corpse()
    {
        var rig = DeadAt(There);
        rig.Clock.Respawn(PlayerOf(A));
        rig.Clock.Origins[PlayerOf(A)] = There;
        int corpse = rig.Ledger.Corpse(rig.Key(A))!.Items.Count;
        // The backpack refilled while he walked back: room for one.
        int room = rig.Heroes.Rules.Inventory.BackpackSlots(rig.Hero(A).Sheet!);
        for (int i = 0; i < room - 1; i++)
            Assert.True((await rig.Ledger.Give(rig.Key(A), "shovel", 0, 1)).Ok);

        string done = await rig.Graves.Use(A);

        Assert.Equal($"recovered 1, {corpse - 1} stay", done);
        Assert.Equal(corpse - 1, rig.Ledger.Corpse(rig.Key(A))!.Items.Count);
        Assert.Single(rig.Corpses.Standing);
    }

    [Fact]
    public async Task A_second_death_merges_onto_one_corpse_where_he_fell_last()
    {
        var rig = DeadAt(There);
        int first = rig.Ledger.Corpse(rig.Key(A))!.Items.Count;
        rig.Clock.Respawn(PlayerOf(A));
        Assert.True((await rig.Ledger.Give(rig.Key(A), "shovel", 0, 1)).Ok);
        var later = new Vector(-300, 0, 0);
        rig.Clock.Origins[PlayerOf(A)] = later;
        rig.RunTo(1300);

        rig.Hit(A, 500);
        rig.RunTo(1500);

        var grave = Assert.Single(rig.Corpses.Standing);
        Assert.Equal(later, grave.Value.At);
        Assert.Equal(first + 1, rig.Ledger.Corpse(rig.Key(A))!.Items.Count);
        Assert.Equal(1, rig.Corpses.Made);
    }

    [Fact]
    public void A_hero_who_dies_carrying_nothing_leaves_no_corpse()
    {
        var rig = new DownedRig(A);
        var held = rig.Ledger.Find(rig.Key(A))!;
        rig.Ledger.Replace(held with { Inventory = new TF2.Descent.Rules.Inventory.HeroInventory() });

        rig.Hit(A, 500);

        Assert.Empty(rig.Corpses.Standing);
    }

    [Fact]
    public async Task A_hardcore_heros_corpse_is_lootable_by_nobody()
    {
        var rig = new DownedRig();
        Assert.True(rig.Heroes.Sheets.Apply(A, s => s with { Hardcore = true }).Ok);
        rig.Clock.Origins[PlayerOf(A)] = There;
        rig.Hit(A, 500);
        rig.RunTo(1200);
        rig.Clock.Respawn(PlayerOf(A));

        Assert.Equal("refused: hardcore", await rig.Graves.Use(A));
    }

    [Fact]
    public void A_new_level_starts_with_no_corpse()
    {
        var rig = DeadAt(There);

        rig.Graves.OnLevelInit("next");

        Assert.Empty(rig.Graves.All);
    }

    [Fact]
    public void A_press_of_use_in_the_frame_loots_his_corpse()
    {
        var rig = DeadAt(There);
        rig.Clock.Respawn(PlayerOf(A));
        rig.Clock.Origins[PlayerOf(A)] = There;

        rig.Clock.Holding.Add(PlayerOf(A));
        rig.RunTo(1201);

        Assert.Empty(rig.Corpses.Standing);
    }

    [Fact]
    public void A_downed_heros_use_loots_nothing()
    {
        var rig = DeadAt(There);
        rig.Clock.Respawn(PlayerOf(A));
        rig.RunTo(1300);
        rig.Clock.Origins[PlayerOf(A)] = There;
        rig.Hit(A, 500);

        rig.Clock.Holding.Add(PlayerOf(A));
        rig.RunTo(1301);

        Assert.True(rig.Hero(A).Downed);
        Assert.Single(rig.Corpses.Standing);
    }

    [Fact]
    public void A_bleed_out_is_named_for_the_robot_that_downed_him()
    {
        var rig = new DownedRig();
        var robot = new EntityHandle(0x0002_0100u);
        rig.Robots.Bodies[robot] = "soldier";
        rig.Hit(A, 500, robot);

        var death = new SourceSharp.Server.DeathNoticeFacts(PlayerOf(A), EntityHandle.None, EntityHandle.None, EntityHandle.None, "world");
        Assert.True(rig.Killers.TryToken(in death, out string token, out int team));
        Assert.Equal(("#Descent_Robot_soldier", 3), (token, team));
    }

    [Fact]
    public void A_robots_own_kill_is_named_for_its_template()
    {
        var rig = new DownedRig();
        var robot = new EntityHandle(0x0002_0100u);
        rig.Robots.Bodies[robot] = "heavy";

        var death = new SourceSharp.Server.DeathNoticeFacts(PlayerOf(A), robot, EntityHandle.None, robot, "minigun");
        Assert.True(rig.Killers.TryToken(in death, out string token, out _));
        Assert.Equal("#Descent_Robot_heavy", token);
    }

    [Fact]
    public void A_world_death_with_no_robot_behind_it_is_not_named()
    {
        var rig = new DownedRig();

        var death = new SourceSharp.Server.DeathNoticeFacts(PlayerOf(A), EntityHandle.None, EntityHandle.None, EntityHandle.None, "world");
        Assert.False(rig.Killers.TryToken(in death, out _, out _));
    }
}
