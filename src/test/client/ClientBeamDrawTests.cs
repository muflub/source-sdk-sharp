using SourceSharp.Abi;
using SourceSharp.Client.Beams;
using SourceSharp.Client.Standalone;

using Xunit;

using static SourceSharp.Tests.ClientDoubles;

namespace SourceSharp.Tests;

/// <summary>
/// beam's client half draws as stock's C_Beam does: at its own DrawModel,
/// through <c>beams->DrawBeam( this )</c>, with the effects its server half
/// networked.
/// </summary>
public class ClientBeamDrawTests
{
    private static (ClientBeam Beam, RecordingEngine Engine) Lit()
    {
        var engine = Fake();
        var beam = new ClientBeam(engine, new RecordingBeams(engine));
        beam.State.BindLocal();
        beam.State.Visible = true;
        beam.State.SpriteIndex = 7;
        beam.State.Start = new(0, 0, 188);
        beam.State.End = new(0, 0, 68);
        beam.State.Flags = SourceSharp.Shared.BeamNetFlags.ShadeOut | SourceSharp.Shared.BeamNetFlags.NoTile;
        beam.State.FadeLength = 120f;
        beam.State.HaloIndex = 9;
        beam.State.HaloScale = 60f;
        beam.State.HdrColorScale = 0.7f;
        return (beam, engine);
    }

    [Fact]
    public void ABeamDrawsInPlaceNotInTheBatch()
    {
        var (beam, _) = Lit();

        Assert.Equal(ClientStandaloneEntity.DrawsInPlace, beam.OnDrawBatched());
    }

    [Fact]
    public void ABeamDrawsItselfWithTheEffectsItsServerHalfSent()
    {
        var (beam, engine) = Lit();

        Assert.Equal(1, beam.OnDraw());

        var call = engine.Last("Beams.DrawEntityBeam");
        Assert.True(call is not null, "the beam drew nothing");
        var info = (BeamInfo)call!.Value.Args[0]!;
        Assert.Equal(BeamFlags.ShadeOut | BeamFlags.NoTile, (BeamFlags)call.Value.Args[1]!);
        Assert.Equal(0.7f, (float)call.Value.Args[2]!);
        Assert.Equal(120f, info.FadeLength);
        Assert.Equal(9, info.HaloIndex);
        Assert.Equal(60f, info.HaloScale);
    }

    [Fact]
    public void ABeamWithNoHaloSendsNone()
    {
        var (beam, engine) = Lit();
        beam.State.HaloIndex = 0;

        beam.OnDraw();

        Assert.Equal(-1, ((BeamInfo)engine.Last("Beams.DrawEntityBeam")!.Value.Args[0]!).HaloIndex);
    }

    [Fact]
    public void AHiddenBeamDrawsNothing()
    {
        var (beam, engine) = Lit();
        beam.State.Visible = false;

        Assert.Equal(0, beam.OnDraw());
        Assert.False(engine.Called("Beams.DrawEntityBeam"));
    }
}
