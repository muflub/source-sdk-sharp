using SourceSharp.Abi;
using SourceSharp.Client.Physics;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's impact sounds against stock's numbers (client physics.cpp
/// PostCollision/ObjectSound, CPhysicsSystem::AddImpactSound, vphysics_sound.h
/// AddImpactSound/PlayImpactSounds). Lane c6-input, plan_client_novalve C6
/// cluster 9.
/// </summary>
public class ClientImpactSoundsTests
{
    private const int ChanStatic = 6;

    private sealed class World : IClientImpactSoundWorld
    {
        public readonly Dictionary<nint, nint> Owners = new();
        public readonly HashSet<nint> Statics = new();
        public readonly Dictionary<nint, Vector> Positions = new();
        public readonly Dictionary<int, SurfaceImpactAudio> Surfaces = new();
        public readonly HashSet<string> NoParameters = new();
        public readonly List<(string Name, Vector Origin, int Channel, float Volume)> Emitted = new();

        public bool IsStatic(nint o) => Statics.Contains(o);
        public nint GameData(nint o) => Owners.TryGetValue(o, out nint d) ? d : 0;
        public Vector Position(nint o) => Positions.TryGetValue(o, out Vector p) ? p : Vector.Zero;
        public SurfaceImpactAudio? Surface(int s) => Surfaces.TryGetValue(s, out var a) ? a : null;

        public bool Emit(string name, Vector origin, int channel, float volume)
        {
            if (NoParameters.Contains(name))
                return false;
            Emitted.Add((name, origin, channel, volume));
            return true;
        }
    }

    private static World Rig()
    {
        var w = new World();
        for (nint o = 1; o <= 12; o++)
        {
            w.Owners[o] = 0x1000 + o;
            w.Positions[o] = new Vector(o, 2 * o, 3 * o);
        }
        for (int s = 0; s <= 12; s++)
            w.Surfaces[s] = new SurfaceImpactAudio("hard" + s, "soft" + s, 1f, 0f, 0f);
        return w;
    }

    private static VCollisionEvent Hit(nint a, nint b, int surfA, int surfB, float speed, float delta = 1f) => new()
    {
        Object0 = a,
        Object1 = b,
        SurfaceProps0 = surfA,
        SurfaceProps1 = surfB,
        CollisionSpeed = speed,
        DeltaCollisionTime = delta,
    };

    [Fact]
    public void AnImpactNoMoreThanATenthOfASecondAfterTheLastIsSilent()
    {
        var w = Rig();
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 0, 1, 300f, delta: 0.1f));
        Assert.Equal(0, s.Queued);
    }

    [Fact]
    public void AnImpactAtSeventyOrSlowerIsSilent()
    {
        var w = Rig();
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 0, 1, 70f));
        Assert.Equal(0, s.Queued);
    }

    [Fact]
    public void AnImpactPastTheGateQueuesBothSides()
    {
        var w = Rig();
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 3, 4, 300f));
        Assert.Equal(2, s.Queued);
    }

    [Fact]
    public void AStaticSideMakesNoSound()
    {
        var w = Rig();
        w.Statics.Add(2);
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 3, 4, 300f));
        Assert.Equal(1, s.Queued);
    }

    [Fact]
    public void ASideWithNoGameDataMakesNoSound()
    {
        var w = Rig();
        w.Owners.Remove(2);
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 3, 4, 300f));
        Assert.Equal(1, s.Queued);
    }

    [Fact]
    public void ANegativeSurfacePropMakesNoSound()
    {
        var w = Rig();
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, -1, 4, 300f));
        Assert.Equal(1, s.Queued);
    }

    [Fact]
    public void TheVolumeIsTheSquaredSpeedOver320Squared()
    {
        var w = Rig();
        w.Statics.Add(2);
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 3, 4, 160f));
        s.Play();
        Assert.Equal(0.25f, Assert.Single(w.Emitted).Volume, 6);
    }

    [Fact]
    public void TheVolumeIsClampedToOne()
    {
        var w = Rig();
        w.Statics.Add(2);
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 3, 4, 400f));
        s.Play();
        Assert.Equal(1f, Assert.Single(w.Emitted).Volume);
    }

    [Fact]
    public void TheSoundIsFromTheWorldOnTheStaticChannelAtTheObject()
    {
        var w = Rig();
        w.Statics.Add(2);
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 3, 4, 300f));
        s.Play();
        var e = Assert.Single(w.Emitted);
        Assert.Equal(ChanStatic, e.Channel);
        Assert.Equal(new Vector(1, 2, 3), e.Origin);
        Assert.Equal("hard3", e.Name);
    }

    [Fact]
    public void ASoftHitSurfaceChoosesTheSoftSound()
    {
        var w = Rig();
        w.Statics.Add(2);
        w.Surfaces[3] = new SurfaceImpactAudio("hard3", "soft3", 1f, 0.5f, 0f);
        w.Surfaces[4] = new SurfaceImpactAudio("hard4", "soft4", 0.25f, 0f, 0f);
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 3, 4, 300f));
        s.Play();
        Assert.Equal("soft3", Assert.Single(w.Emitted).Name);
    }

    [Fact]
    public void WithNoSoftSoundTheHardOneIsPlayed()
    {
        var w = Rig();
        w.Statics.Add(2);
        w.Surfaces[3] = new SurfaceImpactAudio("hard3", null, 1f, 0.5f, 0f);
        w.Surfaces[4] = new SurfaceImpactAudio("hard4", "soft4", 0.25f, 0f, 0f);
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 3, 4, 300f));
        s.Play();
        Assert.Equal("hard3", Assert.Single(w.Emitted).Name);
    }

    [Fact]
    public void TheHardVelocityThresholdIsComparedWithTheSquaredSpeed()
    {
        // Stock hands ObjectSound's `speed` -- collisionSpeed squared -- to
        // AddImpactSound as the impact speed. At 100 in/s that is 10000.0001:
        // a threshold of 20000 is above it (soft) and one of 200 is not (hard).
        var w = Rig();
        w.Statics.Add(2);
        w.Surfaces[3] = new SurfaceImpactAudio("hard3", "soft3", 1f, 0f, 20000f);
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 3, 4, 100f));
        s.Play();
        Assert.Equal("soft3", Assert.Single(w.Emitted).Name);

        w.Surfaces[3] = new SurfaceImpactAudio("hard3", "soft3", 1f, 0f, 200f);
        s.PostCollision(Hit(1, 2, 3, 4, 100f));
        s.Play();
        Assert.Equal("hard3", w.Emitted[1].Name);
    }

    [Fact]
    public void ASurfaceWithNoHardSoundIsSilent()
    {
        var w = Rig();
        w.Statics.Add(2);
        w.Surfaces[3] = new SurfaceImpactAudio(null, "soft3", 1f, 0f, 0f);
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 3, 4, 300f));
        s.Play();
        Assert.Empty(w.Emitted);
    }

    [Fact]
    public void TwoImpactsOnOneSurfaceAreOneSoundWithTheirVolumesAdded()
    {
        var w = Rig();
        w.Statics.Add(2);
        w.Statics.Add(4);
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 3, 5, 160f));   // 0.25
        s.PostCollision(Hit(3, 4, 3, 6, 128f));   // 0.16
        Assert.Equal(1, s.Queued);
        s.Play();
        Assert.Equal(0.41f, Assert.Single(w.Emitted).Volume, 5);
    }

    [Fact]
    public void TheLouderImpactOfAMergeTakesTheOriginAndTheHitSurface()
    {
        var w = Rig();
        w.Statics.Add(2);
        w.Statics.Add(4);
        w.Surfaces[3] = new SurfaceImpactAudio("hard3", "soft3", 1f, 0.5f, 0f);
        w.Surfaces[6] = new SurfaceImpactAudio("hard6", "soft6", 0.25f, 0f, 0f);   // soft under 3
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 3, 5, 128f));   // quieter, hits 5 (hard)
        s.PostCollision(Hit(3, 4, 3, 6, 160f));   // louder, hits 6 (soft)
        s.Play();
        var e = Assert.Single(w.Emitted);
        Assert.Equal(new Vector(3, 6, 9), e.Origin);
        Assert.Equal("soft3", e.Name);
    }

    [Fact]
    public void AQuieterImpactOfAMergeKeepsTheFirstOrigin()
    {
        var w = Rig();
        w.Statics.Add(2);
        w.Statics.Add(4);
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 3, 5, 160f));
        s.PostCollision(Hit(3, 4, 3, 6, 128f));
        s.Play();
        Assert.Equal(new Vector(1, 2, 3), Assert.Single(w.Emitted).Origin);
    }

    [Fact]
    public void PastFourSoundsEveryImpactMergesIntoTheNewest()
    {
        var w = Rig();
        for (nint o = 2; o <= 12; o += 2)
            w.Statics.Add(o);
        var s = new ClientImpactSounds(w);
        // Five distinct surfaces: four, then a fifth added (Count was 4).
        for (int i = 0; i < 5; i++)
            s.PostCollision(Hit(2 * i + 1, 2 * i + 2, i, 12, 160f));
        Assert.Equal(5, s.Queued);
        // A sixth distinct surface with five queued merges into the newest.
        s.PostCollision(Hit(11, 12, 7, 12, 160f));
        Assert.Equal(5, s.Queued);
        s.Play();
        Assert.Equal(["hard4", "hard3", "hard2", "hard1", "hard0"], w.Emitted.Select(e => e.Name));
        Assert.Equal(0.5f, w.Emitted[0].Volume, 5);
    }

    [Fact]
    public void TheNewestSoundPlaysFirst()
    {
        var w = Rig();
        w.Statics.Add(2);
        w.Statics.Add(4);
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 3, 5, 160f));
        s.PostCollision(Hit(3, 4, 7, 6, 160f));
        s.Play();
        Assert.Equal(["hard7", "hard3"], w.Emitted.Select(e => e.Name));
    }

    [Fact]
    public void ASoundWithNoScriptParametersStopsTheRestOfTheFrame()
    {
        var w = Rig();
        w.Statics.Add(2);
        w.Statics.Add(4);
        w.NoParameters.Add("hard7");
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 3, 5, 160f));
        s.PostCollision(Hit(3, 4, 7, 6, 160f));
        s.Play();
        Assert.Empty(w.Emitted);
        Assert.Equal(0, s.Queued);
    }

    [Fact]
    public void PlayingEmptiesTheList()
    {
        var w = Rig();
        var s = new ClientImpactSounds(w);
        s.PostCollision(Hit(1, 2, 3, 4, 300f));
        s.Play();
        Assert.Equal(0, s.Queued);
        s.Play();
        Assert.Equal(2, w.Emitted.Count);
    }
}
