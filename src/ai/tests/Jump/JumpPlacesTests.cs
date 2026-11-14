using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Nav;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The pass that finds the places worth a blast jump (plan_bots_teams_tf2_part_g G.5 step 4), on
/// yards built by hand: a floor 1,536 by 1,024 on 16-unit columns with blocks standing on it, and
/// a graph over it (<see cref="JumpRig.GraphOf"/>). Every height and gap is a share of the reach
/// the simulator gives today (<see cref="JumpEnvelope"/>), so the facts follow the blast's physics;
/// the facts on the game's own map are JumpPlacesMapTests.
/// </summary>
public class JumpPlacesTests
{
    private const int Wide = 96, Deep = 64;
    private const float Voxel = 16f, Sky = 8192f;

    /// <summary>The block the yards stand a ledge on: 256 by 256, from x = 704 and y = 384.</summary>
    private const int BlockX = 44, BlockY = 24, BlockSize = 16;

    private static readonly Lazy<JumpEnvelope> Reach = new(() => Envelope());

    /// <summary>A height that is <paramref name="share"/> of the jump's rise today, on the 16-unit grid.</summary>
    private static float Height(float share) => Voxel * MathF.Round(share * Reach.Value.Rise / Voxel);

    private static bool InBlock(int x, int y, int fromX = BlockX, int size = BlockSize) => x >= fromX && x < fromX + size && y >= BlockY && y < BlockY + BlockSize;

    /// <summary>A yard whose column (x, y) has its floor at what <paramref name="floor"/> answers, open to the sky; NaN is a column solid throughout.</summary>
    private static VoxelColumns Yard(Func<int, int, float> floor, Func<int, int, float>? ceiling = null)
        => VoxelColumns.Of(0f, 0f, Voxel, Wide, Deep, (x, y) => float.IsNaN(floor(x, y)) ? [] : [new FreeSpan(floor(x, y), ceiling?.Invoke(x, y) ?? Sky)]);

    /// <summary>The yard with the block <paramref name="height"/> high.</summary>
    private static VoxelColumns Block(float height) => Yard((x, y) => InBlock(x, y) ? height : 0f);

    /// <summary>A ledge just past a jump on foot (the mod's crouched jump clears 72), whatever the blast does.</summary>
    private const float Low = 96f;

    private static JumpPlaces Places(VoxelColumns columns, Func<JumpPlacesSettings, JumpPlacesSettings>? change = null, NavGraph? graph = null, JumpEnvelope? reach = null,
                                     JumpSearchSettings? search = null)
    {
        NavGraph g = graph ?? GraphOf(columns);
        GraphJumpWalk walk = GraphJumpWalk.Of(g, RunSpeed);
        JumpPlacesSettings settings = JumpPlacesSettings.Of(TFNavProfile.ModNumbers);
        return new JumpPlaces(g, columns, Search(columns, search, walk), walk, reach ?? Reach.Value, change?.Invoke(settings) ?? settings);
    }

    private static float FloorOf(NavGraph graph, int cell) => graph.Cell(cell).FloorZ;

    [Fact]
    public void ALedgeWithNoWayUpIsProposedOnceFromTheGroundBeforeIt()
    {
        float height = Height(0.3f);
        VoxelColumns columns = Block(height);
        NavGraph graph = GraphOf(columns);
        var settings = JumpPlacesSettings.Of(TFNavProfile.ModNumbers);
        Assert.True(height > settings.MinRise, $"the control: a block {height} high is past a jump on foot");
        JumpCandidate c = Assert.Single(Places(columns, graph: graph).Propose());
        Assert.Equal((JumpPlaceKind.Up, height, (float?)null), (c.Kind, c.Rise, c.WalkCost));
        Assert.Equal((0f, height), (FloorOf(graph, c.From), FloorOf(graph, c.To)));
        Assert.Equal((graph.FindCell(new AiVector(c.Takeoff.X, c.Takeoff.Y, c.Takeoff.Z)).Index, graph.FindCell(new AiVector(c.Landing.X, c.Landing.Y, c.Landing.Z)).Index), (c.From, c.To));

        // The block's four sides are alike, so it is the first looked at: the east, the middle of its edge. The landing
        // is the inset in from the edge, the takeoff the standoff out (to the half column), straight across it.
        float edge = (BlockX + BlockSize) * Voxel, middle = (BlockY + BlockSize / 2 + 0.5f) * Voxel;
        Assert.Equal((edge - settings.Inset, middle, height + VoxelMoveWorld.DistEpsilon), (c.Landing.X, c.Landing.Y, c.Landing.Z));
        Assert.InRange(c.Takeoff.X - edge, settings.Standoff - Voxel * 0.5f, settings.Standoff + Voxel * 0.5f);
        Assert.Equal((middle, VoxelMoveWorld.DistEpsilon), (c.Takeoff.Y, c.Takeoff.Z));
        Assert.Equal(c.Takeoff.X - c.Landing.X, c.Distance, 3);
    }

    [Fact]
    public void EveryLinkFlownAgainLandsOnItsCell()
    {
        float height = Height(0.3f);
        VoxelColumns columns = Block(height);
        NavGraph graph = GraphOf(columns);
        JumpPlacesResult result = Places(columns, graph: graph).Find();
        JumpCandidate c = Assert.Single(result.Candidates);
        JumpLink link = Assert.Single(result.Links);
        JumpOutcome flown = Search(columns).Fly(link.Recipe);
        Assert.True(flown.Flew && flown.OnGround);
        Assert.Equal(link.To, graph.FindCell(new AiVector(flown.Rest.X, flown.Rest.Y, flown.Rest.Z)).Index);
        Assert.Equal((c.From, height), (link.From, FloorOf(graph, link.To)));
        Assert.Equal((c.Takeoff, c.Landing), (link.Recipe.Takeoff, link.Recipe.Landing));
        Assert.True(Flat(flown.Rest, c.Landing) <= JumpPlacesSettings.Of(TFNavProfile.ModNumbers).Tolerance);
    }

    [Fact]
    public void ALinkIsTheSearchsAnswerForItsCandidate()
    {
        float height = Height(0.3f);
        VoxelColumns columns = Block(height);
        NavGraph graph = GraphOf(columns);
        JumpPlacesResult result = Places(columns, graph: graph).Find();
        JumpCandidate c = result.Candidates[0];
        JumpLink link = Assert.Single(result.Links);
        JumpSearchResult asked = Search(columns).Find(new JumpAsk(c.Takeoff, c.Landing, JumpPlacesSettings.Of(TFNavProfile.ModNumbers).Tolerance));
        JumpFound found = asked.Found!.Value;
        Assert.Equal((found.Recipe, found.Family, found.Robustness, found.Seconds, found.AirSeconds, found.Health),
                     (link.Recipe, link.Family, link.Robustness, link.Seconds, link.AirSeconds, link.Health));
        Assert.True(link.Robustness >= new JumpSearchSettings().Keep && link.Health > 0f);
        // The search's flights, and the one that finds the cell it rests in.
        Assert.Equal(asked.Flights + 1, result.Flights);
        Assert.Null(link.WalkSeconds);
    }

    [Fact]
    public void ALedgeHigherThanTheJumpRisesIsNotProposed()
    {
        float height = Height(1.2f);
        JumpPlacesResult result = Places(Block(height)).Find();
        Assert.Equal((0, 0, 0), (result.Candidates.Count, result.Links.Count, result.Flights));
        // The control: told the jump reaches twice as far as it does, the pass proposes it, and the search, which flies it, finds nothing.
        JumpPlacesResult fooled = Places(Block(height), s => s with { ReachShare = 2f, PerPair = 1 }).Find();
        Assert.Equal((1, 0), (fooled.Candidates.Count, fooled.Links.Count));
        Assert.InRange(fooled.Flights, 20, new JumpSearchSettings().MaxFlights);
    }

    /// <summary>The block with a stair up its west side: a column a step, 16 units each, the top step level with the block.</summary>
    private static VoxelColumns Staired(float height)
    {
        int steps = (int)(height / Voxel);
        return Yard((x, y) => InBlock(x, y) ? height : InBlock(x, y, BlockX - steps, steps) ? (x - (BlockX - steps) + 1) * Voxel : 0f);
    }

    [Fact]
    public void AWayRoundOnFootIsWeighedAgainstTheStraightLine()
    {
        float height = Height(0.3f);
        VoxelColumns columns = Staired(height);
        NavGraph graph = GraphOf(columns);
        // The stair is on the far side of the block from the east: a long way round, and a jump is worth looking for.
        JumpCandidate c = Assert.Single(Places(columns, graph: graph).Propose());
        float straight = MathF.Sqrt(c.Distance * c.Distance + c.Rise * c.Rise);
        Assert.True(c.WalkCost > 2.5f * straight, $"the control: the walk costs {c.WalkCost}, the straight line is {straight}");
        Assert.Equal(GraphJumpWalk.Of(graph, RunSpeed).Cost(new CellId(c.From), new CellId(c.To)), c.WalkCost);
        // A walk that long is worth no jump to a pass that lets a walk be three times as long as it is.
        float factor = 3f * c.WalkCost!.Value / straight;
        Assert.Empty(Places(columns, s => s with { WalkFactor = factor }, graph).Propose());
        // And the link says what the walk takes.
        JumpLink link = Assert.Single(Places(columns, graph: graph).Find().Links);
        Assert.Equal(c.WalkCost.Value / RunSpeed, link.WalkSeconds!.Value, 2);
    }

    [Fact]
    public void AGapIsProposedFromTheGroundAsHighAcrossIt()
    {
        // Two blocks of one height with a fifth of the jump's range between them (no more than fits the yard).
        float height = Height(0.3f);
        int gap = Math.Min(30, (int)MathF.Round(0.2f * Reach.Value.Range / Voxel)), west = 12, east = west + BlockSize + gap;
        VoxelColumns columns = Yard((x, y) => InBlock(x, y, west) || InBlock(x, y, east) ? height : 0f);
        NavGraph graph = GraphOf(columns);
        var settings = JumpPlacesSettings.Of(TFNavProfile.ModNumbers);
        IReadOnlyList<JumpCandidate> candidates = Places(columns, graph: graph).Propose();
        // Onto each block from the floor, and over the gap each way.
        Assert.Equal(4, candidates.Count);
        Assert.Equal(2, candidates.Count(c => c.Kind == JumpPlaceKind.Up && FloorOf(graph, c.From) == 0f));
        JumpCandidate over = Assert.Single(candidates, c => c.Kind == JumpPlaceKind.Gap && c.Takeoff.X < c.Landing.X);
        Assert.Equal((height, height, 0f), (FloorOf(graph, over.From), FloorOf(graph, over.To), over.Rise));
        float nearEdge = (west + BlockSize) * Voxel, farEdge = east * Voxel;
        // The takeoff a little in from its own edge, the landing the inset in from the other.
        Assert.InRange(nearEdge - over.Takeoff.X, settings.GapInset - Voxel * 0.5f, settings.GapInset + Voxel);
        Assert.Equal(farEdge + settings.Inset, over.Landing.X);
        Assert.Equal(over.Takeoff.Y, over.Landing.Y);
        Assert.Single(candidates, c => c.Kind == JumpPlaceKind.Gap && c.Takeoff.X > c.Landing.X);
    }

    [Fact]
    public void TheWayOutFromAnEdgeIsFollowedNoFurtherThanTheJumpsRange()
    {
        float height = Height(0.3f);
        int gap = Math.Min(30, (int)MathF.Round(0.2f * Reach.Value.Range / Voxel)), west = 12, east = west + BlockSize + gap;
        VoxelColumns columns = Yard((x, y) => InBlock(x, y, west) || InBlock(x, y, east) ? height : 0f);
        NavGraph graph = GraphOf(columns);
        float share = JumpPlacesSettings.Of(TFNavProfile.ModNumbers).ReachShare;
        // A pass told the jump's range is two columns short of the gap: the other block is past where it looks, the floor is not.
        IReadOnlyList<JumpCandidate> shortOf = Places(columns, graph: graph, reach: Reach.Value with { Range = (gap - 2) * Voxel / share }).Propose();
        Assert.Equal(2, shortOf.Count);
        Assert.DoesNotContain(shortOf, c => c.Kind == JumpPlaceKind.Gap);
        // The control: told it is four columns past the gap's far side, it proposes the gap both ways.
        IReadOnlyList<JumpCandidate> past = Places(columns, graph: graph, reach: Reach.Value with { Range = (gap + 4) * Voxel / share }).Propose();
        Assert.Equal(2, past.Count(c => c.Kind == JumpPlaceKind.Gap));
    }

    [Fact]
    public void NothingIsProposedFromUnderACeilingTheBodyCannotRisePast()
    {
        // The floor round the block is roofed from four columns out: the roof is over the ledge's level, and
        // under a crouched body's height more. The standoff's takeoff is under it.
        float height = Low;
        var settings = JumpPlacesSettings.Of(TFNavProfile.ModNumbers);
        Assert.True(height > settings.MinRise && height <= settings.ReachShare * Reach.Value.Rise, $"the control: a block {height} high is past a jump on foot and within the blast's");
        static bool Roofed(int x, int y) => !InBlock(x, y) && !(x >= BlockX - 4 && x < BlockX + BlockSize + 4 && y >= BlockY - 4 && y < BlockY + BlockSize + 4);
        VoxelColumns low = Yard((x, y) => InBlock(x, y) ? height : 0f, (x, y) => Roofed(x, y) ? height + settings.CrouchHeight - 8f : Sky);
        Assert.Empty(Places(low).Propose());
        // The control: the roof a crouched body's height and a little over the ledge, and the same jump is proposed.
        VoxelColumns high = Yard((x, y) => InBlock(x, y) ? height : 0f, (x, y) => Roofed(x, y) ? height + settings.CrouchHeight + 8f : Sky);
        JumpCandidate c = Assert.Single(Places(high).Propose());
        Assert.True(Roofed((int)(c.Takeoff.X / Voxel), (int)(c.Takeoff.Y / Voxel)), "the control: the takeoff is under the roof");
    }

    [Fact]
    public void SomethingInTheWayAtTheLedgesLevelEndsTheWayOut()
    {
        // A wall five columns out from the block all round, higher than it: the floor beyond is no takeoff, and
        // the strip before the wall is.
        float height = Height(0.3f);
        static bool Wall(int x, int y)
            => (x == BlockX - 6 || x == BlockX + BlockSize + 5 || y == BlockY - 6 || y == BlockY + BlockSize + 5)
               && x >= BlockX - 6 && x <= BlockX + BlockSize + 5 && y >= BlockY - 6 && y <= BlockY + BlockSize + 5;
        VoxelColumns columns = Yard((x, y) => Wall(x, y) ? float.NaN : InBlock(x, y) ? height : 0f);
        JumpCandidate c = Assert.Single(Places(columns).Propose());
        float edge = (BlockX + BlockSize) * Voxel;
        Assert.InRange(c.Takeoff.X - edge, 0f, 5f * Voxel);
    }

    [Fact]
    public void OfAPairsSidesTheOneWithRoomForTheStandoffIsKept()
    {
        // A wall close behind the east side's takeoff: eight columns of floor, short of the standoff. The other sides are open.
        float height = Height(0.3f);
        VoxelColumns columns = Yard((x, y) => x >= BlockX + BlockSize + 8 ? float.NaN : InBlock(x, y) ? height : 0f);
        var settings = JumpPlacesSettings.Of(TFNavProfile.ModNumbers);
        JumpCandidate c = Assert.Single(Places(columns).Propose());
        float edge = (BlockX + BlockSize) * Voxel, north = (BlockY + BlockSize) * Voxel;
        Assert.True(c.Takeoff.X < edge, $"the takeoff {c.Takeoff} is not on the cramped east side");
        Assert.InRange(c.Takeoff.Y - north, settings.Standoff - Voxel * 0.5f, settings.Standoff + Voxel * 0.5f);
    }

    [Fact]
    public void AnEdgeShorterThanALandingIsNone()
    {
        float height = Height(0.3f);
        var settings = JumpPlacesSettings.Of(TFNavProfile.ModNumbers);
        int narrow = (int)(settings.MinRun / Voxel) - 1;
        static bool Pillar(int x, int y, int size) => x >= BlockX && x < BlockX + size && y >= BlockY && y < BlockY + size;
        Assert.Empty(Places(Yard((x, y) => Pillar(x, y, narrow) ? height : 0f)).Propose());
        Assert.Single(Places(Yard((x, y) => Pillar(x, y, narrow + 1) ? height : 0f)).Propose());
    }

    [Fact]
    public void AFloorOnlyACrouchedBodyFitsOnIsNoLanding()
    {
        // The block's top under a roof a crouched body's height and a little over it: the graph has cells there, for a
        // crouched body alone. The floor round it is open to the sky.
        float height = Height(0.3f);
        NavNumbers body = TFNavProfile.ModNumbers;
        VoxelColumns low = Yard((x, y) => InBlock(x, y) ? height : 0f, (x, y) => InBlock(x, y) ? height + body.CrouchHeight + 4f : Sky);
        NavGraph lowGraph = GraphOf(low);
        NavCell top = lowGraph.Cell(lowGraph.FindCell(new AiVector((BlockX + 8.5f) * Voxel, (BlockY + 8.5f) * Voxel, height + 1f)));
        Assert.True(top.FloorZ == height && top.CrouchOnly, $"the control: the cell on the block's top: {top}");
        Assert.Empty(Places(low, graph: lowGraph).Propose());
        // The control: a standing body's height and a little, and it is one.
        VoxelColumns room = Yard((x, y) => InBlock(x, y) ? height : 0f, (x, y) => InBlock(x, y) ? height + body.StandHeight + 4f : Sky);
        Assert.Single(Places(room).Propose());
    }

    [Fact]
    public void ALedgeTooNarrowForTheInsetIsLandedOnAtItsEdgeColumn()
    {
        // A wall's top two columns across and the block's length along: the inset in from its east edge is past its west one.
        float height = Height(0.3f);
        var settings = JumpPlacesSettings.Of(TFNavProfile.ModNumbers);
        VoxelColumns columns = Yard((x, y) => InBlock(x, y, BlockX, 2) ? height : 0f);
        NavGraph graph = GraphOf(columns);
        Assert.True(settings.Inset > 2f * Voxel, "the control: the inset is wider than the wall");
        JumpCandidate c = Assert.Single(Places(columns, graph: graph).Propose());
        Assert.Equal(height, FloorOf(graph, c.To));
        Assert.Equal((BlockX + 2) * Voxel - Voxel * 0.5f, c.Landing.X);
    }

    [Fact]
    public void OfAPairsSidesWithRoomTheLongerEdgeIsKept()
    {
        // A block 256 along x and 128 along y: the east side is looked at first, and the north is twice as long.
        float height = Height(0.3f);
        VoxelColumns columns = Yard((x, y) => x >= BlockX && x < BlockX + 16 && y >= BlockY && y < BlockY + 8 ? height : 0f);
        JumpCandidate c = Assert.Single(Places(columns).Propose());
        Assert.True(c.Takeoff.Y > (BlockY + 8) * Voxel && c.Takeoff.X == c.Landing.X, $"the takeoff {c.Takeoff} is north of the block, straight out from the landing {c.Landing}");
    }

    [Fact]
    public void AStairsStepsAreNoTakeoff()
    {
        // A block with a stair up its west side, and a second block before the stair's foot with half the standoff of
        // floor between: the standoff out from the second block's east edge is on the stair, whose columns are each a
        // level of their own and no ground to take off from. The second block is jumped onto from the floor.
        float height = 2f * Low;
        var settings = JumpPlacesSettings.Of(TFNavProfile.ModNumbers);
        Assert.True(height <= settings.ReachShare * Reach.Value.Rise, "the control: the blocks are within the blast's reach");
        int steps = (int)(height / Voxel), foot = BlockX - steps, second = foot - 5 - 8;
        VoxelColumns columns = Yard((x, y) => InBlock(x, y) || InBlock(x, y, second, 8) ? height : InBlock(x, y, foot, steps) ? (x - foot + 1) * Voxel : 0f);
        NavGraph graph = GraphOf(columns);
        IReadOnlyList<JumpCandidate> candidates = Places(columns, graph: graph).Propose();
        Assert.All(candidates, c => Assert.True(FloorOf(graph, c.From) == 0f || FloorOf(graph, c.From) == height, $"a takeoff on a step: {c}"));
        // The control: the standoff out from the second block's east edge is on the stair.
        float edge = (second + 8) * Voxel;
        Assert.True(edge + settings.Standoff > foot * Voxel + Voxel, "the control: the standoff from the second block is past the stair's foot");
        Assert.Contains(candidates, c => FloorOf(graph, c.To) == height && c.Landing.X < edge && FloorOf(graph, c.From) == 0f);
    }

    [Fact]
    public void AJumpThatLandsInTooFewOfItsTrialsIsNoLink()
    {
        float height = Height(0.3f);
        VoxelColumns columns = Block(height);
        NavGraph graph = GraphOf(columns);
        // A pass whose search keeps nothing short of more than every trial.
        JumpPlacesResult strict = Places(columns, s => s with { PerPair = 1 }, graph: graph, search: new JumpSearchSettings { Keep = 1.01f }).Find();
        Assert.Equal((1, 0), (strict.Candidates.Count, strict.Links.Count));
        Assert.True(strict.Flights > 20);
        // The control: the same pass keeping at the plan's 60 % has the link.
        Assert.Single(Places(columns, graph: graph).Find().Links);
    }

    [Fact]
    public void ADropABodyJumpsBackUpIsNoEdge()
    {
        var settings = JumpPlacesSettings.Of(TFNavProfile.ModNumbers);
        Assert.Equal(TFNavProfile.ModNumbers.MaxCrouchJumpRise, settings.MinRise);
        float under = Voxel * MathF.Floor(settings.MinRise / Voxel), over = under + Voxel;
        Assert.True(over <= 0.9f * Reach.Value.Rise, "the control: the higher block is within the jump's reach");
        // The graph walks no step that high, so on foot there is no way up either block: only the edge rule tells them apart.
        Assert.Empty(Places(Block(under)).Propose());
        Assert.Single(Places(Block(over)).Propose());
    }

    [Fact]
    public void GroundTooShortToStandABodyOnIsNoTakeoff()
    {
        // A second, lower block's top two columns wide, straight out from the first's east edge: lower ground, and no takeoff.
        float step = Low, height = 2f * Low;
        var settings = JumpPlacesSettings.Of(TFNavProfile.ModNumbers);
        Assert.True(height - step > settings.MinRise && height <= settings.ReachShare * Reach.Value.Rise, "the control: both blocks are ledges, one over the other, within the blast's reach");
        static bool Shelf(int x, int y, int wide) => x >= BlockX + BlockSize + 6 && x < BlockX + BlockSize + 6 + wide && y >= BlockY && y < BlockY + BlockSize;
        NavGraph graph = GraphOf(Yard((x, y) => InBlock(x, y) ? height : Shelf(x, y, 2) ? step : 0f));
        IReadOnlyList<JumpCandidate> narrow = Places(Yard((x, y) => InBlock(x, y) ? height : Shelf(x, y, 2) ? step : 0f), graph: graph).Propose();
        Assert.DoesNotContain(narrow, c => FloorOf(graph, c.From) == step);
        // The control: the shelf four columns wide is one.
        VoxelColumns wide = Yard((x, y) => InBlock(x, y) ? height : Shelf(x, y, 4) ? step : 0f);
        NavGraph wideGraph = GraphOf(wide);
        Assert.Contains(Places(wide, graph: wideGraph).Propose(), c => FloorOf(wideGraph, c.From) == step && FloorOf(wideGraph, c.To) == height);
    }

    [Fact]
    public void WhatProposingLookedThroughIsCounted()
    {
        // The block, and a wall two columns out from its east side: 16 columns of edge a side, four runs, and three
        // landings whose way on foot is asked (the east side's ground is too short to take off from, so nothing asks).
        float height = Height(0.3f);
        VoxelColumns columns = Yard((x, y) => x >= BlockX + BlockSize + 2 ? float.NaN : InBlock(x, y) ? height : 0f);
        JumpPlaces places = Places(columns);
        Assert.Single(places.Propose(out JumpPlacesWork work));
        Assert.Equal(new JumpPlacesWork(4 * BlockSize, 4, 3), work);
        Assert.Equal(work, places.Find().Work);
    }

    /// <summary>Two blocks of one height with a fifth of the jump's range between them: four candidates.</summary>
    private static VoxelColumns TwoBlocks(float height)
    {
        int gap = Math.Min(30, (int)MathF.Round(0.2f * Reach.Value.Range / Voxel)), west = 12, east = west + BlockSize + gap;
        return Yard((x, y) => InBlock(x, y, west) || InBlock(x, y, east) ? height : 0f);
    }

    [Fact]
    public void ThePassIsTheSameEveryTime()
    {
        float height = Height(0.3f);
        VoxelColumns columns = Block(height);
        NavGraph graph = GraphOf(columns);
        JumpPlaces places = Places(columns, graph: graph);
        JumpPlacesResult first = places.Find(), again = places.Find(), other = Places(columns, graph: graph).Find();
        Assert.True(first.Links.Count == 1, $"the control: {first.Links.Count} links of {first.Candidates.Count} candidates");
        foreach (JumpPlacesResult r in (JumpPlacesResult[])[again, other])
        {
            Assert.Equal(first.Candidates, r.Candidates);
            Assert.Equal(first.Links, r.Links);
            Assert.Equal((first.Flights, first.Work), (r.Flights, r.Work));
        }
    }

    [Fact]
    public void TheCandidatesAreInTheOrderOfTheirLandingCellThenTheirTakeoffs()
    {
        VoxelColumns columns = TwoBlocks(Height(0.3f));
        NavGraph graph = GraphOf(columns);
        IReadOnlyList<JumpCandidate> first = Places(columns, graph: graph).Propose();
        Assert.Equal(4, first.Count);
        Assert.Equal(first.OrderBy(c => c.To).ThenBy(c => c.From), first);
        Assert.Equal(first, Places(columns, graph: graph).Propose());
    }

    [Fact]
    public void PassesOnOtherThreadsOverOneGraphEachFindWhatOneAloneFinds()
    {
        float height = Height(0.3f);
        VoxelColumns columns = Block(height);
        NavGraph graph = GraphOf(columns);
        JumpPlacesResult alone = Places(columns, graph: graph).Find();
        Assert.True(alone.Links.Count == 1, "the control: there is something to find");
        // Built here, as the game's thread builds a simulator; run there, three at once over the one graph and free space.
        JumpPlaces[] passes = [.. Enumerable.Range(0, 3).Select(_ => Places(columns, graph: graph))];
        var found = new JumpPlacesResult?[passes.Length];
        Thread[] threads = [.. passes.Select((p, i) => new Thread(() => found[i] = p.Find()))];
        foreach (Thread t in threads)
            t.Start();
        foreach (Thread t in threads)
            t.Join();
        Assert.All(found, r =>
        {
            Assert.Equal(alone.Candidates, r!.Candidates);
            Assert.Equal(alone.Links, r.Links);
            Assert.Equal((alone.Flights, alone.Work), (r.Flights, r.Work));
        });
    }

    [Fact]
    public void ItSearchesALedgeOnceNotACellPairAtATime()
    {
        // The block has 256 cells on top and its edges 64 columns; the floor some 5,800. One pair of level stretches, one search.
        float height = Height(0.3f);
        VoxelColumns columns = Block(height);
        NavGraph graph = GraphOf(columns);
        Assert.True(graph.CellCount > 5000, $"the control: {graph.CellCount} cells");
        JumpPlacesResult result = Places(columns, graph: graph).Find();
        Assert.Single(result.Candidates);
        Assert.InRange(result.Flights, 1, new JumpSearchSettings().MaxFlights + 1);
    }

    [Fact]
    public void ATakeoffIsProposedOnlyWhereTheGraphLeadsFromASpawn()
    {
        // The block on the floor, and a pen of floor in the yard's west, walled off by a solid column across the yard.
        float height = Height(0.3f);
        VoxelColumns columns = Yard((x, y) => x == 4 ? float.NaN : InBlock(x, y) ? height : 0f);
        NavGraph graph = GraphOf(columns);
        int pen = graph.FindCell(new AiVector(2 * Voxel, 2 * Voxel, 1f)).Index, floor = graph.FindCell(new AiVector(20 * Voxel, 2 * Voxel, 1f)).Index;
        int top = graph.FindCell(new AiVector((BlockX + 8) * Voxel, (BlockY + 8) * Voxel, height + 1f)).Index;
        Assert.True(graph.ComponentOf(pen) != graph.ComponentOf(floor) && graph.Cell(top).FloorZ == height, "the control: the pen is a part of the graph of its own");
        JumpPlaces Seeded(params int[] seeds)
        {
            GraphJumpWalk walk = GraphJumpWalk.Of(graph, RunSpeed);
            return new JumpPlaces(graph, columns, Search(columns, null, walk), walk, Reach.Value, JumpPlacesSettings.Of(TFNavProfile.ModNumbers), seeds);
        }

        // Spawned on the floor: the jump up from it. Spawned on the top: the drop leads to the floor, and the jump is
        // from there. Spawned only in the pen: nobody stands on the floor, nothing to jump from.
        JumpCandidate up = Assert.Single(Seeded(floor).Propose());
        Assert.Equal(top, graph.FindCell(new AiVector(up.Landing.X, up.Landing.Y, up.Landing.Z)).Index is var t && graph.Cell(t).FloorZ == height ? top : t);
        Assert.Single(Seeded(top).Propose());
        Assert.Empty(Seeded(pen).Propose(out JumpPlacesWork work));
        Assert.True(work.Unplayed > 0, $"{work}");
        // With no spawn known, everywhere is played.
        Assert.Single(Seeded().Propose());
    }

    [Fact]
    public void ALedgeOnlyAJumpReachesIsSomewhereToJumpOnFrom()
    {
        // Two blocks side by side: a low one, and past it a high one no jump from the floor reaches but one from the low
        // block's top does. Spawned on the floor, the low block's top is played only once a link lands on it.
        float low = Height(0.3f), high = low + Height(0.6f);
        var settings = JumpPlacesSettings.Of(TFNavProfile.ModNumbers);
        Assert.True(high > settings.ReachShare * Reach.Value.Rise && high - low <= settings.ReachShare * Reach.Value.Rise, "the control: the high block is out of reach from the floor and in reach from the low one");
        static bool In(int x, int y, int from, int to) => x >= from && x < to && y >= BlockY && y < BlockY + BlockSize;
        VoxelColumns columns = Yard((x, y) => In(x, y, 30, 50) ? low : In(x, y, 50, 66) ? high : 0f);
        NavGraph graph = GraphOf(columns);
        int floor = graph.FindCell(new AiVector(100f, 100f, 1f)).Index;
        JumpPlaces Seeded(int rounds)
        {
            GraphJumpWalk walk = GraphJumpWalk.Of(graph, RunSpeed);
            return new JumpPlaces(graph, columns, Search(columns, null, walk), walk, Reach.Value, settings with { MaxRounds = rounds }, [floor]);
        }

        bool FromLowToHigh(JumpLink l) => FloorOf(graph, l.From) == low && FloorOf(graph, l.To) == high;
        JumpPlacesResult rounds = Seeded(4).Find();
        Assert.Contains(rounds.Links, l => FloorOf(graph, l.From) == 0f && FloorOf(graph, l.To) == low);
        Assert.Contains(rounds.Links, FromLowToHigh);
        Assert.Contains(rounds.Candidates, c => FloorOf(graph, c.From) == low && FloorOf(graph, c.To) == high);
        // The control: in one round, nothing is proposed from the low block's top.
        JumpPlacesResult one = Seeded(1).Find();
        Assert.Contains(one.Links, l => FloorOf(graph, l.From) == 0f && FloorOf(graph, l.To) == low);
        Assert.DoesNotContain(one.Candidates, c => FloorOf(graph, c.From) == low);
        Assert.Equal(rounds.Candidates.OrderBy(c => c.To).ThenBy(c => c.From), rounds.Candidates);
    }

    [Fact]
    public void WhenAPairsBestTakeoffGivesNoLinkItsAlternatesAreSearched()
    {
        // The block, with a search that keeps nothing: the best of the floor-to-top pair, then its alternates, are searched.
        float height = Height(0.3f);
        VoxelColumns columns = Block(height);
        NavGraph graph = GraphOf(columns);
        var never = new JumpSearchSettings { Keep = 1.01f };
        JumpPlacesResult one = Places(columns, s => s with { PerPair = 1 }, graph, search: never).Find();
        JumpPlacesResult three = Places(columns, s => s with { PerPair = 3 }, graph, search: never).Find();
        Assert.Single(one.Candidates);
        Assert.Equal(3, three.Candidates.Count);
        Assert.Equal(3, three.Candidates.Select(c => (c.From, c.To)).Distinct().Count());
        Assert.Contains(one.Candidates[0], three.Candidates);
        Assert.True(three.Flights > one.Flights, $"{three.Flights} flights against {one.Flights}");
        // And once one gives a link, no more of the pair are searched: the plan's keep finds the best's.
        JumpPlacesResult kept = Places(columns, s => s with { PerPair = 3 }, graph).Find();
        Assert.Equal((1, 1), (kept.Candidates.Count, kept.Links.Count));
        Assert.Equal(one.Candidates, kept.Candidates);
    }

    [Fact]
    public void ACancelledPassStopsBeforeItsFirstSearch()
    {
        VoxelColumns columns = Block(Height(0.3f));
        JumpPlaces places = Places(columns);
        Assert.Single(places.Propose());
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => places.Find(cancel.Token));
        // The control: uncancelled, the same pass searches and links.
        Assert.Single(places.Find(CancellationToken.None).Links);
    }

    [Fact]
    public void ACappedPassSearchesOnlyItsFirstCandidates()
    {
        VoxelColumns columns = TwoBlocks(Height(0.3f));
        NavGraph graph = GraphOf(columns);
        JumpPlacesResult all = Places(columns, s => s with { PerPair = 1 }, graph: graph).Find();
        Assert.True(all.Candidates.Count == 4 && all.Links.Select(l => l.Recipe.Landing).Distinct().Count() > 1, $"the control: {all.Candidates.Count} candidates, {all.Links.Count} links");
        JumpPlacesResult one = Places(columns, s => s with { MaxCandidates = 1, PerPair = 1 }, graph).Find();
        Assert.Equal(all.Candidates, one.Candidates);
        JumpCandidate first = all.Candidates[0];
        Assert.All(one.Links, l => Assert.True(l.From == first.From && l.Recipe.Landing.Equals(first.Landing), $"a link not of the first candidate: {l}"));
        Assert.True(one.Flights < all.Flights, $"{one.Flights} flights capped against {all.Flights}");
        Assert.Equal(all.Links.Where(l => l.From == first.From && l.Recipe.Landing.Equals(first.Landing)), one.Links);
    }

    [Fact]
    public void AFlightThatRestsWhereTheGraphHasNoCellAtItsHeightIsNoLink()
    {
        // The block's free space, and a graph of one floor cell under everything and one cell a unit wide on the block's
        // top where the east edge's landing is: the flight lands on the block, beside that cell, where the graph has
        // only the floor under it. Which cell would the link go to? The floor's, 'height' below the bot: none is right.
        float height = Height(0.3f);
        VoxelColumns columns = Block(height);
        var settings = JumpPlacesSettings.Of(TFNavProfile.ModNumbers);
        float landX = (BlockX + BlockSize) * Voxel - settings.Inset, landY = (BlockY + BlockSize / 2 + 0.5f) * Voxel;
        NavGraph Graph(float half)
        {
            var b = new NavGraphBuilder();
            b.AddCell(new NavCell(0f, 0f, Wide * Voxel, Deep * Voxel, 0f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
            b.AddCell(new NavCell(landX - half, landY - half, landX + half, landY + half, height, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
            return b.Build();
        }

        NavGraph narrow = Graph(0.5f);
        JumpPlacesResult r = Places(columns, graph: narrow).Find();
        JumpCandidate c = Assert.Single(r.Candidates);
        Assert.Equal((0, 1), (c.From, c.To));
        Assert.Empty(r.Links);

        // The control: the search finds a recipe for it that rests on the block's top, off the unit-wide cell.
        JumpSearch search = Search(columns);
        JumpFound found = search.Find(new JumpAsk(c.Takeoff, c.Landing, settings.Tolerance)).Found!.Value;
        JumpOutcome flown = search.Fly(found.Recipe);
        Assert.True(flown.OnGround && MathF.Abs(flown.Rest.Z - height) < 1f && narrow.FindCell(new AiVector(flown.Rest.X, flown.Rest.Y, flown.Rest.Z)).Index == 0,
                    $"the control: the flight rests at {flown.Rest}");
        // And a graph whose cell on the top holds where it rests links to it.
        JumpLink link = Assert.Single(Places(columns, graph: Graph(settings.Tolerance + Voxel)).Find().Links);
        Assert.Equal(1, link.To);
    }
}
