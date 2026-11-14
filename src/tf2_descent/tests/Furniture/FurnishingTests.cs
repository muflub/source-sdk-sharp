using System.Text;
using System.Text.Json.Nodes;
using TF2.Descent.Rules;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Furniture;
using Xunit;

namespace TF2.Descent.Tests.Furniture;

/// <summary>
/// The furniture table and placing it (plan_descent_logic RU11; plan_tf2_rpg 2l): seed
/// stable, within each room's marker limits, different on another level seed, and the
/// bad-shrine share under its cap. Distribution facts state their seed counts.
/// </summary>
public sealed class FurnishingTests
{
    private static readonly TF2DescentRules Rules = new();
    private static FurnishingRules Placement => Rules.Furniture.Placement;
    private static FurnitureSettings Table => Rules.Content.Furniture;

    private static readonly string[] Pois =
        ["descent_mark_shrine", "descent_mark_container", "descent_mark_dispenser", "descent_mark_barrel", "descent_mark_trap"];

    /// <summary>A room with <paramref name="each"/> markers of every furniture kind and two pack spawns.</summary>
    private static RoomMarkers Room(string id, int each) =>
        new(id, [.. Pois.SelectMany(p => Enumerable.Range(0, each).Select(i => new FurnitureMarker($"{id}.{p}.{i}", p))),
            new FurnitureMarker(id + ".pack.0", "descent_spawn_pack"), new FurnitureMarker(id + ".pack.1", "descent_spawn_pack")]);

    private static readonly RoomMarkers[] Level = [Room("hall", 6), Room("crypt", 6), Room("stair", 2)];

    private static string Signature(Furnishing f, string room) =>
        string.Join(";", f.In(room).Select(p => $"{p.Marker}={p.Variant}/{p.KeyId}/{p.Trap}"));

    [Fact]
    public void The_marker_kinds_are_the_five_furniture_markers_the_rooms_declare()
    {
        Assert.Equal(Pois, Table.Markers.Select(m => m.Poi));
    }

    [Fact]
    public void Furnishing_is_seed_stable()
    {
        var a = Placement.Furnish(1234, Level);
        var b = Placement.Furnish(1234, Level);

        Assert.NotEmpty(a.Placed);   // the stimulus: something was placed
        Assert.Equal(a.Placed.ToArray(), b.Placed.ToArray());
    }

    [Fact]
    public void No_room_is_furnished_past_its_per_room_limit_over_2000_seeds()
    {
        int placed = 0;
        for (ulong seed = 0; seed < 2000; seed++)
        {
            var f = Placement.Furnish(seed, Level);
            placed += f.Placed.Length;
            foreach (var room in Level)
                foreach (var rule in Table.Markers)
                    Assert.True(f.In(room.Room).Count(p => p.Kind == rule.Kind) <= rule.PerRoom,
                        $"seed {seed}: {room.Room} holds more than {rule.PerRoom} {rule.Kind}");
        }
        Assert.True(placed > 2000);
    }

    [Fact]
    public void Each_kind_reaches_its_limit_in_a_room_with_enough_markers()
    {
        // The limit is the bound, not a ceiling never met: over 2000 seeds each kind fills to it.
        foreach (var rule in Table.Markers)
            Assert.Contains(Enumerable.Range(0, 2000), s =>
                Placement.Furnish((ulong)s, Level).In("hall").Count(p => p.Kind == rule.Kind) == rule.PerRoom);
    }

    [Fact]
    public void A_marker_is_furnished_at_its_kinds_fill_chance_over_20k_seeds()
    {
        foreach (var rule in Table.Markers)
        {
            RoomMarkers[] one = [new("r", [new FurnitureMarker("m", rule.Poi)])];
            int filled = Enumerable.Range(0, 20_000).Count(s => Placement.Furnish((ulong)s, one).Placed.Length == 1);

            Assert.InRange(100.0 * filled / 20_000, rule.FillPct - 1.5, rule.FillPct + 1.5);
        }
    }

    [Fact]
    public void Only_furniture_markers_are_furnished()
    {
        var f = Placement.Furnish(77, Level);

        Assert.DoesNotContain(f.Placed, p => p.Marker.Contains(".pack.", StringComparison.Ordinal));
        Assert.All(f.Placed, p => Assert.Contains(Pois, poi => p.Marker.Contains(poi, StringComparison.Ordinal)));
    }

    [Fact]
    public void The_same_room_is_furnished_differently_on_another_level_seed()
    {
        // Over 500 seeds the hall's furnishing is all but always new.
        var seen = Enumerable.Range(0, 500).Select(s => Signature(Placement.Furnish((ulong)s, Level), "hall")).ToHashSet();

        Assert.True(seen.Count >= 495, $"{seen.Count} distinct furnishings of 500");
    }

    [Fact]
    public void A_locked_containers_key_drops_in_another_room()
    {
        var locked = Enumerable.Range(0, 300).SelectMany(s => Placement.Furnish((ulong)s, Level).Placed)
            .Where(p => p.KeyId is not null).ToList();

        Assert.NotEmpty(locked);
        Assert.All(locked, p =>
        {
            Assert.Equal(FurnitureKind.Container, p.Kind);
            Assert.NotNull(p.KeyRoom);
            Assert.NotEqual(p.Room, p.KeyRoom);
        });
    }

    [Fact]
    public void A_level_of_one_room_locks_nothing()
    {
        var locked = Enumerable.Range(0, 300).SelectMany(s => Placement.Furnish((ulong)s, [Room("only", 6)]).Placed)
            .Count(p => p.KeyId is not null);

        Assert.Equal(0, locked);
    }

    [Fact]
    public void Bad_shrines_stay_under_their_cap_over_100k_seeded_shrines()
    {
        RoomMarkers[] one = [new("r", [new FurnitureMarker("altar", "descent_mark_shrine")])];
        int shrines = 0, bad = 0;
        for (ulong seed = 0; seed < 100_000; seed++)
            foreach (var p in Placement.Furnish(seed, one).Placed)
            {
                shrines++;
                if (Table.Shrine(p.Variant).Nature == ShrineNature.Bad) bad++;
            }

        Assert.True(shrines > 50_000);
        Assert.True(bad > 0);   // bad shrines exist: the cap is a bound, not a ban
        Assert.True(100.0 * bad / shrines <= Table.BadShrineCapPct, $"{bad} bad of {shrines}");
    }

    [Fact]
    public void Bad_shrine_weight_over_the_cap_is_a_load_error()
    {
        var errors = Errors(n =>
        {
            foreach (var e in n["shrines"]!["effects"]!.AsArray())
                if ((string?)e!["nature"] == "bad") e["weight"] = 100;
        });

        var e = Assert.Single(errors);
        Assert.Equal(ContentRule.OutOfRange, e.Rule);
        Assert.Contains("bad shrines", e.Message);
    }

    [Fact]
    public void A_trap_of_an_unknown_damage_type_is_a_load_error()
    {
        var errors = Errors(n => n["traps"]![0]!["damageType"] = "poison");

        var e = Assert.Single(errors);
        Assert.Equal((ContentTables.Furniture, ContentRule.DanglingReference), (e.Table, e.Rule));
    }

    [Fact]
    public void A_trade_shrine_that_takes_nothing_is_a_load_error()
    {
        var errors = Errors(n =>
        {
            foreach (var e in n["shrines"]!["effects"]!.AsArray())
                if ((string?)e!["id"] == "bargain") e["stats"]![1]!["amount"] = 5;
        });

        Assert.Equal(ContentRule.OutOfRange, Assert.Single(errors).Rule);
    }

    private static IReadOnlyList<ContentError> Errors(Action<JsonNode> mutate)
    {
        var source = new MemoryContentSource(new EmbeddedContentSource(typeof(TF2DescentRules).Assembly), ContentTables.Names());
        Assert.True(source.TryRead(ContentTables.Furniture, out var bytes));
        var node = JsonNode.Parse(bytes)!;
        mutate(node);
        return new ContentLoader().Load(source.Set(ContentTables.Furniture, Encoding.UTF8.GetBytes(node.ToJsonString()))).Errors;
    }
}
