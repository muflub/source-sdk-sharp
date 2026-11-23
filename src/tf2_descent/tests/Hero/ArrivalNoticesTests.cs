using TF2.Descent.Server;
using TF2.Descent.Server.Hero;
using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// Notices held across this process's level change (the whole run's F28: one sent just before an
/// unhosted changelevel never reached the HUD): sent on the next level, once the player has stood
/// alive there for <see cref="ArrivalNotices.DelaySeconds"/>.
/// </summary>
public sealed class ArrivalNoticesTests
{
    private const string Token = "#Descent_Travel_Up";

    private static (DownedRig Rig, ClientMessages Messages, ArrivalNotices Arrivals) Rig()
    {
        var rig = new DownedRig(DownedRig.A);
        var messages = new ClientMessages(rig.Messages);
        return (rig, messages, new ArrivalNotices(rig.Heroes.Heroes, rig.Clock, messages));
    }

    private static int Sent(DownedRig rig) =>
        rig.Messages.Calls.Count(c => c.Member == "UserMessages.Send" && (string)c.Arg(0)! == "TF2.Descent:notice"
                                      && System.Text.Encoding.UTF8.GetString((byte[])c.Arg(4)!).Contains(Token, StringComparison.Ordinal));

    private static void Stand(ArrivalNotices arrivals, float seconds)
    {
        for (float t = 0; t < seconds; t += 0.5f)
            arrivals.OnThink(0.5f);
    }

    [Fact]
    public void A_notice_held_for_after_the_level_change_is_not_sent_on_the_level_it_was_held_on()
    {
        var (rig, messages, arrivals) = Rig();

        messages.NoticeAfterArrival(DownedRig.A, Token);
        Stand(arrivals, 10);

        Assert.Equal(0, Sent(rig));
    }

    [Fact]
    public void A_held_notice_is_sent_on_the_next_level_once_the_player_has_stood_there_alive()
    {
        var (rig, messages, arrivals) = Rig();
        messages.NoticeAfterArrival(DownedRig.A, Token);

        arrivals.OnLevelInit("tf2_descent_town");
        Stand(arrivals, ArrivalNotices.DelaySeconds - 1);
        int early = Sent(rig);
        Stand(arrivals, 2);

        Assert.Equal((0, 1), (early, Sent(rig)));
    }

    [Fact]
    public void A_held_notice_is_sent_once()
    {
        var (rig, messages, arrivals) = Rig();
        messages.NoticeAfterArrival(DownedRig.A, Token);
        arrivals.OnLevelInit("tf2_descent_town");

        Stand(arrivals, 10);
        arrivals.OnLevelInit("tf2_descent-1-s1");
        Stand(arrivals, 10);

        Assert.Equal(1, Sent(rig));
    }

    [Fact]
    public void A_held_notice_waits_while_the_player_is_dead()
    {
        var (rig, messages, arrivals) = Rig();
        messages.NoticeAfterArrival(DownedRig.A, Token);
        arrivals.OnLevelInit("tf2_descent_town");
        rig.Clock.Alive.Remove(DownedRig.PlayerOf(DownedRig.A));

        Stand(arrivals, 10);

        Assert.Equal(0, Sent(rig));
    }
}
