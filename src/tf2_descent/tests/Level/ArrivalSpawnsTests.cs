using SourceSharp.Abi;
using SourceSharp.Shared;
using TF2.Descent.Server.Level;
using TF2.Descent.Server.Mode;
using Xunit;

namespace TF2.Descent.Tests.Level;

/// <summary>Arriving heroes stand on the level's arrival points (mine plan D1) and, back in the town, on its arrival spawns (D2).</summary>
public sealed class ArrivalSpawnsTests
{
    private sealed class Mode(bool active) : IDescentModeState
    {
        public bool Active { get; set; } = active;
    }

    private static EntityHandle Hero(uint n) => new(n);

    private readonly LevelService _level = new(new LevelServiceTests.LevelFilesOnDisk(LevelFixture.Committed()), new LevelServiceTests.ListLog());
    private readonly RecordingEngine _engine = new();
    private readonly Mode _mode = new(active: true);
    private uint _next = 1000;

    public ArrivalSpawnsTests()
    {
        _engine.ReturnsEachCall("Entities.Create", () => new EntityHandle(_next++))
            .Returns("Entities.IsValid", true)
            .Returns("Entities.SetKeyValue", true)
            .Returns("Entities.GetOrigin", Vector.Zero);
    }

    private ArrivalSpawns Spawns() => new(_level, _mode, _engine, new LevelServiceTests.ListLog());

    private IEnumerable<EngineCall> Calls(string member) => _engine.Calls.Where(c => c.Member == member);

    [Fact]
    public void A_party_of_four_arrives_at_four_spots_made_on_the_levels_arrival_points()
    {
        _level.OnLevelInit("tf2_descent-1-s1");
        ArrivalSpawns spawns = Spawns();

        var spots = Enumerable.Range(1, 4).Select(i => spawns.SelectSpawn(Hero((uint)i))).ToList();

        Assert.Equal(4, spots.Distinct().Count());
        Assert.All(spots, s => Assert.NotEqual(EntityHandle.None, s));
        // One spot per arrival point, an info_player_deathmatch named as the town's, where the level put it.
        Assert.Equal(5, Calls("Entities.Create").Count());
        Assert.All(Calls("Entities.Create"), c => Assert.Equal(EntityNames.InfoPlayerDeathmatch, c.Arg(0)));
        Assert.All(Calls("Entities.SetKeyValue"), c => Assert.Equal(("targetname", ArrivalSpawns.ArrivalName), (c.Arg(1), c.Arg(2))));
        Assert.Equal(_level.Current!.Arrivals.Select(a => a.Position), Calls("Entities.SetOrigin").Select(c => c.ArgAs<Vector>(1)));
        Assert.Equal(_level.Current!.Arrivals.Select(a => new Vector(0, a.Yaw, 0)), Calls("Entities.SetAngles").Select(c => c.ArgAs<Vector>(1)));
        Assert.Equal(5, Calls("Entities.Spawn").Count());
    }

    [Fact]
    public void A_respawning_hero_keeps_his_spot_and_a_fifth_takes_the_fifth()
    {
        _level.OnLevelInit("tf2_descent-1-s1");
        ArrivalSpawns spawns = Spawns();
        var first = Enumerable.Range(1, 4).Select(i => spawns.SelectSpawn(Hero((uint)i))).ToList();

        Assert.Equal(first[2], spawns.SelectSpawn(Hero(3)));
        EntityHandle fifth = spawns.SelectSpawn(Hero(5));
        Assert.DoesNotContain(fifth, first);
        Assert.Equal(5, Calls("Entities.Create").Count());   // made once a level
    }

    [Fact]
    public void Outside_the_descent_mode_it_names_no_spot_and_makes_none()
    {
        _mode.Active = false;
        _level.OnLevelInit("tf2_descent-1-s1");

        Assert.Equal(EntityHandle.None, Spawns().SelectSpawn(Hero(1)));
        Assert.Empty(Calls("Entities.Create"));
    }

    [Theory]
    [InlineData("ctf_2fort")]
    [InlineData("tf2_descent_town")]
    public void On_a_map_that_is_no_level_and_no_return_it_names_no_spot(string map)
    {
        _level.OnLevelInit(map);

        Assert.Equal(EntityHandle.None, Spawns().SelectSpawn(Hero(1)));
        Assert.Empty(Calls("Entities.Create"));
        Assert.Empty(Calls("Entities.FindAllByClassname"));
    }

    [Fact]
    public void Back_in_the_town_heroes_arrive_at_its_arrival_spawns_only()
    {
        EntityHandle square = new(10), shaftA = new(11), shaftB = new(12);
        var names = new Queue<string>(["", ArrivalSpawns.ArrivalName, ArrivalSpawns.ArrivalName]);
        _engine.Returns("Entities.FindAllByClassname", new[] { square, shaftA, shaftB })
            .ReturnsEachCall("Entities.GetName", () => names.Dequeue());
        _level.OnLevelInit("tf2_descent-1-s1");
        _level.OnLevelInit("tf2_descent_town");
        ArrivalSpawns spawns = Spawns();

        var spots = Enumerable.Range(1, 2).Select(i => spawns.SelectSpawn(Hero((uint)i))).ToList();

        Assert.Equal([shaftA, shaftB], spots);
        Assert.Empty(Calls("Entities.Create"));
        Assert.Equal(EntityNames.InfoPlayerDeathmatch, Assert.Single(Calls("Entities.FindAllByClassname")).Arg(0));
    }

    [Fact]
    public void The_next_level_makes_its_own_spots_and_forgets_who_stood_where()
    {
        _level.OnLevelInit("tf2_descent-1-s1");
        ArrivalSpawns spawns = Spawns();
        EntityHandle before = spawns.SelectSpawn(Hero(2));
        spawns.SelectSpawn(Hero(1));

        _level.OnLevelInit("tf2_descent-1-s1");
        EntityHandle after = spawns.SelectSpawn(Hero(1));

        Assert.Equal(10, Calls("Entities.Create").Count());
        Assert.NotEqual(before, after);
        Assert.Equal(new EntityHandle(1005), after);   // the second level's first spot: its point 0
    }
}
