using Xunit;

namespace SourceSharp.Ai.Tests.Tactics;

/// <summary>
/// When a hurt bot gives a fight up (plan_hl2mp_ai §5.4, plan_bots_teams_tf2_part_g G.2.1): under
/// the share of its health Nerve says, or Tenacity's give-up when that is higher, and only under
/// threat: someone in sight, or hurt in the last three seconds.
/// </summary>
public sealed class RetreatRuleTests
{
    private const float Dt = 0.015f;

    private static DecisionParams P(float nerve, float giveUp = 0f) => default(DecisionParams) with { RetreatHealthPct = nerve, GiveUpHealth = giveUp };

    [Fact]
    public void TheShareIsNervesOrTenacitysWhenThatIsHigher()
        => Assert.Equal((0.3f, 0.6f), (RetreatRule.HealthShare(P(0.3f)), RetreatRule.HealthShare(P(0.3f, giveUp: 0.6f))));

    [Theory]
    [InlineData(0.29f, true)]
    [InlineData(0.31f, false)]
    public void UnderThreatABotRetreatsUnderItsShare(float health, bool retreats)
        => Assert.Equal(retreats, RetreatRule.Retreats(health, P(0.3f), threatened: true));

    [Fact]
    public void WithNoThreatItDoesNotHoweverHurt()
        => Assert.False(RetreatRule.Retreats(0.05f, P(0.3f), threatened: false));

    [Fact]
    public void TenacitysGiveUpMakesItRetreatSooner()
        => Assert.True(RetreatRule.Retreats(0.5f, P(0.1f, giveUp: 0.6f), threatened: true));

    [Fact]
    public void SomeoneInSightIsAThreat()
        => Assert.True(RetreatRule.Threatened(true, new Tick(10_000), new Tick(0), Dt));

    [Theory]
    [InlineData(2.9f, true)]
    [InlineData(3.1f, false)]
    public void BeingHurtIsAThreatForThreeSeconds(float secondsAgo, bool threatened)
    {
        var now = new Tick(10_000);
        Assert.Equal(threatened, RetreatRule.Threatened(false, now, new Tick(now.Value - (int)(secondsAgo / Dt)), Dt));
    }
}
