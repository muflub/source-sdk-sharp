using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// One server command's decisions around the movement loop, from stock's
/// CPlayerMove::RunCommand / SetupMove / FinishMove (player_command.cpp) and
/// CBasePlayer::AdjustPlayerTimeBase (player.cpp), with the divergences
/// shell_standalone_player.cpp recorded (plan_server_native_v2 T4g). The
/// tick-base cases are tickbase_rule_test.cpp's, which they replace.
/// </summary>
public sealed class ServerCommandRulesTests
{
    private const int Correction = 4;   // TIME_TO_TICKS( 0.060 ) at 66 ticks

    [Fact]
    public void TheCorrectionWindowIsSixtyMillisecondsInTicks()
        => Assert.True(ServerCommandRules.ClockCorrectionTicks(0.015f) == 4
                       && ServerCommandRules.ClockCorrectionTicks(1f / 33f) == 2,
                       "(int)(0.5 + 0.060 / interval): 4 at 66 ticks, 2 at 33");

    [Fact]
    public void AFrameWithNoCommandsLeavesTheBaseAlone()
        => Assert.Equal(1000, ServerCommandRules.AdjustTickBase(1000, 1010, 0, 1, Correction, false));

    [Fact]
    public void ABatchEndingInsideTheWindowKeepsTheBase()
        => Assert.Equal(1005, ServerCommandRules.AdjustTickBase(1005, 1010, 7, 1, Correction, false));

    [Fact]
    public void ABatchEndingTooLateIsPulledBack()
        => Assert.Equal(1014, ServerCommandRules.AdjustTickBase(1020, 1010, 1, 1, Correction, false));

    [Fact]
    public void ABatchEndingTooEarlyIsPushedForward()
        => Assert.Equal(1013, ServerCommandRules.AdjustTickBase(900, 1010, 2, 1, Correction, false));

    [Fact]
    public void SinglePlayerAlwaysPinsTheBaseToTickcount()
        => Assert.Equal(1009, ServerCommandRules.AdjustTickBase(1005, 1010, 2, 1, Correction, true));

    [Fact]
    public void FiniteReasonableAnglesPassUnchanged()
    {
        var a = new Vector(-89f, 359000f, 12.5f);
        Assert.Equal(a, ServerCommandRules.SaneViewAngles(a));
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(0f, float.PositiveInfinity, 0f)]
    [InlineData(0f, 0f, 360000f)]
    [InlineData(-360000f, 0f, 0f)]
    public void AnInvalidOrUnreasonableAngleZeroesAllThree(float p, float y, float r)
    {
        Vector got = ServerCommandRules.SaneViewAngles(new Vector(p, y, r));
        Assert.True(got.X == 0f && got.Y == 0f && got.Z == 0f, $"({p}, {y}, {r}) -> {got}");
    }

    [Fact]
    public void FixAngleNoneTakesTheCommandsAngles()
    {
        var (v, fix) = ServerCommandRules.CommandAngles(new Vector(1f, 2f, 3f), ServerCommandRules.FixAngleNone,
                                                        new Vector(9f, 9f, 9f), new Vector(10f, 20f, 0f));
        Assert.True(v.Equals(new Vector(10f, 20f, 0f)) && fix == ServerCommandRules.FixAngleNone, $"{v} {fix}");
    }

    [Fact]
    public void FixAngleRelativeAddsTheChangeAndIsConsumed()
    {
        var (v, fix) = ServerCommandRules.CommandAngles(new Vector(1f, 2f, 3f), ServerCommandRules.FixAngleRelative,
                                                        new Vector(0f, 90f, 0f), new Vector(10f, 20f, 0f));
        Assert.True(v.Equals(new Vector(10f, 110f, 0f)) && fix == ServerCommandRules.FixAngleNone, $"{v} {fix}");
    }

    [Fact]
    public void FixAngleAbsoluteKeepsTheAngleTheGameSet()
    {
        var (v, fix) = ServerCommandRules.CommandAngles(new Vector(1f, 2f, 3f), ServerCommandRules.FixAngleAbsolute,
                                                        new Vector(0f, 90f, 0f), new Vector(10f, 20f, 0f));
        Assert.True(v.Equals(new Vector(1f, 2f, 3f)) && fix == ServerCommandRules.FixAngleAbsolute, $"{v} {fix}");
    }

    [Fact]
    public void AnUnflaggedBaseVelocityBecomesVelocityAtOneAndAHalfTicks()
    {
        Vector velocity = new(10f, 0f, 0f), baseVelocity = new(100f, 0f, 0f);
        int flags = 1;   // FL_ONGROUND, untouched

        ServerCommandRules.FoldBaseVelocity(ref velocity, ref baseVelocity, ref flags, 0.015f);

        Assert.True(velocity.X == 10f + (1f + 0.015f * 0.5f) * 100f && baseVelocity.Equals(Vector.Zero) && flags == 1,
                    $"velocity {velocity}, base {baseVelocity}, flags {flags}");
    }

    [Fact]
    public void AFlaggedBaseVelocityStaysAndTheFlagClears()
    {
        Vector velocity = new(10f, 0f, 0f), baseVelocity = new(100f, 0f, 0f);
        int flags = ServerCommandRules.FlBaseVelocity | 1;

        ServerCommandRules.FoldBaseVelocity(ref velocity, ref baseVelocity, ref flags, 0.015f);

        Assert.True(velocity.X == 10f && baseVelocity.X == 100f && flags == 1,
                    $"velocity {velocity}, base {baseVelocity}, flags {flags}");
    }

    [Theory]
    [InlineData(45f, 45f)]
    [InlineData(350f, -10f)]
    [InlineData(181f, -90f)]
    [InlineData(120f, 90f)]
    [InlineData(-120f, -90f)]
    public void TheBodyPitchIsFoldedAndClamped(float pitch, float expected)
        => Assert.Equal(expected, ServerCommandRules.BodyPitch(pitch));

    [Fact]
    public void SetupMoveCopiesTheCommandAndThePlayersWords()
    {
        var s = new MoveState();
        var cmd = new UserCmd
        {
            ForwardMove = 400f, SideMove = -200f, UpMove = 50f, Buttons = 0x22, Impulse = 0x1FF,
            CommandNumber = 77,
        };

        ServerCommandRules.SetupMove(s, in cmd, new Vector(5f, 6f, 0f), new Vector(5f, 6f, 0f), new Vector(1f, 2f, 0f),
                                     new Vector(100f, 0f, 0f), new Vector(100f, 0f, 0f), 250f, 0x2, 3f);

        Assert.True(s.FirstRunOfFunctions && !s.GameCodeMovedPlayer && s.ImpulseCommand == 0xFF
                    && s.ForwardMove == 400f && s.SideMove == -200f && s.UpMove == 50f && s.Buttons == 0x22
                    && s.ClientMaxSpeed == 250f && s.MaxSpeed == 250f && s.OldButtons == 0x2 && s.OldForwardMove == 3f
                    && s.Angles.Equals(new Vector(5f, 6f, 0f)) && s.OldAngles.Equals(new Vector(1f, 2f, 0f))
                    && s.ViewAngles.Equals(new Vector(5f, 6f, 0f)) && s.Origin.Equals(new Vector(100f, 0f, 0f))
                    && s.CommandNumber == 77,
                    "SetupMove's words, the impulse as the byte the native CUserCmd held");
    }

    [Fact]
    public void AnOriginMovedSinceTheLastCommandIsGameCodeMoved()
    {
        var s = new MoveState();
        var cmd = new UserCmd();

        ServerCommandRules.SetupMove(s, in cmd, Vector.Zero, Vector.Zero, Vector.Zero,
                                     new Vector(100f, 0f, 0f), new Vector(100f, 0f, 0.0625f), 320f, 0, 0f);

        Assert.True(s.GameCodeMovedPlayer, "the previously predicted origin differs by 1/16 unit");
    }
}
