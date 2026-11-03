using Xunit;

namespace SourceSharp.Ai.Tests.Predict;

/// <summary>
/// The intent classifier, the aim lead and the push test (plan_hl2mp_ai
/// §5.3.2-4, §5.4) with the plan's numbers.
/// </summary>
public sealed class PredictorTests
{
    private static IntentInputs Opponent(float retreat) => new(retreat, 0.5f, 0f, 60, 600f, 0f, false, false);

    [Fact]
    public void ARetreatTendencyOfPointEightPredictsARetreatAndPointTwoAnApproach()
    {
        Assert.Equal(OpponentIntent.Retreat, IntentClassifier.Top(Opponent(0.8f)).Intent);
        Assert.Equal(OpponentIntent.Approach, IntentClassifier.Top(Opponent(0.2f)).Intent);
    }

    [Fact]
    public void AReloadingOpponentIsPredictedReloading()
        => Assert.Equal(OpponentIntent.Reloading, IntentClassifier.Top(Opponent(0.5f) with { ReloadingNow = true }).Intent);

    [Fact]
    public void TheLeadOnACrossingTargetLandsOnTheAnalyticIntercept()
    {
        // A target crossing at 300 u/s, 800 u away, a bolt at 1,000 u/s (§12.4:
        // the plan's 100 u/s bolt never meets a 300 u/s crossing target).
        var from = new AiVector(0, 0, 0);
        var target = new AiVector(800, 0, 0);
        var vel = new AiVector(0, 300, 0);
        AiVector aim = AimLead.Point(from, target, vel, 1000f, 1f);
        // Analytic: |(800, 300 t)| = 1000 t → t = 800 / sqrt(1000² − 300²).
        float t = 800f / MathF.Sqrt(1000f * 1000f - 300f * 300f);
        Assert.True(aim.DistanceTo(target + vel * t) <= 8f, $"aim {aim}, intercept {target + vel * t}");
        Assert.Equal(target, AimLead.Point(from, target, vel, 0f, 1f));
        Assert.True(AimLead.Point(from, target, vel, 1000f, 0f) == target, "no lead quality, no lead");
    }

    [Fact]
    public void ABoltSlowerThanACrossingTargetNeverMeetsIt()
        => Assert.Null(AimLead.InterceptTime(default, new AiVector(800, 0, 0), new AiVector(0, 300, 0), 100f));

    [Fact]
    public void FullDisciplinePushesOnPointSixWithAFifthOfASecondToSpare()
    {
        SkillCurves curves = SourceSharp.Ai.Tests.Skills.Curves.Game();
        (float trust, float margin) = (curves["pushTrust"].At(10), curves["pushMarginSeconds"].At(10));
        Assert.Equal((0.6f, 0.2f), (MathF.Round(trust, 3), MathF.Round(margin, 3)));
        Assert.True(curves["pushTrust"].At(0) > 2f, "at 0 the estimate is ignored: no P(empty) + P(reloading) reaches it");
    }

    [Fact]
    public void APerceivedReloadPushesWhenTheBotArrivesInTime()
    {
        var w = new WeaponFacts(3, "weapon_smg1", 45, 0.075f, 1.5f, true, false, 0f);
        var a = new AmmoEstimate();
        a.SeeWeapon(w, new Tick(0));
        a.ReloadStarted(w, new Tick(10), 0.015f);
        Assert.True(AmmoWindow.Judge(a, w, new Tick(10), 0.015f, float.NaN, 300f, 320f, 0.6f, 0.2f).Push, "0.94 s to arrive, 1.5 s left");
        Assert.False(AmmoWindow.Judge(a, w, new Tick(10), 0.015f, float.NaN, 600f, 320f, 0.6f, 0.2f).Push, "1.9 s to arrive is too late");
    }
}
