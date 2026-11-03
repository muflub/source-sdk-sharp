using SourceSharp.Ai.Hl2mp.Senses;
using Xunit;

namespace SourceSharp.Ai.Tests.Senses;

/// <summary>
/// The damage, death and scoreboard taps (plan_hl2mp_ai §4.3, Phase 4 step 4):
/// what each bot's own HUD shows of a hit, a death and the scoreboard.
/// </summary>
public sealed class CombatTapTests
{
    private static readonly ActorId A = new(2, 1), B = new(3, 2), C = new(4, 3);
    private static readonly ActorId Human = new(1, 0x4000_0005);

    [Fact]
    public void AHitIsTheVictimsDamageAndTheAttackersHitMarkerAndNobodyElses()
    {
        var into = new List<AiEvent>();
        CombatTap.Hurt(A, B, 20, 80, new AiVector(1, 0, 0), [A, B, C], new Tick(4), into);
        Assert.Equal(2, into.Count);
        AiEvent hurt = into.Single(e => e.Bot == A);
        Assert.Equal((AiEventKind.Damaged, B, 20f, new AiVector(1, 0, 0)), (hurt.Kind, hurt.Other, hurt.Amount, hurt.Position));
        AiEvent marker = into.Single(e => e.Bot == B);
        Assert.Equal((AiEventKind.DealtDamage, A, 20f, 80), (marker.Kind, marker.Other, marker.Amount, marker.Code));
        Assert.DoesNotContain(into, e => e.Bot == C);
    }

    [Fact]
    public void AHumanHurtByTheWorldTellsNoBot()
    {
        var into = new List<AiEvent>();
        CombatTap.Hurt(Human, ActorId.None, 20, 80, AiVector.Zero, [A, B, C], new Tick(4), into);
        Assert.Empty(into);
    }

    [Fact]
    public void EveryBotsKillFeedShowsADeathAndTheVictimIsKilled()
    {
        var into = new List<AiEvent>();
        CombatTap.Died(A, Human, [A, B, C], new Tick(7), into);
        Assert.All([A, B, C], b => Assert.Contains(into, e => e.Bot == b && e.Kind == AiEventKind.KillFeed && e.Other == A && e.Killer == Human));
        Assert.Equal(A, Assert.Single(into, e => e.Kind == AiEventKind.Killed).Bot);
        Assert.Equal(Human, into.Single(e => e.Kind == AiEventKind.Killed).Other);
    }

    [Fact]
    public void TheLeaderRanksFirstAndATieGoesToFewerDeaths()
    {
        ScoreRow[] rows = [new(A, 3, 5), new(B, 7, 1), new(Human, 3, 2), new(C, 0, 0)];
        Assert.Equal([3, 1, 2, 4], CombatTap.Ranks(rows));
    }
}
