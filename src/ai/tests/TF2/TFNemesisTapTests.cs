using SourceSharp.Ai.TF2.Senses;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// Dominations as a bot's HUD shows them (plan_bots_teams_tf2_part_g G.2.5, <see cref="TFNemesisTap"/>):
/// a bot is told when its own HUD begins to mark someone as dominating it and when the mark goes
/// (the revenge), once each, and nothing of anyone who was never marked.
/// </summary>
public sealed class TFNemesisTapTests
{
    private static readonly ActorId A = new(2, 1), B = new(3, 2);
    private static readonly ActorId Human = new(5, 0x4000_0005);

    private readonly Dictionary<(ActorId Bot, ActorId Actor), bool> _told = new();
    private readonly List<AiEvent> _into = new();

    [Fact]
    public void ADominationIsToldOnceToTheBotItIsOver()
    {
        TFNemesisTap.Tell(A, Human, true, _told, new Tick(33), _into);
        TFNemesisTap.Tell(A, Human, true, _told, new Tick(66), _into);
        AiEvent e = Assert.Single(_into);
        Assert.Equal((AiEventKind.Nemesis, A, Human, 1, new Tick(33)), (e.Kind, e.Bot, e.Other, e.Code, e.Tick));
    }

    [Fact]
    public void SomeoneNeverMarkedIsNoNews()
    {
        TFNemesisTap.Tell(A, Human, false, _told, new Tick(33), _into);
        Assert.Empty(_into);
    }

    /// <summary>The revenge: the mod takes the row off and the bot is told the mark is gone.</summary>
    [Fact]
    public void TheMarkGoingIsToldToo()
    {
        TFNemesisTap.Tell(A, Human, true, _told, new Tick(33), _into);
        TFNemesisTap.Tell(A, Human, false, _told, new Tick(66), _into);
        Assert.Equal([1, 0], _into.Select(e => e.Code));
    }

    /// <summary>What one bot's HUD marks is that bot's alone: another bot is told nothing of it.</summary>
    [Fact]
    public void EachBotIsToldOfItsOwnHudOnly()
    {
        TFNemesisTap.Tell(A, Human, true, _told, new Tick(33), _into);
        TFNemesisTap.Tell(B, Human, false, _told, new Tick(33), _into);
        Assert.Equal([A], _into.Select(e => e.Bot));
    }
}
