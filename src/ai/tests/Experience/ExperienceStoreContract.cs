using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>
/// What every <see cref="IExperienceStore"/> does (plan_bots_teams_tf2 Part G,
/// G.3): a merge is a sum, rows are apart by map, checksum and mode, and a row
/// fades by half per half-life of rounds played on its key, at a read and
/// before a merge adds to it. The memory store and the SQLite store each run
/// these facts.
/// </summary>
public abstract class ExperienceStoreContract
{
    protected static readonly ExperienceKey Key = new("ctf_test", "abc123", "ctf");
    protected static readonly ExperienceContext Red = new(0, 1, 3);
    protected static readonly WorldBin Here = new(1, 2, 0), There = new(-9, 2, 3);
    protected static readonly CellKey HereCell = new(Red, Here, ExperienceSource.Human);
    protected const double HalfLife = 20d;

    /// <summary>A new, empty store.</summary>
    protected abstract IExperienceStore Open();

    protected static ExperienceDelta Delta(Action<ExperienceDelta> fill)
    {
        var delta = new ExperienceDelta();
        fill(delta);
        return delta;
    }

    /// <summary>A delta of nothing but <paramref name="rounds"/> rounds played.</summary>
    protected static ExperienceDelta Rounds(int rounds)
        => Delta(d =>
        {
            for (int i = 0; i < rounds; i++)
                d.Add(new RoundRow(60d, 1, 2d, 2d, ""));
        });

    protected static void FillEveryTable(ExperienceDelta d)
    {
        d.Add(HereCell, new CellStat(8d, 1d, 2d, 30d, 60d));
        d.Add(new CellKey(Red, There, ExperienceSource.Bot), new CellStat(0.25d, 0d, 0d, 0d, 0d));
        d.Add(new SightKey(Red, Here, There, 2), new Tally(1d));
        d.Add(new LookKey(Red, Here, 4, 1), new Tally(3d));
        d.Add(new SpotKey(Red, 1, There, 15), new SpotStat(2d, 60d, 1d, 0.5d));
        d.Add(new LinkKey(3, Here, There, 1, LinkSource.Solver, new LinkRecipe([1, 2, 255, 0])), new LinkStat(4d, 3d, 6d, 120d));
        d.Add(new LinkKey(3, Here, There, 1, LinkSource.Human, LinkRecipe.None), new LinkStat(1d, 1d, 2d, 0d));
        d.Add(new OutcomeKey(Red, 1, long.MinValue + 5), new OutcomeStat(5d, 1.5d));
        d.Add(new RoundRow(300.5d, 2, 4d, 8d, "1.3*2 2.5*1"));
    }

    [Fact]
    public void AKeyNeverWrittenLoadsEmpty()
    {
        using IExperienceStore store = Open();
        MapExperience e = store.Load(Key, HalfLife);
        Assert.True(e.IsEmpty);
        Assert.Equal(Key, e.Key);
        Assert.Equal(0, e.Rounds);
        Assert.Equal(default, store.Counts(Key));
        Assert.Null(store.Refusal);
    }

    [Fact]
    public void AMergedDeltaLoadsBackRowForRow()
    {
        using IExperienceStore store = Open();
        Assert.True(store.Merge(Key, Delta(FillEveryTable), HalfLife));
        MapExperience e = store.Load(Key, HalfLife);
        Assert.Equal(new MapExperience(Key, 1, Delta(FillEveryTable)).ContentHash, e.ContentHash);
        Assert.Equal(new CellStat(8d, 1d, 2d, 30d, 60d), e.Cells[HereCell]);
        Assert.Equal(new LinkStat(4d, 3d, 6d, 120d), e.Links[new LinkKey(3, Here, There, 1, LinkSource.Solver, new LinkRecipe([1, 2, 255, 0]))]);
        Assert.Equal(new RoundRow(300.5d, 2, 4d, 8d, "1.3*2 2.5*1"), Assert.Single(e.RoundRows));
        Assert.Equal(new ExperienceCounts(1, 2, 1, 1, 1, 2, 1, 1), store.Counts(Key));
    }

    [Fact]
    public void TwoMergesAreASum()
    {
        using IExperienceStore store = Open();
        store.Merge(Key, Delta(d => d.Add(HereCell, new CellStat(8d, 1d, 0d, 0d, 0d))), HalfLife);
        store.Merge(Key, Delta(d => d.Add(HereCell, new CellStat(2d, 0d, 1d, 5d, 0d))), HalfLife);
        Assert.Equal(new CellStat(10d, 1d, 1d, 5d, 0d), Assert.Single(store.Load(Key, HalfLife).Cells).Value);
    }

    [Fact]
    public void AKeysRoundsAreCountedFromTheDeltasRounds()
    {
        using IExperienceStore store = Open();
        store.Merge(Key, Rounds(2), HalfLife);
        store.Merge(Key, Delta(d => d.Add(HereCell, new CellStat(1d, 0d, 0d, 0d, 0d))), HalfLife);
        store.Merge(Key, Rounds(3), HalfLife);
        Assert.Equal(5, store.Load(Key, HalfLife).Rounds);
        Assert.Equal(5, store.Counts(Key).Rounds);
    }

    [Fact]
    public void AReadHalvesARowAHalfLifeOfRoundsOld()
    {
        using IExperienceStore store = Open();
        store.Merge(Key, Delta(d => d.Add(HereCell, new CellStat(8d, 4d, 0d, 0d, 0d))), HalfLife);
        store.Merge(Key, Rounds(20), HalfLife);
        CellStat cell = store.Load(Key, HalfLife).Cells[HereCell];
        Assert.Equal(4d, cell.Seconds, 9);
        Assert.Equal(2d, cell.Deaths, 9);
    }

    [Fact]
    public void AReadDecaysNothingWithNoRoundsPlayedSince()
    {
        using IExperienceStore store = Open();
        store.Merge(Key, Rounds(20), HalfLife);
        store.Merge(Key, Delta(d => d.Add(HereCell, new CellStat(8d, 0d, 0d, 0d, 0d))), HalfLife);
        Assert.Equal(8d, store.Load(Key, HalfLife).Cells[HereCell].Seconds);
    }

    [Fact]
    public void AMergeFadesTheOldRowBeforeAddingToIt()
    {
        using IExperienceStore store = Open();
        store.Merge(Key, Delta(d => d.Add(HereCell, new CellStat(8d, 0d, 0d, 0d, 0d))), HalfLife);
        store.Merge(Key, Rounds(20), HalfLife);
        store.Merge(Key, Delta(d => d.Add(HereCell, new CellStat(1d, 0d, 0d, 0d, 0d))), HalfLife);
        Assert.Equal(5d, store.Load(Key, HalfLife).Cells[HereCell].Seconds, 9);
        // And the sum is stamped at the merge: twenty more rounds halve all of it.
        store.Merge(Key, Rounds(20), HalfLife);
        Assert.Equal(2.5d, store.Load(Key, HalfLife).Cells[HereCell].Seconds, 9);
    }

    [Fact]
    public void EveryTableFadesAlike()
    {
        using IExperienceStore store = Open();
        store.Merge(Key, Delta(FillEveryTable), HalfLife);
        store.Merge(Key, Rounds(20), HalfLife);
        MapExperience e = store.Load(Key, HalfLife);
        Assert.Equal(0.5d, e.Sights[new SightKey(Red, Here, There, 2)].Count, 9);
        Assert.Equal(1.5d, e.Looks[new LookKey(Red, Here, 4, 1)].Count, 9);
        Assert.Equal(1d, e.Spots[new SpotKey(Red, 1, There, 15)].Uses, 9);
        Assert.Equal(2d, e.Links[new LinkKey(3, Here, There, 1, LinkSource.Solver, new LinkRecipe([1, 2, 255, 0]))].Tries, 9);
        Assert.Equal(2.5d, e.Outcomes[new OutcomeKey(Red, 1, long.MinValue + 5)].Tries, 9);
        Assert.Equal(0.5d, e.RoundRows[0].Weight, 9);
        Assert.Equal(1d, e.RoundRows[^1].Weight, 9);
    }

    [Fact]
    public void RowsOfAnotherMapChecksumOrModeAreApart()
    {
        using IExperienceStore store = Open();
        store.Merge(Key, Delta(d => d.Add(HereCell, new CellStat(8d, 0d, 0d, 0d, 0d))), HalfLife);
        store.Merge(Key with { Mode = "koth" }, Rounds(20), HalfLife);
        Assert.True(store.Load(Key with { Checksum = "edited" }, HalfLife).IsEmpty);
        Assert.True(store.Load(Key with { Map = "ctf_other" }, HalfLife).IsEmpty);
        Assert.Empty(store.Load(Key with { Mode = "koth" }, HalfLife).Cells);
        // Another mode's rounds do not age this one's rows.
        Assert.Equal(8d, store.Load(Key, HalfLife).Cells[HereCell].Seconds);
    }

    [Fact]
    public void ARowUnwrittenForTenHalfLivesIsDroppedByTheNextMerge()
    {
        using IExperienceStore store = Open();
        store.Merge(Key, Delta(d =>
        {
            d.Add(HereCell, new CellStat(8d, 0d, 0d, 0d, 0d));
            d.Add(new LookKey(Red, Here, 4, 1), new Tally(3d));
        }), 1d);
        store.Merge(Key, Rounds(10), 1d);
        Assert.Equal(1, store.Counts(Key).Cells);
        store.Merge(Key, Rounds(1), 1d);
        ExperienceCounts counts = store.Counts(Key);
        Assert.Equal((0L, 0L), (counts.Cells, counts.Looks));
        Assert.Equal(11, counts.Rounds);
        Assert.Equal(11, counts.RoundRows);
        Assert.Empty(store.Load(Key, 1d).Cells);
    }

    [Fact]
    public void AHalfLifeOfZeroKeepsEveryRowWhole()
    {
        using IExperienceStore store = Open();
        store.Merge(Key, Delta(d => d.Add(HereCell, new CellStat(8d, 0d, 0d, 0d, 0d))), 0d);
        store.Merge(Key, Rounds(500), 0d);
        Assert.Equal(8d, store.Load(Key, 0d).Cells[HereCell].Seconds);
    }
}

/// <summary>The contract over the memory store.</summary>
public sealed class MemoryExperienceStoreTests : ExperienceStoreContract
{
    protected override IExperienceStore Open() => new MemoryExperienceStore();
}
