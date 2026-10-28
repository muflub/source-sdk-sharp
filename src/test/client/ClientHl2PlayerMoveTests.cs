using SourceSharp.Abi;
using SourceSharp.Client;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The predicting client's HL2MP player: C_HL2MP_Player's speeds at stock's
/// defaults, and its CanSprint (c_hl2mp_player.cpp:781), which refuses any
/// duck and never knows the sprint was disabled.
/// </summary>
public class ClientHl2PlayerMoveTests
{
    private static ClientHl2PlayerMove Rules() => new(ConVarRegistry.For(Fake()));

    [Fact]
    public void TheSpeedsAreStocks()
    {
        ClientHl2PlayerMove rules = Rules();
        Assert.Equal((150f, 190f, 320f, false), (rules.WalkSpeed, rules.NormSpeed, rules.SprintSpeed, rules.InfiniteAuxPower));
    }

    [Fact]
    public void AnyDuckOrWaterOverTheEyesRefusesASprint()
    {
        ClientHl2PlayerMove rules = Rules();

        Assert.True(rules.CanSprint(false, false, 0, false));
        Assert.False(rules.CanSprint(ducked: true, ducking: true, 0, false));
        Assert.False(rules.CanSprint(false, ducking: true, 0, false));
        Assert.False(rules.CanSprint(false, false, waterLevel: 3, false));
    }

    [Fact]
    public void TheClientDoesNotKnowTheSprintWasDisabled()
        => Assert.True(Rules().CanSprint(false, false, 0, sprintDisabled: true));
}
