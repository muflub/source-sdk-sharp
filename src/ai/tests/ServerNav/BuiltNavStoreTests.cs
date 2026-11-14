using SourceSharp.Ai.Server.Nav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// The loader's search order and the server's own builds (plan_bots_teams_tf2
/// E.3 step 1, E.4): <c>maps/&lt;map&gt;.nav3d</c> first, then the build in the
/// cache directory keyed by the bsp's hash; a file that does not bind falls
/// through, and what is refused carries the bsp a build starts from.
/// </summary>
public sealed class BuiltNavStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai-navstore-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Bsp = TinyBsp.Make(TinyBsp.Stock);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private BuiltNavStore Store => new(_dir);

    private static byte[] NavFor(byte[] bsp) => NavFixtures.Course(Nav3dBspBinding.Of(bsp)).Write();

    private static NavBuildStamp Stamp(byte[] nav) => new(BuiltNavStore.Sha256(Bsp), BuiltNavStore.Sha256(nav), "1.0.0", "1.0.2", "ab", 3, 1, 4.5, 2);

    private NavLoadResult Load(byte[]? shipped)
        => NavLoader.Load("dm_test", p => p.EndsWith(".bsp", StringComparison.Ordinal) ? Bsp : shipped, null, built: Store);

    private void StoreBuild(byte[] nav) => Store.Write("dm_test", BuiltNavStore.Sha256(Bsp), nav, Stamp(nav));

    [Fact]
    public void WithNoShippedNavTheStoredBuildLoads()
    {
        StoreBuild(NavFor(Bsp));

        NavLoadResult r = Load(null);

        Assert.Null(r.Refusal);
        Assert.True(r.Nav!.Built);
    }

    [Fact]
    public void AShippedNavThatBindsWinsOverTheStoredBuild()
    {
        StoreBuild(NavFor(Bsp));

        NavLoadResult r = Load(NavFor(Bsp));

        Assert.Null(r.Refusal);
        Assert.False(r.Nav!.Built);
    }

    [Fact]
    public void AShippedNavOfAnotherMapFallsThroughToTheStoredBuild()
    {
        StoreBuild(NavFor(Bsp));

        NavLoadResult r = Load(NavFor(TinyBsp.Make(TinyBsp.Stock, planes: 9)));

        Assert.Null(r.Refusal);
        Assert.True(r.Nav!.Built);
    }

    [Fact]
    public void NoNavAnywhereIsRefusedAndCarriesTheBspToBuildFrom()
    {
        NavLoadResult r = Load(null);

        Assert.Equal("no navigation for dm_test: expected maps/dm_test.nav3d", r.Refusal);
        Assert.Equal(Bsp, r.Bsp);
    }

    [Fact]
    public void AMissingBspCarriesNothingToBuildFrom()
    {
        NavLoadResult r = NavLoader.Load("dm_test", _ => null, null, built: Store);

        Assert.NotNull(r.Refusal);
        Assert.Null(r.Bsp);
    }

    [Fact]
    public void AHalfWrittenStoredBuildIsRefusedNamingItAndCarriesTheBsp()
    {
        byte[] nav = NavFor(Bsp);
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Store.PathFor("dm_test", BuiltNavStore.Sha256(Bsp)), nav[..(nav.Length / 2)]);

        NavLoadResult r = Load(null);

        Assert.Null(r.Nav);
        Assert.Contains($"maps/ai-nav/dm_test-{BuiltNavStore.Sha256(Bsp)[..16]}.nav3d for dm_test cannot be read", r.Refusal, StringComparison.Ordinal);
        Assert.Equal(Bsp, r.Bsp);
    }

    [Fact]
    public void TheKeyChangesWithTheBsp()
    {
        byte[] other = TinyBsp.Make(TinyBsp.Stock, planes: 9);

        Assert.NotEqual(Store.PathFor("dm_test", BuiltNavStore.Sha256(Bsp)), Store.PathFor("dm_test", BuiltNavStore.Sha256(other)));
        Assert.EndsWith($"dm_test-{BuiltNavStore.Sha256(Bsp)[..16]}.nav3d", Store.PathFor("dm_test", BuiltNavStore.Sha256(Bsp)), StringComparison.Ordinal);
    }

    [Fact]
    public void AStoredBuildLeavesTheFileAndItsStampAndNoTemporaryName()
    {
        StoreBuild(NavFor(Bsp));
        StoreBuild(NavFor(Bsp));

        Assert.Equal([$"dm_test-{BuiltNavStore.Sha256(Bsp)[..16]}.nav3d", $"dm_test-{BuiltNavStore.Sha256(Bsp)[..16]}.nav3d.stamp"],
                     Directory.GetFiles(_dir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheStampNamesTheBspTheFileAndTheToolVersions()
    {
        byte[] nav = NavFor(Bsp);
        StoreBuild(nav);

        string stamp = Store.TryReadStamp("dm_test", BuiltNavStore.Sha256(Bsp))!;

        Assert.Contains($"\"bsp_sha256\": \"{BuiltNavStore.Sha256(Bsp)}\"", stamp, StringComparison.Ordinal);
        Assert.Contains($"\"nav_sha256\": \"{BuiltNavStore.Sha256(nav)}\"", stamp, StringComparison.Ordinal);
        Assert.Contains("\"maptools\": \"1.0.0\"", stamp, StringComparison.Ordinal);
        Assert.Contains("\"mapformats\": \"1.0.2\"", stamp, StringComparison.Ordinal);
    }

    [Fact]
    public void AMapNameWithAFolderStaysInTheStoreDirectory()
        => Assert.Equal(_dir, Path.GetDirectoryName(Store.PathFor("workshop/123/dm_x", BuiltNavStore.Sha256(Bsp))));

    [Fact]
    public void TheGraphCacheLeavesNoTemporaryName()
    {
        var cache = new NavGraphCache(_dir, NavEdgeCosts.Default);
        byte[] nav = NavFor(Bsp);
        Assert.Null(NavLoader.Load("dm_test", p => p.EndsWith(".bsp", StringComparison.Ordinal) ? Bsp : nav, cache).Refusal);

        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Single(Directory.GetFiles(_dir, "*.navgraph"));
    }
}
