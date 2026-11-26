using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// Teammates take a teleporter and stand at a dispenser (round 2's S8, TFDecider.Teleporter.cs, TFDecider.Buildings.cs): a
/// friendly entrance in sight, ready, whose exit the bot saw nearer its goal, is the way there; a hurt bot stands at a
/// friendly dispenser in sight until it is full.
/// </summary>
public sealed class TFTeleporterTests
{
    private const int Engie = 5, EntranceId = 800, ExitId = 801, DispenserId = 802;
    private static readonly AiVector Start = new(512f, 256f, 0f), EntranceAt = new(768f, 256f, 0f), ExitAt = new(3328f, 256f, 0f);
    private static readonly int Ready = (int)TFBuildingFlags.Ready;

    private static ThingFacts Entrance(int flags, int team = TFField.Red, AiVector? at = null)
        => new(EntranceId, ThingKind.Building, (int)TFThingType.TeleporterEntrance, at ?? EntranceAt, default, team, Engie, 1, -1, flags);

    private static ThingFacts Exit(int team = TFField.Red, AiVector? at = null)
        => new(ExitId, ThingKind.Building, (int)TFThingType.TeleporterExit, at ?? ExitAt, default, team, Engie, 1, -1, Ready);

    /// <summary>The goal after the bot, on its own side going for the enemy's flag, saw both ends of a teleporter (the entrance as <paramref name="entrance"/>).</summary>
    private static (AiVector Goal, TFField Field) Going(ThingFacts entrance, ThingFacts exit, TFClass playerClass = TFClass.Soldier)
    {
        var f = new TFField();
        SelfState self = TFField.Self(Start, playerClass: playerClass);
        for (int i = 0; i < 3; i++)
            f.StepSeeing(self, TFField.Ctf(), [entrance, exit]);
        return (f.Decider.Doing.Goal, f);
    }

    /// <summary>
    /// A ready friendly entrance 256 u off whose exit stands 512 u short of the enemy's flag: the bot on its way to the flag
    /// walks to the entrance (the walk across the field is 3,300 u; the trip saves most of it).
    /// </summary>
    [Fact]
    public void AReadyFriendlyTeleporterIsTakenWhenItsExitSavesTime()
    {
        (AiVector goal, TFField f) = Going(Entrance(Ready), Exit());
        Assert.Equal((TFDecider.Fetch, EntranceAt), (f.Branch, goal));
    }

    /// <summary>
    /// A ready entrance glimpsed on one decide between two weighs (the bot saw its exit first, then the entrance a moment, then
    /// neither) is still taken at the next weigh: a bot passing an entrance has it in view only a moment.
    /// </summary>
    [Fact]
    public void AnEntranceGlimpsedBetweenTwoWeighsIsTaken()
    {
        var f = new TFField();
        f.StepSeeing(TFField.Self(Start), TFField.Ctf(), [Exit()]);
        f.StepSeeing(TFField.Self(Start), TFField.Ctf(), [Entrance(Ready)]);
        var goals = new List<AiVector>();
        for (int i = 0; i < (int)(TFDecider.TeleporterSearchSeconds / (DecideSchedule.Period * 0.015f)) + 2; i++)
        {
            f.Step(TFField.Self(Start), TFField.Ctf());
            goals.Add(f.Decider.Doing.Goal);
        }

        Assert.Contains(EntranceAt, goals);
    }

    /// <summary>
    /// A recharging entrance (seen, not ready) is not taken, on any decide: stock takes a teleporter only if it is ready now.
    /// Nothing is given up either, since nothing was gone for.
    /// </summary>
    [Fact]
    public void ARechargingTeleporterIsNotTaken()
    {
        var f = new TFField();
        var goals = new List<AiVector>();
        for (int i = 0; i < 3; i++)
        {
            f.StepSeeing(TFField.Self(Start), TFField.Ctf(), [Entrance(0), Exit()]);
            goals.Add(f.Decider.Doing.Goal);
        }

        Assert.Equal(TFDecider.Fetch, f.Branch);
        Assert.DoesNotContain(EntranceAt, goals);
        Assert.Equal(0, f.Decider.TeleporterGiveUps);
    }

    /// <summary>An enemy's ready entrance and exit, seen as the friendly ones were, are never taken.</summary>
    [Fact]
    public void AnEnemysTeleporterIsNeverTaken()
    {
        (AiVector goal, _) = Going(Entrance(Ready, TFField.Blue), Exit(TFField.Blue));
        Assert.NotEqual(EntranceAt, goal);
    }

    /// <summary>An exit that is no nearer the goal (behind the bot, by its own spawn) saves nothing: the bot walks.</summary>
    [Fact]
    public void AnExitThatSavesNoTimeIsNotTaken()
    {
        (AiVector goal, _) = Going(Entrance(Ready), Exit(at: new AiVector(128f, 256f, 0f)));
        Assert.NotEqual(EntranceAt, goal);
    }

    /// <summary>A Medic never takes one on its own account (stock's ShouldOpportunisticallyTeleport).</summary>
    [Fact]
    public void AMedicDoesNotTakeATeleporter()
    {
        (AiVector goal, _) = Going(Entrance(Ready), Exit(), TFClass.Medic);
        Assert.NotEqual(EntranceAt, goal);
    }

    /// <summary>On its way to the entrance, the bot that sees it gone unready gives it up (stock's USE_IF_READY) and walks.</summary>
    [Fact]
    public void AnEntranceSeenUnreadyOnTheWayIsGivenUp()
    {
        (_, TFField f) = Going(Entrance(Ready), Exit());
        f.StepSeeing(TFField.Self(Start), TFField.Ctf(), [Entrance(0)]);
        Assert.NotEqual(EntranceAt, f.Decider.Doing.Goal);
        Assert.Equal(1, f.Decider.TeleporterGiveUps);
    }

    /// <summary>The bot that came out by the exit it went for has made the trip: counted once, and it walks on to its goal.</summary>
    [Fact]
    public void ComingOutByTheExitIsATrip()
    {
        (_, TFField f) = Going(Entrance(Ready), Exit());
        f.Step(TFField.Self(EntranceAt), TFField.Ctf());
        f.Step(TFField.Self(ExitAt + new AiVector(0f, 0f, 13f)), TFField.Ctf());
        Assert.Equal(1, f.Decider.TeleporterTrips);
        Assert.NotEqual(EntranceAt, f.Decider.Doing.Goal);
    }

    private static readonly int Decides1s = (int)MathF.Round(1f / (DecideSchedule.Period * 0.015f));

    /// <summary>
    /// The flag carrier never takes a teleporter (TF2 refuses him the trip) on his way home. The control: the same bot at the
    /// same place, the enemy's flag dropped by his own side, takes the same entrance on his way to it.
    /// </summary>
    [Fact]
    public void TheFlagCarrierNeverTakesATeleporter()
    {
        AiVector at = new(3500f, 256f, 0f), entrance = new(3300f, 256f, 0f), exit = new(600f, 256f, 0f);
        List<AiVector> Goals(TFField f, ObjectiveKnowledge hud)
        {
            var goals = new List<AiVector>();
            for (int i = 0; i < 3; i++)
            {
                f.StepSeeing(TFField.Self(at), hud, [Entrance(Ready, at: entrance), Exit(at: exit)]);
                goals.Add(f.Decider.Doing.Goal);
            }

            return goals;
        }

        var carrier = new TFField();
        Assert.DoesNotContain(entrance, Goals(carrier, TFField.Ctf(blue: ObjectiveState.Carried, blueCarrier: carrier.Bot.Slot)));
        Assert.Contains(entrance, Goals(new TFField(), TFField.Ctf(blue: ObjectiveState.Dropped, blueAt: new AiVector(700f, 256f, 0f))));
    }

    /// <summary>An entrance 900 u off is weighed and taken; one 1,100 u off, past <see cref="TFDecider.TeleporterReach"/>, is not.</summary>
    [Fact]
    public void AnEntrancePastTheReachIsNotTaken()
    {
        AiVector Goal(float off)
        {
            var f = new TFField();
            AiVector entrance = Start + new AiVector(off, 0f, 0f);
            for (int i = 0; i < 3; i++)
                f.StepSeeing(TFField.Self(Start), TFField.Ctf(), [Entrance(Ready, at: entrance), Exit()]);
            return f.Decider.Doing.Goal - entrance;
        }

        Assert.Equal(default, Goal(900f));
        Assert.NotEqual(default, Goal(1100f));
    }

    /// <summary>
    /// An exit seen five minutes before still counts once its entrance is seen ready: a ready entrance proves its exit stands,
    /// and a bot's life outlasts any short memory of the exit's place.
    /// </summary>
    [Fact]
    public void AnExitSeenLongAgoStillCountsForAReadyEntrance()
    {
        var f = new TFField();
        f.StepSeeing(TFField.Self(Start), TFField.Ctf(), [Exit()]);
        for (int i = 0; i < 300 * Decides1s; i++)
            f.Step(TFField.Self(Start), TFField.Ctf());
        for (int i = 0; i < 3; i++)
            f.StepSeeing(TFField.Self(Start), TFField.Ctf(), [Entrance(Ready)]);
        var goals = new List<AiVector> { f.Decider.Doing.Goal };
        // The next weigh may be up to the search's beat away: the glimpse carries the sighting to it.
        for (int i = 0; i < 2 * Decides1s; i++)
        {
            f.Step(TFField.Self(Start), TFField.Ctf());
            goals.Add(f.Decider.Doing.Goal);
        }

        Assert.Contains(EntranceAt, goals);
    }

    /// <summary>
    /// An entrance not reached and ridden in <see cref="TFDecider.TeleporterPatienceSeconds"/> is given up, and not gone for
    /// again for <see cref="TFDecider.TeleporterSkipSeconds"/>, though seen ready all along; after that it is.
    /// </summary>
    [Fact]
    public void AnEntranceNotRiddenInTwelveSecondsIsSkippedForThirty()
    {
        var f = new TFField();
        var goals = new List<(float T, AiVector Goal)>();
        for (int i = 0; i < 50 * Decides1s; i++)
        {
            f.StepSeeing(TFField.Self(Start), TFField.Ctf(), [Entrance(Ready), Exit()]);
            goals.Add((i / (float)Decides1s, f.Decider.Doing.Goal));
        }

        Assert.All(goals.Where(g => g.T < 11f), g => Assert.Equal(EntranceAt, g.Goal));
        Assert.All(goals.Where(g => g.T > 13f && g.T < 41f), g => Assert.NotEqual(EntranceAt, g.Goal));
        Assert.Contains(goals.Where(g => g.T > 43f), g => g.Goal == EntranceAt);
        Assert.Equal(1, f.Decider.TeleporterGiveUps);
    }

    /// <summary>
    /// An Engineer takes a teammate's teleporter only once his own entrance stands, as his HUD tray shows it (stock's
    /// ShouldOpportunisticallyTeleport): with the tray's entrance row he goes; with none he walks.
    /// </summary>
    [Fact]
    public void AnEngineerRidesOnlyWithHisOwnEntranceOnHisTray()
    {
        AiVector Goal(bool ownEntrance)
        {
            var f = new TFField { Hud = new TFHud([], ownEntrance ? [new TFMeter(TFBuildingTrayMeters.EntranceSapped, 0f)] : [], []) };
            for (int i = 0; i < 3; i++)
                f.StepSeeing(TFField.Self(Start, playerClass: TFClass.Engineer), TFField.Ctf(), [Entrance(Ready), Exit()]);
            return f.Decider.Doing.Goal;
        }

        Assert.Equal(EntranceAt, Goal(ownEntrance: true));
        Assert.NotEqual(EntranceAt, Goal(ownEntrance: false));
    }

    /// <summary>
    /// A bot on a ledge above an entrance, its goal a few steps along the ledge, walks to the goal: the walk is the bot's own,
    /// not the entrance's long way round less the short drop to it (a drop is one way). The exit stands on the ledge by the goal.
    /// </summary>
    [Fact]
    public void ABotOnALedgeAboveAnEntranceWalksToAGoalBesideIt()
    {
        NavGraph g = Ledge();
        var f = new TFField(floor: g);
        AiVector self = new(800f, 544f, 256f), goal = new(1056f, 544f, 256f);
        AiVector entrance = new(800f, 416f, 0f), exit = new(1184f, 544f, 256f);
        var goals = new List<AiVector>();
        for (int i = 0; i < 3; i++)
        {
            f.StepSeeing(TFField.Self(self), TFField.Ctf(blueAt: goal), [Entrance(Ready, at: entrance), Exit(at: exit)]);
            goals.Add(f.Decider.Doing.Goal);
        }

        Assert.DoesNotContain(entrance, goals);
        Assert.Equal(0, f.Decider.TeleporterTrips);
        Assert.True(f.Decider.TeleporterWeighs > 0, "the entrance was weighed");
    }

    /// <summary>A bot that came out by the exit, the entrance ready in sight 830 u back, never chooses it again: its way back cannot beat the walk.</summary>
    [Fact]
    public void ABotAtTheExitNeverRidesBack()
    {
        AiVector exit = new(1600f, 256f, 0f);
        var f = new TFField();
        for (int i = 0; i < 3; i++)
            f.StepSeeing(TFField.Self(Start), TFField.Ctf(), [Entrance(Ready), Exit(at: exit)]);
        Assert.Equal(EntranceAt, f.Decider.Doing.Goal);
        var back = new List<AiVector>();
        for (int i = 0; i < 3 * Decides1s; i++)
        {
            f.StepSeeing(TFField.Self(exit + new AiVector(0f, 0f, 13f)), TFField.Ctf(), [Entrance(Ready), Exit(at: exit)]);
            back.Add(f.Decider.Doing.Goal);
        }

        Assert.DoesNotContain(EntranceAt, back);
        Assert.Equal(1, f.Decider.TeleporterTrips);
    }

    /// <summary>The floor of <see cref="TFField.Floor"/>, and a ledge 256 u up along its north edge: a drop from every ledge cell to the floor, the only way up a ramp at the far east end.</summary>
    private static NavGraph Ledge()
    {
        const int length = TFField.Length, width = TFField.Width;
        const float cell = TFField.CellSize;
        var b = new NavGraphBuilder();
        var floor = new CellId[length, width];
        var ledge = new CellId[length];
        for (int x = 0; x < length; x++)
        {
            for (int y = 0; y < width; y++)
                floor[x, y] = b.AddCell(new NavCell(x * cell, y * cell, (x + 1) * cell, (y + 1) * cell, 0f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
            ledge[x] = b.AddCell(new NavCell(x * cell, width * cell, (x + 1) * cell, (width + 1) * cell, 256f, ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
        }

        for (int x = 0; x < length; x++)
        {
            for (int y = 0; y < width; y++)
            {
                if (x + 1 < length)
                    b.AddBoth(floor[x, y], floor[x + 1, y], NavEdgeType.Walk);
                if (y + 1 < width)
                    b.AddBoth(floor[x, y], floor[x, y + 1], NavEdgeType.Walk);
            }

            if (x + 1 < length)
                b.AddBoth(ledge[x], ledge[x + 1], NavEdgeType.Walk);
            b.AddEdge(ledge[x], floor[x, width - 1], NavEdgeType.Drop);
        }

        b.AddBoth(floor[length - 1, width - 1], ledge[length - 1], NavEdgeType.Walk);
        return b.Build();
    }

    /// <summary>
    /// At a dispenser the bot that came there hurt stands until full, unless a target is in sight: at 190 of 200, rising, it
    /// stands; with an enemy in sight 1,300 u off it goes.
    /// </summary>
    [Fact]
    public void TheDispenserStandEndsWithATargetInSight()
    {
        string After(bool enemy)
        {
            AiVector by = DispenserAt + new AiVector(-30f, 0f, 0f);
            var f = new TFField();
            for (int i = 0; i < 3; i++)
                f.StepSeeing(TFField.Self(by, health: 60), TFField.Ctf(), [Dispenser()]);
            for (int i = 0; i < 3; i++)
            {
                ActorFacts[] seen = enemy ? [new ActorFacts(new ActorId(5, 0x4000_0001), by + new AiVector(1300f, 0f, 0f), AiVector.Zero, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, (int)TFClass.Soldier)] : [];
                f.StepSeeing(TFField.Self(by, health: 188 + i), TFField.Ctf(), [Dispenser()], null, seen);
            }

            return f.Branch;
        }

        Assert.Equal(TFDecider.Survive, After(enemy: false));
        Assert.NotEqual(TFDecider.Survive, After(enemy: true));
    }

    /// <summary>
    /// A hurt bot standing by a health pack it is not healed at (taken, or the pack not where it was believed) gives it up
    /// after <see cref="TFDecider.HealPatienceSeconds"/>, as far from it: the dispenser's exemption from the patience is a
    /// dispenser's alone.
    /// </summary>
    [Fact]
    public void AHurtBotByAPackItIsNotHealedAtGivesItUp()
    {
        var f = new TFField();
        AiVector pack = new(512f, 64f, 0f), by = new(572f, 64f, 0f);
        f.Step(TFField.Self(by, health: 50), TFField.Ctf());
        Assert.Equal(pack, f.Decider.Doing.Goal);
        int decides = (int)(TFDecider.HealPatienceSeconds / 0.015f / DecideSchedule.Period) + 2;
        for (int d = 0; d < decides; d++)
            f.Step(TFField.Self(by, health: 50), TFField.Ctf());
        Assert.NotEqual(pack, f.Decider.Doing.Goal);
    }

    private static readonly AiVector Mid = new(2048f, 256f, 0f), DispenserAt = new(2348f, 256f, 0f);

    private static ThingFacts Dispenser(int flags = 0)
        => new(DispenserId, ThingKind.Building, (int)TFThingType.Dispenser, DispenserAt, default, TFField.Red, Engie, 1, -1, flags);

    /// <summary>
    /// A hurt Soldier (60 of 200) in a lull with a friendly dispenser 300 u off in sight, the packs 1,500 u away, heals at the
    /// dispenser. The control: one with a sapper on it is no heal place.
    /// </summary>
    [Fact]
    public void AHurtBotGoesToAFriendlyDispenserInSight()
    {
        (string Branch, AiVector Goal) Heal(int flags)
        {
            var f = new TFField();
            for (int i = 0; i < 3; i++)
                f.StepSeeing(TFField.Self(Mid, health: 60), TFField.Ctf(), [Dispenser(flags)]);
            return (f.Branch, f.Decider.Doing.Goal);
        }

        Assert.Equal((TFDecider.Survive, DispenserAt), Heal(0));
        Assert.NotEqual(DispenserAt, Heal((int)TFBuildingFlags.Sapped).Goal);
    }

    private static readonly int StepsPerSecond = (int)MathF.Round(1f / (DecideSchedule.Period * 0.015f));

    /// <summary>
    /// The stand at a dispenser ends when health stops rising for <see cref="TFDecider.DispenserStallSeconds"/>, short of the
    /// class's full (a kit whose most is under it, a dispenser that heals nobody): at 150 of 200 held for 5 s the bot leaves.
    /// The control: health rising a point each decide keeps it standing as long.
    /// </summary>
    [Fact]
    public void AStandAtADispenserEndsWhenHealthStopsRising()
    {
        string After(bool rising)
        {
            AiVector by = DispenserAt + new AiVector(-30f, 0f, 0f);
            var f = new TFField();
            for (int i = 0; i < 3; i++)
                f.StepSeeing(TFField.Self(by, health: 60), TFField.Ctf(), [Dispenser()]);
            for (int i = 0; i < 5 * StepsPerSecond; i++)
                f.StepSeeing(TFField.Self(by, health: 120 + (rising ? i : 0)), TFField.Ctf(), [Dispenser()]);
            return f.Branch;
        }

        Assert.Equal(TFDecider.Survive, After(rising: true));
        Assert.NotEqual(TFDecider.Survive, After(rising: false));
    }

    /// <summary>A friendly dispenser still under construction heals nobody, so a hurt bot does not go to it (stock leaves out one that IsBuilding).</summary>
    [Fact]
    public void ADispenserUnderConstructionIsNoHealPlace()
    {
        AiVector Goal(int flags)
        {
            var f = new TFField();
            for (int i = 0; i < 3; i++)
                f.StepSeeing(TFField.Self(Mid, health: 60), TFField.Ctf(), [Dispenser(flags)]);
            return f.Decider.Doing.Goal;
        }

        Assert.Equal(DispenserAt, Goal(0));
        Assert.NotEqual(DispenserAt, Goal((int)TFBuildingFlags.Building));
    }

    /// <summary>
    /// At the dispenser, the bot that came there hurt stands at it until it is full (stock's CTFBotGetHealth: "I've been
    /// healed"): at 150 of 200, over the lull's share, it still stands there; full, it goes. The control: a bot at 150 that
    /// did not come for health does not stop at a dispenser.
    /// </summary>
    [Fact]
    public void ABotStandsAtTheDispenserUntilItIsFull()
    {
        AiVector by = DispenserAt + new AiVector(-30f, 0f, 0f);
        var f = new TFField();
        for (int i = 0; i < 3; i++)
            f.StepSeeing(TFField.Self(by, health: 60), TFField.Ctf(), [Dispenser()]);
        f.StepSeeing(TFField.Self(by, health: 150), TFField.Ctf(), [Dispenser()]);
        (string branch, string verdict) = (f.Branch, f.Decider.Doing.Verdict);
        f.StepSeeing(TFField.Self(by, health: 200), TFField.Ctf(), [Dispenser()]);
        Assert.Equal((TFDecider.Survive, TFDecider.DispenserVerdict), (branch, verdict));
        Assert.NotEqual(TFDecider.Survive, f.Branch);

        var passing = new TFField();
        for (int i = 0; i < 3; i++)
            passing.StepSeeing(TFField.Self(by, health: 150), TFField.Ctf(), [Dispenser()]);
        Assert.NotEqual(TFDecider.Survive, passing.Branch);
    }
}
