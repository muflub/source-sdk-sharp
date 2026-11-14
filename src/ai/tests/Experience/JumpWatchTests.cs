using Xunit;

namespace SourceSharp.Ai.Tests.Experience;

/// <summary>
/// The jumps players make (lane G6a, <see cref="JumpWatch"/>): a flight of a player's own doing (a jump press or its own
/// blast) of 0.4 to 6 s that comes down dry, walking and on the world is handed over with its places, its time, its cost
/// and its commands; a fall, a push, a kick, a move by the game, a death, water, a ladder or a player under the feet is
/// thrown away and counted by why.
/// </summary>
public sealed class JumpWatchTests
{
    private const double Tick = 0.015d;
    private static readonly AiVector Start = new(0f, 0f, 0f), Mid = new(0f, 150f, 200f), Ledge = new(0f, 300f, 250f), Far = new(0f, 3150f, 250f);
    private readonly JumpWatch _watch = new(Tick);
    private readonly List<ShownJumpRecipe> _landed = new();

    private static ShownJumpBody Standing(AiVector at, bool ducked = false) => new(at, default, OnGround: true, ducked, Wet: false, Walking: true);

    private static ShownJumpCommand Command(byte buttons = 0, float pitch = 10f, float yaw = 90f) => new(buttons, pitch, yaw, 400f, 0f);

    private void Read(in ShownJumpBody body, byte buttons = 0, JumpBlast blast = JumpBlast.None, int slot = 1, float pitch = 10f)
        => _watch.Command(slot, in body, Command(buttons, pitch), blast, _landed);

    /// <summary><paramref name="commands"/> commands standing at <paramref name="at"/>, the last with <paramref name="lastButtons"/>.</summary>
    private void Ground(int commands, AiVector at, byte lastButtons = 0, int slot = 1)
    {
        for (int i = 0; i < commands; i++)
            Read(Standing(at), i == commands - 1 ? lastButtons : (byte)0, slot: slot);
    }

    /// <summary>
    /// <paramref name="reads"/> reads in the air from <paramref name="from"/> to <paramref name="to"/> at one steady
    /// velocity, the first with <paramref name="first"/>'s blast, each ducked as <paramref name="ducked"/> says.
    /// </summary>
    private void Fly(int reads, AiVector from, AiVector to, JumpBlast first = JumpBlast.None, bool ducked = false, int slot = 1)
    {
        AiVector velocity = (to - from) * (float)(1d / (reads * Tick));
        for (int i = 0; i < reads; i++)
        {
            AiVector at = from + (to - from) * ((i + 1f) / (reads + 1f));
            Read(new ShownJumpBody(at, velocity, OnGround: false, ducked, Wet: false, Walking: true), blast: i == 0 ? first : JumpBlast.None, slot: slot);
        }
    }

    /// <summary>A standing run, a jump pressed, 40 reads in the air (0.6 s) and a landing on the ledge.</summary>
    private void Jump(int air = 40, bool ducked = false)
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        Fly(air, Start, Ledge, ducked: ducked);
        Read(Standing(Ledge));
    }

    [Fact]
    public void AJumpOfMoreThanFourTenthsOfASecondIsHandedOverWithItsPlacesAndTime()
    {
        Jump();
        ShownJumpRecipe jump = Assert.Single(_landed);
        Assert.Equal(ShownJumpKind.Plain, jump.Kind);
        Assert.Equal(Start, jump.Takeoff);
        Assert.Equal(Ledge, jump.Landing);
        Assert.Equal(40, jump.AirCommands);
        Assert.Equal(1L, _watch.Landed);
    }

    [Fact]
    public void AFlightUnderFourTenthsOfASecondIsThrownAsShort()
    {
        Jump(air: 26);
        Assert.Empty(_landed);
        Assert.Equal(1L, _watch.Thrown(JumpThrow.Short));
    }

    [Fact]
    public void AFlightOfTwentySevenCommandsIsLongEnough()
    {
        Jump(air: 27);
        Assert.Single(_landed);
    }

    [Fact]
    public void AFlightOverSixSecondsIsThrownAsLong()
    {
        Jump(air: 401);
        Assert.Empty(_landed);
        Assert.Equal(1L, _watch.Thrown(JumpThrow.Long));
    }

    [Fact]
    public void AFallWithNoJumpPressAndNoBlastIsThrownAsAFall()
    {
        Ground(50, Start);
        Fly(40, Start, new AiVector(0f, 300f, -200f));
        Read(Standing(new AiVector(0f, 300f, -200f)));
        Assert.Empty(_landed);
        Assert.Equal(1L, _watch.Thrown(JumpThrow.Fall));
    }

    [Fact]
    public void APressFourCommandsBeforeTheTakeoffIsNotTheJumps()
    {
        Ground(46, Start);
        Read(Standing(Start), ShownJumpCommand.Jump);
        Ground(3, Start);
        Fly(40, Start, Ledge);
        Read(Standing(Ledge));
        Assert.Equal(1L, _watch.Thrown(JumpThrow.Fall));
    }

    [Fact]
    public void ABodyDuckedInTheAirMakesACrouchJump()
    {
        Jump(ducked: true);
        Assert.Equal(ShownJumpKind.Crouch, Assert.Single(_landed).Kind);
    }

    [Fact]
    public void ItsOwnBlastOnTheFirstReadInTheAirMakesABlastJump()
    {
        Ground(50, Start);
        Fly(40, Start, Ledge, first: JumpBlast.Own);
        Read(Standing(Ledge));
        ShownJumpRecipe jump = Assert.Single(_landed);
        Assert.Equal(ShownJumpKind.Blast, jump.Kind);
        Assert.True(jump.Flags.HasFlag(ShownJumpFlags.OwnBlast));
        Assert.Equal(jump.TakeoffIndex, jump.BlastIndex);
    }

    [Fact]
    public void ItsOwnBlastReadOnTheGroundJustBeforeTheTakeoffMakesABlastJump()
    {
        Ground(49, Start);
        Read(Standing(Start), blast: JumpBlast.Own);
        Fly(40, Start, Ledge);
        Read(Standing(Ledge));
        Assert.Equal(ShownJumpKind.Blast, Assert.Single(_landed).Kind);
    }

    [Fact]
    public void ItsOwnStickyMakesAStickyJump()
    {
        Ground(50, Start);
        Fly(40, Start, Ledge, first: JumpBlast.OwnSticky);
        Read(Standing(Ledge));
        Assert.Equal(ShownJumpKind.Sticky, Assert.Single(_landed).Kind);
    }

    [Fact]
    public void ASelfBlastHurtOnTheGroundMakesABlastJumpAndCountsItsCost()
    {
        Ground(50, Start);
        _watch.Hurt(1, 40, byOther: false, ownBlast: true);
        Fly(40, Start, Ledge);
        Read(Standing(Ledge));
        ShownJumpRecipe jump = Assert.Single(_landed);
        Assert.Equal(ShownJumpKind.Blast, jump.Kind);
        Assert.Equal(40, jump.HealthCost);
    }

    [Fact]
    public void TheCostIsItsOwnAndTheWorldsHurtsFromBeforeTheTakeoffToTheLanding()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        _watch.Hurt(1, 30, byOther: false, ownBlast: false);
        Fly(20, Start, Mid);
        _watch.Hurt(1, 25, byOther: false, ownBlast: true);
        Fly(20, Mid, Ledge);
        _watch.Hurt(1, 7, byOther: false, ownBlast: false);
        Read(Standing(Ledge));
        Assert.Equal(62, Assert.Single(_landed).HealthCost);
    }

    [Fact]
    public void AHurtLongBeforeTheTakeoffIsNotItsCost()
    {
        Ground(10, Start);
        _watch.Hurt(1, 30, byOther: false, ownBlast: false);
        Jump();
        Assert.Equal(0, Assert.Single(_landed).HealthCost);
    }

    [Fact]
    public void AHurtByAnotherJustBeforeTheTakeoffIsThrownAsPushed()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        _watch.Hurt(1, 20, byOther: true, ownBlast: false);
        Fly(40, Start, Ledge);
        Read(Standing(Ledge));
        Assert.Empty(_landed);
        Assert.Equal(1L, _watch.Thrown(JumpThrow.Pushed));
    }

    [Fact]
    public void AHurtByAnotherInTheAirIsThrownAsPushed()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        Fly(20, Start, Mid);
        _watch.Hurt(1, 20, byOther: true, ownBlast: false);
        Fly(20, Mid, Ledge);
        Read(Standing(Ledge));
        Assert.Equal(1L, _watch.Thrown(JumpThrow.Pushed));
    }

    [Fact]
    public void AnothersBlastAsTheGameSaysIsThrownAsPushed()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        Fly(40, Start, Ledge, first: JumpBlast.Other);
        Read(Standing(Ledge));
        Assert.Equal(1L, _watch.Thrown(JumpThrow.Pushed));
    }

    [Fact]
    public void AnothersBlastToldInTheAirIsThrownAsPushed()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        Fly(20, Start, Mid);
        Fly(20, Mid, Ledge, first: JumpBlast.Other);
        Read(Standing(Ledge));
        Assert.Equal(1L, _watch.Thrown(JumpThrow.Pushed));
    }

    [Fact]
    public void AKickInTheAirWithNothingOfItsOwnIsThrownAsKicked()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        Fly(20, Start, Mid);
        Fly(20, Mid, Far);
        Read(Standing(Far));
        Assert.Equal(1L, _watch.Thrown(JumpThrow.Kicked));
    }

    [Fact]
    public void AKickJustAfterAJumpPressedInTheAirIsADoubleJump()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        Fly(20, Start, Mid);
        Read(new ShownJumpBody(Mid, default, false, false, false, true), ShownJumpCommand.Jump);
        Fly(20, Mid, Far);
        Read(Standing(Far));
        ShownJumpRecipe jump = Assert.Single(_landed);
        Assert.Equal(ShownJumpKind.Double, jump.Kind);
        Assert.True(jump.Flags.HasFlag(ShownJumpFlags.AirJump));
    }

    [Fact]
    public void AKickAfterItsOwnBlastIsKeptAsTheBlastsVelocity()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        Fly(20, Start, Mid);
        Read(new ShownJumpBody(Mid, default, false, false, false, true), blast: JumpBlast.Own);
        Fly(20, Mid, Far);
        Read(Standing(Far));
        ShownJumpRecipe jump = Assert.Single(_landed);
        Assert.Equal(ShownJumpKind.Blast, jump.Kind);
        Assert.True(jump.BlastVelocity.Length > 1000f);
        Assert.Equal(jump.TakeoffIndex + 20, jump.BlastIndex);
    }

    [Fact]
    public void AMoveOverTwoHundredFiftySixUnitsInTheAirIsThrownAsMoved()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        Fly(20, Start, new AiVector(0f, 100f, 50f));
        Fly(20, new AiVector(0f, 2000f, 50f), new AiVector(0f, 2100f, 50f));
        Read(Standing(new AiVector(0f, 2100f, 50f)));
        Assert.Empty(_landed);
        Assert.Equal(1L, _watch.Thrown(JumpThrow.Moved));
    }

    [Fact]
    public void ADeathInTheAirIsThrownAndTheNextLandingIsNoJump()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        Fly(30, Start, Ledge);
        _watch.Died(1);
        Fly(10, Ledge, Ledge);
        Read(Standing(Ledge));
        Assert.Empty(_landed);
        Assert.Equal(1L, _watch.Thrown(JumpThrow.Died));
        Assert.Equal(0L, _watch.Thrown(JumpThrow.Fall));
    }

    [Fact]
    public void ALandingInWaterIsThrown()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        Fly(40, Start, Ledge);
        Read(new ShownJumpBody(Ledge, default, OnGround: false, false, Wet: true, Walking: true));
        Read(Standing(Ledge));
        Assert.Empty(_landed);
        Assert.Equal(1L, _watch.Thrown(JumpThrow.Wet));
    }

    [Fact]
    public void ALadderCaughtInTheAirIsThrown()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        Fly(40, Start, Ledge);
        Read(new ShownJumpBody(Ledge, default, OnGround: false, false, Wet: false, Walking: false));
        Read(Standing(Ledge));
        Assert.Empty(_landed);
        Assert.Equal(1L, _watch.Thrown(JumpThrow.Ladder));
    }

    [Fact]
    public void ALandingOnAPlayerIsThrown()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        Fly(40, Start, Ledge);
        Read(Standing(Ledge) with { OnPlayer = true });
        Assert.Empty(_landed);
        Assert.Equal(1L, _watch.Thrown(JumpThrow.OnPlayer));
    }

    [Fact]
    public void AFlightWithNoGroundBeforeItIsNoJump()
    {
        Fly(40, Start, Ledge);
        Read(Standing(Ledge));
        Assert.Empty(_landed);
        Assert.Equal(0L, _watch.Thrown(JumpThrow.Fall));
    }

    [Fact]
    public void TheTraceHoldsHalfASecondBeforeTheTakeoffThroughTheLanding()
    {
        Jump();
        ShownJumpRecipe jump = Assert.Single(_landed);
        Assert.Equal(33, jump.TakeoffIndex);
        Assert.Equal(33 + 40 + 1, jump.Trace.Length);
        Assert.Equal(32, jump.JumpIndex);
        Assert.True(jump.Trace[jump.JumpIndex].Holds(ShownJumpCommand.Jump));
    }

    [Fact]
    public void AShotFiredBeforeTheTakeoffIsInTheTraceWithItsView()
    {
        Ground(45, Start);
        Read(Standing(Start), ShownJumpCommand.Attack, pitch: 77f);
        _watch.Fired(1);
        Ground(4, Start, lastButtons: ShownJumpCommand.Jump);
        Fly(40, Start, Ledge);
        Read(Standing(Ledge));
        ShownJumpRecipe jump = Assert.Single(_landed);
        Assert.True(jump.Flags.HasFlag(ShownJumpFlags.ShotSeen));
        Assert.Equal(77f, jump.ShotCommand!.Value.Pitch);
        Assert.Equal(-4, jump.ShotDelay);
    }

    [Fact]
    public void AShotFiredInTheAirIsInTheTrace()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        Fly(5, Start, Mid);
        _watch.Fired(1);
        Fly(35, Mid, Ledge);
        Read(Standing(Ledge));
        ShownJumpRecipe jump = Assert.Single(_landed);
        Assert.Equal(jump.TakeoffIndex + 4, jump.ShotIndex);
        Assert.Equal(5, jump.ShotDelay);
    }

    [Fact]
    public void TheTakeoffStateIsTheLastReadOnTheGround()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        Read(new ShownJumpBody(Start + new AiVector(0f, 4f, 0f), new AiVector(0f, 250f, 0f), true, true, false, true), ShownJumpCommand.Jump);
        Fly(40, Start, Ledge);
        Read(Standing(Ledge));
        ShownJumpRecipe jump = Assert.Single(_landed);
        Assert.Equal(Start + new AiVector(0f, 4f, 0f), jump.Takeoff);
        Assert.Equal(new AiVector(0f, 250f, 0f), jump.GroundVelocity);
        Assert.True(jump.Flags.HasFlag(ShownJumpFlags.DuckedAtTakeoff));
    }

    [Fact]
    public void ASlotLeftMidFlightLandsNothingForTheNextPlayer()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        Fly(30, Start, Ledge);
        _watch.Leave(1);
        Fly(10, Ledge, Ledge);
        Read(Standing(Ledge));
        Assert.Empty(_landed);
    }

    [Fact]
    public void ClearDropsEveryFlightUnderWay()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump);
        Fly(30, Start, Ledge);
        _watch.Clear();
        Fly(10, Ledge, Ledge);
        Read(Standing(Ledge));
        Assert.Empty(_landed);
        Assert.False(_watch.Flying(1));
    }

    [Fact]
    public void TwoPlayersFlightsAreTheirOwn()
    {
        Ground(50, Start, lastButtons: ShownJumpCommand.Jump, slot: 1);
        Ground(50, Start, slot: 2);
        Fly(40, Start, Ledge, slot: 1);
        Fly(40, Start, Ledge, slot: 2);
        Read(Standing(Ledge), slot: 1);
        Read(Standing(Ledge), slot: 2);
        Assert.Single(_landed);
        Assert.Equal(1L, _watch.Thrown(JumpThrow.Fall));
    }
}
