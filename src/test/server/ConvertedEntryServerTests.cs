using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

//=============================================================================
// THE IVEngineServer ENTRIES THAT MOVED ONTO AN ENGINE BINDING, the server's
// half of ConvertedEntryTests (src/test/client), and why they are here: the
// wrapper and its forwarder tables are Server.Interop's
// (plan_engine_forwarders.md, one library per side), which the client's tests
// do not reference. The method is that file's: each entry is driven through
// the impl class over a forwarder table built here, the neighbour slots filled
// with different answers.
//=============================================================================
public unsafe class ConvertedEntryServerTests
{
    // A forwarder's slot: its field's offset in the generated struct.
    private static int ForwarderSlotOf<TFwd>(string field) where TFwd : unmanaged
        => (int)(Marshal.OffsetOf<TFwd>(field) / sizeof(void*));

    // A zeroed forwarder table the size of TFwd, for a fact to fill.
    private static void** ForwarderTable<TFwd>() where TFwd : unmanaged, IForwarderTable
        => (void**)NativeMemory.AllocZeroed((nuint)Forwarders.SlotCount<TFwd>(), (nuint)sizeof(void*));

    /// <summary>
    /// An impl class over a table this test built. The constructors are private
    /// and stay private — production reaches them through <c>Acquire</c> and
    /// nothing else — so this is reflection, which is a thing only a test may
    /// do.
    /// </summary>
    private static object NewImpl(Type impl, void* self, void** table, params nint[] moreTables)
    {
        ConstructorInfo ctor = impl
            .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single();
        ParameterInfo[] parameters = ctor.GetParameters();

        // Each pointer boxed as the parameter it is passed for: the object,
        // then its forwarders, then any other forwarder table the impl holds.
        var args = new object?[parameters.Length];
        args[0] = Pointer.Box(self, parameters[0].ParameterType);
        args[1] = Pointer.Box(table, parameters[1].ParameterType);
        for (int i = 0; i < moreTables.Length; i++)
            args[2 + i] = Pointer.Box((void*)moreTables[i], parameters[2 + i].ParameterType);

        return ctor.Invoke(args);
    }

    //-------------------------------------------------------------------------
    // IVEngineServer: the three reads.
    //-------------------------------------------------------------------------

    private static readonly int IsDedicatedServerSlot =
        ForwarderSlotOf<EngineServerFwd>("IsDedicatedServer");
    private static readonly int GetEntityCountSlot = ForwarderSlotOf<EngineServerFwd>("GetEntityCount");
    private static readonly int GetAreaSlot = ForwarderSlotOf<EngineServerFwd>("GetArea");

    private static float s_AreaSawX;

    [UnmanagedCallersOnly]
    private static byte IsDedicatedServerAnswers(void* self) => 1;

    [UnmanagedCallersOnly]
    private static int GetEntityCountAnswers(void* self) => 473;

    [UnmanagedCallersOnly]
    private static int GetAreaAnswers(void* self, void* origin)
    {
        s_AreaSawX = ((float*)origin)[0];
        return 255;
    }

    private static readonly int GetClientSteamIdSlot = ForwarderSlotOf<EngineServerFwd>("GetClientSteamIDByPlayerIndex");
    private static readonly int GetPlayerNetInfoSlot = ForwarderSlotOf<EngineServerFwd>("GetPlayerNetInfo");
    private static readonly int GetAddressSlot = ForwarderSlotOf<NetChannelInfoFwd>("GetAddress");

    // Slot 3 holds a client: its SteamID and a network channel. Every other
    // slot is empty, and the engine answers null for it.
    private const int OccupiedSlot = 3;
    private static readonly ulong* s_SteamId = (ulong*)NativeMemory.AllocZeroed(1, sizeof(ulong));
    private static readonly byte* s_Address = Utf8Z("203.0.113.7:27005");
    private static readonly void** s_Channel = (void**)NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
    private static readonly void** s_ChannelForwarders = ChannelTable();

    private static byte* Utf8Z(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text + "\0");
        var p = (byte*)NativeMemory.Alloc((nuint)bytes.Length);
        bytes.CopyTo(new Span<byte>(p, bytes.Length));
        return p;
    }

    // The INetChannelInfo forwarders: the channel object itself is only an
    // address, handed to them as `self`.
    private static void** ChannelTable()
    {
        var t = ForwarderTable<NetChannelInfoFwd>();
        t[GetAddressSlot] = (delegate* unmanaged<void*, byte*>)&GetAddressAnswers;
        return t;
    }

    [UnmanagedCallersOnly]
    private static byte* GetAddressAnswers(void* self) => self == s_Channel ? s_Address : null;

    [UnmanagedCallersOnly]
    private static void* GetClientSteamIdAnswers(void* self, int entIndex) => entIndex == OccupiedSlot ? s_SteamId : null;

    [UnmanagedCallersOnly]
    private static void* GetPlayerNetInfoAnswers(void* self, int entIndex) => entIndex == OccupiedSlot ? s_Channel : null;

    private static void* FakeEngineServer(out void** table)
    {
        table = ForwarderTable<EngineServerFwd>();

        table[IsDedicatedServerSlot] = (delegate* unmanaged<void*, byte>)&IsDedicatedServerAnswers;
        table[GetEntityCountSlot] = (delegate* unmanaged<void*, int>)&GetEntityCountAnswers;
        table[GetAreaSlot] = (delegate* unmanaged<void*, void*, int>)&GetAreaAnswers;
        table[GetClientSteamIdSlot] = (delegate* unmanaged<void*, int, void*>)&GetClientSteamIdAnswers;
        table[GetPlayerNetInfoSlot] = (delegate* unmanaged<void*, int, void*>)&GetPlayerNetInfoAnswers;

        void** self = (void**)NativeMemory.AllocZeroed(1, (nuint)sizeof(void*));
        *self = table;

        return self;
    }

    private static IVtableEngineServer ServerOver(void* self, void** table)
        => (IVtableEngineServer)NewImpl(typeof(EngineServer), self, table, (nint)s_ChannelForwarders);

    [Fact]
    public void ClientSteamIdReadsTheCSteamIdAsOneWordAndZeroForAnEmptySlot()
    {
        *s_SteamId = 76561197960287930UL;
        void* self = FakeEngineServer(out void** table);
        var server = ServerOver(self, table);

        Assert.Equal(76561197960287930UL, server.ClientSteamId(OccupiedSlot));
        Assert.Equal(0UL, server.ClientSteamId(OccupiedSlot + 1));

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void ClientAddressCallsGetAddressOnTheChannelTheEngineHandsBack()
    {
        void* self = FakeEngineServer(out void** table);
        var server = ServerOver(self, table);

        Assert.Equal("203.0.113.7:27005", server.ClientAddress(OccupiedSlot));
        Assert.Null(server.ClientAddress(OccupiedSlot + 1));

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void IsDedicatedServerReadsTheEnginesBooleanByte()
    {
        void* self = FakeEngineServer(out void** table);

        Assert.True(ServerOver(self, table).IsDedicatedServer());

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void EntityCountReadsTheEnginesEntityCount()
    {
        void* self = FakeEngineServer(out void** table);

        Assert.Equal(473, ServerOver(self, table).EntityCount());

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    [Fact]
    public void GetAreaPassesTheOriginByAddressAndPassesTheEnginesOwn255Through()
    {
        // TWO THINGS AT ONCE, and both were decisions in the shell body.
        //
        // `const Vector &` is a POINTER at the ABI, so the three floats have to
        // reach the callee by address and in order -- the seam records what it
        // saw. And 255 is the engine's own answer, reserved further down the
        // pipe to mean "no sky camera": softening it here would leave the
        // managed side unable to say so.
        s_AreaSawX = 0f;

        void* self = FakeEngineServer(out void** table);

        int area = ServerOver(self, table).GetArea(new Vector(11f, 22f, 33f));

        Assert.Equal(255, area);
        Assert.Equal(11f, s_AreaSawX);

        NativeMemory.Free(table);
        NativeMemory.Free(self);
    }

    //-------------------------------------------------------------------------
    // AND THE SURFACE RULE THAT SURVIVED THE CONVERSION.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheEngineServerKeepsItsReadsAndNoRawWrite()
    {
        // Three READS were exposed on this impl by Phase 3 and no setter was.
        // EngineLightStyle waited on the owner's call to make this class more
        // than a read-only window; plan_server_native_min P2.2 (carried into
        // plan_server_native_v2 T3, ruled 2026-10-18) made it, and LightStyle is
        // one of the purpose-named writes VtableBindingTests now holds. What
        // stays true here: the reads are still offered, and no raw command slot.
        string[] offered = typeof(EngineServer)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .ToArray();

        Assert.DoesNotContain("ServerCommand", offered);
        Assert.Contains("IsDedicatedServer", offered);
        Assert.Contains("EntityCount", offered);
        Assert.Contains("GetArea", offered);
    }
}
