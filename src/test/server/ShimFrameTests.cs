using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

//=============================================================================
// THE PILOT AREA, DISPATCHED THROUGH A FORWARDER TABLE BUILT IN THIS FILE.
//
// plan_native_api.md Phase 2 made EngineFrame the first area to call through
// a generated struct; plan_forwarders_everywhere.md phase 1 made that struct
// the area's forwarder table (SSFwd_ShimFrame, libss_fwd_server) rather than
// the object's vtable. What has to be true of it is three things:
//
//   1. THE FIELD'S POSITION IS THE SLOT. A call lands in the slot the struct's
//      field order says and no index is written anywhere -- so a table built
//      here with a distinct function in each position is enough to catch a
//      member wired to its neighbour.
//
//   2. THE COUNT CHECK IS `managed <= native`: the table only appends
//      (tools/engine-forwarders refuses anything else), so native gaining
//      forwarders must not refuse an assembly that has not been rebuilt, while
//      managed expecting MORE than native serves is a stale struct and is
//      refused by name.
//
//   3. THE MARSHALLING. `bool` is not blittable in an unmanaged function
//      pointer, so PhysicsRunThinkFunctions crosses as an int and the
//      conversion happens at the one-line call site.
//
// WHAT IS NOT HERE, DELIBERATELY: anything asserting that the frame RUNS.
// tools/gate5 and `make live-gate` own that, and they own it in a real process
// -- these methods run sixty-six times a second, so two map loads exercise
// every one of them. A hermetic test that claimed to check the frame order
// would be asserting its own fixture.
//=============================================================================
public unsafe class ShimFrameTests
{
    //-------------------------------------------------------------------------
    // 1. THE LAYOUT.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheShimFrameTableHasNineteenSlots()
        // One forwarder per method IShimFrame declares (an every=True row in
        // tools/engine-forwarders), and nothing else.
        //
        // Seventeen until step 1g, which split StandaloneRunFrame into
        // StandaloneUpdate and StandaloneFinish so the managed think pass could
        // run between them (plan_entity.md §3.4); eighteen until the
        // sub-ioqueue lane appended DeliverToSdkEntities.
        => Assert.Equal(19, Forwarders.SlotCount<ShimFrameFwd>());

    [Fact]
    public void EveryShimFrameFieldIsPointerSized()
    {
        // A forwarder table is an array of function pointers, so a struct
        // describing one is its field count times eight and nothing else. A
        // field that was not pointer-sized would make every slot below it read
        // the wrong entry.
        Assert.Equal(19 * sizeof(void*), sizeof(ShimFrameFwd));
    }

    [Fact]
    public void TheFirstFieldIsSlotZeroAndTheLastIsSlotEighteen()
    {
        // The generated file's own claim, read as BYTE OFFSETS rather than as
        // an order in a listing -- which is what the ABI actually cares about.
        var layout = typeof(ShimFrameFwd);

        int first = (int)Marshal.OffsetOf(layout, "IsSimulatingOnAlternateTicks");
        int last = (int)Marshal.OffsetOf(layout, "DeliverToSdkEntities");

        Assert.Equal(0, first);
        Assert.Equal(18 * sizeof(void*), last);
    }

    [Fact]
    public void PhysicsRunThinkFunctionsIsSlotSix()
        // A MIDDLE SLOT, named, because the two ends agreeing does not say the
        // eighteen in between are in the header's order -- and a reorder in
        // the middle is precisely what the span check cannot see.
        => Assert.Equal(6 * sizeof(void*),
                        (int)Marshal.OffsetOf(typeof(ShimFrameFwd),
                                              "PhysicsRunThinkFunctions"));

    //-------------------------------------------------------------------------
    // 2. DISPATCH, over a table this file builds.
    //-------------------------------------------------------------------------

    private static int s_TickAnswer;
    private static int s_PhysicsSawSimulating;
    private static int s_ServiceEventQueueCalls;

    [UnmanagedCallersOnly]
    private static int TickCountAnswers(void* self) => s_TickAnswer;

    [UnmanagedCallersOnly]
    private static float GetFrameTimeAnswers(void* self) => 0.015f;

    [UnmanagedCallersOnly]
    private static void PhysicsRecords(void* self, int simulating)
        => s_PhysicsSawSimulating = simulating;

    [UnmanagedCallersOnly]
    private static void ServiceEventQueueCounts(void* self)
        => s_ServiceEventQueueCalls++;

    /// <summary>
    /// A forwarder table of the struct's length with a distinct function in
    /// the four slots the facts below reach, written by index, and an object
    /// it is called on. Nothing reads the object: a forwarder table is handed
    /// the object, it does not live in it.
    /// </summary>
    private static void* FakeShim(out void** table)
    {
        table = (void**)NativeMemory.AllocZeroed((nuint)Forwarders.SlotCount<ShimFrameFwd>(), (nuint)sizeof(void*));

        table[1] = (delegate* unmanaged<void*, int>)&TickCountAnswers;
        table[2] = (delegate* unmanaged<void*, float>)&GetFrameTimeAnswers;
        table[6] = (delegate* unmanaged<void*, int, void>)&PhysicsRecords;
        table[8] = (delegate* unmanaged<void*, void>)&ServiceEventQueueCounts;

        return NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
    }

    /// <summary>
    /// An <see cref="EngineFrame"/> bound to a table this test built.
    ///
    /// <para>
    /// The fields are private and stay private -- production binds them through
    /// <c>Forwarders.TryAcquire</c> and nothing else -- so this reaches them by
    /// REFLECTION, which is a thing only a test may do. An accessor would be a
    /// second, permanent door into a class whose whole point is that it has one,
    /// and it would compile in the shipped assembly.
    /// </para>
    /// </summary>
    private static IEngineFrame BoundTo(void* self, void** table, EngineSide? side = null)
    {
        // Over an engine of the fact's own (plan_abi_hotreload D8), whose
        // globals are unbound, so the reads reach the table.
        object frame = new EngineFrame(side ?? new EngineSide(Side.Server));

        Set(frame, "_self", Pointer.Box(self, typeof(void*)));
        Set(frame, "_vtbl", Pointer.Box(table, typeof(ShimFrameFwd*)));

        return (IEngineFrame)frame;
    }

    private static void Set(object target, string field, object value)
        => target.GetType()
                 .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
                 .SetValue(target, value);

    [Fact]
    public void TickCountReachesSlotOneWhenTheGlobalVarsPointerIsNotBound()
    {
        // GPGLOBALS IS NOT BOUND IN A TEST PROCESS, which is what makes this
        // fact able to see the binding at all: on a live server TickCount reads
        // the engine's own struct and never touches the table. The live probe
        // (ShimProbes.Frame) is the other half -- it calls the SLOT directly and
        // holds it against the CGlobalVars read.
        var side = new EngineSide(Side.Server);
        Assert.True(side.GlobalVarsPointer == 0,
                    "no engine set gpGlobals for the frame's side, so the fallback is "
                    + "the path under test");

        s_TickAnswer = 4242;

        void* self = FakeShim(out void** table);
        IEngineFrame frame = BoundTo(self, table, side);

        Assert.Equal(4242, frame.TickCount());

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void GetFrameTimeReachesSlotTwoAndNotSlotOne()
    {
        // THE ARM THAT SAYS THE POSITION IS THE SLOT. Slot 1 answers an int and
        // slot 2 a float; a member wired to its neighbour would come back with
        // the tick reinterpreted, not with a compile error.
        s_TickAnswer = 7;

        void* self = FakeShim(out void** table);
        IEngineFrame frame = BoundTo(self, table);

        Assert.Equal(0.015f, frame.GetFrameTime(), 5);

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void PhysicsRunThinkFunctionsPassesTrueAsOne()
    {
        // C# `bool` is NOT blittable in an unmanaged function pointer, so the
        // generated struct spells the parameter `int` and the conversion happens
        // at the one-line call site rather than in a hidden marshaller. The flat
        // ABI field it replaces was `void (*)( int32_t )` for the same reason.
        s_PhysicsSawSimulating = -1;

        void* self = FakeShim(out void** table);
        IEngineFrame frame = BoundTo(self, table);

        frame.PhysicsRunThinkFunctions(true);
        Assert.Equal(1, s_PhysicsSawSimulating);

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void PhysicsRunThinkFunctionsPassesFalseAsZero()
    {
        s_PhysicsSawSimulating = -1;

        void* self = FakeShim(out void** table);
        IEngineFrame frame = BoundTo(self, table);

        frame.PhysicsRunThinkFunctions(false);
        Assert.Equal(0, s_PhysicsSawSimulating);

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void AVoidStepReachesItsOwnSlotAndIsCalledExactlyOnce()
    {
        s_ServiceEventQueueCalls = 0;

        void* self = FakeShim(out void** table);
        IEngineFrame frame = BoundTo(self, table);

        frame.ServiceEventQueue();

        Assert.Equal(1, s_ServiceEventQueueCalls);

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    //-------------------------------------------------------------------------
    // 2a. WHAT A REFUSED BIND SAYS. Regression: it said it twice.
    //-------------------------------------------------------------------------

    /// <summary>
    /// An area whose factory refuses everything, so the first call binds
    /// nothing and throws.
    /// </summary>
    private static IEngineFrame RefusedArea(EngineSide side)
        => new EngineFrame(side, new NativeInterfaceFactory(new RefusingSeam().Acquire));

    [Fact]
    public void ARefusedShimBindReportsItsContextOnceRatherThanTwice()
    {
        // THIS FACT EXISTS BECAUSE A LIVE RUN FOUND THE BUG AND NOTHING ELSE DID.
        // The factory-row mutation arm deleted the internalFactory's row for
        // SourceSharpFrame001, rebuilt, and loaded dm_lockdown; the frame threw
        // once per frame -- correctly, loudly, and NAMED -- but the message read
        //
        //   could not acquire "SourceSharpFrame001" from the Internal factory:
        //   could not acquire "SourceSharpFrame001" from the Internal factory:
        //   factory does not know that interface
        //
        // 1,795 times in one map load. The acquire already composes the
        // context (NativeInterfaceFactory.TryCreate) and the exception's
        // constructor composed it again.
        //
        // AND IT GOES THROUGH Bind() RATHER THAN THROUGH THE EXCEPTION HELPER,
        // which is the difference between a fact and a decoration: the helper
        // was never wrong. What was wrong was which constructor this class
        // called, and only a fact that reaches the call site can see that.
        var side = new EngineSide(Side.Server);
        Assert.True(side.GlobalVarsPointer == 0,
                    "gpGlobals is unbound for the area's side, so TickCount falls "
                    + "through to the table and the bind is reached");

        var thrown = Assert.Throws<InterfaceAcquisitionException>(
            () => RefusedArea(side).TickCount());

        int said = 0;
        int at = 0;

        while ((at = thrown.Message.IndexOf("could not acquire", at, StringComparison.Ordinal)) >= 0)
        {
            said++;
            at += 1;
        }

        Assert.True(said == 1,
                    $"the failure says its own context once and it said it {said} times: "
                    + thrown.Message);
    }

    [Fact]
    public void ARefusedShimBindStillNamesTheVersionStringAndTheReason()
    {
        // The other half: saying it once must not mean saying less. A reader of
        // this line has to be able to tell WHICH interface refused and WHY
        // without a second lookup.
        var thrown = Assert.Throws<InterfaceAcquisitionException>(
            () => RefusedArea(new EngineSide(Side.Server)).TickCount());

        Assert.Contains("SourceSharpFrame001", thrown.Message);
        Assert.Contains("Internal", thrown.Message);
        Assert.Contains("factory does not know that interface", thrown.Message);
        Assert.Equal(SourceSharpFactoryType.Internal, thrown.FactoryType);
        Assert.Equal("SourceSharpFrame001", thrown.InterfaceVersion);
    }

    [Fact]
    public void AnAreaThatCannotAcquireThrowsRatherThanDoingNothing()
    {
        // THE BEHAVIOUR CHANGE THIS PHASE MAKES, asserted rather than assumed.
        // The flat slot it replaced was a null function pointer: Engine.cs
        // tested it, returned, and the frame step quietly did not happen. An
        // area that cannot reach its interface is not a degraded area, it is a
        // bug, and it now says so at the site that asked.
        Assert.Throws<InterfaceAcquisitionException>(
            () => RefusedArea(new EngineSide(Side.Server)).ServiceEventQueue());
    }

    //-------------------------------------------------------------------------
    // 3. THE AREA'S SURFACE.
    //-------------------------------------------------------------------------

    [Fact]
    public void EngineFrameOffersExactlyIEngineFrameAndNothingElse()
    {
        // The same rule VtableBindingTests enforces on the five engine impls:
        // POLICY IS EXPRESSED BY ABSENCE, and absence is only a rule if
        // something notices when a method appears. A public member here that
        // IEngineFrame does not declare would be a second way into the shim
        // that game code could reach without asking for the area.
        Type impl = typeof(EngineServices).Assembly.GetType("SourceSharp.Abi.EngineFrame")!;

        string[] declared = impl
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        string[] contract = typeof(IEngineFrame)
            .GetMethods()
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(contract, declared);
    }

    [Fact]
    public void EngineFrameIsInternalSoNoPointerReachesGameCode()
    {
        Type impl = typeof(EngineServices).Assembly.GetType("SourceSharp.Abi.EngineFrame")!;

        Assert.False(impl.IsPublic,
                     "the area facade holds the interface pointer; publishing it "
                     + "would undo what the layouts being internal buys");
    }

    [Fact]
    public void TheAreaStillResolvesToItsBinding()
        // The container pairs IEngineFoo with EngineFoo by NAME, reflectively.
        // Moving EngineFrame out of EngineBinding.cs into its own file must not
        // break that pairing, and nothing in the compiler would have said so.
        => Assert.NotNull(typeof(EngineServices).Assembly.GetType("SourceSharp.Abi.EngineFrame"));

    [Fact]
    public void TheVersionStringFollowsTheEnginesOwnConvention()
        // SourceSharp<Area>001, with no Client/Server suffix because this area
        // does not split. The string is the contract: it is what the internal
        // factory switches on and what a refusal names.
        => Assert.Equal("SourceSharpFrame001", EngineFrame.InterfaceVersion);

    //-------------------------------------------------------------------------
    // 4. WHAT THE FORWARDER COUNT CHECK ACCEPTS AND REFUSES.
    //-------------------------------------------------------------------------

    [Fact]
    public void AStructShorterThanTheServedTableIsAccepted()
    {
        // THE WHOLE REASON THE CHECK IS `<=` RATHER THAN `==`. Appending a
        // method to IShimFrame appends its forwarder at the END and moves
        // nothing, so a library that gained one must still bind an assembly
        // compiled before it. Driven through the factory seam, because there is
        // no native half in this process.
        Assert.True(AcquireAgainstNative(nativeEntries: 20, out string why),
                    $"a shorter managed struct binds: {why}");
    }

    [Fact]
    public void AStructLongerThanTheServedTableIsRefusedNamingBothNumbers()
    {
        Assert.False(AcquireAgainstNative(nativeEntries: 18, out string why),
                     "a managed struct expecting more slots than native serves "
                     + "must not bind");

        Assert.Contains("18", why);
        Assert.Contains("19", why);
    }

    [Fact]
    public void AStructExactlyTheServedTablesLengthIsAccepted()
        => Assert.True(AcquireAgainstNative(nativeEntries: 19, out _),
                       "equality is the ordinary case and is not excluded by `<=`");

    /// <summary>
    /// Drive the REAL <c>Forwarders.TryAcquire</c> against a native half that
    /// serves the frame object and a forwarder table of
    /// <paramref name="nativeEntries"/> slots.
    ///
    /// <para>
    /// The internal overload takes the factory, so what is under test is the
    /// shipped comparison rather than a copy of it written here. An earlier
    /// draft of this helper restated the <c>declared &gt; entries</c> test and
    /// would have passed against any comparison at all, including none.
    /// </para>
    ///
    /// <para>
    /// Called directly rather than through reflection: <c>InternalsVisibleTo</c>
    /// already points at this assembly, and a reflective call cannot hand a
    /// <c>void*</c> out-parameter across <c>Invoke</c> without boxing every one
    /// of them.
    /// </para>
    /// </summary>
    private static bool AcquireAgainstNative(int nativeEntries, out string why)
    {
        var seam = new EntrySeam(nativeEntries);
        var factory = new NativeInterfaceFactory(seam.Acquire);

        bool ok = Forwarders.TryAcquire(
            factory, SourceSharpFactoryType.Internal, EngineFrame.InterfaceVersion,
            out void* _, out ShimFrameFwd* _, out why);

        seam.Free();
        return ok;
    }

    /// <summary>
    /// A native half that serves nothing, writing the closed-set literal the
    /// real switch writes for a name it does not know.
    /// </summary>
    private sealed class RefusingSeam
    {
        public void* Acquire(SourceSharpFactoryType type, byte* version,
                             byte* error, int errorBytes)
        {
            ReadOnlySpan<byte> reason = "factory does not know that interface"u8;

            if (error != null && errorBytes > reason.Length)
            {
                reason.CopyTo(new Span<byte>(error, reason.Length));
                error[reason.Length] = 0;
            }

            return null;
        }
    }

    /// <summary>
    /// A native half that answers the frame's version string with an object
    /// and <c>SSFwd_ShimFrame</c> with a table header of the count it was
    /// built with, as SSFwd_Serve does.
    ///
    /// <para>
    /// It has to answer with REAL answers: the count check runs after both
    /// acquires succeed, and a null answer would fail before reaching it — a
    /// seam that refused everything would make the accept arms pass for the
    /// wrong reason and the refuse arms pass for no reason at all.
    /// </para>
    /// </summary>
    private sealed class EntrySeam
    {
        private readonly void** _slots;
        private readonly ForwarderTableHeader* _header;
        private readonly void* _self;

        public EntrySeam(int entries)
        {
            _slots = (void**)NativeMemory.AllocZeroed((nuint)Math.Max(entries, 1), (nuint)sizeof(void*));
            _header = (ForwarderTableHeader*)NativeMemory.AllocZeroed(1, (nuint)sizeof(ForwarderTableHeader));
            _header->Slots = _slots;
            _header->SlotCount = entries;
            _self = NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
        }

        public void Free()
        {
            NativeMemory.Free(_slots);
            NativeMemory.Free(_header);
            NativeMemory.Free(_self);
        }

        public void* Acquire(SourceSharpFactoryType type, byte* version,
                             byte* error, int errorBytes)
        {
            string name = new string((sbyte*)version);

            return name == ShimFrameFwd.TableName ? _header : _self;
        }
    }
}
