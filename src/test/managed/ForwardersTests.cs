using System;
using System.Linq;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

//=============================================================================
// plan_engine_forwarders.md: a forwarder table binds only when native's count
// and every slot's hash are the generated struct's.
//
// The tables here are built in managed memory in SSForwarderTable's shape
// (src/fwd/native/ss_fwd.h): a slot array, a hash array and a count. Nothing
// is called through them; the facts are about what binds and what refuses.
//=============================================================================

public sealed unsafe class ForwardersTests
{
    // A table in native's shape, filled from ModelInfoFwd's own numbers and
    // then edited by the fact under test.
    private sealed class Served : IDisposable
    {
        public readonly ForwarderTableHeader* Header;
        public readonly void** Slots;
        public readonly uint* Hashes;

        public Served()
        {
            int count = ModelInfoFwd.SlotCount;
            Slots = (void**)NativeMemory.AllocZeroed((nuint)count, (nuint)sizeof(void*));
            Hashes = (uint*)NativeMemory.AllocZeroed((nuint)count, sizeof(uint));
            Header = (ForwarderTableHeader*)NativeMemory.AllocZeroed(1, (nuint)sizeof(ForwarderTableHeader));

            for (int i = 0; i < count; i++)
            {
                Slots[i] = (void*)(0x1000 + i);
                Hashes[i] = ModelInfoFwd.SlotHashes[i];
            }

            Header->Slots = Slots;
            Header->SlotHashes = Hashes;
            Header->SlotCount = count;
        }

        // A stand-in engine object: the forwarders are never called here, so
        // nothing reads through it.
        public static readonly nint EngineObject = 0x5150;

        public bool ServesTable = true;
        public bool ServesObject = true;

        // The factory seam: the header under ModelInfoFwd's name from the
        // internal factory, and the engine object under the server's model
        // info name from the app-system one.
        public void* Acquire(SourceSharpFactoryType type, byte* version, int* entries, byte* error, int errorBytes)
        {
            string name = Vtable.TextOf(version, 64) ?? string.Empty;
            bool table = ServesTable && type == SourceSharpFactoryType.Internal && name == ModelInfoFwd.TableName;
            bool engine = ServesObject && type == SourceSharpFactoryType.AppSystem
                          && name == EngineModelInfo.ServerInterfaceVersion;
            if (entries != null)
                *entries = table ? Header->SlotCount : engine ? 59 : 0;
            return table ? Header : engine ? (void*)EngineObject : null;
        }

        public void Dispose()
        {
            NativeMemory.Free(Slots);
            NativeMemory.Free(Hashes);
            NativeMemory.Free(Header);
        }
    }

    [Fact]
    public void AMatchingTableBindsToItsSlotArray()
    {
        using var served = new Served();

        Assert.True(Forwarders.TryBind(served.Header, out ModelInfoFwd* table, out string why), why);
        Assert.Equal((nint)served.Slots, (nint)table);
        Assert.Equal((nint)0x1000, (nint)table->GetModel);
        Assert.Equal(string.Empty, why);
    }

    [Fact]
    public void ACountThatDiffersRefusesNamingBothNumbers()
    {
        using var served = new Served();
        served.Header->SlotCount = ModelInfoFwd.SlotCount - 1;

        Assert.False(Forwarders.TryBind(served.Header, out ModelInfoFwd* table, out string why));
        Assert.True(table == null);
        Assert.Contains($"{ModelInfoFwd.SlotCount - 1} forwarder(s)", why);
        Assert.Contains($"declares {ModelInfoFwd.SlotCount}", why);
    }

    [Fact]
    public void AHashThatDiffersRefusesNamingTheSlot()
    {
        using var served = new Served();
        int slot = Enumerable.Range(0, ModelInfoFwd.SlotCount)
            .First(i => ModelInfoFwd.SlotName(i) == "GetVCollide_2");
        served.Hashes[slot] ^= 1;

        Assert.False(Forwarders.TryBind(served.Header, out ModelInfoFwd* table, out string why));
        Assert.True(table == null);
        Assert.Contains($"slot {slot} (GetVCollide_2)", why);
    }

    [Fact]
    public void TheLastSlotsHashIsCheckedToo()
    {
        // The loop runs to the end: a stale last slot is a refusal, not a pass.
        using var served = new Served();
        int last = ModelInfoFwd.SlotCount - 1;
        served.Hashes[last] ^= 0x80000000;

        Assert.False(Forwarders.TryBind(served.Header, out ModelInfoFwd* _, out string why));
        Assert.Contains($"slot {last} ({ModelInfoFwd.SlotName(last)})", why);
    }

    [Fact]
    public void AMissingTableRefuses()
    {
        Assert.False(Forwarders.TryBind((ForwarderTableHeader*)null, out ModelInfoFwd* table, out string why));
        Assert.True(table == null);
        Assert.Contains(ModelInfoFwd.TableName, why);
    }

    [Fact]
    public void TheTableIsAskedForByNameFromTheInternalFactory()
    {
        using var served = new Served();
        var factory = new NativeInterfaceFactory(served.Acquire);

        Assert.True(Forwarders.TryAcquire(factory, out ModelInfoFwd* table, out string why), why);
        Assert.Equal((nint)served.Slots, (nint)table);
    }

    [Fact]
    public void AnUnservedTableRefusesWithTheFactorysReason()
    {
        var factory = new NativeInterfaceFactory(
            (type, version, entries, error, errorBytes) => null);

        Assert.False(Forwarders.TryAcquire(factory, out ModelInfoFwd* table, out string why));
        Assert.True(table == null);
        Assert.Contains(ModelInfoFwd.TableName, why);
    }

    [Fact]
    public void TheGeneratedStructIsOneFieldPerSlot()
    {
        // Field N is slot N because the struct is pointer-sized fields and
        // nothing else; a field the count did not include would misalign
        // every call after it.
        Assert.Equal(ModelInfoFwd.SlotCount, sizeof(ModelInfoFwd) / sizeof(void*));
        Assert.Equal(ModelInfoFwd.SlotCount, ModelInfoFwd.SlotHashes.Length);
        var names = Enumerable.Range(0, ModelInfoFwd.SlotCount).Select(ModelInfoFwd.SlotName).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal("GetModel", names[0]);
    }

    [Fact]
    public void FieldOrderIsTheSlotOrder()
    {
        // The field named by SlotName(i) sits at byte 8i.
        for (int i = 0; i < ModelInfoFwd.SlotCount; i++)
        {
            nint offset = Marshal.OffsetOf<ModelInfoFwd>(ModelInfoFwd.SlotName(i));
            Assert.Equal((nint)(i * sizeof(void*)), offset);
        }
    }

    [Fact]
    public void AnEngineInterfaceBindsItsObjectAndItsForwarders()
    {
        using var served = new Served();
        var factory = new NativeInterfaceFactory(served.Acquire);

        Assert.True(Forwarders.TryAcquire(
            factory, SourceSharpFactoryType.AppSystem, EngineModelInfo.ServerInterfaceVersion,
            out void* self, out ModelInfoFwd* table, out string why), why);
        Assert.Equal(Served.EngineObject, (nint)self);
        Assert.Equal((nint)served.Slots, (nint)table);
    }

    [Fact]
    public void AnUnservedEngineObjectRefusesByItsOwnName()
    {
        using var served = new Served { ServesObject = false };
        var factory = new NativeInterfaceFactory(served.Acquire);

        Assert.False(Forwarders.TryAcquire(
            factory, SourceSharpFactoryType.AppSystem, EngineModelInfo.ServerInterfaceVersion,
            out void* self, out ModelInfoFwd* table, out string why));
        Assert.True(self == null && table == null);
        Assert.Contains(EngineModelInfo.ServerInterfaceVersion, why);
    }

    [Fact]
    public void AnUnservedForwarderTableRefusesEvenWithTheObjectInHand()
    {
        using var served = new Served { ServesTable = false };
        var factory = new NativeInterfaceFactory(served.Acquire);

        Assert.False(Forwarders.TryAcquire(
            factory, SourceSharpFactoryType.AppSystem, EngineModelInfo.ServerInterfaceVersion,
            out void* self, out ModelInfoFwd* table, out string why));
        Assert.True(self == null && table == null);
        Assert.Contains(ModelInfoFwd.TableName, why);
    }
}
