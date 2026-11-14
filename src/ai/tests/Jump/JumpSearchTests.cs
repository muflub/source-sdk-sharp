using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The search for a jump's recipe (plan_bots_teams_tf2_part_g G.5 step 4), on a field with a ledge
/// across it. The blast's physics are about to change, so no fact names a height or a distance a
/// jump reaches: a ledge is as high as a share of the reach the simulator gives today
/// (<see cref="JumpEnvelope"/>), and what a search finds is held to what the simulator then does
/// with it.
/// </summary>
public class JumpSearchTests
{
    /// <summary>A ledge half as high as the jump rises: well within reach, and far over a hop.</summary>
    private static float Reachable() => MathF.Round(0.5f * Envelope().Rise);

    private static JumpFound Found(JumpSearch search, in JumpAsk ask)
    {
        JumpSearchResult result = search.Find(ask);
        Assert.True(result.Found is not null, $"nothing found in {result.Flights} flights");
        return result.Found!.Value;
    }

    [Fact]
    public void AFoundRecipeFlownAgainLandsOnItsTarget()
    {
        float height = Reachable();
        JumpSearch search = Search(LedgeField(height));
        JumpAsk ask = OntoTheLedge(height);
        JumpFound found = Found(search, ask);
        JumpOutcome again = search.Fly(found.Recipe);
        Assert.True(again.Flew && again.OnGround);
        Assert.Equal(height, again.Rest.Z, 0);
        Assert.True(Flat(again.Rest, ask.Landing) <= ask.Tolerance, $"it rests {Flat(again.Rest, ask.Landing)} from the landing");
        Assert.Equal((ask.Takeoff, ask.Landing), (found.Recipe.Takeoff, found.Recipe.Landing));
        // The control: a hop from the same place does not get there.
        JumpOutcome hop = search.Fly(found.Recipe with { Pitch = -80f });
        Assert.True(hop.Flew && hop.Rest.Z < 1f, $"the control: a hop rests at {hop.Rest}");
    }

    [Fact]
    public void ATargetTheSimulatorCannotReachYieldsNone()
    {
        // A ledge half as high again as any grid flight rises.
        float height = MathF.Round(1.5f * Envelope().Rise);
        JumpSearchResult result = Search(LedgeField(height)).Find(OntoTheLedge(height));
        Assert.Null(result.Found);
        Assert.True(result.Flights > 20, $"the control: it looked: {result.Flights} flights");
    }

    [Fact]
    public void TheSameAskGivesTheSameAnswer()
    {
        float height = Reachable();
        JumpSearchResult first = Search(LedgeField(height)).Find(OntoTheLedge(height)), again = Search(LedgeField(height)).Find(OntoTheLedge(height));
        Assert.NotNull(first.Found);
        Assert.Equal(first, again);
        // And from one search asked twice: nothing is left over from the first.
        JumpSearch search = Search(LedgeField(height));
        Assert.Equal(first, search.Find(OntoTheLedge(height)));
        Assert.Equal(first, search.Find(OntoTheLedge(height)));
    }

    /// <summary>A hit of the up grid whose robustness is neither none nor all of its trials: where a seed's draw shows.</summary>
    private static (JumpSearch Search, JumpAsk Ask, JumpRecipe Recipe) AFragileHit()
    {
        float height = Reachable();
        var settings = new JumpSearchSettings { Trials = 40 };
        JumpSearch search = Search(LedgeField(height), settings);
        foreach (float standoff in (float[])[48f, 96f, 160f, 320f])
        {
            JumpAsk ask = OntoTheLedge(height, standoff);
            for (float pitch = 40f; pitch <= 88f; pitch += 4f)
            {
                var recipe = new JumpRecipe(ask.Takeoff, ask.Landing, 0f, pitch, 180f, 2, true);
                if (!search.Made(ask, search.Fly(recipe)))
                    continue;
                int flights = 0;
                float robustness = search.Robustness(ask, recipe, ref flights);
                if (robustness > 0.15f && robustness < 0.85f)
                    return (search, ask, recipe);
            }
        }

        throw new Xunit.Sdk.XunitException("the control: no hit onto the ledge is fragile today; the fact needs another place to look");
    }

    [Fact]
    public void RobustnessIsDrawnFromTheAsksSeed()
    {
        (JumpSearch search, JumpAsk ask, JumpRecipe recipe) = AFragileHit();
        int flights = 0;
        var seen = new HashSet<float>();
        for (uint seed = 1; seed <= 12; seed++)
        {
            JumpAsk seeded = ask with { Seed = seed };
            float robustness = search.Robustness(seeded, recipe, ref flights);
            Assert.Equal(robustness, search.Robustness(seeded, recipe, ref flights));
            seen.Add(robustness);
        }

        Assert.True(seen.Count > 1, "the control: another seed draws other trials");
        Assert.Equal(12 * 2 * search.Settings.Trials, flights);
    }

    [Fact]
    public void WithNothingPerturbedRobustnessIsWhetherTheFlightLands()
    {
        float height = Reachable();
        var still = new JumpSearchSettings { StartJitter = 0f, ViewJitter = 0f, ShotJitter = 0, Trials = 5 };
        JumpSearch search = Search(LedgeField(height), still);
        JumpAsk ask = OntoTheLedge(height);
        JumpRecipe hit = Found(Search(LedgeField(height)), ask).Recipe;
        int flights = 0;
        Assert.Equal(1f, search.Robustness(ask, hit, ref flights));
        Assert.Equal(0f, search.Robustness(ask, hit with { Pitch = -80f }, ref flights));
        Assert.Equal(10, flights);
    }

    [Fact]
    public void EachPerturbationReachesTheFlight()
    {
        // One perturbation at a time, far past the plan's: a start hundreds of units off, a view tens of degrees off, a
        // shot a second off. Each alone loses trials that the unperturbed recipe makes.
        float height = Reachable();
        JumpAsk ask = OntoTheLedge(height);
        JumpRecipe hit = Found(Search(LedgeField(height)), ask).Recipe;
        var still = new JumpSearchSettings { StartJitter = 0f, ViewJitter = 0f, ShotJitter = 0 };
        foreach (JumpSearchSettings one in (JumpSearchSettings[])[still with { StartJitter = 600f }, still with { ViewJitter = 80f }, still with { ShotJitter = 60 }])
        {
            int flights = 0;
            float robustness = Search(LedgeField(height), one).Robustness(ask, hit, ref flights);
            Assert.True(robustness < 1f, $"start {one.StartJitter}, view {one.ViewJitter}, shot {one.ShotJitter}: {robustness}");
        }
    }

    [Fact]
    public void TheAnswersRobustnessSecondsAndHealthAreItsOwnFlights()
    {
        float height = Reachable();
        JumpSearch search = Search(LedgeField(height));
        JumpAsk ask = OntoTheLedge(height);
        JumpFound found = Found(search, ask);
        JumpOutcome flown = search.Fly(found.Recipe);
        Assert.Equal((flown.Seconds, flown.AirSeconds, flown.BlastDamage), (found.Seconds, found.AirSeconds, found.Health));
        Assert.True(found.Health > 0f && found.AirSeconds > 0f && found.Seconds > found.AirSeconds);
        Assert.Equal(Flat(flown.Rest, ask.Landing), found.Miss);
        int flights = 0;
        Assert.Equal(search.Robustness(ask, found.Recipe, ref flights), found.Robustness);
        Assert.True(found.Robustness >= search.Settings.Keep, $"robustness {found.Robustness}");
    }

    [Fact]
    public void ItSpendsNoMoreFlightsThanItsBound()
    {
        float height = Reachable();
        JumpAsk ask = OntoTheLedge(height);
        JumpSearchResult whole = Search(LedgeField(height)).Find(ask);
        Assert.InRange(whole.Flights, 60, new JumpSearchSettings().MaxFlights);
        foreach (int bound in (int[])[0, 10, 40])
        {
            JumpSearchResult cut = Search(LedgeField(height), new JumpSearchSettings { MaxFlights = bound }).Find(ask);
            Assert.InRange(cut.Flights, bound == 0 ? 0 : 1, bound);
        }
    }

    [Fact]
    public void AnUpJumpStandsAndAForwardJumpRuns()
    {
        // On the flat, a third of the way the furthest flight goes: both families get there.
        float along = MathF.Round(Envelope().Range / 3f);
        var settings = new JumpSearchSettings();
        JumpSearch search = Search(Field());
        var ask = new JumpAsk(OnTheField(), OnTheField() + new Vector(along, 0f, 0f), 24f);
        JumpFound up = Found(search, ask with { Families = JumpFamilies.Up }), forward = Found(search, ask with { Families = JumpFamilies.Forward });
        Assert.Equal((JumpFamily.Up, 0f), (up.Family, up.Recipe.RunUp));
        Assert.Equal((JumpFamily.Forward, settings.Forward.RunUp), (forward.Family, forward.Recipe.RunUp));
        Assert.InRange(up.Recipe.Pitch, settings.Up.PitchFrom - settings.Up.PitchStep, settings.Up.PitchTo + settings.Up.PitchStep);
        Assert.InRange(forward.Recipe.Pitch, settings.Forward.PitchFrom - settings.Forward.PitchStep, settings.Forward.PitchTo + settings.Forward.PitchStep);
        Assert.Null(search.Find(ask with { Families = JumpFamilies.None }).Found);
    }

    /// <summary>The field with a wall across it from x = 0 to <paramref name="wall"/>.</summary>
    private static VoxelColumns Walled(float wall) => VoxelColumns.Of(0f, 0f, 16f, 256, 128, (x, _) => (x + 1) * 16f <= wall ? [] : [new FreeSpan(0f, 8192f)]);

    [Fact]
    public void ARunUpWithNoRoomBehindIsHalvedAndThenGivenUp()
    {
        float along = MathF.Round(Envelope().Range / 3f), runUp = new JumpSearchSettings().Forward.RunUp;
        // The wall's face at 480. A body 24 wide each way stands from 504 on: a takeoff 0.75 of the run-up past that has room for half.
        var roomForHalf = new JumpAsk(new Vector(504f + 0.75f * runUp, 1024f, VoxelMoveWorld.DistEpsilon), new Vector(504f + 0.75f * runUp + along, 1024f, VoxelMoveWorld.DistEpsilon), 24f, JumpFamilies.Forward);
        Assert.Equal(runUp * 0.5f, Found(Search(Walled(480f)), roomForHalf).Recipe.RunUp);
        // The control: with the wall gone the same ask runs the whole run-up.
        Assert.Equal(runUp, Found(Search(Field()), roomForHalf).Recipe.RunUp);
        // A quarter of the run-up from the wall there is room for neither: no forward jump, and no flight spent finding out.
        var noRoom = roomForHalf with { Takeoff = new Vector(504f + 0.25f * runUp, 1024f, VoxelMoveWorld.DistEpsilon) };
        Assert.Equal(new JumpSearchResult(null, 0), Search(Walled(480f)).Find(noRoom));
    }

    [Fact]
    public void ATakeoffInAWallOrOverNothingYieldsNoneAndSpendsNoFlight()
    {
        float height = Reachable();
        JumpAsk ask = OntoTheLedge(height);
        Assert.Equal(new JumpSearchResult(null, 0), Search(Walled(LedgeEdge)).Find(ask with { Landing = new Vector(LedgeEdge + 200f, 1024f, VoxelMoveWorld.DistEpsilon) }));
        Assert.Equal(new JumpSearchResult(null, 0), Search(LedgeField(height)).Find(ask with { Takeoff = ask.Takeoff + new Vector(0f, 0f, 300f) }));
    }

    [Fact]
    public void ARestIsMadeOnlyStandingOnTheLandingsFloorWithinTheTolerance()
    {
        JumpSearch search = Search(Field());
        var ask = new JumpAsk(OnTheField(), new Vector(1000f, 1024f, 200f), 24f);
        var made = new JumpOutcome(true, ask.Landing, ask.Landing + new Vector(10f, -10f, 5f), true, 2f, 1.5f, 300f, 30f, 1, 150);
        Assert.True(search.Made(ask, made));
        Assert.False(search.Made(ask, made with { Flew = false }));
        Assert.False(search.Made(ask, made with { OnGround = false }));
        Assert.False(search.Made(ask, made with { Rest = ask.Landing + new Vector(20f, 20f, 0f) }));
        // On the floor under a ledge, right under the landing: not on it.
        Assert.False(search.Made(ask, made with { Rest = ask.Landing - new Vector(0f, 0f, search.Settings.HeightTolerance + 1f) }));
        Assert.True(search.Made(ask, made with { Rest = ask.Landing - new Vector(0f, 0f, search.Settings.HeightTolerance - 1f) }));
    }

    private static JumpFound Measured(float seconds, float robustness) => new(default, JumpFamily.Up, robustness, seconds, seconds, 30f, null, 0f);

    [Fact]
    public void TheAnswerIsTheLeastSecondsOverRobustnessAmongThoseWorthKeeping()
    {
        // A failed jump is jumped again: 2.7 s at nine in ten is 3.0 s a landing, under 3.2 s at ten in ten.
        JumpFound quick = Measured(2.7f, 0.9f), sure = Measured(3.2f, 1f), fragile = Measured(1.5f, 0.5f), hopeless = Measured(1f, 0.1f);
        Assert.Equal(3f, JumpSearch.Cost(2.7f, 0.9f), 4);
        Assert.Equal(float.PositiveInfinity, JumpSearch.Cost(2f, 0f));
        Assert.Equal(quick, JumpSearch.Better(sure, quick, 0.6f));
        Assert.Equal(quick, JumpSearch.Better(quick, sure, 0.6f));
        // One worth keeping over one that is not, however quick the other; between two that are not, the more robust.
        Assert.Equal(sure, JumpSearch.Better(fragile, sure, 0.6f));
        Assert.Equal(sure, JumpSearch.Better(sure, fragile, 0.6f));
        Assert.Equal(fragile, JumpSearch.Better(hopeless, fragile, 0.6f));
        Assert.Equal(fragile, JumpSearch.Better(fragile, hopeless, 0.6f));
        Assert.Equal(fragile, JumpSearch.Better(null, fragile, 0.6f));
    }

    [Fact]
    public void AHitsNeighboursAreTheCellsAStepAwayThatHitToo()
    {
        // Three pitches by four delays; the hits an L and a lone corner.
        bool[] made =
        [
            true, true, false, false,
            true, false, false, false,
            true, false, false, true,
        ];
        Assert.Equal(2, JumpSearch.Neighbours(made, 3, 4, 0));
        Assert.Equal(1, JumpSearch.Neighbours(made, 3, 4, 1));
        Assert.Equal(2, JumpSearch.Neighbours(made, 3, 4, 4));
        Assert.Equal(1, JumpSearch.Neighbours(made, 3, 4, 8));
        // The grid's edge is no neighbour, and a row's end is not the next row's start (cell 4 hits, and is none of cell 3's).
        Assert.Equal(0, JumpSearch.Neighbours(made, 3, 4, 11));
        Assert.Equal(0, JumpSearch.Neighbours(made, 3, 4, 3));
        Assert.Equal(1, JumpSearch.Neighbours(made, 3, 4, 7));
    }

    [Fact]
    public void ATrialIsDrawnWithinThePlansBoundsAndUsesThem()
    {
        var settings = new JumpSearchSettings();
        Assert.Equal((16f, 3f, 2), (settings.StartJitter, settings.ViewJitter, settings.ShotJitter));
        uint state = 1;
        var trials = new List<JumpSearch.Trial>();
        for (int i = 0; i < 2000; i++)
            trials.Add(JumpSearch.Draw(ref state, settings));
        Assert.All(trials, t =>
        {
            Assert.InRange(t.StartX, -16f, 16f);
            Assert.InRange(t.StartY, -16f, 16f);
            Assert.InRange(t.Pitch, -3f, 3f);
            Assert.InRange(t.Yaw, -3f, 3f);
            Assert.InRange(t.Shot, -2, 2);
        });
        // Each part goes nearly to its bound both ways, and no two parts are one draw.
        Assert.True(trials.Min(t => t.StartX) < -15f && trials.Max(t => t.StartX) > 15f && trials.Min(t => t.StartY) < -15f && trials.Max(t => t.StartY) > 15f);
        Assert.True(trials.Min(t => t.Pitch) < -2.8f && trials.Max(t => t.Pitch) > 2.8f && trials.Min(t => t.Yaw) < -2.8f && trials.Max(t => t.Yaw) > 2.8f);
        Assert.Equal([-2, -1, 0, 1, 2], trials.Select(t => t.Shot).Distinct().Order());
        Assert.Contains(trials, t => MathF.Abs(t.StartX - t.StartY) > 8f);
        Assert.Contains(trials, t => MathF.Abs(t.Pitch - t.Yaw) > 2f);
        // The settings' bounds are the draw's: twice the bound, twice the spread.
        state = 1;
        JumpSearch.Trial wide = JumpSearch.Draw(ref state, settings with { StartJitter = 32f, ViewJitter = 6f });
        Assert.Equal((trials[0].StartX * 2f, trials[0].StartY * 2f, trials[0].Pitch * 2f, trials[0].Yaw * 2f), (wide.StartX, wide.StartY, wide.Pitch, wide.Yaw));
    }

    [Fact]
    public void ATrialFliesTheRecipeFromAStartAndWithAShotThatAreOff()
    {
        var recipe = new JumpRecipe(OnTheField(), OnTheField() + new Vector(0f, 400f, 0f), 100f, 60f, 180f, 2, true);
        (JumpRecipe flown, JumpStart start) = JumpSearch.Perturbed(recipe, new JumpSearch.Trial(5f, -7f, 2f, -3f, -4));
        Assert.Equal(recipe with { Pitch = 62f, YawOffset = 177f, ShotDelay = -2 }, flown);
        Assert.Equal(recipe.Start() with { Origin = recipe.Start().Origin + new Vector(5f, -7f, 0f) }, start);
        // The view cannot look steeper than the engine lets it.
        Assert.Equal(JumpSearch.MaxPitch, JumpSearch.Perturbed(recipe with { Pitch = 88f }, new JumpSearch.Trial(0f, 0f, 3f, 0f, 0)).Recipe.Pitch);
    }

    [Fact]
    public void WithNoCoarseHitItLooksRoundTheNearestMiss()
    {
        // A coarse grid of one cell, 40 degrees shallower than a recipe that works: its rocket goes off down the field.
        // Half its pitch step is those 40 degrees, so the refinement round the miss holds the recipe that works.
        float height = Reachable();
        JumpAsk ask = OntoTheLedge(height, families: JumpFamilies.Up);
        JumpRecipe works = Found(Search(LedgeField(height)), ask).Recipe;
        var oneCell = new JumpSearchSettings { Up = new JumpGrid(0f, works.Pitch - 40f, works.Pitch - 40f, 80f, works.ShotDelay, works.ShotDelay, 2) };
        JumpSearch search = Search(LedgeField(height), oneCell);
        Assert.False(search.Made(ask, search.Fly(works with { Pitch = works.Pitch - 40f })), "the control: the one coarse cell misses");
        JumpSearchResult result = search.Find(ask);
        Assert.NotNull(result.Found);
        Assert.True(search.Made(ask, search.Fly(result.Found!.Value.Recipe)));
        Assert.InRange(result.Found.Value.Recipe.Pitch, works.Pitch - oneCell.Up.PitchStep * 0.5f, works.Pitch + oneCell.Up.PitchStep * 0.5f);
    }

    [Fact]
    public void TheFirstHitMeasuredIsTheQuickestWeighedByItsNeighbours()
    {
        // One hit measured and no flights left to refine with: the answer is the hit the order puts first. Over ledges
        // of several heights and standoffs, so that somewhere the first is not simply the quickest.
        var settings = new JumpSearchSettings { Measured = 1 };
        int coarse = settings.Up.Pitches * settings.Up.Delays + settings.Forward.Pitches * settings.Forward.Delays;
        settings = settings with { MaxFlights = coarse + settings.Trials };
        float rise = Envelope().Rise;
        int asked = 0;
        foreach (float share in (float[])[0.15f, 0.35f, 0.5f])
        {
            foreach (float standoff in (float[])[160f, 320f])
            {
                float height = MathF.Round(share * rise);
                JumpSearch search = Search(LedgeField(height), settings);
                JumpAsk ask = OntoTheLedge(height, standoff);
                JumpSearchResult result = search.Find(ask);

                // The grids again by hand.
                (JumpRecipe Recipe, float Likely)? first = null;
                foreach (JumpGrid grid in (JumpGrid[])[settings.Up, settings.Forward])
                {
                    var recipes = new JumpRecipe[grid.Pitches * grid.Delays];
                    var outcomes = new JumpOutcome[recipes.Length];
                    for (int i = 0; i < recipes.Length; i++)
                    {
                        recipes[i] = new JumpRecipe(ask.Takeoff, ask.Landing, grid.RunUp, grid.PitchFrom + i / grid.Delays * grid.PitchStep, settings.YawOffset,
                                                    grid.DelayFrom + i % grid.Delays * grid.DelayStep, true);
                        outcomes[i] = search.Fly(recipes[i]);
                    }

                    bool[] made = [.. outcomes.Select(o => search.Made(ask, o))];
                    for (int i = 0; i < recipes.Length; i++)
                    {
                        float likely = JumpSearch.Likely(outcomes[i].Seconds, JumpSearch.Neighbours(made, grid.Pitches, grid.Delays, i));
                        if (made[i] && (first is null || likely < first.Value.Likely))
                            first = (recipes[i], likely);
                    }
                }

                if (first is null)
                    continue;

                asked++;
                Assert.Equal(coarse + settings.Trials, result.Flights);
                Assert.Equal(first.Value.Recipe, result.Found!.Value.Recipe);
            }
        }

        Assert.True(asked >= 3, $"the control: {asked} of the asks had a coarse hit");
    }

    [Fact]
    public void AHitInTheMiddleOfWhatWorksIsMeasuredBeforeAQuickerOneAtItsEdge()
    {
        Assert.Equal(3.2f, JumpSearch.Likely(3.2f, 4), 4);
        Assert.Equal(16f, JumpSearch.Likely(3.2f, 0), 4);
        Assert.True(JumpSearch.Likely(3.2f, 4) < JumpSearch.Likely(2.7f, 2));
        Assert.True(JumpSearch.Likely(2.7f, 4) < JumpSearch.Likely(3.2f, 4));
    }

    [Fact]
    public void NoQuickerBetterRecipeIsHalfAPitchStepFromTheAnswer()
    {
        // Wherever the search had flights left to go on refining, the answer's neighbours half a pitch step away are
        // no better: none hits, is quicker and costs less seconds over robustness.
        float height = Reachable(), along = MathF.Round(Envelope().Range / 3f);
        var settings = new JumpSearchSettings();
        int refined = 0;
        foreach ((VoxelColumns world, JumpAsk ask) in ((VoxelColumns, JumpAsk)[])
                 [
                     (LedgeField(height), OntoTheLedge(height, 96f)), (LedgeField(height), OntoTheLedge(height, 160f)), (LedgeField(height), OntoTheLedge(height, 320f)),
                     (Field(), new JumpAsk(OnTheField(), OnTheField() + new Vector(along, 0f, 0f), 24f)),
                     (Field(), new JumpAsk(OnTheField(), OnTheField() + new Vector(2f * along, 0f, 0f), 24f)),
                 ])
        {
            JumpSearch search = Search(world, settings);
            JumpSearchResult result = search.Find(ask);
            if (result.Found is not { } found || result.Flights + 1 + settings.Trials > settings.MaxFlights)
                continue;
            refined++;
            float half = (found.Family == JumpFamily.Up ? settings.Up : settings.Forward).PitchStep * 0.5f;
            foreach (float pitch in (float[])[found.Recipe.Pitch - half, found.Recipe.Pitch + half])
            {
                JumpRecipe beside = found.Recipe with { Pitch = pitch };
                JumpOutcome outcome = search.Fly(beside);
                if (pitch > JumpSearch.MaxPitch || !search.Made(ask, outcome) || outcome.Seconds >= found.Seconds)
                    continue;
                int flights = 0;
                float robustness = search.Robustness(ask, beside, ref flights);
                Assert.False(robustness >= settings.Keep && JumpSearch.Cost(outcome.Seconds, robustness) < JumpSearch.Cost(found.Seconds, found.Robustness),
                             $"pitch {pitch} beside {found.Recipe.Pitch}: {outcome.Seconds} s at {robustness} against {found.Seconds} s at {found.Robustness}");
            }
        }

        Assert.True(refined >= 3, $"the control: {refined} searches had flights left to refine with");
    }

    private sealed class WalkOf(float? seconds) : IJumpWalk
    {
        public List<(Vector From, Vector To)> Asked { get; } = [];

        public float? Seconds(Vector from, Vector to)
        {
            Asked.Add((from, to));
            return seconds;
        }
    }

    [Fact]
    public void TheWalkBetweenTheSamePlacesIsSetBesideTheJump()
    {
        float height = Reachable();
        JumpAsk ask = OntoTheLedge(height);
        var walk = new WalkOf(7.5f);
        Assert.Equal(7.5f, Found(Search(LedgeField(height), walk: walk), ask).WalkSeconds);
        Assert.Equal([(ask.Takeoff, ask.Landing)], walk.Asked);
        Assert.Null(Found(Search(LedgeField(height), walk: new WalkOf(null)), ask).WalkSeconds);
        Assert.Null(Found(Search(LedgeField(height)), ask).WalkSeconds);
    }

    /// <summary>Three cells in a row, 100 apart, walked both ways; a fourth off on its own.</summary>
    private static NavGraph Row()
    {
        var b = new NavGraphBuilder();
        CellId[] cells = [.. Enumerable.Range(0, 4).Select(i => b.AddCell(new NavCell(i * 100f, 0f, i * 100f + 100f, 100f, i == 3 ? 500f : 0f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f)))];
        b.AddBoth(cells[0], cells[1], NavEdgeType.Walk);
        b.AddBoth(cells[1], cells[2], NavEdgeType.Walk);
        b.AddEdge(cells[3], cells[2], NavEdgeType.Drop, 8f);
        return b.Build();
    }

    [Fact]
    public void AWalksTimeIsItsCheapestPathAtTheRunSpeed()
    {
        GraphJumpWalk walk = GraphJumpWalk.Of(Row(), 200f);
        Assert.Equal(1f, walk.Seconds(new Vector(50f, 50f, 0f), new Vector(250f, 50f, 0f))!.Value, 4);
        Assert.Equal(200f, walk.Cost(new CellId(0), new CellId(2))!.Value, 3);
        Assert.Equal(0f, walk.Seconds(new Vector(50f, 50f, 0f), new Vector(60f, 60f, 0f)));
    }

    [Fact]
    public void AWalkWithNoWayOrNoCellIsNone()
    {
        GraphJumpWalk walk = GraphJumpWalk.Of(Row(), 200f);
        // Down from the fourth cell there is a way (a drop); up to it there is none.
        Assert.NotNull(walk.Seconds(new Vector(350f, 50f, 500f), new Vector(50f, 50f, 0f)));
        Assert.Null(walk.Seconds(new Vector(50f, 50f, 0f), new Vector(350f, 50f, 500f)));
        Assert.Null(walk.Seconds(new Vector(50f, 50f, 0f), new Vector(950f, 50f, 0f)));
        Assert.Null(walk.Seconds(new Vector(-950f, 50f, 0f), new Vector(50f, 50f, 0f)));
    }

    [Fact]
    public void AWayOnFootIsFollowedTheWayTheEdgesGo()
    {
        GraphJumpWalk walk = GraphJumpWalk.Of(Row(), 200f);
        CellId[] c = [.. Enumerable.Range(0, 4).Select(i => new CellId(i))];
        // Down the drop and along, not up it; and asked of one cell, then another, then the first again.
        Assert.True(walk.Reaches(c[3], c[0]));
        Assert.False(walk.Reaches(c[0], c[3]));
        Assert.True(walk.Reaches(c[0], c[2]));
        Assert.False(walk.Reaches(c[1], c[3]));
        Assert.True(walk.Reaches(c[3], c[3]));
        Assert.True(walk.Reaches(c[2], c[0]));
        Assert.Null(walk.Cost(c[0], c[3]));
        Assert.NotNull(walk.Cost(c[3], c[0]));
    }
}
