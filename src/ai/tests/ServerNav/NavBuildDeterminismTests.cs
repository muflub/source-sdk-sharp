using SourceSharp.Ai.Server.Nav;
using SourceSharp.MapTools.Io;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// The server's build is the file <c>make map</c> writes (plan_bots_teams_tf2
/// E.3, P4 step 9): dm_lockdown built in this process through the server's own
/// path (<see cref="NavBuildInputs"/>, <see cref="NavBuildJob"/>), with the
/// models read from the test mod's content mounted as <c>ssmap nav3d -game</c>
/// mounts it, is byte for byte this tree's <c>maps/dm_lockdown.nav3d</c>, at
/// one thread and at four (ssmap built it at the processor count). Skips,
/// visibly, only when this tree has not run <c>make map MAP=dm_lockdown</c>.
/// </summary>
public sealed class NavBuildDeterminismTests
{
    private const string Map = "dm_lockdown";

    /// <summary>The test mod's content as ssmap mounts it (VbspCommand.MountGameAsync, the default Steam root), read by path.</summary>
    private static async Task<Func<string, byte[]?>> MountedContentAsync()
    {
        var disk = new PhysicalFileSystem("/");
        var content = new ReadOnlyFileSystem(disk);
        string game = Path.GetDirectoryName(PerTreeNav.MapsDir)!;
        VPath gameInfoPath = VPath.Create(Path.Combine(game, "gameinfo.txt"));
        GameInfo info = await GameInfo.LoadAsync(content, gameInfoPath);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        ISteamAppLocator steam = SteamLibraryFolders.Deferred(disk, VPath.Create(Path.Combine(home, ".steam", "steam")));
        GameContentMounter.Result mounted = await GameContentMounter.MountAsync(
            content, info, new GameContentRoots(gameInfoPath.Directory, VPath.Create(Path.GetDirectoryName(game)!)) { Steam = steam });
        var bytes = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        return path =>
        {
            if (bytes.TryGetValue(path, out byte[]? held))
                return held;
            using System.Buffers.IMemoryOwner<byte>? owner = mounted.Content.ReadAsync(VPath.Create(path)).AsTask().GetAwaiter().GetResult();
            return bytes[path] = owner?.Memory.ToArray();
        };
    }

    private static async Task<byte[]> BuildAsync(int threads, Func<string, byte[]?> content)
    {
        string dir = Path.Combine(Path.GetTempPath(), "ai-navsame-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BuiltNavStore(dir);
            byte[] bsp = File.ReadAllBytes(PerTreeNav.BspPath(Map));
            NavBuildInputs inputs = NavBuildInputs.Read(Map, bsp, content);
            Assert.Empty(inputs.Missing);
            NavBuildJob job = NavBuildJob.Start(inputs, new NavBuildSettings(threads, TimeSpan.FromMinutes(10), NavBuildSettings.DefaultMaxLeaves), store, null);
            while (!job.Done)
                await Task.Delay(20);
            job.Release();
            Assert.IsType<NavBuildOutcome.Built>(job.Outcome);
            return File.ReadAllBytes(store.PathFor(Map, BuiltNavStore.Sha256(bsp)));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [PerTreeNavFact(Map)]
    public async Task TheServersBuildAtFourThreadsIsTheFileMakeMapWrote()
    {
        byte[] made = File.ReadAllBytes(PerTreeNav.NavPath(Map));

        byte[] built = await BuildAsync(4, await MountedContentAsync());

        Assert.Equal(BuiltNavStore.Sha256(made), BuiltNavStore.Sha256(built));
    }

    [PerTreeNavFact(Map)]
    public async Task TheServersBuildAtOneThreadIsTheFileMakeMapWrote()
    {
        byte[] made = File.ReadAllBytes(PerTreeNav.NavPath(Map));

        byte[] built = await BuildAsync(1, await MountedContentAsync());

        Assert.Equal(BuiltNavStore.Sha256(made), BuiltNavStore.Sha256(built));
    }
}
