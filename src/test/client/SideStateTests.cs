using System.Runtime.InteropServices;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Shared.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The side's state that outlives its loads (statics to zero): what the
/// Bootstrap's one opaque handle per side holds, and how the side's entry
/// reaches it. Each side entry hands its own crossing op to
/// <see cref="SideLoad"/>, which counts it on that state, so two game
/// instances of one side (two handles) count separately.
/// </summary>
public unsafe class SideStateTests
{
    private static byte[] Facts()
        => Encoding.UTF8.GetBytes("managed_dir\t/nowhere\nmod_id\ttest\ngame_dir\t/nowhere\n" +
                                  "server_assemblies\tSourceSharp.Server\nclient_assemblies\tSourceSharp.Client\n\0");

    /// <summary>
    /// One Load with no shim root: refused (-4) after the state exists and
    /// the entry's op is counted on it. Answers the Bootstrap's two handles.
    /// </summary>
    private static int RefusedLoad(ref nint state, out nint session)
    {
        byte[] facts = Facts();
        void* root = null;
        nint s = 0;
        int rc;
        fixed (byte* pFacts = facts)
        fixed (nint* pState = &state)
            rc = SideLoad.Load(Side.Server, CrossingOp.LoadServer, null, null, &root, pState, &s, pFacts);
        session = s;
        return rc;
    }

    [Fact]
    public void TheEntrysBindRunsOnTheSidesEngineBeforeTheLoadGoesOn()
    {
        // What only one side's assembly can bind (the server's IVEngineServer):
        // the entry hands it in and it runs once, on this load's engine, even
        // when the load is then refused for want of a shim root.
        byte[] facts = Facts();
        void* root = null;
        nint state = 0, session = 0;
        var bound = new List<Side>();
        bool detached = false;
        try
        {
            int rc;
            fixed (byte* pFacts = facts)
                rc = SideLoad.Load(Side.Server, CrossingOp.LoadServer, null, null, &root, &state, &session, pFacts,
                                   attachment =>
                                   {
                                       bound.Add(attachment.Side);
                                       // Before the root binds: nothing attached, nothing initialised.
                                       detached = attachment.Root is null && !attachment.IsInitialized;
                                   });

            Assert.Equal(-4, rc);
            Assert.Equal(new[] { Side.Server }, bound);
            Assert.True(detached, "the entry binds a detached attachment");
            Assert.Same(SessionOf(session).Attachment.State, StateOf(state));
            Assert.Equal(0, SideLoad.Unload(CrossingOp.UnloadServer, session));
        }
        finally
        {
            Free(state);
        }
    }

    private static SideState StateOf(nint handle) => (SideState)GCHandle.FromIntPtr(handle).Target!;

    private static SideSession SessionOf(nint handle) => (SideSession)GCHandle.FromIntPtr(handle).Target!;

    /// <summary>
    /// Every load builds its own attachment (a reload attaches afresh), over
    /// the one state the side keeps across loads.
    /// </summary>
    [Fact]
    public void EachLoadBuildsAFreshAttachmentOverTheKeptState()
    {
        nint handle = 0;
        try
        {
            RefusedLoad(ref handle, out nint first);
            RefusedLoad(ref handle, out nint second);

            SideAttachment one = SessionOf(first).Attachment, two = SessionOf(second).Attachment;
            Assert.NotSame(one, two);
            Assert.NotSame(one.Bound, two.Bound);
            Assert.Same(StateOf(handle), one.State);
            Assert.Same(one.State, two.State);

            SideLoad.Unload(CrossingOp.UnloadServer, first);
            SideLoad.Unload(CrossingOp.UnloadServer, second);
        }
        finally
        {
            Free(handle);
        }
    }

    [Fact]
    public void SideLoadCountsTheEntrysOpOnTheStateItKeeps()
    {
        nint handle = 0;
        try
        {
            Assert.Equal(-4, RefusedLoad(ref handle, out nint first));
            Assert.NotEqual(0, handle);   // the first load made the state
            SideState state = StateOf(handle);
            Assert.Equal(Side.Server, state.Side);
            Assert.Equal(1, state.Crossings.Read(CrossingOp.LoadServer));

            // The next load is handed the same handle and counts on the same state.
            nint kept = handle;
            Assert.Equal(-4, RefusedLoad(ref handle, out nint second));
            Assert.Equal(kept, handle);
            Assert.Equal(2, state.Crossings.Read(CrossingOp.LoadServer));

            // The unload counts its own op on the session's state.
            Assert.Equal(0, SideLoad.Unload(CrossingOp.UnloadServer, first));
            Assert.Equal(0, SideLoad.Unload(CrossingOp.UnloadServer, second));
            Assert.Equal(2, state.Crossings.Read(CrossingOp.UnloadServer));
        }
        finally
        {
            Free(handle);
        }
    }

    /// <summary>
    /// Two game instances of the server side in one process are two handles:
    /// each load counts on its own instance's state and never the other's.
    /// </summary>
    [Fact]
    public void TwoInstancesOfOneSideCountOnTheirOwnStates()
    {
        nint a = 0, b = 0;
        try
        {
            RefusedLoad(ref a, out nint sa);
            RefusedLoad(ref a, out nint sa2);
            RefusedLoad(ref b, out nint sb);

            Assert.NotEqual(a, b);
            Assert.NotSame(StateOf(a), StateOf(b));
            Assert.Equal(2, StateOf(a).Crossings.Read(CrossingOp.LoadServer));
            Assert.Equal(1, StateOf(b).Crossings.Read(CrossingOp.LoadServer));

            SideLoad.Unload(CrossingOp.UnloadServer, sa);
            SideLoad.Unload(CrossingOp.UnloadServer, sa2);
            SideLoad.Unload(CrossingOp.UnloadServer, sb);
        }
        finally
        {
            Free(a);
            Free(b);
        }
    }

    [Fact]
    public void AnEngineComposedWithoutALoadHasAStateOfItsOwn()
    {
        var one = TestSides.Create(Side.Server);
        var two = TestSides.Create(Side.Server);

        Assert.NotSame(one.Attachment.State, two.Attachment.State);
        Assert.NotSame(one.Attachment.State.Crossings, two.Attachment.State.Crossings);
    }

    /// <summary>
    /// The build stamps are each state's own: a managed stamp recorded on one
    /// instance's state leaves another's unknown, and what the HUD's corner
    /// stamp reads by default is the state it was handed.
    /// </summary>
    [Fact]
    public void EachStateKeepsItsOwnBuildStamps()
    {
        var mine = new SideState(Side.Client);
        var other = new SideState(Side.Client);
        var half = new BuildHalf(new BuildStamp("abc12345", DateTime.UnixEpoch, 1), GitProvenance.Unknown, "/x", false);

        mine.Build.SetManaged(half, Array.Empty<string>());
        mine.Build.SetNativeOnce(half);

        Assert.Equal("abc12345", mine.Build.Managed.Stamp.Hash);   // the stimulus landed
        Assert.Equal("abc12345", mine.Build.Native.Stamp.Hash);
        Assert.Equal(BuildHalf.Unknown, other.Build.Managed);
        Assert.Equal(BuildHalf.Unknown, other.Build.Native);

        var stamp = new SourceSharp.Client.HudBuildStamp(null!, mine.Build);
        Assert.Equal("abc12345", stamp.Managed(Side.Client).Stamp.Hash);
        Assert.Equal(BuildHalf.Unknown, new SourceSharp.Client.HudBuildStamp(null!, other.Build).Managed(Side.Client));
    }

    /// <summary>
    /// The reload store and the connection id stamps are each state's own: a
    /// section put on one instance's store is not waiting on another's, and
    /// one instance's stamps never advance another's.
    /// </summary>
    [Fact]
    public void EachStateCarriesItsOwnReloadAndStamps()
    {
        var mine = new SideState(Side.Server);
        var other = new SideState(Side.Server);

        mine.Reload.Put("section", new byte[] { 1, 2, 3 }, 3);
        mine.Reload.NoteRestoreFailures(2, "first");
        int a1 = mine.Stamps.Next(), a2 = mine.Stamps.Next();

        Assert.Equal(3, mine.Reload.Count("section"));   // the stimulus landed
        Assert.Equal(0, other.Reload.Count("section"));
        Assert.Equal((0, (string?)null), other.Reload.RestoreFailures);
        Assert.Equal((1, 2), (a1, a2));
        Assert.Equal(1, other.Stamps.Next());
        Assert.Same(mine.Reload, mine.Reload);   // one store per state, not one per ask
    }

    /// <summary>
    /// The crash handlers are installed once per state: a second Install on
    /// the same state (a reload) adds nothing, and another state (another game
    /// instance) installs its own.
    /// </summary>
    [Fact]
    public void CrashHandlersInstallOncePerState()
    {
        var mine = new SideState(Side.Server);
        var other = new SideState(Side.Server);

        Assert.False(mine.CrashHandlersInstalled);
        ManagedDiagnostics.Install(mine);
        ManagedDiagnostics.Install(mine);

        Assert.True(mine.CrashHandlersInstalled);    // the stimulus landed
        Assert.False(other.CrashHandlersInstalled);  // and only there
    }

    [Fact]
    public void AnEngineRefusesTheOtherSidesState()
        => Assert.Throws<ArgumentException>(() => SideAttachment.Detached(Side.Server, new SideState(Side.Client)));

    private static void Free(nint handle)
    {
        if (handle == 0)
            return;
        var h = GCHandle.FromIntPtr(handle);
        ((SideState)h.Target!).Vtables.Dispose();
        h.Free();
    }

    private static nint SlotOf(NetState state)
        => (nint)System.Reflection.Pointer.Unbox(typeof(NetState)
               .GetField("_slot", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
               .GetValue(state)!);

    /// <summary>
    /// A local net state's slot is its own (statics to zero: every local state
    /// in the process wrote its dirty word into one static slot).
    /// </summary>
    [Fact]
    public void EachLocalNetStateHasItsOwnSlot()
    {
        var one = new SourceSharp.Shared.PlayerNetState();
        var two = new SourceSharp.Shared.PlayerNetState();
        one.BindLocal();
        two.BindLocal();

        Assert.True(one.IsLive && two.IsLive);   // both bound, so both slots are real
        Assert.NotEqual(0, SlotOf(one));
        Assert.NotEqual(SlotOf(one), SlotOf(two));
    }
}

