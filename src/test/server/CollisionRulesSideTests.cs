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
public unsafe class CollisionRulesSideTests : IDisposable
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
    public void AServerSideStillDeliversTheMatrixToItsShimRoot()
    {
        // The retention half: the server's route must survive the move.
        uint* recorder = Serve(ServerCollisionVersion, ServerCollisionFwd);
        var engine = Attached(Side.Server);

        uint[] rows = CollisionRules.BuildMatrix();
        bool delivered = SideBindings.Of<IEngineCollisionRules>(engine).RegisterCollisionRules(rows);

        Assert.True(delivered, "the server side reports the matrix delivered");
        Assert.True(recorder[0] == 1, $"the server's rules slot was called once ({recorder[0]} calls)");
        Assert.True(new ReadOnlySpan<uint>(recorder + 1, CollisionRulesBlob.RowCount).SequenceEqual(rows),
            "the server's native side received the managed matrix row for row");
    }

    [Fact]
    public void ASideBeforeItsAttachRefusesTheMatrixLoudly()
    {
        // The silence this replaced: an unattached side used to return as if
        // it had registered. Nothing checks for the attach now (plan_shim_areas_split
        // phase 11 review); a side as its load makes it, before the attach,
        // refuses the call, naming the area, rather than answering anything.
        var engine = EngineSide.Create(Side.Client, new SideState(Side.Client));
        engine.Initialize();
        // Its engine interfaces answer the test defaults, so the composition
        // reaches the area: the subject is the area's refusal.
        DefaultForwarders.HoldOn(engine);

        var refusal = Assert.Throws<InvalidOperationException>(
            () => SideBindings.Of<IEngineCollisionRules>(engine).RegisterCollisionRules(CollisionRules.BuildMatrix()));
        Assert.Contains("before the shim areas were attached", refusal.Message);
    }

    [Fact]
    public void TheRowHashMovesWhenOneBitOfTheMatrixDoes()
    {
        uint[] rows = CollisionRules.BuildMatrix();
        uint[] flipped = (uint[])rows.Clone();
        flipped[CollisionRulesBlob.RowCount - 1] ^= 1u << 7;

        Assert.Equal(CollisionRules.RowHash(rows), CollisionRules.RowHash((uint[])rows.Clone()));
        Assert.NotEqual(CollisionRules.RowHash(rows), CollisionRules.RowHash(flipped));
    }

    //-------------------------------------------------------------------------

    /// <summary>The area the server binds the rules slot through: the one its registration acquires it from.</summary>
    private static string ServerCollisionVersion
        => "SourceSharp" + ServerCollisionArea.Name[..^"Area".Length] + "001";

    private static Type ServerCollisionFwd
        => ShimAreaTypes.FwdOf(ServerCollisionArea);

    // The server's own area whose table carries the rules slot: the one it
    // registers under its own type, not a base it is registered as too.
    private static Type ServerCollisionArea
    {
        get
        {
            var areas = new List<(Type Type, object Area)>();
            ServerAreaRegistration.AddDetached((type, area) => areas.Add((type, area)));
            // Under its own interface (IXArea for XArea), not a base's.
            return areas.Where(a => a.Type.Name == "I" + a.Area.GetType().Name)
                        .Select(a => a.Area.GetType())
                        .Single(t => ShimAreaTypes.FwdOf(t).GetField(nameof(ShimCollisionFwd.RegisterCollisionRules)) is not null);
        }
    }

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
        var api = TestSides.Create(side);
        api.Initialize();

        string why;
        bool attached = api.AttachOwnShims(_root.Bind(), out why);

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
