using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The dust puff as the client starts it (lane c5-particles): lit where stock
/// sampled the light, one executor effect per material it used, simulated and
/// DRAWN (stock's particles_ez singleton was drawn by viewrender.cpp's
/// DrawParticleSingletons; an earlier port of this left them undrawn), and its
/// particles counted for step 7a; each executor in the world singleton space.
/// </summary>
public class DustPuffWiringTests
{
    private sealed class Light : IClientWorldLight
    {
        public readonly List<Vector> Asked = [];

        public Vector LightForPoint(Vector position, bool clamp)
        {
            Asked.Add(position);
            return new Vector(0.5f, 0.5f, 0.5f);
        }
    }

    private sealed class Views : IParticleViewSource, IParticlePixelVisibility
    {
        public ParticleView Read() => ParticleViewReading.From(new float[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -100 }, 0);

        public float FractionVisible(Vector origin, ref int queryHandle) => 1;
    }

    [Fact]
    public void APuffIsLitWhereStockLitItStartedDrawnAndCounted()
    {
        var executor = new RecordingParticleExecutor();
        var effects = new ClientParticleEffects(executor, new ParticleEffectKinds());
        var views = new Views();
        var light = new Light();
        var puffs = new ClientDustPuffs(effects, new ParticleViews(views), views, light, new FxTestRig(), new RandomSeeds(7));

        int n = puffs.Puff(new Vector(0, 0, 8), new Vector(0, 0, 1), 64, 32);

        Assert.Equal(32, n);
        Assert.Equal(32, puffs.Particles);
        Assert.Equal([new Vector(0, 0, 72)], light.Asked);
        Assert.True(effects.Count is >= 1 and <= 2, $"one effect per material used ({effects.Count})");
        // Each in the WORLD singleton's space (8), drawn at DrawParticleSingletons( false ) as
        // stock's AddSimpleParticle singleton was, never through the leaf system.
        var spaces = executor.Calls.Where(c => c.StartsWith("space ", StringComparison.Ordinal)).ToList();
        Assert.Equal(effects.Count, spaces.Count);
        Assert.All(spaces, c => Assert.EndsWith($" 0 0 {(int)ParticleSpace.Singleton}", c));

        Span<ParticleQuadRaw> quads = new ParticleQuadRaw[64];
        int drawn = 0, simulated = 0;
        Span<int> ids = new int[64];
        Span<byte> aliveAll = new byte[64];
        for (int id = 1; id <= 8; id++)
        {
            if (effects.Get(id) is not SimpleSheet sheet)
                continue;
            var emit = new ParticleEmit();
            int added = effects.Emit(id, ref emit, ids);
            Span<byte> alive = aliveAll[..added];
            simulated += effects.Simulate(id, 0.01f, false, ids[..added], alive) == 1 ? added : 0;
            drawn += effects.Draw(id, quads);
        }

        Assert.Equal(32, simulated);
        Assert.Equal(32, drawn);
    }
}
