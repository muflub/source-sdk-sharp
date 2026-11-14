using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Ai.Level;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Level;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The level the horde is given (plan_descent_logic HO2 joined to WO1): the
/// level's own rooms where its files ship them, the stand-in where they do not,
/// and a line that says which.
/// </summary>
public sealed class LevelHordeSourceTests
{
    private sealed class Levels(HordeLevel? horde) : ILevelData
    {
        public LevelData? Current => null;
        public HordeLevel? Horde => horde;
    }

    private sealed class World : IStandInWorld
    {
        public int Asked { get; private set; }

        public AiVector? Arrival()
        {
            Asked++;
            return new AiVector(2336, 4608, 16);
        }

        public AiVector? StandAt(AiVector point) => point;
    }

    private static HordeLevel Shipped() => StandInLevel.Around(new AiVector(0, 0, 0), 3, 7, p => p, out _);

    [Fact]
    public void The_levels_own_rooms_are_the_hordes_when_the_level_ships_them()
    {
        var shipped = Shipped();
        var world = new World();
        var log = new RecordingLog();
        var source = new LevelHordeSource(new Levels(shipped), new StandInLevelSource(world, log), new RigRules(), log);

        var given = source.LevelFor("tf2_descent-3-s7", 3, 7);

        Assert.Same(shipped, given);
        Assert.Equal(0, world.Asked);
        Assert.DoesNotContain("stand-in", log.Text);
    }

    [Fact]
    public void A_level_with_nothing_to_give_gets_the_stand_in_and_says_so()
    {
        var world = new World();
        var log = new RecordingLog();
        var source = new LevelHordeSource(new Levels(null), new StandInLevelSource(world, log), new RigRules(), log);

        var given = source.LevelFor("tf2_descent-1-s1", 1, 1);

        Assert.NotNull(given);
        Assert.Equal(StandInLevel.Rooms, given!.RoomCount);
        Assert.True(world.Asked > 0, "the stand-in was never asked");
        Assert.Contains("tf2_descent-1-s1: the level gave no rooms for the horde", log.Text);
    }
}
