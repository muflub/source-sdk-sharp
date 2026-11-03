using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>The AR2 ball's client effects, against c_prop_combine_ball.cpp's two callbacks.</summary>
public class CombineBallEffectsTests
{
    private sealed class Quads : IFxQuadSink
    {
        public readonly List<(float Start, float End, float Life, string Material)> Added = [];

        public void AddQuad(Vector origin, Vector normal, float startSize, float endSize, float sizeBias,
                            float startAlpha, float endAlpha, float alphaBias, float yaw, float deltaYaw,
                            Vector color, float lifeTime, string material, FxQuadFlags flags)
            => Added.Add((startSize, endSize, lifeTime, material));
    }

    private sealed class Sparks : IClientSparks
    {
        public readonly List<(int Magnitude, int Trail, Vector? Direction)> Electric = [];

        public void ElectricSpark(Vector position, int magnitude, int trailLength, Vector? direction)
            => Electric.Add((magnitude, trailLength, direction));

        public void MetalSpark(Vector position, Vector direction, Vector surfaceVelocity, int scale) { }
        public void EnergySplash(Vector position, Vector normal, bool explosive) { }
        public void GaussExplosion(Vector position, Vector direction, int type) { }
    }

    [Fact]
    public void A_bounce_is_a_flash_a_burn_and_sparks_off_the_wall()
    {
        var quads = new Quads();
        var sparks = new Sparks();
        var normal = new Vector(1f, 0f, 0f);
        var data = new EffectData { Origin = new Vector(1f, 2f, 3f), Normal = normal, Radius = 16f };

        new CombineBallBounceEffect(quads, sparks).Dispatch(in data);

        Assert.Equal([(160f, 0f, 0.25f, "effects/combinemuzzle1_nocull"), (32f, 64f, 0.5f, "effects/combinemuzzle2_nocull")], quads.Added);
        Assert.Equal([(2, 1, (Vector?)normal)], sparks.Electric);
    }

    [Fact]
    public void An_end_is_sparks_thrown_up()
    {
        var sparks = new Sparks();
        var data = new EffectData { Origin = new Vector(1f, 2f, 3f) };

        new CombineBallExplodeEffect(sparks).Dispatch(in data);

        Assert.Equal([(4, 1, (Vector?)new Vector(0f, 0f, 1f))], sparks.Electric);
    }
}
