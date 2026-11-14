using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Ai.Replay;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Nav;
using TF2.Shared;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// A practice row in the game (lane G8): a recipe the practice tool wrote to the store, read back, flown in the level's
/// simulator before it is trusted, and joined to the level's links by the learn pass; and the tool's own choice of work
/// and of what it writes. Over a yard built by hand (ShownJumpFitTests' yard: a floor 1,536 by 1,024 on 16 u columns, one
/// block 256 by 256 on it, 0.3 of the jump's rise high). No game.
/// </summary>
public class JumpPracticeTests
{
    private const int Wide = 96, Deep = 64, BlockX = 44, BlockY = 24, BlockSize = 16;
    private const float Voxel = 16f;

    private static readonly Lazy<JumpEnvelope> Reach = new(() => Envelope());

    private static readonly Lazy<(VoxelColumns Columns, NavGraph Graph, float Height)> Yard = new(() =>
    {
        float height = Voxel * MathF.Round(0.3f * Reach.Value.Rise / Voxel);
        VoxelColumns columns = VoxelColumns.Of(0f, 0f, Voxel, Wide, Deep,
            (x, y) => [new FreeSpan(x >= BlockX && x < BlockX + BlockSize && y >= BlockY && y < BlockY + BlockSize ? height : 0f, 8192f)]);
        return (columns, GraphOf(columns), height);
    });

    /// <summary>The level's own links on the yard: the shape pass's, with the game's settings.</summary>
    private static readonly Lazy<JumpLinkSet> Shape = new(() =>
    {
        (VoxelColumns columns, NavGraph graph, _) = Yard.Value;
        GraphJumpWalk walk = GraphJumpWalk.Of(graph, RunSpeed);
        JumpPlacesResult found = new JumpPlaces(graph, columns, Search(columns, walk: walk), walk, Reach.Value, JumpPlacesSettings.Of(TFNavProfile.ModNumbers)).Find();
        Assert.NotEmpty(found.Links);
        return JumpLinkSet.Of(graph, found.Links, new NavLinkCosts { RunSpeed = RunSpeed });
    });

    private const float East = (BlockX + BlockSize) * Voxel, Middle = (BlockY + BlockSize / 2 + 0.5f) * Voxel;

    /// <summary>A recipe onto the block from 160 u east of it, as the game's search finds it.</summary>
    private static readonly Lazy<JumpRecipe> Onto = new(() =>
    {
        var ask = new JumpAsk(new Vector(East + 160f, Middle, VoxelMoveWorld.DistEpsilon), new Vector(East - 40f, Middle, Yard.Value.Height + VoxelMoveWorld.DistEpsilon), 24f);
        return Assert.NotNull(Search(Yard.Value.Columns).Find(ask).Found).Recipe;
    });

    private static MapExperience Snapshot(params (LinkKey Key, LinkStat Stat)[] rows)
    {
        var delta = new ExperienceDelta();
        foreach ((LinkKey key, LinkStat stat) in rows)
            delta.Add(key, stat);
        return new MapExperience(new ExperienceKey("yard", "0123456789abcdef", "ctf"), 1, delta);
    }

    private static (LinkKey, LinkStat) Practice(JumpRecipe recipe, double successes = 36d) => (TFLinkExperience.PracticeKeyOf(recipe), new LinkStat(40d, successes, 2d * successes, 30d * successes));

    private static JumpPractice Reader(JumpPracticeSettings? settings = null)
    {
        (VoxelColumns columns, NavGraph graph, _) = Yard.Value;
        GraphJumpWalk walk = GraphJumpWalk.Of(graph, RunSpeed);
        JumpPracticeSettings practice = settings ?? new JumpPracticeSettings();
        return new JumpPractice(graph, Search(columns, practice.Verifying(new JumpSearchSettings()), walk), walk, practice);
    }

    [Fact]
    public void APracticeRecipeThatMakesItsLandingIsFlownFirstAndJoinsAsALinkMarkedPracticed()
    {
        NavGraph graph = Yard.Value.Graph;
        PracticeResult result = Reader().Check(Snapshot(Practice(Onto.Value)), level: null);
        PracticeCheck check = Assert.Single(result.Checks);
        Assert.Equal(PracticeVerdict.Taken, check.Verdict);
        JumpLink link = Assert.Single(result.Links);
        Assert.True(link.Practiced);
        Assert.False(link.Shown);
        Assert.Equal(Onto.Value, link.Recipe);
        Assert.Equal(Yard.Value.Height, graph.Cell(link.To).FloorZ);
        Assert.Equal(0f, graph.Cell(link.From).FloorZ);
        Assert.True(link.Robustness >= new JumpSearchSettings().Keep, $"robustness {link.Robustness}");

        // Verified, not taken blind: the recipe flown once, then its trials (the practice's count, the game's draws).
        Assert.Equal(1 + new JumpPracticeSettings().Trials, result.Flights);
    }

    [Fact]
    public void ARecipeThatNoLongerMakesItsLandingIsSpoiledAndGivesNoLink()
    {
        // The recipe's shot at the sky: the body never leaves the floor, whatever its row says of its trials.
        PracticeResult result = Reader().Check(Snapshot(Practice(Onto.Value with { Pitch = -60f })), level: null);
        Assert.Equal(PracticeVerdict.Spoiled, Assert.Single(result.Checks).Verdict);
        Assert.Empty(result.Links);
        Assert.True(result.Flights > 0);
    }

    [Fact]
    public void ARecipeThatLandsTooSeldomIsSpoiled()
    {
        // Lands unperturbed, but under the game's Keep of its trials: a reader that asks more than its trials than anyone.
        PracticeResult loose = Reader().Check(Snapshot(Practice(Onto.Value)), level: null);
        float robustness = Assert.Single(loose.Links).Robustness;
        JumpSearchSettings strict = new JumpPracticeSettings().Verifying(new JumpSearchSettings { Keep = robustness + 0.01f });
        (VoxelColumns columns, NavGraph graph, _) = Yard.Value;
        GraphJumpWalk walk = GraphJumpWalk.Of(graph, RunSpeed);
        PracticeResult result = new JumpPractice(graph, Search(columns, strict, walk), walk, new JumpPracticeSettings()).Check(Snapshot(Practice(Onto.Value)), level: null);
        Assert.Equal(PracticeVerdict.Spoiled, Assert.Single(result.Checks).Verdict);
    }

    [Fact]
    public void BytesNoRecipeIsReadFromAreUnreadWithNoFlight()
    {
        LinkKey row = TFLinkExperience.PracticeKeyOf(Onto.Value) with { Recipe = new LinkRecipe([ShownJumpRecipe.CountFormat]) };
        PracticeResult result = Reader().Check(Snapshot((row, new LinkStat(1d, 1d, 1d, 1d))), level: null);
        Assert.Equal(PracticeVerdict.Unread, Assert.Single(result.Checks).Verdict);
        Assert.Equal(0, result.Flights);
    }

    [Fact]
    public void OnlyASoldiersBlastPracticeRowsAreRead()
    {
        (LinkKey key, LinkStat stat) = Practice(Onto.Value);
        PracticeResult result = Reader().Check(Snapshot((key with { Class = (int)TFClass.Scout }, stat), (key with { Source = LinkSource.Solver }, stat),
                                                        (key with { Kind = (int)NavLinkKind.Blast + 1 }, stat)), level: null);
        Assert.Empty(result.Checks);
        Assert.Same(PracticeResult.None, result);
    }

    [Fact]
    public void WhereTheLevelsOwnLinkIsNoCostlierThePracticeLinkIsNotBetter()
    {
        // The shape link onto the block as the practice row: the same jump, the same cost.
        JumpLink own = Shape.Value.Jumps.First(j => Yard.Value.Graph.Cell(j.To).FloorZ == Yard.Value.Height);
        PracticeResult result = Reader().Check(Snapshot(Practice(own.Recipe)), Shape.Value);
        Assert.Equal(PracticeVerdict.NotBetter, Assert.Single(result.Checks).Verdict);
        Assert.Empty(result.Links);
    }

    [Fact]
    public void WhereTheBotsGaveUpTheLevelsOwnLinkThePracticeLinkIsTaken()
    {
        JumpLink own = Shape.Value.Jumps.First(j => Yard.Value.Graph.Cell(j.To).FloorZ == Yard.Value.Height);
        PracticeResult result = Reader().Check(Snapshot(Practice(own.Recipe), (TFLinkExperience.KeyOf(own.Recipe), new LinkStat(10d, 1d, 2d, 30d))), Shape.Value);
        Assert.Equal(PracticeVerdict.Taken, Assert.Single(result.Checks).Verdict);
        Assert.Single(result.Links);
    }

    [Fact]
    public void EachRowIsFlownOnceInTheReadersLife()
    {
        JumpPractice reader = Reader();
        MapExperience snapshot = Snapshot(Practice(Onto.Value));
        PracticeResult first = reader.Check(snapshot, level: null), again = reader.Check(snapshot, level: null);
        Assert.True(first.Flights > 0);
        Assert.Equal(0, again.Flights);
        Assert.Equal(first.Links, again.Links);

        // A new row (another recipe of the store) is flown; the one checked is not again.
        PracticeResult grown = reader.Check(Snapshot(Practice(Onto.Value), Practice(Onto.Value with { ShotDelay = Onto.Value.ShotDelay + 40 })), level: null);
        Assert.Equal(2, grown.Checks.Count);
        Assert.InRange(grown.Flights, 1, first.Flights);
    }

    [Fact]
    public void TwoRowsOfOnePlaceGiveOneLinkTheMoreSuccessfulsOwn()
    {
        JumpRecipe other = Onto.Value with { YawOffset = Onto.Value.YawOffset + 0.5f };
        PracticeResult result = Reader().Check(Snapshot(Practice(Onto.Value, 20d), Practice(other, 30d)), level: null);
        Assert.Equal(other, Assert.Single(result.Links).Recipe);
        Assert.Contains(result.Checks, c => c.Verdict == PracticeVerdict.Twin);
    }

    [Fact]
    public void PastTheCapRowsAreNotFlown()
    {
        PracticeResult result = Reader(new JumpPracticeSettings { MaxRows = 0 }).Check(Snapshot(Practice(Onto.Value)), level: null);
        Assert.Equal(PracticeVerdict.Capped, Assert.Single(result.Checks).Verdict);
        Assert.Equal(0, result.Flights);
    }

    /// <summary>The learn pass over the yard, the shape links its level's.</summary>
    private static JumpLearning Learning()
    {
        (VoxelColumns columns, NavGraph graph, _) = Yard.Value;
        return new JumpLearning(graph, Sim(columns), Sim(JumpEnvelope.OpenAir()), Limits(), new JumpSearchSettings(), new ShownJumpFitSettings(),
                                new NavLinkCosts { RunSpeed = RunSpeed }, null);
    }

    [Fact]
    public void TheLearnPassJoinsATakenPracticeLinkAfterTheShapeLinks()
    {
        JumpLinkSet shape = Shape.Value;
        JumpLink own = shape.Jumps.First(j => Yard.Value.Graph.Cell(j.To).FloorZ == Yard.Value.Height);
        JumpLearnOutcome outcome = Learning().Learn(Snapshot(Practice(own.Recipe), (TFLinkExperience.KeyOf(own.Recipe), new LinkStat(10d, 1d, 2d, 30d))), shape, [], default);
        Assert.NotNull(outcome.Set);
        JumpLinkSet set = outcome.Set;
        Assert.Equal(shape.Links.Count + 1, set.Links.Count);
        Assert.Equal(shape.Jumps, set.Jumps.Take(shape.Jumps.Count));
        Assert.True(set.Jumps[^1].Practiced);
        Assert.Equal(1, outcome.Practiced);
        Assert.Equal(set.Links.Count, outcome.Plans.Count);
    }

    [Fact]
    public void WithNoPracticeRowsTheLearnPassIsTheOneOfBefore()
    {
        JumpLearnOutcome outcome = Learning().Learn(Snapshot((new LinkKey(3, default, default, 1, LinkSource.Solver, LinkRecipe.None), new LinkStat(1d, 1d, 1d, 1d))), Shape.Value, [], default);
        Assert.Null(outcome.Set);
        Assert.Same(PracticeResult.None, outcome.Practice);
        Assert.Equal(0, outcome.Flights);
    }

    [Fact]
    public void AShownJumpAPracticeLinkMakesIsPracticedAndNotSearched()
    {
        // A person's jump onto the block, and the practice row of that place: the practice recipe answers it.
        (VoxelColumns columns, NavGraph graph, float height) = Yard.Value;
        Vector takeoff = new(East + 160f, Middle, 0f), landing = new(East - 40f, Middle, height);
        var trace = ImmutableArray.Create(new ShownJumpCommand((byte)(ShownJumpCommand.Duck | ShownJumpCommand.Jump | ShownJumpCommand.Attack), 89f, 0f, 0f, 0f));
        var shown = new ShownJumpRecipe(ShownJumpKind.Blast, ShownJumpFlags.JumpPressed | ShownJumpFlags.OwnBlast | ShownJumpFlags.ShotSeen, new AiVector(takeoff.X, takeoff.Y, takeoff.Z),
                                        new AiVector(landing.X, landing.Y, landing.Z), default, default, default, default, 90, 30, 0, 0, 0, 1, trace);
        int soldier = (int)TFClass.Soldier;
        MapExperience snapshot = Snapshot((shown.CountKey(soldier), new LinkStat(2d, 2d, 2d, 60d)), (shown.ExemplarKey(soldier), default), Practice(Onto.Value));
        JumpLearnOutcome outcome = Learning().Learn(snapshot, JumpLinkSet.Of(graph, [], new NavLinkCosts { RunSpeed = RunSpeed }), [], default);
        Assert.Equal(ShownFitVerdict.Practiced, Assert.Single(outcome.Fits.Fits).Verdict);
        Assert.Equal(0, outcome.Fits.Flights);
        Assert.Equal(1, outcome.Practiced);
        Assert.Empty(outcome.Fits.Links);
        Assert.NotNull(outcome.Set);
        Assert.True(Assert.Single(outcome.Set.Jumps).Practiced);
    }

    // The tool (src/ai/replay/JumpPracticeTool.cs).

    private static JumpPracticeTool.Level ToolLevel() => new(Yard.Value.Graph, Yard.Value.Columns, Sim, Limits(), [], null);

    [Fact]
    public void TheToolLeavesStrongTrustedLinksAloneAndWritesNothingForThem()
    {
        IReadOnlyList<PracticeItem> items = JumpPracticeTool.Practise(ToolLevel(), Snapshot(), JumpPracticeTool.Rich, below: 0f, out ExperienceDelta rows, out int links, out _);
        Assert.True(links > 0);
        Assert.DoesNotContain(items, i => i.Why is PracticeWhy.LinkWeak or PracticeWhy.LinkShut);
        Assert.Empty(rows.Links);
    }

    [Fact]
    public void TheToolSearchesEveryWeakLinkAgainAndWritesOnlyWhatTheGamesReaderTakes()
    {
        IReadOnlyList<PracticeItem> items = JumpPracticeTool.Practise(ToolLevel(), Snapshot(), JumpPracticeTool.Rich, below: 1.01f, out ExperienceDelta rows, out int links, out _);
        Assert.Equal(links, items.Count(i => i.Why == PracticeWhy.LinkWeak));
        Assert.All(items.Where(i => i.Why == PracticeWhy.LinkWeak), i => Assert.True(i.Flights > 0));
        Assert.Equal(items.Count(i => i.Written), rows.Links.Count);
        Assert.All(items.Where(i => i.Written), i => Assert.Equal(PracticeVerdict.Taken, i.Verdict));
        foreach ((LinkKey key, LinkStat stat) in rows.Links)
        {
            Assert.Equal(LinkSource.Practice, key.Source);
            Assert.True(TFLinkExperience.TryRecipe(key.Recipe, out _));
            Assert.Equal(JumpPracticeTool.Rich.Trials, stat.Tries);
            Assert.InRange(stat.Successes, JumpPracticeTool.Rich.Keep * stat.Tries, stat.Tries);
        }
    }

    [Fact]
    public void ARowIsVerifiedWithThePracticesTrialsAndTheGamesDraws()
    {
        // The game's settings but the trials: the first 20 draws of the seed are the shape pass's own.
        JumpSearchSettings game = new(), verifying = new JumpPracticeSettings().Verifying(game);
        Assert.Equal(game with { Trials = JumpPracticeTool.Rich.Trials }, verifying);
        Assert.Equal(JumpPracticeTool.Rich.Trials, new JumpPracticeSettings().Trials);
    }

    [Fact]
    public void TheToolsRichSearchIsFinerThanTheGamesInEveryNamedNumber()
    {
        JumpSearchSettings game = new(), rich = JumpPracticeTool.Rich;
        Assert.True(rich.Up.Pitches * rich.Up.Delays > game.Up.Pitches * game.Up.Delays && rich.Up.Pitches * rich.Up.Delays <= JumpSearch.MaxGrid);
        Assert.True(rich.Forward.Pitches * rich.Forward.Delays > game.Forward.Pitches * game.Forward.Delays && rich.Forward.Pitches * rich.Forward.Delays <= JumpSearch.MaxGrid);
        Assert.True(rich.Trials > game.Trials && rich.Measured > game.Measured && rich.MaxFlights > game.MaxFlights);
        Assert.Equal(game.Keep, rich.Keep);
    }
}
