using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Knowledge;
using TF2.Server.Objectives;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The objectives every TF2 HUD shows, in the core's kinds (P6 step 5): one objective per record
/// of the mod's feed, with its id, the game's team numbers, the teams that may act as bits, its
/// state, carrier, timers and the setup; a capture zone at the middle of its volume; the version
/// the feed counted.
/// </summary>
public sealed class TFObjectiveFeedTests
{
    private static readonly TFObjective RedFlag = new(40, TFObjectiveKind.Flag, TFTeam.Red, TFTeam.Blue, TFObjectiveState.Carried,
                                                      new Vector(100, 200, 30), 1f, CarrierIndex: 7, UnlockIn: 0f, TimerLeft: 0f);

    private static readonly TFObjective BlueZone = new(41, TFObjectiveKind.CaptureZone, TFTeam.Blue, TFTeam.Unassigned, TFObjectiveState.Open,
                                                       new Vector(0, 0, 0), 1f, CarrierIndex: 0, UnlockIn: 0f, TimerLeft: 0f);

    private static ObjectiveKnowledge Build(bool setup = false)
        => TFObjectiveFeed.Build([RedFlag, BlueZone], 12, setup, id => id == 41 ? new AiVector(-500, 50, 8) : null);

    [Fact]
    public void AFlagIsCarriedByTheOtherTeam()
    {
        Objective flag = Build().All[0];
        Assert.Equal((40, ObjectiveKind.Carry, 2, 1 << 3, ObjectiveState.Carried, 7), (flag.Id, flag.Kind, flag.OwnerTeam, flag.MayAct, flag.State, flag.CarrierSlot));
        Assert.Equal(new AiVector(100, 200, 30), flag.Position);
    }

    [Fact]
    public void ACaptureZoneIsDeliveredToAtItsVolumesMiddle()
    {
        Objective zone = Build().All[1];
        Assert.Equal((41, ObjectiveKind.Deliver, 3, ObjectiveState.Open), (zone.Id, zone.Kind, zone.OwnerTeam, zone.State));
        Assert.Equal(new AiVector(-500, 50, 8), zone.Position);
        Assert.Equal((1 << 2) | (1 << 3), zone.MayAct);
    }

    [Fact]
    public void TheFeedsVersionAndTheSetupRideAlong()
    {
        ObjectiveKnowledge during = Build(setup: true);
        Assert.Equal(12, during.Version);
        Assert.All(during.All, o => Assert.True(o.Setup));
        Assert.All(Build().All, o => Assert.False(o.Setup));
    }

    [Fact]
    public void EveryFeedKindAndStateHasItsCoreName()
    {
        Assert.All(Enum.GetValues<TFObjectiveKind>(), k => TFObjectiveFeed.KindOf(k));
        Assert.All(Enum.GetValues<TFObjectiveState>(), s => Assert.Equal(s.ToString(), TFObjectiveFeed.StateOf(s).ToString()));
        Assert.Equal(ObjectiveKind.Hold, TFObjectiveFeed.KindOf(TFObjectiveKind.KothPoint));
        Assert.Equal(ObjectiveKind.Push, TFObjectiveFeed.KindOf(TFObjectiveKind.Cart));
    }

    private static readonly TFObjective Point = new(50, TFObjectiveKind.ControlPoint, TFTeam.Unassigned, TFTeam.Unassigned, TFObjectiveState.Open,
                                                    new Vector(10, 20, 30), 0f, CarrierIndex: 0, UnlockIn: 0f, TimerLeft: 0f);

    [Fact]
    public void AControlPointsTeamsThatMayActAreTheTeamsTheRulesLetCaptureItNow()
    {
        // The rules let red alone take it (blue holds the point before, say): only red's bit.
        Objective point = TFObjectiveFeed.Build([Point], 1, false, _ => null, id => id == 50 ? 1 << 2 : null).All[0];
        Assert.Equal((ObjectiveKind.Capture, 1 << 2), (point.Kind, point.MayAct));
        // No team may (each needs a point it does not hold): none.
        Assert.Equal(0, TFObjectiveFeed.Build([Point], 1, false, _ => null, _ => 0).All[0].MayAct);
        // Without the rules' answer, the feed's own: an unowned open point stands for both teams.
        Assert.Equal((1 << 2) | (1 << 3), TFObjectiveFeed.Build([Point], 1, false, _ => null).All[0].MayAct);
    }

    [Fact]
    public void TheRulesAnswerNothingForAFlagOrAZone()
    {
        ObjectiveKnowledge built = TFObjectiveFeed.Build([RedFlag, BlueZone], 12, false, id => id == 41 ? new AiVector(-500, 50, 8) : null, _ => 0);
        Assert.Equal(1 << 3, built.All[0].MayAct);
        Assert.Equal((1 << 2) | (1 << 3), built.All[1].MayAct);
    }
}
