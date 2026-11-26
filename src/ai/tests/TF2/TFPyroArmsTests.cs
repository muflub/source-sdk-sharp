using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;
using static SourceSharp.Ai.Tests.TF2.TFPyroTests;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The arms of the Pyro's shoves and turn that no fact held before the second code review (2026-11-22, section 4): the
/// point's owner and state, its reach, the split's Medic and beam range, and where the view turns to an incoming rocket.
/// </summary>
public sealed class TFPyroArmsTests
{
    private static readonly ActorId Medic = new(6, 0x4000_0001), Other = new(7, 0x4000_0001);

    private static AiVector Eye => Mid + new AiVector(0f, 0f, 68f);

    private static long Counter(TFField f, string name) => f.Decider.ClassCounters().Where(c => c.Key == name).Sum(c => c.Value);

    private static long Run(string counter, ObjectiveKnowledge? objectives, params ActorFacts[] sighted)
    {
        TFField f = Field(Sure);
        for (int n = 0; n < 20; n++)
            f.StepDrawn(Pyro(Mid), objectives, sighted);
        return Counter(f, counter);
    }

    private static ObjectiveKnowledge Point(int owner, ObjectiveState state, float at = 100f)
        => new([new Objective(40, ObjectiveKind.Capture, owner, (1 << TFField.Red) | (1 << TFField.Blue), state, Mid + new AiVector(at, 0f, 0f))]);

    private static ActorFacts Heavy(float x, ActorConditions conditions = 0, ActorId? id = null, TFClass cls = TFClass.HeavyWeapons)
        => Drawn(id ?? Enemy, Mid + new AiVector(x, 0f, 0f), TFField.Blue, cls, conditions);

    /// <summary>
    /// The point's owner and state: an enemy on a point the enemy owns and nobody captures, or on our point while it is locked,
    /// is no objective shove; on our open point he is (the control).
    /// </summary>
    [Fact]
    public void OnlyOurPointOrOneBeingCapturedIsDefended()
    {
        Assert.True(Run("pyro.cap_shoves", Point(TFField.Red, ObjectiveState.Open), Heavy(200f)) > 0);
        Assert.Equal(0, Run("pyro.cap_shoves", Point(TFField.Blue, ObjectiveState.Open), Heavy(200f)));
        Assert.Equal(0, Run("pyro.cap_shoves", Point(TFField.Red, ObjectiveState.Locked), Heavy(200f)));
        Assert.True(Run("pyro.cap_shoves", Point(TFField.Blue, ObjectiveState.Capturing), Heavy(200f)) > 0);
    }

    /// <summary>An enemy 300 u from our point's middle (past its 256 u) is off it already: no objective shove.</summary>
    [Fact]
    public void AnEnemyPastThePointsReachIsNotOnIt()
        => Assert.Equal(0, Run("pyro.cap_shoves", Point(TFField.Red, ObjectiveState.Open, at: -100f), Heavy(200f)));

    /// <summary>Two Übered Heavies (no Medic between them) are no Übered pair: no split.</summary>
    [Fact]
    public void TwoUberedHeaviesAreNoPair()
        => Assert.Equal(0, Run("pyro.uber_splits", TFField.Ctf(), Heavy(200f, ActorConditions.Invulnerable),
                               Heavy(100f, ActorConditions.Invulnerable, Other)));

    /// <summary>An Übered Medic 200 u in front and an Übered Heavy 400 u behind the Pyro (600 u apart, past the beam's 540 u stick range) are no pair.</summary>
    [Fact]
    public void APairPastTheBeamsReachIsNoPair()
        => Assert.Equal(0, Run("pyro.uber_splits", TFField.Ctf(), Heavy(200f, ActorConditions.Invulnerable, Medic, TFClass.Medic),
                               Heavy(-400f, ActorConditions.Invulnerable, Other)));

    /// <summary>The same Medic with the Heavy 300 u behind the Pyro (500 u apart, inside 540 u) is split from him (the control).</summary>
    [Fact]
    public void APairInsideTheBeamsReachIsSplit()
        => Assert.True(Run("pyro.uber_splits", TFField.Ctf(), Heavy(200f, ActorConditions.Invulnerable, Medic, TFClass.Medic),
                           Heavy(-300f, ActorConditions.Invulnerable, Other)) > 0);

    /// <summary>A rocket 500 u out flying at the bot turns the view to where it will be 200 u out, not to where it is.</summary>
    [Fact]
    public void TheViewTurnsToWhereTheRocketWillBe200UnitsOut()
    {
        TFField f = Field(Sure);
        var rocket = new ThingFacts(950, ThingKind.Projectile, (int)TFThingType.Rocket, Eye + new AiVector(500f, 0f, 0f), new AiVector(-1100f, 0f, 0f), TFField.Blue, 0);
        IntentCommand i = f.StepSeeing(Pyro(Mid), TFField.Ctf(), [rocket]);
        Assert.Equal(LookKind.Point, i.Look.Kind);
        Assert.True(MathF.Abs(i.Look.Point.X - (Eye.X + 200f)) < 5f, $"turned to x {i.Look.Point.X - Eye.X} out");
    }
}
