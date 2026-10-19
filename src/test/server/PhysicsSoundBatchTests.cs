using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_physics_bones_native_zero 2.3: the server's queued physics sounds,
/// managed. Facts hand-derived from native's physics.cpp
/// (SourceSharp_PlayImpactSounds / SourceSharp_PlayBreakSounds,
/// CPhysicsHook::PreClientUpdate) and ss_physics_sound.h (AddImpactSound /
/// AddBreakSound) at bc21e4d46, which are stock's vphysics_sound.h on ss::
/// filters.
/// </summary>
public class PhysicsSoundBatchTests
{
    private sealed class World : IPhysicsSoundWorld
    {
        public readonly Dictionary<EntityHandle, Vector> Objects = new();
        public readonly Dictionary<EntityHandle, (Vector, int)> Breakers = new();
        public readonly Dictionary<int, SurfaceImpactSounds> Surfaces = new();
        public readonly HashSet<string> Unscripted = new();
        public readonly List<(string Name, Vector Origin, int Channel, float Volume)> Emitted = new();

        public Vector? ObjectPosition(EntityHandle entity)
            => Objects.TryGetValue(entity, out Vector v) ? v : null;

        public (Vector Origin, int Material)? BreakSource(EntityHandle entity)
            => Breakers.TryGetValue(entity, out var b) ? b : null;

        public SurfaceImpactSounds? Surface(int surfaceProps)
            => Surfaces.TryGetValue(surfaceProps, out var s) ? s : null;

        public bool Emit(string soundName, Vector origin, int channel, float volume)
        {
            if (Unscripted.Contains(soundName))
                return false;
            Emitted.Add((soundName, origin, channel, volume));
            return true;
        }
    }

    private static readonly EntityHandle A = new(0x0001_0010u);
    private static readonly EntityHandle B = new(0x0001_0011u);

    private readonly World _world = new();
    private readonly PhysicsSoundBatch _batch;

    public PhysicsSoundBatchTests()
    {
        _batch = new PhysicsSoundBatch(_world);
        _world.Objects[A] = new Vector(1, 2, 3);
        _world.Objects[B] = new Vector(4, 5, 6);
        // Surface 1: hard and soft, a hard threshold of 0.5, a velocity threshold of 100.
        _world.Surfaces[1] = new SurfaceImpactSounds("Wood.ImpactHard", "Wood.ImpactSoft", "Wood.Break", 0.8f, 0.5f, 100f);
        // Surface 2: a soft surface (hardness 0.2).
        _world.Surfaces[2] = new SurfaceImpactSounds("Dirt.Impact", null, null, 0.2f, 0f, 0f);
        // Surface 3: a hard surface with no sounds.
        _world.Surfaces[3] = new SurfaceImpactSounds(null, null, null, 0.9f, 0f, 0f);
    }

    private void Play() => _batch.OnPreClientUpdate(0.06f);

    [Fact]
    public void Nothing_plays_until_more_than_fifty_milliseconds_of_frame_time()
    {
        _batch.AddImpactSound(A, 4, 1, 3, 0.5f, 500f);
        _batch.OnPreClientUpdate(0.03f);
        _batch.OnPreClientUpdate(0.02f);   // 0.05 exactly: not more than
        Assert.Empty(_world.Emitted);

        _batch.OnPreClientUpdate(0.001f);
        Assert.Single(_world.Emitted);

        // And the clock restarts after a play.
        _batch.AddImpactSound(A, 4, 1, 3, 0.5f, 500f);
        _batch.OnPreClientUpdate(0.03f);
        Assert.Single(_world.Emitted);
    }

    [Fact]
    public void An_entity_with_no_object_queues_nothing()
    {
        _batch.AddImpactSound(new EntityHandle(0x0001_0099u), 4, 1, 3, 0.5f, 500f);
        Assert.Equal(0, _batch.QueuedImpacts);
    }

    [Fact]
    public void A_hard_hit_on_a_hard_surface_plays_the_hard_sound_at_the_object()
    {
        _batch.AddImpactSound(A, 4, 1, 3, 0.5f, 500f);
        Play();
        Assert.Equal([("Wood.ImpactHard", new Vector(1, 2, 3), 4, 0.5f)], _world.Emitted);
    }

    [Fact]
    public void A_softer_other_surface_plays_the_soft_sound()
    {
        _batch.AddImpactSound(A, 4, 1, 2, 0.5f, 500f);
        Play();
        Assert.Equal("Wood.ImpactSoft", Assert.Single(_world.Emitted).Name);
    }

    [Fact]
    public void A_hit_slower_than_the_velocity_threshold_plays_the_soft_sound()
    {
        _batch.AddImpactSound(A, 4, 1, 3, 0.5f, 99f);
        Play();
        Assert.Equal("Wood.ImpactSoft", Assert.Single(_world.Emitted).Name);
    }

    [Fact]
    public void A_surface_with_no_soft_sound_plays_the_hard_one_whatever_it_hit()
    {
        _batch.AddImpactSound(A, 4, 2, 2, 0.5f, 1f);
        Play();
        Assert.Equal("Dirt.Impact", Assert.Single(_world.Emitted).Name);
    }

    [Fact]
    public void A_surface_with_no_hard_sound_is_skipped_and_the_walk_goes_on()
    {
        _batch.AddImpactSound(A, 4, 1, 3, 0.5f, 500f);
        _batch.AddImpactSound(B, 4, 3, 3, 0.5f, 500f);
        Play();
        Assert.Equal("Wood.ImpactHard", Assert.Single(_world.Emitted).Name);
        Assert.Equal(0, _batch.QueuedImpacts);
    }

    [Fact]
    public void Hits_on_one_surface_fold_volumes_add_and_the_louder_takes_the_position()
    {
        _batch.AddImpactSound(A, 4, 1, 3, 0.3f, 200f);
        _batch.AddImpactSound(B, 6, 1, 2, 0.4f, 150f);
        Assert.Equal(1, _batch.QueuedImpacts);
        Play();

        // B was louder: its position, channel and partner (soft surface 2);
        // the volumes added to 0.7; the speed is the larger, above threshold.
        var e = Assert.Single(_world.Emitted);
        Assert.Equal(("Wood.ImpactSoft", new Vector(4, 5, 6), 6), (e.Name, e.Origin, e.Channel));
        Assert.Equal(0.7f, e.Volume, 5);
    }

    [Fact]
    public void A_folded_volume_is_clamped_to_one_when_it_plays()
    {
        _batch.AddImpactSound(A, 4, 1, 3, 0.8f, 500f);
        _batch.AddImpactSound(A, 4, 1, 3, 0.8f, 500f);
        Play();
        Assert.Equal(1f, Assert.Single(_world.Emitted).Volume);
    }

    [Fact]
    public void Past_five_queued_every_new_hit_folds()
    {
        for (int s = 10; s < 15; s++)
        {
            _world.Surfaces[s] = new SurfaceImpactSounds($"S{s}", null, null, 1f, 0f, 0f);
            _batch.AddImpactSound(A, 4, s, 3, 0.1f, 500f);
        }

        Assert.Equal(5, _batch.QueuedImpacts);
        _world.Surfaces[20] = new SurfaceImpactSounds("S20", null, null, 1f, 0f, 0f);
        _batch.AddImpactSound(A, 4, 20, 3, 0.1f, 500f);
        Assert.Equal(5, _batch.QueuedImpacts);
    }

    [Fact]
    public void Impacts_play_newest_first_and_a_missing_script_ends_the_walk_but_clears_the_queue()
    {
        _world.Surfaces[4] = new SurfaceImpactSounds("Metal.Impact", null, null, 1f, 0f, 0f);
        _world.Surfaces[5] = new SurfaceImpactSounds("Glass.Impact", null, null, 1f, 0f, 0f);
        _batch.AddImpactSound(A, 4, 4, 3, 0.5f, 500f);
        _batch.AddImpactSound(A, 4, 5, 3, 0.5f, 500f);
        _batch.AddImpactSound(A, 4, 1, 3, 0.5f, 500f);
        _world.Unscripted.Add("Glass.Impact");
        Play();

        Assert.Equal(["Wood.ImpactHard"], _world.Emitted.Select(e => e.Name));
        Assert.Equal(0, _batch.QueuedImpacts);
    }

    [Fact]
    public void A_break_plays_on_chan_static_at_full_volume_from_the_origin()
    {
        _world.Breakers[A] = (new Vector(7, 8, 9), 1);
        _batch.AddBreakSound(A);
        Play();
        Assert.Equal([("Wood.Break", new Vector(7, 8, 9), PhysicsSoundBatch.ChanStatic, 1f)], _world.Emitted);
    }

    [Fact]
    public void A_surface_with_no_break_sound_or_an_entity_with_no_object_queues_no_break()
    {
        _world.Breakers[A] = (new Vector(7, 8, 9), 2);
        _batch.AddBreakSound(A);
        _batch.AddBreakSound(B);
        Assert.Equal(0, _batch.QueuedBreaks);
    }

    [Fact]
    public void Breaks_fold_by_surface_only_once_more_than_two_are_queued()
    {
        _world.Surfaces[6] = new SurfaceImpactSounds(null, null, "Glass.Break", 1f, 0f, 0f);
        _world.Breakers[A] = (new Vector(0, 0, 0), 1);
        _world.Breakers[B] = (new Vector(10, 0, 0), 1);
        var c = new EntityHandle(0x0001_0012u);
        _world.Breakers[c] = (new Vector(0, 0, 0), 6);

        _batch.AddBreakSound(A);
        _batch.AddBreakSound(B);   // two queued: no fold yet
        _batch.AddBreakSound(c);
        Assert.Equal(3, _batch.QueuedBreaks);

        var d = new EntityHandle(0x0001_0013u);
        _world.Breakers[d] = (new Vector(20, 0, 0), 1);
        _batch.AddBreakSound(d);   // three queued: folds into the newest wood entry, B's
        Assert.Equal(3, _batch.QueuedBreaks);
        Play();
        Assert.Equal(["Glass.Break", "Wood.Break", "Wood.Break"], _world.Emitted.Select(e => e.Name));
        Assert.Equal(new Vector(15, 0, 0), _world.Emitted[1].Origin);   // B (10) and d (20), the midpoint
        Assert.Equal(new Vector(0, 0, 0), _world.Emitted[2].Origin);
    }

    [Fact]
    public void A_missing_break_script_returns_and_leaves_the_breaks_queued()
    {
        _world.Breakers[A] = (new Vector(7, 8, 9), 1);
        _batch.AddBreakSound(A);
        _world.Unscripted.Add("Wood.Break");
        Play();
        Assert.Empty(_world.Emitted);
        Assert.Equal(1, _batch.QueuedBreaks);
    }

    [Fact]
    public void The_level_end_empties_both_queues()
    {
        _world.Breakers[A] = (new Vector(7, 8, 9), 1);
        _batch.AddImpactSound(A, 4, 1, 3, 0.5f, 500f);
        _batch.AddBreakSound(A);
        _batch.OnLevelShutdownPostEntity();
        Assert.Equal(0, _batch.QueuedImpacts);
        Assert.Equal(0, _batch.QueuedBreaks);
    }
}

/// <summary>
/// surfacedata_t's offsets, held here to the numbers
/// vtable_index_agreement_test's SurfaceDataLayout holds to the header.
/// </summary>
public class VPhysicsSurfaceDataLayoutTests
{
    [Theory]
    [InlineData(nameof(VPhysicsSurfaceData.HardnessFactor), 24)]
    [InlineData(nameof(VPhysicsSurfaceData.HardThreshold), 36)]
    [InlineData(nameof(VPhysicsSurfaceData.HardVelocityThreshold), 40)]
    [InlineData(nameof(VPhysicsSurfaceData.ImpactSoft), 48)]
    [InlineData(nameof(VPhysicsSurfaceData.ImpactHard), 50)]
    [InlineData(nameof(VPhysicsSurfaceData.RoughnessFactor), 28)]
    [InlineData(nameof(VPhysicsSurfaceData.RoughThreshold), 32)]
    [InlineData(nameof(VPhysicsSurfaceData.ScrapeSmooth), 52)]
    [InlineData(nameof(VPhysicsSurfaceData.ScrapeRough), 54)]
    [InlineData(nameof(VPhysicsSurfaceData.BreakSound), 60)]
    [InlineData(nameof(VPhysicsSurfaceData.MaxSpeedFactor), 64)]
    [InlineData(nameof(VPhysicsSurfaceData.JumpFactor), 68)]
    [InlineData(nameof(VPhysicsSurfaceData.GameMaterial), 72)]
    [InlineData(nameof(VPhysicsSurfaceData.Climbable), 74)]
    public void Each_field_is_at_the_headers_offset(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<VPhysicsSurfaceData>(field));

    [Fact]
    public void The_record_is_the_headers_size()
        => Assert.Equal(VPhysicsSurfaceData.SizeInBytes, Marshal.SizeOf<VPhysicsSurfaceData>());
}
