using System;
using System.IO;
using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_crossings.md Phase X1: the engine's <c>CGlobalVars</c> pointer crossing
/// once per side, and the clock reads becoming loads through it.
///
/// <para>
/// <b>What this file is about is the FEEDING, not the layout.</b>
/// GlobalVarsLayoutParityTests already pins <see cref="CGlobalVarsRaw"/> to the
/// C++ class field by field and size by size. It passed for every one of the
/// commits in which <c>Globals</c> was shipped, correct, and never handed a
/// pointer — <c>Globals.IsAvailable</c> answered false in every live session
/// and <c>GetCurrentTime</c> crossed the boundary 11.1 times a server frame to
/// read the very float the mirror describes. A layout that is right about a
/// struct nobody points at is what these facts exist to stop happening again.
/// </para>
///
/// <para>
/// <b>Every fact binds a pointer into an <see cref="EngineSide"/> of its own</b>
/// (plan_abi_hotreload D8), never the static <c>Engine</c>'s, so none of them
/// shares state with another test and the file runs in parallel. The stub lives
/// on the stack and dies with the side that holds it.
/// </para>
/// </summary>
public class GlobalVarsBindingTests
{
    /// <summary>
    /// A stand-in for the engine's struct, filled the way the engine fills it:
    /// the two constants <c>CGlobalVarsBase</c>'s constructor writes, and a side.
    /// </summary>
    private static CGlobalVarsRaw Engineish(bool client, float curtime = 0f)
    {
        CGlobalVarsRaw raw = default;
        raw.nTimestampNetworkingBase = 100;
        raw.nTimestampRandomizeWindow = 32;
        raw.m_bClient = (byte)(client ? 1 : 0);
        raw.curtime = curtime;
        return raw;
    }

    //-------------------------------------------------------------------------
    // The side check. A swapped pointer is the failure mode this phase can
    // introduce, and it is silent: two CGlobalVars in one listen-server process,
    // one managed Engine shared between the two game DLLs, and a curtime that
    // means three different things client-side (globalvars_base.h).
    //-------------------------------------------------------------------------

    [Fact]
    public unsafe void TheServersOwnGlobalsAreAccepted()
    {
        CGlobalVarsRaw raw = Engineish(client: false);

        Assert.True(EngineSide.RefuseGlobalVars((nint)(&raw), Side.Server) is null,
            "globals whose m_bClient says server are accepted by the server");
    }

    [Fact]
    public unsafe void TheClientsOwnGlobalsAreAccepted()
    {
        CGlobalVarsRaw raw = Engineish(client: true);

        Assert.True(EngineSide.RefuseGlobalVars((nint)(&raw), Side.Client) is null,
            "globals whose m_bClient says client are accepted by the client");
    }

    [Fact]
    public unsafe void TheClientsGlobalsAreRefusedWhenTheServerAsks()
    {
        CGlobalVarsRaw raw = Engineish(client: true);

        string? refusal = EngineSide.RefuseGlobalVars((nint)(&raw), Side.Server);

        Assert.True(refusal is not null && refusal.Contains("m_bClient"),
            "the server is refused the client's globals, and the refusal names m_bClient");
    }

    [Fact]
    public unsafe void TheServersGlobalsAreRefusedWhenTheClientAsks()
    {
        CGlobalVarsRaw raw = Engineish(client: false);

        string? refusal = EngineSide.RefuseGlobalVars((nint)(&raw), Side.Client);

        Assert.True(refusal is not null && refusal.Contains("m_bClient"),
            "the client is refused the server's globals, and the refusal names m_bClient");
    }

    /// <summary>
    /// The runtime half of globalvars_layout.h's compile-time table.
    ///
    /// <para>
    /// <c>CGlobalVarsBase</c>'s constructor writes 100 and 32 into the two
    /// timestamp fields and nothing else in the tree ever writes them. Reading
    /// them back through the mirror's offsets is evidence that the address is a
    /// <c>CGlobalVars</c> at all AND that the mirror agrees with it about where
    /// its fields are — which is the difference between a refused pointer and a
    /// plausible wrong clock. It is checked BEFORE <c>m_bClient</c>, because
    /// <c>m_bClient</c> read at the wrong offset is not an answer.
    /// </para>
    /// </summary>
    [Fact]
    public unsafe void AnAddressThatIsNotAGlobalVarsIsRefusedBeforeItsSideIsRead()
    {
        CGlobalVarsRaw raw = Engineish(client: false);
        raw.nTimestampNetworkingBase = 0;

        string? refusal = EngineSide.RefuseGlobalVars((nint)(&raw), Side.Server);

        Assert.True(refusal is not null && refusal.Contains("100/32"),
            "a struct without the constructor's two constants is refused, naming them");
    }

    [Fact]
    public unsafe void TheRandomizeWindowIsCheckedToo()
    {
        CGlobalVarsRaw raw = Engineish(client: false);
        raw.nTimestampRandomizeWindow = 7;

        Assert.True(EngineSide.RefuseGlobalVars((nint)(&raw), Side.Server) is not null,
            "a wrong nTimestampRandomizeWindow is refused as well, not only the other one");
    }

    [Fact]
    public void ANullPointerIsRefusedRatherThanRead()
    {
        Assert.True(EngineSide.RefuseGlobalVars(0, Side.Server) is not null,
            "a null globals pointer is refused rather than dereferenced");
    }

    //-------------------------------------------------------------------------
    // Binding, and what a refusal leaves behind.
    //-------------------------------------------------------------------------

    // Over an engine of each fact's own (plan_abi_hotreload D8): the globals
    // are an EngineSide's, so nothing here binds the process's and nothing has
    // to be put back.

    [Fact]
    public unsafe void AnAcceptedPointerBecomesTheSidesGlobals()
    {
        CGlobalVarsRaw raw = Engineish(client: false, curtime: 11.25f);
        var server = new EngineSide(Side.Server);

        server.BindGlobalVars(Side.Server, (nint)(&raw));

        Assert.True(server.GlobalVarsPointer == (nint)(&raw),
            "the server's globals pointer is the one it was handed");
    }

    /// <summary>
    /// A refused pointer is not merely un-preferred; it is not kept at all, so
    /// nothing downstream can read through it.
    /// </summary>
    [Fact]
    public unsafe void ARefusedPointerIsNotKept()
    {
        CGlobalVarsRaw raw = Engineish(client: true, curtime: 11.25f);
        var server = new EngineSide(Side.Server);

        server.BindGlobalVars(Side.Server, (nint)(&raw));

        Assert.True(server.GlobalVarsPointer == 0,
            "a pointer refused for the wrong side leaves the side with no globals at all");
    }

    /// <summary>
    /// The two sides do not share a pointer, which is the whole reason each
    /// side's engine holds its own.
    /// </summary>
    [Fact]
    public unsafe void BindingOneSideDoesNotAnswerForTheOther()
    {
        CGlobalVarsRaw raw = Engineish(client: false, curtime: 3f);
        var server = new EngineSide(Side.Server);
        var client = new EngineSide(Side.Client);

        server.BindGlobalVars(Side.Server, (nint)(&raw));

        Assert.True(server.GlobalVarsPointer == (nint)(&raw), "the server took them (the positive arm)");
        Assert.True(client.GlobalVarsPointer == 0,
            "the client does not read the server's globals just because the server bound some");
    }

    //-------------------------------------------------------------------------
    // The reroute. In a test process there is no ABI table, so the fallback
    // answers zero — which is what makes these facts red the moment the load
    // path stops being taken, rather than merely slower.
    //-------------------------------------------------------------------------

    [Fact]
    public unsafe void CurrentTimeIsLoadedFromTheGlobalsRatherThanCrossed()
    {
        CGlobalVarsRaw raw = Engineish(client: false, curtime: 42.5f);
        var server = new EngineSide(Side.Server);

        server.BindGlobalVars(Side.Server, (nint)(&raw));

        Assert.True(server.CurrentTime == 42.5f, "CurrentTime reads curtime out of the bound globals");
    }

    /// <summary>
    /// The load is LIVE, not a value captured when the pointer was bound — the
    /// one claim that makes "never stale" true rather than hopeful.
    /// </summary>
    [Fact]
    public unsafe void CurrentTimeFollowsTheEngineAfterTheFrameMoves()
    {
        CGlobalVarsRaw raw = Engineish(client: false, curtime: 1f);
        var server = new EngineSide(Side.Server);

        server.BindGlobalVars(Side.Server, (nint)(&raw));

        float first = server.CurrentTime;
        raw.curtime = 2.5f;

        Assert.True(first == 1f && server.CurrentTime == 2.5f,
            "CurrentTime is a load at the moment it is asked, not a copy taken at bind time");
    }

    [Fact]
    public unsafe void FrameTimeIsLoadedFromTheGlobalsRatherThanCrossed()
    {
        CGlobalVarsRaw raw = Engineish(client: false);
        raw.frametime = 0.015f;

        // The frame area's OWN side's globals (plan_abi_hotreload D8), so an
        // engine of the fact's own rather than the process's.
        var side = new EngineSide(Side.Server);
        side.BindGlobalVars(Side.Server, (nint)(&raw));

        // THROUGH THE AREA, because that is where this behaviour now lives.
        // Engine.FrameGetFrameTime was deleted with the rest of EngineFrame's
        // god-class wrappers (plan_native_api.md Phase 2); the Globals-first
        // read moved into the facade unchanged, and this fact moved with it.
        // A new EngineFrame binds nothing: with the globals bound the vtable
        // is never reached, which is exactly what is being asserted.
        Assert.True(new EngineFrame(side).GetFrameTime() == 0.015f,
            "IEngineFrame.GetFrameTime reads frametime out of the bound globals "
            + "rather than crossing to the shim");
    }

    [Fact]
    public unsafe void TickCountIsLoadedFromTheGlobalsRatherThanCrossed()
    {
        CGlobalVarsRaw raw = Engineish(client: false);
        raw.tickcount = 1234;

        var side = new EngineSide(Side.Server);
        side.BindGlobalVars(Side.Server, (nint)(&raw));

        Assert.True(new EngineFrame(side).TickCount() == 1234,
            "IEngineFrame.TickCount reads tickcount out of the bound globals "
            + "rather than crossing to the shim");
    }

    /// <summary>
    /// With no globals the clock is the slot's answer, which outside the game is
    /// zero. The fallback is what keeps a test process, and the moments before
    /// the engine has set <c>gpGlobals</c>, working exactly as they did.
    /// </summary>
    [Fact]
    public void WithNoGlobalsTheClockIsStillTheSlotsAnswer()
    {
        var server = new EngineSide(Side.Server);
        server.BindGlobalVars(Side.Server, 0);

        Assert.True(server.CurrentTime == 0f,
            "with nothing bound the clock falls back to the ABI slot rather than throwing");
    }

    //-------------------------------------------------------------------------
    // The environment the container registers — the half that was dead.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <b>The fact that fails at 09902981.</b> Production resolves
    /// <c>IEngineEnvironment</c> out of the container, and the container builds
    /// every engine area with <c>Activator.CreateInstance</c> — the
    /// parameterless constructor, which used to mean a zero pointer. So
    /// <c>Globals.IsAvailable</c> was false in every live session while the
    /// mirror, the layout header and its parity tests all shipped and passed.
    /// </summary>
    [Fact]
    public unsafe void TheEnvironmentTheContainerRegistersReadsTheEnginesGlobals()
    {
        var server = new EngineSide(Side.Server);
        var services = new ServiceContainer().AddInstance(new StandaloneMirror());
        EngineServices.AddTo(services, server);
        IEngineEnvironment env = services.Require<IEngineEnvironment>();

        CGlobalVarsRaw raw = Engineish(client: false, curtime: 8.75f);
        server.BindGlobalVars(Side.Server, (nint)(&raw));

        Assert.True(env.Globals.IsAvailable && env.Globals.CurTime == 8.75f,
            "the environment the game resolves has the engine's globals and reads them");
    }

    /// <summary>
    /// Each side's registered environment reads ITS OWN side's globals, whichever
    /// side is executing (plan_abi_hotreload D8). It once followed the executing
    /// side, which is a process-wide reach two instances in one process cannot
    /// share.
    /// </summary>
    [Fact]
    public unsafe void EachRegisteredEnvironmentReadsItsOwnSidesGlobals()
    {
        var serverSide = new EngineSide(Side.Server);
        var clientSide = new EngineSide(Side.Client);
        var serverServices = new ServiceContainer().AddInstance(new StandaloneMirror());
        var clientServices = new ServiceContainer().AddInstance(new StandaloneMirror());
        EngineServices.AddTo(serverServices, serverSide);
        EngineServices.AddTo(clientServices, clientSide);
        IEngineEnvironment serverEnv = serverServices.Require<IEngineEnvironment>();
        IEngineEnvironment clientEnv = clientServices.Require<IEngineEnvironment>();

        CGlobalVarsRaw server = Engineish(client: false, curtime: 4f);
        CGlobalVarsRaw client = Engineish(client: true, curtime: 9f);
        serverSide.BindGlobalVars(Side.Server, (nint)(&server));
        clientSide.BindGlobalVars(Side.Client, (nint)(&client));

        Assert.True(serverEnv.Globals.CurTime == 4f && clientEnv.Globals.CurTime == 9f,
                    "each environment answers out of its own side's globals");
    }

    /// <summary>
    /// An environment handed a pointer explicitly still uses it. That
    /// constructor is what every other test in EngineEnvironmentTests stands on,
    /// and the live-reading default must not have quietly replaced it.
    /// </summary>
    [Fact]
    public unsafe void AnEnvironmentHandedAPointerStillReadsThatOne()
    {
        CGlobalVarsRaw handed = Engineish(client: false, curtime: 5.5f);
        CGlobalVarsRaw engine = Engineish(client: false, curtime: 77f);

        IEngineEnvironment env = new EngineEnvironment(null, (nint)(&handed));
        var side = new EngineSide(Side.Server);
        side.BindGlobalVars(Side.Server, (nint)(&engine));
        IEngineEnvironment sides = new EngineEnvironment(side);

        Assert.True(sides.Globals.CurTime == 77f, "the control: the side's own environment reads the engine's");
        Assert.True(env.Globals.CurTime == 5.5f,
            "an environment built around a pointer reads that pointer, not the engine's");
    }

    //-------------------------------------------------------------------------
    // The slot itself. Declared, and wired where BOTH sides reach it.
    //-------------------------------------------------------------------------

    /// <summary>
    /// <b>The slot has to be wired on BOTH sides, and the tree already has the
    /// machinery that says so.</b> Each game DLL has its own <c>gpGlobals</c>
    /// pointing at its own side's <c>CGlobalVars</c>, so a wiring line inside
    /// <c>#ifndef CLIENT_DLL</c> compiles, links, and passes AbiParityTests'
    /// "every declared slot is wired" scan while leaving the client's pointer
    /// null forever — the phase half-happens and nothing fails.
    ///
    /// <para>
    /// That fact is <c>SideAuditTests.PartitionMatchesTheDeclaredSides</c>,
    /// which parses the fill block's preprocessor nesting and requires every
    /// both-sides slot to be declared in its <c>BothSides</c> set. It went red
    /// on this slot before the declaration was added, and moving the wiring line
    /// under the server guard turns it red again. This fact only pins that the
    /// declaration is there to be checked, so that deleting the row is a failure
    /// rather than a way to make the audit quiet.
    /// </para>
    /// </summary>
    [Fact]
    public void TheGlobalVarsSlotIsDeclaredDualSidedForTheSideAudit()
    {
        string? path = RepoLayout.FindSdkFile(Path.Combine("managed", "SourceSharp.Tests", "SideAuditTests.cs"));

        if (path is null)
        {
            Assert.True(true, "the side audit was found for the declaration check (skipped, not found)");
            return;
        }

        Assert.True(File.ReadAllText(path).Contains("\"GetGlobalVarsPtr\"", StringComparison.Ordinal),
            "SideAuditTests declares GetGlobalVarsPtr, so the audit checks both game DLLs fill it");
    }

    // TheGlobalVarsSlotWasAppendedAfterEverySlotThatPrecededIt went with the
    // engine API struct (plan_client_vtables CC-last 2c): it held the slot's
    // position in the struct's field order, and there is no struct. The slot
    // is IShimEngine's second method; tools/vtable-layout holds each area's
    // order. AbiParityTests.TheEngineApiStructIsGone holds the absence.
}
