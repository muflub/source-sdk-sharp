using System;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <c>default(EntityHandle)</c> IS THE INVALID HANDLE (user decision
/// 2026-10-05). The struct stores the raw packed handle inverted, so its
/// all-zero bits read back as <c>0xFFFFFFFF</c>, and entry 0 (the world) has to
/// be named on purpose.
///
/// <para>
/// The struct's bits are therefore NOT the raw handle, and every place the
/// managed side handed native a pinned run of handles has to convert. The
/// three such paths (<see cref="EngineSide.DestroyShells"/>,
/// <see cref="EngineSide.EntitiesInSphere"/>,
/// <see cref="EngineSide.EntityOverlapReport"/>) are driven here through a
/// <see cref="FakeShimRoot"/> whose slots record or write raw uint32 handles,
/// as native does.
/// </para>
/// </summary>
public unsafe class EntityHandleInversionTests : IDisposable
{
    private readonly FakeShimRoot _root = new();

    public void Dispose() => _root.Dispose();

    [Fact]
    public void TheDefaultHandleIsTheInvalidHandle()
        => Assert.Equal(EntityHandle.None, default(EntityHandle));

    [Fact]
    public void TheDefaultHandleReadsBackAsTheInvalidRawValue()
        => Assert.Equal(0xFFFF_FFFFu, default(EntityHandle).Value);

    [Fact]
    public void ARawZeroIsNotTheInvalidHandle()
        => Assert.NotEqual(EntityHandle.None, new EntityHandle(0u));

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(0x0042_0007u)]
    [InlineData(0x7FFF_1FFFu)]
    [InlineData(0xFFFF_FFFEu)]
    [InlineData(0xFFFF_FFFFu)]
    public void ARawValueRoundTrips(uint raw)
        => Assert.Equal(raw, new EntityHandle(raw).Value);

    [Fact]
    public void TheEntryIsDecodedFromTheRawValue()
        => Assert.Equal(7, new EntityHandle(0x0042_0007u).EntryIndex);

    [Fact]
    public void EntryZeroIsDecodedFromARawZero()
        => Assert.Equal(0, new EntityHandle(0u).EntryIndex);

    [Fact]
    public void TheInvalidHandlesEntryIsTheLastEntry()
        => Assert.Equal(EntityHandle.EntryCount - 1, default(EntityHandle).EntryIndex);

    [Fact]
    public void AHandleInTheInvisibleRangeIsDecodedFromTheRawValue()
        => Assert.True(new EntityHandle(0x0001_0800u).IsInvisibleRange);

    [Fact]
    public void TheDefaultHandlePrintsAsInvalid()
        => Assert.Equal("(invalid handle)", default(EntityHandle).ToString());

    [Fact]
    public void TheWorldsRawHandlePrintsAsEntryZero()
        => Assert.Equal("#0", new EntityHandle(0u).ToString());

    //-------------------------------------------------------------------------
    // The pinned runs native reads and writes as uint32.
    //-------------------------------------------------------------------------

    private static readonly uint[] Raw = { 0u, 0x0042_0007u, 0x7FFF_1FFFu, 0xFFFF_FFFFu };

    [ThreadStatic] private static uint[]? t_seen;

    [UnmanagedCallersOnly]
    private static void RecordShells(void* self, uint* handles, int count)
        => t_seen = new ReadOnlySpan<uint>(handles, count).ToArray();

    [UnmanagedCallersOnly]
    private static int WriteSphere(void* self, float* center, float radius, uint* into, int max)
    {
        int n = Math.Min(max, Raw.Length);
        for (int i = 0; i < n; i++)
            into[i] = Raw[i];
        return n;
    }

    [UnmanagedCallersOnly]
    private static int WriteOverlaps(void* self, uint* into, int maxPairs)
    {
        int n = Math.Min(maxPairs * 2, Raw.Length);
        for (int i = 0; i < n; i++)
            into[i] = Raw[i];
        return n / 2;
    }

    [UnmanagedCallersOnly]
    private static int RecordTraceIgnore(void* self, float* start, float* end, int mask, uint ignore,
                                         float* fraction, float* endPos, float* normal, uint* hit)
    {
        t_seen = new[] { ignore };
        *fraction = 1f;
        *hit = 0xFFFF_FFFFu;
        return 0;
    }

    private EngineSide Attached(string area, string slot, nint fn)
    {
        var side = new EngineSide(Side.Server);
        side.Initialize();
        _root.Live(area, slot, fn);
        Assert.True(side.AttachShims(_root.Bind(), out string why), why);
        return side;
    }

    private static EntityHandle[] Handles(uint[] raw)
        => Array.ConvertAll(raw, r => new EntityHandle(r));

    [Fact]
    public void DestroyShellsHandsNativeTheRawHandles()
    {
        t_seen = null;
        var side = Attached("SourceSharpEntityLifecycle001", nameof(ShimEntityLifecycleFwd.DestroyShell),
                            (nint)(delegate* unmanaged<void*, uint*, int, void>)&RecordShells);

        side.DestroyShells(Handles(Raw));

        Assert.Equal(Raw, t_seen);
    }

    [Fact]
    public void DestroyShellsHandsNativeTheRawHandlesOfALargeBatch()
    {
        t_seen = null;
        var side = Attached("SourceSharpEntityLifecycle001", nameof(ShimEntityLifecycleFwd.DestroyShell),
                            (nint)(delegate* unmanaged<void*, uint*, int, void>)&RecordShells);
        uint[] raw = new uint[3000];
        for (int i = 0; i < raw.Length; i++)
            raw[i] = (uint)(i * 0x0001_0001);

        side.DestroyShells(Handles(raw));

        Assert.Equal(raw, t_seen);
    }

    [Fact]
    public void EntitiesInSphereReadsTheRawHandlesNativeWrote()
    {
        var side = Attached("SourceSharpEntitiesServer001", nameof(ShimEntitiesServerFwd.EntitiesInSphere),
                            (nint)(delegate* unmanaged<void*, float*, float, uint*, int, int>)&WriteSphere);
        var into = new EntityHandle[8];

        int n = side.EntitiesInSphere(Vector.Zero, 64f, into);

        Assert.Equal(Raw, Array.ConvertAll(into[..n], h => h.Value));
    }

    [Fact]
    public void EntitiesInSphereLeavesTheUnwrittenTailInvalid()
    {
        var side = Attached("SourceSharpEntitiesServer001", nameof(ShimEntitiesServerFwd.EntitiesInSphere),
                            (nint)(delegate* unmanaged<void*, float*, float, uint*, int, int>)&WriteSphere);
        var into = new EntityHandle[8];

        int n = side.EntitiesInSphere(Vector.Zero, 64f, into);

        Assert.All(into[n..], h => Assert.Equal(EntityHandle.None, h));
    }

    [Fact]
    public void TheOverlapReportReadsTheRawHandlesNativeWrote()
    {
        var side = Attached("SourceSharpCollisionServer001", nameof(ShimCollisionServerFwd.EntityOverlapReport),
                            (nint)(delegate* unmanaged<void*, uint*, int, int>)&WriteOverlaps);
        var into = new OverlapPair[4];

        int n = side.EntityOverlapReport(into);

        Assert.Equal(Raw, new[] { into[0].A.Value, into[0].B.Value, into[1].A.Value, into[1].B.Value }[..(2 * n)]);
    }

    [Fact]
    public void AnOmittedTraceIgnoreReachesNativeAsTheInvalidHandle()
    {
        t_seen = null;
        var side = Attached("SourceSharpWorldServer001", nameof(ShimWorldServerFwd.TraceLine),
                            (nint)(delegate* unmanaged<void*, float*, float*, int, uint, float*, float*, float*, uint*, int>)&RecordTraceIgnore);

        side.TraceLine(Vector.Zero, new Vector(0f, 0f, 64f), TraceMask.Solid);

        Assert.Equal(new[] { 0xFFFF_FFFFu }, t_seen);
    }
}
