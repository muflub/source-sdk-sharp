using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The beam temp entities env_beam strikes and zaps with, packed as stock's
/// te_beampoints.cpp, te_beamentpoint.cpp, te_beaments.cpp and te_beamring.cpp
/// fill them: entities as their index with attachment 1 in bits 12-15, a
/// ring's end width its width and its fade none.
/// </summary>
public class TempEntBeamTests
{
    private static readonly TeBeamLook Look = new(ModelIndex: 12, HaloIndex: 0, StartFrame: 2, FrameRate: 10, Life: 0.5f,
                                                  Width: 4f, EndWidth: 1f, FadeLength: 3, Amplitude: 6f,
                                                  R: 255, G: 128, B: 0, A: 200, Speed: 35);

    private static (SourceSharp.Server.TempEnts Temps, RecordingEngine Engine, TempEntRegistry Registry) Make()
    {
        var engine = Fake();
        int next = 1;
        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);
        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);
        return (new SourceSharp.Server.TempEnts(new RecordingLog(), registry, engine), engine, registry);
    }

    private static TempEntReader Sent(RecordingEngine engine, TempEntRegistry registry, string className)
    {
        var call = engine.Last("Effects.TempEntPlayback")!.Value;
        TempEntType type = registry.Find(className)!;
        Assert.Equal(type.Handle, call.ArgAs<int>(0));
        return new TempEntReader(type, (byte[])call.Args[4]!);
    }

    [Fact]
    public void Beam_points_carry_both_points_and_the_look()
    {
        var (temps, engine, registry) = Make();

        temps.BeamPoints(TeFilters.Broadcast, 0f, new Vector(1f, 2f, 3f), new Vector(4f, 5f, 6f), Look);

        var r = Sent(engine, registry, "CTEBeamPoints");
        Assert.Equal(new Vector(1f, 2f, 3f), r.Vector("StartPoint"));
        Assert.Equal(new Vector(4f, 5f, 6f), r.Vector("EndPoint"));
        Assert.Equal((12, 2, 10, 3, 35), (r.Int("ModelIndex"), r.Int("StartFrame"), r.Int("FrameRate"), r.Int("FadeLength"), r.Int("Speed")));
    }

    [Fact]
    public void An_entity_end_is_its_index_with_attachment_one_and_a_point_end_is_zero()
    {
        var (temps, engine, registry) = Make();

        temps.BeamEntPoint(TeFilters.Broadcast, 0f, 7, Vector.Zero, 0, new Vector(9f, 9f, 9f), Look);

        var r = Sent(engine, registry, "CTEBeamEntPoint");
        Assert.Equal(7 | (1 << 12), r.UInt("StartEntity"));
        Assert.Equal(0, r.UInt("EndEntity"));
        Assert.Equal(new Vector(9f, 9f, 9f), r.Vector("EndPoint"));

        temps.BeamEntPoint(TeFilters.Broadcast, 0f, 0, new Vector(1f, 1f, 1f), 7, Vector.Zero, Look);
        r = Sent(engine, registry, "CTEBeamEntPoint");
        Assert.Equal(0, r.UInt("StartEntity"));
        Assert.Equal(new Vector(1f, 1f, 1f), r.Vector("StartPoint"));
    }

    [Fact]
    public void Beam_ents_name_both_ends_by_index()
    {
        var (temps, engine, registry) = Make();

        temps.BeamEnts(TeFilters.Broadcast, 0f, 3, 4100, Look);

        var r = Sent(engine, registry, "CTEBeamEnts");
        Assert.Equal(3 | (1 << 12), r.UInt("StartEntity"));
        Assert.Equal((4100 & 0x0FFF) | (1 << 12), r.UInt("EndEntity"));
    }

    [Fact]
    public void A_ring_ends_as_wide_as_it_starts_and_never_fades()
    {
        var (temps, engine, registry) = Make();

        temps.BeamRing(TeFilters.Broadcast, 0f, 3, 4, Look);

        var r = Sent(engine, registry, "CTEBeamRing");
        Assert.Equal(4f, r.Float("EndWidth"), 1);
        Assert.Equal(0, r.Int("FadeLength"));
    }
}
