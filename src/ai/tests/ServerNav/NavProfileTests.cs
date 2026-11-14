using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.MapFormats.Nav;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// The games' navigation profiles (plan_bots_teams_tf2 P6 step 2): HL2MP's is the
/// core's default movement exactly, so its graphs and cached graphs keep their
/// bytes and names; TF2's numbers (TFNavProfile, from the mod's movement) are TF's hull and jumps with the Scout's double
/// jump; a profile's numbers name its cached graph.
/// </summary>
public sealed class NavProfileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai-navprofile-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private sealed class Fixed(NavNumbers numbers) : INavProfile
    {
        public NavNumbers Numbers(float fileJumpHeight) => numbers;

        public JumpReach Reach(EntityHandle player) => JumpReach.None;

        public IReadOnlyList<TeamSolid> TeamSolids(MapEntities entities) => [];
    }

    [Theory]
    [InlineData(56f)]
    [InlineData(64f)]
    public void HL2MPsNumbersAreTheDefaultMovementsForAnyFile(float fileJumpHeight)
        => Assert.Equal(NavNumbers.Stock(fileJumpHeight), new Hl2mpNavProfile().Numbers(fileJumpHeight));

    [Fact]
    public void HL2MPsPlayersReachNoFurtherThanACrouchJump()
        => Assert.Equal(JumpReach.None, new Hl2mpNavProfile().Reach(new EntityHandle(0x0001_0002u)));

    [Fact]
    public void HL2MPHasNoTeamSolids() => Assert.Empty(new Hl2mpNavProfile().TeamSolids(new MapEntities([], null)));

    [Fact]
    public void HL2MPsNumbersHaveNoDoubleJump()
        => Assert.Equal(0f, new Hl2mpNavProfile().Numbers(56f).MaxDoubleJumpRise);

    [Fact]
    public void TheDefaultNumbersHaveNoCacheKey()
        => Assert.Equal("", NavGraphCache.NumbersKey(NavNumbers.Stock(56f), 56f));

    [Fact]
    public void OtherNumbersHaveACacheKeyOfTheirOwn()
    {
        string tf = NavGraphCache.NumbersKey(TFNavProfile.ModNumbers, 56f);
        string other = NavGraphCache.NumbersKey(TFNavProfile.ModNumbers with { MaxDoubleJumpRise = 0f }, 56f);
        Assert.Equal(16, tf.Length);
        Assert.NotEqual(tf, other);
    }

    [Fact]
    public void TF2sHullIsFortyEightByEightyTwoCrouchedSixtyTwo()
    {
        NavNumbers n = TFNavProfile.ModNumbers;
        Assert.Equal((48f, 82f, 62f, 18f), (n.HullWidth, n.StandHeight, n.CrouchHeight, n.StepHeight));
    }

    [Fact]
    public void TF2sJumpsAreTheGroundJumpTheStepTheTuckAndTheDash()
    {
        NavNumbers n = TFNavProfile.ModNumbers;
        // 289² / 1600 = 52.2; + 18 = 70.2; + 20 = 72.2; + 268.33² / 1600 = 45 more.
        Assert.Equal((70.2f, 72.2f, 117.2f), (MathF.Round(n.MaxJumpRise, 1), MathF.Round(n.MaxCrouchJumpRise, 1), MathF.Round(n.MaxDoubleJumpRise, 1)));
    }

    [Fact]
    public void TF2sSafeDropIsSixHundredFiftyUnderEightHundred()
        => Assert.Equal(264.06f, MathF.Round(TFNavProfile.ModNumbers.SafeDrop, 2));

    [Theory]
    [InlineData(TFClass.Scout, JumpReach.Double)]
    [InlineData(TFClass.Soldier, JumpReach.Blast)]
    [InlineData(TFClass.Undefined, JumpReach.None)]
    public void TheScoutDoubleJumpsAndTheSoldierBlastJumps(TFClass playerClass, JumpReach reach)
        => Assert.Equal(reach, TFNavProfile.ReachOf(playerClass));

    /// <summary>Lane G5c-2b: the Soldier's searches take the level's blast links, and no other class's (G5c-2a held that nobody's did yet).</summary>
    [Fact]
    public void OnlyTheSoldierHasTheBlastReach()
        => Assert.Equal([TFClass.Soldier], Enum.GetValues<TFClass>().Where(c => (TFNavProfile.ReachOf(c) & JumpReach.Blast) != 0));

    /// <summary>TF2's profile for one player of <paramref name="playerClass"/> at <paramref name="health"/>, the level's links published or not.</summary>
    private static (TFNavProfile Profile, SourceSharp.Ai.TF2.Jump.TFBlastLinks Published) Tf(TFClass playerClass, int health)
    {
        var facts = new FactsOf(playerClass);
        IEngineEntities entities = SourceSharp.Ai.Tests.Senses.FakeEngine.Of<IEngineEntities>((m, _) => m.Name == nameof(IEngineEntities.GetHealth)
            ? health : throw SourceSharp.Ai.Tests.Senses.FakeEngine.Unasked(m));
        var published = new SourceSharp.Ai.TF2.Jump.TFBlastLinks();
        return (new TFNavProfile(facts, null!, entities, published), published);
    }

    private sealed class FactsOf(TFClass playerClass) : global::TF2.Server.Player.ITFActorFacts
    {
        public bool TryRead(int playerIndex, out global::TF2.Server.Player.TFActorFacts facts)
        {
            facts = default(global::TF2.Server.Player.TFActorFacts) with { Class = playerClass };
            return true;
        }
    }

    /// <summary>A snapshot's routes over a row of cells, with deaths of <paramref name="role"/>'s players in its first bin.</summary>
    private static ExperienceRoutes DeathsOf(int role)
    {
        var (g, _) = SourceSharp.Ai.Tests.Nav.Grids.Parse("....");
        var rows = new ExperienceDelta();
        rows.Add(new CellKey(new ExperienceContext(0, role, 3), new WorldBin(0, 0, 0), ExperienceSource.Human), new CellStat(10d, 1d, 0d, 0d, 0d));
        return ExperienceRoutes.Build(new MapExperience(new ExperienceKey("m", "0123456789abcdef", "ctf"), 1, rows), g);
    }

    /// <summary>TF2's profile with the experience's routes and the bots' weights.</summary>
    private static (TFNavProfile Profile, SourceSharp.Ai.Server.Experience.ExperienceRouteSlot Routes, TFRouteWeights Weights) Minding()
    {
        var routes = new SourceSharp.Ai.Server.Experience.ExperienceRouteSlot();
        var weights = new TFRouteWeights();
        return (new TFNavProfile(new FactsOf(TFClass.Soldier), null!, null!, new SourceSharp.Ai.TF2.Jump.TFBlastLinks(), routes, weights), routes, weights);
    }

    /// <summary>Lane G4a: a bot's searches weigh its own team's field of where players died, by its Cunning; none without a field or a weight.</summary>
    [Fact]
    public void ABotsSearchesMindItsTeamsFieldByItsCunning()
    {
        var (profile, routes, weights) = Minding();
        Assert.Equal(default, profile.HazardFor(Player, (int)TFTeam.Red));
        ExperienceRoutes red = DeathsOf((int)TFTeam.Red);
        routes.Current = red;
        // Not a bot of ours yet: no weight, no hazard.
        Assert.Equal(default, profile.HazardFor(Player, (int)TFTeam.Red));
        var table = new float[Player.EntryIndex + 1];
        table[Player.EntryIndex] = 0.6f;
        weights.Set(table);
        NavHazardAllowance allowance = profile.HazardFor(Player, (int)TFTeam.Red);
        Assert.Same(red.For((int)TFTeam.Red), allowance.Hazard);
        Assert.Equal(0.6f, allowance.Weight);
    }

    [Fact]
    public void ABotDoesNotMindTheOtherTeamsDeaths()
    {
        var (profile, routes, weights) = Minding();
        routes.Current = DeathsOf((int)TFTeam.Red);
        var table = new float[Player.EntryIndex + 1];
        table[Player.EntryIndex] = 1f;
        weights.Set(table);
        Assert.Equal(default, profile.HazardFor(Player, (int)TFTeam.Blue));
        // The control: as red, the same player minds it.
        Assert.NotEqual(default, profile.HazardFor(Player, (int)TFTeam.Red));
    }

    [Fact]
    public void TheWeightsTableHoldsEachBotsCunningBySlotAndNothingElse()
    {
        Assert.True(SkillPresets.TryGet("expert", out SkillProfile? expert));
        Assert.True(SkillPresets.TryGet("easy", out SkillProfile? easy));
        float[] table = TFRouteWeights.Table([new SourceSharp.Ai.Server.AiBot(new ActorId(3, 1), "a", expert!), new SourceSharp.Ai.Server.AiBot(new ActorId(5, 1), "b", easy!),
                                              new SourceSharp.Ai.Server.AiBot(new ActorId(99, 1), "far", expert!)], 8);
        Assert.Equal([0f, 0f, 0f, 0.6f, 0f, 0.1f, 0f, 0f], table);
    }

    [Theory]
    [InlineData("easy", 0.1f)]
    [InlineData("normal", 0.3f)]
    [InlineData("hard", 0.5f)]
    [InlineData("expert", 0.6f)]
    public void ARouteWeightIsCunningOverTheCap(string preset, float weight)
    {
        Assert.True(SkillPresets.TryGet(preset, out SkillProfile? p));
        Assert.Equal(weight, TFRouteWeights.WeightOf(p!), 5);
    }

    private static SourceSharp.Ai.TF2.Jump.JumpLinkSet Links()
    {
        var (g, ids) = SourceSharp.Ai.Tests.Nav.Grids.Parse("..... .....");
        var recipe = new SourceSharp.Ai.TF2.Jump.JumpRecipe(new Vector(144f, 16f, 0f), new Vector(208f, 16f, 0f), 0f, 60f, 180f, 2, false);
        return SourceSharp.Ai.TF2.Jump.JumpLinkSet.Of(g, [new SourceSharp.Ai.TF2.Jump.JumpLink(ids[4, 0], ids[6, 0], recipe, SourceSharp.Ai.TF2.Jump.JumpFamily.Up, 1f, 2f, 1.5f, 40f, null)],
                                                     new NavLinkCosts { RunSpeed = 240f });
    }

    private static readonly EntityHandle Player = new(0x0001_0002u);

    [Fact]
    public void ASoldiersSearchesTakeThePublishedLinksSpendingItsHealthOverTheJumpsFloor()
    {
        var (profile, published) = Tf(TFClass.Soldier, 150);
        Assert.Equal(default, profile.LinksFor(Player));
        SourceSharp.Ai.TF2.Jump.JumpLinkSet set = Links();
        published.Set = set;
        NavLinkAllowance allowance = profile.LinksFor(Player);
        Assert.Same(set.Links, allowance.Links);
        Assert.Equal(150f - SourceSharp.Ai.TF2.Trees.TFDecider.RocketJumpHealthFloor, allowance.Health);
    }

    /// <summary>Lane G5e: a Soldier's searches leave out the links the snapshot's rows shut, when the mask is the published set's.</summary>
    [Fact]
    public void ASoldiersSearchesLeaveOutTheLinksTheSnapshotShuts()
    {
        var facts = new FactsOf(TFClass.Soldier);
        IEngineEntities entities = SourceSharp.Ai.Tests.Senses.FakeEngine.Of<IEngineEntities>((m, _) => m.Name == nameof(IEngineEntities.GetHealth)
            ? 200 : throw SourceSharp.Ai.Tests.Senses.FakeEngine.Unasked(m));
        var published = new SourceSharp.Ai.TF2.Jump.TFBlastLinks();
        var routes = new SourceSharp.Ai.Server.Experience.ExperienceRouteSlot();
        var profile = new TFNavProfile(facts, null!, entities, published, routes);
        SourceSharp.Ai.TF2.Jump.JumpLinkSet set = Links();
        published.Set = set;
        ExperienceLinkKeys keys = SourceSharp.Ai.TF2.Jump.TFLinkExperience.KeysOf(set);
        var key = new ExperienceKey("m", "c", "ctf");
        var store = new MemoryExperienceStore();
        var failing = new ExperienceDelta();
        failing.Add(keys[0], new LinkStat(6d, 0d, 0d, 0d));
        store.Merge(key, failing, 20d);
        var (graph, _) = SourceSharp.Ai.Tests.Nav.Grids.Parse("...");
        Assert.Null(profile.LinksFor(Player).Shut);
        routes.Current = ExperienceRoutes.Build(store.Load(key, 20d), graph, null, keys);
        NavLinkMask shut = profile.LinksFor(Player).Shut!;
        Assert.True(shut.Shut(0));
        // The same links found again are the same set by their hash, and the mask holds; another set's is no mask.
        published.Set = Links();
        Assert.True(profile.LinksFor(Player).Shut!.Shut(0));
        published.Set = SourceSharp.Ai.TF2.Jump.JumpLinkSet.Of(SourceSharp.Ai.Tests.Nav.Grids.Parse("...... .....").Graph, [], new NavLinkCosts { RunSpeed = 240f });
        Assert.Null(profile.LinksFor(Player).Shut);
    }

    [Fact]
    public void ABotUnderTheJumpsFloorMaySpendNothing()
    {
        var (profile, published) = Tf(TFClass.Soldier, (int)SourceSharp.Ai.TF2.Trees.TFDecider.RocketJumpHealthFloor - 10);
        published.Set = Links();
        Assert.Equal(0f, profile.LinksFor(Player).Health);
    }

    [Fact]
    public void AnotherClassTakesNoLinks()
    {
        var (profile, published) = Tf(TFClass.Scout, 150);
        published.Set = Links();
        Assert.Equal(default, profile.LinksFor(Player));
    }

    [Fact]
    public void AProfilesNumbersNameItsCachedGraphAndTheDefaultsDoNot()
    {
        byte[] bsp = TinyBsp.Make(TinyBsp.Stock);
        byte[] nav = NavFixtures.Course(Nav3dBspBinding.Of(bsp)).Write();
        var cache = new NavGraphCache(_dir, NavEdgeCosts.Default);
        byte[]? Read(string p) => p.EndsWith(".bsp", StringComparison.Ordinal) ? bsp : nav;

        LoadedNav hl2mp = NavLoader.Load("course", Read, cache, new Hl2mpNavProfile()).Nav!;
        LoadedNav none = NavLoader.Load("course", Read, cache).Nav!;
        LoadedNav tf = NavLoader.Load("course", Read, cache, new Fixed(TFNavProfile.ModNumbers)).Nav!;

        Assert.Equal((false, true, false), (hl2mp.FromCache, none.FromCache, tf.FromCache));
        Assert.True(File.Exists(cache.PathFor("course", tf.BspSha256, tf.NavSha256, tf.NumbersKey)));
        Assert.Equal(2, Directory.GetFiles(_dir, "*.navgraph").Length);
    }
}
