using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Ai.Roster;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The dev call (<see cref="HordeBrains.CallPackTo"/>, <c>tf2_descent_horde_call</c>): a pack told
/// where a hero is from any distance, the way a pack call tells a pack-mate, so a hunt along the
/// level's paths can be measured from rooms no sense reaches.
/// </summary>
public sealed class HordeDevCallTests
{
    /// <summary>Room 1: a robot and its pack-mate with brains, a robot of another pack; nothing sees anything; a hero three rooms on.</summary>
    private static (BrainRig Rig, RobotId Caller, RobotId Mate, RobotId Stranger, int MateBody) Packs()
    {
        var rig = new BrainRig(Row(5, markers: 0));
        var caller = rig.Robot(Centre(1) + At(-150, 0));
        var mate = rig.Robot(Centre(1) + At(-150, 120), joinLastPack: true);
        var stranger = rig.Robot(Centre(1) + At(-150, -120));
        rig.Lend(caller);
        int mateBody = rig.Lend(mate);
        rig.Lend(stranger);
        rig.Lines.Clear = (_, _) => false;
        rig.Slide = false;
        rig.Hero(1, Centre(4));
        rig.Tick();
        return (rig, caller, mate, stranger, mateBody);
    }

    [Fact]
    public void A_dev_call_tells_each_member_of_the_pack_where_the_hero_is_however_far()
    {
        var (rig, caller, mate, _, _) = Packs();

        int told = rig.Brains.CallPackTo(rig.Now, caller.Index, 1, Centre(4));
        rig.Tick();

        Assert.Equal(2, told);
        Assert.Equal(1, rig.Roster.Carried(mate.Index).TargetSeat);
        Assert.Equal(Centre(4), rig.Roster.Carried(mate.Index).LastKnownPosition);
    }

    [Fact]
    public void A_dev_called_robot_goes_to_look_where_the_hero_is()
    {
        var (rig, caller, _, _, mateBody) = Packs();

        rig.Brains.CallPackTo(rig.Now, caller.Index, 1, Centre(4));
        rig.Tick(30);

        Assert.Equal(HuntDecider.Node.Investigate, rig.NodeOf(mateBody));
    }

    [Fact]
    public void A_dev_call_tells_no_robot_of_another_pack()
    {
        var (rig, caller, _, stranger, _) = Packs();

        rig.Brains.CallPackTo(rig.Now, caller.Index, 1, Centre(4));
        rig.Tick();

        Assert.Equal(-1, rig.Roster.Carried(stranger.Index).TargetSeat);
        Assert.Equal(AlertLevel.Calm, rig.Roster.PackAlert(rig.Roster.Pack(stranger.Index)));
    }

    [Fact]
    public void A_dev_call_gives_the_pack_the_heros_room_as_its_contact_for_its_far_robots()
    {
        var (rig, caller, _, _, _) = Packs();
        int pack = rig.Roster.Pack(caller.Index);
        Assert.Equal(AlertLevel.Calm, rig.Roster.PackAlert(pack));

        rig.Brains.CallPackTo(rig.Now, caller.Index, 1, Centre(4));

        Assert.Equal(AlertLevel.Alerted, rig.Roster.PackAlert(pack));
        Assert.Equal(4, rig.Roster.ContactRoom(pack));
        Assert.Equal(Centre(4), rig.Roster.Contact(pack));
    }
}
