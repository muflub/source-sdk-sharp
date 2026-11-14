using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Ai.Director;
using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Level;
using TF2.Descent.Tests.Horde;
using TF2.Descent.Tests.Level;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Bosses;

/// <summary>
/// The boss on its level (plan_descent_logic HO13, item 1): a boss depth's level whose files name
/// its arena holds bosses.json's boss for the depth, on the arena's first pack marker; the spawn
/// director plans its row first, rolled for the depth and the party; a level of another depth
/// holds none.
/// </summary>
public sealed class BossLevelTests
{
    private sealed class Levels(LevelData? current, HordeLevel? horde) : ILevelData
    {
        public LevelData? Current => current;
        public HordeLevel? Horde => horde;
    }

    private sealed class NoWorld : IStandInWorld
    {
        public AiVector? Arrival() => null;
        public AiVector? StandAt(AiVector point) => point;
    }

    private static readonly LevelData Committed = LevelFixture.Committed().Read();

    /// <summary>The committed depth 1 level read as a depth's, its treasure room standing in as an arena when <paramref name="arena"/>.</summary>
    private static LevelData AsDepth(int depth, bool arena = true) =>
        Committed with
        {
            Depth = depth,
            BossArena = arena ? Committed.Rooms.Single(r => r.Role == LevelRoomRole.Treasure).Id : null,
        };

    private static HordeLevel? Given(LevelData data, RecordingLog log)
    {
        var source = new LevelHordeSource(new Levels(data, LevelAdapters.ToHorde(data)), new StandInLevelSource(new NoWorld(), log), new RigRules(), log);
        return source.LevelFor(data.Map, data.Depth, data.Seed!.Value);
    }

    [Fact]
    public void A_boss_depth_s_level_holds_its_boss_on_its_arena_s_first_pack_marker()
    {
        var data = AsDepth(5);
        var log = new RecordingLog();

        var boss = Assert.IsType<HordeBoss>(Given(data, log)!.Boss);

        var spot = data.In(data.BossArena!.Value).First(m => m.Kind == LevelMarkerKind.PackSpawn);
        Assert.Equal(("sir_nukesalot", data.BossArena.Value), (boss.Template, boss.ArenaRoom));
        Assert.Equal((spot.Position.X, spot.Position.Y), (boss.Spot.Position.X, boss.Spot.Position.Y));
        Assert.Contains("the boss sir_nukesalot holds arena room", log.Text);
    }

    [Fact]
    public void Each_boss_depth_holds_its_own_boss()
    {
        Assert.Equal("major_crits", Given(AsDepth(10), new RecordingLog())!.Boss!.Template);
        Assert.Equal("captain_punch", Given(AsDepth(15), new RecordingLog())!.Boss!.Template);
    }

    [Fact]
    public void A_level_of_another_depth_or_with_no_arena_holds_no_boss()
    {
        Assert.Null(Given(AsDepth(4), new RecordingLog())!.Boss);
        Assert.Null(Given(AsDepth(5, arena: false), new RecordingLog())!.Boss);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void The_director_plans_the_boss_s_row_first_rolled_for_the_depth_and_the_party(int party)
    {
        var level = Given(AsDepth(5), new RecordingLog())!;

        var plan = new SpawnDirector(TheRules).Plan(level, party);

        var first = plan.Packs[0].Robots;
        var boss = Assert.Single(first);
        Assert.True(boss.Boss);
        Assert.Equal(("sir_nukesalot", level.Boss!.ArenaRoom), (boss.Roll.Template, boss.Room));
        var t = TheRules.Robots.Template("sir_nukesalot");
        double life = TheRules.Robots.Stats(t, t.ScaleMin, TheRules.Robots.MonsterLevel(5), [], party).Life;
        Assert.Equal(life, boss.Roll.Stats.Life, 9);
        Assert.Single(plan.Packs.SelectMany(p => p.Robots), r => r.Boss);
    }

    [Fact]
    public void A_bigger_party_meets_a_boss_with_more_life()
    {
        var level = Given(AsDepth(5), new RecordingLog())!;

        double solo = new SpawnDirector(TheRules).Plan(level, 1).Packs[0].Robots[0].Roll.Stats.Life;
        double four = new SpawnDirector(TheRules).Plan(level, 4).Packs[0].Robots[0].Roll.Stats.Life;

        Assert.True(four > solo, $"a party of four's boss has {four} life, a lone hero's {solo}");
    }

    [Fact]
    public void The_roster_holds_the_boss_asleep_as_data_until_its_room_wakes()
    {
        var level = Given(AsDepth(5), new RecordingLog())!;
        var roster = RobotRoster.Create(TheRules, 66);

        roster.Load(level, 1);

        int row = Enumerable.Range(0, roster.Count).Single(roster.IsBoss);
        Assert.Equal(RobotState.Asleep, roster.State(row));
        Assert.Equal(0, roster.Body(row));
        Assert.Equal(level.Boss!.Spot.Position, roster.Post(row));
    }

    [BuiltLevelFact(5, 1)]
    public void The_built_depth_5_level_holds_sir_nukesalot_in_its_arena()
    {
        var data = LevelFixture.Built(5, 1).Read();
        Assert.NotNull(data.BossArena);
        Assert.Equal(LevelRoomRole.Arena, data.Rooms[data.BossArena!.Value].Role);

        var boss = Given(data, new RecordingLog())!.Boss!;

        Assert.Equal("sir_nukesalot", boss.Template);
        Assert.Equal(data.BossArena.Value, boss.ArenaRoom);
    }
}
