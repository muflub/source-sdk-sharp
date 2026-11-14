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
