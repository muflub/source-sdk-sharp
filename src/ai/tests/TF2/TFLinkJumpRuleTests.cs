using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Ai.Tests.Nav;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A blast link the route takes, as the tree begins it (lane G5c-2b): the follower says the bot stands at the link's
/// start, and the tree begins the link's jump under the jump's own rule (nobody in sight, its health after the blast
/// over the floor, a rocket to spare, ...) with a one-shot that names the link. On the synthetic field with the
/// level's real book, the follower's report put in as the main thread would.
/// </summary>
public sealed class TFLinkJumpRuleTests
{
    private const float Floor = TFDecider.RocketJumpHealthFloor;
    private static readonly AiVector At = new(300f, 256f, 0f);
    private static readonly ActorId Enemy = new(5, 0x4000_0001);

    /// <summary>A plan for link 0 whose blast costs <see cref="TFJumpAdvice.Cost"/>.</summary>
    private static (JumpBook Book, NavLinks Links) Book()
    {
        var (g, ids) = Grids.Parse("..... .....");
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[4, 0], ids[6, 0], NavLinkKind.Blast, 1f, TFJumpAdvice.Cost, 1f, 0)], new NavLinkCosts { RunSpeed = 240f });
        var book = new JumpBook(new JumpBookSettings());
        book.SetLinks(links, [TFJumpAdvice.Plan(id: NavLinks.Argument(0)) with { StartsStanding = true }]);
        return (book, links);
    }

    /// <summary>The field, its book, and the follower's report that the bot stands at link 0's start on its way to the blue flag, said before the next decide.</summary>
    private static (TFField Field, JumpBook Book) Standing(bool atStart = true)
    {
        var (book, links) = Book();
        var f = new TFField(tune: p => p with { GlanceRate = 0f }, jumps: book);
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(0, links.Hash, TFField.BlueFlag, atStart));
        return (f, book);
    }

    private static SelfState Still(int health = 200, int clip = 4) => TFField.Self(At, health: health, clip: clip);

    [Fact]
    public void AtALinksStartTheTreeBeginsItsJumpAndTheOneShotNamesTheLink()
    {
        var (f, _) = Standing();
        IntentCommand jump = f.Step(Still(), TFField.Ctf());
        Assert.True(f.BeginsJump(jump), "standing still, no stretch jump is asked about: this is the link's");
        Assert.Equal(0, NavLinks.LinkOf(AbilityCall.Argument(jump.Action.WeaponSlot)));
        Assert.Equal((1, true), (f.Decider.RocketJumps, f.Decider.LastJump!.StartsStanding));
    }

    [Fact]
    public void NoLinkJumpWhenTheFollowerSaysTheBotIsNotAtItsStart()
    {
        var (f, _) = Standing(atStart: false);
        Assert.False(f.BeginsJump(f.Step(Still(), TFField.Ctf())));
    }

    [Fact]
    public void NoLinkJumpWithAnEnemyInSight()
    {
        var (f, _) = Standing();
        Assert.False(f.BeginsJump(f.Step(Still(), TFField.Ctf(), (Enemy, new AiVector(1200f, 256f, 0f), AiVector.Zero, TFField.Blue))));
    }

    [Fact]
    public void NoLinkJumpWhoseBlastWouldTakeTheBotUnderItsHealthFloor()
    {
        var (low, _) = Standing();
        Assert.False(low.BeginsJump(low.Step(Still(health: (int)(Floor + TFJumpAdvice.Cost) - 1), TFField.Ctf())));
        var (enough, _) = Standing();
        Assert.True(enough.BeginsJump(enough.Step(Still(health: (int)(Floor + TFJumpAdvice.Cost)), TFField.Ctf())));
    }

    [Fact]
    public void NoLinkJumpWithoutARocketToSpare()
    {
        var (f, _) = Standing();
        Assert.False(f.BeginsJump(f.Step(Still(clip: TFDecider.RocketJumpRockets - 1), TFField.Ctf())));
    }

    [Fact]
    public void AClassThatIsNoSoldierBeginsNoLinkJump()
    {
        var (f, _) = Standing();
        Assert.False(f.BeginsJump(f.Step(TFField.Self(At, playerClass: TFClass.Scout), TFField.Ctf())));
    }

    [Fact]
    public void AnErrandIsTheTreesGoalWithNobodyInSight()
    {
        var (f, book) = Standing(atStart: false);
        var place = new AiVector(1500f, 300f, 0f);
        book.SetErrand(f.Bot.Slot, place);
        IntentCommand go = f.Step(Still(), TFField.Ctf());
        Assert.Equal((MoveKind.Follow, place, "errand"), (go.Move.Kind, go.Move.Corner0, f.Decider.Doing.Leaf));
        IntentCommand fight = f.Step(Still(), TFField.Ctf(), (Enemy, new AiVector(1200f, 256f, 0f), AiVector.Zero, TFField.Blue));
        Assert.NotEqual("errand", f.Decider.Doing.Leaf);
        book.SetErrand(f.Bot.Slot, null);
        f.Step(Still(), TFField.Ctf());
        Assert.NotEqual("errand", f.Decider.Doing.Leaf);
        _ = fight;
    }
}
