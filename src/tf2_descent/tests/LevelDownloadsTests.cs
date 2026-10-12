extern alias client;

using System.Net;
using SourceSharp.Abi;
using Xunit;
using LevelDownloads = client::TF2.Descent.Client.LevelDownloads;
using MapPrefetcher = client::TF2.Descent.Client.MapPrefetcher;

namespace TF2.Descent.Tests;

/// <summary>
/// download/maps keeps only the generated levels still in play: the current
/// map and the announced next one. Old levels, from the prefetch or the
/// engine's own download, go at the next level init; other maps stay.
/// </summary>
public sealed class LevelDownloadsTests : IDisposable
{
    private const string Old     = "tf2_descent-1-00112233445566aa";
    private const string Current = "tf2_descent-2-3d4ae1c8956d33f1";
    private const string Next    = "tf2_descent-3-ffeeddccbbaa9988";

    private sealed class ListLog : ILog
    {
        public List<string> Lines { get; } = new();
        public void Msg(string message) => Lines.Add(message);
        public void Warning(string message) => Lines.Add("W:" + message);
    }

    private sealed class Mod(string dir) : IModInfo
    {
        public string Id => "tf2_descent";
        public string GameDir => dir;
        public string ManagedDir => dir;
        public IReadOnlyList<string> AssembliesFor(Side side) => [];
    }

    private sealed class NoFastDl : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => throw new InvalidOperationException("nothing here should download");
    }

    private readonly string _game = Directory.CreateTempSubdirectory("tf2_descent-levels-").FullName;
    private readonly MapPrefetcher _prefetcher;
    private readonly LevelDownloads _downloads;
    private readonly ListLog _log = new();

    public LevelDownloadsTests()
    {
        var mod = new Mod(_game);
        _prefetcher = new MapPrefetcher(mod, _log, new NoFastDl());
        _downloads = new LevelDownloads(mod, _prefetcher, _log);
    }

    public void Dispose()
    {
        _prefetcher.Dispose();
        Directory.Delete(_game, recursive: true);
    }

    private string Maps => _prefetcher.DownloadMaps;

    /// <summary>The cleaner with no room for old levels: what the bound leaves to delete is everything unpinned.</summary>
    private LevelDownloads Strict(int keep = 0) => new(new Mod(_game), _prefetcher, _log) { KeepLevels = keep };

    private string Put(string fileName)
    {
        Directory.CreateDirectory(Maps);
        string path = Path.Combine(Maps, fileName);
        File.WriteAllText(path, fileName);
        return path;
    }

    /// <summary>Announces <see cref="Next"/> with its map already on disk, so nothing downloads.</summary>
    private void Announce()
    {
        Put(Next + ".bsp");
        _prefetcher.OnNextLevel(3, Next, $"http://h/maps/{Next}.bsp.bz2", null);
        Assert.Equal(Next, _prefetcher.Next);
    }

    /// <summary>
    /// THE BUG (-a5, k3s, 2026-10-02): the host keeps a level running after the
    /// party leaves, so going down from town re-enters the SAME map. Deleting
    /// it on the way up made every down hop download it again, costing ~4.5 s.
    /// </summary>
    [Fact]
    public void In_town_a_recently_left_level_is_kept()
    {
        string left = Put(Current + ".bsp");   // the level the party just came up from

        _downloads.OnLevelInit("tf2_descent_town");

        Assert.True(File.Exists(left));
        Assert.DoesNotContain(_log.Lines, l => l.Contains("removed", StringComparison.Ordinal));
    }

    [Fact]
    public void An_old_level_is_removed_with_its_part_and_bz2()
    {
        string[] old = [Put(Old + ".bsp"), Put(Old + ".bsp.part"), Put(Old + ".bsp.bz2")];
        Assert.All(old, f => Assert.True(File.Exists(f)));

        Strict().OnLevelInit(Current);

        Assert.All(old, f => Assert.False(File.Exists(f)));
        Assert.Contains(_log.Lines, l => l.Contains("removed 3 old level", StringComparison.Ordinal));
    }

    [Fact]
    public void The_current_and_next_levels_are_kept()
    {
        string current = Put(Current + ".bsp");
        Announce();
        string nextPart = Put(Next + ".bsp.part");
        string old = Put(Old + ".bsp");

        Strict().OnLevelInit(Current);

        Assert.True(File.Exists(current));
        Assert.True(File.Exists(Path.Combine(Maps, Next + ".bsp")));
        Assert.True(File.Exists(nextPart));
        Assert.False(File.Exists(old));
    }

    [Fact]
    public void Maps_that_are_not_generated_levels_are_left_alone()
    {
        string[] kept =
        [
            Put("tf2_descent_town.bsp"),
            Put("tf2_descent-1.bsp"),
            Put("ctf_2fort.bsp"),
            Put("other_mod-2-3d4ae1c8956d33f1.bsp"),
            Put("tf2_descent-2-3D4AE1C8956D33F1.bsp"),
        ];
        string old = Put(Old + ".bsp");

        Strict().OnLevelInit(Current);

        Assert.All(kept, f => Assert.True(File.Exists(f), f));
        Assert.False(File.Exists(old));
    }

    private string PutUsed(string map, int minutesAgo)
    {
        string file = Put(map + ".bsp");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-minutesAgo));
        return file;
    }

    [Fact]
    public void Beyond_the_bound_the_least_recently_used_levels_go()
    {
        string newest = PutUsed("tf2_descent-1-0000000000000001", 1);
        string second = PutUsed("tf2_descent-1-0000000000000002", 2);
        string third  = PutUsed("tf2_descent-1-0000000000000003", 3);
        string oldest = PutUsed("tf2_descent-1-0000000000000004", 4);

        Strict(keep: 2).OnLevelInit("tf2_descent_town");

        Assert.True(File.Exists(newest));
        Assert.True(File.Exists(second));
        Assert.False(File.Exists(third));
        Assert.False(File.Exists(oldest));
    }

    [Fact]
    public void The_current_level_is_marked_used_so_it_outranks_older_ones()
    {
        string current = PutUsed(Current, 60);          // loaded again now, but last written an hour ago
        string recent  = PutUsed("tf2_descent-1-0000000000000001", 1);

        Strict(keep: 1).OnLevelInit(Current);            // touches Current
        Strict(keep: 1).OnLevelInit("tf2_descent_town"); // one slot: the most recently used wins

        Assert.True(File.Exists(current));
        Assert.False(File.Exists(recent));
    }

    [Fact]
    public void A_kept_level_keeps_its_map_but_not_its_leftovers()
    {
        string map = Put(Current + ".bsp");
        string part = Put(Current + ".bsp.part");

        _downloads.OnLevelInit("tf2_descent_town");

        Assert.True(File.Exists(map));
        Assert.False(File.Exists(part));
    }

    [Fact]
    public void Without_a_download_directory_nothing_happens()
    {
        Assert.False(Directory.Exists(Maps));

        _downloads.OnLevelInit(Current);

        Assert.Empty(_log.Lines);
    }

    [Theory]
    [InlineData("tf2_descent-2-3d4ae1c8956d33f1.bsp", "tf2_descent-2-3d4ae1c8956d33f1")]
    [InlineData("tf2_descent-12-3d4ae1c8956d33f1.bsp.part", "tf2_descent-12-3d4ae1c8956d33f1")]
    [InlineData("tf2_descent-2-3d4ae1c8956d33f1.bsp.bz2", "tf2_descent-2-3d4ae1c8956d33f1")]
    [InlineData("tf2_descent-2-3d4ae1c8956d33f.bsp", null)]
    [InlineData("tf2_descent-x-3d4ae1c8956d33f1.bsp", null)]
    [InlineData("tf2_descent--3d4ae1c8956d33f1.bsp", null)]
    [InlineData("tf2_descent-2-3d4ae1c8956d33g1.bsp", null)]
    [InlineData("tf2_descent-2-3d4ae1c8956d33f1.nav", null)]
    [InlineData("tf2_descent_town.bsp", null)]
    public void Generated_level_files_are_recognised_by_name(string fileName, string? map)
    {
        Assert.Equal(map, _downloads.GeneratedMapOf(fileName));
    }
}
