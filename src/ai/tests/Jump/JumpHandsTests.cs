using System.Security.Cryptography;
using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Jump;
using TF2.Shared.Movement;
using Xunit;
using static SourceSharp.Ai.Tests.Jump.JumpRig;

namespace SourceSharp.Ai.Tests.Jump;

/// <summary>
/// The hands in flight (round 2's S1): while a jump flies, the program keeps the keys it needs and lends the rest to the
/// class's play. A bot whose class asks nothing flies as it did before the hook, command for command.
/// </summary>
public class JumpHandsTests
{
    private static readonly MotionParams Normal = new(250f, 600f, 10f, 150f);

    private static JumpPlan Plan => JumpPlannerTests.OpenPlan;

    private static AiVector Goal => Plan.Goal;

    private static JumpLive Bot()
        => new(JumpPlannerTests.Hall(), new JumpStart(new Vector(40f, Plan.Recipe.Takeoff.Y, VoxelMoveWorld.DistEpsilon), Vector.Zero, Vector.Zero, Ducked: false, OnGround: true),
               Normal);

    /// <summary>The plan with a second rocket fired at the floor under its takeoff (RocketJumpProgramTests' own).</summary>
    private static JumpPlan Doubled
    {
        get
        {
            int air = (int)(Plan.AirSeconds / TickSeconds), earliest = (int)MathF.Ceiling(RocketRefire / TickSeconds);
            return Plan with { Recipe = Plan.Recipe with { Second = new JumpSecondShot(Plan.Recipe.Takeoff with { Z = 0f }, earliest, air - 4, 89f, 180f) } };
        }
    }

    /// <summary>The bot walks on, jumps <paramref name="plan"/> and flies it to the end: the program's commands, from its first.</summary>
    private static List<UserCmd> Flown(JumpLive live, JumpPlan plan)
    {
        JumpPlan kept = live.Keep(plan);
        live.WalkPast(170f, Goal);
        int first = live.Jump(kept.Id, Goal);
        Assert.Equal(JumpEnd.Landed, Assert.Single(live.Flown).End);
        return live.Sent[first..];
    }

    /// <summary>A hash of every field a command sends, in order: two runs agree only if they agree command for command.</summary>
    private static string Hash(List<UserCmd> commands)
    {
        using var bytes = new MemoryStream();
        using (var w = new BinaryWriter(bytes, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            foreach (UserCmd c in commands)
            {
                w.Write(c.Pitch);
                w.Write(c.Yaw);
                w.Write(c.ForwardMove);
                w.Write(c.SideMove);
                w.Write(c.UpMove);
                w.Write(c.Buttons);
                w.Write(c.WeaponSelect);
            }
        }

        return $"{commands.Count}:{Convert.ToHexString(SHA256.HashData(bytes.ToArray()))[..16]}";
    }

    /// <summary>
    /// The program's commands as they were before the hook (base 7e79f0290c, measured there with this fact's own rig): the open
    /// hall's plan and its doubled plan, each flown by a middling hand. A bot whose class asks nothing in the air sends exactly
    /// these.
    /// </summary>
    private const string SingleBefore = "159:9DD7107D6D7DC5E1", DoubleBefore = "159:11AD9F9A16BE1301";

    [Fact]
    public void WithNoClassAnswerAJumpsCommandsAreTheProgramsOwnTickForTick()
    {
        Assert.Equal((SingleBefore, DoubleBefore), (Hash(Flown(Bot(), Plan)), Hash(Flown(Bot(), Doubled))));
    }

    private const int Attack = (int)InputButtons.Attack, JumpKey = (int)InputButtons.Jump, Duck = (int)InputButtons.Duck;

    /// <summary>
    /// The bot walks on and jumps <paramref name="plan"/>, its class asking <paramref name="ask"/> afresh every decide's worth of
    /// commands from the jump's one-shot to its end, as the tree does in flight: the program's commands, from its first.
    /// </summary>
    private static List<UserCmd> FlownAsking(JumpLive live, JumpPlan plan, Func<JumpPoint, AirAsk?> ask, ActorId? asker = null)
    {
        JumpPlan kept = live.Keep(plan);
        live.WalkPast(170f, Goal);
        live.Command(Goal, new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(JumpLive.Row, kept.Id), OneShotSeq: 1_000));
        int first = live.Sent.Count;
        for (int i = 0; i < 1500 && live.Applier.ProgramRunning(JumpLive.Bot); i++)
        {
            if (i % DecideSchedule.Period == 0)
                live.Book.AskHands(asker ?? JumpLive.Bot, ask(live.Now));
            live.Command(Goal);
        }

        Assert.Single(live.Flown);
        return live.Sent[first..];
    }

    private static AirAsk Holding(int keys) => new(JumpLive.Bot) { Hold = keys };

    [Fact]
    public void ALentAttackLandsInTheAirAfterTheLaunchAndNeverOnItsCommands()
    {
        JumpLive by = Bot();
        List<UserCmd> alone = Flown(by, Plan);
        JumpLive live = Bot();
        List<UserCmd> lent = FlownAsking(live, Plan, _ => Holding(Attack));
        // The program's own trigger is where it was, and the first command the two differ on is a lent Attack in the air,
        // no sooner than the command the blast was felt on (counted from the trigger's first) and the view let go.
        int trigger = alone.FindIndex(c => (c.Buttons & Attack) != 0), felt = by.Flown[0].FeltAfterShot;
        Assert.True(felt > JumpPilot.TriggerCommands, $"the control: the blast was felt {felt} commands after the trigger");
        Assert.Equal(alone[trigger].Buttons, lent[trigger].Buttons);
        int differs = Enumerable.Range(0, Math.Min(alone.Count, lent.Count)).First(i => alone[i].Buttons != lent[i].Buttons);
        int launch = live.Sent.Count - lent.Count;
        Assert.True(differs >= trigger + felt, $"the first lent command is {differs}, the trigger {trigger}, the blast felt {felt} after it");
        Assert.Equal(Attack, (lent[differs].Buttons ^ alone[differs].Buttons) & Attack);
        Assert.False(live.Track[launch + differs - 1].OnGround, "the lent Attack went out from the air");
        Assert.True(live.Flown[0].Lent > 0 && live.Program.LentAttacks > 0);
    }

    [Fact]
    public void TheBookHoldsTheBotsFlightFromItsLaunchToItsEnd()
    {
        JumpLive live = Bot();
        JumpPlan kept = live.Keep(Plan);
        live.WalkPast(170f, Goal);
        Assert.Null(live.Book.Flying(JumpLive.Bot));
        live.Command(Goal, new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(JumpLive.Row, kept.Id), OneShotSeq: 1_000));
        var seen = new List<(bool Flying, bool Grounded)>();
        for (int i = 0; i < 1500 && live.Applier.ProgramRunning(JumpLive.Bot); i++)
        {
            live.Command(Goal);
            seen.Add((live.Book.Flying(JumpLive.Bot) is not null, live.Now.OnGround));
        }

        // None on the approach, one through every command in the air, none once the program is over.
        int first = seen.FindIndex(s => s.Flying), airborne = seen.FindIndex(s => !s.Grounded);
        Assert.True(first >= 0 && first <= airborne, $"the flight was written at command {first}, the body left the ground at {airborne}");
        Assert.All(seen.Where(s => !s.Grounded), s => Assert.True(s.Flying));
        Assert.Null(live.Book.Flying(JumpLive.Bot));
        Assert.Equal(JumpEnd.Landed, Assert.Single(live.Flown).End);
    }

    [Fact]
    public void ABotsFlightIsItsOwnLifesAndEndsOnlyItself()
    {
        var book = new JumpBook(new JumpBookSettings());
        var other = new ActorId(JumpLive.Bot.Slot, JumpLive.Bot.Serial + 1);
        var mine = new AirFlight(JumpLive.Bot, Plan);
        book.FlightBegan(mine);
        Assert.Same(mine, book.Flying(JumpLive.Bot));
        Assert.Null(book.Flying(other));
        // A flight that ended before this one began takes nothing back.
        book.FlightEnded(new AirFlight(JumpLive.Bot, Plan));
        Assert.Same(mine, book.Flying(JumpLive.Bot));
        book.FlightEnded(mine);
        Assert.Null(book.Flying(JumpLive.Bot));
    }

    /// <summary>
    /// The bot walks on and jumps <paramref name="plan"/>, <paramref name="ask"/> told each command the body the program will
    /// read and how many commands it has been in the air (as the program counts them), and its answer, when one, written for
    /// that command: the program's commands, from its first.
    /// </summary>
    private static List<UserCmd> FlownWriting(JumpLive live, JumpPlan plan, Func<JumpPoint, int, AirAsk?> ask)
    {
        JumpPlan kept = live.Keep(plan);
        live.WalkPast(170f, Goal);
        live.Command(Goal, new ActionIntent(ActionKind.Ability, FireStyle.Tap, AbilityCall.Pack(JumpLive.Row, kept.Id), OneShotSeq: 1_000));
        int first = live.Sent.Count, air = 0;
        for (int i = 0; i < 1500 && live.Applier.ProgramRunning(JumpLive.Bot); i++)
        {
            if (!live.Now.OnGround)
                air++;
            if (ask(live.Now, air) is { } written)
                live.Book.AskHands(JumpLive.Bot, written);
            live.Command(Goal);
        }

        Assert.Single(live.Flown);
        return live.Sent[first..];
    }

    [Fact]
    public void AnAskNotRenewedStopsBeingLentAfterItsStaleCommands()
    {
        // Written once, past the apex, and never again: the same ask stands on the board for the rest of the flight.
        AirAsk once = Holding(Attack);
        int writtenAt = -1, command = -1;
        JumpLive live = Bot();
        List<UserCmd> lent = FlownWriting(live, Plan, (p, _) =>
        {
            command++;
            if (writtenAt >= 0 || p.OnGround || p.Velocity.Z >= 0f)
                return null;
            writtenAt = command;
            return once;
        });
        int held = Enumerable.Range(writtenAt, lent.Count - writtenAt).Count(i => (lent[i].Buttons & Attack) != 0);
        int last = Enumerable.Range(writtenAt, lent.Count - writtenAt).Last(i => (lent[i].Buttons & Attack) != 0);
        Assert.True(writtenAt > 0 && held > 0, $"the control: written at command {writtenAt}, lent on {held}");
        // Lent on the ask's first command and the stale limit's after it, then no more while the flight goes on.
        Assert.Equal(JumpHands.AskStaleCommands + 1, held);
        Assert.True(lent.Count - 1 > last + 1, $"the flight went on past the last lent command {last}: {lent.Count} commands");
    }

    [Fact]
    public void AJumpKeyAskedOnTheProgramsOwnCloseIsNotSentTwiceRunning()
    {
        JumpPlan quick = Plan with { AirSeconds = Plan.AirSeconds - JumpHands.ChuteKeepSeconds - 0.15f };
        AirAsk? OpenPastTheApex(JumpPoint p, ref bool asked)
        {
            if (asked || p.OnGround || p.Velocity.Z >= 0f)
                return null;
            asked = true;
            return new AirAsk(JumpLive.Bot) { Press = JumpKey };
        }

        // The close's command, as the program counts the air, from a flight that asks nothing then.
        bool opened = false;
        JumpLive first = Bot();
        FlownWriting(first, quick, (p, _) => OpenPastTheApex(p, ref opened));
        int closeAir = first.Flown[0].ChuteClosedAir;
        Assert.True(closeAir > first.Flown[0].ChuteOpenedAir, $"the control: opened at {first.Flown[0].ChuteOpenedAir}, closed at {closeAir}");

        // Again, with a fresh press of the jump key asked on the very command the program closes it.
        bool asked = false;
        int closeAt = -1, command = -1;
        JumpLive live = Bot();
        List<UserCmd> lent = FlownWriting(live, quick, (p, air) =>
        {
            command++;
            if (air == closeAir && !p.OnGround)
            {
                closeAt = command;
                return new AirAsk(JumpLive.Bot) { Press = JumpKey };
            }

            return OpenPastTheApex(p, ref asked);
        });
        Assert.Equal(closeAir, live.Flown[0].ChuteClosedAir);
        Assert.True((lent[closeAt].Buttons & JumpKey) != 0, "the program's own close is pressed");
        Assert.True((lent[closeAt + 1].Buttons & JumpKey) == 0, "the command after the close lets the key go");
    }

    [Fact]
    public void ALentAttackGoesOutOnAFlightWhoseBlastThePilotNeverFelt()
    {
        // The shot straight up: the bot hops on its legs, nothing pushes it, and the pilot steers once its wait for a blast is over.
        JumpPlan hop = Plan with { Recipe = Plan.Recipe with { Pitch = -85f } };
        JumpLive live = Bot();
        FlownAsking(live, hop, _ => Holding(Attack));
        Assert.False(live.Flown[0].Felt, "the control: no blast was felt");
        Assert.True(live.Program.LentAttacks > 0, "the Attack was lent in the air past the pilot's wait for a blast");
    }

    [Fact]
    public void ALentAttackIsNotSentOnTheGround()
    {
        JumpLive live = Bot();
        List<UserCmd> lent = FlownAsking(live, Plan, _ => Holding(Attack));
        int launch = live.Sent.Count - lent.Count;
        List<UserCmd> alone = Flown(Bot(), Plan);
        for (int i = 0; i < Math.Min(lent.Count, alone.Count); i++)
        {
            bool grounded = i == 0 || live.Track[launch + i - 1].OnGround;
            if (grounded)
                Assert.Equal(alone[i].Buttons & Attack, lent[i].Buttons & Attack);
        }
    }

    [Fact]
    public void ADoublesSecondRocketIsTheProgramsAndALentAttackWaitsForIt()
    {
        JumpLive alone = Bot();
        Flown(alone, Doubled);
        int second = alone.Flown[0].SecondAfterShot;
        Assert.True(second > 0, $"the control: the doubled plan fires its second rocket, {second}");
        JumpLive live = Bot();
        List<UserCmd> lent = FlownAsking(live, Doubled, _ => Holding(Attack));
        // The second rocket goes on the command it went on alone, and nothing is lent from the first trigger to the second's
        // let-go: a lent trigger never fires the second rocket sooner.
        Assert.Equal(second, live.Flown[0].SecondAfterShot);
        List<UserCmd> own = Flown(Bot(), Doubled);
        int trigger = own.FindIndex(c => (c.Buttons & Attack) != 0);
        for (int i = trigger; i < trigger + second + JumpPilot.TriggerCommands; i++)
            Assert.True(own[i].Buttons == lent[i].Buttons, $"command {i} ({i - trigger} after the trigger): {own[i].Buttons} alone, {lent[i].Buttons} lent");
    }

    [Fact]
    public void TheDuckAndTheMoveAreNeverLent()
    {
        // The plan flown standing up in the air, so a lent duck would show.
        JumpPlan upright = Plan with { Recipe = Plan.Recipe with { Ducked = false } };
        List<UserCmd> alone = Flown(Bot(), upright);
        Assert.DoesNotContain(alone, c => (c.Buttons & Duck) != 0);
        List<UserCmd> lent = FlownAsking(Bot(), upright, _ => Holding(Duck));
        Assert.Equal(Hash(alone), Hash(lent));
        // The control: a lent Attack changes the flight's commands.
        Assert.NotEqual(Hash(alone), Hash(FlownAsking(Bot(), upright, _ => Holding(Attack))));
    }

    [Fact]
    public void ALentLookAndWeaponGoOutInTheAir()
    {
        var down = new AiAngles(60f, 90f, 0f);
        JumpLive live = Bot();
        List<UserCmd> lent = FlownAsking(live, Plan, _ => new AirAsk(JumpLive.Bot) { Look = down, Weapon = 1005 });
        int launch = live.Sent.Count - lent.Count;
        int selected = lent.FindIndex(c => c.WeaponSelect != 0);
        Assert.True(selected > 0 && !live.Track[launch + selected - 1].OnGround, $"the weapon went out on command {selected}");
        Assert.Equal(6, lent[selected].WeaponSelect);
        // The view comes round to the lent look while the bot is still in the air.
        Assert.Contains(Enumerable.Range(selected, lent.Count - selected), i => !live.Track[launch + i].OnGround && JumpPilot.ViewGap(new Vector(lent[i].Pitch, lent[i].Yaw, 0f), new Vector(down.Pitch, down.Yaw, 0f)) < 5f);
        Assert.True(live.Program.LentLooks > 0 && live.Program.LentWeapons > 0);
    }

    [Fact]
    public void AnAskStandingWhenTheJumpBeganIsNeverLent()
    {
        List<UserCmd> alone = Flown(Bot(), Plan);
        JumpLive live = Bot();
        live.Book.AskHands(JumpLive.Bot, Holding(Attack));
        Assert.Equal(Hash(alone), Hash(Flown(live, Plan)));
        Assert.Equal(0L, live.Program.LentCommands);
    }

    [Fact]
    public void AnotherBotsAskIsNotLent()
    {
        List<UserCmd> alone = Flown(Bot(), Plan);
        var other = new ActorId(JumpLive.Bot.Slot, JumpLive.Bot.Serial + 1);
        JumpLive live = Bot();
        Assert.Equal(Hash(alone), Hash(FlownAsking(live, Plan, _ => new AirAsk(other) { Hold = Attack }, asker: other)));
        Assert.Equal(0L, live.Program.LentCommands);
    }

    [Fact]
    public void ALentPressOfTheJumpKeyOpensTheChuteOnceAndTheProgramClosesItForItsLanding()
    {
        // A plan whose time in the air, with the keep, ends 0.15 s before the flight's (the simulator's body has no parachute):
        // the program's close comes in the flight, past the apex the press was asked at.
        JumpPlan quick = Plan with { AirSeconds = Plan.AirSeconds - JumpHands.ChuteKeepSeconds - 0.15f };
        int closesAt = (int)MathF.Ceiling((Plan.AirSeconds - 0.15f) / TickSeconds);
        JumpLive live = Bot();
        bool asked = false;
        List<UserCmd> lent = FlownAsking(live, quick, p =>
        {
            // Once, past the apex, as the Soldier's play asks it.
            if (asked || p.OnGround || p.Velocity.Z >= 0f)
                return null;
            asked = true;
            return new AirAsk(JumpLive.Bot) { Press = JumpKey };
        });
        JumpFlown flown = live.Flown[0];
        Assert.True(asked && flown.ChuteOpenedAir > 0, $"the lent press opened it at {flown.ChuteOpenedAir}");
        // Closed by the program's own press once the flight has been up the plan's time and the keep.
        Assert.True(flown.ChuteClosedAir > flown.ChuteOpenedAir, $"opened at {flown.ChuteOpenedAir}, closed at {flown.ChuteClosedAir}");
        Assert.InRange(flown.ChuteClosedAir, closesAt - 1, closesAt + 1);
        Assert.Equal((1L, 1L), (live.Program.ChuteOpens, live.Program.ChuteCloses));
        // Each press is an edge: never two commands running with the jump key down in the air.
        int launch = live.Sent.Count - lent.Count;
        for (int i = 1; i < lent.Count; i++)
            Assert.False(!live.Track[launch + i - 1].OnGround && (lent[i].Buttons & lent[i - 1].Buttons & JumpKey) != 0, $"the jump key held over command {i}");
    }

    [Fact]
    public void WithNoChuteLentOpenTheProgramPressesNoJumpKeyInTheAir()
    {
        JumpPlan quick = Plan with { AirSeconds = Plan.AirSeconds - JumpHands.ChuteKeepSeconds - 0.15f };
        JumpLive live = Bot();
        List<UserCmd> alone = Flown(live, quick);
        int launch = live.Sent.Count - alone.Count;
        Assert.DoesNotContain(Enumerable.Range(1, alone.Count - 1), i => !live.Track[launch + i - 1].OnGround && (alone[i].Buttons & JumpKey) != 0);
        Assert.Equal(0L, live.Program.ChuteCloses);
    }
}
