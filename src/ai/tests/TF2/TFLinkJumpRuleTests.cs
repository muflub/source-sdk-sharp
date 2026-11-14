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
    public void ALinkWithASecondRocketIsBegunOnlyWithBothRocketsAndOneToSpareLoaded()
    {
        // Link 0's plan with a second rocket in the air (lane G7b): three rockets loaded, not two.
        (TFField Field, JumpBook Book) DoubleStanding()
        {
            var (g, ids) = Grids.Parse("..... .....");
            NavLinks links = NavLinks.Build(g, [new NavLink(ids[4, 0], ids[6, 0], NavLinkKind.Blast, 1f, TFJumpAdvice.Cost, 1f, 0)], new NavLinkCosts { RunSpeed = 240f });
            var book = new JumpBook(new JumpBookSettings());
            JumpPlan plan = TFJumpAdvice.Plan(id: NavLinks.Argument(0)) with { StartsStanding = true };
            book.SetLinks(links, [plan with { Recipe = plan.Recipe with { Second = new JumpSecondShot(plan.Recipe.Takeoff, 54, 70, 89f, 180f) } }]);
            var f = new TFField(tune: p => p with { GlanceRate = 0f }, jumps: book);
            book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(0, links.Hash, TFField.BlueFlag, true));
            return (f, book);
        }

        var (two, twoBook) = DoubleStanding();
        Assert.False(two.BeginsJump(two.Step(Still(clip: TFDecider.RocketJumpRockets), TFField.Ctf())));
        Assert.Equal(1L, twoBook.LinkRefusals(JumpLinkRefusal.Clip));
        var (three, _) = DoubleStanding();
        Assert.True(three.BeginsJump(three.Step(Still(clip: TFDecider.RocketJumpRockets + 1), TFField.Ctf())));

        // The control: a one-rocket link is begun with two.
        var (single, _) = Standing();
        Assert.True(single.BeginsJump(single.Step(Still(clip: TFDecider.RocketJumpRockets), TFField.Ctf())));
    }

    [Fact]
    public void AClassThatIsNoSoldierBeginsNoLinkJump()
    {
        var (f, _) = Standing();
        Assert.False(f.BeginsJump(f.Step(TFField.Self(At, playerClass: TFClass.Scout), TFField.Ctf())));
    }

    // ------------------------------------------------------------------ chains and the jump before (lane G5e)

    /// <summary>Two links on the way, 0 and 1, each with a plan whose blast costs <see cref="TFJumpAdvice.Cost"/>.</summary>
    private static (JumpBook Book, NavLinks Links) Chain()
    {
        var (g, ids) = Grids.Parse("..... ..... .....");
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[4, 0], ids[6, 0], NavLinkKind.Blast, 1f, TFJumpAdvice.Cost, 1f, 0), new NavLink(ids[10, 0], ids[12, 0], NavLinkKind.Blast, 1f, TFJumpAdvice.Cost, 1f, 1)],
                                         new NavLinkCosts { RunSpeed = 240f });
        var book = new JumpBook(new JumpBookSettings());
        book.SetLinks(links, [TFJumpAdvice.Plan(id: NavLinks.Argument(0)) with { StartsStanding = true }, TFJumpAdvice.Plan(id: NavLinks.Argument(1)) with { StartsStanding = true }]);
        return (book, links);
    }

    /// <summary>The book's route links, and one stretch's plan for the first stretch asked about: a jump on the way before a link.</summary>
    private sealed class BookAndStretch(JumpBook book) : IJumpAdvisor
    {
        private JumpPlan? _stretch = TFJumpAdvice.Plan();

        public JumpPlan? Advise(in JumpStretchAsk ask)
        {
            JumpPlan? plan = _stretch;
            _stretch = null;
            return plan;
        }

        public JumpPlan? RouteLink(int slot, Tick tick, AiVector goal) => book.RouteLink(slot, tick, goal);

        public void LinkRefused(int slot, Tick tick, AiVector goal, JumpLinkRefusal why) => book.LinkRefused(slot, tick, goal, why);
    }

    /// <summary>Decides standing still until a jump begun has let the bot go: its approach's hold over, the bot on the ground.</summary>
    private static void Down(TFField f, int health = 200)
    {
        for (int i = 0; i * DecideSchedule.Period * 0.015f <= TFDecider.RocketJumpBusySeconds + 0.1f; i++)
            Assert.False(f.BeginsJump(f.Step(Still(health: health), TFField.Ctf())), "no jump while the last has the bot");
    }

    /// <summary>The bot took link 0 and came down by link 1's start, which its follower says it stands at: well inside the jumps' spacing.</summary>
    private static (TFField Field, JumpBook Book, int First) AtTheSecondLink(int health = 200)
    {
        var (book, links) = Chain();
        var f = new TFField(tune: p => p with { GlanceRate = 0f }, jumps: book);
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(0, links.Hash, TFField.BlueFlag, true));
        Assert.True(f.BeginsJump(f.Step(Still(), TFField.Ctf())), "the control: the first link is begun");
        int first = f.Tick;
        // On the link the follower says nothing; down by the second, it says the bot stands at its start.
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), NavLinkStep.None);
        Down(f, health);
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(1, links.Hash, TFField.BlueFlag, true));
        return (f, book, first);
    }

    [Fact]
    public void TheSecondLinkOfAChainIsBegunWithoutWaitingTheJumpsSpacing()
    {
        var (f, _, first) = AtTheSecondLink();
        IntentCommand second = f.Step(Still(), TFField.Ctf());
        Assert.True((f.Tick - first) * 0.015f < TFDecider.RocketJumpEverySeconds, "the control: inside the jumps' spacing");
        Assert.True(f.BeginsJump(second), "the second link is begun at its start");
        Assert.Equal(1, NavLinks.LinkOf(AbilityCall.Argument(second.Action.WeaponSlot)));
        Assert.Equal(2, f.Decider.RocketJumps);
    }

    [Fact]
    public void ASecondLinkWhoseBlastWouldTakeTheBotUnderItsFloorIsRefused()
    {
        int health = (int)(Floor + TFJumpAdvice.Cost) - 1;
        var (f, book, _) = AtTheSecondLink(health);
        Assert.False(f.BeginsJump(f.Step(Still(health: health), TFField.Ctf())));
        Assert.Equal(1L, book.LinkRefusals(JumpLinkRefusal.Health));
        // The control: with the health for it, the same second link is begun.
        var (enough, _, _) = AtTheSecondLink((int)(Floor + TFJumpAdvice.Cost));
        Assert.True(enough.BeginsJump(enough.Step(Still(health: (int)(Floor + TFJumpAdvice.Cost)), TFField.Ctf())));
    }

    /// <summary>
    /// The skip after a reload (lane G5e): the bot's own stretch jump on its way from its spawn set the jumps' spacing, and
    /// at the link's start the tree refused the link for it until the follower's wait ran out. A link the route takes is
    /// not spaced from the jump before it.
    /// </summary>
    [Fact]
    public void AStretchJumpJustBeforeDoesNotHoldALinkBackAtItsStart()
    {
        var (book, links) = Chain();
        var f = new TFField(tune: p => p with { GlanceRate = 0f }, jumps: new BookAndStretch(book));
        Assert.True(f.BeginsJump(f.Step(TFField.Self(At, velocity: new AiVector(240f, 0f, 0f)), TFField.Ctf())), "the control: a stretch jump on the way");
        Assert.False(f.Decider.LastJump!.StartsStanding);
        Down(f);
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(0, links.Hash, TFField.BlueFlag, true));
        Assert.True(f.BeginsJump(f.Step(Still(), TFField.Ctf())));
        Assert.Equal((2, true), (f.Decider.RocketJumps, f.Decider.LastJump!.StartsStanding));
        Assert.Equal(0L, book.LinkRefusals(JumpLinkRefusal.Spacing));
    }

    [Fact]
    public void AGlanceDoesNotHoldALinkBackAtItsStart()
    {
        var (book, links) = Chain();
        var f = new TFField(tune: p => p with { GlanceRate = 1000f }, jumps: book);
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(0, links.Hash, TFField.BlueFlag, false));
        f.Step(Still(), TFField.Ctf());
        Assert.Equal(1, f.Decider.Glances);
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(0, links.Hash, TFField.BlueFlag, true));
        Assert.True(f.BeginsJump(f.Step(Still(), TFField.Ctf())));
    }

    [Fact]
    public void AStretchJumpStillWaitsTheSpacingAfterALink()
    {
        var (book, links) = Chain();
        var advisor = new BookAndStretch(book);
        var f = new TFField(tune: p => p with { GlanceRate = 0f }, jumps: advisor);
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(0, links.Hash, TFField.BlueFlag, true));
        Assert.True(f.BeginsJump(f.Step(Still(), TFField.Ctf())));
        int first = f.Tick;
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), NavLinkStep.None);
        Down(f);
        Assert.False(f.BeginsJump(f.Step(TFField.Self(At, velocity: new AiVector(240f, 0f, 0f)), TFField.Ctf())), "a stretch inside the spacing");
        Assert.Equal(1, f.Decider.RocketJumps);
        // The control: once the spacing is over, the same stretch is jumped.
        while ((f.Tick + DecideSchedule.Period - first) * 0.015f < TFDecider.RocketJumpEverySeconds)
            f.Step(Still(), TFField.Ctf());
        Assert.True(f.BeginsJump(f.Step(TFField.Self(At, velocity: new AiVector(240f, 0f, 0f)), TFField.Ctf())));
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
