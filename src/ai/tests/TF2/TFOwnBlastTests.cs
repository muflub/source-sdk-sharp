using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A TF2 bot's own blast is no attack on it (plan_bots_teams_tf2_part_g G.13, G.14's follow-up):
/// what ends a commitment is a hit by someone else, as its HUD names the attacker.
/// </summary>
public sealed class TFOwnBlastTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001);

    private static readonly AiVector Mid = new(2048f, 256f, 0f);

    /// <summary>
    /// An attacker a decide into fetching the enemy's flag (held for two seconds), when its HUD shows its
    /// own flag taken 300 u away, which scores higher, and it takes 45 damage from <paramref name="attacker"/>:
    /// the branch it is in after that.
    /// </summary>
    private static string BranchAfterAHitBy(Func<TFField, ActorId> attacker)
    {
        var f = new TFField();
        f.Step(TFField.Self(Mid), TFField.Ctf());
        Assert.Equal(TFDecider.Fetch, f.Branch);
        ObjectiveKnowledge taken = TFField.Ctf(red: ObjectiveState.Carried, redAt: Mid + new AiVector(300f, 0f, 0f), redCarrier: Enemy.Slot);
        f.StepHearing(TFField.Self(Mid, health: 155), taken, [AiEvent.Damaged(f.Bot, new Tick(f.Tick + 1), attacker(f), 45f, default, 0)]);
        return f.Branch;
    }

    /// <summary>A Soldier committed to a branch stays in it through its own rocket jump's damage.</summary>
    [Fact]
    public void ACommittedBotStaysInItsBranchThroughItsOwnBlast() => Assert.Equal(TFDecider.Fetch, BranchAfterAHitBy(f => f.Bot));

    /// <summary>The same damage from an enemy ends the commitment, and the better branch is taken at once.</summary>
    [Fact]
    public void ACommittedBotLeavesItsBranchForAnEnemysHit() => Assert.Equal(TFDecider.ChaseCarrier, BranchAfterAHitBy(_ => Enemy));

    /// <summary>The control for both: with no hit at all the commitment holds, so it was the hit that ended it.</summary>
    [Fact]
    public void WithNoHitTheCommitmentHolds()
    {
        var f = new TFField();
        f.Step(TFField.Self(Mid), TFField.Ctf());
        f.Step(TFField.Self(Mid), TFField.Ctf(red: ObjectiveState.Carried, redAt: Mid + new AiVector(300f, 0f, 0f), redCarrier: Enemy.Slot));
        Assert.Equal(TFDecider.Fetch, f.Branch);
    }

    [Fact]
    public void TheBotsOwnBlastIsNoHurtByAnother()
    {
        var f = new TFField();
        f.Memory.Apply(AiEvent.Damaged(f.Bot, new Tick(10), f.Bot, 45f, default, 0));
        Assert.False(f.Decider.HurtByAnother());
    }

    [Fact]
    public void AnEnemysHitIsAHurtByAnotherOnce()
    {
        var f = new TFField();
        f.Memory.Apply(AiEvent.Damaged(f.Bot, new Tick(10), Enemy, 45f, new AiVector(1f, 0f, 0f), 0));
        Assert.True(f.Decider.HurtByAnother());
        Assert.False(f.Decider.HurtByAnother(), "the same hit, asked about again");
    }
}
