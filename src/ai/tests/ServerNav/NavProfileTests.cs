using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// The games' navigation profiles (plan_bots_teams_tf2 P6 step 2): HL2MP's is the
/// core's default movement exactly, so its graphs and cached graphs keep their
/// bytes and names; TF2's numbers are TF's hull and jumps with the Scout's double
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
        string tf = NavGraphCache.NumbersKey(TF2NavNumbers.Numbers, 56f);
        string other = NavGraphCache.NumbersKey(TF2NavNumbers.Numbers with { MaxDoubleJumpRise = 0f }, 56f);
        Assert.Equal(16, tf.Length);
        Assert.NotEqual(tf, other);
    }

    [Fact]
    public void TF2sHullIsFortyEightByEightyTwoCrouchedSixtyTwo()
    {
        NavNumbers n = TF2NavNumbers.Numbers;
        Assert.Equal((48f, 82f, 62f, 18f), (n.HullWidth, n.StandHeight, n.CrouchHeight, n.StepHeight));
    }

    [Fact]
    public void TF2sJumpsAreTheGroundJumpTheStepTheTuckAndTheDash()
    {
        NavNumbers n = TF2NavNumbers.Numbers;
        // 289² / 1600 = 52.2; + 18 = 70.2; + 20 = 72.2; + 268.33² / 1600 = 45 more.
        Assert.Equal((70.2f, 72.2f, 117.2f), (MathF.Round(n.MaxJumpRise, 1), MathF.Round(n.MaxCrouchJumpRise, 1), MathF.Round(n.MaxDoubleJumpRise, 1)));
    }

    [Fact]
    public void TF2sSafeDropIsSixHundredFiftyUnderEightHundred()
        => Assert.Equal(264.06f, MathF.Round(TF2NavNumbers.Numbers.SafeDrop, 2));

    [Theory]
    [InlineData(1, JumpReach.Double)]
    [InlineData(3, JumpReach.None)]
    [InlineData(0, JumpReach.None)]
    public void OnlyTheScoutDoubleJumps(int playerClass, JumpReach reach)
        => Assert.Equal(reach, TF2NavNumbers.ReachOf(playerClass));

    [Fact]
    public void AProfilesNumbersNameItsCachedGraphAndTheDefaultsDoNot()
    {
        byte[] bsp = TinyBsp.Make(TinyBsp.Stock);
        byte[] nav = NavFixtures.Course(Nav3dBspBinding.Of(bsp)).Write();
        var cache = new NavGraphCache(_dir, NavEdgeCosts.Default);
        byte[]? Read(string p) => p.EndsWith(".bsp", StringComparison.Ordinal) ? bsp : nav;

        LoadedNav hl2mp = NavLoader.Load("course", Read, cache, new Hl2mpNavProfile()).Nav!;
        LoadedNav none = NavLoader.Load("course", Read, cache).Nav!;
        LoadedNav tf = NavLoader.Load("course", Read, cache, new Fixed(TF2NavNumbers.Numbers)).Nav!;

        Assert.Equal((false, true, false), (hl2mp.FromCache, none.FromCache, tf.FromCache));
        Assert.True(File.Exists(cache.PathFor("course", tf.BspSha256, tf.NavSha256, tf.NumbersKey)));
        Assert.Equal(2, Directory.GetFiles(_dir, "*.navgraph").Length);
    }
}
