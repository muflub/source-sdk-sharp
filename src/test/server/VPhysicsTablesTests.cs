using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_engine_forwarders.md: a side's vphysics forwarder tables are bound
/// from its interface factory together, on first use and only then, and a
/// table the factory does not serve fails that first use by name.
/// </summary>
public sealed unsafe class VPhysicsTablesTests : IDisposable
{
    // Every table VPhysicsTables binds, by the name native serves it under.
    private static readonly string[] Names =
    {
        PhysicsEnvironmentFwd.TableName, PhysicsObjectFwd.TableName, PhysicsShadowControllerFwd.TableName,
        PhysicsMotionControllerFwd.TableName, PhysicsPlayerControllerFwd.TableName,
        PhysicsFrictionSnapshotFwd.TableName, PhysicsFluidControllerFwd.TableName,
        PhysicsConstraintFwd.TableName, PhysicsConstraintGroupFwd.TableName, PhysicsSpringFwd.TableName,
        PhysicsCollisionSetFwd.TableName, PhysicsCollisionDataFwd.TableName,
        VPhysicsKeyParserFwd.TableName, PhysicsCollisionEventFwd.TableName,
    };

    // One header per table, long enough for any struct, each with its own
    // slot array so a fact can tell the tables apart.
    private readonly Dictionary<string, nint> _headers = new(StringComparer.Ordinal);
    private readonly List<nint> _blocks = new();
    private readonly List<string> _asked = new();
    private string? _unserved;

    public VPhysicsTablesTests()
    {
        foreach (string name in Names)
        {
            var header = (ForwarderTableHeader*)NativeMemory.AllocZeroed((nuint)sizeof(ForwarderTableHeader));
            header->Slots = (void**)NativeMemory.AllocZeroed(256, (nuint)sizeof(void*));
            header->SlotCount = 256;
            _blocks.Add((nint)header->Slots);
            _blocks.Add((nint)header);
            _headers[name] = (nint)header;
        }
    }

    public void Dispose()
    {
        foreach (nint block in _blocks)
            NativeMemory.Free((void*)block);
    }

    private void* Acquire(SourceSharpFactoryType type, byte* version, int* entries, byte* error, int errorBytes)
    {
        string name = Vtable.TextOf(version, 64) ?? string.Empty;
        _asked.Add(name);
        bool served = type == SourceSharpFactoryType.Internal && name != _unserved
                      && _headers.TryGetValue(name, out _);
        if (entries != null)
            *entries = served ? 256 : 0;
        return served ? (void*)_headers[name] : null;
    }

    [Fact]
    public void NothingIsAskedForUntilATableIsFirstUsed()
    {
        _ = new VPhysicsTables(new NativeInterfaceFactory(Acquire));

        Assert.Empty(_asked);
    }

    [Fact]
    public void TheFirstUseBindsEveryTableOnceAndEachIsItsOwn()
    {
        var tables = new VPhysicsTables(new NativeInterfaceFactory(Acquire));

        PhysicsObjectFwd* objects = tables.PhysicsObject;

        Assert.Equal(Names, _asked);
        Assert.Equal((nint)((ForwarderTableHeader*)_headers[PhysicsObjectFwd.TableName])->Slots, (nint)objects);
        Assert.Equal((nint)((ForwarderTableHeader*)_headers[PhysicsCollisionEventFwd.TableName])->Slots,
                     (nint)tables.PhysicsCollisionEvent);

        // Bound once. The factory caches what it answered, so a second ask
        // would not reach Acquire; what would show is a rebind reading the
        // header again. Point the header at another slot array: the tables
        // still answer the one they bound.
        var header = (ForwarderTableHeader*)_headers[PhysicsObjectFwd.TableName];
        void** bound = header->Slots;
        header->Slots = ((ForwarderTableHeader*)_headers[PhysicsSpringFwd.TableName])->Slots;
        Assert.Equal((nint)bound, (nint)tables.PhysicsObject);
        header->Slots = bound;
    }

    [Fact]
    public void ATableTheFactoryDoesNotServeFailsTheFirstUseByName()
    {
        _unserved = PhysicsSpringFwd.TableName;
        var tables = new VPhysicsTables(new NativeInterfaceFactory(Acquire));

        var refused = Assert.Throws<InterfaceAcquisitionException>(() => (nint)tables.PhysicsObject);

        Assert.Contains(PhysicsSpringFwd.TableName, refused.Message);
    }
}
