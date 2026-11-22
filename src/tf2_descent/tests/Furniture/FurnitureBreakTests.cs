using SourceSharp.Abi;

using TF2.Descent.Rules.Furniture;
using TF2.Descent.Rules.Loot;
using TF2.Descent.Server.Furniture;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests.Furniture;

/// <summary>
/// Crates and barrels broken on the server (the user, 2026-11-15): a hit makes exactly one roll, the
/// rules' own for the level seed and the marker, and each outcome does what it says; a supply crate a
/// hero strikes breaks open. The rig's level is depth 1 seed 1 with a built level's breakable
/// markers (<see cref="FurnitureRig.D1S1Breakables"/>), which hold the crates and barrels.
/// </summary>
public sealed class FurnitureBreakTests
{
    private static FurnitureRig Breakables(ulong seed = 1) => new(seed, level: FurnitureRig.D1S1Breakables);

    private static (FurnitureRig Rig, FurnitureRow Row, FurnitureHero Hero) Crate(ulong seed = 1)
    {
        var rig = Breakables(seed);
        var row = rig.First(FurnitureKind.Breakable);
        var hero = rig.Stand(FurnitureRig.SlotA, row);
        return (rig, row, hero);
    }

    /// <summary>The row struck by the hero and broken, with <paramref name="outcome"/> forced (null: its own roll).</summary>
    private static async Task<BreakOutcome?> BreakAs(FurnitureRig rig, FurnitureRow row, FurnitureHero hero, BreakOutcome? outcome)
    {
        rig.Service.ForceNext(outcome);
        Assert.True(rig.Service.Strike(row, hero.Player, 10));
        return await rig.Service.Break(row);
    }

    [Fact]
    public void Depth_1_seed_1_holds_crates_and_barrels_on_its_breakable_markers_and_none_takes_a_pool_entity()
    {
        var rig = Breakables();
        var breakables = rig.Service.Rows.Where(r => r.Kind == FurnitureKind.Breakable).ToList();

        Assert.NotEmpty(breakables);
        Assert.All(breakables, r => Assert.Null(r.Entity));
        Assert.All(breakables, r => Assert.Contains(r.Variant, FurnitureRig.Rules.Content.Furniture.Breakables.Kinds));
    }

    [Fact]
    public void A_struck_crate_stops_nothing_at_once_and_breaks_at_the_next_think()
    {
        var (rig, row, hero) = Crate();

        bool struck = rig.Service.Strike(row, hero.Player, 5);
        bool goneAtOnce = row.Gone, brokenAtOnce = row.Went;
        rig.Think();

        Assert.True(struck);
        Assert.True(goneAtOnce);
        Assert.False(brokenAtOnce);
        Assert.True(row.Went);
        Assert.NotNull(row.Broke);
        Assert.Equal(1, rig.Service.Broken);
    }

    [Fact]
    public void A_crate_struck_again_and_again_makes_exactly_one_roll()
    {
        var (rig, row, hero) = Crate();

        rig.Service.Strike(row, hero.Player, 5);
        bool again = rig.Service.Strike(row, hero.Player, 5);
        rig.Think();
        bool afterBreak = rig.Service.Strike(row, hero.Player, 50);
        rig.Think(3);

        Assert.False(again);
        Assert.False(afterBreak);
        Assert.Equal(1, rig.Service.Broken);
        Assert.Single(rig.Breaks.Shattered);
        Assert.Single(rig.Log.Lines, l => l.Text.Contains($"#{row.Index} ({row.Placed.Marker}) broken by"));
    }

    [Fact]
    public async Task A_break_rolls_the_rules_roll_for_the_level_seed_and_its_marker()
    {
        // Every crate and barrel of twelve level seeds, each against the rules' roll for its seed and marker.
        var outcomes = new HashSet<BreakOutcome>();
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var rig = Breakables(seed);
            foreach (var row in rig.Service.Rows.Where(r => r.Kind == FurnitureKind.Breakable))
            {
                var outcome = await BreakAs(rig, row, rig.Stand(FurnitureRig.SlotA, row), null);

                Assert.Equal(FurnitureRig.Rules.Furniture.Breaks.Roll(seed, row.Placed.Marker).Outcome, outcome);
                Assert.Equal(outcome, row.Broke);
                outcomes.Add(outcome!.Value);
            }
        }

        Assert.True(outcomes.Count >= 3, $"only {outcomes.Count} outcomes rolled");
    }

    [Fact]
    public async Task A_break_plays_the_wood_break_of_its_model_where_it_stood()
    {
        var (rig, row, hero) = Crate();

        await BreakAs(rig, row, hero, BreakOutcome.Nothing);

        var (model, at, yaw, _) = Assert.Single(rig.Breaks.Shattered);
        Assert.Equal(FurnitureCatalog.Of(FurnitureCatalog.Number(FurnitureKindId.Breakable, row.Variant))!.SpentModel, model);
        Assert.Equal(row.Position.X, at.X);
        Assert.Equal(row.Position.Y, at.Y);
        Assert.Equal(row.Yaw, yaw);
    }

    [Fact]
    public async Task A_crate_breaks_as_its_broken_body_whose_every_piece_is_a_splinter()
    {
        // miningcrate001's own break leaves its body standing (gibs/miningcrate001_break01, the
        // DYNAMITE lid showing) beside two planks; the body's break is the six splinters.
        var rig = Breakables();
        var row = rig.First(FurnitureKind.Breakable, "crate");

        await BreakAs(rig, row, rig.Stand(FurnitureRig.SlotA, row), BreakOutcome.Nothing);

        Assert.Equal(FurnitureCatalog.CrateOpenedModel, Assert.Single(rig.Breaks.Shattered).Model);
    }

    [Fact]
    public async Task A_barrel_breaks_as_itself_its_staves_and_half_a_barrel()
    {
        var rig = Breakables();
        var row = rig.First(FurnitureKind.Breakable, "barrel");

        await BreakAs(rig, row, rig.Stand(FurnitureRig.SlotA, row), BreakOutcome.Nothing);

        Assert.Equal(FurnitureCatalog.WoodenBarrelModel, Assert.Single(rig.Breaks.Shattered).Model);
    }

    [Fact]
    public async Task A_breaks_pieces_fly_with_the_hits_push_over_the_props_mass()
    {
        // Stock's prop_physics takes the hit's force on its physics (VPhysicsTakeDamage) before it
        // breaks, so the pieces leave at force / mass: the wooden barrel's .phy mass is 60.
        var rig = Breakables();
        var row = rig.First(FurnitureKind.Breakable, "barrel");
        var hero = rig.Stand(FurnitureRig.SlotA, row);
        rig.Service.ForceNext(BreakOutcome.Nothing);

        rig.Service.Strike(row, hero.Player, 65f, force: new Vector(0f, 19500f, 0f));
        await rig.Service.Break(row);

        var velocity = Assert.Single(rig.Breaks.Shattered).Velocity;
        Assert.Equal(0f, velocity.X);
        Assert.Equal(19500f / FurnitureModels.Mass(FurnitureCatalog.WoodenBarrelModel), velocity.Y, 3);
        Assert.Equal(325f, velocity.Y, 3);
    }

    [Fact]
    public async Task Nothing_puts_nothing_on_the_floor_and_hurts_nobody()
    {
        var (rig, row, hero) = Crate();

        await BreakAs(rig, row, hero, BreakOutcome.Nothing);

        Assert.Empty(rig.Floor.Drops);
        Assert.Empty(rig.World.Explosions);
        Assert.Empty(rig.Breaks.Marks);
        Assert.Empty(rig.Breaks.Ambushes);
    }

    [Fact]
    public async Task An_item_break_gives_each_hero_in_its_room_his_own_item_and_none_to_a_hero_beyond_it()
    {
        var (rig, row, hero) = Crate();
        rig.Stand(FurnitureRig.SlotB, row);
        rig.Heroes.Add(3, "hero-c", new Vector(-90000, -90000, 0));

        await BreakAs(rig, row, hero, BreakOutcome.Item);

        var seats = rig.Floor.Drops.SelectMany(d => d.Seats).ToList();
        Assert.Equal([FurnitureRig.A, FurnitureRig.B], seats.Select(s => s.Hero).Order());
        foreach (var seat in seats)
        {
            var share = FurnitureRig.Rules.Loot.Breakable(FurnitureRig.Rules.Furniture.Breaks.Request(1, row.Placed.Marker, seat.Hero), BreakOutcome.Item, rig.Service.Depth);
            Assert.Equal(share.Tier, seat.Item!.Item.Rarity);
        }
    }

    [Fact]
    public async Task An_australium_break_puts_each_heroes_own_pile_on_the_floor()
    {
        var (rig, row, hero) = Crate();
        rig.Stand(FurnitureRig.SlotB, row);

        await BreakAs(rig, row, hero, BreakOutcome.Australium);

        var seats = rig.Floor.Drops.SelectMany(d => d.Seats).ToList();
        Assert.Equal(2, seats.Count);
        foreach (var seat in seats)
        {
            long pile = FurnitureRig.Rules.Loot.Breakable(FurnitureRig.Rules.Furniture.Breaks.Request(1, row.Placed.Marker, seat.Hero), BreakOutcome.Australium, rig.Service.Depth).Australium;
            Assert.True(pile > 0);
            Assert.Equal(pile, seat.Australium);
            Assert.Null(seat.Item);
        }
    }

    [Fact]
    public async Task A_potion_break_gives_each_hero_a_potion_of_the_depths_band()
    {
        var (rig, row, hero) = Crate();

        await BreakAs(rig, row, hero, BreakOutcome.Potion);

        var seat = Assert.Single(rig.Floor.Drops.SelectMany(d => d.Seats));
        var band = FurnitureRig.Rules.Content.Loot.Table(FurnitureRig.Rules.Robots.Depth(rig.Service.Depth).LootTable).Potions.Select(p => p.Base);
        Assert.Contains(seat.Item!.Item.BaseId, band);
    }

    [Fact]
    public async Task A_blast_break_goes_off_as_the_furniture_barrel_in_the_breakers_name()
    {
        var (rig, row, hero) = Crate();
        var barrel = FurnitureRig.Rules.Content.Furniture.Explosive(FurnitureRig.Rules.Content.Furniture.Breakables.Blast);

        await BreakAs(rig, row, hero, BreakOutcome.Blast);

        Assert.Single(rig.World.Explosions);
        var blast = Assert.Single(rig.World.Blasts);
        Assert.Equal(hero.Player, blast.Attacker);
        Assert.Equal((float)barrel.Damage, blast.Damage);
        Assert.Equal((float)barrel.Radius, blast.Radius);
        Assert.Contains(rig.World.Hits, h => h.Victim == hero.Player && h.Damage > 0);
    }

    [Fact]
    public async Task A_cloud_break_marks_each_hero_in_its_radius_and_not_one_beyond_it()
    {
        var (rig, row, hero) = Crate();
        var cloud = FurnitureRig.Rules.Content.Furniture.Breakables.Cloud;
        var far = rig.Stand(FurnitureRig.SlotB, row, new Vector((float)cloud.Radius + 100, 0, 0));

        await BreakAs(rig, row, hero, BreakOutcome.Cloud);

        Assert.Single(rig.Breaks.Clouds);
        var mark = Assert.Single(rig.Breaks.Marks);
        Assert.Equal((hero.Player, cloud.Condition, cloud.Seconds), (mark.Hero, mark.Condition, mark.Seconds));
        Assert.DoesNotContain(rig.Breaks.Marks, m => m.Hero == far.Player);
    }

    [Fact]
    public async Task An_ambush_break_wakes_the_rules_robots_beside_it_hunting_the_breaker()
    {
        var (rig, row, hero) = Crate();

        await BreakAs(rig, row, hero, BreakOutcome.Ambush);

        var call = Assert.Single(rig.Breaks.Ambushes);
        Assert.Equal(FurnitureRig.Rules.Furniture.Breaks.Ambush(1, row.Placed.Marker, rig.Service.Depth).ToArray(), call.Templates);
        Assert.Equal(hero.Player, call.Hero);
        Assert.Equal(row.Position, call.At);
        Assert.Equal(FurnitureRig.Rules.Content.Furniture.Breakables.Ambush.HuntSeconds, call.HuntSeconds);
    }

    [Fact]
    public async Task A_forced_outcome_serves_one_break_and_the_next_rolls_its_own()
    {
        var rig = Breakables();
        var rows = rig.Service.Rows.Where(r => r.Kind == FurnitureKind.Breakable).Take(2).ToList();
        Assert.Equal(2, rows.Count);
        var hero = rig.Stand(FurnitureRig.SlotA, rows[0]);

        var first = await BreakAs(rig, rows[0], hero, BreakOutcome.Cloud);
        Assert.True(rig.Service.Strike(rows[1], hero.Player, 10));
        var second = await rig.Service.Break(rows[1]);

        Assert.Equal(BreakOutcome.Cloud, first);
        Assert.Equal(FurnitureRig.Rules.Furniture.Breaks.Roll(1, rows[1].Placed.Marker).Outcome, second);
        Assert.Null(rig.Service.ForcedNext);
    }

    [Fact]
    public void The_breakables_gibs_are_precached_while_the_level_loads()
    {
        var rig = Breakables();

        rig.Service.OnLevelInitPreEntity("tf2_descent-1-s1");

        Assert.Equal([FurnitureCatalog.CrateOpenedModel, FurnitureCatalog.CrateModel, FurnitureCatalog.WoodenBarrelModel], rig.Breaks.Precached.Order());
    }

    //-------------------------------------------------------------------------
    // The supply crate
    //-------------------------------------------------------------------------

    private static (FurnitureRig Rig, FurnitureRow Row) SupplyCrate()
    {
        for (ulong seed = 1; seed < 5000; seed++)
        {
            var rig = new FurnitureRig(seed);
            if (rig.Service.Rows.FirstOrDefault(r => r is { Kind: FurnitureKind.Container, Variant: "supply_crate", Placed.KeyId: null }) is { } row)
                return (rig, row);
        }

        throw new InvalidOperationException("no seed places an unlocked supply crate");
    }

    [Fact]
    public async Task A_supply_crate_a_hero_strikes_breaks_open_for_him_as_use_opens_it()
    {
        var (rig, row) = SupplyCrate();
        var hero = rig.Stand(FurnitureRig.SlotA, row);

        bool struck = rig.Service.Strike(row, hero.Player, 10);
        await rig.Service.Break(row);

        Assert.True(struck);
        Assert.True(row.Container!.Opened);
        Assert.Equal(FurnitureFlags.Opened, (FurnitureFlags)row.Entity!.State.Flags);
        Assert.Single(rig.Breaks.Shattered);
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains($"container #{row.Index}") && l.Text.Contains("broken open"));
    }

    [Fact]
    public async Task A_supply_crate_broken_open_shatters_the_body_its_entity_now_wears_not_a_second_one()
    {
        // The standing crate's own break leaves its body (miningcrate001_break01) among the client's
        // pieces, and the opened entity wears that body too: two stood where one should (seen live,
        // d1 s2 crate #30, the client's prop at the entity's spot). The body's own break is splinters.
        var (rig, row) = SupplyCrate();
        var hero = rig.Stand(FurnitureRig.SlotA, row);

        rig.Service.Strike(row, hero.Player, 10);
        await rig.Service.Break(row);

        Assert.Equal(FurnitureCatalog.CrateOpenedModel, row.Entity!.Model);
        Assert.Equal(FurnitureCatalog.CrateOpenedModel, Assert.Single(rig.Breaks.Shattered).Model);
    }

    [Fact]
    public void A_supply_crate_takes_a_hit_from_no_hero_as_nothing()
    {
        var (rig, row) = SupplyCrate();

        Assert.False(rig.Service.Strike(row, EntityHandle.None, 10));
        Assert.False(row.Gone);
    }

    [Fact]
    public void A_shut_supply_crate_takes_damage_and_a_locker_does_not()
    {
        var (rig, row) = SupplyCrate();

        Assert.Equal(1, rig.Service.Look(row).Health);
        Assert.All(rig.Service.Rows.Where(r => r is { Kind: FurnitureKind.Container } && r.Variant != "supply_crate"),
            r => Assert.Equal(0, rig.Service.Look(r).Health));
    }
}
