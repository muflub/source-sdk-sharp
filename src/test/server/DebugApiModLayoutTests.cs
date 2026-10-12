using System.Text.Json;

using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Where the mod is: the engine's -game, which /console and /map read from.
/// Two levels above the managed directory stopped being the mod when the
/// launcher moved mods under mods/&lt;id&gt; (debug-api lane, 2026-10-04).
/// </summary>
public sealed class DebugApiModLayoutTests : IDisposable
{
    private readonly string _mod = Directory.CreateTempSubdirectory("ss-mod-").FullName;

    public void Dispose() => Directory.Delete(_mod, recursive: true);

    [Fact]
    public void The_engines_game_directory_is_the_mod()
        => Assert.Equal(_mod, new ModLayout(() => "/game/bin/debug/bin/managed", () => _mod).ModDirectory);

    [Fact]
    public void Without_a_game_directory_it_is_two_above_the_managed_one()
        => Assert.Equal("/game/bin/debug", new ModLayout(() => "/game/bin/debug/bin/managed", () => "").ModDirectory);

    [Fact]
    public void A_game_directory_that_does_not_exist_is_not_believed()
        => Assert.Equal("/game/bin/debug",
                        new ModLayout(() => "/game/bin/debug/bin/managed", () => _mod + "/missing").ModDirectory);

    [Fact]
    public void A_relative_game_directory_is_not_believed()
        => Assert.Equal("/game/bin/debug", new ModLayout(() => "/game/bin/debug/bin/managed", () => "mods/test").ModDirectory);

    //-------------------------------------------------------------------------
    // The maps directory with SOURCESHARP_MAPS unset: the mod's maps, from
    // -game, not bin/<config>/maps two above the shared managed directory.
    //-------------------------------------------------------------------------

    [Fact]
    public void Unset_SOURCESHARP_MAPS_reads_the_maps_of_the_mod_game_names()
    {
        // The server's managed directory is the game directory's shared
        // bin/managed: the guess two above it is bin/<config>, not the mod.
        string game = Path.Combine(_mod, "bin", "debug");
        string mod = Path.Combine(game, "mods", "test");
        Directory.CreateDirectory(Path.Combine(mod, "maps"));

        using var server = new DebugApiServer(["-game", mod], TestDebugHooks.Server(world: new FakeDebugWorld { ManagedDirectory = Path.Combine(game, "bin", "managed") }),
                                              new DebugApiDirectories(DebugApiServer.DefaultSocketDirectory, MapDirectory: null));

        using var doc = JsonDocument.Parse(DebugApiGolden.Get(server, "/map").Body);
        Assert.Equal(Path.Combine(mod, "maps"), doc.RootElement.GetProperty("directory").GetString());
    }

    //-------------------------------------------------------------------------
    // The console log /console follows: +con_logfile moves the engine's.
    //-------------------------------------------------------------------------

    [Fact]
    public void The_console_log_is_console_log_in_the_mod()
        => Assert.Equal(Path.Combine(_mod, "console.log"),
                        ConsoleTail.DefaultPath(new ModLayout(() => "/x/bin/managed", () => _mod)));

    [Fact]
    public void A_con_logfile_on_the_command_line_moves_the_console_log_the_tail_follows()
        => Assert.Equal(Path.Combine(_mod, "3f-bisect-42.log"),
                        ConsoleTail.DefaultPath(new ModLayout(() => "/x/bin/managed", () => _mod), "3f-bisect-42.log"));

    [Fact]
    public void The_server_follows_the_file_its_con_logfile_names()
    {
        using var server = new DebugApiServer(["-debugapisocket", "-game", _mod, "+con_logfile", "own.log"],
                                              TestDebugHooks.Server(world: new FakeDebugWorld()),
                                              new DebugApiDirectories(_mod, MapDirectory: null));
        Assert.True(server.Start(), "the test server could not listen");
        using var doc = JsonDocument.Parse(DebugApiGolden.Get(server, "/console").Body);
        Assert.Equal(Path.Combine(_mod, "own.log"), doc.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public void Console_names_the_epoch_of_the_server_that_answered()
    {
        using var server = DebugApiServer.ForTest(TestDebugHooks.Server(world: new FakeDebugWorld()));
        using var doc = JsonDocument.Parse(DebugApiGolden.Get(server, "/console").Body);
        Assert.True(doc.RootElement.GetProperty("epoch").GetInt64() > 0);
    }
}
