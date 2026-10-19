using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The abstract environment (plan_gamedll.md Part 0a #1): one place an engine
/// interface is acquired, and a reader over the engine's live globals.
/// </summary>
public class EngineEnvironmentTests
{
    [Fact]
    public void AnEnvironmentTheShellHasNotFilledInAnswersNothing()
    {
        IEngineEnvironment env = SideBindings.Of<IEngineEnvironment>(new EngineSide(Side.Server));

        Assert.True(env.GetInterface("VEngineServer023") == 0,
            "with no factory handed across, every interface lookup answers null");
    }

    [Fact]
    public void AnEnvironmentTheShellHasNotFilledInHasNoGlobals()
    {
        IEngineEnvironment env = SideBindings.Of<IEngineEnvironment>(new EngineSide(Side.Server));

        Assert.True(!env.Globals.IsAvailable,
            "and Globals says it is not available rather than answering a plausible zero");
    }

    [Fact]
    public void AnInterfaceTheEngineHasIsAnswered()
    {
        IEngineEnvironment env = new EngineEnvironment(name => name == "VEngineServer023" ? 0x1234 : 0, 0);

        Assert.True(env.GetInterface("VEngineServer023") == 0x1234, "the factory's answer is what comes back");
    }

    [Fact]
    public void AnInterfaceTheEngineDoesNotHaveIsNullRatherThanAThrow()
    {
        IEngineEnvironment env = new EngineEnvironment(_ => 0, 0);

        Assert.True(env.GetInterface("IDoNotExist001") == 0,
            "a missing interface is a normal answer here, not an exception");
    }

    /// <summary>
    /// An empty name never reaches the factory. Native's
    /// <c>CreateInterfaceFn</c> is handed the pointer straight through, and the
    /// engine's implementation compares it against every registered name — an
    /// empty string is a question with no useful answer and a null one is worse.
    /// </summary>
    [Fact]
    public void AnEmptyNameIsRefusedWithoutAskingTheEngine()
    {
        int asked = 0;
        IEngineEnvironment env = new EngineEnvironment(_ => { asked++; return 0x99; }, 0);

        Assert.True(env.GetInterface("") == 0 && asked == 0,
            "an empty interface name is refused here rather than passed to the engine");
    }

    /// <summary>
    /// <b>The environment's globals are LIVE, not a snapshot taken when the
    /// shell handed the pointer across.</b>
    ///
    /// <para>
    /// <c>GlobalVarsLayoutParityTests</c> makes this claim about
    /// <see cref="Globals"/> itself. It has to be made again here because
    /// <see cref="EngineEnvironment"/> is where it could quietly stop being
    /// true: a <c>Globals</c> field initialised in the constructor instead of a
    /// pointer would still pass every one of those facts, and would then be one
    /// frame stale everywhere — which nothing in the game reports, because a
    /// slightly old <c>curtime</c> is a plausible number.
    /// </para>
    /// </summary>
    [Fact]
    public unsafe void TheEnvironmentsGlobalsAreReadLiveRatherThanCapturedAtLoad()
    {
        CGlobalVarsRaw live = default;
        live.curtime = 1.0f;

        IEngineEnvironment env = new EngineEnvironment(null, (nint)(&live));

        float atLoad = env.Globals.CurTime;

        // The engine, running a frame.
        live.curtime = 2.5f;
        live.tickcount = 67;

        Assert.True(atLoad == 1.0f && env.Globals.CurTime == 2.5f && env.Globals.TickCount == 67,
            "the environment reads the engine's globals at the moment it is asked, not at the moment it was built");
    }
}
