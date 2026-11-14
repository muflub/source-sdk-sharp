using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>
/// The snapshot a decider reads (plan_bots_teams_tf2 Part G, G.3 and G.4's
/// queries): a delta's rows sum, a snapshot is immutable and never null, a
/// view takes the rows of its context, and the content hash names the content.
/// </summary>
public sealed class MapExperienceTests
{
    private static readonly ExperienceKey Key = new("ctf_test", "abc123", "ctf");
    private static readonly ExperienceContext RedSoldier = new(0, 1, 3), BlueSoldier = new(0, 2, 3), RedMedic = new(0, 1, 5);
    private static readonly WorldBin Here = new(1, 2, 0), There = new(9, 2, 0);

    private static MapExperience Of(Action<ExperienceDelta> fill, long rounds = 0)
    {
        var rows = new ExperienceDelta();
        fill(rows);
        return new MapExperience(Key, rounds, rows);
    }

    [Fact]
    public void AddingTheSameKeyToADeltaSumsItsCounts()
    {
        var delta = new ExperienceDelta();
        var key = new CellKey(RedSoldier, Here, ExperienceSource.Human);
        delta.Add(key, new CellStat(1d, 0d, 1d, 10d, 0d));
        delta.Add(key, new CellStat(2d, 1d, 0d, 0d, 5d));
        Assert.Equal(new CellStat(3d, 1d, 1d, 10d, 5d), Assert.Single(delta.Cells).Value);
    }

    [Fact]
    public void AddingADeltaSumsItsRowsAndAppendsItsRounds()
    {
        var a = new ExperienceDelta();
        var b = new ExperienceDelta();
        var look = new LookKey(RedSoldier, Here, 4, 1);
        a.Add(look, new Tally(1.5d));
        a.Add(new RoundRow(100d, 1, 3d, 2d, "first"));
        b.Add(look, new Tally(0.5d));
        b.Add(new OutcomeKey(RedSoldier, 1, 42L), new OutcomeStat(1d, 0.75d));
        b.Add(new RoundRow(200d, 2, 3d, 2d, "second"));
        a.Add(b);
        Assert.Equal(2d, a.Looks[look].Count);
        Assert.Equal(new OutcomeStat(1d, 0.75d), a.Outcomes[new OutcomeKey(RedSoldier, 1, 42L)]);
        Assert.Equal(["first", "second"], a.Rounds.Select(r => r.Composition));
        Assert.Equal(4, a.Rows);
    }

    [Fact]
    public void TheEmptyExperienceHasNoRowsAndAnswersNothing()
    {
        MapExperience empty = MapExperience.Empty;
        Assert.True(empty.IsEmpty);
        Assert.False(empty.Key.IsValid);
        ExperienceView view = empty.For(ExperienceContext.All);
        Assert.Equal(0d, view.Hazard(Here));
        Assert.Equal(0d, view.Traffic(Here));
        Assert.Null(view.Look(Here));
        Assert.Empty(view.Spots(1, Here.Centre, 10_000f));
        Assert.Empty(view.Links);
        Assert.Empty(view.Threats(Here));
        Assert.Equal(default, view.Outcome(1, 1L));
    }

    [Fact]
    public void ASnapshotDoesNotChangeWhenItsRowsDoAfter()
    {
        var rows = new ExperienceDelta();
        var key = new CellKey(RedSoldier, Here, ExperienceSource.Human);
        rows.Add(key, new CellStat(5d, 0d, 0d, 0d, 0d));
        var snapshot = new MapExperience(Key, 0, rows);
        ulong hash = snapshot.ContentHash;
        rows.Add(key, new CellStat(5d, 0d, 0d, 0d, 0d));
        rows.Add(new CellKey(RedSoldier, There, ExperienceSource.Human), new CellStat(1d, 0d, 0d, 0d, 0d));
        Assert.Equal(5d, snapshot.For(ExperienceContext.All).Traffic(Here));
        Assert.Equal(1, snapshot.Rows);
        Assert.Equal(hash, new MapExperience(Key, 0, Rows(r => r.Add(key, new CellStat(5d, 0d, 0d, 0d, 0d)))).ContentHash);
    }

    private static ExperienceDelta Rows(Action<ExperienceDelta> fill)
    {
        var rows = new ExperienceDelta();
        fill(rows);
        return rows;
    }

    [Fact]
    public void TrafficIsTheSecondsPresentOfHumansAndBotsTogether()
    {
        MapExperience e = Of(r =>
        {
            r.Add(new CellKey(RedSoldier, Here, ExperienceSource.Human), new CellStat(8d, 0d, 0d, 0d, 0d));
            r.Add(new CellKey(RedSoldier, Here, ExperienceSource.Bot), new CellStat(0.5d, 0d, 0d, 0d, 0d));
        });
        Assert.Equal(8.5d, e.For(ExperienceContext.All).Traffic(Here));
        Assert.Equal(0d, e.For(ExperienceContext.All).Traffic(There));
    }

    [Fact]
    public void HazardIsDeathsPerSecondPresent()
    {
        MapExperience e = Of(r => r.Add(new CellKey(RedSoldier, Here, ExperienceSource.Human), new CellStat(40d, 2d, 0d, 0d, 0d)));
        Assert.Equal(0.05d, e.For(ExperienceContext.All).Hazard(Here), 12);
    }

    [Fact]
    public void ADeathInABinCrossedInAnInstantIsDividedByTheFloor()
    {
        MapExperience e = Of(r => r.Add(new CellKey(RedSoldier, Here, ExperienceSource.Human), new CellStat(0.1d, 1d, 0d, 0d, 0d)));
        Assert.Equal(1d / ExperienceView.HazardFloorSeconds, e.For(ExperienceContext.All).Hazard(Here), 12);
    }

    [Fact]
    public void AViewTakesOnlyTheRowsOfItsContext()
    {
        MapExperience e = Of(r =>
        {
            r.Add(new CellKey(RedSoldier, Here, ExperienceSource.Human), new CellStat(10d, 1d, 0d, 0d, 0d));
            r.Add(new CellKey(BlueSoldier, Here, ExperienceSource.Human), new CellStat(30d, 0d, 0d, 0d, 0d));
            r.Add(new CellKey(RedMedic, Here, ExperienceSource.Human), new CellStat(5d, 0d, 0d, 0d, 0d));
        });
        Assert.Equal(45d, e.For(ExperienceContext.All).Traffic(Here));
        Assert.Equal(15d, e.For(new ExperienceContext(ExperienceContext.Any, 1, ExperienceContext.Any)).Traffic(Here));
        Assert.Equal(40d, e.For(new ExperienceContext(ExperienceContext.Any, ExperienceContext.Any, 3)).Traffic(Here));
        Assert.Equal(10d, e.For(RedSoldier).Traffic(Here));
        Assert.Equal(0d, e.For(BlueSoldier).Hazard(Here));
    }

    [Fact]
    public void TheLookFromABinIsTheYawWithTheMostSeconds()
    {
        MapExperience e = Of(r =>
        {
            r.Add(new LookKey(RedSoldier, Here, 4, 1), new Tally(3d));
            r.Add(new LookKey(RedSoldier, Here, 4, 0), new Tally(2d));
            r.Add(new LookKey(RedSoldier, Here, 12, 1), new Tally(4d));
            r.Add(new LookKey(RedSoldier, Here, 0, 1), new Tally(1d));
        });
        ExperienceLook look = Assert.NotNull(e.For(RedSoldier).Look(Here));
        Assert.Equal(90f, look.Yaw);
        Assert.Equal(0f, look.Pitch);
        Assert.Equal(10d, look.Seconds);
        Assert.Equal(0.5d, look.Share, 12);
        Assert.Null(e.For(RedSoldier).Look(There));
    }

    [Fact]
    public void SpotsOfAKindNearAPointComeMostUsedFirst()
    {
        const int Sentry = 1, Hold = 2;
        var far = new WorldBin(40, 2, 0);
        MapExperience e = Of(r =>
        {
            r.Add(new SpotKey(RedSoldier, Sentry, Here, 4), new SpotStat(2d, 60d, 1d, 0d));
            r.Add(new SpotKey(RedSoldier, Sentry, There, 8), new SpotStat(5d, 90d, 3d, 1d));
            r.Add(new SpotKey(RedSoldier, Sentry, far, 0), new SpotStat(50d, 900d, 30d, 1d));
            r.Add(new SpotKey(RedSoldier, Hold, Here, 0), new SpotStat(9d, 10d, 0d, 0d));
        });
        IReadOnlyList<ExperienceSpot> spots = e.For(RedSoldier).Spots(Sentry, Here.Centre, 600f);
        Assert.Equal([There, Here], spots.Select(s => s.Bin));
        Assert.Equal(180f, spots[0].Yaw);
        Assert.Equal(There.Centre, spots[0].At);
        Assert.Equal(new SpotStat(5d, 90d, 3d, 1d), spots[0].Stat);
    }

    [Fact]
    public void AViewsLinksAreItsClasss()
    {
        var jump = new LinkRecipe([1, 2, 3]);
        MapExperience e = Of(r =>
        {
            r.Add(new LinkKey(3, Here, There, 1, LinkSource.Solver, jump), new LinkStat(4d, 3d, 6d, 120d));
            r.Add(new LinkKey(4, There, Here, 1, LinkSource.Human, LinkRecipe.None), new LinkStat(1d, 1d, 2d, 0d));
        });
        ExperienceLink link = Assert.Single(e.For(RedSoldier).Links);
        Assert.Equal((Here, There, LinkSource.Solver), (link.Key.From, link.Key.To, link.Key.Source));
        Assert.Equal(new byte[] { 1, 2, 3 }, link.Key.Recipe.Bytes.ToArray());
        Assert.Equal(2d, link.Stat.MeanSeconds);
        Assert.Equal(40d, link.Stat.MeanHealthCost);
        Assert.Single(e.For(RedSoldier).LinksFrom(Here));
        Assert.Empty(e.For(RedSoldier).LinksFrom(There));
        Assert.Equal(2, e.For(ExperienceContext.All).Links.Count);
    }

    [Fact]
    public void TwoRecipesOfTheSameBytesAreOneLinkRow()
    {
        var delta = new ExperienceDelta();
        delta.Add(new LinkKey(3, Here, There, 1, LinkSource.Practice, new LinkRecipe([7, 8])), new LinkStat(1d, 0d, 0d, 0d));
        delta.Add(new LinkKey(3, Here, There, 1, LinkSource.Practice, new LinkRecipe([7, 8])), new LinkStat(1d, 1d, 3d, 40d));
        delta.Add(new LinkKey(3, Here, There, 1, LinkSource.Practice, new LinkRecipe([7, 9])), new LinkStat(1d, 0d, 0d, 0d));
        Assert.Equal(2, delta.Links.Count);
        Assert.Equal(new LinkStat(2d, 1d, 3d, 40d), delta.Links[new LinkKey(3, Here, There, 1, LinkSource.Practice, new LinkRecipe([7, 8]))]);
    }

    [Fact]
    public void ThreatsAreWhereDeathCameFromTheMostKillsFirst()
    {
        var roof = new WorldBin(5, 5, 3);
        MapExperience e = Of(r =>
        {
            r.Add(new SightKey(RedSoldier, Here, There, 2), new Tally(1d));
            r.Add(new SightKey(RedSoldier, Here, roof, 6), new Tally(4d));
            r.Add(new SightKey(RedSoldier, There, roof, 6), new Tally(9d));
        });
        ExperienceView view = e.For(RedSoldier);
        Assert.Equal([new ExperienceSightLine(Here, roof, 6, 4d), new ExperienceSightLine(Here, There, 2, 1d)], view.Threats(Here));
        Assert.Equal([There, Here], view.Targets(roof).Select(t => t.Victim));
    }

    [Fact]
    public void AnOutcomeAnswersItsTriesAndScore()
    {
        MapExperience e = Of(r =>
        {
            r.Add(new OutcomeKey(RedSoldier, 1, 77L), new OutcomeStat(5d, 1d));
            r.Add(new OutcomeKey(RedSoldier, 1, 77L), new OutcomeStat(1d, 1d));
            r.Add(new OutcomeKey(RedSoldier, 2, 77L), new OutcomeStat(9d, 9d));
        });
        Assert.Equal(new OutcomeStat(6d, 2d), e.For(RedSoldier).Outcome(1, 77L));
        Assert.Equal(default, e.For(RedSoldier).Outcome(1, 78L));
    }

    private static void Fill(ExperienceDelta r, bool reversed)
    {
        Action[] adds =
        [
            () => r.Add(new CellKey(RedSoldier, Here, ExperienceSource.Human), new CellStat(8d, 1d, 2d, 30d, 60d)),
            () => r.Add(new CellKey(BlueSoldier, There, ExperienceSource.Bot), new CellStat(0.25d, 0d, 0d, 0d, 0d)),
            () => r.Add(new SightKey(RedSoldier, Here, There, 2), new Tally(1d)),
            () => r.Add(new LookKey(RedSoldier, Here, 4, 1), new Tally(3d)),
            () => r.Add(new SpotKey(RedSoldier, 1, Here, 4), new SpotStat(2d, 60d, 1d, 0d)),
            () => r.Add(new LinkKey(3, Here, There, 1, LinkSource.Solver, new LinkRecipe([1, 2, 3])), new LinkStat(4d, 3d, 6d, 120d)),
            () => r.Add(new OutcomeKey(RedSoldier, 1, 77L), new OutcomeStat(5d, 1d)),
        ];
        foreach (Action add in reversed ? adds.Reverse() : adds)
            add();
        r.Add(new RoundRow(300d, 1, 4d, 8d, "1.3*2"));
    }

    [Fact]
    public void TheContentHashIsTheSameWhateverOrderTheRowsWereAddedIn()
    {
        Assert.Equal(Of(r => Fill(r, reversed: false), rounds: 3).ContentHash, Of(r => Fill(r, reversed: true), rounds: 3).ContentHash);
    }

    /// <summary>A literal, so a hash that differs between processes (a string's own, a dictionary's order) fails here.</summary>
    [Fact]
    public void TheContentHashIsTheSameInEveryProcess()
    {
        Assert.Equal(1891496136413167457UL, Of(r => Fill(r, reversed: false), rounds: 3).ContentHash);
    }

    [Fact]
    public void TheContentHashChangesWithACountAKeyOrTheRoundCount()
    {
        ulong whole = Of(r => Fill(r, reversed: false), rounds: 3).ContentHash;
        Assert.NotEqual(whole, Of(r => Fill(r, reversed: false), rounds: 4).ContentHash);
        Assert.NotEqual(whole, Of(r =>
        {
            Fill(r, reversed: false);
            r.Add(new LookKey(RedSoldier, Here, 4, 1), new Tally(0.001d));
        }, rounds: 3).ContentHash);
        Assert.NotEqual(whole, new MapExperience(Key with { Checksum = "abc124" }, 3, Rows(r => Fill(r, reversed: false))).ContentHash);
        Assert.NotEqual(Of(r => r.Add(new CellKey(RedSoldier, Here, ExperienceSource.Human), new CellStat(1d, 0d, 0d, 0d, 0d))).ContentHash,
                        Of(r => r.Add(new CellKey(RedSoldier, Here, ExperienceSource.Bot), new CellStat(1d, 0d, 0d, 0d, 0d))).ContentHash);
    }

    [Fact]
    public void AWorldMadeWithoutAStoreHoldsTheEmptyExperience()
    {
        MemoryWorld world = MemoryWorld.Empty();
        Assert.NotNull(world.Experience);
        Assert.True(world.Experience.IsEmpty);
    }

    [Fact]
    public void AWorldReadsWhatItsSlotHoldsNow()
    {
        var slot = new ExperienceSlot();
        MemoryWorld world = MemoryWorld.Empty() with { ExperienceSlot = slot };
        MapExperience fresh = Of(r => r.Add(new CellKey(RedSoldier, Here, ExperienceSource.Human), new CellStat(1d, 0d, 0d, 0d, 0d)));
        slot.Current = fresh;
        Assert.Same(fresh, world.Experience);
    }

    [Fact]
    public void ASlotRefusesNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ExperienceSlot().Current = null!);
    }
}
