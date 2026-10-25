using System.Runtime.InteropServices;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_native_v2 T4f: the server's movement loop traces its hull through
/// the managed trace filter. The side names the query --
/// <c>CTraceFilterSimple( player, group )</c>, as shell_movement.cpp's
/// UTIL_TraceRay asked -- and hands the native filter to the slot that carries
/// the trace out: SourceSharp_MoveTraceServer since T4g (MoveTraceThrough's
/// slot is retired), which also answers the hit entity and the ground as
/// handles for the managed command loop. The recorders are static, as
/// unmanaged callbacks must be, and only this class's facts touch them.
/// </summary>
public sealed unsafe class MoveTraceServerTests : IDisposable
{
    private const string MovementServer = "SourceSharpMovementServer001";

    private static readonly EntityHandle Player = new(3u | (7u << 12));

    private static int s_calls;
    private static nint s_filter;
    private static int s_mask;
    private static int s_group;

    [UnmanagedCallersOnly]
    private static void Through(void* self, float* s, float* e, float* lo, float* hi, int mask, int group,
                                MoveTraceBlob* blob, void* filter, uint* handles)
    {
        s_calls++;
        s_filter = (nint)filter;
        s_mask = mask;
        s_group = group;
        blob->Fraction = 0.5f;
        handles[0] = HitRaw;
        handles[1] = GroundRaw;
    }

    private const uint HitRaw = 5u | (9u << 12);
    private const uint GroundRaw = 6u | (2u << 12);

    private sealed class Recorder : IServerTraceRoute
    {
        public readonly List<(EntityHandle Pass, int Group)> Entered = [];
        public int Exits;

        public ServerTrace Line(Vector start, Vector end, uint mask, EntityHandle pass, int collisionGroup) => default;
        public ServerTrace Hull(Vector start, Vector end, Vector mins, Vector maxs, uint mask, EntityHandle pass, int collisionGroup) => default;
        public int PointContents(Vector point) => 0;

        public nint Enter(EntityHandle pass, int collisionGroup)
        {
            Entered.Add((pass, collisionGroup));
            return 0xF11;
        }

        public void Exit() => Exits++;
    }

    private readonly FakeShimRoot _root = new();
    private readonly EngineSide _side = TestSides.Create(Side.Server);
    private readonly Recorder _traces = new();

    public MoveTraceServerTests()
    {
        s_calls = 0;
        s_filter = 0;
        _root.Live(MovementServer, nameof(ShimMovementServerFwd.MoveTraceServer),
                   (nint)(delegate* unmanaged<void*, float*, float*, float*, float*, int, int, MoveTraceBlob*, void*, uint*, void>)&Through);
        _side.Attachment.Initialize();
        Assert.True(_side.Attachment.AttachShims(_root.Bind(), out string why), why);
        _side.Tracer.Fill(_traces);
    }

    public void Dispose() => _root.Dispose();

    private MoveTraceBlob Trace(uint mask, int group, EntityHandle player)
    {
        float* s = stackalloc float[3] { 1, 2, 3 };
        float* e = stackalloc float[3] { 4, 5, 6 };
        float* lo = stackalloc float[3] { -16, -16, 0 };
        float* hi = stackalloc float[3] { 16, 16, 72 };
        MoveTraceBlob blob = default;
        _side.MoveTrace(_side.Attachment.Areas.Require<IMovementArea>(), s, e, lo, hi, mask, group, player, &blob);
        return blob;
    }

    [Fact]
    public void TheHullGoesThroughTheServersSlotWithTheManagedFilter()
    {
        MoveTraceBlob blob = Trace(0x201400B, 8, Player);

        Assert.Equal(1, s_calls);
        Assert.Equal((nint)0xF11, s_filter);
        Assert.Equal(0x201400B, s_mask);
        Assert.Equal(8, s_group);
        Assert.Equal(0.5f, blob.Fraction);
    }

    [Fact]
    public void TheQueryIsThePlayerAndTheGroupAndItClosesAfterTheTrace()
    {
        Trace(0x201400B, 8, Player);

        Assert.Equal((Player, 8), Assert.Single(_traces.Entered));
        Assert.Equal(1, _traces.Exits);
    }

    [Fact]
    public void TheHitAndTheGroundComeBackAsHandles()
    {
        float* s = stackalloc float[3] { 1, 2, 3 };
        float* e = stackalloc float[3] { 4, 5, 6 };
        float* lo = stackalloc float[3] { -16, -16, 0 };
        float* hi = stackalloc float[3] { 16, 16, 72 };
        MoveTraceBlob blob = default;

        _side.MoveTraceServer(_side.Attachment.Areas.Require<IMovementServerArea>(), s, e, lo, hi, 1, 0, Player, &blob, out EntityHandle hit, out EntityHandle ground);

        Assert.True(hit == new EntityHandle(HitRaw) && ground == new EntityHandle(GroundRaw), $"hit {hit}, ground {ground}");
    }

    [Fact]
    public void WithNoManagedFilterTheHullIsBlockedAtTheStart()
    {
        _side.Tracer.Fill(null);

        MoveTraceBlob blob = Trace(1, 0, Player);

        Assert.Equal(0, s_calls);
        Assert.Equal((1, 1), (blob.StartSolid, blob.AllSolid));
        Assert.Equal((1f, 2f, 3f), (blob.EndPosX, blob.EndPosY, blob.EndPosZ));
    }
}
