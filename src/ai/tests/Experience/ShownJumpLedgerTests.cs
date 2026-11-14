using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>
/// The rows a learned jump adds (lane G6a, <see cref="ShownJumpLedger"/>): a try and a success of its count row with its
/// air time and health cost, and its exemplar (counting nothing) only while the store has none for the jump; and the
/// snapshot's reader (<see cref="ExperienceView.ShownJumps"/>), which pairs each count row with its exemplar decoded.
/// </summary>
public sealed class ShownJumpLedgerTests
{
    private const int Soldier = 3, Scout = 1;
    private static readonly ExperienceKey Key = new("ss_ai_tf_jump", "c0ffee", "ctf");
    private readonly ShownJumpLedger _ledger = new();
    private readonly List<(LinkKey Key, LinkStat Stat)> _rows = new();

    private static ShownJumpRecipe Jump(float takeoffX = 10f, int air = 40, int health = 30)
        => new(ShownJumpKind.Blast, ShownJumpFlags.OwnBlast, new AiVector(takeoffX, 10f, 0f), new AiVector(10f, 300f, 250f), default, default, default, default,
               air, health, 1, 0, -1, 1, [new(ShownJumpCommand.Jump, 0f, 90f, 0f, 0f), new(0, 0f, 90f, 0f, 0f)]);

    [Fact]
    public void ALearnedJumpIsATryAndASuccessOfItsCountRowWithItsTimeAndCost()
    {
        _ledger.Rows(Jump(air: 40, health: 30), Soldier, 0.015d, _rows);
        (LinkKey key, LinkStat stat) = _rows[0];
        Assert.Equal(Jump().CountKey(Soldier), key);
        Assert.Equal(1d, stat.Tries);
        Assert.Equal(1d, stat.Successes);
        Assert.Equal(0.6d, stat.Seconds, 9);
        Assert.Equal(30d, stat.HealthCost);
    }

    [Fact]
    public void TheFirstSightingIsAlsoTheExemplarCountingNothing()
    {
        _ledger.Rows(Jump(), Soldier, 0.015d, _rows);
        Assert.Equal(2, _rows.Count);
        Assert.Equal((Jump().ExemplarKey(Soldier), default(LinkStat)), _rows[1]);
    }

    [Fact]
    public void ALaterSightingAddsOnlyToTheCountRow()
    {
        _ledger.Rows(Jump(takeoffX: 10f), Soldier, 0.015d, _rows);
        _rows.Clear();
        _ledger.Rows(Jump(takeoffX: 30f), Soldier, 0.015d, _rows);
        Assert.Equal(Jump().CountKey(Soldier), Assert.Single(_rows).Key);
    }

    [Fact]
    public void AnotherClasssSightingHasItsOwnExemplar()
    {
        _ledger.Rows(Jump(), Soldier, 0.015d, _rows);
        _rows.Clear();
        _ledger.Rows(Jump(), Scout, 0.015d, _rows);
        Assert.Equal(2, _rows.Count);
    }

    [Fact]
    public void AnExemplarInTheSnapshotIsNotWrittenAgain()
    {
        var rows = new ExperienceDelta();
        rows.Add(Jump(takeoffX: 30f).ExemplarKey(Soldier), default);
        _ledger.Seed(new MapExperience(Key, 0, rows));
        _ledger.Rows(Jump(), Soldier, 0.015d, _rows);
        Assert.Single(_rows);
    }

    [Fact]
    public void AfterClearAnExemplarIsWrittenAgain()
    {
        _ledger.Rows(Jump(), Soldier, 0.015d, _rows);
        _ledger.Clear();
        _rows.Clear();
        _ledger.Rows(Jump(), Soldier, 0.015d, _rows);
        Assert.Equal(2, _rows.Count);
    }

    /// <summary>A snapshot of <paramref name="sightings"/> of the jump by a soldier, with its exemplar.</summary>
    private static MapExperience Snapshot(int sightings, bool exemplar = true)
    {
        var ledger = new ShownJumpLedger();
        var delta = new ExperienceDelta();
        var rows = new List<(LinkKey Key, LinkStat Stat)>();
        for (int i = 0; i < sightings; i++)
            ledger.Rows(Jump(takeoffX: 10f + i), Soldier, 0.015d, rows);
        foreach ((LinkKey k, LinkStat s) in rows)
        {
            if (exemplar || ShownJumpLedger.IsCount(k))
                delta.Add(k, s);
        }

        delta.Add(new LinkKey(Soldier, new WorldBin(0, 0, 0), new WorldBin(0, 4, 3), 0, LinkSource.Solver, new LinkRecipe([1, 9, 9])), new LinkStat(4d, 3d, 6d, 90d));
        return new MapExperience(Key, 0, delta);
    }

    [Fact]
    public void TheViewGivesEachJumpWithItsCountsAndItsExemplarDecoded()
    {
        ExperienceShownJump jump = Assert.Single(Snapshot(3).For(new ExperienceContext(ExperienceContext.Any, ExperienceContext.Any, Soldier)).ShownJumps());
        Assert.Equal(ShownJumpKind.Blast, jump.Kind);
        Assert.Equal(new LinkStat(3d, 3d, 1.8d, 90d), jump.Stat with { Seconds = Math.Round(jump.Stat.Seconds, 9) });
        Assert.Equal(Jump(), jump.Exemplar);
    }

    [Fact]
    public void TheViewGivesAJumpWithNoExemplarWithNone()
        => Assert.Null(Assert.Single(Snapshot(1, exemplar: false).For(ExperienceContext.All).ShownJumps()).Exemplar);

    [Fact]
    public void AnotherClasssViewHasNoneOfTheSoldiersJumps()
        => Assert.Empty(Snapshot(2).For(new ExperienceContext(ExperienceContext.Any, ExperienceContext.Any, Scout)).ShownJumps());
}
