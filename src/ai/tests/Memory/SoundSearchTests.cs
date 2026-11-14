using Xunit;

namespace SourceSharp.Ai.Tests.Memory;

/// <summary>
/// Going to look into a sound (plan_hl2mp_ai §5.1, plan_bots_teams_tf2_part_g G.2.1): worth it
/// while fresh by the skill's seconds; the search goes to the board's best; reached, or given up
/// on, it is settled with the sounds beside it, and one further off is left.
/// </summary>
public sealed class SoundSearchTests
{
    private const float Dt = 0.015f;
    private static readonly AiVector Loud = new(1000f, 0f, 0f), Beside = new(1090f, 0f, 0f), Far = new(0f, 900f, 0f);

    private static Blackboard Heard(params (AiVector At, float Loudness, int Tick)[] sounds)
    {
        var b = new Blackboard(8);
        foreach ((AiVector at, float loudness, int tick) in sounds)
            b.Investigate(new Investigation(at, loudness, new Tick(tick), SoundKind.Gunfire, ActorId.None), new Tick(tick), Dt);
        return b;
    }

    private static Tick After(float seconds) => new(100 + (int)(seconds / Dt));

    [Theory]
    [InlineData(3.9f, true)]
    [InlineData(4.1f, false)]
    public void ASoundIsWorthGoingToWhileItIsFreshByTheSkillsSeconds(float secondsAgo, bool worth)
        => Assert.Equal(worth, SoundSearch.Worth(Heard((Loud, 1f, 100)), After(secondsAgo), Dt, 4f, out _));

    [Fact]
    public void WithNothingHeardThereIsNothingToSearch()
    {
        Assert.False(SoundSearch.Worth(new Blackboard(8), After(0f), Dt, 4f, out _));
        Assert.False(SoundSearch.Next(new Blackboard(8), After(0f), Dt, default, false, out _));
    }

    [Fact]
    public void TheSearchGoesToTheLoudestOfTheSoundsHeard()
    {
        Blackboard b = Heard((Far, 0.3f, 100), (Loud, 1f, 100));
        Assert.True(SoundSearch.Next(b, After(1f), Dt, default, false, out AiVector at));
        Assert.Equal(Loud, at);
        Assert.Equal(2, b.InvestigationCount);
    }

    [Fact]
    public void ReachedASoundIsSettledWithTheOnesBesideItAndOneFurtherOffIsLeft()
    {
        Blackboard b = Heard((Loud, 1f, 100), (Beside, 0.8f, 100), (Far, 0.3f, 100));
        Assert.True(SoundSearch.Next(b, After(1f), Dt, Loud + new AiVector(50f, 0f, 0f), false, out _));
        Assert.Equal(1, b.InvestigationCount);
        Assert.True(SoundSearch.Next(b, After(1f), Dt, default, false, out AiVector next));
        Assert.Equal(Far, next);
    }

    [Fact]
    public void NotYetReachedItIsKept()
    {
        Blackboard b = Heard((Loud, 1f, 100));
        SoundSearch.Next(b, After(1f), Dt, Loud + new AiVector(200f, 0f, 0f), false, out _);
        Assert.Equal(1, b.InvestigationCount);
    }

    [Fact]
    public void GettingNowhereTowardsItTheSoundIsGivenUp()
    {
        Blackboard b = Heard((Loud, 1f, 100));
        SoundSearch.Next(b, After(1f), Dt, Loud + new AiVector(600f, 0f, 0f), gettingNowhere: true, out _);
        Assert.Equal(0, b.InvestigationCount);
    }
}
