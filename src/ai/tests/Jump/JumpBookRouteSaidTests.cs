using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.Tests.Nav;
using Xunit;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// What a bot's follower last said of its route to a goal (lane G7a): the newest report before the decide's tick of a
/// route to that goal, a link next or no way with what it needs; a report of a route elsewhere does not hide it.
/// </summary>
public class JumpBookRouteSaidTests
{
    private const int Slot = 5;
    private static readonly AiVector Goal = new(900f, 100f, 0f), Elsewhere = new(100f, 900f, 0f);

    private static (JumpBook Book, NavLinks Links) Book()
    {
        var (g, ids) = Grids.Parse("..... .....");
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[4, 0], ids[6, 0], NavLinkKind.Blast, 1f, 30f, 1f, 0)], new NavLinkCosts { RunSpeed = 240f });
        var book = new JumpBook(new JumpBookSettings());
        return (book, links);
    }

    private static NavLinkStep NoWay(NavLinks links, float need, AiVector? to = null) => new(-1, links.Hash, to ?? Goal, false) { NoWay = true, Need = need };

    [Fact]
    public void ANoWayReportForTheGoalIsReadWithItsNeed()
    {
        var (book, links) = Book();
        Assert.Null(book.RouteSaid(Slot, new Tick(100), Goal));
        book.NoteRoute(Slot, new Tick(90), NoWay(links, 30f));
        Assert.Equal((true, 30f), (book.RouteSaid(Slot, new Tick(100), Goal)!.Value.NoWay, book.RouteSaid(Slot, new Tick(100), Goal)!.Value.Need));
    }

    [Fact]
    public void AReportOfARouteElsewhereDoesNotHideTheGoalsAndIsNotTheGoals()
    {
        var (book, links) = Book();
        book.NoteRoute(Slot, new Tick(90), NoWay(links, 30f));
        book.NoteRoute(Slot, new Tick(95), NoWay(links, 0f, Elsewhere));
        book.NoteRoute(Slot, new Tick(96), NavLinkStep.None);
        Assert.Equal(30f, book.RouteSaid(Slot, new Tick(100), Goal)!.Value.Need);
        Assert.Equal(0f, book.RouteSaid(Slot, new Tick(100), Elsewhere)!.Value.Need);
        // A link next for the goal is newer than the no-way: it is the answer.
        book.NoteRoute(Slot, new Tick(97), new NavLinkStep(0, links.Hash, Goal, false));
        Assert.Equal((true, false), (book.RouteSaid(Slot, new Tick(100), Goal)!.Value.IsLink, book.RouteSaid(Slot, new Tick(100), Goal)!.Value.NoWay));
    }

    [Fact]
    public void AReportStampedAtTheDecidesTickOrAfterIsNotRead()
    {
        var (book, links) = Book();
        book.NoteRoute(Slot, new Tick(100), NoWay(links, 30f));
        Assert.Null(book.RouteSaid(Slot, new Tick(100), Goal));
        Assert.NotNull(book.RouteSaid(Slot, new Tick(101), Goal));
    }

    [Fact]
    public void ANewLevelForgetsThem()
    {
        var (book, links) = Book();
        book.NoteRoute(Slot, new Tick(90), NoWay(links, 30f));
        book.Clear();
        Assert.Null(book.RouteSaid(Slot, new Tick(100), Goal));
    }
}
