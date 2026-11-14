using SourceSharp.Abi;
using SourceSharp.Host.Sdk;
using TF2.Descent.Server.Level;
using Xunit;

namespace TF2.Descent.Tests.Level;

/// <summary>The server's level data at level init (WO1): read on a Descent level, absent elsewhere, the seed from the name or Booting.</summary>
public sealed class LevelServiceTests
{
    internal sealed class ListLog : ILog
    {
        public List<string> Lines { get; } = [];
        public void Msg(string message) => Lines.Add(message);
        public void Warning(string message) => Lines.Add("W:" + message);
    }

    /// <summary>The game's files: the committed level under any name asked for, and what was asked.</summary>
    internal sealed class LevelFilesOnDisk(LevelFixture? level) : IEngineFiles
    {
        public List<string> Asked { get; } = [];

        public int GameFileSize(string path) => ReadGameFileBytes(path)?.Length ?? -1;

        public string? ReadGameFile(string path) => null;

        public byte[]? ReadGameFileBytes(string path)
        {
            Asked.Add(path);
            return level is null ? null
                : path.EndsWith(".nav3d", StringComparison.Ordinal) ? level.Nav3d
                : path.EndsWith(".map2d", StringComparison.Ordinal) ? level.Map2d
                : null;
        }

        public byte[]? ReadFileOnPath(string path, string pathId) => null;
    }

    private readonly ListLog _log = new();
    private readonly LevelFilesOnDisk _files = new(LevelFixture.Committed());

    private LevelService Service() => new(_files, _log);

    private static BootInfo Booted(ulong seed) => new(ModuleAnswer.Known, seed, "level", 1, "hash", false);

    [Fact]
    public void A_descent_level_is_read_at_level_init_and_said_in_one_line()
    {
        LevelService service = Service();

        service.OnLevelInit("tf2_descent-1-s1");

        LevelData level = Assert.IsType<LevelData>(service.Current);
        Assert.Equal(18, level.Rooms.Length);
        Assert.Equal(["maps/tf2_descent-1-s1.nav3d", "maps/tf2_descent-1-s1.map2d"], _files.Asked);
        string line = Assert.Single(_log.Lines);
        Assert.StartsWith("[TF2Descent/Level] tf2_descent-1-s1: depth 1 seed 1 (map name): 18 rooms (0 derived), 18 doors, 53 markers "
            + "(packspawn 26, shrine 2, container 4, dispenser 1, barrel 8, trap 2, spawn 8, arrival 2), 5 arrival points, "
            + "exit at (4772 144 56) in room 2 to tf2_descent-2, missing: none (read in ", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tf2_descent_town")]
    [InlineData("ctf_2fort")]
    public void Any_other_map_has_no_level_data_and_reads_nothing(string map)
    {
        LevelService service = Service();
        service.OnLevelInit("tf2_descent-1-s1");

        service.OnLevelInit(map);

        Assert.Null(service.Current);
        Assert.Null(service.Horde);
        Assert.Equal(2, _files.Asked.Count);   // the level's two files only
    }

    [Fact]
    public void A_pool_instance_waits_for_bootings_seed_and_then_rolls_from_it()
    {
        LevelService service = Service();

        service.OnLevelInit("tf2_descent-1-9f2c1a");

        Assert.Null(service.Current!.Seed);
        Assert.Null(service.Horde);
        Assert.Contains("seed none", _log.Lines[0], StringComparison.Ordinal);
        Assert.Contains("missing: seed", _log.Lines[0], StringComparison.Ordinal);

        service.OnBooted(Booted(0xABCDEF));

        Assert.Equal(0xABCDEFUL, service.Current!.Seed);
        Assert.Equal("booting", service.Current.SeedFrom);
        Assert.Equal(PieceSource.Shipped, service.Current.Source(LevelPiece.Seed));
        Assert.Equal(0xABCDEFUL, service.Horde!.Seed);
    }

    [Fact]
    public void Bootings_seed_replaces_the_names()
    {
        LevelService service = Service();
        service.OnLevelInit("tf2_descent-1-s1");
        Assert.Equal(1UL, service.Horde!.Seed);

        service.OnBooted(Booted(42));

        Assert.Equal(42UL, service.Horde!.Seed);
    }

    [Fact]
    public void A_level_whose_files_are_missing_says_what_is_missing()
    {
        var log = new ListLog();
        var service = new LevelService(new LevelFilesOnDisk(null), log);

        service.OnLevelInit("tf2_descent-1-s1");

        Assert.Empty(service.Current!.Rooms);
        Assert.Contains("missing: arrivals, doors, exit, markers, rooms", log.Lines[0], StringComparison.Ordinal);
        Assert.Contains("[TF2Descent/Level] tf2_descent-1-s1: no maps/tf2_descent-1-s1.nav3d\n", log.Lines);
    }

    [Fact]
    public void The_town_reached_from_a_level_is_a_return_and_a_fresh_town_is_not()
    {
        LevelService service = Service();

        service.OnLevelInit("tf2_descent_town");
        Assert.False(service.ReturnedFromLevel);
        service.OnLevelInit("tf2_descent-1-s1");
        Assert.False(service.ReturnedFromLevel);
        service.OnLevelInit("tf2_descent_town");
        Assert.True(service.ReturnedFromLevel);
        service.OnLevelInit("tf2_descent_town");
        Assert.False(service.ReturnedFromLevel);
    }

    [Fact]
    public void A_level_shutdown_forgets_the_level()
    {
        LevelService service = Service();
        service.OnLevelInit("tf2_descent-1-s1");

        service.OnLevelShutdown();

        Assert.Null(service.Current);
        Assert.Null(service.Horde);
    }

    [Fact]
    public void The_dev_command_lists_every_room_door_marker_and_arrival()
    {
        LevelService service = Service();
        service.OnLevelInit("tf2_descent-1-s1");
        var log = new ListLog();

        new LevelCommand(service, log).Execute();

        Assert.Equal(1 + 18 + 18 + 53 + 5, log.Lines.Count);
        Assert.Contains(log.Lines, l => l.StartsWith("room 13 arrival_shaftb-ew-h 'Way in' Arrival: cells (2,4) 1x1 turn 1", StringComparison.Ordinal));
        Assert.Contains(log.Lines, l => l.StartsWith("marker 0 descent_spawn_pack at (2496 576 16) yaw 0 room 0", StringComparison.Ordinal));
    }

    [Fact]
    public void The_dev_command_on_another_map_says_there_is_no_level()
    {
        LevelService service = Service();
        service.OnLevelInit("tf2_descent_town");
        var log = new ListLog();

        new LevelCommand(service, log).Execute();

        Assert.Equal(["[TF2Descent/Level] this map is not a Descent level: no level data\n"], log.Lines);
    }
}
