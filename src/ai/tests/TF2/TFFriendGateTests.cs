using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A TF2 bot does not fire through a teammate (plan_bots_teams_tf2_part_g G.2.1) on the synthetic
/// field (<see cref="TFField"/>): with one it sees on the line to its target, nearer than it, the
/// trigger is let go and the shot is not counted; aside or beyond, it fires; and with friendly fire
/// on, a teammate where the rocket's blast would reach holds it too.
/// </summary>
public sealed class TFFriendGateTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001), Mate = new(4, 0x4000_0003);
    private static readonly AiVector Mid = new(2048f, 256f, 0f);

    private static ActorFacts Them(float east = 600f) => new(Enemy, Mid + new AiVector(east, 0f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, (int)TFClass.Soldier);

    private static ActorFacts Friend(float east, float north = 0f) => new(Mate, Mid + new AiVector(east, north, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Red, (int)TFClass.Soldier);

    private static TFField Field(bool friendlyFire = false)
    {
        var f = new TFField(tune: p => p with { AimErrorDeg = 0f });
        if (friendlyFire)
            f.Memory.Apply(AiEvent.FriendlyFireSet(f.Bot, new Tick(1), true));
        return f;
    }

    [Fact]
    public void WithATeammateOnTheLineToItsTargetTheBotHoldsItsFire()
    {
        TFField f = Field();
        IntentCommand i = f.StepDrawn(TFField.Self(Mid), TFField.Ctf(), Them(), Friend(250f));
        Assert.Equal(Enemy, f.Decider.Doing.Target);
        Assert.Equal((ActionKind.None, "held fire (teammate)"), (i.Action.Kind, f.Decider.Doing.Verdict));
        Assert.Equal((1, 0, 0), (f.Decider.HeldFireFriend, f.Decider.Shots, f.Decider.FeetShots));
    }

    [Fact]
    public void WithTheTeammateAsideItFires()
    {
        TFField f = Field();
        IntentCommand i = f.StepDrawn(TFField.Self(Mid), TFField.Ctf(), Them(), Friend(250f, north: 150f));
        Assert.Equal((ActionKind.Fire, 0, 1), (i.Action.Kind, f.Decider.HeldFireFriend, f.Decider.Shots));
    }

    [Fact]
    public void WithTheTeammateBeyondItsTargetItFires()
    {
        TFField f = Field();
        IntentCommand i = f.StepDrawn(TFField.Self(Mid), TFField.Ctf(), Them(), Friend(900f));
        Assert.Equal((ActionKind.Fire, 0), (i.Action.Kind, f.Decider.HeldFireFriend));
    }

    [Fact]
    public void AnEnemyOnTheLineHoldsNothing()
    {
        TFField f = Field();
        ActorFacts nearer = Friend(250f) with { Team = TFField.Blue };
        IntentCommand i = f.StepDrawn(TFField.Self(Mid), TFField.Ctf(), Them(), nearer);
        Assert.Equal((ActionKind.Fire, 0), (i.Action.Kind, f.Decider.HeldFireFriend));
    }

    [Fact]
    public void WithFriendlyFireOnATeammateWhereTheRocketsBlastReachesHoldsIt()
    {
        TFField f = Field(friendlyFire: true);
        IntentCommand i = f.StepDrawn(TFField.Self(Mid), TFField.Ctf(), Them(), Friend(600f, north: 120f));
        Assert.Equal((ActionKind.None, 1), (i.Action.Kind, f.Decider.HeldFireFriend));
    }

    /// <summary>The control: the same teammate with friendly fire off is hurt by nothing, and the rocket goes.</summary>
    [Fact]
    public void WithFriendlyFireOffATeammateBesideTheTargetHoldsNothing()
    {
        TFField f = Field();
        IntentCommand i = f.StepDrawn(TFField.Self(Mid), TFField.Ctf(), Them(), Friend(600f, north: 120f));
        Assert.Equal((ActionKind.Fire, 0), (i.Action.Kind, f.Decider.HeldFireFriend));
    }

    [Fact]
    public void AShotgunHasNoBlastToHoldFor()
    {
        TFField f = Field(friendlyFire: true);
        IntentCommand i = f.StepDrawn(TFField.Self(Mid, weapon: TFField.Shotgun, clip: 6), TFField.Ctf(), Them(), Friend(600f, north: 120f));
        Assert.Equal((ActionKind.Fire, 0), (i.Action.Kind, f.Decider.HeldFireFriend));
    }

    /// <summary>The view has not turned to the target yet: the shot would go where it points, and a teammate stands there.</summary>
    [Fact]
    public void ATeammateWhereTheViewStillPointsHoldsTheFire()
    {
        TFField f = Field();
        ActorFacts north = Them() with { Origin = Mid + new AiVector(0f, 600f, 0f) };
        IntentCommand i = f.StepDrawn(TFField.Self(Mid), TFField.Ctf(), north, Friend(250f));
        Assert.Equal(Enemy, f.Decider.Doing.Target);
        Assert.Equal((ActionKind.None, 1), (i.Action.Kind, f.Decider.HeldFireFriend));
    }
}
