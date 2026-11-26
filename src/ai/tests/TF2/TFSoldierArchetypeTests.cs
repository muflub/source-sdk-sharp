using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Soldier's archetypes (plan_bots_teams_tf2 C.3.1, P7; research p7-research/soldier.md §2 and §4.3 C, D, E): the
/// table's loadouts and draw, and the rules that make each play differently from the stock all-rounder.
/// </summary>
public sealed class TFSoldierArchetypeTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001);
    private static readonly AiVector Mid = new(2048f, 256f, 0f);

    private static TFKitItem Item(TFLoadoutSlot slot, int defindex, string itemClass, int kind, params TFOwnKitAttribute[] attributes)
        => new(slot, defindex, itemClass, kind, [.. attributes]);

    /// <summary>A seed whose draw is <paramref name="archetype"/>: the decider plays the archetype its seed draws (TFDecider.ArchetypeOf).</summary>
    private static ulong SeedOf(string archetype)
        => (ulong)Enumerable.Range(1, 5000).First(s => TFSoldierClass.Archetypes().Draw((ulong)s)!.Name == archetype);

    private static readonly TFKitItem Launcher = Item(TFLoadoutSlot.Primary, 18, "tf_weapon_rocketlauncher", TFField.Launcher),
                                      Shotgun = Item(TFLoadoutSlot.Secondary, 10, "tf_weapon_shotgun", TFField.Shotgun),
                                      Shovel = Item(TFLoadoutSlot.Melee, 6, "tf_weapon_shovel", TFField.Shovel);

    private static TFHud StockKit() => new([Launcher, Shotgun, Shovel], [], []);

    /// <summary>The field with a bot playing <paramref name="archetype"/>, the stock kit in its hands (the rules are the archetype's, not its items').</summary>
    private static TFField As(string archetype, SourceSharp.Ai.TF2.Jump.IJumpAdvisor? jumps = null)
        => new(seed: SeedOf(archetype), jumps: jumps, tune: p => p with { GlanceRate = 0f }) { Hud = StockKit() };

    // ------------------------------------------------------------------ the table

    /// <summary>The table holds the four archetypes the review's ship order puts first, each drawn by some seed.</summary>
    [Fact]
    public void EveryArchetypeIsDrawnBySomeSeed()
    {
        TFArchetypeTable table = TFSoldierClass.Archetypes();
        var drawn = Enumerable.Range(0, 400).Select(s => table.Draw((ulong)s)!.Name).ToHashSet();
        Assert.Equal(new[] { TFSoldierClass.Bannerman, TFSoldierClass.DirectHitter, TFSoldierClass.Roamer, TFSoldierClass.Stock }.Order(), drawn.Order());
    }

    /// <summary>The stock Soldier is the heaviest row, so it is what most bots are (the A/B's fastest and most damaging).</summary>
    [Fact]
    public void TheStockSoldierIsTheHeaviestRow()
    {
        TFArchetype heaviest = TFSoldierClass.Archetypes().Rows.MaxBy(a => a.Weight)!;
        Assert.Equal(TFSoldierClass.Stock, heaviest.Name);
        Assert.True(heaviest.Weight > 0.5f * TFSoldierClass.Archetypes().Rows.Sum(a => a.Weight), "most bots");
    }

    /// <summary>The roamer wears the Gunboats (a worn item only an archetype may choose) and prefers the Black Box.</summary>
    [Fact]
    public void TheRoamerWearsTheGunboats()
    {
        TFArchetype roamer = TFSoldierClass.Archetypes().Named(TFSoldierClass.Roamer)!;
        Assert.Equal([133], roamer.Prefer(TFLoadoutSlot.Secondary).ToArray());
        Assert.Equal(228, roamer.Prefer(TFLoadoutSlot.Primary)[0]);
    }

    /// <summary>The banner Soldier carries a banner in its secondary; the Direct Hit Soldier the Direct Hit in its primary.</summary>
    [Fact]
    public void TheBannerAndDirectHitArchetypesCarryTheirItems()
    {
        TFArchetypeTable table = TFSoldierClass.Archetypes();
        Assert.Equal(129, table.Named(TFSoldierClass.Bannerman)!.Prefer(TFLoadoutSlot.Secondary)[0]);
        Assert.Equal(127, table.Named(TFSoldierClass.DirectHitter)!.Prefer(TFLoadoutSlot.Primary)[0]);
    }

    /// <summary>No archetype names an item the bots may not draw: the Rocket Jumper (237), the Half-Zatoichi (357), the Escape Plan (775, filtered at load).</summary>
    [Fact]
    public void NoArchetypeNamesAnItemBotsMayNotDraw()
    {
        int[] barred = [237, 357, 775];
        foreach (TFArchetype a in TFSoldierClass.Archetypes().Rows)
        {
            foreach (TFArchetypeSlot s in a.Slots)
                Assert.DoesNotContain(s.Prefer, d => barred.Contains(d));
        }
    }

    // ------------------------------------------------------------------ the rules

    /// <summary>The decider plays the archetype its seed draws (the facts below lean on it).</summary>
    [Fact]
    public void TheDeciderPlaysItsSeedsArchetype()
    {
        var f = As(TFSoldierClass.Roamer);
        f.Step(TFField.Self(Mid), TFField.Ctf());
        Assert.Equal(TFSoldierClass.Roamer, f.Decider.Archetype);
    }

    /// <summary>
    /// A roamer takes a jump at 105 health that leaves it 75 (its floor 70, research §4.3 C); the stock Soldier, floor 90,
    /// does not.
    /// </summary>
    [Fact]
    public void ARoamerJumpsLowerThanTheStockSoldier()
    {
        int Jumps(string archetype)
        {
            var f = As(archetype, new TFJumpAdvice());
            return Enumerable.Range(0, 20).Select(_ => f.Step(TFField.Self(new AiVector(300f, 256f, 0f), health: 105, velocity: new AiVector(240f, 0f, 0f)), TFField.Ctf()))
                             .Count(i => f.BeginsJump(i));
        }

        Assert.Equal(0, Jumps(TFSoldierClass.Stock));
        Assert.Equal(1, Jumps(TFSoldierClass.Roamer));
    }

    /// <summary>A roamer at 37 % health in a lull fights on; the stock Soldier goes to heal (research §4.3 C: a roamer leaves at 35 %).</summary>
    [Fact]
    public void ARoamerLeavesForHealthLater()
    {
        string BranchAt(string archetype)
        {
            var f = As(archetype);
            f.Step(TFField.Self(Mid, health: 75), TFField.Ctf());
            return f.Branch;
        }

        Assert.Equal(TFDecider.Survive, BranchAt(TFSoldierClass.Stock));
        Assert.NotEqual(TFDecider.Survive, BranchAt(TFSoldierClass.Roamer));
    }

    /// <summary>A Direct Hit Soldier with a target 420 u off backs off to its band (500 to 1,500); the stock Soldier holds its own (320 to 900).</summary>
    [Fact]
    public void ADirectHitSoldierFightsFurtherOut()
    {
        string VerdictAt(string archetype)
        {
            var f = As(archetype);
            f.Step(TFField.Self(Mid, clip: 4), TFField.Ctf(), (Enemy, Mid + new AiVector(420f, 0f, 0f), AiVector.Zero, TFField.Blue));
            return f.Decider.Doing.Verdict;
        }

        Assert.Equal("holding range", VerdictAt(TFSoldierClass.Stock));
        Assert.Equal("backing off", VerdictAt(TFSoldierClass.DirectHitter));
    }

    // ------------------------------------------------------------------ the branch factors (the review's C3, C4)

    private static readonly string[] Branches = [TFDecider.Fight, TFDecider.TeamBranch, TFDecider.PointsBranch, TFDecider.Fetch, TFDecider.Survive, TFDecider.ClassBranch];

    /// <summary>A roamer's team branch stays over Search and over a remembered fight, so it pushes rather than looking into every sound.</summary>
    [Fact]
    public void ARoamersTeamBranchStaysOverSearch()
    {
        float team = TFDecider.TeamWeight * TFSoldierClass.BranchFactor(TFSoldierClass.Roamer, TFDecider.TeamBranch);
        Assert.True(team < TFDecider.TeamWeight, $"a roamer gathers less: {team}");
        Assert.True(team > TFDecider.SearchWeight && team > 0.3f, $"team {team}");
    }

    /// <summary>A banner Soldier's team branch is over the general one and under a far Fetch.</summary>
    [Fact]
    public void ABannersTeamBranchStaysUnderAFarFetch()
    {
        float team = TFDecider.TeamWeight * TFSoldierClass.BranchFactor(TFSoldierClass.Bannerman, TFDecider.TeamBranch);
        Assert.InRange(team, TFDecider.TeamWeight + 0.01f, TFDecider.FetchFar - 0.01f);
    }

    /// <summary>A Direct Hit Soldier weighs shelling more, still under the points.</summary>
    [Fact]
    public void ADirectHitsShellingStaysUnderThePoints()
    {
        float factor = TFSoldierClass.BranchFactor(TFSoldierClass.DirectHitter, TFDecider.ClassBranch);
        Assert.True(factor > 1f);
        Assert.True(TFDecider.SoldierShellUtility * factor < TFDecider.PointsWeight, $"shelling {TFDecider.SoldierShellUtility * factor}");
    }

    /// <summary>No archetype weighs the fight, and the stock Soldier weighs nothing: a far fight stays under a far Fetch, a near one under Survive.</summary>
    [Fact]
    public void NoArchetypeWeighsTheFightAndStockWeighsNothing()
    {
        foreach (TFArchetype a in TFSoldierClass.Archetypes().Rows)
            Assert.Equal(1f, TFSoldierClass.BranchFactor(a.Name, TFDecider.Fight));
        Assert.All(Branches, b => Assert.Equal(1f, TFSoldierClass.BranchFactor(TFSoldierClass.Stock, b)));
    }

    /// <summary>With no kit read (a replay, a fact with no HUD) a roamer's seed plays the general play: it goes to heal at 37 % like any other.</summary>
    [Fact]
    public void WithNoKitAnArchetypePlaysTheGeneralPlay()
    {
        var f = new TFField(seed: SeedOf(TFSoldierClass.Roamer), tune: p => p with { GlanceRate = 0f });
        f.Step(TFField.Self(Mid, health: 75), TFField.Ctf());
        Assert.Equal(TFDecider.Survive, f.Branch);
    }
}
