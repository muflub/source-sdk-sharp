using SourceSharp.Abi;
using SourceSharp.Client.Beams;
using SourceSharp.Client.Ropes;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The beam arms <c>beam-stock.tsv</c> cannot reach because no creation path
/// in this build makes them (a spline, an entity-to-entity ring) or because
/// their caller is a managed entity rather than a temp entity (the quadratic
/// beam). Expectations are worked from beamdraw.cpp and view_beams.cpp by hand.
/// </summary>
public class BeamDrawArmTests
{
    private sealed class World : IBeamWorld, IBeamDrawWorld, IBeamRenderables
    {
        public bool EntitiesHaveModels = true;
        public readonly Dictionary<uint, Vector> Positions = [];

        public bool ModelExists(int modelIndex) => modelIndex > 0;
        public int ModelFrameCount(int modelIndex) => 1;
        public int ModelIndexForName(string name) => 7;
        public EntityHandle EntityAt(int entry) => entry == 0 ? EntityHandle.None : new EntityHandle((uint)entry);
        public bool EntityHasModel(EntityHandle entity) => EntitiesHaveModels;

        public bool EntityPosition(EntityHandle entity, int attachment, bool useHitboxes, ref Vector position)
        {
            if (!Positions.TryGetValue(entity.Value, out Vector p))
                return false;
            position = p;
            return true;
        }

        public bool IsBoxVisible(in Vector mins, in Vector maxs) => true;
        public bool CullBox(in Vector mins, in Vector maxs) => false;
        public float HaloVisibility(Beam beam, in Vector origin, float proxySize) => 1;
        public Vector ScreenPosition(in Vector point) => point;
        public int Add(Beam beam) => 0;
        public void Changed(Beam beam) { }
        public void Remove(int handle) { }
    }

    private sealed class Sink : IBeamDrawSink
    {
        public readonly List<string> Calls = [];
        public readonly List<BeamSeg[]> Strips = [];
        public readonly List<BeamVertex[]> Meshes = [];

        public bool BindSprite(int modelIndex, int frame, int renderMode)
        {
            Calls.Add($"bind {modelIndex} {renderMode}");
            return true;
        }

        public void SetHdrColorScale(int modelIndex, int renderMode, float scale) => Calls.Add($"hdr {modelIndex} {renderMode}");
        public bool HasGlow(int modelIndex) => true;
        public void BindGlow(int modelIndex) => Calls.Add($"glow {modelIndex}");

        public void Strip(ReadOnlySpan<BeamSeg> segments)
        {
            Calls.Add($"strip {segments.Length}");
            Strips.Add(segments.ToArray());
        }

        public void Mesh(BeamPrimitive primitive, int primitives, BeamColorKind colorKind, ReadOnlySpan<BeamVertex> vertices)
        {
            Calls.Add($"mesh {primitive} {primitives}");
            Meshes.Add(vertices.ToArray());
        }
    }

    private static BeamView View(float now) => new(
        new Vector(0, -200, 0), new Vector(0, 1, 0), new Vector(1, 0, 0), new Vector(0, 0, 1), default,
        new Vector(0, 0, 1), new Vector(1, 0, 0),
        new BeamMatrix([1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 1, 0, 200]), now, 1f / 60f, 1);

    [Fact]
    public void TheCatmullRomSplinePassesThroughItsInnerPoints()
    {
        var p1 = new Vector(0, 0, 0);
        var p2 = new Vector(10, 0, 0);
        var p3 = new Vector(20, 10, 0);
        var p4 = new Vector(30, 10, 0);

        Vector at0 = BeamDraw.CatmullRomSpline(p1, p2, p3, p4, 0);
        Vector at1 = BeamDraw.CatmullRomSpline(p1, p2, p3, p4, 1);

        Assert.Equal(10f, at0.X);
        Assert.Equal(0f, at0.Y);
        Assert.Equal(20f, at1.X);
        Assert.Equal(10f, at1.Y);
    }

    [Fact]
    public void ASplineDrawsOneStripAcrossItsAttachmentsAndAHaloAtTheEnd()
    {
        var world = new World();
        var system = new BeamSystem(world, world);
        var sink = new Sink();

        var beam = new Beam
        {
            Type = BeamType.Spline, ModelIndex = 5, HaloIndex = 6, HaloScale = 4, FrameCount = 1, Width = 2,
            EndWidth = 2, Segments = 8, NumAttachments = 3, R = 255, G = 255, B = 255, Brightness = 255,
            Delta = new Vector(40, 0, 0),
        };
        beam.Attachment[0] = new Vector(0, 0, 0);
        beam.Attachment[1] = new Vector(20, 0, 10);
        beam.Attachment[2] = new Vector(40, 0, 0);

        system.DrawBeam(beam, new BeamDraw(sink, world, View(1)), world);

        // (segments - 1) points per span, two spans; then the glow quad.
        Assert.Equal(["bind 5 5", "hdr 5 5", "strip 14", "glow 6", "hdr 6 3", "mesh Quads 1"], sink.Calls);

        // The strip ends on the last attachment (fraction 1 of the last span),
        // and the halo is centred there: its first vertex is down-left of it.
        BeamSeg last = sink.Strips[0][^1];
        Assert.Equal(40f, last.Pos.X, 3);
        Assert.Equal(0f, last.Pos.Z, 3);
        BeamVertex halo = sink.Meshes[0][0];
        Assert.Equal(40f - 4f, halo.Pos.X, 3);
        Assert.Equal(-4f, halo.Pos.Z, 3);
    }

    [Fact]
    public void AQuadraticBeamRunsFromStartToEndDarkAtBothEnds()
    {
        var world = new World();
        var sink = new Sink();
        var draw = new BeamDraw(sink, world, View(1));

        var start = new Vector(0, 0, 0);
        var control = new Vector(50, 0, 50);
        var end = new Vector(100, 0, 0);
        var color = new Vector(1, 0.5f, 0.25f);

        draw.DrawBeamQuadratic(start, control, end, 3, color, 2.25f);

        BeamSeg[] segs = Assert.Single(sink.Strips);
        Assert.Equal(17, segs.Length);
        Assert.Equal(start.X, segs[0].Pos.X);
        Assert.Equal(end.X, segs[16].Pos.X);
        Assert.Equal(25f, segs[8].Pos.Z);              // (2 * 0.5 * 0.5) * 50 at the midpoint
        Assert.Equal(0f, segs[0].Color.X);
        Assert.Equal(0f, segs[16].Color.X);
        Assert.Equal(1f, segs[8].Color.X);
        Assert.Equal(0.25f, segs[0].TexCoord);          // fmod( 2.25, 1 ) - 0
        Assert.Equal(-0.75f, segs[16].TexCoord);         // 0.25 - 1
        Assert.All(segs, s => Assert.Equal(3f, s.Width));
    }

    [Fact]
    public void ARingBetweenTwoModelledEntitiesIsCreatedAndFollowsThem()
    {
        var world = new World();
        world.Positions[1] = new Vector(0, 0, 0);
        world.Positions[2] = new Vector(0, 64, 0);
        var system = new BeamSystem(world, world);

        Beam? ring = system.CreateBeamRing(1, 2, 5, 0, 0, 2, 1, 1, 0, 1, 255, 0, 0, 10, 255, 255, 255, 0, 3);

        Assert.NotNull(ring);
        Assert.Equal(BeamType.Ring, ring!.Type);
        Assert.Equal(BeamFlags.StartEntity | BeamFlags.EndEntity | BeamFlags.StartVisible | BeamFlags.EndVisible,
                     ring.Flags & (BeamFlags.StartEntity | BeamFlags.EndEntity | BeamFlags.StartVisible | BeamFlags.EndVisible));
        Assert.Equal(64f, ring.Delta.Y);

        world.Positions[2] = new Vector(0, 128, 0);
        system.UpdateTempEntBeams(3.1f, 0.1f);
        Assert.Equal(128f, ring.Delta.Y);
    }

    [Fact]
    public void ARingBetweenEntitiesWithoutModelsIsRefused()
    {
        // The stock arm the recording took for every entity-to-entity beam:
        // CreateBeamRing refuses a finite-life ring whose endpoints have no model.
        var world = new World { EntitiesHaveModels = false };
        var system = new BeamSystem(world, world);

        Assert.Null(system.CreateBeamRing(1, 2, 5, 0, 0, 2, 1, 1, 0, 1, 255, 0, 0, 10, 255, 255, 255, 0, 3));
        Assert.Empty(system.Active);
    }

    [Fact]
    public void KillingAnEntityFreesItsBeamsButLetsAFollowTrailFinish()
    {
        var world = new World();
        world.Positions[1] = new Vector(0, 0, 0);
        world.Positions[2] = new Vector(0, 64, 0);
        var system = new BeamSystem(world, world);

        Beam? ents = system.CreateBeamEnts(1, 2, 5, 0, 0, 2, 1, 1, 0, 0, 255, 0, 0, 10, 255, 255, 255, 0, 3);
        Beam? follow = system.CreateBeamFollow(1, 5, 0, 0, 2, 1, 1, 0, 255, 255, 255, 255, 3);
        Assert.NotNull(ents);
        Assert.NotNull(follow);

        system.KillDeadBeams(new EntityHandle(1), 3);

        Assert.DoesNotContain(ents!, system.Active);
        Assert.Contains(follow!, system.Active);
        Assert.Equal(0, (int)(follow!.Flags & (BeamFlags.StartEntity | BeamFlags.EndEntity)));
    }

    // beam_reaper.h's guard, carried by KillDeadBeams since the C2 beam port:
    // the invalid handle reaps nothing, although every beam with no start
    // entity holds exactly that handle in its first endpoint.
    [Fact]
    public void TheInvalidHandleReapsNothingEvenWhenTheEndpointMatches()
    {
        var world = new World();
        var system = new BeamSystem(world, world);
        Beam? points = system.CreateBeamPoints(new Vector(0, 0, 0), new Vector(64, 0, 0), 5, 0, 0, 2, 1, 1, 0, 0,
                                               255, 0, 0, 10, 255, 255, 255, 3);
        Assert.NotNull(points);
        Assert.Equal(EntityHandle.None, points!.Entity[0]);

        system.KillDeadBeams(EntityHandle.None, 3);

        Assert.Contains(points, system.Active);
    }

    [Fact]
    public void AValidHandleSparesABeamThatIsNotItsSubject()
    {
        var world = new World();
        world.Positions[1] = new Vector(0, 0, 0);
        world.Positions[2] = new Vector(0, 64, 0);
        var system = new BeamSystem(world, world);
        Beam? ents = system.CreateBeamEnts(1, 2, 5, 0, 0, 2, 1, 1, 0, 0, 255, 0, 0, 10, 255, 255, 255, 0, 3);
        Assert.NotNull(ents);
        BeamFlags before = ents!.Flags;

        system.KillDeadBeams(new EntityHandle(2), 3);       // the END entity: not the reap's subject

        Assert.Contains(ents, system.Active);
        Assert.Equal(before, ents.Flags);
    }
}
