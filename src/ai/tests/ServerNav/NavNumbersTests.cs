using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;
using SourceSharp.Ai.Server.Nav;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// The thresholds the derivation splits jump links by are the movement
/// code's (plan_hl2mp_ai §6.2: "pinned by a fact against the managed
/// movement code"), and come to the numbers the plan quotes.
/// </summary>
public class NavNumbersTests
{
    private static readonly NavNumbers N = NavNumbers.Stock(56f);

    [Fact]
    public void TheStepIsSvStepsize()
        => Assert.Equal((Movement.Current.StepSize, 18f), (N.StepHeight, N.StepHeight));

    [Fact]
    public void AJumpClearsJumpHeightPlusAStep()
        => Assert.Equal((GameMovement.StockJumpHeight + Movement.Current.StepSize, 39f), (N.MaxJumpRise, N.MaxJumpRise));

    [Fact]
    public void ACrouchJumpClearsTheFilesJumpHeight()
        => Assert.Equal(56f, N.MaxCrouchJumpRise);

    [Fact]
    public void TheSafeDropIsTheSafeFallSpeedUnderGravity()
    {
        float v = MoveMath.PlayerMaxSafeFallSpeed;
        Assert.Equal((v * v / (2f * Movement.Current.Gravity), 231f), (N.SafeDrop, MathF.Round(N.SafeDrop)));
    }

    [Fact]
    public void TheHullIsThePlayersStandingAndCrouchedBox()
        => Assert.Equal((32f, 72f, 36f), (N.HullWidth, N.StandHeight, N.CrouchHeight));
}
