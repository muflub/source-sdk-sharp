using System.Collections.Immutable;
using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>
/// A link the bots' own tries say fails is left out (lane G5e, plan_bots_teams_tf2_part_g G.5 step 7): under 30 % after
/// five tries, as a snapshot's decayed rows say, until it decays back; the mask a snapshot makes leaves it out of a
/// field, and a snapshot with no row of it (an empty store, another class's rows, another place's) leaves out nothing.
/// </summary>
public sealed class LinkTrustTests
{
    private const int Soldier = 3;
    private static readonly ExperienceKey Map = new("ss_test", "0123456789abcdef", "ctf");
    private static readonly NavLinkCosts Costs = new() { RunSpeed = 240f };

    /// <summary>Two halves of the bottom row joined the long way over the top, and link 0 across the gap (the field's facts' horseshoe).</summary>
    private static readonly string[] Horseshoe =
    [
        "...........",
        ".         .",
        "..... .....",
    ];

    private static LinkKey Key(int cls = Soldier, int fromX = 4, byte recipe = 1)
        => new(cls, new WorldBin(fromX, 2, 0), new WorldBin(6, 2, 0), 0, LinkSource.Solver, new LinkRecipe(ImmutableArray.Create(recipe, (byte)7)));

    private static (NavGraph Graph, int[,] Ids, ExperienceLinkKeys Keys) Level()
    {
        var (g, ids) = Grids.Parse(Horseshoe);
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[4, 2], ids[6, 2], NavLinkKind.Blast, 1f, 30f, 1f, 0)], Costs);
        return (g, ids, new ExperienceLinkKeys(links, [Key()]));
    }

    /// <summary>A snapshot of a store that was told <paramref name="rows"/>, then played <paramref name="roundsAfter"/> rounds more.</summary>
    private static MapExperience Snapshot(int roundsAfter = 0, params (LinkKey Key, LinkStat Stat)[] rows)
    {
        var store = new MemoryExperienceStore();
        var delta = new ExperienceDelta();
        foreach ((LinkKey key, LinkStat stat) in rows)
            delta.Add(key, stat);
        store.Merge(Map, delta, ExperienceDecay.DefaultHalfLife);
        for (int i = 0; i < roundsAfter; i++)
        {
            var round = new ExperienceDelta();
            round.Add(new RoundRow(60d, 2, 0d, 1d, ""));
            store.Merge(Map, round, ExperienceDecay.DefaultHalfLife);
        }

        return store.Load(Map, ExperienceDecay.DefaultHalfLife);
    }

    private static NavObstacleState Soldiers(NavLinks links, NavLinkMask? shut) => new(0) { Links = links, Reach = JumpReach.Blast, LinkHealth = 100f, LinksShut = shut };

    private static FlowField Field(NavGraph g, CellId goal, NavObstacleState agent)
    {
        var f = new FlowField();
        f.Build(g, goal, ClearanceClass.Stand, agent);
        return f;
    }

    [Fact]
    public void FiveTriesUnderThirtyPercentShutALink()
        => Assert.True(LinkTrust.Shut(new LinkStat(5d, 1d, 2d, 30d)));

    [Fact]
    public void UnderFiveTriesALinkIsNotJudged()
        => Assert.False(LinkTrust.Shut(new LinkStat(4.9d, 0d, 0d, 0d)));

    [Fact]
    public void ThirtyPercentOrMoreKeepsALink()
        => Assert.False(LinkTrust.Shut(new LinkStat(10d, 3d, 6d, 90d)));

    [Fact]
    public void ASnapshotWhoseRowPutsALinkUnderTheRuleShutsThatLink()
    {
        var (_, _, keys) = Level();
        NavLinkMask mask = LinkTrust.Mask(Snapshot(rows: (Key(), new LinkStat(6d, 1d, 2d, 30d))), keys);
        Assert.Equal((1, true), (mask.Count, mask.Shut(0)));
        Assert.True(mask.IsOver(keys.Links));
    }

    [Fact]
    public void AnotherClasssRowsShutNothing()
    {
        var (_, _, keys) = Level();
        Assert.Equal(0, LinkTrust.Mask(Snapshot(rows: (Key(cls: 1), new LinkStat(6d, 0d, 0d, 0d))), keys).Count);
    }

    [Fact]
    public void RowsOfAnotherPlaceOrRecipeShutNothing()
    {
        var (_, _, keys) = Level();
        MapExperience elsewhere = Snapshot(rows: [(Key(fromX: 40), new LinkStat(6d, 0d, 0d, 0d)), (Key(recipe: 2), new LinkStat(6d, 0d, 0d, 0d))]);
        Assert.Equal(0, LinkTrust.Mask(elsewhere, keys).Count);
        // The control: the same rows under the link's own key shut it.
        Assert.Equal(1, LinkTrust.Mask(Snapshot(rows: (Key(), new LinkStat(6d, 0d, 0d, 0d))), keys).Count);
    }

    [Fact]
    public void AShutLinkComesBackOnceItsTriesDecayUnderFive()
    {
        var (_, _, keys) = Level();
        (LinkKey, LinkStat) failing = (Key(), new LinkStat(6d, 0d, 0d, 0d));
        Assert.Equal(1, LinkTrust.Mask(Snapshot(roundsAfter: 5, failing), keys).Count);
        // 6 tries at a half-life of 20 rounds: under 5 after six rounds (6 x 0.5^(6/20) = 4.87).
        Assert.Equal(0, LinkTrust.Mask(Snapshot(roundsAfter: 6, failing), keys).Count);
    }

    [Fact]
    public void AShutLinkIsLeftOutOfASoldiersField()
    {
        var (g, ids, keys) = Level();
        NavLinkMask mask = LinkTrust.Mask(Snapshot(rows: (Key(), new LinkStat(6d, 1d, 2d, 30d))), keys);
        FlowField shut = Field(g, Grids.At(ids, 8, 2), Soldiers(keys.Links, mask));
        Assert.Equal(-1, shut.LinkFrom(ids[4, 2]));
        Assert.Contains(ids[0, 0], Grids.Walk(shut, ids[4, 2]));
        // The control: the same Soldier with no mask steps over it.
        Assert.Equal(0, Field(g, Grids.At(ids, 8, 2), Soldiers(keys.Links, null)).LinkFrom(ids[4, 2]));
    }

    /// <summary>An empty store's snapshot makes a mask that shuts nothing, and the field built with it is the one built with none, cell for cell.</summary>
    [Fact]
    public void WithAnEmptyStoreTheFieldIsTodaysCellForCell()
    {
        var (g, ids, keys) = Level();
        NavLinkMask mask = LinkTrust.Mask(Snapshot(), keys);
        Assert.Equal(0, mask.Count);
        FlowField with = Field(g, Grids.At(ids, 8, 2), Soldiers(keys.Links, mask));
        FlowField without = Field(g, Grids.At(ids, 8, 2), Soldiers(keys.Links, null));
        Assert.Equal(0, without.LinkFrom(ids[4, 2]));
        for (int c = 0; c < g.CellCount; c++)
            Assert.Equal((without.CostAt(c), without.NextFrom(c), without.ViaFrom(c), without.LinkFrom(c)), (with.CostAt(c), with.NextFrom(c), with.ViaFrom(c), with.LinkFrom(c)));
    }

    [Fact]
    public void AMaskOfAnotherSetShutsNothing()
    {
        var (g, ids, keys) = Level();
        NavLinks other = NavLinks.Build(g, [new NavLink(ids[4, 2], ids[6, 2], NavLinkKind.Blast, 2f, 30f, 1f, 0)], Costs);
        NavLinkMask foreign = NavLinkMask.Of(other, [0]);
        Assert.True(keys.Links.Admits(g, 0, ClearanceClass.Stand, Soldiers(keys.Links, foreign)));
        Assert.False(keys.Links.Admits(g, 0, ClearanceClass.Stand, Soldiers(keys.Links, NavLinkMask.Of(keys.Links, [0]))));
    }

    [Fact]
    public void AShovedCopyFollowsItsOriginalsMask()
    {
        var (_, _, keys) = Level();
        NavObstacleState original = Soldiers(keys.Links, null);
        NavObstacleState shoved = original.Shoved();
        NavLinkMask mask = NavLinkMask.Of(keys.Links, [0]);
        original.LinksShut = mask;
        Assert.Same(mask, shoved.LinksShut);
        Assert.Same(mask, original.Clone().LinksShut);
    }
}
