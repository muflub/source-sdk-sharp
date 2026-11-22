using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The health a planned rocket jump may spend (lane G9, the user's ruling of 2026-11-13): the plain floor, or under it
/// when a heal place within a walk of the landing pays for it; the walk along the graph; what a route search spends on
/// a link that lands by health, and that its routes still weigh the blast's cost.
/// </summary>
public sealed class JumpHealthRuleTests
{
    private const float Floor = TFDecider.RocketJumpHealthFloor;
    private const int Soldier = TFDecider.SoldierHealth;

    [Fact]
    public void ThePlainRuleIsTheHealthAfterTheBlastAtTheFloor()
        => Assert.Equal((true, false), (JumpHealthRule.Plain(130f, 40f, Floor), JumpHealthRule.Plain(129f, 40f, Floor)));

    [Fact]
    public void ByHealthTheBotMustLandWithTheHardFloorAfterTheBlastAndTheFall()
    {
        float h = JumpHealthRule.HardFloor;
        Assert.True(JumpHealthRule.ByHealth(h + 40f + 10f, 40f, 10f, 200f, Floor));
        Assert.False(JumpHealthRule.ByHealth(h + 40f + 10f - 1f, 40f, 10f, 200f, Floor));
    }

    [Fact]
    public void ByHealthWhatItLandsWithAndThePlacesPayMustReachTheFloor()
    {
        // A 125-health class's small pack (25): landing at 60 it reaches 85, short of 90; landing at 65 it reaches 90.
        Assert.False(JumpHealthRule.ByHealth(100f, 40f, 0f, 25f, Floor));
        Assert.True(JumpHealthRule.ByHealth(105f, 40f, 0f, 25f, Floor));
    }

    [Fact]
    public void APackPaysItsShareOfTheMaximumRoundedUpAndACabinetAllOfIt()
    {
        Assert.Equal((40f, 100f, 200f, 40f, 200f),
                     (JumpHealthRule.Pays(false, ItemKind.HealthSmall, Soldier), JumpHealthRule.Pays(false, ItemKind.HealthMedium, Soldier),
                      JumpHealthRule.Pays(false, ItemKind.HealthLarge, Soldier), JumpHealthRule.Pays(false, ItemKind.Health, Soldier),
                      JumpHealthRule.Pays(true, ItemKind.Other, Soldier)));
        // Rounded up as the game rounds a pack's heal: a 125-health class's medium is 62.5, given as 63.
        Assert.Equal((25f, 63f), (JumpHealthRule.Pays(false, ItemKind.HealthSmall, 125), JumpHealthRule.Pays(false, ItemKind.HealthMedium, 125)));
    }

    [Fact]
    public void AFallUnderTheSafeSpeedCostsNothingAndOneOverItTheRulesTopOfSpread()
    {
        // 650 u/s is a drop of 650² / 1,600 = 264 u. 400 u falls at 800 u/s: 5 × 800 / 300 × 2 × 1.2 = 32.
        Assert.Equal(0f, JumpHealthRule.FallOf(260f, Soldier));
        Assert.Equal(32f, JumpHealthRule.FallOf(400f, Soldier), 2);
    }

    [Fact]
    public void APlansFallIsFromItsApexOverTheTakeoffToItsTouch()
    {
        JumpPlan plan = TFJumpAdvicePlan(apex: 300f, touchZ: -100f);
        Assert.Equal(JumpHealthRule.FallOf(400f, Soldier), JumpHealthRule.Fall(plan, Soldier));
    }

    /// <summary>A search spending a link's spend from the health over the floor takes it exactly when either rule holds.</summary>
    [Fact]
    public void ASearchsSpendAdmitsALinkExactlyWhenThePlainRuleOrTheRuleByHealthHolds()
    {
        int checkedCases = 0, byHealthOnly = 0;
        foreach (int health in (int[])[40, 60, 79, 80, 89, 90, 110, 119, 120, 150, 200])
        {
            foreach (float cost in (float[])[10f, 30f, 40f, 45f, 70f, 110f, 140f])
            {
                foreach (float fall in (float[])[0f, 18f, 32f])
                {
                    foreach (float pays in (float[])[25f, 40f, 100f, 200f])
                    {
                        bool rule = JumpHealthRule.Plain(health, cost, Floor) || JumpHealthRule.ByHealth(health, cost, fall, pays, Floor);
                        bool search = JumpHealthRule.Spend(cost, fall, pays, Floor) <= TFNavProfile.LinkHealthOf(health);
                        Assert.True(rule == search, $"health {health} cost {cost} fall {fall} pays {pays}: rule {rule}, search {search}");
                        checkedCases++;
                        byHealthOnly += rule && !JumpHealthRule.Plain(health, cost, Floor) ? 1 : 0;
                    }
                }
            }
        }

        Assert.True(byHealthOnly > 50, $"the stimulus reaches the rule: {byHealthOnly} of {checkedCases} cases only the health rule allows");
    }

    [Fact]
    public void ABudgetUnderTheFloorIsUnderZeroSoNoLinkThatCostsIsTaken()
        => Assert.Equal((-10f, 110f), (TFNavProfile.LinkHealthOf(80), TFNavProfile.LinkHealthOf(200)));

    // ------------------------------------------------------------------ the walk to a place

    /// <summary>
    /// A row of cells (32 u each) with a pack at its west end; under it, a second row reached only round its east end: the
    /// pack is 32 u from the second row's west end in a straight line, and a long walk.
    /// </summary>
    private static (NavGraph Graph, int[,] Ids) Rows()
        => Grids.Parse("....................", "                   .", "....................");

    private static JumpHealPlace PackAt(int[,] ids, NavGraph g, int x, int y, ItemKind kind = ItemKind.HealthMedium)
        => new(false, kind, 0, Centre(g, ids[x, y]), default, default);

    private static AiVector Centre(NavGraph g, int cell) => g.Cell(cell).Centre;

    [Fact]
    public void APlaceIsNearALandingWithinTheWalkAlongTheGraph()
    {
        var (g, ids) = Rows();
        JumpHeals heals = JumpHeals.Of(g, [PackAt(ids, g, 0, 0)]);
        // 12 cells of 32 u is the walk (384 u); the 13th is past it.
        Assert.Equal(384f, Assert.Single(heals.NearCell(ids[12, 0])).Walk);
        Assert.Empty(heals.NearCell(ids[13, 0]));
    }

    [Fact]
    public void APlaceNearInAStraightLineButFarOnFootIsNotNear()
    {
        var (g, ids) = Rows();
        JumpHeals heals = JumpHeals.Of(g, [PackAt(ids, g, 0, 0)]);
        Assert.True(Centre(g, ids[0, 2]).DistanceTo(Centre(g, ids[0, 0])) <= 64f, "the control: the second row's west end is beside the pack");
        Assert.Empty(heals.NearCell(ids[0, 2]));
    }

    [Fact]
    public void ALandingOffTheGraphHasNoPlaceNear()
    {
        var (g, ids) = Rows();
        JumpHeals heals = JumpHeals.Of(g, [PackAt(ids, g, 0, 0)]);
        Assert.Single(heals.Near(Centre(g, ids[3, 0])));
        Assert.Empty(heals.Near(new AiVector(5000f, 5000f, 0f)));
    }

    [Fact]
    public void ACabinetsCellsAreTheOnesWhoseMiddleIsInItsZone()
    {
        var (g, ids) = Rows();
        // A zone over cells 16..19 of the top row: the walk is from its west edge.
        AiVector a = Centre(g, ids[16, 0]), b = Centre(g, ids[19, 0]);
        var zone = new JumpHealPlace(true, ItemKind.Other, 2, a, new AiVector(a.X - 4f, a.Y - 4f, -8f), new AiVector(b.X + 4f, b.Y + 4f, 128f));
        JumpHeals heals = JumpHeals.Of(g, [zone]);
        Assert.Equal((0f, 32f * 12f), (heals.NearCell(ids[18, 0])[0].Walk, heals.NearCell(ids[4, 0])[0].Walk));
        Assert.Empty(heals.NearCell(ids[3, 0]));
    }

    // ------------------------------------------------------------------ what a search spends

    private static JumpPlan TFJumpAdvicePlan(float apex = 100f, float touchZ = 0f, float health = 30f)
        => new(new JumpRecipe(new Vector(0f, 0f, 0f), new Vector(100f, 0f, touchZ), 64f, 45f, 180f, 0, true), default, 16f, 1f, 0.8f, 2f, health, 0.95f,
               new Vector(100f, 0f, touchZ), apex);

    private static readonly NavLinkCosts Costs = new() { RunSpeed = 240f };

    /// <summary>Three links on the top row, landing by the pack (cell 2), by nothing (cell 17) and by a team cabinet (cell 19).</summary>
    private static (JumpLinkSet Set, JumpPlan[] Plans, JumpHeals Heals, NavGraph Graph) Links()
    {
        var (g, ids) = Rows();
        AiVector c = Centre(g, ids[19, 0]);
        var cabinet = new JumpHealPlace(true, ItemKind.Other, 3, c, c - new AiVector(8f, 8f, 8f), c + new AiVector(8f, 8f, 128f));
        JumpHeals heals = JumpHeals.Of(g, [PackAt(ids, g, 0, 0, ItemKind.HealthSmall), cabinet]);
        JumpLink Jump(int from, int to)
            => new(ids[from, 0], ids[to, 0], new JumpRecipe(new Vector(0f, 0f, 0f), new Vector(100f, 0f, 0f), 64f, 45f, 180f, 0, true), JumpFamily.Forward, 0.95f, 1f, 0.8f, 40f, 2f);
        JumpLinkSet set = JumpLinkSet.Of(g, [Jump(10, 2), Jump(10, 17), Jump(5, 19)], Costs);
        return (set, [TFJumpAdvicePlan(), TFJumpAdvicePlan(), TFJumpAdvicePlan()], heals, g);
    }

    [Fact]
    public void ALinkThatLandsByAPackSpendsWhatThePackDoesNotGiveBackAndOneByNothingOrAnotherTeamsCabinetItsCost()
    {
        var (set, plans, heals, _) = Links();
        float[] spends = heals.Spends(set, plans, Soldier, Floor);
        // The small pack pays 40, the floor less the hard floor is 40: 40 - 40 = 0.
        Assert.Equal((0f, 40f, 40f), (spends[0], spends[1], spends[2]));
    }

    [Fact]
    public void ASetWithSpendsSpendsThemAndWeighsEachLinkAtItsBlastsCost()
    {
        var (set, plans, heals, g) = Links();
        JumpLinkSet spent = set.WithSpends(g, heals.Spends(set, plans, Soldier, Floor), Costs);
        Assert.Equal((0f, 40f), (spent.Links[0].Health, spent.Links[1].Health));
        Assert.Equal((set.Links.CostOf(0), set.Links.CostOf(1), set.Links.CostOf(2)), (spent.Links.CostOf(0), spent.Links.CostOf(1), spent.Links.CostOf(2)));
        Assert.Equal(40f, spent.JumpOf(0).Health);
        Assert.NotEqual(set.Links.Hash, spent.Links.Hash);
    }

    [Fact]
    public void ABotUnderTheFloorIsLetOntoALinkItsLandingsPackPaysForAndOntoNoOther()
    {
        var (set, plans, heals, g) = Links();
        JumpLinkSet spent = set.WithSpends(g, heals.Spends(set, plans, Soldier, Floor), Costs);
        var hurt = new NavObstacleState(g.ObstacleCount) { Reach = JumpReach.Blast, LinkHealth = TFNavProfile.LinkHealthOf(90) };
        Assert.Equal((true, false, false), (spent.Links.Admits(g, 0, ClearanceClass.Stand, hurt), spent.Links.Admits(g, 1, ClearanceClass.Stand, hurt),
                                            spent.Links.Admits(g, 2, ClearanceClass.Stand, hurt)));
        Assert.False(set.Links.Admits(g, 0, ClearanceClass.Stand, hurt), "the control: the set as found keeps the floor");
    }

    [Fact]
    public void AStretchsPlanAnsweredAfterTheLevelsPlacesAreKnownCarriesThoseByItsTouch()
    {
        var (g, ids) = Rows();
        var book = new JumpBook(new JumpBookSettings());
        var ask = new JumpStretchAsk(new AiVector(0f, 0f, 0f), new AiVector(1f, 0f, 0f), new AiVector(600f, 0f, 0f), new Tick(5));
        AiVector near = Centre(g, ids[3, 0]);
        JumpPlan plan = TFJumpAdvicePlan() with { Touch = new Vector(near.X, near.Y, 0f) };
        Assert.True(book.Answer(ask, 0, new JumpPlanned(plan, JumpRefusal.None, 1))!.Heals.IsEmpty, "the control: no places known, none carried");
        book.Heals = JumpHeals.Of(g, [PackAt(ids, g, 0, 0)]);
        Assert.Equal(96f, Assert.Single(book.Answer(ask, 0, new JumpPlanned(plan, JumpRefusal.None, 1))!.Heals).Walk);
    }
}
