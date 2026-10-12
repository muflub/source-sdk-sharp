using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The order a map's standalone entities spawn in — stock's classname priority
/// list from <c>SortSpawnListByHierarchy</c>, ported by plan_entity.md's
/// Phase 4 exit.
///
/// <para>
/// <b>The rule had gone quiet rather than been removed.</b> Stock builds the
/// list and breaks <c>CompareSpawnOrder</c>'s depth ties with it, over the
/// CBaseEntity spawn list. Eleven of its thirteen classnames are managed
/// entities now, and a managed entity is never in that list — it is queued in
/// <c>g_PendingNonBaseSpawns</c> and spawned in MAP-FILE ORDER after everything
/// else. So the tie-break still ran and had nothing left to apply itself to,
/// which is 5e's shape in the map-load path.
/// </para>
/// </summary>
public class MapSpawnOrderTests
{
    private static MapSpawnOrder Order() => new();

    private static string[] Sequence(params string?[] classNames)
    {
        var order = new int[classNames.Length];
        int n = Order().Order(classNames, order);

        Assert.Equal(classNames.Length, n);
        return order.Select(i => classNames[i] ?? "").ToArray();
    }

    //-------------------------------------------------------------------------
    // The table. The values are stock's and only their ORDER matters.
    //-------------------------------------------------------------------------

    [Fact]
    public void FuncWallSpawnsFirstOfAll()
        => Assert.Equal(10, Order().PriorityOf("func_wall"));

    [Fact]
    public void AConstraintOutranksAProp()
    {
        // The pair that matters on the shipped maps: dm_lockdown has 100
        // prop_physics and two constraint-family entities.
        Assert.True(Order().PriorityOf("phys_lengthconstraint") > Order().PriorityOf("prop_physics"),
            "stock puts the constraint family at 8 and the props at 7");
    }

    [Fact]
    public void EveryClassnameStockListsIsInTheTable()
    {
        // The whole of stock's list, so a row deleted by accident is a failure
        // here rather than a reordering nobody sees.
        var expected = new Dictionary<string, int>
        {
            ["func_wall"] = 10,
            ["scripted_sequence"] = 9,
            ["phys_hinge"] = 8,
            ["phys_ballsocket"] = 8,
            ["phys_slideconstraint"] = 8,
            ["phys_constraint"] = 8,
            ["phys_pulleyconstraint"] = 8,
            ["phys_lengthconstraint"] = 8,
            ["phys_ragdollconstraint"] = 8,
            ["info_mass_center"] = 8,
            ["trigger_vphysics_motion"] = 8,
            ["prop_physics"] = 7,
            ["prop_ragdoll"] = 7,
        };

        var order = Order();
        foreach (var (name, priority) in expected)
            Assert.True(order.PriorityOf(name) == priority,
                $"{name} should be {priority}, is {order.PriorityOf(name)}");
    }

    [Fact]
    public void AClassnameNobodyListedSortsLast()
    {
        // Stock states it in its own comment: "by default, anything not in this
        // list has priority -1".
        //
        // THE LITERAL, NOT THE CONSTANT. Asserting MapSpawnOrder.Unlisted
        // against PriorityOf's answer compares the constant with itself: this
        // fact passed with Unlisted mutated to 100, which is the recorded
        // "check that cannot fail". The number is stock's and is written out.
        Assert.Equal(-1, Order().PriorityOf("info_player_deathmatch"));
        Assert.Equal(-1, Order().PriorityOf(null));

        // And what the number is FOR: an unlisted entity sorts behind every
        // listed one, whatever the number happens to be.
        Assert.True(Order().PriorityOf("info_player_deathmatch") < Order().PriorityOf("prop_physics"),
            "the lowest priority stock names is prop_physics/prop_ragdoll at 7");
    }

    [Fact]
    public void ClassnameMatchingIsCaseInsensitive()
    {
        // CStringRegistry::GetStringID lowercases both sides, unlike the global
        // state table's CUtlSymbol comparison. The difference is stock's.
        Assert.Equal(7, Order().PriorityOf("PROP_PHYSICS"));
    }

    //-------------------------------------------------------------------------
    // The permutation.
    //-------------------------------------------------------------------------

    [Fact]
    public void HigherPrioritySpawnsFirst()
    {
        // The direction is the one thing about these numbers that matters, and
        // it is the opposite of what CompareSpawnOrder's return values read like
        // at a glance. Stock says it in the list's own comment: "Highest string
        // ID spawns first."
        Assert.Equal(new[] { "func_wall", "phys_hinge", "prop_physics", "info_target" },
                     Sequence("prop_physics", "info_target", "phys_hinge", "func_wall"));
    }

    [Fact]
    public void EqualPrioritiesKeepMapFileOrder()
    {
        // CompareSpawnOrder answers 0 for an equal pair and qsort then leaves
        // them where they were, which for a map load means map-file order. An
        // unstable sort would shuffle the ~400 unlisted entities of a real map
        // against each other on every load -- a far bigger change than the one
        // being made.
        Assert.Equal(new[] { "a", "b", "c", "d" }, Sequence("a", "b", "c", "d"));
    }

    [Fact]
    public void UnlistedEntitiesKeepTheirOrderAmongThemselves()
        => Assert.Equal(new[] { "prop_physics", "first", "second", "third" },
                        Sequence("first", "second", "prop_physics", "third"));

    [Fact]
    public void EveryIndexComesBackExactlyOnce()
    {
        // A PERMUTATION AND NOT A FILTER. An index dropped here is a map entity
        // that never spawns, which the native caller also checks for -- and it
        // is the failure this lane is guarded against, so it is asserted on both
        // sides rather than once.
        var names = new string?[] { "prop_physics", "func_wall", "x", null, "phys_hinge", "y" };
        var order = new int[names.Length];

        Assert.Equal(names.Length, Order().Order(names, order));
        Assert.Equal(Enumerable.Range(0, names.Length), order.OrderBy(i => i));
    }

    [Fact]
    public void AnEmptyQueueOrdersNothing()
        => Assert.Equal(0, Order().Order(Array.Empty<string?>(), Array.Empty<int>()));

    [Fact]
    public void ABufferTooSmallAsksForTheSizeAndWritesNothing()
    {
        // The size-then-fill contract this ABI already uses for
        // AnimListActivityNames and PlayerCommands.
        var names = new string?[] { "a", "b", "c" };
        var tooSmall = new int[2];

        Assert.Equal(3, Order().Order(names, tooSmall));
        Assert.Equal(new[] { 0, 0 }, tooSmall);
    }

    //-------------------------------------------------------------------------
    // The shipped maps, which is where the loss was measured.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheOrderResolvesEveryInversionInAShippedMapsClassnameSequence()
    {
        // dm_lockdown's real shape, reduced to what the rule can see: 100
        // prop_physics interleaved with two constraint-family entities at
        // indices 360 and 407 of 605. Counted from the BSP's entity lump, the
        // map-file order has 189 pairs where an entity stock spawns LATER comes
        // first; the ordered sequence must have none.
        var names = new string?[605];
        for (int i = 0; i < names.Length; i++)
            names[i] = "info_target";

        // 100 props spread across the file, and the two constraints where the
        // map actually puts them.
        for (int i = 0; i < 100; i++)
            names[11 + (i * 5)] = "prop_physics";
        names[360] = "phys_hinge";
        names[407] = "phys_lengthconstraint";

        var order = new int[names.Length];
        Assert.Equal(names.Length, Order().Order(names, order));

        var table = Order();
        int inversions = 0;
        for (int a = 0; a < order.Length; a++)
        {
            for (int b = a + 1; b < order.Length; b++)
            {
                if (table.PriorityOf(names[order[a]]) < table.PriorityOf(names[order[b]]))
                    inversions++;
            }
        }

        Assert.Equal(0, inversions);
    }
}
