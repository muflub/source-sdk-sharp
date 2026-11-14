using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.Tests.Nav;
using SourceSharp.Ai.Tests.TF2;
using Xunit;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The book and the level's links (lane G5c-2b): a link's number answers the link's plan; what a bot's follower
/// said of its route's next link is kept with the tick of the drive step that said it, and a decide reads the newest
/// said before its own tick, of the set the level has now, at the link's start, toward its own goal; an errand is
/// kept until called off.
/// </summary>
public class JumpBookLinkTests
{
    private const int Slot = 3;
    private static readonly AiVector Goal = new(900f, 100f, 0f);

    private static (JumpBook Book, NavLinks Links, JumpPlan Plan) Book()
    {
        var (g, ids) = Grids.Parse("..... .....");
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[4, 0], ids[6, 0], NavLinkKind.Blast, 1f, 30f, 1f, 0)], new NavLinkCosts { RunSpeed = 240f });
        JumpPlan plan = TFJumpAdvice.Plan(id: NavLinks.Argument(0)) with { StartsStanding = true };
        var book = new JumpBook(new JumpBookSettings());
        book.SetLinks(links, [plan]);
        return (book, links, plan);
    }

    private static NavLinkStep At(NavLinks links, bool atStart = true, AiVector? objective = null) => new(0, links.Hash, objective ?? Goal, atStart);

    /// <summary>Lane G5e: a decide that begins no link is counted by its reason only where the bot stands at a link's start for that decide.</summary>
    [Fact]
    public void ARefusalIsCountedOnlyAtALinksStart()
    {
        var (book, links, _) = Book();
        book.LinkRefused(Slot, new Tick(100), Goal, JumpLinkRefusal.Spacing);
        Assert.Equal(0, book.LinkRefusals(JumpLinkRefusal.Spacing));
        book.NoteRoute(Slot, new Tick(90), At(links, atStart: false));
        book.LinkRefused(Slot, new Tick(100), Goal, JumpLinkRefusal.Spacing);
        Assert.Equal(0, book.LinkRefusals(JumpLinkRefusal.Spacing));
        book.NoteRoute(Slot, new Tick(95), At(links));
        book.LinkRefused(Slot, new Tick(100), Goal, JumpLinkRefusal.Spacing);
        book.LinkRefused(Slot, new Tick(106), Goal, JumpLinkRefusal.Reloading);
        Assert.Equal((1L, 1L, 0L), (book.LinkRefusals(JumpLinkRefusal.Spacing), book.LinkRefusals(JumpLinkRefusal.Reloading), book.RouteCounts.Advised));
    }

    [Fact]
    public void ALinksNumberAnswersTheLinksPlan()
    {
        var (book, _, plan) = Book();
        Assert.Same(plan, book.Plan(NavLinks.Argument(0)));
        Assert.Null(book.Plan(NavLinks.Argument(1)));
        book.SetLinks(null, null);
        Assert.Null(book.Plan(NavLinks.Argument(0)));
    }

    [Fact]
    public void ADecideReadsTheNewestReportSaidBeforeItsTick()
    {
        var (book, links, plan) = Book();
        book.NoteRoute(Slot, new Tick(100), At(links));
        Assert.Null(book.RouteLink(Slot, new Tick(100), Goal));
        Assert.Same(plan, book.RouteLink(Slot, new Tick(101), Goal));
        // A report said on the decide's own tick or after it (the main thread runs on while a decide is on a worker) is not read.
        book.NoteRoute(Slot, new Tick(105), NavLinkStep.None);
        Assert.Same(plan, book.RouteLink(Slot, new Tick(105), Goal));
        Assert.Null(book.RouteLink(Slot, new Tick(106), Goal));
    }

    [Fact]
    public void AReportNotAtTheStartOfAnotherSetOrToAnotherGoalIsNoLink()
    {
        var (book, links, plan) = Book();
        book.NoteRoute(Slot, new Tick(1), At(links, atStart: false));
        Assert.Null(book.RouteLink(Slot, new Tick(2), Goal));
        book.NoteRoute(Slot, new Tick(2), At(links) with { Links = links.Hash ^ 1UL });
        Assert.Null(book.RouteLink(Slot, new Tick(3), Goal));
        book.NoteRoute(Slot, new Tick(3), At(links, objective: Goal + new AiVector(new JumpBookSettings().GoalSame + 1f, 0f, 0f)));
        Assert.Null(book.RouteLink(Slot, new Tick(4), Goal));
        Assert.Null(book.RouteLink(Slot + 1, new Tick(4), Goal));
        // The control: the same report, right, is the link.
        book.NoteRoute(Slot, new Tick(4), At(links));
        Assert.Same(plan, book.RouteLink(Slot, new Tick(5), Goal));
    }

    /// <summary>A level gone takes its links and what was said of them: the same map loaded again has a set of the same hash, and its bots have said nothing of it yet.</summary>
    [Fact]
    public void ALevelGoneTakesItsReportsAndLinksWithIt()
    {
        var (book, links, plan) = Book();
        book.NoteRoute(Slot, new Tick(1), At(links));
        book.Clear();
        Assert.Null(book.RouteLink(Slot, new Tick(2), Goal));
        Assert.Empty(book.LinkPlans());
        book.SetLinks(links, [plan]);
        Assert.Null(book.RouteLink(Slot, new Tick(2), Goal));
    }

    [Fact]
    public void TheLastFewChangesAreKeptAndARepeatIsNoChange()
    {
        var (book, links, plan) = Book();
        book.NoteRoute(Slot, new Tick(10), At(links));
        for (int t = 11; t < 11 + JumpBook.RouteKept - 1; t++)
            book.NoteRoute(Slot, new Tick(t), t % 2 == 0 ? At(links) : NavLinkStep.None);
        Assert.Same(plan, book.RouteLink(Slot, new Tick(11), Goal));
        // Said again unchanged, it is no change: the oldest is still kept.
        book.NoteRoute(Slot, new Tick(40), NavLinkStep.None);
        book.NoteRoute(Slot, new Tick(41), NavLinkStep.None);
        Assert.Same(plan, book.RouteLink(Slot, new Tick(11), Goal));
        Assert.Equal(JumpBook.RouteKept, book.RouteCounts.Notes);
    }

    [Fact]
    public void AnErrandIsKeptUntilCalledOff()
    {
        var book = new JumpBook(new JumpBookSettings());
        Assert.False(book.Errand(Slot, out _));
        book.SetErrand(Slot, Goal);
        Assert.True(book.Errand(Slot, out AiVector goal));
        Assert.Equal(Goal, goal);
        Assert.False(book.Errand(Slot + 1, out _));
        book.SetErrand(Slot, null);
        Assert.False(book.Errand(Slot, out _));
    }
}
