using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The blood stream and the debug line as the client starts them (lane
/// c5-particles): one executor effect each at its origin, every particle made
/// handed over, stepped at the engine's gravity, drawn as unrotated quads, and
/// finished when the last one dies.
/// </summary>
public class TeParticleWiringTests
{
    [Fact]
    public void AStreamIsOneEffectSteppedAtTheEnginesGravityUntilItsLastDropDies()
    {
        var engine = new RecordingEngine();
        engine.Returns("Effects.CurrentGravity", 600f);
        var effects = new ClientParticleEffects(new RecordingParticleExecutor(), new ParticleEffectKinds());
        var te = new ClientTeParticles(effects, engine, new RandomSeeds(5));

        Assert.Equal(160, te.BloodStream(new Vector(0, 0, 64), new Vector(0, 1, 0), 247, 0, 0, 255, 100));
        Assert.Equal(1, effects.Count);
        var sheet = (TeParticleSheet)effects.Get(1)!;

        var emit = new ParticleEmit();
        Span<int> ids = new int[256];
        Span<byte> alive = new byte[256];
        int added = effects.Emit(1, ref emit, ids);
        Assert.Equal(160, added);

        Vector before = sheet.Live[0].Velocity;
        Assert.Equal(1, effects.Simulate(1, 0.1f, true, ids[..added], alive[..added]));
        // a vox_grav drop falls by eight times the step's gravity (0.1 * 600 * 0.05)
        Assert.Equal(before.Z - 0.1f * 600f * 0.05f * 8, sheet.Live.Single(p => p.Id == 1).Velocity.Z);
        Assert.True(engine.Called("Effects.CurrentGravity"), "the gravity is the engine's");

        Span<ParticleQuadRaw> quads = new ParticleQuadRaw[256];
        Assert.Equal(160, effects.Draw(1, quads));
        Assert.Equal(160, te.QuadsDrawn);
        Assert.Equal((int)ParticleQuadKind.ColorSize, quads[0].Kind);

        for (int i = 0; i < 40 && !sheet.Finished; i++)
            effects.Simulate(1, 0.1f, true, ids[..added], alive[..added]);
        Assert.True(sheet.Finished, "every drop dies within its three seconds");
    }

    [Fact]
    public void ALineIsADotEveryThreeUnits()
    // Eleven dots over thirty units, not ten: VectorNormalize's reciprocal square root answers the length
    // a hair over 30 (stock's, held by TeParticleOracleTests), so the countdown
    // takes one more step past zero... and stock drew the same eleven.
    {
        var engine = new RecordingEngine();
        var effects = new ClientParticleEffects(new RecordingParticleExecutor(), new ParticleEffectKinds());
        var te = new ClientTeParticles(effects, engine, new RandomSeeds(5));
        Assert.Equal(11, te.ShowLine(new Vector(0, 0, 0), new Vector(30, 0, 0)));
    }
}
