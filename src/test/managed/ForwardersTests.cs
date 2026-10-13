using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

//=============================================================================
// plan_engine_forwarders.md: a forwarder table binds when native serves at
// least the slots the generated struct reads. Slots only append (the
// generator refuses anything else), so a longer table is a newer library and
// binds; a shorter one is an older library and refuses.
//
// The tables here are built in managed memory in SSForwarderTable's shape
// (src/fwd/native/ss_fwd.h): a slot array and a count. Nothing is called
// through them; the facts are about what binds and what refuses.
//=============================================================================

public sealed unsafe class ForwardersTests
{
    private static readonly int Count = Forwarders.SlotCount<ModelInfoFwd>();

    // A table in native's shape, ModelInfoFwd's slots plus `extra` more at
    // the end, then edited by the fact under test.
    private sealed class Served : IDisposable
    {
        public readonly ForwarderTableHeader* Header;
        public readonly void** Slots;

        public Served(int extra = 0)
        {
            int count = Count + extra;
            Slots = (void**)NativeMemory.AllocZeroed((nuint)count, (nuint)sizeof(void*));
            Header = (ForwarderTableHeader*)NativeMemory.AllocZeroed(1, (nuint)sizeof(ForwarderTableHeader));

            for (int i = 0; i < count; i++)
                Slots[i] = (void*)(0x1000 + i);

            Header->Slots = Slots;
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
    public void ALongerTableBindsAndTheStructReadsItsPrefix()
    {
        // A newer library: its extra slots are at the end, past every field.
        using var served = new Served(extra: 3);

        Assert.True(Forwarders.TryBind(served.Header, out ModelInfoFwd* table, out string why), why);
        Assert.Equal((nint)served.Slots, (nint)table);
        Assert.Equal((nint)(0x1000 + Count - 1), (nint)table->GetCollideForVirtualTerrain);
    }

    [Fact]
    public void AShorterTableRefusesNamingBothNumbers()
    {
        // An older library: the struct's last field would read past its table.
        using var served = new Served();
        served.Header->SlotCount = Count - 1;

        Assert.False(Forwarders.TryBind(served.Header, out ModelInfoFwd* table, out string why));
        Assert.True(table == null);
        Assert.Contains($"{Count - 1} forwarder(s)", why);
        Assert.Contains($"reads {Count}", why);
    }

    [Fact]
    public void ATableWithNoSlotArrayRefuses()
    {
        using var served = new Served();
        served.Header->Slots = null;

        Assert.False(Forwarders.TryBind(served.Header, out ModelInfoFwd* table, out string why));
        Assert.True(table == null);
        Assert.Contains(ModelInfoFwd.TableName, why);
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
    public void TheSlotCountIsOnePointerFieldPerForwarder()
    {
        // The count is the struct's size, so every instance field must be a
        // pointer: anything else would make the count wrong and misalign
        // every call after it.
        FieldInfo[] fields = typeof(ModelInfoFwd).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Equal(12, Count);
        Assert.Equal(Count, fields.Length);
        Assert.All(fields, f => Assert.True(f.FieldType.IsFunctionPointer, f.Name));
        Assert.Equal("GetModel", fields[0].Name);
    }

    [Fact]
    public void FieldOrderIsTheSlotOrder()
    {
        // Field N, in declaration order, sits at byte 8N.
        FieldInfo[] fields = typeof(ModelInfoFwd).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderBy(f => f.MetadataToken).ToArray();
        for (int i = 0; i < fields.Length; i++)
            Assert.Equal((nint)(i * sizeof(void*)), Marshal.OffsetOf<ModelInfoFwd>(fields[i].Name));
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
