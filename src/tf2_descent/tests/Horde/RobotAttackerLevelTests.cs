using SourceSharp.Abi;
using TF2.Descent.Server.Horde;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The attacker level a hero's armour is measured against (<see cref="RobotAttackerLevel"/>, HO8):
/// a lent robot body's is its row's monster level; anything else's is 1.
/// </summary>
public sealed class RobotAttackerLevelTests
{
    private sealed class RigCurrent(ServerHordeRig rig) : IHordeCurrent
    {
        public HordeController? Current => rig.Controller;
    }

    private static (ServerHordeRig Rig, RobotAttackerLevel Levels) Make()
    {
        var rig = new ServerHordeRig(Row(4, markers: 3, depth: 6));
        return (rig, new RobotAttackerLevel(new RigCurrent(rig), rig.Registry));
    }

    [Fact]
    public void A_lent_robots_body_is_its_rows_monster_level()
    {
        var (rig, levels) = Make();
        var (id, body) = rig.Lent("soldier");
        Assert.True(rig.Roster.MonsterLevel(id.Index) > 1, "the stimulus: a level above the default");

        Assert.Equal(rig.Roster.MonsterLevel(id.Index), levels.LevelOf(body.Handle));
    }

    [Fact]
    public void A_parked_body_is_level_one()
    {
        var (rig, levels) = Make();

        Assert.Equal(RobotAttackerLevel.Default, levels.LevelOf(rig.Bodies.Body(9).Handle));
    }

    [Fact]
    public void A_dead_robots_body_is_level_one()
    {
        var (rig, levels) = Make();
        var (_, body) = rig.Lent("soldier");
        body.OnDamagedAt(EntityHandle.None, 1_000_000f, DamageType.Blast, default, default, HitGroup.Chest);

        Assert.Equal(RobotAttackerLevel.Default, levels.LevelOf(body.Handle));
    }

    [Fact]
    public void Anything_that_is_no_robot_is_level_one()
    {
        var (_, levels) = Make();

        Assert.Equal(RobotAttackerLevel.Default, levels.LevelOf(new EntityHandle(0x0001_0000u | 3)));
        Assert.Equal(RobotAttackerLevel.Default, levels.LevelOf(EntityHandle.None));
    }
}
