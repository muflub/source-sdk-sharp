using SourceSharp.Abi;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_native_v2 T4f: the server's traces through IEngineTrace's
/// TraceRay forwarder. <see cref="EngineRay"/> is stock's <c>Ray_t::Init</c>
/// (public/cmodel.h); <see cref="ServerTracer"/> is <c>UTIL_TraceLine</c> /
/// <c>UTIL_TraceHull</c> over a <c>CTraceFilterSimple</c>, the filter the
/// managed sink with the trace's query in scope.
/// </summary>
public class ServerTracerTests
{
    private static readonly EntityHandle Shooter = new(2u | (9u << 12));
    private static readonly EntityHandle Crate = new(40u | (3u << 12));

    // ---- Ray_t::Init ---------------------------------------------------------

    [Fact]
    public void ALineIsARayFromTheStartAlongTheDelta()
    {
        EngineRay ray = EngineRay.Line(new Vector(1, 2, 3), new Vector(11, 2, 3));

        Assert.Equal(new Vector(1, 2, 3), ray.Start);
        Assert.Equal(new Vector(10, 0, 0), ray.Delta);
        Assert.Equal(Vector.Zero, ray.Extents);
        Assert.Equal(Vector.Zero, ray.StartOffset);
        Assert.Equal(1, ray.IsRay);
        Assert.Equal(1, ray.IsSwept);
    }

    [Fact]
    public void ALineOfNoLengthIsNotSwept()
    {
        EngineRay ray = EngineRay.Line(new Vector(1, 2, 3), new Vector(1, 2, 3));
        Assert.Equal(0, ray.IsSwept);
    }

    [Fact]
    public void AHullStartsAtTheBoxCentreAndOffsetsBack()
    {
        EngineRay ray = EngineRay.Hull(new Vector(0, 0, 0), new Vector(0, 0, 50),
                                       new Vector(-16, -16, 0), new Vector(16, 16, 72));

        Assert.Equal(new Vector(16, 16, 36), ray.Extents);
        Assert.Equal(new Vector(0, 0, 36), ray.Start);
        Assert.Equal(new Vector(0, 0, -36), ray.StartOffset);
        Assert.Equal(new Vector(0, 0, 50), ray.Delta);
        Assert.Equal(0, ray.IsRay);
        Assert.Equal(1, ray.IsSwept);
    }

    [Fact]
    public void AHullWithNoExtentsIsARay()
    {
        EngineRay ray = EngineRay.Hull(Vector.Zero, new Vector(5, 0, 0), new Vector(1, 1, 1), new Vector(1, 1, 1));
        Assert.Equal(1, ray.IsRay);
    }

    [Fact]
    public void ATraceStoppedShortOrStartedSolidHit()
    {
        Assert.True(new EngineGameTrace { Fraction = 0.5f }.DidHit);
        Assert.True(new EngineGameTrace { Fraction = 1f, StartSolid = 1 }.DidHit);
        Assert.True(new EngineGameTrace { Fraction = 1f, AllSolid = 1 }.DidHit);
        Assert.False(new EngineGameTrace { Fraction = 1f }.DidHit);
    }

    // ---- the tracer ---------------------------------------------------------

    private sealed class Candidates : ITraceCandidates
    {
        public readonly Dictionary<EntityHandle, TraceCandidate> Blocks = [];
        public bool TryRead(EntityHandle entity, out TraceCandidate candidate) => Blocks.TryGetValue(entity, out candidate);
        public bool IsWorld(EntityHandle entity) => false;
        public bool IsBrushModel(int modelIndex) => false;
    }

    /// <summary>An engine whose trace asks the filter about planted candidates, as TraceRay does.</summary>
    private sealed class Engine(TraceFilterSink sink) : IVtableEngineTrace
    {
        public readonly List<uint> Candidates = [];
        public readonly List<(EngineRay Ray, uint Mask, nint Filter)> Calls = [];
        public readonly List<int> Answers = [];
        public nint HitEntity = 0x1234;

        public int PointContents(Vector point) => 0x20;

        public EngineGameTrace TraceRay(in EngineRay ray, uint mask, nint filter)
        {
            Calls.Add((ray, mask, filter));
            foreach (uint candidate in Candidates)
                Answers.Add(sink.ShouldHitEntity(candidate, (int)mask));
            return new EngineGameTrace { Fraction = 0.25f, Entity = HitEntity };
        }
    }

    private sealed class Handles : ITraceEntityHandles
    {
        public EntityHandle HandleOf(nint entity) => entity == 0x1234 ? Crate : EntityHandle.None;
    }

    private static (ServerTracer Tracer, Engine Engine, TraceFilterSink Sink) Build(Candidates candidates)
    {
        var sink = new TraceFilterSink(new TraceFilterRules(candidates, CollisionRules.BuildMatrix()), new Crossings());
        var engine = new Engine(sink);
        return (new ServerTracer(engine, sink, new Handles(), 0xF00D), engine, sink);
    }

    [Fact]
    public void ALineIsTracedWithTheNativeFilterAndTheMask()
    {
        var (tracer, engine, _) = Build(new Candidates());

        tracer.Line(new Vector(0, 0, 0), new Vector(100, 0, 0), 0x400B, EntityHandle.None, 0);

        var call = Assert.Single(engine.Calls);
        Assert.Equal((nint)0xF00D, call.Filter);
        Assert.Equal(0x400Bu, call.Mask);
        Assert.Equal(new Vector(100, 0, 0), call.Ray.Delta);
        Assert.Equal(1, call.Ray.IsRay);
    }

    [Fact]
    public void AHullIsTracedAsABox()
    {
        var (tracer, engine, _) = Build(new Candidates());

        tracer.Hull(Vector.Zero, new Vector(0, 0, 10), new Vector(-1, -1, -1), new Vector(1, 1, 1), 1, EntityHandle.None, 0);

        Assert.Equal(new Vector(1, 1, 1), Assert.Single(engine.Calls).Ray.Extents);
    }

    [Fact]
    public void TheCandidatesAreAskedAboutWithTheTracesPassEntity()
    {
        var c = new Candidates();
        c.Blocks[Shooter] = new TraceCandidate(0, SolidType.BoundingBox, 3, EntityHandle.None);
        c.Blocks[Crate] = new TraceCandidate(0, SolidType.BoundingBox, 3, EntityHandle.None);
        var (tracer, engine, _) = Build(c);
        engine.Candidates.AddRange([Shooter.Value, Crate.Value]);

        tracer.Line(Vector.Zero, new Vector(1, 0, 0), 0x2000000, Shooter, 0);

        Assert.Equal([0, 1], engine.Answers);
    }

    [Fact]
    public void EnteringATraceNativeCarriesOutNamesItsQueryAndAnswersTheFilter()
    {
        var c = new Candidates();
        c.Blocks[Shooter] = new TraceCandidate(0, SolidType.BoundingBox, 3, Crate);
        var (tracer, _, sink) = Build(c);

        nint filter = tracer.Enter(Shooter, 8);

        Assert.Equal((nint)0xF00D, filter);
        Assert.Equal(new TraceFilterQuery(Shooter, true, Crate, 8), sink.Current);
    }

    [Fact]
    public void ExitPutsTheOuterQueryBack()
    {
        var c = new Candidates();
        c.Blocks[Shooter] = new TraceCandidate(0, SolidType.BoundingBox, 3, EntityHandle.None);
        var (tracer, _, sink) = Build(c);

        tracer.Enter(Shooter, 8);
        tracer.Enter(Crate, 5);
        tracer.Exit();
        Assert.Equal(Shooter, sink.Current!.Value.Pass);
        tracer.Exit();
        Assert.Null(sink.Current);
    }

    [Fact]
    public void AnExitWithoutAnEnterIsRefused()
    {
        var (tracer, _, _) = Build(new Candidates());
        var thrown = Assert.Throws<InvalidOperationException>(tracer.Exit);
        Assert.Contains("ServerTracer.Exit without an Enter", thrown.Message);
    }

    [Fact]
    public void TheQueryIsOutOfScopeOnceTheTraceReturns()
    {
        var (tracer, _, sink) = Build(new Candidates());
        tracer.Line(Vector.Zero, new Vector(1, 0, 0), 1, Shooter, 0);
        Assert.Null(sink.Current);
    }

    [Fact]
    public void TheEntityTheTraceNamesComesBackAsItsHandle()
    {
        var (tracer, engine, _) = Build(new Candidates());

        ServerTrace hit = tracer.Line(Vector.Zero, new Vector(1, 0, 0), 1, EntityHandle.None, 0);
        engine.HitEntity = 0;
        ServerTrace miss = tracer.Line(Vector.Zero, new Vector(1, 0, 0), 1, EntityHandle.None, 0);

        Assert.Equal(Crate, hit.Hit);
        Assert.Equal(0.25f, hit.Trace.Fraction);
        Assert.Equal(EntityHandle.None, miss.Hit);
    }
}
