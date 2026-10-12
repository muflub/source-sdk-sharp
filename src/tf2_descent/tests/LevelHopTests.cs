using TF2.Descent.Server;
using SourceSharp.Abi;
using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// A same-process hop's level change (docs/sdk-integration.md §6): link the
/// target's level files into pool/maps, then changelevel, once per map, and
/// only for a same-process hop the service acknowledged.
/// </summary>
public sealed class LevelHopTests : IDisposable
{
    private sealed class Mod(string dir) : IModInfo
    {
        public string Id => "tf2_descent";
        public string GameDir => dir;
        public string ManagedDir => dir;
        public IReadOnlyList<string> AssembliesFor(Side side) => [];
    }

    private sealed class Console : IServerConsole
    {
        public List<string> Executed { get; } = new();
        public string Execute(string command) { Executed.Add(command); return ""; }
        public void Quit(int exitCode) { }
    }

    private sealed class ListLog : ILog
    {
        public List<string> Lines { get; } = new();
        public void Msg(string message) => Lines.Add(message);
        public void Warning(string message) => Lines.Add("W:" + message);
    }

    private readonly string _root = Directory.CreateTempSubdirectory("tf2_descent-levelhop-").FullName;
    private readonly Console _console = new();
    private readonly ListLog _log = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private LevelHop Hop() => new(new Mod(Path.Combine(_root, "game")), _console, _log);

    private string LevelFile(string name)
    {
        string dir = Path.Combine(_root, "levels");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllText(path, name);
        return path;
    }

    private static HopNotice Notice(string map, bool sameProcess = true, bool ready = true, params string[] files)
        => new("76561197960287930", "inst-2", map, sameProcess, files, ready);

    [Fact]
    public void A_same_process_hop_links_the_level_files_then_changes_level()
    {
        var hop = Hop();
        string bsp = LevelFile("tf2_descent-1-a.bsp");
        string nav = LevelFile("tf2_descent-1-a.nav3d");

        hop.OnHopped(Notice("tf2_descent-1-a", files: [bsp, nav]));

        Assert.Equal(["changelevel tf2_descent-1-a"], _console.Executed);
        Assert.Equal(bsp, new FileInfo(Path.Combine(hop.PoolMaps, "tf2_descent-1-a.bsp")).LinkTarget);
        Assert.Equal(nav, new FileInfo(Path.Combine(hop.PoolMaps, "tf2_descent-1-a.nav3d")).LinkTarget);
    }

    [Fact]
    public void Every_players_hop_names_the_same_map_and_only_the_first_changes_level()
    {
        var hop = Hop();

        hop.OnHopped(Notice("tf2_descent-1-a"));
        hop.OnHopped(Notice("tf2_descent-1-a"));

        Assert.Single(_console.Executed);

        // The next hop, to another map, is a level change of its own.
        hop.OnHopped(Notice("tf2_descent_town"));
        Assert.Equal(2, _console.Executed.Count);
    }

    [Fact]
    public void A_cross_process_or_unacknowledged_hop_changes_nothing()
    {
        var hop = Hop();

        hop.OnHopped(Notice("tf2_descent-1-a", sameProcess: false));
        hop.OnHopped(Notice("tf2_descent-1-a", ready: false));

        Assert.Empty(_console.Executed);
        Assert.False(Directory.Exists(hop.PoolMaps));
    }

    [Fact]
    public void A_target_that_is_not_a_map_name_never_reaches_the_console()
    {
        Hop().OnHopped(Notice("town; quit"));

        Assert.Empty(_console.Executed);
        Assert.Contains(_log.Lines, l => l.StartsWith("W:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_level_file_linked_by_an_earlier_hop_is_replaced()
    {
        var hop = Hop();
        string first = LevelFile("tf2_descent-1-a.bsp");
        hop.OnHopped(Notice("tf2_descent-1-a", files: [first]));

        string again = Path.Combine(_root, "other", "tf2_descent-1-a.bsp");
        Directory.CreateDirectory(Path.GetDirectoryName(again)!);
        File.WriteAllText(again, "newer");
        hop.OnHopped(Notice("tf2_descent_town"));
        hop.OnHopped(Notice("tf2_descent-1-a", files: [again]));

        Assert.Equal(again, new FileInfo(Path.Combine(hop.PoolMaps, "tf2_descent-1-a.bsp")).LinkTarget);
    }
}
