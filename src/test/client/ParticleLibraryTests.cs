using System.Runtime.InteropServices;

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
    public void TheParticleLibTableHasNineSlots()
        // 48 while the row was every=True; the serialised ABI pass (2026-10-20)
        // named the nine ParticleLibrary calls, and client.so's own calls go
        // through the C++ vtable, which no forwarder is needed for.
        => Assert.Equal(9, Forwarders.SlotCount<ParticleLibFwd>());

    [Fact]
    public void TheSlotsAreTheRowsNamesInItsOrder()
    {
        Assert.Equal(0, SlotOf("Mgr"));
        Assert.Equal(4, SlotOf("MgrParticleThrottleScaling"));
        Assert.Equal(8, SlotOf("CollectionSetBounds"));
    }

    [Fact]
    public void AForwarderNoManagedCodeCallsIsNoField()
    {
        // The pass's pin: the 39 the row stopped naming are not fields, read
        // the same way as a live one.
        foreach (string gone in new[] { "Connect", "Disconnect", "Counters", "MgrInit", "MgrGetParticleSystemCount",
                                        "MgrGetParticleSystemNameFromIndex", "MgrUncacheAllParticleSystems" })
            Assert.Null(typeof(ParticleLibFwd).GetField(gone));
        Assert.NotNull(typeof(ParticleLibFwd).GetField("MgrParticleThrottleScaling"));
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

    [UnmanagedCallersOnly]
    private static void* MgrAnswers(void* self) => (void*)FakeManager;

    [UnmanagedCallersOnly]
    private static float ThrottleRecords(void* self, void* mgr)
    {
        s_ThrottleSawManager = (nint)mgr;
        return 0.25f;
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
        using var served = new Served(9);
        s_ThrottleSawManager = 0;

        Assert.Equal(0.25f, Over(served).ThrottleScaling());
        Assert.Equal(FakeManager, s_ThrottleSawManager);
    }

    [Fact]
    public void ALibraryWithFewerSlotsThanTheLayoutIsRefused()
    {
        // The count rule: managed <= native. A table of 8 slots cannot hold
        // the 9 this struct reaches.
        using var served = new Served(8);
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

    /// <summary>
    /// The client's particle throttle as its root registers it over
    /// <paramref name="side"/>, with <paramref name="library"/> registered first
    /// as the library it asks (plan_shim_areas_split phase 6).
    /// </summary>
    private static IParticleThrottle ViewSlots(EngineSide side, IParticleLibrary library)
    {
        var services = new ServiceContainer().AddInstance(library);
        new ClientEntityFetches().AddTo(services, new ClientModuleEngine(side));
        return services.Require<IParticleThrottle>();
    }

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

        public void DumpProfileInformation() { }
    }

    [Fact]
    public void WithTheClientAttachedTheThrottleIsTheLibrarysAnswer()
    {
        using var root = new FakeShimRoot();
        var side = TestSides.Create(Side.Client);
        Assert.True(side.Attachment.AttachShims(root.Bind(), out string why), why);
        var library = new CountingLibrary();

        Assert.Equal(0.5f, ViewSlots(side, library).ThrottleScaling());
        Assert.Equal(1, library.Asked);
    }
}
