using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Ai.Tests.Nav;
using SourceSharp.Ai.Tests.TF2;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The tree's side of a jump to health (lane G9, the user's ruling of 2026-11-13): a link or a stretch jump that lands by a
/// pack the bot believes is there, or a cabinet it may use, is begun under the health floor when the place pays for it, and
/// the bot takes that place next; one that lands by nothing keeps the floor; the switch off keeps it everywhere; the Course
/// branch does not walk back to heal for such a link, and heals for one the tree refused at its start. On the synthetic
/// field with the level's real book.
/// </summary>
public sealed class TFJumpHealthTests
{
    private const float Floor = TFDecider.RocketJumpHealthFloor, Cost = TFJumpAdvice.Cost;
    private static readonly AiVector At = new(300f, 256f, 0f);

    /// <summary>A small pack by the link's landing (the plan's touch is at x 1690).</summary>
    private static readonly ItemSpawn Pack = new(0, "item_healthkit_small", ItemKind.HealthSmall, new AiVector(1700f, 256f, 0f), 10f);

    private static JumpHeal HealAtPack => new(0, false, Pack.Kind, 0, Pack.Position, 10f);

    /// <summary>A plan for link 0 whose blast costs <paramref name="cost"/>, carrying <paramref name="heals"/>.</summary>
    private static (JumpBook Book, NavLinks Links) Book(JumpHeal[] heals, float cost = Cost)
    {
        var (g, ids) = Grids.Parse("..... .....");
        NavLinks links = NavLinks.Build(g, [new NavLink(ids[4, 0], ids[6, 0], NavLinkKind.Blast, 1f, cost, 1f, 0)], new NavLinkCosts { RunSpeed = 240f });
        var book = new JumpBook(new JumpBookSettings());
        book.SetLinks(links, [TFJumpAdvice.Plan(id: NavLinks.Argument(0), health: cost) with { StartsStanding = true, Heals = [.. heals] }]);
        return (book, links);
    }

    /// <summary>The field (its one pack by the landing), the book, and the follower's report that the bot stands at link 0's start.</summary>
    private static (TFField Field, JumpBook Book) Standing(JumpHeal[]? heals = null, TFFeelSwitches? feel = null, TeamZone[]? zones = null,
                                                          Func<DecisionParams, DecisionParams>? tune = null, float cost = Cost)
    {
        var (book, links) = Book(heals ?? [HealAtPack], cost);
        var f = new TFField(tune: tune ?? (p => p with { GlanceRate = 0f }), jumps: book, packs: [Pack], feel: feel, zones: zones);
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(0, links.Hash, TFField.BlueFlag, true));
        return (f, book);
    }

    private static SelfState Still(int health) => TFField.Self(At, health: health);

    /// <summary>Health the plain rule refuses the link at by one, the rule by health allows: it lands at 89.</summary>
    private const int UnderPlain = (int)(Floor + Cost) - 1;

    [Fact]
    public void ALinkThatLandsByAPackIsBegunUnderTheFloorAndOneByNothingIsNot()
    {
        var (byPack, _) = Standing();
        Assert.True(byPack.BeginsJump(byPack.Step(Still(UnderPlain), TFField.Ctf())));
        Assert.Equal(1, byPack.Decider.HealthJumps);
        var (byNothing, book) = Standing(heals: []);
        Assert.False(byNothing.BeginsJump(byNothing.Step(Still(UnderPlain), TFField.Ctf())));
        Assert.Equal(1L, book.LinkRefusals(JumpLinkRefusal.Health));
    }

    /// <summary>A heal place read where the pack came to rest (1.3 u off the map's place, after a changelevel) is the same pack; one a pack's size off is not.</summary>
    [Fact]
    public void AHealPlaceAFewUnitsOffTheMapsPackIsThatPack()
    {
        var (near, _) = Standing(heals: [HealAtPack with { At = Pack.Position + new AiVector(0f, 0f, 1.3f) }]);
        Assert.True(near.BeginsJump(near.Step(Still(UnderPlain), TFField.Ctf())));
        var (far, _) = Standing(heals: [HealAtPack with { At = Pack.Position + new AiVector(TFDecider.PackSameUnits + 1f, 0f, 0f) }]);
        Assert.False(far.BeginsJump(far.Step(Still(UnderPlain), TFField.Ctf())));
    }

    [Fact]
    public void AJumpThePlainRuleCoversIsNoJumpByHealth()
    {
        var (f, _) = Standing();
        Assert.True(f.BeginsJump(f.Step(Still(UnderPlain + 1), TFField.Ctf())));
        Assert.Equal(0, f.Decider.HealthJumps);
    }

    /// <summary>
    /// The hard floor: a link of 40 begun at 90 lands at 50, at 89 it would land at 49 and is refused for the health (over the
    /// lull's 40 %, so the bot is not off for health instead); and at 80, under the floor of 90, the flat health part no longer
    /// refuses a jump first: a cheaper link of 30 lands at 50 and is begun.
    /// </summary>
    [Fact]
    public void AJumpByHealthMustLandWithTheHardFloor()
    {
        var (enough, _) = Standing(cost: 40f);
        Assert.True(enough.BeginsJump(enough.Step(Still(90), TFField.Ctf())));
        var (short1, book) = Standing(cost: 40f);
        Assert.False(short1.BeginsJump(short1.Step(Still(89), TFField.Ctf())));
        Assert.Equal(1L, book.LinkRefusals(JumpLinkRefusal.Health));
        var (under, _) = Standing(tune: Bold);
        Assert.True(under.BeginsJump(under.Step(Still((int)(JumpHealthRule.HardFloor + Cost)), TFField.Ctf())));
    }

    [Fact]
    public void APackTheBotSawTakenPaysForNothing()
    {
        var (f, _) = Standing();
        f.Memory.Board.SeeItem(0, false, new Tick(f.Tick), 0.015f);
        Assert.False(f.BeginsJump(f.Step(Still(UnderPlain), TFField.Ctf())));
    }

    /// <summary>A pack seen taken is believed back once its respawn has passed since: it pays again.</summary>
    [Fact]
    public void APackSeenTakenLongerAgoThanItsRespawnPaysAgain()
    {
        var (book, links) = Book([HealAtPack]);
        var f = new TFField(tune: p => p with { GlanceRate = 0f }, jumps: book, packs: [Pack]);
        f.Memory.Board.SeeItem(0, false, new Tick(f.Tick), 0.015f);
        for (int i = 0; i * DecideSchedule.Period * 0.015f <= Pack.RespawnSeconds + 0.5f; i++)
            f.Step(Still(200), TFField.Ctf());
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(0, links.Hash, TFField.BlueFlag, true));
        Assert.True(f.BeginsJump(f.Step(Still(UnderPlain), TFField.Ctf())));
        Assert.Equal(false, f.Memory.Board.Items[0].Present);
    }

    [Fact]
    public void WithTheSwitchOffEveryJumpKeepsTheFloor()
    {
        var (f, _) = Standing(feel: TFFeelSwitches.All with { JumpHealth = false });
        Assert.False(f.BeginsJump(f.Step(Still(UnderPlain), TFField.Ctf())));
        var (control, _) = Standing(feel: TFFeelSwitches.All with { JumpHealth = false });
        Assert.True(control.BeginsJump(control.Step(Still(UnderPlain + 1), TFField.Ctf())), "the control: the plain rule still holds");
    }

    [Fact]
    public void ACabinetOfTheBotsTeamOrEveryTeamPaysAndTheOthersDoesNot()
    {
        AiVector mid = new(1700f, 256f, 0f);
        foreach ((int team, bool begun) in ((int, bool)[])[(0, true), (TFField.Red, true), (TFField.Blue, false)])
        {
            var (f, _) = Standing(heals: [new JumpHeal(0, true, ItemKind.Other, team, mid, 10f)]);
            Assert.True(begun == f.BeginsJump(f.Step(Still(UnderPlain), TFField.Ctf())), $"a cabinet of team {team}");
        }
    }

    [Fact]
    public void AStretchJumpThatLandsByAPackIsBegunUnderTheFloor()
    {
        static SelfState Running(int health) => TFField.Self(At, health: health, velocity: new AiVector(240f, 0f, 0f));
        var byPack = new TFField(tune: p => p with { GlanceRate = 0f }, jumps: new TFJumpAdvice(TFJumpAdvice.Plan() with { Heals = [HealAtPack] }), packs: [Pack]);
        Assert.True(byPack.BeginsJump(byPack.Step(Running(UnderPlain), TFField.Ctf())));
        var byNothing = new TFField(tune: p => p with { GlanceRate = 0f }, jumps: new TFJumpAdvice(TFJumpAdvice.Plan()), packs: [Pack]);
        Assert.False(byNothing.BeginsJump(byNothing.Step(Running(UnderPlain), TFField.Ctf())));
    }

    /// <summary>A reload under way does not hold back a link at its start when the rockets to begin it are loaded (the trigger ends the reload); with too few it does.</summary>
    [Fact]
    public void AReloadUnderWayDoesNotHoldBackALinkWithItsRocketsLoaded()
    {
        var (loaded, _) = Standing();
        Assert.True(loaded.BeginsJump(loaded.Step(TFField.Self(At, clip: 2, extra: SelfFlags.Reloading), TFField.Ctf())));
        var (shy, book) = Standing();
        Assert.False(shy.BeginsJump(shy.Step(TFField.Self(At, clip: 1, extra: SelfFlags.Reloading), TFField.Ctf())));
        Assert.Equal(1L, book.LinkRefusals(JumpLinkRefusal.Clip));
    }

    // ------------------------------------------------------------------ the pack, next

    /// <summary>A bot that goes for health only under 10 % (and the lull's 40 %): landing at 89 it would not go for it of its own.</summary>
    private static DecisionParams Bold(DecisionParams p) => p with { GlanceRate = 0f, RetreatHealthPct = 0.1f, GiveUpHealth = 0.1f };

    /// <summary>
    /// Decides standing still until the jump begun has let the bot go (its approach over, on the ground): it has landed. On the
    /// link and after it the follower says the bot stands at no link's start.
    /// </summary>
    private static void Down(TFField f, JumpBook book, int health)
    {
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), NavLinkStep.None);
        for (int i = 0; i * DecideSchedule.Period * 0.015f <= TFDecider.RocketJumpBusySeconds + 0.1f; i++)
            f.Step(Still(health), TFField.Ctf());
    }

    [Fact]
    public void DownFromAJumpByHealthTheBotGoesForThatPackNext()
    {
        var (f, book) = Standing(tune: Bold);
        Assert.True(f.BeginsJump(f.Step(Still(UnderPlain), TFField.Ctf())));
        Down(f, book, UnderPlain - (int)Cost);
        // Within the root's commitment to the branch it ran (Fetch's 2 s), at most.
        for (int i = 0; i * DecideSchedule.Period * 0.015f < 2.5f && f.Branch != TFDecider.Survive; i++)
            f.Step(Still(UnderPlain - (int)Cost), TFField.Ctf());
        Assert.Equal((TFDecider.Survive, Pack.Position), (f.Branch, f.Decider.Doing.Goal));

        // The control: a bot at the same health that came by no such jump does not go for health.
        var control = new TFField(tune: Bold, packs: [Pack]);
        for (int i = 0; i * DecideSchedule.Period * 0.015f < 4f; i++)
        {
            control.Step(Still(UnderPlain - (int)Cost), TFField.Ctf());
            Assert.NotEqual(TFDecider.Survive, control.Branch);
        }
    }

    /// <summary>In the air on a jump by health, hurt by its own blast and going for health, the bot goes for that jump's place, not a nearer-looking other.</summary>
    [Fact]
    public void InTheAirOnAJumpByHealthTheHealPlaceIsThatJumpsPlace()
    {
        // A second pack nearer the takeoff: by a straight line the heal branch's pick.
        var near = new ItemSpawn(1, "item_healthkit_small", ItemKind.HealthSmall, new AiVector(400f, 256f, 0f), 10f);
        var (book, links) = Book([HealAtPack]);
        var f = new TFField(tune: p => p with { GlanceRate = 0f, RetreatHealthPct = 0.5f }, jumps: book, packs: [Pack, near]);
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(0, links.Hash, TFField.BlueFlag, true));
        Assert.True(f.BeginsJump(f.Step(Still(UnderPlain), TFField.Ctf())));
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), NavLinkStep.None);
        // Up in the air at 89: under half (its share), it goes for health.
        SelfState air = TFField.Self(At + new AiVector(0f, 0f, 120f), health: UnderPlain - (int)Cost) with { Flags = SelfFlags.Alive };
        // Within the root's commitment to the branch it ran (Fetch's 2 s), at most; still in the air.
        for (int i = 0; i * DecideSchedule.Period * 0.015f < 2.5f && f.Branch != TFDecider.Survive; i++)
            f.Step(air, TFField.Ctf());
        Assert.Equal((TFDecider.Survive, Pack.Position), (f.Branch, f.Decider.Doing.Goal));
    }

    /// <summary>A heal that broke off starts its patience again: going for the same pack long after, the bot does not give it up at once.</summary>
    [Fact]
    public void GoingAgainForAPackLongAfterAVisitDoesNotGiveItUpAtOnce()
    {
        var f = new TFField(tune: p => p with { GlanceRate = 0f }, packs: [Pack]);
        f.Step(Still(70), TFField.Ctf());
        Assert.Equal((TFDecider.Survive, Pack.Position), (f.Branch, f.Decider.Doing.Goal));
        // Healed; well over the patience on other branches; hurt again: the same pack, not given up.
        for (int i = 0; i * DecideSchedule.Period * 0.015f < TFDecider.HealPatienceSeconds + 2f; i++)
            f.Step(Still(200), TFField.Ctf());
        Assert.NotEqual(TFDecider.Survive, f.Branch);
        f.Step(Still(70), TFField.Ctf());
        Assert.Equal((TFDecider.Survive, Pack.Position), (f.Branch, f.Decider.Doing.Goal));
    }

    [Fact]
    public void TheHealAfterAJumpByHealthIsCountedWithItsHealthsAndSeconds()
    {
        var (f, book) = Standing(tune: Bold);
        f.Step(Still(UnderPlain), TFField.Ctf());
        int landed = UnderPlain - (int)Cost;
        Down(f, book, landed);
        f.Step(Still(landed), TFField.Ctf());
        f.Step(Still(landed + 40), TFField.Ctf());
        (int Landed, int After, float Seconds) heal = Assert.Single(f.Decider.HealthJumpHeals);
        Assert.Equal((landed, landed + 40), (heal.Landed, heal.After));
        Assert.InRange(heal.Seconds, 0.01f, 1f);
        Assert.NotEqual(TFDecider.Survive, f.Branch);
    }

    [Fact]
    public void APackSeenTakenAfterTheLandingIsCountedAndOwedNoMore()
    {
        var (f, book) = Standing(tune: Bold);
        f.Step(Still(UnderPlain), TFField.Ctf());
        int landed = UnderPlain - (int)Cost;
        Down(f, book, landed);
        f.Memory.Board.SeeItem(0, false, new Tick(f.Tick), 0.015f);
        f.Step(Still(landed), TFField.Ctf());
        Assert.Equal(1, f.Decider.HealthJumpGone);
        Assert.NotEqual(TFDecider.Survive, f.Branch);
    }

    [Fact]
    public void ADeathSoonAfterAJumpByHealthsLandingIsCounted()
    {
        var (f, book) = Standing(tune: Bold);
        f.Step(Still(UnderPlain), TFField.Ctf());
        Down(f, book, UnderPlain - (int)Cost);
        f.Step(TFField.Self(At, health: 0) with { Flags = SelfFlags.None }, TFField.Ctf());
        f.Step(TFField.Self(At, health: 0) with { Flags = SelfFlags.None }, TFField.Ctf());
        Assert.Equal(1, f.Decider.HealthJumpDeaths);
    }

    // ------------------------------------------------------------------ the Course branch's wait

    private static readonly AiVector Stage1 = new(800f, 256f, 0f), Stage2 = new(1600f, 256f, 0f);
    private static readonly TeamZone Cabinet = new(0, ZoneKind.Resupply, new AiVector(200f, 0f, 0f), new AiVector(328f, 128f, 128f));
    private static readonly AiVector CabinetMid = new(264f, 64f, 0f);

    private static ObjectiveKnowledge Points()
        => new([
            new Objective(20, ObjectiveKind.Capture, TFField.Red, 0, ObjectiveState.Won, Stage1),
            new Objective(21, ObjectiveKind.Capture, 0, (1 << TFField.Red) | (1 << TFField.Blue), ObjectiveState.Open, Stage2),
        ]);

    /// <summary>A need under 0 (a cheap link by a big pack, not taken by a bot further under the floor) sends the bot to heal as any need does.</summary>
    [Fact]
    public void ANeedUnderZeroIsHealedForAsAnyNeedIs()
    {
        var (book, links) = Book([]);
        var f = new TFField(tune: p => p with { GlanceRate = 0f, ScoreNoise = 0f }, jumps: book, zones: [Cabinet], packs: []);
        f.Step(Still(200), Points());
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(-1, links.Hash, Stage2, false) { NoWay = true, Need = -20f });
        f.Step(Still((int)Floor - 20 - 1), Points());
        Assert.Equal((TFDecider.Course, CabinetMid), (f.Branch, f.Decider.Doing.Goal));
        f.Step(Still((int)Floor - 20), Points());
        Assert.Equal(Stage2, f.Decider.Doing.Goal);
    }

    [Fact]
    public void ALinkTheTreeRefusedAtItsStartForThePackGoneIsHealedForAtItsBlastsCost()
    {
        var (book, links) = Book([HealAtPack], cost: 40f);
        var f = new TFField(tune: p => p with { GlanceRate = 0f, ScoreNoise = 0f }, jumps: book, zones: [Cabinet], packs: [Pack]);
        f.Memory.Board.SeeItem(0, false, new Tick(f.Tick), 0.015f);
        book.NoteRoute(f.Bot.Slot, new Tick(f.Tick), new NavLinkStep(0, links.Hash, Stage2, true));
        Assert.False(f.BeginsJump(f.Step(Still(100), Points())), "the pack is believed gone: the link is refused");
        f.Step(Still(100), Points());
        Assert.Equal((TFDecider.Course, CabinetMid), (f.Branch, f.Decider.Doing.Goal));

        // The control: with the pack believed there the link is begun at the same health.
        var (cBook, cLinks) = Book([HealAtPack], cost: 40f);
        var control = new TFField(tune: p => p with { GlanceRate = 0f, ScoreNoise = 0f }, jumps: cBook, zones: [Cabinet], packs: [Pack]);
        cBook.NoteRoute(control.Bot.Slot, new Tick(control.Tick), new NavLinkStep(0, cLinks.Hash, Stage2, true));
        Assert.True(control.BeginsJump(control.Step(Still(100), Points())));
    }
}
