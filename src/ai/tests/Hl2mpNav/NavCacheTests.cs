using SourceSharp.Ai.Hl2mp.Nav;
using SourceSharp.MapFormats.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Hl2mpNav;

/// <summary>
/// The derived graph's cache (plan_hl2mp_ai §6.2): named and headed by both
/// files' hashes and the reader version, so a one-byte change to the nav
/// finds no cache and derives again; a cached graph hashes equal to a fresh
/// one; a file whose header disagrees is ignored.
/// </summary>
public sealed class NavCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ai-navcache-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _bsp = TinyBsp.Make(TinyBsp.Stock);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private NavGraphCache Cache => new(_dir, NavEdgeCosts.Default);

    private NavLoadResult Load(byte[] nav)
        => NavLoader.Load("dm_test", p => p.EndsWith(".bsp", StringComparison.Ordinal) ? _bsp : nav, Cache);

    private byte[] Nav() => NavFixtures.Course(Nav3dBspBinding.Of(_bsp)).Write();

    [Fact]
    public void TheFirstLoadDerivesAndTheSecondReadsTheCache()
    {
        byte[] nav = Nav();
        LoadedNav first = Load(nav).Nav!, second = Load(nav).Nav!;
        Assert.Equal((false, true), (first.FromCache, second.FromCache));
    }

    [Fact]
    public void ACachedGraphHashesEqualToAFreshOne()
    {
        byte[] nav = Nav();
        LoadedNav first = Load(nav).Nav!, second = Load(nav).Nav!;
        Assert.True(second.FromCache);
        Assert.Equal(first.Nav.Graph.Hash, second.Nav.Graph.Hash);
        Assert.Equal(first.Nav.LeafOfCell, second.Nav.LeafOfCell);
        Assert.Equal(first.Nav.Obstacles, second.Nav.Obstacles);
    }

    [Fact]
    public void AOneByteChangeToTheNavDerivesAgain()
    {
        byte[] nav = Nav();
        Assert.False(Load(nav).Nav!.FromCache);
        // One byte of the string table: the door is now door_2.
        byte[] changed = (byte[])nav.Clone();
        int at = changed.AsSpan().IndexOf("door_1"u8);
        Assert.True(at > 0);
        changed[at + 5] = (byte)'2';
        NavLoadResult again = Load(changed);
        Assert.Null(again.Refusal);
        Assert.False(again.Nav!.FromCache);
        Assert.Equal(2, Directory.GetFiles(_dir, "*.navgraph").Length);
        Assert.Equal("door_2", again.Nav.Nav.Obstacles[0].Name);
    }

    [Fact]
    public void TheFileNameAndHeaderNameBothHashesAndTheReaderVersion()
    {
        LoadedNav n = Load(Nav()).Nav!;
        string path = Cache.PathFor("dm_test", n.BspSha256, n.NavSha256);
        Assert.EndsWith($"dm_test-{n.BspSha256[..16]}-{n.NavSha256[..16]}-v{NavReaderVersion.Package}-g{NavGraphFromNav3d.Revision}.navgraph", path, StringComparison.Ordinal);
        string head = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path).AsSpan(0, 200));
        Assert.Contains(NavReaderVersion.Package, head, StringComparison.Ordinal);
        Assert.Contains(n.BspSha256, head, StringComparison.Ordinal);
        Assert.Contains(n.NavSha256, head, StringComparison.Ordinal);
    }

    [Fact]
    public void ACacheWhoseHeaderNamesOtherFilesIsIgnoredAndRewritten()
    {
        byte[] nav = Nav();
        LoadedNav n = Load(nav).Nav!;
        string path = Cache.PathFor("dm_test", n.BspSha256, n.NavSha256);
        byte[] bytes = File.ReadAllBytes(path);
        int at = System.Text.Encoding.UTF8.GetString(bytes).IndexOf(n.NavSha256, StringComparison.Ordinal);
        bytes[at] = (byte)(bytes[at] == (byte)'0' ? '1' : '0');
        File.WriteAllBytes(path, bytes);

        Assert.Null(Cache.TryRead("dm_test", n.BspSha256, n.NavSha256));
        LoadedNav again = Load(nav).Nav!;
        Assert.False(again.FromCache);
        Assert.NotNull(Cache.TryRead("dm_test", n.BspSha256, n.NavSha256));
    }
}
