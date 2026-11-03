using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.Ai.Tests.Trees;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.Tactics;

/// <summary>
/// Handling recoil like a person (plan_hl2mp_ai §3.4.1, 2026-11-02): what scales it is Accuracy for
/// the pull against the climb, Discipline for letting go and waiting; none of it perfect.
/// </summary>
public sealed class RecoilTests
{
    private static MotionParams Motion(params (string Skill, int Level)[] more)
    {
        SkillProfile p = Duel.Preset("normal");
        foreach ((string skill, int level) in more)
            p = p.With(p.IndexOf(skill), level);
        return MotionEnvelope.Clamp(MotionCurves.For(p, SourceSharp.Ai.Tests.Skills.Curves.Game().For(p)));
    }

    /// <summary>The better the aim, the more of a climb it takes out.</summary>
    [Fact]
    public void AccuracyPullsDownMore()
    {
        float low = Motion(("accuracy", 1)).RecoilControl, mid = Motion(("accuracy", 5)).RecoilControl, high = Motion(("accuracy", 10)).RecoilControl;
        Assert.True(low < mid && mid < high, $"{low}, {mid}, {high}");
    }

    /// <summary>Even the best leave some of it: no bot takes out a whole climb, whatever a curve says.</summary>
    [Fact]
    public void NoBotTakesOutAWholeClimb()
    {
        Assert.True(Motion(("accuracy", 10)).RecoilControl < 1f);
        Assert.Equal(MotionEnvelope.MaxRecoilControl, MotionEnvelope.Clamp(new MotionParams(150f, 600f, 10f, 150f, RecoilControl: 5f)).RecoilControl);
        Assert.True(MotionEnvelope.MaxRecoilControl < 1f);
    }

    /// <summary>The more disciplined, the shorter the burst and the further the crosshair comes back first.</summary>
    [Fact]
    public void DisciplineLetsGoSoonerAndWaitsLonger()
    {
        MotionParams careless = Motion(("discipline", 0)), careful = Motion(("discipline", 10));
        Assert.True(careful.BurstSeconds < careless.BurstSeconds, $"{careful.BurstSeconds} of {careless.BurstSeconds}");
        Assert.True(careful.RecoilSettleDeg < careless.RecoilSettleDeg, $"{careful.RecoilSettleDeg} of {careless.RecoilSettleDeg}");
    }

    /// <summary>A bot's own facts hold its punch: its crosshair is its own to see (§2.4).</summary>
    [Fact]
    public void ABotsFactsHoldItsOwnPunch()
    {
        var w = new SenseWorld();
        SenseWorld.Player me = w.Add(1, new AiVector(0f, 0f, 0f));
        me.Punch = new AiVector(-4f, 1f, 0f);
        var self = new EngineBotSelf(w.Engine(), w.Entities(), WeaponCatalog.Stock(), new NoInventory());
        BotSelfFacts f = self.Read(me.Handle);
        Assert.Equal(new AiAngles(-4f, 1f, 0f), f.Punch);
    }

    private sealed class NoInventory : IBotInventory
    {
        public IReadOnlyList<string> Held(EntityHandle player) => [];
    }
}
