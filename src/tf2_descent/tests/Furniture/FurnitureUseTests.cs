using SourceSharp.Abi;

using TF2.Descent.Rules.Furniture;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Loot;
using TF2.Descent.Server.Furniture;
using TF2.Descent.Server.Items;
using TF2.Descent.Shared;

using Xunit;

namespace TF2.Descent.Tests.Furniture;

/// <summary>
/// A shrine, the Dispenser and a container used, each against the rules' own answer for the same
/// inputs (plan_descent_logic WO3: "each kind used once matches its Rules replay").
/// </summary>
public sealed class FurnitureUseTests
{
    private static FurnitureRig RigWith(FurnitureKind kind, string? variant = null) =>
        new(FurnitureRig.SeedWith((kind, variant)));

    //-------------------------------------------------------------------------
    // Shrines
    //-------------------------------------------------------------------------

    [Fact]
    public async Task A_shrine_used_gives_its_taker_the_rules_contributions()
    {
        var rig = RigWith(FurnitureKind.Shrine, "fury");
        var row = rig.First(FurnitureKind.Shrine, "fury");
        var hero = rig.Stand(FurnitureRig.SlotA, row);
        var replay = FurnitureRig.Rules.Furniture.Shrines.Use(new ShrineState(row.Placed.Marker, "fury"), FurnitureRig.A, [FurnitureRig.A], _ => [], rig.Service.Seed);

        var outcome = await rig.Service.UseAs(row, hero);

        var contributions = new FurnitureContributions(rig.Buffs, rig.Heroes).ContributionsOf(FurnitureRig.SlotA, null!);
        Assert.Equal(FurnitureService.UseOutcome.Used, outcome);
        Assert.Equal(ShrineRules.Contributions(ShrineRules.Take(ShrineBuffs.None, replay, FurnitureRig.A)), contributions);
        Assert.Contains(new Contribution("damage_all_pct", 25, new ContributionSource(SourceKind.Buff, "shrine:fury")), contributions);
        Assert.Contains(FurnitureRig.SlotA, rig.Heroes.Recomputed);
    }

    [Fact]
    public async Task A_used_shrine_refuses_says_so_and_looks_spent()
    {
        var rig = RigWith(FurnitureKind.Shrine);
        var row = rig.First(FurnitureKind.Shrine);
        var hero = rig.Stand(FurnitureRig.SlotA, row);
        await rig.Service.UseAs(row, hero);
        int recomputes = rig.Heroes.Recomputed.Count;

        var again = await rig.Service.UseAs(row, hero);

        Assert.Equal(FurnitureService.UseOutcome.Refused, again);
        Assert.Equal(recomputes, rig.Heroes.Recomputed.Count);
        Assert.Equal(FurnitureCatalog.ShrineSpentModel, row.Entity!.Model);
        Assert.Equal(FurnitureFlags.Spent, (FurnitureFlags)row.Entity.State.Flags);
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("refused, AlreadyUsed"));
    }

    [Fact]
    public async Task A_level_long_shrine_effect_ends_with_the_level()
    {
        var rig = RigWith(FurnitureKind.Shrine, "fury");
        var row = rig.First(FurnitureKind.Shrine, "fury");
        await rig.Service.UseAs(row, rig.Stand(FurnitureRig.SlotA, row));
        Assert.Equal(1, rig.Buffs.Carrying);

        rig.Service.OnLevelShutdownPreEntity();

        Assert.Equal(0, rig.Buffs.Carrying);
        Assert.Empty(new FurnitureContributions(rig.Buffs, rig.Heroes).ContributionsOf(FurnitureRig.SlotA, null!));
    }

    [Fact]
    public async Task A_refill_shrine_fills_its_takers_life_and_mana()
    {
        var rig = RigWith(FurnitureKind.Shrine, "plenty");
        var row = rig.First(FurnitureKind.Shrine, "plenty");

        await rig.Service.UseAs(row, rig.Stand(FurnitureRig.SlotA, row));

        Assert.Equal([FurnitureRig.SlotA], rig.Heroes.Refilled);
        Assert.Equal(0, rig.Buffs.Carrying);
    }

    [Fact]
    public async Task A_sight_shrine_reveals_every_room_on_its_takers_map_and_shows_used()
    {
        var rig = RigWith(FurnitureKind.Shrine, "sight");
        var row = rig.First(FurnitureKind.Shrine, "sight");

        await rig.Service.UseAs(row, rig.Stand(FurnitureRig.SlotA, row));

        Assert.Equal([FurnitureRig.SlotA], rig.Automap.RevealedAll);
        Assert.Equal([row.Placed.Marker], rig.Automap.ShrinesUsed);
    }

    [Fact]
    public async Task A_party_wide_shrine_takes_the_users_party_and_not_a_stranger()
    {
        var rig = RigWith(FurnitureKind.Shrine, "sight");
        var row = rig.First(FurnitureKind.Shrine, "sight");
        rig.Stand(FurnitureRig.SlotB, row, new Vector(10, 10, 0));
        rig.Parties.Strangers.Add(FurnitureRig.SlotB);    // WO9: B is in no party with A

        await rig.Service.UseAs(row, rig.Stand(FurnitureRig.SlotA, row));

        Assert.Equal([FurnitureRig.SlotA], rig.Automap.RevealedAll);
    }

    [Fact]
    public async Task A_party_wide_shrine_takes_every_member_of_the_users_party()
    {
        var rig = RigWith(FurnitureKind.Shrine, "sight");
        var row = rig.First(FurnitureKind.Shrine, "sight");
        rig.Stand(FurnitureRig.SlotB, row, new Vector(10, 10, 0));

        await rig.Service.UseAs(row, rig.Stand(FurnitureRig.SlotA, row));

        Assert.Equal([FurnitureRig.SlotA, FurnitureRig.SlotB], rig.Automap.RevealedAll.Order());
    }

    [Fact]
    public async Task A_shrine_that_is_not_of_sight_shows_used_and_reveals_no_room()
    {
        var rig = RigWith(FurnitureKind.Shrine, "plenty");
        var row = rig.First(FurnitureKind.Shrine, "plenty");

        await rig.Service.UseAs(row, rig.Stand(FurnitureRig.SlotA, row));

        Assert.Empty(rig.Automap.RevealedAll);
        Assert.Equal([row.Placed.Marker], rig.Automap.ShrinesUsed);
    }

    [Fact]
    public async Task A_shrine_whose_room_sleeps_answers_no_use()
    {
        var rig = RigWith(FurnitureKind.Shrine);
        var row = rig.First(FurnitureKind.Shrine);
        rig.Rooms.Asleep.Add(row.Room);
        rig.Service.CheckRooms();

        var outcome = await rig.Service.UseAs(row, rig.Stand(FurnitureRig.SlotA, row));

        Assert.Equal(FurnitureService.UseOutcome.NotThere, outcome);
        Assert.False(row.Shrine!.Used);
    }

    //-------------------------------------------------------------------------
    // The Dispenser
    //-------------------------------------------------------------------------

    [Fact]
    public void A_dispenser_heals_a_hero_in_reach_by_the_rules_draw()
    {
        var rig = RigWith(FurnitureKind.Dispenser);
        var row = rig.First(FurnitureKind.Dispenser);
        rig.Stand(FurnitureRig.SlotA, row, new Vector(30, 0, 0));
        rig.Heroes.LifeOf[FurnitureRig.SlotA] = 100;
        var replay = FurnitureRig.Rules.Furniture.Dispensers.Draw(FurnitureRig.Rules.Furniture.Dispensers.New(), 1.0, 100, 0);

        for (int i = 0; i < 5; i++)
            rig.Service.Dispense(100 + i * 0.2, 0.2);

        Assert.Equal(20, replay.Health, 6);
        Assert.Equal(100 + (int)replay.Health, rig.Heroes.LifeOf[FurnitureRig.SlotA]);
        Assert.Equal(replay.After.Charge, row.Dispenser!.Charge, 6);
    }

    [Fact]
    public void A_dispenser_gives_ammo_after_health_from_the_same_charge()
    {
        var rig = RigWith(FurnitureKind.Dispenser);
        var row = rig.First(FurnitureKind.Dispenser);
        rig.Stand(FurnitureRig.SlotA, row, new Vector(30, 0, 0));
        rig.Heroes.LifeOf[FurnitureRig.SlotA] = 190;
        rig.Heroes.AmmoMissing[FurnitureRig.SlotA] = 50;
        var replay = FurnitureRig.Rules.Furniture.Dispensers.Draw(FurnitureRig.Rules.Furniture.Dispensers.New(), 1.0, 10, 50);

        rig.Service.Dispense(100, 1.0);

        Assert.Equal((10, 40), ((int)replay.Health, (int)replay.Ammo));
        Assert.Equal(200, rig.Heroes.LifeOf[FurnitureRig.SlotA]);
        Assert.Equal(10, rig.Heroes.AmmoMissing[FurnitureRig.SlotA]);
        Assert.Equal(replay.After.Charge, row.Dispenser!.Charge, 6);
    }

    [Fact]
    public void A_hero_beyond_a_dispensers_reach_draws_nothing()
    {
        var rig = RigWith(FurnitureKind.Dispenser);
        var row = rig.First(FurnitureKind.Dispenser);
        rig.Stand(FurnitureRig.SlotA, row, new Vector(70, 0, 0));
        rig.Heroes.LifeOf[FurnitureRig.SlotA] = 100;

        rig.Service.Dispense(100, 1.0);

        Assert.Equal(100, rig.Heroes.LifeOf[FurnitureRig.SlotA]);
        Assert.Equal(FurnitureRig.Rules.Content.Furniture.Dispenser.Charge, row.Dispenser!.Charge);
    }

    [Fact]
    public void A_dispensers_charge_runs_out_and_it_looks_spent()
    {
        var rig = RigWith(FurnitureKind.Dispenser);
        var row = rig.First(FurnitureKind.Dispenser);
        rig.Stand(FurnitureRig.SlotA, row, new Vector(30, 0, 0));
        double last = row.Dispenser!.Charge;

        for (int i = 0; i < 100 && row.Dispenser.Charge > 0; i++)
        {
            rig.Heroes.LifeOf[FurnitureRig.SlotA] = 1;
            rig.Service.Dispense(100 + i, 10);
            Assert.True(row.Dispenser.Charge <= last, "the charge rose");
            last = row.Dispenser.Charge;
        }

        Assert.Equal(0, row.Dispenser.Charge);
        Assert.Equal(FurnitureFlags.Spent, (FurnitureFlags)row.Entity!.State.Flags);
        Assert.Equal(1, rig.Skins.Skin[row.Entity.Handle]);
    }

    //-------------------------------------------------------------------------
    // Containers
    //-------------------------------------------------------------------------

    private static (FurnitureRig Rig, FurnitureRow Row) Container(Func<Placed, bool>? which = null, Func<IItemLedger, IItemLedger>? ledger = null)
    {
        which ??= p => p.KeyId is null && p.Trap is null;
        var rooms = Server.Level.LevelAdapters.ToFurniture(FurnitureRig.D1S1);
        for (ulong seed = 1; seed < 5000; seed++)
        {
            var placed = FurnitureRig.Rules.Furniture.Placement.Furnish(seed, rooms).Placed;
            if (placed.FirstOrDefault(p => p.Kind == FurnitureKind.Container && which(p)) is { } p)
            {
                var rig = new FurnitureRig(seed, ledger: ledger);
                return (rig, rig.Service.Rows.Single(r => r.Placed == p));
            }
        }

        throw new InvalidOperationException("no seed places such a container");
    }

    /// <summary>The ledger the service is handed, every <see cref="IItemLedger.ContainerDrop"/> it is asked kept.</summary>
    public class ContainerAsks : System.Reflection.DispatchProxy
    {
        public IItemLedger Inner { get; set; } = null!;
        public List<ContainerDrop> Asked { get; } = [];

        public static (IItemLedger Ledger, ContainerAsks Asks) Over(IItemLedger inner)
        {
            var proxy = Create<IItemLedger, ContainerAsks>();
            var asks = (ContainerAsks)(object)proxy;
            asks.Inner = inner;
            return (proxy, asks);
        }

        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IItemLedger.ContainerDrop))
                Asked.Add((ContainerDrop)args![0]!);
            return method.Invoke(Inner, args);
        }
    }

    [Fact]
    public async Task A_container_asks_the_ledger_for_each_heroes_share_under_its_number()
    {
        ContainerAsks? asks = null;
        var (rig, row) = Container(ledger: inner =>
        {
            var (proxy, a) = ContainerAsks.Over(inner);
            asks = a;
            return proxy;
        });
        var a = rig.Stand(FurnitureRig.SlotA, row, new Vector(0, 0, 0));
        rig.Stand(FurnitureRig.SlotB, row, new Vector(10, 10, 0));

        await rig.Service.UseAs(row, a);

        Assert.Equal([(FurnitureRig.A, (uint?)row.Index), (FurnitureRig.B, (uint?)row.Index)],
            asks!.Asked.Select(d => (d.Hero, d.Seq)).Order());
    }

    [Fact]
    public async Task A_container_puts_each_heroes_rules_roll_on_the_floor_at_its_foot()
    {
        var (rig, row) = Container();
        var a = rig.Stand(FurnitureRig.SlotA, row, new Vector(0, 0, 0));
        rig.Stand(FurnitureRig.SlotB, row, new Vector(10, 10, 0));
        int depth = rig.Service.Depth;
        var replay = FurnitureRig.Rules.Furniture.Containers.Open(ContainerState.From(row.Placed), rig.Service.Seed, [FurnitureRig.A, FurnitureRig.B],
            new HashSet<string>(), r => FurnitureRig.Rules.Loot.Container(r, depth));

        var outcome = await rig.Service.UseAs(row, a);

        Assert.Equal(FurnitureService.UseOutcome.Used, outcome);
        Assert.True(row.Container!.Opened);
        Assert.Contains(replay.Loot.Values, l => l.Outcome != LootOutcome.Nothing);
        Assert.NotEmpty(rig.Floor.Drops);
        foreach (var (hero, loot) in replay.Loot)
        {
            var seat = rig.Floor.Drops.SelectMany(d => d.Seats).SingleOrDefault(s => s.Hero == hero);
            bool something = loot.Outcome != LootOutcome.Nothing || loot.Australium > 0;
            Assert.Equal(something, seat is not null);
            if (seat is null)
                continue;
            Assert.Equal(loot.Australium, seat.Australium);
            Assert.Equal(loot.Outcome == LootOutcome.Item ? loot.Tier : -1, seat.Item is { } i && loot.Outcome == LootOutcome.Item ? i.Item.Rarity : -1);
            Assert.Equal(loot.Outcome == LootOutcome.Potion ? loot.Potion : null, loot.Outcome == LootOutcome.Potion ? seat.Item?.Item.BaseId : null);
        }

        Assert.All(rig.Floor.Drops, d => Assert.Equal(rig.Service.Foot(row), d.Position));
    }

    [Fact]
    public async Task A_container_rolls_for_the_heroes_in_its_room_and_not_one_beyond_it()
    {
        var (rig, row) = Container();
        var a = rig.Stand(FurnitureRig.SlotA, row);
        rig.Heroes.Add(FurnitureRig.SlotB, FurnitureRig.B, new Vector(-90000, -90000, 0));

        await rig.Service.UseAs(row, a);

        Assert.Contains(rig.Log.Lines, l => l.Text.Contains($"container #{row.Index}") && l.Text.Contains(FurnitureRig.A) && !l.Text.Contains(FurnitureRig.B));
    }

    [Fact]
    public async Task A_container_opens_once_and_rolls_for_nobody_after()
    {
        var (rig, row) = Container();
        var a = rig.Stand(FurnitureRig.SlotA, row);
        await rig.Service.UseAs(row, a);
        int seats = rig.Floor.Drops.Sum(d => d.Seats.Count);

        var again = await rig.Service.UseAs(row, a);

        Assert.Equal(FurnitureService.UseOutcome.Refused, again);
        Assert.Equal(seats, rig.Floor.Drops.Sum(d => d.Seats.Count));
        Assert.Equal(FurnitureFlags.Opened, (FurnitureFlags)row.Entity!.State.Flags);
        Assert.NotEqual(FurnitureCatalog.Of(row.Entity.State.Entry)!.Model, row.Entity.Model);
    }

    [Fact]
    public async Task A_locked_container_stays_shut_until_a_hero_walks_into_its_key_room()
    {
        var (rig, row) = Container(p => p.KeyId is not null && p.Trap is null);
        var a = rig.Stand(FurnitureRig.SlotA, row);

        var shut = await rig.Service.UseAs(row, a);
        var keyRoom = FurnitureRig.D1S1.Rooms[int.Parse(row.Placed.KeyRoom!, System.Globalization.CultureInfo.InvariantCulture)];
        rig.Heroes.Move(FurnitureRig.SlotA, keyRoom.Centre with { Z = keyRoom.Min.Z + 1 });
        int found = rig.Service.FindKeys();
        rig.Heroes.Move(FurnitureRig.SlotA, row.Position);
        var opened = await rig.Service.UseAs(row, rig.Hero(FurnitureRig.SlotA));

        Assert.Equal(FurnitureService.UseOutcome.Refused, shut);
        Assert.Equal(1, found);
        Assert.Equal(FurnitureService.UseOutcome.Used, opened);
        Assert.Empty(rig.Service.Keys);
    }

    [Fact]
    public async Task A_trapped_container_fires_its_trap_at_the_opener()
    {
        // A dart (radius 0) on a container hits whoever opened it, standing at its foot, clear of its box.
        var (rig, row) = Container(p => p.KeyId is null && p.Trap == "dart");
        var a = rig.Heroes.Add(FurnitureRig.SlotA, FurnitureRig.A, rig.Service.Foot(row));
        var fire = FurnitureRig.Rules.Furniture.Traps.Fire(row.Placed.Trap!);

        await rig.Service.UseAs(row, a);

        var hit = Assert.Single(rig.World.Hits, h => h.Victim == a.Player);
        Assert.Equal((float)fire.Damage, hit.Damage);
        Assert.Equal(FurnitureService.DamageBits(fire.DamageType), hit.Bits);
    }
}
