using System.Runtime.InteropServices;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The collision matrix reaches native from BOTH sides. ShouldCollide is asked
/// on the client too (util_shared.cpp's trace filter runs during prediction),
/// so ClientModule registers the same matrix the server does.
///
/// <para>
/// It used not to arrive. RegisterCollisionRules lived in the server-only
/// IShimCollision area, so on the client <c>Has_RegisterCollisionRules</c> was
/// false and the call returned without a word; the client's native table kept
/// its stock defaults. SideAuditTests' transitive walk found it (lane
/// side-audit, 2026-10-02).
/// </para>
///
/// <para>
/// Each fact serves its own area object through a <see cref="FakeShimRoot"/>:
/// word 0 is the vtable, word 1 points at this test's recorder, so the
/// callback records into the instance that set it up and nothing is static.
/// </para>
/// </summary>
public unsafe class CollisionRulesClientSideTests : IDisposable
{
    /// <summary>Recorder: [0] calls, [1..20] the rows of the last call.</summary>
    private const int RecorderWords = 1 + CollisionRulesBlob.RowCount;

    private readonly FakeShimRoot _root = new();
    private readonly List<nint> _blocks = new();

    public void Dispose()
    {
        _root.Dispose();
        foreach (nint block in _blocks)
            NativeMemory.Free((void*)block);
    }

    [Fact]
    public void AClientSideDeliversTheMatrixToItsShimRoot()
    {
        uint* recorder = Serve("SourceSharpCollision001", typeof(ShimCollisionFwd));
        var engine = Attached(Side.Client);

        uint[] rows = CollisionRules.BuildMatrix();
        bool delivered = engine.RegisterCollisionRules(rows);

        Assert.True(delivered, "the client side reports the matrix delivered");
        Assert.True(recorder[0] == 1, $"the client's rules slot was called once ({recorder[0]} calls)");
        Assert.True(new ReadOnlySpan<uint>(recorder + 1, CollisionRulesBlob.RowCount).SequenceEqual(rows),
            "the client's native side received the managed matrix row for row");
    }

    //-------------------------------------------------------------------------

    /// <summary>The area the server binds the rules slot through: the one ShimAreas acquires it from.</summary>
    private static string ServerCollisionVersion
        => "SourceSharp" + ServerCollisionField.Name + "001";

    private static Type ServerCollisionFwd
        => ShimAreaTypes.FwdOf(ServerCollisionField.FieldType);

    private static System.Reflection.FieldInfo ServerCollisionField
        => typeof(ShimAreas).GetFields().Single(f =>
               ShimAreaTypes.FwdOf(f.FieldType).GetField(nameof(ShimCollisionFwd.RegisterCollisionRules)) is not null);

    /// <summary>
    /// Serves an area object for <paramref name="version"/> whose rules slot
    /// records into a fresh recorder, which it returns.
    /// </summary>
    private uint* Serve(string version, Type vtbl)
    {
        int words = Marshal.SizeOf(vtbl) / sizeof(nint);
        var table = (nint*)Alloc(words * sizeof(nint));
        var recorder = (uint*)Alloc(RecorderWords * sizeof(uint));
        var self = (nint*)Alloc(2 * sizeof(nint));

        int slot = (int)Marshal.OffsetOf(vtbl, nameof(ShimCollisionFwd.RegisterCollisionRules)) / sizeof(nint);
        table[slot] = (nint)(delegate* unmanaged<void*, CollisionRulesBlob*, void>)&Record;
        self[1] = (nint)recorder;

        _root.Area(version, self, table);
        return recorder;
    }

    private EngineSide Attached(Side side)
    {
        var api = new EngineSide(side);
        api.Initialize();

        string why;
        bool attached = side == Side.Server
            ? api.AttachShims(_root.Bind(), out why)
            : api.AttachClientShims(_root.Bind(), out why);

        Assert.True(attached, why);
        return api;
    }

    private void* Alloc(int bytes)
    {
        void* p = NativeMemory.AllocZeroed((nuint)bytes);
        _blocks.Add((nint)p);
        return p;
    }

    [UnmanagedCallersOnly]
    private static void Record(void* self, CollisionRulesBlob* blob)
    {
        var recorder = (uint*)((nint*)self)[1];
        recorder[0]++;
        for (int i = 0; i < CollisionRulesBlob.RowCount; i++)
            recorder[1 + i] = blob->Rows[i];
    }
}
