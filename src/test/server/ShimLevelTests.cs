using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

//=============================================================================
// THE SECOND SHIM AREA -- plan_native_api.md Phase 6, wave 2.
//
// The recipe is EngineFrame's and the facts are the same three kinds, because
// what can go wrong is the same three things: a slot reached by position, a
// marshalling done at the one-line call site, and a surface that must be
// exactly the area's interface.
//
// WHAT IS DELIBERATELY NOT HERE: anything asserting that a level LOADS. Every
// method on IShimLevel is a step of the level lifecycle, so a hermetic test
// that claimed to check them would be asserting its own fixture. `make
// live-gate` loads dm_lockdown TWICE, which walks the init half and the
// shutdown half of this interface in order, four times over -- and a
// level-scoped mistake lives in the reload, which is exactly what one map load
// structurally cannot see.
//=============================================================================
public unsafe class ShimLevelTests
{
    private static int SlotOf(string field)
        => (int)Marshal.OffsetOf(typeof(ShimLevelFwd), field) / sizeof(void*);

    //-------------------------------------------------------------------------
    // 1. THE FORWARDER TABLE (SSFwd_ShimLevel, plan_forwarders_everywhere.md):
    // one slot per method ishim_level.h declares, in its order.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheShimLevelTableHasThirtyOneSlots()
        //
        // Twenty-two until the save lane's stage 2 appended LoadSaveGameState;
        // twenty-three until plan_server_novalve P2.1 appended FileSizeOnPath
        // and ReadFileOnPath; twenty-five until P5 appended SpawnWorld;
        // twenty-six until plan_server_native_v2 T4a appended IsPrecacheAllowed;
        // twenty-seven until T4e appended SetLoadType; thirty-one after T4e's
        // four system walks retired and its seven system stages appended.
        => Assert.Equal(31, Forwarders.SlotCount<ShimLevelFwd>());

    [Fact]
    public void EveryShimLevelFieldIsPointerSized()
        => Assert.Equal(31 * sizeof(void*), sizeof(ShimLevelFwd));

    [Fact]
    public void TheFirstFieldIsSlotZeroAndTheLastIsSlotThirty()
    {
        // Four lower after plan_server_native_v2 T4e retired the four system
        // walks (tools/engine-forwarders --reorder): InitPostEntitySystems,
        // ShutdownPreClearSteamSystems, ShutdownPreEntitySystems and
        // ShutdownPostEntitySystems.
        Assert.Equal(0, SlotOf("ResetServerTime"));
        Assert.Equal(18, SlotOf("LoadSaveGameState"));
        Assert.Equal(19, SlotOf("FileSizeOnPath"));
        Assert.Equal(20, SlotOf("ReadFileOnPath"));
        Assert.Equal(21, SlotOf("SpawnWorld"));
        Assert.Equal(22, SlotOf("IsPrecacheAllowed"));
        Assert.Equal(23, SlotOf("SetLoadType"));
        Assert.Equal(24, SlotOf("PhysicsInit"));
        Assert.Equal(30, SlotOf("ResetEntitySlots"));
    }

    [Fact]
    public void TheNewSlotWasAPPENDEDAndDidNotMoveTheOnesBeforeIt()
        // THE CHECK THE COUNT CANNOT MAKE. `managed <= native` protects against
        // an append and against nothing else, so a method INSERTED in the
        // middle passes every count there is and silently misbinds every slot
        // below it. PropDataClearFile was the last field before stage 2 added
        // LoadSaveGameState; it was at 21 until plan_server_native_v2 T4e's
        // deliberate --reorder took four walks out above it, and is at 17.
        => Assert.Equal(17, SlotOf("PropDataClearFile"));

    [Fact]
    public void ResetDeleteListIsSlotSeven()
        // A MIDDLE SLOT, named. The two ends agreeing says nothing about the
        // twenty in between, and a reorder in the middle is precisely what the
        // `managed <= native` span check cannot see.
        => Assert.Equal(7, SlotOf("ResetDeleteList"));

    //-------------------------------------------------------------------------
    // 2. DISPATCH, over a table this file builds.
    //-------------------------------------------------------------------------

    private static int s_DeleteListAnswer;
    private static int s_PrecacheAllowedSaw;
    private static int s_PrecacheAllowedAnswer;
    private static int s_TemplateCountSaw;
    private static string? s_MapNameSaw;
    private static int s_ClearEntityListCalls;

    [UnmanagedCallersOnly]
    private static int ResetDeleteListAnswers(void* self) => s_DeleteListAnswer;

    [UnmanagedCallersOnly]
    private static void SetPrecacheAllowedRecords(void* self, int allowed)
        => s_PrecacheAllowedSaw = allowed;

    [UnmanagedCallersOnly]
    private static int IsPrecacheAllowedAnswers(void* self) => s_PrecacheAllowedAnswer;

    [UnmanagedCallersOnly]
    private static void PrecachePointTemplatesRecords(
        void* self, byte* blobs, int* sizes, int count)
        => s_TemplateCountSaw = count;

    [UnmanagedCallersOnly]
    private static void SetLowViolenceRecords(void* self, byte* mapName)
        => s_MapNameSaw = Vtable.TextOf(mapName, Vtable.MaxPath);

    [UnmanagedCallersOnly]
    private static void ClearEntityListCounts(void* self) => s_ClearEntityListCalls++;

    private static void* FakeShim(out void** table)
    {
        table = (void**)NativeMemory.AllocZeroed(
            (nuint)Forwarders.SlotCount<ShimLevelFwd>(), (nuint)sizeof(void*));

        table[SlotOf("ResetDeleteList")] =
            (delegate* unmanaged<void*, int>)&ResetDeleteListAnswers;
        table[SlotOf("SetPrecacheAllowed")] =
            (delegate* unmanaged<void*, int, void>)&SetPrecacheAllowedRecords;
        table[SlotOf("IsPrecacheAllowed")] =
            (delegate* unmanaged<void*, int>)&IsPrecacheAllowedAnswers;
        table[SlotOf("PrecachePointTemplates")] =
            (delegate* unmanaged<void*, byte*, int*, int, void>)&PrecachePointTemplatesRecords;
        table[SlotOf("SetLowViolence")] =
            (delegate* unmanaged<void*, byte*, void>)&SetLowViolenceRecords;
        table[SlotOf("ClearEntityList")] =
            (delegate* unmanaged<void*, void>)&ClearEntityListCounts;

        // The object a forwarder is handed; nothing reads it.
        return NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
    }

    private static IEngineLevel BoundTo(void* self, void** table)
    {
        object level = SideBindings.Of<IEngineLevel>(new EngineSide(Side.Server));

        Set(level, "_self", Pointer.Box(self, typeof(void*)));
        Set(level, "_vtbl", Pointer.Box(table, typeof(ShimLevelFwd*)));

        return (IEngineLevel)level;
    }

    private static void Set(object target, string field, object value)
        => target.GetType()
                 .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
                 .SetValue(target, value);

    [Fact]
    public void ResetDeleteListAnswersThroughItsOwnSlot()
    {
        s_DeleteListAnswer = 5;

        void* self = FakeShim(out void** table);

        Assert.Equal(5, BoundTo(self, table).ResetDeleteList());

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    /// <summary>
    /// The container's level acquires through ITS OWN side's root
    /// (plan_abi_hotreload D8), not the process-wide factory that follows the
    /// active side: a level over a side whose root serves the area answers, and
    /// one over a side with no root is refused.
    /// </summary>
    [Fact]
    public void ALevelAcquiresThroughItsOwnSidesRoot()
    {
        s_DeleteListAnswer = 6;
        void* self = FakeShim(out void** table);
        var header = (ForwarderTableHeader*)NativeMemory.AllocZeroed(1, (nuint)sizeof(ForwarderTableHeader));
        try
        {
            var served = new EngineSide(Side.Server);
            header->Slots = table;
            header->SlotCount = Forwarders.SlotCount<ShimLevelFwd>();
            using var root = new FakeShimRoot()
                .Interface(EngineLevel.InterfaceVersion, self)
                .Interface(ShimLevelFwd.TableName, header);
            Assert.True(served.AttachShims(root.Bind(), out string why), why);

            Assert.Equal(6, SideBindings.Of<IEngineLevel>(served).ResetDeleteList());

            // The control: a side with no root behind it refuses the same call.
            var unserved = new EngineSide(Side.Server);
            Assert.Throws<InterfaceAcquisitionException>(() => SideBindings.Of<IEngineLevel>(unserved).ResetDeleteList());
        }
        finally
        {
            NativeMemory.Free(header);
            NativeMemory.Free(table);
            NativeMemory.Free(self);
        }
    }

    [Fact]
    public void IsPrecacheAllowedReadsNonZeroAsTrue()
    {
        void* self = FakeShim(out void** table);
        IEngineLevel level = BoundTo(self, table);

        s_PrecacheAllowedAnswer = 1;
        Assert.True(level.IsPrecacheAllowed());
        s_PrecacheAllowedAnswer = 0;
        Assert.False(level.IsPrecacheAllowed());

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void SetPrecacheAllowedPassesTrueAsOneAndFalseAsZero()
    {
        // C# `bool` is not blittable in an unmanaged function pointer, so the
        // generated struct spells the parameter `int` and the conversion is at
        // the one-line call site. The flat field it replaces was
        // `void (*)( int32_t )` for exactly the same reason.
        void* self = FakeShim(out void** table);
        IEngineLevel level = BoundTo(self, table);

        s_PrecacheAllowedSaw = -1;
        level.SetPrecacheAllowed(true);
        Assert.Equal(1, s_PrecacheAllowedSaw);

        level.SetPrecacheAllowed(false);
        Assert.Equal(0, s_PrecacheAllowedSaw);

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void AnEmptyTemplateListStillMakesTheCall()
    {
        // THE STEP IS THE RECORD OF HAVING REACHED THIS POINT IN THE LOAD, not
        // of having had work to do -- the god-class made this call
        // unconditionally and so does the facade. tools/gate5 records the
        // level-load sequence, so a step that stopped happening on a map with no
        // point_templates would be a reordering nothing else in this tree can
        // see.
        s_TemplateCountSaw = -1;

        void* self = FakeShim(out void** table);

        BoundTo(self, table).PrecachePointTemplates(Array.Empty<TemplateSpawn>());

        Assert.Equal(0, s_TemplateCountSaw);

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void AStringMemberCrossesAsNulTerminatedUtf8()
    {
        s_MapNameSaw = null;

        void* self = FakeShim(out void** table);

        BoundTo(self, table).SetLowViolence("dm_lockdown");

        Assert.Equal("dm_lockdown", s_MapNameSaw);

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void AnEmptyMapNameIsNotPassedDown()
    {
        // The god-class's own guard, moved rather than dropped: the native side
        // walks a NUL-terminated string and an empty one has nothing to say.
        s_MapNameSaw = "unchanged";

        void* self = FakeShim(out void** table);

        BoundTo(self, table).SetLowViolence(string.Empty);

        Assert.Equal("unchanged", s_MapNameSaw);

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void AVoidStepReachesItsOwnSlotAndIsCalledExactlyOnce()
    {
        s_ClearEntityListCalls = 0;

        void* self = FakeShim(out void** table);

        BoundTo(self, table).ClearEntityList();

        Assert.Equal(1, s_ClearEntityListCalls);

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    //-------------------------------------------------------------------------
    // 3. THE AREA'S SURFACE.
    //-------------------------------------------------------------------------

    [Fact]
    public void EngineLevelOffersExactlyIEngineLevelAndILevelFilesAndNothingElse()
    {
        Type impl = typeof(EngineServices).Assembly.GetType("SourceSharp.Abi.EngineLevel")!;

        string[] declared = impl
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        // Two faces of one native object: the load's STEPS, and the whole-file
        // reads the managed parsers need (plan_server_novalve.md P2.1).
        string[] contract = typeof(IEngineLevel)
            .GetMethods()
            .Concat(typeof(ILevelFiles).GetMethods())
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(contract, declared);
    }

    [Fact]
    public void EngineLevelIsInternalSoNoPointerReachesGameCode()
        => Assert.False(
               typeof(EngineServices).Assembly.GetType("SourceSharp.Abi.EngineLevel")!.IsPublic);

    [Fact]
    public void TheVersionStringFollowsTheEnginesOwnConvention()
        => Assert.Equal("SourceSharpLevel001", EngineLevel.InterfaceVersion);

    [Fact]
    public void AnAreaThatCannotAcquireThrowsRatherThanDoingNothing()
    {
        // The behaviour change the shim route makes, asserted rather than
        // assumed: the flat slots this replaced were null function pointers,
        // and Engine.cs tested them and RETURNED -- so a level step quietly did
        // not happen. Here it says so at the site that asked.
        // A side whose root serves no level area, reached as the side registers it.
        using var root = new FakeShimRoot();
        var side = new EngineSide(Side.Server);
        Assert.True(side.AttachShims(root.Bind(), out string why), why);
        var refused = SideBindings.Of<IEngineLevel>(side);

        Assert.Throws<InterfaceAcquisitionException>(() => refused.ClearEntityList());
    }
}
