using Xunit;

namespace SourceSharp.Ai.Tests.Boundary;

/// <summary>A bot knows other actors only through what it sees (plan_hl2mp_ai §4.3).</summary>
public class PerceivedViewTests
{
    private static readonly ActorId Bot = new(1, 1);
    private static readonly ActorId Seen = new(2, 1);
    private static readonly ActorId Unseen = new(3, 1);

    private static PerceivedView View()
    {
        var v = new PerceivedView(4);
        v.Reset(Bot, new Tick(10), default);
        v.AddSighted(new ActorFacts(Seen, new AiVector(100, 0, 0), default, 1, ActorActivity.None, 1f));
        return v;
    }

    [Fact]
    public void ASightedActorIsReadable()
        => Assert.True(View().TryActor(Seen, out ActorFacts f) && f.Origin == new AiVector(100, 0, 0), "the sighted actor's facts come back");

    [Fact]
    public void AnUnsightedActorIsNotReadable()
        => Assert.False(View().TryActor(Unseen, out _), "an actor the bot cannot see is not in its view");

    [Fact]
    public void AnUnsightedActorAnswersNoFacts()
    {
        View().TryActor(Unseen, out ActorFacts f);
        Assert.Equal(default, f);
    }

    [Fact]
    public void AnEarlierLifeOfASightedActorIsNotReadable()
        => Assert.False(View().TryActor(new ActorId(2, 2), out _), "the serial is part of who was seen");

    [Fact]
    public void ResettingTheViewForgetsWhoWasSighted()
    {
        PerceivedView v = View();
        v.Reset(Bot, new Tick(11), default);
        Assert.False(v.TryActor(Seen, out _), "a new tick starts with nobody sighted");
    }

    [Fact]
    public void ActorFactsCarryNoHealthClipOrReserve()
    {
        // What a client is sent of another player, and nothing else (§2.2, §4.3).
        string[] members = typeof(ActorFacts).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["Activity", "Id", "Origin", "Velocity", "VisibleFraction", "WeaponSeen"], members);
    }

    [Fact]
    public void TheBotsOwnStateCarriesItsHealthAndClip()
    {
        // The control for the fact above: the member read sees these names where they are.
        string[] members = typeof(SelfState).GetProperties().Select(p => p.Name).ToArray();
        Assert.True(members.Contains("Health") && members.Contains("Clip") && members.Contains("Reserve"), "SelfState has them");
    }

    [Fact]
    public void ASightedActorNeedsAnId()
        => Assert.Throws<ArgumentException>(() => new PerceivedView(1).AddSighted(default));

    [Fact]
    public void AFullViewRefusesMore()
    {
        var v = new PerceivedView(1);
        v.AddSighted(new ActorFacts(Seen, default, default, 0, 0, 1f));
        Assert.Throws<InvalidOperationException>(() => v.AddSighted(new ActorFacts(Unseen, default, default, 0, 0, 1f)));
    }
}
