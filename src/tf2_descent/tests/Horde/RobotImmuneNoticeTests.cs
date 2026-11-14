using SourceSharp.Abi;
using SourceSharp.Tests;
using TF2.Descent.Server;
using TF2.Descent.Server.Horde;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// IMMUNE, told to the attacking hero (<see cref="RobotImmuneNotices"/>, HO8): an immune hit by a
/// player sends his client alone the notice, once a second at most; any other hit sends
/// nothing; a live immune hit through the bodies reaches it.
/// </summary>
public sealed class RobotImmuneNoticeTests
{
    private static readonly EntityHandle Hero = new(0x0001_0000u | 3);
    private static readonly EntityHandle Other = new(0x0001_0000u | 4);

    private sealed class Clock : IHordeClock
    {
        public long Tick { get; set; } = 1000;
        public double TicksPerSecond => 66;
    }

    private sealed record Kit(RecordingEngine Engine, Clock Clock, RobotImmuneNotices Notices);

    private static Kit Make()
    {
        var engine = RecordingEngine.Default();
        engine.Returns("Entities.IsValid", true);
        engine.ReturnsEachCall("Entities.IsPlayer", () => engine.Calls[^1].ArgAs<EntityHandle>(0) is var e && (e == Hero || e == Other));
        engine.ReturnsEachCall("Entities.GetIndex", () => (int)engine.Calls[^1].ArgAs<EntityHandle>(0).EntryIndex);
        var clock = new Clock();
        return new Kit(engine, clock, new RobotImmuneNotices(new ClientMessages(engine), engine, clock, new RigRules(), new Bodies()));
    }

    /// <summary>Body b is entity 100 + b.</summary>
    private sealed class Bodies : TF2.Descent.Server.Bestiary.IRobotFactsSource
    {
        public void Lent(List<TF2.Descent.Server.Bestiary.LentRobot> into) => into.Clear();

        public int EntityOfBody(int body) => 100 + body;
    }

    private static RobotHit Hit(EntityHandle attacker, bool immune, string type = "fire") =>
        new(new TF2.Descent.Ai.Roster.RobotId(0, 1), 1, attacker, 20f, DamageType.Burn, type, immune ? 0 : 20, 100, false,
            HitGroup.Generic, default, default, immune);

    /// <summary>Each IMMUNE sent: who it went to, the robot's entity and the damage type's row.</summary>
    private static List<(int Player, int Entity, int Type)> Sent(Kit k) =>
        [.. k.Engine.Calls.Where(c => c.Member == "UserMessages.Send" && (string)c.Arg(0)! == "TF2.Descent:" + TF2.Descent.Shared.RobotImmuneMessage.Name)
            .Select(c =>
            {
                var reader = new UserMessageReader((byte[])c.Arg(4)!);
                var immune = TF2.Descent.Shared.RobotImmune.Read(ref reader);
                return ((int)c.Arg(2)!, (int)immune.Entity, (int)immune.DamageType);
            })];

    [Fact]
    public void An_immune_hit_tells_the_attacker_alone_immune_with_the_robot_and_the_type()
    {
        var k = Make();

        k.Notices.OnRobotHit(Hit(Hero, immune: true));

        var (player, entity, type) = Assert.Single(Sent(k));
        Assert.Equal(3, player);
        Assert.Equal(101, entity);                       // body 1's entity
        Assert.Equal(0, type);                           // damage_types.json's first row: fire
    }

    [Fact]
    public void Immune_is_no_longer_a_line_on_the_notice_channel()
    {
        var k = Make();

        k.Notices.OnRobotHit(Hit(Hero, immune: true));

        Assert.Single(Sent(k));                          // the stimulus reached it
        Assert.DoesNotContain(k.Engine.Calls, c => c.Member == "UserMessages.Send" && (string)c.Arg(0)! == "TF2.Descent:notice");
    }

    [Fact]
    public void A_hit_that_is_not_immune_tells_nothing()
    {
        var k = Make();

        k.Notices.OnRobotHit(Hit(Hero, immune: false));

        Assert.Empty(Sent(k));
    }

    [Fact]
    public void An_immune_hit_by_no_player_tells_nothing()
    {
        var k = Make();

        k.Notices.OnRobotHit(Hit(new EntityHandle(0x0001_0000u | 200), immune: true));
        k.Notices.OnRobotHit(Hit(EntityHandle.None, immune: true));

        Assert.Empty(Sent(k));
    }

    [Fact]
    public void A_stream_of_immune_hits_tells_once_a_second()
    {
        var k = Make();

        for (int t = 0; t < 132; t++)   // two seconds of a hit every tick
        {
            k.Notices.OnRobotHit(Hit(Hero, immune: true));
            k.Clock.Tick++;
        }

        Assert.Equal(2, Sent(k).Count);
        Assert.Equal((2L, 130L), (k.Notices.Told, k.Notices.Held));
    }

    [Fact]
    public void The_throttle_is_each_heros_own()
    {
        var k = Make();

        k.Notices.OnRobotHit(Hit(Hero, immune: true));
        k.Notices.OnRobotHit(Hit(Other, immune: true));

        Assert.Equal([3, 4], Sent(k).Select(s => s.Player));
    }

    [Fact]
    public void A_new_levels_clock_starting_again_does_not_hold_the_notice()
    {
        var k = Make();
        k.Notices.OnRobotHit(Hit(Hero, immune: true));

        k.Clock.Tick = 10;   // the next level's tick count
        k.Notices.OnRobotHit(Hit(Hero, immune: true));

        Assert.Equal(2, Sent(k).Count);
    }

    [Fact]
    public void The_notice_comes_after_the_brain_hears_the_hit_and_before_the_push()
    {
        Assert.True(TF2.Descent.Server.Horde.Senses.HordeHitSense.Order < RobotImmuneNotices.Order);
        Assert.True(RobotImmuneNotices.Order < TF2.Descent.Server.Horde.Motion.RobotBlastPush.Order);
    }
}
