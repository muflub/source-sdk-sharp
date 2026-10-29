using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// What stock's CEnvBeam sets on its beam's look from its spawnflags
/// (EnvBeam.cpp BeamUpdateVars and Spawn): SF_BEAM_SHADEIN (0x80) is
/// FBEAM_SHADEIN, else SF_BEAM_SHADEOUT (0x100) is FBEAM_SHADEOUT, and
/// SF_BEAM_TAPEROUT (0x200) narrows the far end to nothing.
/// </summary>
public class EnvBeamLookTests
{
    private static readonly EntityHandle Beam = new(0x0004_0601u);
    private static readonly EntityHandle Owner = new(0x0004_0602u);

    private static SsBeam Spawned(int spawnflags)
    {
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(EnvBeam).Assembly);
        registry.Services = Services(out var engine);
        engine.Strict = false;
        bool made = false;
        engine.Returns("Precache.PrecacheModel", 7)
              .Returns("Entities.GetSpawnFlags", spawnflags)
              .ReturnsEachCall("Entities.IsValid", () => made)
              .ReturnsEachCall("Entities.Create", () => { made = true; return Beam; });

        registry.Spawn(Beam, EntityNames.Beam);
        registry.KeyValue(Owner, EntityNames.EnvBeam, "texture", "sprites/laserbeam.vmt");
        registry.KeyValue(Owner, EntityNames.EnvBeam, "BoltWidth", "4");
        registry.Spawn(Owner, EntityNames.EnvBeam);
        return Assert.IsType<SsBeam>(registry.Get(Beam));
    }

    [Fact]
    public void ShadeInIsTheBeamsShadeIn() => Assert.Equal(BeamNetFlags.ShadeIn, Spawned(0x80).State.Flags);

    [Fact]
    public void ShadeOutIsTheBeamsShadeOut() => Assert.Equal(BeamNetFlags.ShadeOut, Spawned(0x100).State.Flags);

    [Fact]
    public void ShadeInWinsWhenBothAreTicked() => Assert.Equal(BeamNetFlags.ShadeIn, Spawned(0x180).State.Flags);

    [Fact]
    public void NeitherLeavesItPlain() => Assert.Equal(0, Spawned(1).State.Flags);

    [Fact]
    public void TaperOutNarrowsTheFarEndToNothing()
    {
        var beam = Spawned(0x200);

        Assert.Equal(4f, beam.State.Width);
        Assert.Equal(0f, beam.State.EndWidth);
    }

    [Fact]
    public void WithoutTaperBothEndsAreTheWidth() => Assert.Equal(4f, Spawned(1).State.EndWidth);

    [Fact]
    public void ItsHdrScaleIsStocksOne() => Assert.Equal(1f, Spawned(1).State.HdrColorScale);
}
