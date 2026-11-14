using TF2.Descent.Rules;
using TF2.Descent.Rules.Furniture;
using Xunit;

namespace TF2.Descent.Tests.Furniture;

/// <summary>
/// Containers, the Dispenser, explosives and traps (plan_descent_logic RU11; plan_tf2_rpg
/// 2l): loot rolled once per eligible player and never twice, a charge that only falls,
/// barrel damage and chain radius, each trap's type and tell.
/// </summary>
public sealed class FixtureTests
{
    private static readonly TF2DescentRules Rules = new();
    private static FurnitureRules F => Rules.Furniture;

    private static readonly HashSet<string> NoKeys = [];

    private static string Loot(LootRequest r) => $"{r.LootTable}:{r.Player}:{r.Seed}";

    //-------------------------------------------------------------------------
    // Containers
    //-------------------------------------------------------------------------

    [Fact]
    public void A_container_rolls_once_for_each_player_in_the_room()
    {
        var calls = new List<LootRequest>();
        var open = F.Containers.Open(new ContainerState("chest", "locker", null, null), 7, ["ann", "bob", "cat"], NoKeys,
            r => { calls.Add(r); return Loot(r); });

        Assert.Equal(["ann", "bob", "cat"], calls.Select(c => c.Player).Order());
        Assert.Equal(["ann", "bob", "cat"], open.Loot.Keys);
        Assert.All(calls, c => Assert.Equal("container", c.LootTable));
        Assert.Equal(3, calls.Select(c => c.Seed).Distinct().Count());
    }

    [Fact]
    public void A_player_listed_twice_rolls_once()
    {
        int calls = 0;
        var open = F.Containers.Open(new ContainerState("chest", "locker", null, null), 7, ["ann", "ann", "bob"], NoKeys,
            r => { calls++; return Loot(r); });

        Assert.Equal(2, calls);
        Assert.Equal(2, open.Loot.Count);
    }

    [Fact]
    public void An_opened_container_rolls_for_nobody_again()
    {
        var first = F.Containers.Open(new ContainerState("chest", "locker", null, null), 7, ["ann"], NoKeys, Loot);
        int calls = 0;
        var again = F.Containers.Open(first.Container, 7, ["ann", "bob"], NoKeys, r => { calls++; return Loot(r); });

        Assert.True(first.Container.Opened);
        Assert.Equal(ContainerRefusal.AlreadyOpen, again.Refused);
        Assert.Equal(0, calls);
        Assert.Empty(again.Loot);
    }

    [Fact]
    public void A_containers_roll_is_seed_stable()
    {
        var a = F.Containers.Open(new ContainerState("chest", "locker", null, null), 7, ["ann"], NoKeys, Loot);
        var b = F.Containers.Open(new ContainerState("chest", "locker", null, null), 7, ["ann"], NoKeys, Loot);
        var other = F.Containers.Open(new ContainerState("chest", "locker", null, null), 8, ["ann"], NoKeys, Loot);

        Assert.Equal(a.Loot["ann"], b.Loot["ann"]);
        Assert.NotEqual(a.Loot["ann"], other.Loot["ann"]);
    }

    [Fact]
    public void A_locked_container_stays_shut_without_its_key()
    {
        var shut = F.Containers.Open(new ContainerState("chest", "locker", "key:chest", null), 7, ["ann"], new HashSet<string> { "key:other" }, Loot);

        Assert.Equal(ContainerRefusal.Locked, shut.Refused);
        Assert.False(shut.Container.Opened);
        Assert.Empty(shut.Loot);
    }

    [Fact]
    public void A_locked_container_opens_with_its_key_and_takes_it()
    {
        var open = F.Containers.Open(new ContainerState("chest", "locker", "key:chest", null), 7, ["ann"], new HashSet<string> { "key:chest" }, Loot);

        Assert.True(open.Ok);
        Assert.Equal("key:chest", open.KeyUsed);
    }

    [Fact]
    public void A_trapped_container_sets_its_trap_off_when_opened()
    {
        var open = F.Containers.Open(new ContainerState("chest", "locker", null, "flame_jet"), 7, ["ann"], NoKeys, Loot);
        var plain = F.Containers.Open(new ContainerState("chest", "locker", null, null), 7, ["ann"], NoKeys, Loot);

        Assert.Equal("flame_jet", open.TrapFired);
        Assert.Null(plain.TrapFired);
    }

    //-------------------------------------------------------------------------
    // The Dispenser
    //-------------------------------------------------------------------------

    [Fact]
    public void A_dispenser_heals_20_a_second_from_its_charge_of_600()
    {
        var d = F.Dispensers.New();
        var draw = F.Dispensers.Draw(d, 1, missingHealth: 100, missingAmmo: 0);

        Assert.Equal(600, d.Charge);
        Assert.Equal(20, draw.Health);
        Assert.Equal(580, draw.After.Charge);
    }

    [Fact]
    public void A_dispenser_pays_for_health_before_ammo()
    {
        var draw = F.Dispensers.Draw(new DispenserState(30), 2, missingHealth: 100, missingAmmo: 100);

        Assert.Equal(30, draw.Health);
        Assert.Equal(0, draw.Ammo);
        Assert.Equal(0, draw.After.Charge);
    }

    [Fact]
    public void A_dispensers_charge_only_falls_over_10k_seeded_draws()
    {
        var roll = new Seeded(2026);
        var d = F.Dispensers.New();
        double given = 0;
        for (int i = 0; i < 10_000; i++)
        {
            var draw = F.Dispensers.Draw(d, roll.Range(0, 3), roll.Range(0, 200), roll.Range(0, 200));
            Assert.True(draw.After.Charge <= d.Charge, $"draw {i}: {d.Charge} -> {draw.After.Charge}");
            Assert.True(draw.After.Charge >= 0);
            given += draw.Health + draw.Ammo;
            d = draw.After;
        }
        Assert.Equal(0, d.Charge);
        Assert.True(given > 0);
    }

    [Fact]
    public void An_empty_dispenser_gives_nothing()
    {
        var draw = F.Dispensers.Draw(new DispenserState(0), 5, 100, 100);

        Assert.Equal((0.0, 0.0), (draw.Health, draw.Ammo));
    }

    //-------------------------------------------------------------------------
    // Explosives
    //-------------------------------------------------------------------------

    [Theory]
    [InlineData("barrel", 0, 110)]
    [InlineData("barrel", 73, 82.5)]
    [InlineData("barrel", 146, 55)]
    [InlineData("barrel", 146.5, 0)]
    [InlineData("pumpkin_bomb", 0, 150)]
    [InlineData("pumpkin_bomb", 160, 75)]
    public void Explosive_damage_falls_from_full_at_the_centre_to_half_at_the_radius(string kind, double distance, double damage) =>
        Assert.Equal(damage, F.Explosives.DamageAt(kind, distance), 9);

    [Fact]
    public void A_barrel_hurts_heroes_and_robots_alike_with_blast()
    {
        var hits = F.Explosives.Detonate("b1", [new ExplosiveAt("b1", "barrel", new Point3(0, 0, 0))],
            [new BlastTarget("hero", true, new Point3(50, 0, 0)), new BlastTarget("robot", false, new Point3(0, 50, 0))]).Hits;

        Assert.Equal(2, hits.Length);
        Assert.Equal(hits[0].Damage, hits[1].Damage, 9);
        Assert.All(hits, h => Assert.Equal("blast", h.DamageType));
    }

    [Fact]
    public void A_barrel_sets_off_another_within_its_chain_radius_and_not_one_beyond()
    {
        var d = F.Explosives.Detonate("b1",
        [
            new ExplosiveAt("b1", "barrel", new Point3(0, 0, 0)),
            new ExplosiveAt("near", "barrel", new Point3(180, 0, 0)),
            new ExplosiveAt("far", "barrel", new Point3(0, -181, 0)),
        ], []);

        Assert.Equal(["b1", "near"], d.Exploded.ToArray());
    }

    [Fact]
    public void A_chain_carries_on_from_each_barrel_it_sets_off()
    {
        var d = F.Explosives.Detonate("b1",
        [
            new ExplosiveAt("b1", "barrel", new Point3(0, 0, 0)),
            new ExplosiveAt("b2", "barrel", new Point3(170, 0, 0)),
            new ExplosiveAt("b3", "barrel", new Point3(340, 0, 0)),
        ], [new BlastTarget("robot", false, new Point3(340, 50, 0))]);

        Assert.Equal(["b1", "b2", "b3"], d.Exploded.ToArray());
        Assert.Equal(["b3"], d.Hits.Select(h => h.Explosive).ToArray());   // only b3 reaches the robot
    }

    //-------------------------------------------------------------------------
    // Traps
    //-------------------------------------------------------------------------

    [Theory]
    [InlineData("dart", "bullet", "sound", "click")]
    [InlineData("flame_jet", "fire", "visual", "scorch_marks")]
    [InlineData("charge", "blast", "sound", "beeping")]
    [InlineData("tesla_coil", "shock", "visual", "sparks")]
    public void Each_trap_has_its_damage_type_and_its_tell(string trap, string type, string tellKind, string tell)
    {
        var fire = F.Traps.Fire(trap);

        Assert.Equal(type, fire.DamageType);
        Assert.Equal(new TrapTell(tellKind, tell), fire.Tell);
        Assert.True(fire.Damage > 0);
    }

    [Fact]
    public void Every_trap_names_a_shared_damage_type()
    {
        Assert.All(Rules.Content.Furniture.Traps, t => Assert.True(Rules.Content.DamageTypes.Contains(t.DamageType)));
        Assert.Equal(4, Rules.Content.Furniture.Traps.Select(t => t.DamageType).Distinct().Count());
    }
}
