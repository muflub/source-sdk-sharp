using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_native_v2 T4d: which engine MASK_* a <see cref="TraceMask"/>
/// means is decided in managed code, and the server's three trace slots carry
/// the engine's own mask. The tables are the ones shell_worldquery.cpp held
/// before the move (SourceSharp_TraceLineInto's seven arms with
/// MASK_SOLID_BRUSHONLY for anything else; SourceSharp_TraceHull's
/// SHOT ? MASK_SHOT : MASK_SOLID), with bspflags.h's values:
///
/// <code>
/// MASK_SOLID_BRUSHONLY 0x400b      MASK_SHOT 0x46004003     MASK_WATER|BRUSHONLY 0x403b
/// MASK_ALL 0xffffffff              MASK_SHOT_HULL 0x600400b MASK_SOLID 0x200400b
/// MASK_BLOCKLOS 0x4041             MASK_OPAQUE_AND_NPCS|CONTENTS_WINDOW 0x2004083
/// </code>
///
/// Recorded through a fake root (the three slots are not
/// [SuppressGCTransition]). The recorder is static, as an unmanaged callback
/// must be, and only this class's facts, which xUnit runs one at a time, touch it.
/// </summary>
public sealed unsafe class TraceMaskFoldTests : System.IDisposable
{
    private const string WorldServer = "SourceSharpWorldServer001";

    private static int s_Mask;

    [UnmanagedCallersOnly]
    private static int Line(void* self, float* s, float* e, int mask, uint ignore,
                            float* fraction, float* endPos, float* normal, uint* hit)
    {
        s_Mask = mask;
        *fraction = 1f;
        *hit = 0;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int Surface(void* self, float* s, float* e, int mask, uint ignore,
                               float* fraction, float* endPos, float* normal, uint* hit,
                               int* props, int* flags, int* group)
    {
        s_Mask = mask;
        *fraction = 1f;
        *hit = 0;
        return 0;
    }

    [UnmanagedCallersOnly]
    private static int Hull(void* self, float* s, float* e, float* mins, float* maxs, int mask,
                            uint ignore, uint* hit, int* startSolid)
    {
        s_Mask = mask;
        *hit = 0;
        *startSolid = 0;
        return 0;
    }

    private readonly FakeShimRoot _root = new();
    private readonly EngineSide _side = new(Side.Server);

    public TraceMaskFoldTests()
    {
        s_Mask = -2;
        _root.Live(WorldServer, "TraceLine",
                   (nint)(delegate* unmanaged<void*, float*, float*, int, uint, float*, float*, float*, uint*, int>)&Line)
             .Live(WorldServer, "TraceLineSurface",
                   (nint)(delegate* unmanaged<void*, float*, float*, int, uint, float*, float*, float*, uint*, int*, int*, int*, int>)&Surface)
             .Live(WorldServer, "TraceHull",
                   (nint)(delegate* unmanaged<void*, float*, float*, float*, float*, int, uint, uint*, int*, int>)&Hull);
        _side.Initialize();
        Assert.True(_side.AttachShims(_root.Bind(), out string why), why);
    }

    public void Dispose() => _root.Dispose();

    [Theory]
    [InlineData(TraceMask.Brushes, 0x400b)]
    [InlineData(TraceMask.Shot, 0x46004003)]
    [InlineData(TraceMask.BrushesAndWater, 0x403b)]
    [InlineData(TraceMask.All, unchecked((int)0xffffffff))]
    [InlineData(TraceMask.ShotHull, 0x600400b)]
    [InlineData(TraceMask.Solid, 0x200400b)]
    [InlineData(TraceMask.BlockLos, 0x4041)]
    [InlineData(TraceMask.OpaqueAndNpcsWindow, 0x2004083)]
    [InlineData(TraceMask.GlowSight, 0x6004081)]
    [InlineData((TraceMask)99, 0x400b)]
    public void ALineTraceCarriesTheEnginesOwnMask(TraceMask mask, int engine)
    {
        _side.TraceLine(Vector.Zero, new Vector(0, 0, 1), mask);

        Assert.Equal(engine, s_Mask);
    }

    [Fact]
    public void ASurfaceTraceCarriesTheSameMaskAsALine()
    {
        _side.TraceLineSurface(Vector.Zero, new Vector(0, 0, 1), TraceMask.ShotHull);

        Assert.Equal(0x600400b, s_Mask);
    }

    [Theory]
    [InlineData(TraceMask.Shot, 0x46004003)]
    [InlineData(TraceMask.Brushes, 0x200400b)]
    [InlineData(TraceMask.Solid, 0x200400b)]
    [InlineData(TraceMask.All, 0x200400b)]
    public void AHullIsAShotOrSolidAndNothingElse(TraceMask mask, int engine)
    {
        _side.TraceHull(Vector.Zero, new Vector(0, 0, 1), new Vector(-1, -1, -1), new Vector(1, 1, 1), mask);

        Assert.Equal(engine, s_Mask);
    }
}
