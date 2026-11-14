using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Nav;
using TF2.Shared;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The learn pass (lane G6b): a jump a player showed, as the experience's snapshot holds it, fitted with a recipe in the
/// simulator and made a link, over a yard built by hand (a floor 1,536 by 1,024 on 16 u columns, one block 256 by 256
/// on it, 0.3 of the jump's rise high, open to the sky) and snapshots built by hand. No game.
/// </summary>
public class ShownJumpFitTests
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

    /// <summary>The block's east edge, and the middle of it across.</summary>
    private const float East = (BlockX + BlockSize) * Voxel, Middle = (BlockY + BlockSize / 2 + 0.5f) * Voxel;

    private static float Height => Yard.Value.Height;

    /// <summary>From the ground 160 u east of the block, onto its top 40 u in from the edge: what the shape pass proposes there.</summary>
    private static Vector Takeoff(float y = Middle) => new(East + 160f, y, 0f);

    private static Vector Landing(float y = Middle) => new(East - 40f, y, Height);

    private static AiVector Ai(Vector v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// A Soldier's rocket jump as the watch writes it: three commands, the jump on the first and the shot
    /// <paramref name="shotAfterJump"/> after it (none when not <paramref name="shot"/>), looking at <paramref name="pitch"/>
    /// and back the way it came; the body at <paramref name="groundSpeed"/> over the ground at the takeoff.
    /// </summary>
    private static ShownJumpRecipe Jump(Vector takeoff, Vector landing, float pitch = 89f, int shotAfterJump = 0, float groundSpeed = 0f, bool shot = true)
    {
        float back = MathF.Atan2(landing.Y - takeoff.Y, landing.X - takeoff.X) * (180f / MathF.PI) + 180f;
        var trace = ImmutableArray.CreateBuilder<ShownJumpCommand>(3);
        for (int i = 0; i < 3; i++)
        {
            byte buttons = (byte)(ShownJumpCommand.Duck | (i == 0 ? ShownJumpCommand.Jump : 0) | (shot && i == shotAfterJump ? ShownJumpCommand.Attack : 0));
            trace.Add(new ShownJumpCommand(buttons, pitch, back, 0f, 0f));
        }

        return new ShownJumpRecipe(ShownJumpKind.Blast, ShownJumpFlags.JumpPressed | ShownJumpFlags.OwnBlast | (shot ? ShownJumpFlags.ShotSeen : 0), Ai(takeoff), Ai(landing),
                                   new AiVector(-groundSpeed, 0f, 0f), default, default, default, 90, 30, 0, 0, shot ? shotAfterJump : -1, shot ? shotAfterJump + 1 : -1,
                                   trace.MoveToImmutable());
    }

    /// <summary>A snapshot holding <paramref name="jumps"/>, each seen as many times as it says, with its exemplar, and <paramref name="rows"/> besides.</summary>
    private static MapExperience Snapshot(IEnumerable<(ShownJumpRecipe Jump, double Seen, int Class)> jumps, params (LinkKey Key, LinkStat Stat)[] rows)
    {
        var delta = new ExperienceDelta();
        foreach ((ShownJumpRecipe jump, double seen, int cls) in jumps)
        {
            delta.Add(jump.CountKey(cls), new LinkStat(seen, seen, seen * 1.2d, seen * 30d));
            delta.Add(jump.ExemplarKey(cls), default);
        }

        foreach ((LinkKey key, LinkStat stat) in rows)
            delta.Add(key, stat);
        return new MapExperience(new ExperienceKey("yard", "0123456789abcdef", "ctf"), 1, delta);
    }

    private static MapExperience Snapshot(ShownJumpRecipe jump, double seen = 1d, int cls = (int)TFClass.Soldier, params (LinkKey Key, LinkStat Stat)[] rows)
        => Snapshot([(jump, seen, cls)], rows);

    private static ShownJumpFit Fitter(ShownJumpFitSettings? settings = null)
    {
        (VoxelColumns columns, NavGraph graph, _) = Yard.Value;
        return new ShownJumpFit(graph, Search(columns, walk: GraphJumpWalk.Of(graph, RunSpeed)), () => Reach.Value, settings ?? new ShownJumpFitSettings());
    }

    /// <summary>The level's own links on the yard: the shape pass's.</summary>
    private static ExperienceLinkKeys ShapeKeys()
    {
        (VoxelColumns columns, NavGraph graph, _) = Yard.Value;
        GraphJumpWalk walk = GraphJumpWalk.Of(graph, RunSpeed);
        JumpPlacesResult found = new JumpPlaces(graph, columns, Search(columns, walk: walk), walk, Reach.Value, JumpPlacesSettings.Of(TFNavProfile.ModNumbers)).Find();
        Assert.NotEmpty(found.Links);
        return TFLinkExperience.KeysOf(JumpLinkSet.Of(graph, found.Links, new NavLinkCosts { RunSpeed = RunSpeed }));
    }

    [Fact]
    public void AJumpOntoALedgeTheLevelHasNoLinkForBecomesALinkOntoIt()
    {
        NavGraph graph = Yard.Value.Graph;
        ShownFitResult result = Fitter().Fit(Snapshot(Jump(Takeoff(), Landing())), level: null);
        ShownFit fit = Assert.Single(result.Fits);
        Assert.Equal(ShownFitVerdict.Fitted, fit.Verdict);
        JumpLink link = Assert.Single(result.Links);
        Assert.Equal(link, fit.Link);
        Assert.True(link.Shown);
        Assert.True(link.Robustness >= new JumpSearchSettings().Keep, $"robustness {link.Robustness}");
        Assert.Equal(Height, graph.Cell(link.To).FloorZ);
        Assert.Equal(0f, graph.Cell(link.From).FloorZ);
        Assert.True(result.Flights > 0 && fit.Flights == result.Flights, $"{result.Flights} flights");

        // Flown again, the recipe comes to rest on the cell the link goes to.
        JumpOutcome again = Search(Yard.Value.Columns).Fly(link.Recipe);
        Assert.Equal(link.To, graph.FindCell(new AiVector(again.Rest.X, again.Rest.Y, again.Rest.Z)).Index);
        Assert.True(Flat(again.Rest, Landing()) <= new ShownJumpFitSettings().Tolerance);
    }

    [Fact]
    public void AJumpTheLevelsOwnLinksAlreadyMakeGivesNoSecondLink()
    {
        ShownFitResult result = Fitter().Fit(Snapshot(Jump(Takeoff(), Landing())), ShapeKeys());
        Assert.Equal(ShownFitVerdict.Known, Assert.Single(result.Fits).Verdict);
        Assert.Empty(result.Links);
        Assert.Equal(0, result.Flights);
    }

    [Theory]
    [InlineData((int)TFClass.Scout, ShownJumpKind.Blast)]
    [InlineData((int)TFClass.Soldier, ShownJumpKind.Sticky)]
    [InlineData((int)TFClass.Soldier, ShownJumpKind.Crouch)]
    public void OnlyASoldiersBlastJumpIsAsked(int cls, ShownJumpKind kind)
    {
        ShownFitResult result = Fitter().Fit(Snapshot(Jump(Takeoff(), Landing()) with { Kind = kind }, cls: cls), level: null);
        Assert.Empty(result.Fits);
        Assert.Equal(0, result.Flights);
    }

    [Fact]
    public void AJumpSeenTooSeldomIsLeftOutUnlessTheBotsHaveLandedItsLink()
    {
        double under = new ShownJumpFitSettings().MinSightings * 0.8d;
        ShownFitResult few = Fitter().Fit(Snapshot(Jump(Takeoff(), Landing()), seen: under), level: null);
        Assert.Equal(ShownFitVerdict.Few, Assert.Single(few.Fits).Verdict);
        Assert.Equal((0, 0), (few.Links.Count, few.Flights));

        // The bots flew its link and landed: its Solver row keeps it.
        JumpLink link = Assert.Single(Fitter().Fit(Snapshot(Jump(Takeoff(), Landing())), level: null).Links);
        LinkKey solver = TFLinkExperience.KeyOf(link.Recipe);
        ShownFitResult kept = Fitter().Fit(Snapshot(Jump(Takeoff(), Landing()), seen: under, rows: (solver, new LinkStat(2d, 2d, 4d, 60d))), level: null);
        Assert.Equal(ShownFitVerdict.Kept, Assert.Single(kept.Fits).Verdict);
        Assert.Equal(link, Assert.Single(kept.Links));

        // Tries that failed keep nothing.
        ShownFitResult failed = Fitter().Fit(Snapshot(Jump(Takeoff(), Landing()), seen: under, rows: (solver, new LinkStat(2d, 0d, 0d, 0d))), level: null);
        Assert.Equal(ShownFitVerdict.Few, Assert.Single(failed.Fits).Verdict);
    }

    [Fact]
    public void ALandingOnNoCellIsRefusedWithNoFlight()
    {
        ShownFitResult result = Fitter().Fit(Snapshot(Jump(Takeoff(), Landing() + new Vector(0f, 0f, 200f))), level: null);
        Assert.Equal((ShownFitVerdict.NoCell, 0), (Assert.Single(result.Fits).Verdict, result.Flights));
    }

    [Fact]
    public void TheMostSeenAreSearchedFirstUpToTheCap()
    {
        // Onto the block from the east, the north and the west: the west seen least.
        float north = (BlockY + BlockSize) * Voxel, west = BlockX * Voxel, across = (BlockX + BlockSize / 2 + 0.5f) * Voxel;
        ShownJumpRecipe east = Jump(Takeoff(), Landing());
        ShownJumpRecipe fromNorth = Jump(new Vector(across, north + 160f, 0f), new Vector(across, north - 40f, Height));
        ShownJumpRecipe fromWest = Jump(new Vector(west - 160f, Middle, 0f), new Vector(west + 40f, Middle, Height));
        MapExperience snapshot = Snapshot([(fromWest, 1d, (int)TFClass.Soldier), (east, 3d, (int)TFClass.Soldier), (fromNorth, 2d, (int)TFClass.Soldier)]);
        ShownFitResult result = Fitter(new ShownJumpFitSettings { MaxAsks = 2 }).Fit(snapshot, level: null);
        Assert.Equal([3d, 2d, 1d], result.Fits.Select(f => f.Jump.Stat.Tries));
        Assert.Equal([ShownFitVerdict.Fitted, ShownFitVerdict.Fitted, ShownFitVerdict.Capped], result.Fits.Select(f => f.Verdict));
        Assert.Equal(2, result.Searched);
        Assert.Equal(0, result.Fits[2].Flights);
    }

    [Fact]
    public void TwoJumpsOfOnePlaceGiveOneLinkTheMoreSeensOwn()
    {
        // A bin apart across the edge: two count rows, one jump.
        ShownJumpRecipe a = Jump(Takeoff(), Landing()), b = Jump(Takeoff(Middle + 64f), Landing(Middle + 64f));
        Assert.NotEqual(a.CountKey((int)TFClass.Soldier), b.CountKey((int)TFClass.Soldier));
        ShownFitResult result = Fitter().Fit(Snapshot([(a, 1d, (int)TFClass.Soldier), (b, 2d, (int)TFClass.Soldier)]), level: null);
        Assert.Equal([ShownFitVerdict.Fitted, ShownFitVerdict.Twin], result.Fits.Select(f => f.Verdict));
        Assert.Equal(b.Landing.Y, Assert.Single(result.Links).Recipe.Landing.Y);
    }

    [Fact]
    public void EachJumpIsSearchedOnceInTheFittersLife()
    {
        ShownJumpFit fitter = Fitter();
        MapExperience snapshot = Snapshot(Jump(Takeoff(), Landing()));
        ShownFitResult first = fitter.Fit(snapshot, level: null);
        ShownFitResult again = fitter.Fit(Snapshot(Jump(Takeoff(), Landing()), seen: 2d), level: null);
        Assert.True(first.Flights > 0);
        Assert.Equal(0, again.Flights);
        Assert.Equal(first.Links, again.Links);
        Assert.Equal(1, fitter.Remembered);
    }

    [Fact]
    public void WithNoExemplarTheBinsMiddlesAreTheAsk()
    {
        ShownJumpRecipe jump = Jump(Takeoff(), Landing());
        var delta = new ExperienceDelta();
        delta.Add(jump.CountKey((int)TFClass.Soldier), new LinkStat(1d, 1d, 1.2d, 30d));
        ShownFitResult result = Fitter().Fit(new MapExperience(new ExperienceKey("yard", "0123456789abcdef", "ctf"), 1, delta), level: null);
        ShownFit fit = Assert.Single(result.Fits);
        Assert.Null(fit.Jump.Exemplar);
        Assert.Equal(ShownFitVerdict.Fitted, fit.Verdict);
        Assert.Equal(Height, Yard.Value.Graph.Cell(fit.Link!.Value.To).FloorZ);
        AiVector bin = jump.To.Centre;
        Assert.Equal((bin.X, bin.Y), (fit.Link.Value.Recipe.Landing.X, fit.Link.Value.Recipe.Landing.Y));
    }

    [Fact]
    public void TheExemplarsShotStandingIsAnUpHintFromStanding()
    {
        var search = new JumpSearchSettings();
        JumpHint hint = Fitter().HintOf(Jump(Takeoff(), Landing(), pitch: 80f), Takeoff(), Landing())!.Value;
        // The way is west (180 degrees); the shot looked east, back the way it came.
        Assert.Equal(new JumpHint(JumpFamily.Up, search.Up.RunUp, 80f, 180f, 0), hint);
    }

    [Fact]
    public void TheExemplarsShotRunningIsAForwardHintFromItsGridsRunUp()
    {
        var search = new JumpSearchSettings();
        float fast = new ShownJumpFitSettings().RanSpeed + 50f;
        JumpHint hint = Fitter().HintOf(Jump(Takeoff(), Landing(), pitch: 60f, groundSpeed: fast), Takeoff(), Landing())!.Value;
        Assert.Equal((JumpFamily.Forward, search.Forward.RunUp, 60f), (hint.Family, hint.RunUp, hint.Pitch));
    }

    [Fact]
    public void WithNoShotSeenThereIsNoHint() => Assert.Null(Fitter().HintOf(Jump(Takeoff(), Landing(), shot: false), Takeoff(), Landing()));

    /// <summary>A fitter over <paramref name="columns"/> (the Double and Wall searches' worlds) and their graph.</summary>
    private static (ShownJumpFit Fit, NavGraph Graph) FitterOver(VoxelColumns columns)
    {
        NavGraph graph = GraphOf(columns);
        return (new ShownJumpFit(graph, Search(columns, walk: GraphJumpWalk.Of(graph, RunSpeed)), () => Reach.Value, new ShownJumpFitSettings()), graph);
    }

    [Fact]
    public void AShownJumpUpALedgePastOneRocketWithAWallBesideIsFittedAsAWallJump()
    {
        // Lane G8: the Wall search's world (a pit, the ledge past one rocket's rise, a wall south of the way): a person's jump
        // from the floor onto the ledge, fitted with a second rocket at the wall.
        JumpAsk ask = JumpSearchWallTests.Ask();
        (ShownJumpFit fit, NavGraph graph) = FitterOver(JumpSearchWallTests.World());
        ShownFitResult result = fit.Fit(Snapshot(Jump(ask.Takeoff, ask.Landing)), level: null);
        ShownFit shown = Assert.Single(result.Fits);
        Assert.Equal(ShownFitVerdict.Fitted, shown.Verdict);
        JumpLink link = Assert.Single(result.Links);
        Assert.Equal(JumpFamily.Wall, link.Family);
        Assert.True(Assert.NotNull(link.Recipe.Second).Wall);
        Assert.Equal(TFLinkExperience.WallRecipeFormat, TFLinkExperience.KeyOf(link.Recipe).Recipe.Bytes[0]);
        Assert.InRange(graph.Cell(link.To).FloorZ, ask.Landing.Z - 18f, ask.Landing.Z);
    }

    [Fact]
    public void AShownJumpOverAFloorUnderTheWayIsFittedAsAFloorDoubleFirst()
    {
        // The Double search's world (the beam under the way, no wall): the floor double, the family tried first.
        JumpAsk ask = JumpSearchDoubleTests.Ask();
        (ShownJumpFit fit, _) = FitterOver(JumpSearchDoubleTests.World());
        JumpLink link = Assert.Single(fit.Fit(Snapshot(Jump(ask.Takeoff, ask.Landing)), level: null).Links);
        Assert.Equal(JumpFamily.Double, link.Family);
        Assert.Equal(TFLinkExperience.SecondRecipeFormat, TFLinkExperience.KeyOf(link.Recipe).Recipe.Bytes[0]);
    }
}
