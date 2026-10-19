using System.Runtime.InteropServices;
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
    public void AnEnvironmentWhoseSideHasNoRootAnswersNothing()
    {
        IEngineEnvironment env = SideBindings.Of<IEngineEnvironment>(new EngineSide(Side.Server));

        Assert.True(env.GetInterface("VEngineServer023") == 0,
            "with no shim root attached, every interface lookup answers null");
    }

    [Fact]
    public void AnEnvironmentTheShellHasNotFilledInHasNoGlobals()
    {
        IEngineEnvironment env = SideBindings.Of<IEngineEnvironment>(new EngineSide(Side.Server));

        Assert.True(!env.Globals.IsAvailable,
            "and Globals says it is not available rather than answering a plausible zero");
    }

    [Fact]
    public unsafe void AnInterfaceTheEngineHasIsAnswered()
    {
        void* self = NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
        try
        {
            using var root = new FakeShimRoot().Interface("VEngineServer023", self);
            IEngineEnvironment env = Attached(root);

            Assert.True(env.GetInterface("VEngineServer023") == (nint)self, "the factory's answer is what comes back");
        }
        finally
        {
            NativeMemory.Free(self);
        }
    }

    /// <summary>
    /// The factory asked is the engine's <c>appSystemFactory</c>, the one a
    /// <c>CreateInterfaceFn</c> handed to a game DLL is; SourceSharp's own
    /// internal factory would answer for our shim objects instead.
    /// </summary>
    [Fact]
    public void TheEnginesAppSystemFactoryIsTheOneAsked()
    {
        using var root = new FakeShimRoot();
        IEngineEnvironment env = Attached(root);
        root.Asked.Clear();

        env.GetInterface("VEngineServer023");

        Assert.Equal([(SourceSharpFactoryType.AppSystem, "VEngineServer023")], root.Asked);
    }

    [Fact]
    public void AnInterfaceTheEngineDoesNotHaveIsNullRatherThanAThrow()
    {
        using var root = new FakeShimRoot { Refusal = "no such interface" };
        IEngineEnvironment env = Attached(root);

        Assert.True(env.GetInterface("IDoNotExist001") == 0,
            "a missing interface is a normal answer here, not an exception");
    }

    /// <summary>
    /// An empty name never reaches the factory. Native's
    /// <c>CreateInterfaceFn</c> is handed the pointer straight through, and the
    /// engine's implementation compares it against every registered name — an
    /// empty string is a question with no useful answer and a null one is worse.
    /// The root here would answer it, so a zero can only mean it was not asked.
    /// </summary>
    [Fact]
    public unsafe void AnEmptyNameIsRefusedWithoutAskingTheEngine()
    {
        void* self = NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
        try
        {
            using var root = new FakeShimRoot().Interface("", self);
            IEngineEnvironment env = Attached(root);
            root.Asked.Clear();

            Assert.True(env.GetInterface("") == 0 && root.Asked.Count == 0,
                "an empty interface name is refused here rather than passed to the engine");
        }
        finally
        {
            NativeMemory.Free(self);
        }
    }

    /// <summary>The server's environment, as its registration builds it over a side attached to <paramref name="root"/>.</summary>
    private static IEngineEnvironment Attached(FakeShimRoot root)
    {
        var side = new EngineSide(Side.Server);
        Assert.True(side.AttachShims(root.Bind(), out string why), why);
        return SideBindings.Of<IEngineEnvironment>(side);
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
        // A block the side accepts as the engine's (EngineSide.RefuseGlobalVars).
        CGlobalVarsRaw live = default;
        live.nTimestampNetworkingBase = 100;
        live.nTimestampRandomizeWindow = 32;
        live.curtime = 1.0f;

        var side = new EngineSide(Side.Server);
        side.BindGlobalVars(Side.Server, (nint)(&live));
        IEngineEnvironment env = SideBindings.Of<IEngineEnvironment>(side);

        float atLoad = env.Globals.CurTime;

        // The engine, running a frame.
        live.curtime = 2.5f;
        live.tickcount = 67;

        Assert.True(atLoad == 1.0f && env.Globals.CurTime == 2.5f && env.Globals.TickCount == 67,
            "the environment reads the engine's globals at the moment it is asked, not at the moment it was built");
    }
}
