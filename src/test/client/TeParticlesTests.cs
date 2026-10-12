using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// CTEParticleRenderer's arms the recording does not reach (the blood stream
/// and the debug line use only the static and vox types): stock-derived from
/// c_te_particlesystem.cpp, not recorded.
/// </summary>
public class TeParticlesTests
{
    private sealed class Ints(params int[] values) : IParticleRandom
    {
        private readonly Queue<int> _q = new(values);

        public float RandomFloat(float min, float max) => throw new InvalidOperationException();

        public int RandomInt(int min, int max) => _q.Dequeue();
    }

    private static TeParticle Particle(TeParticleType type, float lifetime = 5) => new()
    {
        Type = type, Lifetime = lifetime, Velocity = new Vector(10, 20, 30),
    };

    [Fact]
    public void ALifetimeUnderZeroDiesAtTheStepNotBefore()
    {
        var p = Particle(TeParticleType.Static, 0.05f);
        Assert.True(TeParticles.Step(ref p, 0.05f, 600, new Ints()));
        Assert.False(TeParticles.Step(ref p, 0.01f, 600, new Ints()));
    }

    [Fact]
    public void FireClimbsItsRampAndDiesPastTheSixthRow()
    {
        var p = Particle(TeParticleType.Fire);
        // 5 * 0.2 s of ramp a step: 1.0 in 6.10 fixed point, one row a step.
        for (int row = 1; row < 6; row++)
        {
            Assert.True(TeParticles.Step(ref p, 0.2f, 0, new Ints()));
            Assert.Equal(row, p.Word >> TeParticles.SimShift);
        }
        Assert.True(TeParticles.Step(ref p, 0.2f, 0, new Ints()));
        Assert.Equal(-1, p.Lifetime);
        Assert.False(TeParticles.Step(ref p, 0.2f, 0, new Ints()));
    }

    [Fact]
    public void SlowGravAssignsTheGravityStepRatherThanSubtractingIt()
    {
        var p = Particle(TeParticleType.SlowGrav);
        TeParticles.Step(ref p, 0.1f, 600, new Ints());
        Assert.Equal(0.1f * 600 * 0.05f, p.Velocity.Z);
    }

    [Fact]
    public void ABlobFlickersByTheDraw()
    {
        var p = Particle(TeParticleType.Blob);
        TeParticles.Step(ref p, 0.01f, 600, new Ints(0));
        Assert.Equal((TeParticleType.Blob2, (byte)TeParticle.ToByte(255.9f * 255.9f)), (p.Type, p.A));
        TeParticles.Step(ref p, 0.01f, 600, new Ints(2));
        Assert.Equal((TeParticleType.Blob, (byte)0), (p.Type, p.A));
    }

    [Fact]
    public void ExplodeGrowsItsVelocityAndExplode2Damps()
    {
        var a = Particle(TeParticleType.Explode);
        var b = Particle(TeParticleType.Explode2);
        TeParticles.Step(ref a, 0.1f, 0, new Ints());
        TeParticles.Step(ref b, 0.1f, 0, new Ints());
        Assert.True(a.Velocity.X > 10 && b.Velocity.X < 10, $"{a.Velocity.X} {b.Velocity.X}");
    }

    [Fact]
    public void AColourByteIsTheProductModulo256AsStocksCompilerLeftIt()
    {
        Assert.Equal((byte)(54000 % 256), TeParticle.ToByte(54000.5f));
    }
}
