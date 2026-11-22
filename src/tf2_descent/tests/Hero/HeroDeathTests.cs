using SourceSharp.Abi;

using TF2.Descent.Rules.Death;
using TF2.Descent.Rules.Items;
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

    /// <summary>
    /// The whole run's F24: a party wipe's death lines said "0 item(s)" with a corpse of 10 items on
    /// the next line. Each wiped hero's line names what he carried, and the corpse the same.
    /// </summary>
    [Fact]
    public void A_party_wipes_death_lines_name_what_each_hero_carried()
    {
        var rig = new DownedRig();
        int a = rig.Ledger.Find(rig.Key(A))!.Inventory.Items.Count();
        int b = rig.Ledger.Find(rig.Key(B))!.Inventory.Items.Count();

        rig.Hit(B, 500);
        rig.Hit(A, 500);
        rig.RunTo(1200);

        var lines = rig.Heroes.Log.Lines.Select(l => l.Text).Where(l => l.Contains("[TF2Descent/Death]", StringComparison.Ordinal)).ToList();
        Assert.Contains(lines, l => l.Contains($"player {A} died carrying {a} item(s)", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains($"player {B} died carrying {b} item(s)", StringComparison.Ordinal));
        Assert.Equal(a, rig.Ledger.Corpse(rig.Key(A))!.Items.Count);
        Assert.Equal(b, rig.Ledger.Corpse(rig.Key(B))!.Items.Count);
    }

    /// <summary>
    /// F24's cause: a hero who died again before taking his corpse back carries nothing (he respawned
    /// unarmed), so his death line says 0, while the corpse, moved to the new death, still holds what
    /// the first one left. The corpse's line says it moved and holds an earlier death's items.
    /// </summary>
    [Fact]
    public void A_second_death_before_the_corpse_is_taken_carries_nothing_and_the_moved_corpse_says_so()
    {
        var rig = new DownedRig(A);
        int carried = rig.Ledger.Find(rig.Key(A))!.Inventory.Items.Count();
        rig.Hit(A, 500);
        rig.RunTo(1200);
        rig.Clock.Respawn(PlayerOf(A));
        rig.RunTo(3000);   // past his respawn's grace
        int mark = rig.Heroes.Log.Lines.Count;

        rig.Hit(A, 500);

        var lines = rig.Heroes.Log.Lines.Skip(mark).Select(l => l.Text).ToList();
        Assert.Contains(lines, l => l.Contains($"player {A} died carrying 0 item(s)", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("[TF2Descent/Corpse]", StringComparison.Ordinal)
                                    && l.Contains($"{carried} item(s)", StringComparison.Ordinal)
                                    && l.Contains("moved", StringComparison.Ordinal));
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

    /// <summary>The hero's ids by container: equipment slot, belt slot, and the backpack's ids.</summary>
    private static (Dictionary<string, long> Worn, Dictionary<int, long> Belt, long[] Backpack) Containers(DownedRig rig, int index)
    {
        var inv = rig.Ledger.Find(rig.Key(index))!.Inventory;
        return (inv.Equipped.ToDictionary(e => e.Key, e => e.Value.Id), inv.Belt.ToDictionary(e => e.Key, e => e.Value.Id),
                [.. inv.Backpack.Values.Select(h => h.Id).Order()]);
    }

    /// <summary>The starting kit's weapons (in the backpack as the ledger loads it) put on, as the game's first settle does.</summary>
    private static async Task<DownedRig> Kitted()
    {
        var rig = new DownedRig();
        foreach (var (baseId, slot) in new[] { ("rocket_launcher", "primary"), ("shotgun", "secondary"), ("shovel", "melee") })
        {
            int from = rig.Ledger.Find(rig.Key(A))!.Inventory.Backpack.First(p => p.Value.Item.BaseId == baseId).Key;
            Assert.True((await rig.Ledger.Equip(rig.Key(A), from, slot)).Ok);
        }
        return rig;
    }

    private static async Task<DownedRig> RecoveredAfterADeath(Func<DownedRig, Task>? before = null)
    {
        var rig = await Kitted();
        if (before is not null)
            await before(rig);
        rig.Clock.Origins[PlayerOf(A)] = There;
        rig.Hit(A, 500);
        rig.RunTo(1200);
        rig.Clock.Respawn(PlayerOf(A));
        Assert.StartsWith("recovered", await rig.Graves.Use(A));
        return rig;
    }

    [Fact]
    public async Task His_worn_items_go_back_on_to_their_slots()
    {
        var rig = await Kitted();
        var worn = Containers(rig, A).Worn;
        Assert.Equal(3, worn.Count);   // the stimulus: the kit's launcher, shotgun and shovel

        rig = await RecoveredAfterADeath();

        Assert.Equal(worn.Keys, Containers(rig, A).Worn.Keys);
        Assert.Equal(rig.Ledger.LastRecovery!.Equipped, Containers(rig, A).Worn.Count);
        Assert.Empty(Containers(rig, A).Backpack);
    }

    [Fact]
    public async Task Each_recovered_item_is_back_in_the_container_it_died_in_by_id()
    {
        (Dictionary<string, long>, Dictionary<int, long>, long[])? before = null;

        var rig = await RecoveredAfterADeath(r =>
        {
            before = Containers(r, A);
            return Task.CompletedTask;
        });

        var (worn, belt, pack) = Containers(rig, A);
        Assert.Equal(before!.Value.Item1, worn);
        Assert.Equal(before.Value.Item2, belt);
        Assert.Equal(before.Value.Item3, pack);
    }

    [Fact]
    public async Task His_belt_potions_go_back_on_the_belt()
    {
        var rig = await RecoveredAfterADeath();

        Assert.Equal(3, Containers(rig, A).Belt.Count);
        Assert.Equal(3, rig.Ledger.LastRecovery!.Belted);
    }

    [Fact]
    public async Task His_backpack_items_go_back_to_the_backpack()
    {
        long given = 0;
        var rig = await RecoveredAfterADeath(async r =>
        {
            Assert.Empty(r.Ledger.Find(r.Key(A))!.Inventory.Backpack);
            Assert.True((await r.Ledger.Give(r.Key(A), "dog_tags", 0, 1)).Ok);
            given = r.Ledger.Find(r.Key(A))!.Inventory.Backpack.Values.Single().Id;
        });

        Assert.Equal([given], Containers(rig, A).Backpack);
    }

    [Fact]
    public async Task A_slot_filled_on_the_walk_back_sends_the_corpses_item_to_the_backpack()
    {
        var rig = await Kitted();
        long shovel = rig.Ledger.Find(rig.Key(A))!.Inventory.Equipped["melee"].Id;
        rig.Clock.Origins[PlayerOf(A)] = There;
        rig.Hit(A, 500);
        rig.RunTo(1200);
        rig.Clock.Respawn(PlayerOf(A));
        Assert.True((await rig.Ledger.Give(rig.Key(A), "shovel", 0, 1)).Ok);
        Assert.True((await rig.Ledger.Equip(rig.Key(A), 0, "melee")).Ok);
        long newShovel = rig.Ledger.Find(rig.Key(A))!.Inventory.Equipped["melee"].Id;

        await rig.Graves.Use(A);

        var (worn, _, pack) = Containers(rig, A);
        Assert.Equal(newShovel, worn["melee"]);
        Assert.Equal([shovel], pack);
    }

    [Fact]
    public async Task The_wear_pending_at_a_death_is_written_onto_the_items_before_they_move()
    {
        long cap = 0;
        ItemInstance? worn = null;
        var rig = await RecoveredAfterADeath(async r =>
        {
            Assert.True((await r.Ledger.Give(r.Key(A), "cap", 0, 1)).Ok);
            int from = r.Ledger.Find(r.Key(A))!.Inventory.Backpack.Single(p => p.Value.Item.BaseId == "cap").Key;
            Assert.True((await r.Ledger.Equip(r.Key(A), from, "hat")).Ok);
            cap = r.Ledger.Find(r.Key(A))!.Inventory.Equipped["hat"].Id;
            worn = r.Ledger.Find(r.Key(A))!.Inventory.Equipped["hat"].Item;
            r.Wear.Hit(A);
            Assert.True(r.Wear.HasPending(r.Key(A), cap), "the stimulus: a hit below a point is pending");
        });

        var after = rig.Ledger.Find(rig.Key(A))!.Inventory.Equipped["hat"];
        Assert.Equal(cap, after.Id);
        Assert.False(rig.Wear.HasPending(rig.Key(A), cap));
        Assert.NotEqual(worn, after.Item);
        Assert.True(after.Item.WearUses > worn!.WearUses, $"wear progress {worn.WearUses} -> {after.Item.WearUses}");
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
        var rig = await Kitted();
        rig.Clock.Origins[PlayerOf(A)] = There;
        rig.Hit(A, 500);
        rig.RunTo(1200);
        rig.Clock.Respawn(PlayerOf(A));
        rig.Clock.Origins[PlayerOf(A)] = There;
        int corpse = rig.Ledger.Corpse(rig.Key(A))!.Items.Count;
        // While he walked back he took up a shovel and filled his backpack: the corpse's
        // shovel can go neither to its old slot nor to the backpack; the rest goes back.
        int room = rig.Heroes.Rules.Inventory.BackpackSlots(rig.Hero(A).Sheet!);
        for (int i = 0; i < room; i++)
            Assert.True((await rig.Ledger.Give(rig.Key(A), "shovel", 0, 1)).Ok);
        Assert.True((await rig.Ledger.Equip(rig.Key(A), 0, "melee")).Ok);
        Assert.True((await rig.Ledger.Give(rig.Key(A), "shovel", 0, 1)).Ok);

        string done = await rig.Graves.Use(A);

        Assert.Equal($"recovered {corpse - 1}, 1 stay", done);
        Assert.Single(rig.Ledger.Corpse(rig.Key(A))!.Items);
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
