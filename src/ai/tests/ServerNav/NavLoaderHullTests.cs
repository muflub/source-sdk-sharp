using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// Loading a level whose navigation is a hull navigation (plan_bots_teams_tf2_part_g G.7):
/// <c>maps/&lt;map&gt;.navhull</c> is read when the map has no <c>.nav3d</c> that binds, bound by
/// the bsp, refused by name when it is not this map's, not a navigation, or built without a
/// width the game asks; the <c>.nav3d</c> beside it is taken first (NavLoaderTests holds what a
/// map with only a <c>.nav3d</c> answers, unchanged).
/// </summary>
public sealed class NavLoaderHullTests : IDisposable
{
    private const string Hull = "maps/dm_test.navhull", Nav3d = "maps/dm_test.nav3d", BspName = "maps/dm_test.bsp";

    private static readonly byte[] Bsp = TinyBsp.Make(TinyBsp.Stock);
    private static readonly byte[] Other = TinyBsp.Make(TinyBsp.Stock, planes: 9);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai-navhull-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static byte[] HullFor(byte[] bsp) => HullNavFixtures.Floor(Nav3dBspBinding.Of(bsp)).Write();

    private static byte[] Nav3dFor(byte[] bsp) => NavFixtures.Course(Nav3dBspBinding.Of(bsp)).Write();

    private static NavLoadResult Load(Dictionary<string, byte[]> files, INavProfile? profile = null, NavGraphCache? cache = null)
        => NavLoader.Load("dm_test", p => files.GetValueOrDefault(p), cache, profile);

    private sealed class Profile(NavNumbers numbers) : INavProfile
    {
        public NavNumbers Numbers(float fileJumpHeight) => numbers;

        public JumpReach Reach(EntityHandle player) => JumpReach.None;

        public IReadOnlyList<TeamSolid> TeamSolids(MapEntities entities) => [];
    }

    [Fact]
    public void AMapWithOnlyAHullNavigationLoadsIt()
    {
        NavLoadResult r = Load(new() { [BspName] = Bsp, [Hull] = HullFor(Bsp) });
        Assert.Null(r.Refusal);
        Assert.Equal(32, r.Nav!.Nav.Graph.CellCount);
    }

    [Fact]
    public void TheLoadedNavigationNamesTheHullFileItWasReadFrom()
    {
        NavFile file = Load(new() { [BspName] = Bsp, [Hull] = HullFor(Bsp) }).Nav!.File;
        Assert.Equal((NavFile.Hull, Hull), (file.Kind, file.Name));
    }

    [Fact]
    public void TheLoadedNavigationNamesTheNav3dItWasReadFrom()
    {
        NavFile file = Load(new() { [BspName] = Bsp, [Nav3d] = Nav3dFor(Bsp) }).Nav!.File;
        Assert.Equal((NavFile.Nav3d, Nav3d), (file.Kind, file.Name));
    }

    [Fact]
    public void AMapWithBothLoadsItsNav3d()
        => Assert.Equal(NavFile.Nav3d, Load(new() { [BspName] = Bsp, [Nav3d] = Nav3dFor(Bsp), [Hull] = HullFor(Bsp) }).Nav!.File.Kind);

    [Fact]
    public void AMapWhoseNav3dBindsNeverReadsItsHullNavigation()
    {
        var files = new Dictionary<string, byte[]> { [BspName] = Bsp, [Nav3d] = Nav3dFor(Bsp), [Hull] = HullFor(Bsp) };
        var asked = new List<string>();
        NavLoader.Load("dm_test", p =>
        {
            asked.Add(p);
            return files.GetValueOrDefault(p);
        }, null);
        Assert.Equal([Nav3d, BspName], asked);
    }

    [Fact]
    public void AMapWhoseNav3dIsAnotherMapsLoadsItsHullNavigation()
        => Assert.Equal(NavFile.Hull, Load(new() { [BspName] = Bsp, [Nav3d] = Nav3dFor(Other), [Hull] = HullFor(Bsp) }).Nav!.File.Kind);

    [Fact]
    public void AHullNavigationsPointsAreAskedOfItsBoxes()
    {
        IVoxelSpace space = Load(new() { [BspName] = Bsp, [Hull] = HullFor(Bsp) }).Nav!.File.Space;
        Assert.True(space.Free(new AiVector(8f, 8f, 20f)));
        Assert.False(space.Free(new AiVector(8f, 8f, 8f)));
    }

    [Fact]
    public void AHullNavigationBuiltFromAnotherMapIsRefusedByName()
    {
        string? why = Load(new() { [BspName] = Bsp, [Hull] = HullFor(Other) }).Refusal;
        Assert.Equal($"{Hull} was built from another map: it names map checksum {Nav3dBspBinding.Of(Other).MapChecksum}, "
                     + $"not {BspName} (checksum {Nav3dBspBinding.Of(Bsp).MapChecksum})", why);
    }

    [Fact]
    public void AHullNavigationNamingNoMapIsRefused()
        => Assert.Equal($"{Hull} was built from another map: it names no map, not {BspName} (checksum {Nav3dBspBinding.Of(Bsp).MapChecksum})",
                        Load(new() { [BspName] = Bsp, [Hull] = HullNavFixtures.Floor().Write() }).Refusal);

    [Fact]
    public void ALinkedLevelsHullNavigationBindsByItsBsp()
    {
        byte[] bsp = TinyBsp.Make($"{{\n\"classname\" \"worldspawn\"\n\"ss_level_id\" \"{Guid.Parse("0f1e2d3c-4b5a-8978-8685-f4e3d2c1b0a9")}\"\n}}\n");
        Assert.Null(Load(new() { [BspName] = bsp, [Hull] = HullFor(bsp) }).Refusal);
    }

    [Fact]
    public void BytesThatAreNotAHullNavigationAreRefused()
        => Assert.StartsWith($"{Hull} for dm_test cannot be read: ", Load(new() { [BspName] = Bsp, [Hull] = new byte[40] }).Refusal, StringComparison.Ordinal);

    [Fact]
    public void AHullNavigationWithoutTheGamesWidthIsRefusedNamingTheFileAndTheWidth()
    {
        NavLoadResult r = Load(new() { [BspName] = Bsp, [Hull] = HullFor(Bsp) }, new Profile(TFNavProfile.ModNumbers with { HullWidth = 40f }));
        Assert.Null(r.Nav);
        Assert.StartsWith($"{Hull} holds no ceilings for a body 40 wide (it was built for 32, 48)", r.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AHullNavigationIsDerivedByTheGamesNumbers()
    {
        // A ceiling 82 over the floor holds TF2's standing body (82) and not one a unit taller.
        var f = new HullNavFixture(1, 1, 4, 16) { Bsp = Nav3dBspBinding.Of(Bsp) };
        f.Box(0, 0, 0, 4, 4, 1, 6, f.Key(NavHullFlags.Grounded | NavHullFlags.Walkable, 16f, 98f, 98f));
        var files = new Dictionary<string, byte[]> { [BspName] = Bsp, [Hull] = f.Write() };
        Assert.True(Load(files, new Profile(TFNavProfile.ModNumbers)).Nav!.Nav.Graph.Cell(0).Admits(ClearanceClass.Stand));
        Assert.False(Load(files, new Profile(TFNavProfile.ModNumbers with { StandHeight = 83f })).Nav!.Nav.Graph.Cell(0).Admits(ClearanceClass.Stand));
    }

    [Fact]
    public void WithBothRefusedTheRefusalNamesBoth()
    {
        string? why = Load(new() { [BspName] = Bsp, [Nav3d] = Nav3dFor(Other), [Hull] = HullFor(Other) }).Refusal;
        Assert.StartsWith($"{Nav3d} was built from another map: ", why, StringComparison.Ordinal);
        Assert.Contains($"; {Hull} was built from another map: ", why, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedHullNavigationIsNotAMissingFile()
        => Assert.Null(Load(new() { [BspName] = Bsp, [Hull] = HullFor(Other) }).Missing);

    [Fact]
    public void ARefusedHullNavigationStillHandsTheBspToABuild()
        => Assert.Same(Bsp, Load(new() { [BspName] = Bsp, [Hull] = HullFor(Other) }).Bsp);

    [Fact]
    public void AMissingBspNamesTheHullNavigationItCannotBind()
        => Assert.Equal($"no {BspName} to bind {Hull} to", Load(new() { [Hull] = HullFor(Bsp) }).Refusal);

    [Fact]
    public void AMapWithOnlyAHullNavigationHasAUsableOne()
    {
        var files = new Dictionary<string, byte[]> { [BspName] = Bsp, [Hull] = HullFor(Bsp) };
        Assert.Equal("shipped", NavLoader.Usable("dm_test", p => files.GetValueOrDefault(p), null, out string? refusal, out _));
        Assert.Null(refusal);
    }

    [Fact]
    public void AMapWhoseHullNavigationIsAnotherMapsHasNoUsableOne()
    {
        var files = new Dictionary<string, byte[]> { [BspName] = Bsp, [Hull] = HullFor(Other) };
        Assert.Null(NavLoader.Usable("dm_test", p => files.GetValueOrDefault(p), null, out string? refusal, out _));
        Assert.StartsWith($"{Hull} was built from another map: ", refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AHullGraphIsReadBackFromTheCache()
    {
        var files = new Dictionary<string, byte[]> { [BspName] = Bsp, [Hull] = HullFor(Bsp) };
        var cache = new NavGraphCache(_dir, NavEdgeCosts.Default);
        LoadedNav first = Load(files, cache: cache).Nav!, second = Load(files, cache: cache).Nav!;
        Assert.False(first.FromCache);
        Assert.True(second.FromCache);
        Assert.Equal(first.Nav.Graph.Hash, second.Nav.Graph.Hash);
    }

    [Fact]
    public void AHullGraphsCacheNameCarriesTheLeafNumberingsRevision()
    {
        var cache = new NavGraphCache(_dir, NavEdgeCosts.Default);
        Load(new() { [BspName] = Bsp, [Hull] = HullFor(Bsp) }, cache: cache);
        string name = Path.GetFileName(Assert.Single(Directory.GetFiles(_dir, "*.navgraph")));
        Assert.EndsWith($"-nhull{NavHullNavSource.Revision}.navgraph", name, StringComparison.Ordinal);
    }

    [Fact]
    public void ANav3dGraphsCacheNameIsAsItWas()
    {
        var cache = new NavGraphCache(_dir, NavEdgeCosts.Default);
        Load(new() { [BspName] = Bsp, [Nav3d] = Nav3dFor(Bsp) }, cache: cache);
        string name = Path.GetFileName(Assert.Single(Directory.GetFiles(_dir, "*.navgraph")));
        Assert.EndsWith($"-v{NavReaderVersion.Package}-g{NavGraphFromNav3d.Revision}.navgraph", name, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNav3dIsTriedBeforeTheHullNavigation()
        => Assert.Equal([NavFile.Nav3d, NavFile.Hull], NavLoader.Order);
}
