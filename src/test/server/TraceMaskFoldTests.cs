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
/// Recorded through a recording trace route (plan_server_native_v2 T4f: the
/// three slots retired and the side traces through IEngineTrace::TraceRay over
/// the managed filter), installed on a side attached to a fake root.
/// </summary>
public sealed class TraceMaskFoldTests : System.IDisposable
{
    private sealed class Recorder : IServerTraceRoute
    {
        public int Mask = -2;
        public EntityHandle Pass;
        public int Group = -1;

        public ServerTrace Line(Vector start, Vector end, uint mask, EntityHandle pass, int collisionGroup)
            => Record(mask, pass, collisionGroup);

        public ServerTrace Hull(Vector start, Vector end, Vector mins, Vector maxs, uint mask,
                                EntityHandle pass, int collisionGroup)
            => Record(mask, pass, collisionGroup);

        public int PointContents(Vector point) => 0;

        public readonly List<(EntityHandle Pass, int Group)> Entered = [];
        public int Exits;
        public nint Filter = 0xF11;

        public nint Enter(EntityHandle pass, int collisionGroup)
        {
            Entered.Add((pass, collisionGroup));
            return Filter;
        }

        public void Exit() => Exits++;

        private ServerTrace Record(uint mask, EntityHandle pass, int group)
        {
            Mask = (int)mask;
            Pass = pass;
            Group = group;
            return new ServerTrace(new EngineGameTrace { Fraction = 1f }, EntityHandle.None);
        }
    }

    private readonly FakeShimRoot _root = new();
    private readonly EngineSide _side = TestSides.Create(Side.Server);
    private readonly Recorder _traces = new();

    private int s_Mask => _traces.Mask;

    public TraceMaskFoldTests()
    {
        _side.Attachment.Initialize();
        Assert.True(_side.Attachment.AttachShims(_root.Bind(), out string why), why);
        _side.Tracer.Fill(_traces);
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
    [InlineData(TraceMask.ContentsSolid, 0x1)]
    [InlineData(TraceMask.ShotNoWindows, 0x46004001)]
    [InlineData(TraceMask.Opaque, 0x4081)]
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
    [InlineData(TraceMask.ShotHull, 0x600400b)]
    [InlineData(TraceMask.Brushes, 0x200400b)]
    [InlineData(TraceMask.Solid, 0x200400b)]
    [InlineData(TraceMask.All, 0x200400b)]
    public void AHullIsAShotASwingOrSolidAndNothingElse(TraceMask mask, int engine)
    {
        _side.TraceHull(Vector.Zero, new Vector(0, 0, 1), new Vector(-1, -1, -1), new Vector(1, 1, 1), mask);

        Assert.Equal(engine, s_Mask);
    }

    /// <summary>The bludgeon's near-miss hull (CBaseHL2MPBludgeonWeapon::Swing) sweeps MASK_SHOT_HULL.</summary>
    [Fact]
    public void ABoxSweepCarriesTheSameMaskAsAHull()
    {
        _side.TraceBox(Vector.Zero, new Vector(0, 0, 1), new Vector(-16, -16, -16), new Vector(16, 16, 16), TraceMask.ShotHull);

        Assert.Equal(0x600400b, s_Mask);
    }

    [Fact]
    public void ALineTraceSkipsTheCallersEntityWithNoGroup()
    {
        var shooter = new EntityHandle(2u | (9u << 12));
        _side.TraceLine(Vector.Zero, new Vector(0, 0, 1), TraceMask.Shot, shooter);

        Assert.Equal(shooter, _traces.Pass);
        Assert.Equal(0, _traces.Group);   // COLLISION_GROUP_NONE
    }

    [Fact]
    public void AnOmittedIgnoreIsNoPassEntity()
    {
        _side.TraceLine(Vector.Zero, new Vector(0, 0, 1), TraceMask.Solid);

        Assert.Equal(EntityHandle.None, _traces.Pass);
    }

    [Fact]
    public void AnEntitySweepCarriesItsOwnMaskGroupAndEntity()
    {
        var player = new EntityHandle(1u | (4u << 12));
        _side.EntityTraceHull(player, Vector.Zero, new Vector(0, 0, 1), new Vector(-16, -16, 0), new Vector(16, 16, 72),
                              0x1234u, 8);

        Assert.Equal(0x1234, s_Mask);
        Assert.Equal(player, _traces.Pass);
        Assert.Equal(8, _traces.Group);
    }

    [Fact]
    public void WithNoRouteTheSideTracesNothingAndSaysClear()
    {
        _side.Tracer.Fill(null);
        TraceResult tr = _side.TraceLine(Vector.Zero, new Vector(0, 0, 1), TraceMask.Solid);

        Assert.False(tr.Hit);
        Assert.Equal(-2, s_Mask);
    }
}
