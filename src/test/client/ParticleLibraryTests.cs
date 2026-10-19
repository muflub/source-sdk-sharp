using System.Runtime.InteropServices;
using System.Text;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_particles_so.md: managed binds libss_particles.so's ISSParticleLib
/// through the client's internal factory (SSParticleLib001), called through
/// its forwarder table (SSFwd_ParticleLib, ParticleLibFwd:
/// plan_forwarders_everywhere.md). The facts: the table's ends and the slots
/// this binding reaches, the dispatch over a table this file builds, the
/// refusal when nothing serves the name or serves too few slots, and the
/// throttle read going through the library.
/// </summary>
public unsafe class ParticleLibraryTests
{
    private static int SlotOf(string field)
        => (int)Marshal.OffsetOf(typeof(ParticleLibFwd), field) / sizeof(void*);

    //-------------------------------------------------------------------------
    // 1. THE TABLE. tools/engine-forwarders lays it out in the header's order
    // and only appends; these name the slots the binding reaches, so a
    // reorder in the middle (which `managed <= native` cannot see) reddens
    // here.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheParticleLibTableHasFortyFiveSlots()
        => Assert.Equal(45, Forwarders.SlotCount<ParticleLibFwd>());

    [Fact]
    public void ConnectIsSlotZeroAndTheAnchorIsTheLast()
    {
        Assert.Equal(0, SlotOf("Connect"));
        Assert.Equal(44, SlotOf("MgrUncacheAllParticleSystems"));
    }

    [Fact]
    public void TheSlotsTheBindingReachesAreWhereTheHeaderPutsThem()
    {
        Assert.Equal(2, SlotOf("Mgr"));
        Assert.Equal(35, SlotOf("MgrGetParticleSystemCount"));
        Assert.Equal(36, SlotOf("MgrGetParticleSystemNameFromIndex"));
        Assert.Equal(38, SlotOf("MgrParticleThrottleScaling"));
    }

    [Fact]
    public void TheVersionStringIsTheHeadersDefine()
        // SS_PARTICLE_LIB_INTERFACE_VERSION in iparticlelib.h.
        => Assert.Equal("SSParticleLib001", ParticleLibrary.InterfaceVersion);

    //-------------------------------------------------------------------------
    // 2. DISPATCH, over a table this file builds and serves by name.
    //-------------------------------------------------------------------------

    private static readonly nint FakeManager = 0x5150;
    private static nint s_ThrottleSawManager;
    private static nint s_NameSawManager;
    private static int s_NameSawIndex;
    private static readonly byte[] s_Name = Encoding.UTF8.GetBytes("env_fire_small\0");
    private static GCHandle s_NamePin;

    [UnmanagedCallersOnly]
    private static void* MgrAnswers(void* self) => (void*)FakeManager;

    [UnmanagedCallersOnly]
    private static float ThrottleRecords(void* self, void* mgr)
    {
        s_ThrottleSawManager = (nint)mgr;
        return 0.25f;
    }

    [UnmanagedCallersOnly]
    private static int CountAnswers(void* self, void* mgr) => 3;

    [UnmanagedCallersOnly]
    private static byte* NameRecords(void* self, void* mgr, int index)
    {
        s_NameSawManager = (nint)mgr;
        s_NameSawIndex = index;
        return (byte*)s_NamePin.AddrOfPinnedObject();
    }

    /// <summary>
    /// The library's object and its forwarder table, served by the internal
    /// factory as the client serves them: the object by version, the table
    /// by its name, the table's count <c>entries</c>.
    /// </summary>
    private sealed class Served : IDisposable
    {
        public readonly void** Table;
        public readonly void* Self;
        public readonly ForwarderTableHeader* Header;

        public Served(int entries)
        {
            Table = (void**)NativeMemory.AllocZeroed((nuint)Forwarders.SlotCount<ParticleLibFwd>(), (nuint)sizeof(void*));
            Table[SlotOf("Mgr")] = (delegate* unmanaged<void*, void*>)&MgrAnswers;
            Table[SlotOf("MgrParticleThrottleScaling")] = (delegate* unmanaged<void*, void*, float>)&ThrottleRecords;
            Table[SlotOf("MgrGetParticleSystemCount")] = (delegate* unmanaged<void*, void*, int>)&CountAnswers;
            Table[SlotOf("MgrGetParticleSystemNameFromIndex")] = (delegate* unmanaged<void*, void*, int, byte*>)&NameRecords;
            Self = NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
            Header = (ForwarderTableHeader*)NativeMemory.AllocZeroed(1, (nuint)sizeof(ForwarderTableHeader));
            Header->Slots = Table;
            Header->SlotCount = entries;
        }

        public void* Acquire(SourceSharpFactoryType type, byte* version, byte* error, int errorBytes)
        {
            if (type != SourceSharpFactoryType.Internal)
                return null;

            string name = Vtable.TextOf(version, 64) ?? string.Empty;
            return name == ParticleLibrary.InterfaceVersion ? Self
                 : name == ParticleLibFwd.TableName ? Header
                 : null;
        }

        public void Dispose()
        {
            NativeMemory.Free(Table);
            NativeMemory.Free(Self);
            NativeMemory.Free(Header);
        }
    }

    private static ParticleLibrary Over(Served served) => new(new NativeInterfaceFactory(served.Acquire));

    [Fact]
    public void ThrottleScalingAsksTheManagerTheLibraryHandsBack()
    {
        using var served = new Served(45);
        s_ThrottleSawManager = 0;

        Assert.Equal(0.25f, Over(served).ThrottleScaling());
        Assert.Equal(FakeManager, s_ThrottleSawManager);
    }

    [Fact]
    public void ASystemNameIsReadThroughTheManagerByIndex()
    {
        s_NamePin = GCHandle.Alloc(s_Name, GCHandleType.Pinned);
        try
        {
            using var served = new Served(45);
            s_NameSawIndex = -1;

            Assert.Equal("env_fire_small", Over(served).SystemName(2));
            Assert.Equal(2, s_NameSawIndex);
            Assert.Equal(FakeManager, s_NameSawManager);
        }
        finally
        {
            s_NamePin.Free();
        }
    }

    [Fact]
    public void AnIndexPastTheCountIsNullAndNeverReachesTheLibrary()
    {
        using var served = new Served(45);
        s_NameSawIndex = -1;
        ParticleLibrary library = Over(served);

        Assert.Null(library.SystemName(3));
        Assert.Null(library.SystemName(-1));
        Assert.Equal(-1, s_NameSawIndex);
    }

    [Fact]
    public void ALibraryWithFewerSlotsThanTheLayoutIsRefused()
    {
        // The count rule: managed <= native. A table of 44 slots cannot hold
        // the 45 this struct reaches.
        using var served = new Served(44);
        Assert.Throws<InterfaceAcquisitionException>(() => Over(served).ThrottleScaling());
    }

    [Fact]
    public void NothingServingTheNameThrowsRatherThanAnsweringOne()
    {
        var unserved = new ParticleLibrary(new NativeInterfaceFactory(
            (type, version, error, bytes) =>
            {
                return null;
            }));
        Assert.Throws<InterfaceAcquisitionException>(() => unserved.ThrottleScaling());
    }

    //-------------------------------------------------------------------------
    // 3. THE THROTTLE'S ROUTE: the particle view slots read it from the
    // library (the IShimParticlesClient slot it replaced is gone).
    //-------------------------------------------------------------------------

    private sealed class CountingLibrary : IParticleLibrary
    {
        public int Asked;

        public float ThrottleScaling()
        {
            Asked++;
            return 0.5f;
        }

        public int SystemCount() => 0;

        public string? SystemName(int index) => null;
    }

    [Fact]
    public void WithNoClientAttachedTheThrottleIsUnrestrictedAndTheLibraryIsNotAsked()
    {
        var library = new CountingLibrary();
        var slots = new ClientParticleViewSlots(new EngineSide(Side.Client), library);

        Assert.Equal(1.0f, slots.ThrottleScaling());
        Assert.Equal(0, library.Asked);
    }

    [Fact]
    public void WithTheClientAttachedTheThrottleIsTheLibrarysAnswer()
    {
        using var root = new FakeShimRoot();
        var side = new EngineSide(Side.Client);
        Assert.True(side.AttachClientShims(root.Bind(), out string why), why);
        var library = new CountingLibrary();

        Assert.Equal(0.5f, new ClientParticleViewSlots(side, library).ThrottleScaling());
        Assert.Equal(1, library.Asked);
    }
}
