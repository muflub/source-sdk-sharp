using SourceSharp.Abi;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_native_v2 T4f: what the side's trace members answer, read off
/// the engine's trace (shell_worldquery.cpp's rules for the outs it wrote), and
/// the water query over the tracer (its SourceSharp_WaterSurfaceBelow).
/// </summary>
public class ServerTraceResultsTests
{
    private static readonly EntityHandle Crate = new(40u | (3u << 12));

    private static ServerTrace Trace(float fraction = 1f, byte startSolid = 0, byte allSolid = 0,
                                     short props = 0, ushort flags = 0, int hitGroup = 0, int contents = 0)
        => new(new EngineGameTrace
        {
            Fraction = fraction, StartSolid = startSolid, AllSolid = allSolid,
            EndPos = new Vector(1, 2, 3), PlaneNormal = new Vector(0, 0, 1),
            SurfaceProps = props, SurfaceFlags = flags, HitGroup = hitGroup, Contents = contents,
        }, Crate);

    [Fact]
    public void ALineThatStoppedShortHit()
        => Assert.True(ServerTraceResults.Line(Trace(fraction: 0.5f)).Hit);

    [Fact]
    public void ALineThatStartedSolidHitEvenAtFullLength()
        => Assert.True(ServerTraceResults.Line(Trace(fraction: 1f, startSolid: 1)).Hit);

    [Fact]
    public void ALineThatRanItsLengthDidNotHit()
        => Assert.False(ServerTraceResults.Line(Trace(fraction: 1f)).Hit);

    [Fact]
    public void ALineCarriesTheEndTheNormalAndTheEntity()
    {
        TraceResult r = ServerTraceResults.Line(Trace(fraction: 0.25f));
        Assert.Equal(0.25f, r.Fraction);
        Assert.Equal(new Vector(1, 2, 3), r.End);
        Assert.Equal(new Vector(0, 0, 1), r.Normal);
        Assert.Equal(Crate, r.Entity);
    }

    [Fact]
    public void TheSurfaceOutsReachTheirOwnMembers()
    {
        SurfaceTraceResult r = ServerTraceResults.Surface(Trace(fraction: 0.5f, props: 3, flags: 5, hitGroup: 7));
        Assert.Equal(3, r.SurfaceProps);
        Assert.Equal(5, r.SurfaceFlags);
        Assert.Equal((HitGroup)7, r.HitGroup);
    }

    [Fact]
    public void ALineThatHitNothingNamesNoSurface()
    {
        SurfaceTraceResult r = ServerTraceResults.Surface(Trace(fraction: 1f, props: 3, flags: 5, hitGroup: 7));
        Assert.Equal(-1, r.SurfaceProps);
        Assert.Equal(0, r.SurfaceFlags);
        Assert.Equal(HitGroup.Generic, r.HitGroup);
    }

    [Fact]
    public void AHullSaysStartSolidApartFromBlocked()
    {
        HullResult inside = ServerTraceResults.Hull(Trace(fraction: 1f, startSolid: 1));
        HullResult struck = ServerTraceResults.Hull(Trace(fraction: 0.5f));
        Assert.True(inside.Blocked && inside.StartSolid);
        Assert.True(struck.Blocked && !struck.StartSolid);
        Assert.Equal(Crate, struck.Entity);
    }

    [Fact]
    public void AnEntitySweepCarriesTheEndAndBothSolids()
    {
        EntityHullTrace r = ServerTraceResults.EntityHull(Trace(fraction: 0.5f, startSolid: 1, allSolid: 0));
        Assert.Equal(new Vector(1, 2, 3), r.EndPos);
        Assert.True(r.StartSolid);
        Assert.False(r.AllSolid);
        Assert.True(ServerTraceResults.EntityHull(Trace(allSolid: 1)).AllSolid);
    }

    // ---- WaterQuery ---------------------------------------------------------

    private sealed class Route(int contentsHere, ServerTrace below) : IServerTraceRoute
    {
        public uint Mask;
        public Vector End;
        public int Traces;

        public ServerTrace Line(Vector start, Vector end, uint mask, EntityHandle pass, int collisionGroup)
        {
            Traces++;
            Mask = mask;
            End = end;
            return below;
        }

        public ServerTrace Hull(Vector start, Vector end, Vector mins, Vector maxs, uint mask,
                                EntityHandle pass, int collisionGroup) => throw new NotSupportedException();

        public int PointContents(Vector point) => contentsHere;

        public nint Enter(EntityHandle pass, int collisionGroup) => throw new NotSupportedException();

        public void Exit() => throw new NotSupportedException();
    }

    [Fact]
    public void APointInWaterIsSubmergedAndTracesNothing()
    {
        var route = new Route(0x20, Trace());
        WaterBelow w = WaterQuery.Below(route, new Vector(0, 0, 10));
        Assert.True(w.Submerged);
        Assert.False(w.Found);
        Assert.Equal(0, route.Traces);
    }

    [Fact]
    public void TheTraceLooksStraightDownForWaterOrBrush()
    {
        var route = new Route(0, Trace());
        WaterQuery.Below(route, new Vector(5, 6, 100));
        Assert.Equal(new Vector(5, 6, 100 - 4096), route.End);
        Assert.Equal(0x4030u | 0x400Bu, route.Mask);   // MASK_WATER | MASK_SOLID_BRUSHONLY
    }

    [Fact]
    public void NothingBelowIsNoWater()
    {
        WaterBelow w = WaterQuery.Below(new Route(0, Trace(fraction: 1f)), new Vector(0, 0, 10));
        Assert.False(w.Found || w.Submerged);
        Assert.Equal(new Vector(0, 0, 10), w.Surface);
    }

    [Fact]
    public void TheFloorBelowIsNotWater()
    {
        WaterBelow w = WaterQuery.Below(new Route(0, Trace(fraction: 0.5f, contents: 0x1)), new Vector(0, 0, 10));
        Assert.False(w.Found);
        Assert.Equal(new Vector(0, 0, 10), w.Surface);
    }

    [Fact]
    public void WaterBelowIsFoundAtWhereTheTraceStopped()
    {
        WaterBelow w = WaterQuery.Below(new Route(0, Trace(fraction: 0.5f, contents: 0x20)), new Vector(0, 0, 10));
        Assert.True(w.Found);
        Assert.False(w.Slime);
        Assert.Equal(new Vector(1, 2, 3), w.Surface);
    }

    [Fact]
    public void SlimeBelowIsFoundAndSaysSo()
    {
        WaterBelow w = WaterQuery.Below(new Route(0, Trace(fraction: 0.5f, contents: 0x10)), new Vector(0, 0, 10));
        Assert.True(w.Found);
        Assert.True(w.Slime);
    }
}
