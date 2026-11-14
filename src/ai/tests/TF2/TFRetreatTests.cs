using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// When a hurt TF2 bot leaves a fight for health (plan_bots_teams_tf2_part_g G.2.1) on the
/// synthetic field (<see cref="TFField"/>), by its skills where it was 40 % for everyone: under
/// threat a timid bot goes at 60 % and a bold one fights on to 10 %; Tenacity's give-up raises the
/// share; being hurt is a threat for three seconds; and in a lull anyone tops up under 40 %.
/// </summary>
public sealed class TFRetreatTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001);
    // By the west pack (512, 64), the enemy 600 u east.
    private static readonly AiVector Here = new(700f, 64f, 0f);

    private static TFField Bot(float nerve, float giveUp = 0f) => new(tune: p => p with { RetreatHealthPct = nerve, GiveUpHealth = giveUp });

    private static string Branch(TFField f) => f.Decider.Doing.Branch[(f.Decider.Doing.Branch.IndexOf(": ", StringComparison.Ordinal) + 2)..];

    /// <summary>The branch after a decide at <paramref name="health"/> of a Soldier's 200, with the enemy in sight or nobody.</summary>
    private static string At(TFField f, int health, bool enemy)
    {
        if (enemy)
            f.Step(TFField.Self(Here, health: health), TFField.Ctf(), (Enemy, Here + new AiVector(600f, 0f, 0f), AiVector.Zero, TFField.Blue));
        else
            f.Step(TFField.Self(Here, health: health), TFField.Ctf());
        return Branch(f);
    }

    [Fact]
    public void UnderFireATimidBotGoesForHealthAtFiftyFivePercent()
        => Assert.Equal(TFDecider.Survive, At(Bot(nerve: 0.6f), 110, enemy: true));

    [Fact]
    public void UnderFireABoldBotStaysInAtFiftyFivePercentAndAtThirty()
    {
        Assert.NotEqual(TFDecider.Survive, At(Bot(nerve: 0.1f), 110, enemy: true));
        Assert.NotEqual(TFDecider.Survive, At(Bot(nerve: 0.1f), 60, enemy: true));
    }

    [Fact]
    public void UnderFireABoldBotGoesForHealthUnderItsTenPercent()
        => Assert.Equal(TFDecider.Survive, At(Bot(nerve: 0.1f), 16, enemy: true));

    [Fact]
    public void TenacitysGiveUpMakesABoldBotLeaveSooner()
        => Assert.Equal(TFDecider.Survive, At(Bot(nerve: 0.1f, giveUp: 0.6f), 110, enemy: true));

    [Fact]
    public void InALullABoldBotTopsUpUnderFortyPercentAndNotOverIt()
    {
        Assert.Equal(TFDecider.Survive, At(Bot(nerve: 0.1f), 70, enemy: false));
        Assert.NotEqual(TFDecider.Survive, At(Bot(nerve: 0.1f), 90, enemy: false));
    }

    /// <summary>A timid bot's own share holds in a lull too: the end of a threat does not turn it back from the pack.</summary>
    [Fact]
    public void InALullATimidBotStillGoesUnderItsOwnShare()
        => Assert.Equal(TFDecider.Survive, At(Bot(nerve: 0.6f), 110, enemy: false));

    /// <summary>Hurt a moment ago with nobody in sight is still a threat: a bold bot at 30 % does not walk off for a pack until three seconds have gone.</summary>
    [Fact]
    public void BeingHurtIsAThreatForThreeSeconds()
    {
        TFField f = Bot(nerve: 0.1f);
        f.Memory.Apply(AiEvent.Damaged(f.Bot, new Tick(f.Tick + 1), Enemy, 40f, new AiVector(1f, 0f, 0f), 0));
        Assert.NotEqual(TFDecider.Survive, At(f, 60, enemy: false));
        string branch = "";
        for (int i = 0; i < (int)(RetreatRule.ThreatSeconds / (DecideSchedule.Period * 0.015f)) + 2; i++)
            branch = At(f, 60, enemy: false);
        Assert.Equal(TFDecider.Survive, branch);
    }

    /// <summary>A bot never hurt is under no threat at the start of a map, though no time has passed since "its last hit".</summary>
    [Fact]
    public void ABotNeverHurtIsUnderNoThreatFromTheFirstDecide()
    {
        TFField f = Bot(nerve: 0.1f);
        Assert.Equal(0, f.Memory.Board.LastDamageTick.Value);
        Assert.Equal(TFDecider.Survive, At(f, 70, enemy: false));
    }
}
