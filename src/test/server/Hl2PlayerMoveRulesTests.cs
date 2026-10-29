using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The server's HL2MP player as the movement loop asks it: CHL2_Player's
/// speeds at stock's defaults, and its CanSprint (hl2_player.cpp:1229).
/// </summary>
public class Hl2PlayerMoveRulesTests
{
    private readonly GlobalStateTable _globals = new(new RecordingLog());

    private Hl2PlayerMoveRules Rules() => new(ConVarRegistry.For(UnattachedConsole()), _globals);

    [Fact]
    public void TheSpeedsAreStocks()
    {
        Hl2PlayerMoveRules rules = Rules();
        Assert.Equal((150f, 190f, 320f, false), (rules.WalkSpeed, rules.NormSpeed, rules.SprintSpeed, rules.InfiniteAuxPower));
    }

    [Fact]
    public void ASprintIsRefusedDisabledDuckedOrUnderWater()
    {
        Hl2PlayerMoveRules rules = Rules();

        Assert.True(rules.CanSprint(false, false, 0, false));
        Assert.False(rules.CanSprint(false, false, 0, sprintDisabled: true));
        Assert.False(rules.CanSprint(ducked: true, ducking: false, 0, false));
        Assert.False(rules.CanSprint(false, false, waterLevel: 3, false));
    }

    /// <summary>The server refuses only a finished duck: one still going down may sprint.</summary>
    [Fact]
    public void AnUnfinishedDuckMaySprint()
        => Assert.True(Rules().CanSprint(ducked: true, ducking: true, 0, false));

    [Fact]
    public void TheSuitNoSprintGlobalTakesTheModuleAway()
    {
        Hl2PlayerMoveRules rules = Rules();
        _globals.Add(Hl2PlayerMoveRules.SuitNoSprint, "map", GlobalState.On);

        Assert.False(rules.CanSprint(false, false, 0, false));
    }
}
