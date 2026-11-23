using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.Ai.Tests.Nav;
using SourceSharp.Ai.Tests.Senses;
using TF2.Server.Objectives;
using TF2.Server.Rules;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// Each bot's own way (lane Groutes, <see cref="TFRouteCorridors"/>, <see cref="TFRouteVariation"/>): the distinct ways to
/// a goal no dearer than 1.15 times the shortest are its corridors; each life of each bot takes one and keeps to it, its
/// searches minding a field that makes a step off the corridor 1.15 times as dear; teammates arriving together take
/// different corridors, a bot alone draws as before; a way more than 1.15 times the shortest is
/// never one and never taken; a life asks the same field each time; a lone wolf strays more often than a team player; the
/// switch off, or a player that is no bot of ours, leaves the team's hazard as it was.
/// </summary>
public sealed class TFRouteVariationTests
{
    private const int Blue = (int)TFTeam.Blue;

    /// <summary>
    /// A loop of corridors <paramref name="width"/> cells across: a top row and a bottom row <paramref name="height"/> rows
    /// apart, joined by the left and right columns; the start in the left column and the goal in the right, both
    /// <paramref name="row"/> rows down. The way over the top is 2·row + width − 1 steps, the way under the bottom
    /// 2·(height − 1 − row) + width − 1.
    /// </summary>
    private static (NavGraph Graph, CellId Start, CellId Goal, HashSet<int> Bottom) Loop(int width, int height, int row)
    {
        var rows = new string[height];
        for (int y = 0; y < height; y++)
            rows[y] = y == 0 || y == height - 1 ? new string('.', width) : "." + new string(' ', width - 2) + ".";
        var (g, ids) = Grids.Parse(rows);
        var bottom = new HashSet<int>();
        for (int x = 1; x < width - 1; x++)
            bottom.Add(ids[x, height - 1]);
        return (g, Grids.At(ids, 0, row), Grids.At(ids, width - 1, row), bottom);
    }

    private static NavObstacleState Plain(NavGraph g) => new(g.ObstacleCount);

    /// <summary>The way a bot takes from <paramref name="start"/> minding <paramref name="extra"/> (none: no field): whether it goes under the bottom, and its steps over the shortest's.</summary>
    private static (bool Under, float Ratio) Way(NavGraph g, CellId start, CellId goal, HashSet<int> bottom, float[]? extra)
    {
        var shortest = new FlowField();
        shortest.Build(g, goal, ClearanceClass.Stand, Plain(g));
        NavObstacleState minding = Plain(g);
        if (extra is not null)
            (minding.Hazard, minding.HazardWeight) = (TFRouteVariation.Compose(g, extra, null, 0f), 1f);
        var field = new FlowField();
        field.Build(g, goal, ClearanceClass.Stand, minding);
        List<int> cells = Grids.Walk(field, start.Index);
        Assert.Equal(goal.Index, cells[^1]);
        // Every step of these grids costs the same with no field: a walk's steps are its cost.
        return (cells.Any(bottom.Contains), (cells.Count - 1f) / (Grids.Walk(shortest, start.Index).Count - 1f));
    }

    private static IEnumerable<(int Bot, int Life)> Lives(int bots, int lives)
        => Enumerable.Range(1, bots).SelectMany(b => Enumerable.Range(1, lives).Select(l => (b, l)));

    private static int Draw(IReadOnlyList<RouteCorridor> corridors, int bot, int life, float teamwork = 0.3f)
        => TFRouteVariation.Choose(corridors, AiRandom.SeedFor(TFRouteVariation.SeedOf(bot, life), 7), teamwork);

    [Fact]
    public void TheCorridorsOfTwoEqualWaysAreBothWays()
    {
        // Over the top 8 + 39 + 8 steps, under the bottom the same.
        var (g, start, goal, bottom) = Loop(40, 17, 8);
        IReadOnlyList<RouteCorridor> corridors = TFRouteCorridors.Find(g, start, goal, Plain(g));
        Assert.Equal(2, corridors.Count);
        Assert.All(corridors, c => Assert.Equal(1f, c.Ratio));
        // One corridor runs under the bottom, the other over the top.
        Assert.Single(corridors, c => bottom.All(cell => c.Extra[cell] == 0f));
        Assert.Single(corridors, c => bottom.All(cell => c.Extra[cell] == TFRouteVariation.MaxExtra));
    }

    [Fact]
    public void OfTwoEqualWaysBotsAndLivesFromOneStartTakeBothEachTheOneItDrew()
    {
        var (g, start, goal, bottom) = Loop(40, 17, 8);
        IReadOnlyList<RouteCorridor> corridors = TFRouteCorridors.Find(g, start, goal, Plain(g));
        int under = 0, over = 0;
        foreach ((int bot, int life) in Lives(8, 4))
        {
            RouteCorridor drawn = corridors[Draw(corridors, bot, life)];
            (bool way, float ratio) = Way(g, start, goal, bottom, drawn.Extra);
            Assert.Equal(bottom.All(cell => drawn.Extra[cell] == 0f), way);
            Assert.True(ratio <= TFRouteCorridors.Cap, $"{ratio}");
            (under, over) = way ? (under + 1, over) : (under, over + 1);
        }

        Assert.InRange(under, 6, 26);
        Assert.InRange(over, 6, 26);
        // Two lives of one bot: some bot takes the top in one life and the bottom in another.
        Assert.Contains(Enumerable.Range(1, 8), b => Enumerable.Range(1, 4).Select(l => Draw(corridors, b, l)).Distinct().Count() == 2);
        // The control: with no field every bot takes the one way.
        Assert.Single(Lives(8, 4).Select(_ => Way(g, start, goal, bottom, null).Under).Distinct());
    }

    [Fact]
    public void AWayAFewPercentLongerIsACorridorAndABotThatDrewItKeepsToIt()
    {
        // Over the top 6 + 39 + 6 = 51 steps, under the bottom 7 + 39 + 7 = 53: 1.039 times.
        var (g, start, goal, bottom) = Loop(40, 14, 6);
        IReadOnlyList<RouteCorridor> corridors = TFRouteCorridors.Find(g, start, goal, Plain(g));
        Assert.Equal(2, corridors.Count);
        Assert.Equal(1f, corridors[0].Ratio);
        Assert.Equal(53f / 51f, corridors[1].Ratio, 3);
        (bool under, float ratio) = Way(g, start, goal, bottom, corridors[1].Extra);
        Assert.True(under);
        Assert.Equal(53f / 51f, ratio, 3);
        Assert.False(Way(g, start, goal, bottom, corridors[0].Extra).Under);
    }

    /// <summary>The cap raised to 1.15 (the user, 2026-11-20): a way 1.109 times the shortest is a corridor, and kept to.</summary>
    [Fact]
    public void AWayBetweenOnePointOneAndOnePointOneFiveTimesIsACorridorAndKeptTo()
    {
        // Over the top 8 + 39 + 8 = 55 steps, under the bottom 11 + 39 + 11 = 61: 1.109 times.
        var (g, start, goal, bottom) = Loop(40, 20, 8);
        IReadOnlyList<RouteCorridor> corridors = TFRouteCorridors.Find(g, start, goal, Plain(g));
        Assert.Equal(2, corridors.Count);
        Assert.Equal(61f / 55f, corridors[1].Ratio, 3);
        (bool under, float ratio) = Way(g, start, goal, bottom, corridors[1].Extra);
        Assert.True(under);
        Assert.Equal(61f / 55f, ratio, 3);
    }

    [Fact]
    public void TheCapHoldsWhereTheOnlyOtherWayIsLong()
    {
        // Over the top 6 + 39 + 6 = 51 steps, under the bottom 10 + 39 + 10 = 59: 1.157 times, over the cap of 1.15.
        var (g, start, goal, bottom) = Loop(40, 17, 6);
        Assert.Single(TFRouteCorridors.Find(g, start, goal, Plain(g)));
        // Even a field that makes the whole top 1.15 times as dear does not send a bot under the bottom.
        float[] dearTop = new float[g.CellCount];
        for (int c = 0; c < dearTop.Length; c++)
            dearTop[c] = bottom.Contains(c) ? 0f : TFRouteVariation.MaxExtra;
        (bool under, float ratio) = Way(g, start, goal, bottom, dearTop);
        Assert.False(under);
        Assert.Equal(1f, ratio);
        // The control: a bottom only 1.039 times as long is taken under the same field.
        var (g2, start2, goal2, bottom2) = Loop(40, 14, 6);
        float[] dearTop2 = new float[g2.CellCount];
        for (int c = 0; c < dearTop2.Length; c++)
            dearTop2[c] = bottom2.Contains(c) ? 0f : TFRouteVariation.MaxExtra;
        Assert.True(Way(g2, start2, goal2, bottom2, dearTop2).Under);
    }

    /// <summary>
    /// Live on cp_granary the first way a search found past the shortest was over the cap, and the corridors stopped there;
    /// a cheaper one lay past it. Here: the shortest along the bottom row (199 steps); a far row (239, 1.2 times), which the
    /// next search takes first, sharing nothing; and a bump 9 rows up over 80 cells (217, 1.09 times), which shares most of
    /// the shortest and is found after.
    /// </summary>
    [Fact]
    public void AWayWithinTheCapIsFoundPastOneOverIt()
    {
        var rows = new string[21];
        for (int y = 0; y < rows.Length; y++)
        {
            var row = new char[200];
            for (int x = 0; x < row.Length; x++)
                row[x] = y is 0 or 20 || x is 0 or 199 || (y == 11 && x is >= 60 and <= 140) || (y > 11 && x is 60 or 140) ? '.' : ' ';
            rows[y] = new string(row);
        }

        var (g, ids) = Grids.Parse(rows);
        var tries = new List<(float Ratio, float Own)>();
        IReadOnlyList<RouteCorridor> corridors = TFRouteCorridors.Find(g, Grids.At(ids, 0, 20), Grids.At(ids, 199, 20), Plain(g), default, tries);
        Assert.Equal(239f / 199f, tries[0].Ratio, 3);
        Assert.True(corridors.Count == 2 && MathF.Abs(corridors[1].Ratio - (217f / 199f)) < 1e-3f,
                    string.Join(" ", tries.Select(t => $"{t.Ratio:F3}/{t.Own:F2}")));
    }

    [Fact]
    public void EachStepUnderTheFieldCostsFromItsOwnToOnePointOneTimesIt()
    {
        var (g, start, goal, _) = Loop(40, 17, 8);
        float[] seconds = new float[g.CellCount];
        for (int c = 0; c < seconds.Length; c++)
            seconds[c] = c % 7 * 0.6f;
        var team = new NavCellHazard(g, seconds);
        float[] extra = TFRouteCorridors.Find(g, start, goal, Plain(g))[0].Extra;
        foreach (float weight in (float[])[0f, 0.3f, 1f])
        {
            NavCellHazard composed = TFRouteVariation.Compose(g, extra, weight > 0f ? team : null, weight);
            int dearer = 0;
            for (int c = 0; c < g.CellCount; c++)
            {
                float own = 1f + (weight > 0f ? team.Scale(c, weight) : 0f), now = 1f + composed.Scale(c, 1f);
                Assert.InRange(now, own - 1e-5f, (own * (1f + TFRouteVariation.MaxExtra)) + 1e-5f);
                dearer += now > own + 1e-3f ? 1 : 0;
            }

            // The stimulus reached the cells: the field made those off the corridor dearer.
            Assert.True(dearer > g.CellCount / 4, $"{dearer} of {g.CellCount}");
        }
    }

    [Fact]
    public void AFieldAskedForMoreThanTheCapAddsTheCap()
    {
        NavGraph g = Loop(40, 17, 8).Graph;
        float[] extra = new float[g.CellCount];
        Array.Fill(extra, 0.5f);
        NavCellHazard composed = TFRouteVariation.Compose(g, extra, null, 0f);
        Assert.All(Enumerable.Range(0, g.CellCount), c => Assert.Equal(TFRouteVariation.MaxExtra, composed.Scale(c, 1f), 5));
    }

    [Fact]
    public void ALoneWolfStraysToALongerCorridorMoreOftenThanATeamPlayer()
    {
        var corridors = new[] { new RouteCorridor(1f, [], []), new RouteCorridor(1.05f, [], []) };
        int Strays(float teamwork) => Lives(40, 10).Count(x => Draw(corridors, x.Bot, x.Life, teamwork) == 1);
        int wolf = Strays(0f), player = Strays(1f);
        // e^-0.5 against 1 (38 % of 400) and e^-1.5 against 1 (18 %).
        Assert.InRange(wolf, 120, 185);
        Assert.InRange(player, 45, 100);
    }

    /// <summary>A bot with no teammate arriving with it draws as a lone bot did before the together rule.</summary>
    [Fact]
    public void ABotWithNoTeammateArrivingWithItDrawsAsALoneBot()
    {
        var corridors = new[] { new RouteCorridor(1f, [], []), new RouteCorridor(1.05f, [], []), new RouteCorridor(1.12f, [], []) };
        foreach ((int bot, int life) in Lives(16, 8))
        {
            ulong seed = AiRandom.SeedFor(TFRouteVariation.SeedOf(bot, life), 7);
            Assert.Equal(TFRouteVariation.Choose(corridors, seed, 0.3f), TFRouteVariation.Choose(corridors, seed, 0.3f, [0, 0, 0]));
        }
    }

    /// <summary>A wave of more bots than corridors fills the corridors evenly: each next bot takes one of the least taken.</summary>
    [Fact]
    public void AWaveOfMoreBotsThanCorridorsFillsThemEvenly()
    {
        var corridors = new[] { new RouteCorridor(1f, [], []), new RouteCorridor(1.1f, [], []), new RouteCorridor(1.14f, [], []) };
        foreach (int life in Enumerable.Range(1, 20))
        {
            var taken = new int[3];
            for (int bot = 1; bot <= 7; bot++)
                taken[TFRouteVariation.Choose(corridors, AiRandom.SeedFor(TFRouteVariation.SeedOf(bot, life), 7), 1f, taken)]++;
            Assert.True(taken.Max() - taken.Min() <= 1, string.Join(",", taken));
        }
    }

    [Fact]
    public void ABotsDrawIsItsSeedsAndTheSameAgainForThem()
    {
        var corridors = new[] { new RouteCorridor(1f, [], []), new RouteCorridor(1f, [], []), new RouteCorridor(1f, [], []) };
        int[] draws = [.. Lives(8, 8).Select(x => Draw(corridors, x.Bot, x.Life))];
        Assert.Equal(draws, Lives(8, 8).Select(x => Draw(corridors, x.Bot, x.Life)));
        Assert.Equal(3, draws.Distinct().Count());
        Assert.NotEqual(TFRouteVariation.SeedOf(2, 1), TFRouteVariation.SeedOf(2, 2));
        Assert.NotEqual(TFRouteVariation.SeedOf(2, 1), TFRouteVariation.SeedOf(3, 1));
    }

    // ---------------------------------------------------------------- the service: lives, the switch, the profile

    /// <summary>
    /// The services TF2's server list registers that <paramref name="from"/> needs, through every constructor parameter of
    /// every implementation registered for it (an optional one too: the container fills it), itself included.
    /// </summary>
    private static HashSet<Type> Needs(Type from)
    {
        var implementations = new Dictionary<Type, List<Type>>();
        foreach (Type t in (Type[])[.. typeof(global::TF2.Server.Player.TFServerActorFacts).Assembly.GetTypes(), .. typeof(AiBots).Assembly.GetTypes(),
                                    .. typeof(TFRouteVariation).Assembly.GetTypes()])
        {
            foreach (SideServiceAttribute a in t.GetCustomAttributes(typeof(SideServiceAttribute), false).Cast<SideServiceAttribute>().Where(a => a.Side == Side.Server))
            {
                foreach (Type key in (Type[])[t, .. a.As])
                {
                    if (!implementations.TryGetValue(key, out List<Type>? list))
                        implementations[key] = list = [];
                    list.Add(t);
                }
            }
        }

        var seen = new HashSet<Type>();
        var open = new Stack<Type>([from]);
        while (open.TryPop(out Type? t))
        {
            if (!seen.Add(t))
                continue;
            foreach (Type impl in implementations.GetValueOrDefault(t) ?? [t])
            {
                foreach (System.Reflection.ConstructorInfo c in impl.GetConstructors())
                {
                    foreach (System.Reflection.ParameterInfo p in c.GetParameters())
                        open.Push(p.ParameterType);
                }
            }
        }

        return seen;
    }

    /// <summary>
    /// The navigation is built with the profile, and the bots hold the navigation: so nothing the profile needs may need the
    /// bots or the navigation (live, the first wiring's variation held the bots, and the server's composition refused the cycle).
    /// </summary>
    [Fact]
    public void TheNavigationProfileNeedsNothingThatHoldsTheBotsOrTheNavigation()
    {
        HashSet<Type> needs = Needs(typeof(TFNavProfile));
        Assert.Contains(typeof(TFRouteVariation), needs);
        Assert.DoesNotContain(typeof(AiBots), needs);
        Assert.DoesNotContain(typeof(IAiNavigation), needs);
        Assert.DoesNotContain(typeof(AiNav), needs);
        // The control: the closure reaches through: the lives' feeder holds the bots, and through them the navigation.
        Assert.Contains(typeof(IAiNavigation), Needs(typeof(TFRouteLives)));
    }

    private static readonly ActorId Bot = new(4, 0x4000_0001);
    private static readonly EntityHandle Player = new(0x0001_0004u);

    /// <summary>The map's objectives: red's flag at <paramref name="at"/>, blue's far away.</summary>
    private sealed class Flags(AiVector at) : ITFObjectives
    {
        private readonly TFObjective[] _all =
        [
            new(1, TFObjectiveKind.Flag, TFTeam.Red, TFTeam.Unassigned, TFObjectiveState.Home, new Vector(at.X, at.Y, at.Z), 0f, 0, 0f, 0f),
            new(2, TFObjectiveKind.Flag, TFTeam.Blue, TFTeam.Unassigned, TFObjectiveState.Home, new Vector(-9000f, 0f, 0f), 0f, 0, 0f, 0f),
        ];

        public ReadOnlySpan<TFObjective> All => _all;

        public int LastProgressTick(TFTeam team) => 0;

        public int Version => 1;
    }

    private static TFObjectiveFeed Feed(AiVector flag)
        => new(new Flags(flag), FakeEngine.Of<ITFRoundFacts>((m, _) => m.Name == "get_InSetup" ? false : throw FakeEngine.Unasked(m)),
               FakeEngine.Of<IEntityDirectory>((m, _) => m.Name == nameof(IEntityDirectory.OfClass) ? Array.Empty<ScriptedEntity>() : throw FakeEngine.Unasked(m)),
               null!, null!);

    /// <summary>The service over the equal loop, its one bot seated and alive in its first life.</summary>
    private static (TFRouteVariation V, NavGraph G, AiVector Start) Seated(bool on)
    {
        var (g, start, goal, _) = Loop(40, 17, 8);
        Assert.True(SkillPresets.TryGet("normal", out SkillProfile? normal));
        var v = new TFRouteVariation(Feed(g.Cell(goal).Centre));
        v.Seat([new AiBot(Bot, "b", normal!) { SeedIndex = 6 }], on);
        v.Note(Bot.Slot, true);
        return (v, g, g.Cell(start).Centre);
    }

    /// <summary>What the bot's searches mind once its goal's corridors are found.</summary>
    private static async Task<NavHazardAllowance> Asked(TFRouteVariation v, NavGraph g, AiVector at, NavHazardAllowance minded = default)
    {
        v.For(Bot.Slot, g, Blue, minded, Plain(g), at);
        await v.Settled().WaitAsync(TimeSpan.FromSeconds(30));
        return v.For(Bot.Slot, g, Blue, minded, Plain(g), at);
    }

    private static readonly ActorId Mate = new(5, 0x4000_0002);

    private static float NormalTeamwork()
    {
        Assert.True(SkillPresets.TryGet("normal", out SkillProfile? normal));
        return TFRouteVariation.TeamworkOf(normal!);
    }

    /// <summary>The equal loop's service with two bots of the seed indexes given, both alive in their first lives, at 100 s; and the loop's corridors.</summary>
    private static (TFRouteVariation V, NavGraph G, AiVector Start, IReadOnlyList<RouteCorridor> Corridors) Pair(int seedA, int seedB)
    {
        var (g, start, goal, _) = Loop(40, 17, 8);
        Assert.True(SkillPresets.TryGet("normal", out SkillProfile? normal));
        var v = new TFRouteVariation(Feed(g.Cell(goal).Centre));
        v.Seat([new AiBot(Bot, "a", normal!) { SeedIndex = seedA }, new AiBot(Mate, "b", normal!) { SeedIndex = seedB }], true);
        v.Note(Bot.Slot, true);
        v.Note(Mate.Slot, true);
        v.Clock(100d);
        return (v, g, g.Cell(start).Centre, TFRouteCorridors.Find(g, start, goal, Plain(g)));
    }

    private static int ChoiceOf(TFRouteVariation v, ActorId bot) => v.Lives().Single(l => l.Slot == bot.Slot).Choice;

    /// <summary>The lone draw of a bot of <paramref name="seedIndex"/> in its first life, to the loop's goal (id 1).</summary>
    private static int Alone(IReadOnlyList<RouteCorridor> corridors, int seedIndex)
        => TFRouteVariation.Choose(corridors, AiRandom.SeedFor(TFRouteVariation.SeedOf(seedIndex, 1), 1), NormalTeamwork());

    /// <summary>The user, 2026-11-20: "Bots should take different ways to the same objective if arriving at the same time."</summary>
    [Fact]
    public async Task TwoTeammatesLeavingTogetherTakeDifferentCorridors()
    {
        int wouldShare = 0;
        for (int pair = 0; pair < 12; pair++)
        {
            var (v, g, at, corridors) = Pair(1 + pair, 31 + pair);
            Assert.Equal(2, corridors.Count);
            await Asked(v, g, at);
            v.For(Mate.Slot, g, Blue, default, Plain(g), at);
            Assert.True(ChoiceOf(v, Bot) >= 0 && ChoiceOf(v, Mate) >= 0);
            Assert.NotEqual(ChoiceOf(v, Bot), ChoiceOf(v, Mate));
            // The control: drawn alone, the same two lives would have shared a corridor in some pairs.
            wouldShare += Alone(corridors, 1 + pair) == Alone(corridors, 31 + pair) ? 1 : 0;
        }

        Assert.True(wouldShare > 0, "no pair would have shared a corridor drawn alone: the fact shows nothing");
    }

    /// <summary>A teammate who leaves well after another arrives apart from it: it draws as a lone bot.</summary>
    [Fact]
    public async Task ATeammateLeavingWellAfterDrawsAsALoneBot()
    {
        for (int pair = 0; pair < 12; pair++)
        {
            var (v, g, at, corridors) = Pair(1 + pair, 31 + pair);
            await Asked(v, g, at);
            v.Clock(100d + (2 * TFRouteVariation.TogetherSeconds) + 1d);
            v.For(Mate.Slot, g, Blue, default, Plain(g), at);
            Assert.Equal(Alone(corridors, 31 + pair), ChoiceOf(v, Mate));
        }
    }

    /// <summary>A teammate who died frees its corridor: a bot leaving at its old arrival draws as a lone bot.</summary>
    [Fact]
    public async Task ADeadTeammatesCorridorIsFreeAgain()
    {
        for (int pair = 0; pair < 12; pair++)
        {
            var (v, g, at, corridors) = Pair(1 + pair, 31 + pair);
            await Asked(v, g, at);
            v.Note(Bot.Slot, false);
            v.For(Mate.Slot, g, Blue, default, Plain(g), at);
            Assert.Equal(Alone(corridors, 31 + pair), ChoiceOf(v, Mate));
        }
    }

    /// <summary>The first bot's corridor holds when its teammate takes another: its next ask is the same field.</summary>
    [Fact]
    public async Task TheFirstBotsCorridorHoldsWhenItsTeammateTakesAnother()
    {
        var (v, g, at, _) = Pair(1, 31);
        NavHazardAllowance first = await Asked(v, g, at);
        int choice = ChoiceOf(v, Bot);
        v.For(Mate.Slot, g, Blue, default, Plain(g), at);
        Assert.Same(first.Hazard, v.For(Bot.Slot, g, Blue, default, Plain(g), at).Hazard);
        Assert.Equal(choice, ChoiceOf(v, Bot));
    }

    [Fact]
    public async Task ALifeAsksTheSameFieldEachTimeAndTheNextLifeDrawsAgain()
    {
        var (v, g, at) = Seated(on: true);
        Assert.Equal(1, v.LifeOf(Bot.Slot));
        NavHazardAllowance first = await Asked(v, g, at);
        Assert.NotNull(first.Hazard);
        Assert.Equal(1f, first.Weight);
        Assert.Same(first.Hazard, v.For(Bot.Slot, g, Blue, default, Plain(g), at).Hazard);
        Assert.Equal(1, v.Composes);
        // Still alive: the same life, the same field, wherever it stands now.
        v.Note(Bot.Slot, true);
        Assert.Same(first.Hazard, v.For(Bot.Slot, g, Blue, default, Plain(g), at + new AiVector(600f, 0f, 0f)).Hazard);
        v.Note(Bot.Slot, false);
        v.Note(Bot.Slot, true);
        Assert.Equal(2, v.LifeOf(Bot.Slot));
        Assert.NotSame(first.Hazard, v.For(Bot.Slot, g, Blue, default, Plain(g), at).Hazard);
        Assert.Equal(2, v.Composes);
        (int Slot, int Life, int Goal, float[] Ratios, int Choice) row = Assert.Single(v.Lives());
        Assert.Equal((Bot.Slot, 2, 1), (row.Slot, row.Life, row.Goal));
        Assert.Equal([1f, 1f], row.Ratios);
    }

    [Fact]
    public async Task WithTheSwitchOffABotMindsItsTeamsHazardAsBefore()
    {
        var (off, g, at) = Seated(on: false);
        var team = new NavHazardAllowance(new NavCellHazard(g, new float[g.CellCount]), 0.6f);
        Assert.Equal(team, await Asked(off, g, at, team));
        // The control: on, the bot minds its corridor's field composed over the team's, at weight 1.
        var (on, _, _) = Seated(on: true);
        NavHazardAllowance minded = await Asked(on, g, at, team);
        Assert.NotSame(team.Hazard, minded.Hazard);
        Assert.Same(team.Hazard, on.TeamOf(minded.Hazard));
        Assert.Equal(1f, minded.Weight);
    }

    [Fact]
    public void APlayerThatIsNoBotOfOursMindsItsTeamsHazardAsBefore()
    {
        var (v, g, at) = Seated(on: true);
        var team = new NavHazardAllowance(new NavCellHazard(g, new float[g.CellCount]), 0.6f);
        Assert.Equal(team, v.For(Bot.Slot + 1, g, Blue, team, Plain(g), at));
    }

    [Fact]
    public async Task TheProfileGivesABotTheFieldOfItsCorridorWhereItStands()
    {
        var (v, g, at) = Seated(on: true);
        IEngineEntities entities = FakeEngine.Of<IEngineEntities>((m, args) => m.Name == nameof(IEngineEntities.GetOrigin) && (EntityHandle)args[0]! == Player
            ? new Vector(at.X, at.Y, at.Z) : throw FakeEngine.Unasked(m));
        var profile = new TFNavProfile(null!, null!, entities, new SourceSharp.Ai.TF2.Jump.TFBlastLinks(), variation: v);
        profile.HazardFor(Player, Blue, g, Plain(g));
        await v.Settled().WaitAsync(TimeSpan.FromSeconds(30));
        NavHazardAllowance own = profile.HazardFor(Player, Blue, g, Plain(g));
        Assert.True(own.Hazard!.IsOver(g));
        Assert.Equal(1f, own.Weight);
        // Asked with no graph, the profile answers the team's hazard as before: none here.
        Assert.Equal(default, profile.HazardFor(Player, Blue));
    }
}
