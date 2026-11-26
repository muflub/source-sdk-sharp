using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Soldier's hands in a rocket jump's flight (round 2's S1): what its play asks of the hands the jump's program lends it,
/// the Market Gardener's airborne swing and the B.A.S.E. Jumper's parachute. On the synthetic field with a book that keeps
/// every ask (<see cref="TFJumpAdvice.Hands"/>); what the program does with an ask is <c>JumpHandsTests</c>'.
/// </summary>
public sealed class TFSoldierAirTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001);
    private const int Attack = (int)InputButtons.Attack, JumpKey = (int)InputButtons.Jump;

    private static TFKitItem Item(TFLoadoutSlot slot, int defindex, string itemClass, int kind, params TFOwnKitAttribute[] attributes)
        => new(slot, defindex, itemClass, kind, [.. attributes]);

    private static readonly TFKitItem Launcher = Item(TFLoadoutSlot.Primary, 18, "tf_weapon_rocketlauncher", TFField.Launcher),
                                      Shotgun = Item(TFLoadoutSlot.Secondary, 10, "tf_weapon_shotgun", TFField.Shotgun),
                                      Shovel = Item(TFLoadoutSlot.Melee, 6, "tf_weapon_shovel", TFField.Shovel),
                                      Gardener = Item(TFLoadoutSlot.Melee, 416, "tf_weapon_shovel", TFField.Shovel, new TFOwnKitAttribute("crit_while_airborne", 1f)),
                                      BaseJumper = Item(TFLoadoutSlot.Secondary, 1101, "tf_weapon_parachute", -1, new TFOwnKitAttribute("parachute_attribute", 1f));

    /// <summary>The field, a Soldier in it with <paramref name="kit"/>, its book answering every stretch with the default plan (east to x 1,690 on the floor).</summary>
    private static (TFField Field, TFJumpAdvice Book) Soldier(params TFKitItem[] kit)
    {
        var book = new TFJumpAdvice();
        return (new TFField(tune: p => p with { GlanceRate = 0f }, jumps: book) { Hud = new TFHud([.. kit], [], []) }, book);
    }

    /// <summary>The default plan with its landing 256 u under its takeoff (a jump down off high ground).</summary>
    private static JumpPlan DownPlan()
    {
        JumpPlan down = TFJumpAdvice.Plan() with { Touch = new Vector(1690f, 256f, -256f) };
        return down with { Recipe = down.Recipe with { Landing = new Vector(1700f, 256f, -256f) } };
    }

    /// <summary>The field, a Soldier in it with <paramref name="kit"/>, its book answering with <see cref="DownPlan"/>.</summary>
    private static (TFField Field, TFJumpAdvice Book) Downhill(params TFKitItem[] kit)
    {
        var book = new TFJumpAdvice(DownPlan());
        return (new TFField(tune: p => p with { GlanceRate = 0f }, jumps: book) { Hud = new TFHud([.. kit], [], []) }, book);
    }

    /// <summary>The bot running east at the plan's start: the decide that begins the jump.</summary>
    private static void Begin(TFField f)
    {
        f.Step(TFField.Self(new AiVector(300f, 256f, 0f), velocity: new AiVector(240f, 0f, 0f)), TFField.Ctf());
        Assert.Equal(1, f.Decider.RocketJumps);
    }

    /// <summary>The bot in the air at <paramref name="at"/>, moving at <paramref name="velocity"/>, holding <paramref name="weapon"/>; an enemy at <paramref name="enemy"/> in sight when given.</summary>
    private static void InTheAir(TFField f, AiVector at, AiVector velocity, int weapon = TFField.Launcher, AiVector? enemy = null, bool grounded = false)
    {
        // The program has the jump in the air: the flight it writes to the book once it launches (round 2's S1).
        if (f.Decider.Jumps is TFJumpAdvice book && book.Flight is null)
            book.Flight = new AirFlight(f.Bot, f.Decider.LastJump!);
        SelfState self = TFField.Self(at, velocity: velocity, weapon: weapon) with { Flags = SelfFlags.Alive | (grounded ? SelfFlags.Grounded : SelfFlags.None) };
        if (enemy is { } e)
            f.StepDrawn(self, TFField.Ctf(), new ActorFacts(Enemy, e, default, TFField.Launcher, ActorActivity.None, 1f, TFField.Blue, (int)TFClass.Scout));
        else
            f.Step(self, TFField.Ctf());
    }

    /// <summary>The eye of a Soldier standing at <paramref name="at"/>, near enough for a sight cone.</summary>
    private static AiVector EyeAt(AiVector at) => at + new AiVector(0f, 0f, 68f);

    /// <summary>
    /// Whether the bot's view holds an enemy standing at <paramref name="enemy"/> in the sight's cone (90 degrees,
    /// <see cref="VisionParams.MaxFovDeg"/>): the view its last ask lent, else the program's own in flight, level along the way east.
    /// What a fact draws in sight must be in it: <see cref="TFField.StepDrawn"/> draws whatever it is given.
    /// </summary>
    private static bool Holds(TFJumpAdvice book, AiVector at, AiVector enemy)
    {
        AiAngles view = book.Hands.LastOrDefault(a => a is not null)?.Look ?? new AiAngles(0f, 0f, 0f);
        return VisionMath.InCone(EyeAt(at), VisionMath.Forward(view), enemy + new AiVector(0f, 0f, 41f), VisionParams.MaxFovDeg);
    }

    /// <summary>Past the apex, no enemy: the bot looks at the landing; then the enemy is drawn, if that look holds him.</summary>
    private static void LooksThenSees(TFField f, TFJumpAdvice book, AiVector at, AiVector velocity, AiVector enemy, int weapon = TFField.Launcher)
    {
        InTheAir(f, at, velocity, weapon);
        Assert.True(Holds(book, at, enemy), $"the control: the look at the landing holds the enemy at {enemy}");
        InTheAir(f, at, velocity, weapon, enemy: enemy);
    }

    private static readonly AiVector Down = new(240f, 0f, -300f), Up = new(240f, 0f, 300f), Falling = new(0f, 0f, -300f);

    /// <summary>The default plan's touch (<see cref="TFJumpAdvice.Plan"/>), and an enemy standing a swing from it, east of it.</summary>
    private static readonly AiVector Touch = new(1690f, 256f, 0f), AtTheLanding = new(1720f, 256f, 0f);

    private static float Gap(AiAngles a, AiAngles b) => MathF.Sqrt((a.Pitch - b.Pitch) * (a.Pitch - b.Pitch) + (a.Yaw - b.Yaw) * (a.Yaw - b.Yaw));

    [Fact]
    public void PastTheApexAMarketGardenerLooksAtItsLanding()
    {
        (TFField f, TFJumpAdvice book) = Soldier(Launcher, Shotgun, Gardener);
        Begin(f);
        var at = new AiVector(1640f, 256f, 500f);
        InTheAir(f, at, new AiVector(0f, 0f, -100f));
        AiAngles look = Assert.IsType<AirAsk>(book.Hands[^1]).Look!.Value;
        AiAngles toTouch = SourceSharp.Ai.Server.BotCommandApplier.AnglesTo(f.Decider.OwnEye, Touch);
        Assert.True(Gap(look, toTouch) < 0.5f && look.Pitch > 60f, $"it looks {look}, the landing is at {toTouch}");
        // The control: the program's own look, level along the way, does not hold an enemy at the landing.
        Assert.False(VisionMath.InCone(EyeAt(at), VisionMath.Forward(default), AtTheLanding + new AiVector(0f, 0f, 41f), VisionParams.MaxFovDeg));
    }

    [Fact]
    public void RisingAMarketGardenerKeepsTheProgramsLook()
    {
        (TFField f, TFJumpAdvice book) = Soldier(Launcher, Shotgun, Gardener);
        Begin(f);
        InTheAir(f, new AiVector(900f, 256f, 300f), Up);
        Assert.All(book.Hands, a => Assert.Null(a));
    }

    [Fact]
    public void AnEnemyAtTheLandingIsSeenThroughTheLookAndTheMeleeComesOut()
    {
        (TFField f, TFJumpAdvice book) = Soldier(Launcher, Shotgun, Gardener);
        Begin(f);
        // At 500 u falling at 100 u/s: a second of the fall left, over the switch (0.5 s).
        LooksThenSees(f, book, new AiVector(1640f, 256f, 500f), new AiVector(0f, 0f, -100f), AtTheLanding);
        AirAsk ask = Assert.IsType<AirAsk>(book.Hands[^1]);
        Assert.Equal((SourceSharp.Ai.Server.EngineBotSelf.ByKind + TFField.Shovel, 0), (ask.Weapon, ask.Hold));
    }

    [Fact]
    public void TheMeleeStaysAwayWithoutTheAirForTheSwitch()
    {
        (TFField f, TFJumpAdvice book) = Soldier(Launcher, Shotgun, Gardener);
        Begin(f);
        // At 200 u falling at 300 u/s: 0.43 s of the fall left, under the switch.
        LooksThenSees(f, book, new AiVector(1660f, 256f, 200f), Falling, AtTheLanding);
        AirAsk ask = Assert.IsType<AirAsk>(book.Hands[^1]);
        Assert.Equal(-1, ask.Weapon);
        Assert.NotNull(ask.Look);
    }

    [Fact]
    public void TheMeleeStaysAwayForAnEnemyNoSwingFromTheLandingReaches()
    {
        (TFField f, TFJumpAdvice book) = Soldier(Launcher, Shotgun, Gardener);
        Begin(f);
        // 250 u short of the landing, under a bot high over it: the old rule took it out (within 256 u of the landing); no swing reaches it.
        LooksThenSees(f, book, new AiVector(1640f, 256f, 500f), new AiVector(0f, 0f, -100f), new AiVector(1440f, 256f, 0f));
        AirAsk ask = Assert.IsType<AirAsk>(book.Hands[^1]);
        Assert.Equal(-1, ask.Weapon);
    }

    [Fact]
    public void AMarketGardenerSwingsFallingAtAnEnemyInReachWhileStillInTheAirAtTheSmack()
    {
        (TFField f, TFJumpAdvice book) = Soldier(Launcher, Shotgun, Gardener);
        Begin(f);
        // At 110 u falling at 300 u/s the feet are 34 u up when the swing lands, 0.2 s on.
        LooksThenSees(f, book, new AiVector(1700f, 256f, 110f), Falling, AtTheLanding, weapon: TFField.Shovel);
        AirAsk ask = Assert.IsType<AirAsk>(book.Hands[^1]);
        Assert.Equal((Attack, -1), (ask.Hold & Attack, ask.Weapon));
    }

    [Fact]
    public void AMarketGardenerDoesNotSwingWhenItLandsBeforeTheSmack()
    {
        (TFField f, TFJumpAdvice book) = Soldier(Launcher, Shotgun, Gardener);
        Begin(f);
        // At 40 u falling at 300 u/s the feet are down 0.12 s on: the hit would come on the ground, no crit.
        LooksThenSees(f, book, new AiVector(1700f, 256f, 40f), Falling, AtTheLanding, weapon: TFField.Shovel);
        Assert.Equal(0, Assert.IsType<AirAsk>(book.Hands[^1]).Hold & Attack);
    }

    [Fact]
    public void AMarketGardenerDoesNotSwingRising()
    {
        (TFField f, TFJumpAdvice book) = Soldier(Launcher, Shotgun, Gardener);
        Begin(f);
        // Falling first, to look at the landing and see him; then rising past him (a second blast), a swing would meet him.
        LooksThenSees(f, book, new AiVector(1700f, 256f, 110f), Falling, AtTheLanding, weapon: TFField.Shovel);
        InTheAir(f, new AiVector(1700f, 256f, 5f), new AiVector(0f, 0f, 300f), weapon: TFField.Shovel, enemy: AtTheLanding);
        AirAsk ask = Assert.IsType<AirAsk>(book.Hands[^1]);
        Assert.Equal(0, ask.Hold & Attack);
        Assert.NotNull(ask.Look);
    }

    [Fact]
    public void AMarketGardenerDoesNotSwingAtAnEnemyOutOfReach()
    {
        (TFField f, TFJumpAdvice book) = Soldier(Launcher, Shotgun, Gardener);
        Begin(f);
        // In reach of the landing, but the bot is still 300 u over it: no swing yet.
        LooksThenSees(f, book, new AiVector(1690f, 256f, 300f), Falling, AtTheLanding, weapon: TFField.Shovel);
        Assert.Equal(0, Assert.IsType<AirAsk>(book.Hands[^1]).Hold & Attack);
    }

    [Fact]
    public void AStockShovelAsksNothingInTheAir()
    {
        (TFField f, TFJumpAdvice book) = Soldier(Launcher, Shotgun, Shovel);
        Begin(f);
        InTheAir(f, new AiVector(1700f, 256f, 110f), Falling, weapon: TFField.Shovel);
        InTheAir(f, new AiVector(1700f, 256f, 110f), Falling, weapon: TFField.Shovel, enemy: AtTheLanding);
        Assert.All(book.Hands, a => Assert.Null(a));
    }

    [Fact]
    public void TheClassIsAskedNothingWithItsFeetOnTheGround()
    {
        (TFField f, TFJumpAdvice book) = Downhill(Launcher, BaseJumper, Gardener);
        Begin(f);
        // Down on a step on the way, its velocity still falling: everything the play would ask in the air holds but the feet.
        InTheAir(f, new AiVector(1640f, 256f, 300f), new AiVector(0f, 0f, -100f), weapon: TFField.Shovel, grounded: true);
        Assert.All(book.Hands, a => Assert.Null(a));
        // The control: the same in the air asks (the landing's look, the parachute's press).
        InTheAir(f, new AiVector(1640f, 256f, 300f), new AiVector(0f, 0f, -100f), weapon: TFField.Shovel);
        Assert.NotNull(book.Hands[^1]);
    }

    [Fact]
    public void ABaseJumperOpensPastTheApexOverALandingFarUnderItsTakeoff()
    {
        (TFField f, TFJumpAdvice book) = Downhill(Launcher, BaseJumper, Shovel);
        Begin(f);
        InTheAir(f, new AiVector(900f, 256f, 300f), Down);
        Assert.Equal(JumpKey, Assert.IsType<AirAsk>(book.Hands[^1]).Press);
        // Once a jump: the next decide asks no other press.
        InTheAir(f, new AiVector(950f, 256f, 280f), Down);
        Assert.Null(book.Hands[^1]);
    }

    [Fact]
    public void ABaseJumperKeepsItShutOverALevelJumpHoweverHighItsApex()
    {
        // The review's C1: the default plan lands level with its takeoff; 300 u over it past the apex is the jump's own rise.
        (TFField f, TFJumpAdvice book) = Soldier(Launcher, BaseJumper, Shovel);
        Begin(f);
        InTheAir(f, new AiVector(900f, 256f, 300f), Down);
        Assert.All(book.Hands, a => Assert.Null(a));
    }

    [Fact]
    public void ABaseJumperNeverOpensBeforeTheApex()
    {
        (TFField f, TFJumpAdvice book) = Downhill(Launcher, BaseJumper, Shovel);
        Begin(f);
        InTheAir(f, new AiVector(900f, 256f, 300f), Up);
        Assert.All(book.Hands, a => Assert.Null(a));
    }

    [Fact]
    public void WithoutTheBaseJumperNoParachuteIsAsked()
    {
        (TFField f, TFJumpAdvice book) = Downhill(Launcher, Shotgun, Shovel);
        Begin(f);
        InTheAir(f, new AiVector(900f, 256f, 300f), Down);
        Assert.All(book.Hands, a => Assert.Null(a));
    }

    [Fact]
    public void ABaseJumperOverALandingNearByWithFloorUnderItKeepsItShut()
    {
        (TFField f, TFJumpAdvice book) = Soldier(Launcher, BaseJumper, Shovel);
        Begin(f);
        InTheAir(f, new AiVector(900f, 256f, 120f), Down);
        Assert.All(book.Hands, a => Assert.Null(a));
    }

    [Fact]
    public void ABaseJumperOpensOverAGapOnTheWayToItsLanding()
    {
        // The field with no floor from x 1,024 to 1,408 under the way to the landing.
        var book = new TFJumpAdvice();
        var f = new TFField(tune: p => p with { GlanceRate = 0f }, jumps: book, floor: Gapped(16, 22)) { Hud = new TFHud([Launcher, BaseJumper, Shovel], [], []) };
        Begin(f);
        InTheAir(f, new AiVector(900f, 256f, 120f), Down);
        Assert.Equal(JumpKey, Assert.IsType<AirAsk>(book.Hands[^1]).Press);
    }

    /// <summary>The field's floor with the cells from column <paramref name="from"/> to before <paramref name="to"/> left out.</summary>
    private static NavGraph Gapped(int from, int to)
    {
        var b = new NavGraphBuilder();
        var ids = new CellId[TFField.Length, TFField.Width];
        for (int x = 0; x < TFField.Length; x++)
        {
            for (int y = 0; y < TFField.Width; y++)
            {
                ids[x, y] = x >= from && x < to ? CellId.None
                    : b.AddCell(new NavCell(x * TFField.CellSize, y * TFField.CellSize, (x + 1) * TFField.CellSize, (y + 1) * TFField.CellSize, 0f,
                                            ClearanceClasses.Stand | ClearanceClasses.Crouch, NavCellFlags.None, 1f));
            }
        }

        for (int x = 0; x + 1 < TFField.Length; x++)
        {
            for (int y = 0; y < TFField.Width; y++)
            {
                if (ids[x, y].IsValid && ids[x + 1, y].IsValid)
                    b.AddBoth(ids[x, y], ids[x + 1, y], NavEdgeType.Walk);
                if (y + 1 < TFField.Width && ids[x, y].IsValid && ids[x, y + 1].IsValid)
                    b.AddBoth(ids[x, y], ids[x, y + 1], NavEdgeType.Walk);
            }
        }

        return b.Build();
    }

    [Fact]
    public void TheClassIsAskedThroughTheProgramsFlightHoweverLongItsApproach()
    {
        (TFField f, TFJumpAdvice book) = Soldier(Launcher, Shotgun, Gardener);
        Begin(f);
        // An approach longer than the tree's own hold (RocketJumpBusySeconds): the program placing the bot at a link's start and
        // turning its view, on the ground. Live on ss_ai_tf_jump's gap the tree had let the jump go by the launch, and its class
        // was asked nothing in five flights of six.
        for (int i = 0; i * DecideSchedule.Period * f.Memory.World.TickSeconds <= TFDecider.RocketJumpBusySeconds + 0.5f; i++)
            InTheAir(f, new AiVector(400f, 256f, 0f), default, grounded: true);
        book.Flight = new AirFlight(f.Bot, f.Decider.LastJump!);
        LooksThenSees(f, book, new AiVector(1700f, 256f, 110f), Falling, AtTheLanding, weapon: TFField.Shovel);
        Assert.Equal(Attack, Assert.IsType<AirAsk>(book.Hands[^1]).Hold & Attack);
    }

    [Fact]
    public void WithNoFlightTheClassIsAskedNothing()
    {
        (TFField f, TFJumpAdvice book) = Soldier(Launcher, Shotgun, Gardener);
        Begin(f);
        // In the air with an enemy in reach, but the program has launched nothing (called off, or another program's flight).
        book.Flight = new AirFlight(new ActorId(f.Bot.Slot + 1, 1), f.Decider.LastJump!);
        InTheAir(f, new AiVector(1700f, 256f, 110f), Falling, weapon: TFField.Shovel);
        InTheAir(f, new AiVector(1700f, 256f, 110f), Falling, weapon: TFField.Shovel, enemy: AtTheLanding);
        Assert.All(book.Hands, a => Assert.Null(a));
    }

    [Fact]
    public void TheAirPlayIsCountedForTheHarness()
    {
        (TFField f, TFJumpAdvice book) = Soldier(Launcher, Shotgun, Gardener);
        Begin(f);
        LooksThenSees(f, book, new AiVector(1700f, 256f, 110f), Falling, AtTheLanding, weapon: TFField.Shovel);
        var counts = f.Decider.ClassCounters().ToDictionary(k => k.Key, k => k.Value);
        Assert.Equal((1L, 1L, 0L), (counts["soldier.garden_looks"], counts["soldier.garden_swings"], counts["soldier.chute_asks"]));
        Assert.Equal(2, f.Decider.AirAsks);
    }
}
