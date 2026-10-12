using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="PoolEmitters"/>: the temp entities' muzzle-flash emitters on the
/// managed executors (lane c5-particles). What a flash builds is what starts:
/// one effect per material, the particles as filled, the emitter class's rules,
/// and the executor's space for a local-space or before-the-view-model emitter.
/// The flashes' own numbers are MuzzleFlashTests' (replayed against stock).
/// </summary>
public class PoolEmittersTests
{
    private sealed class Sinks : IParticleViewSource, IParticlePixelVisibility, IParticleWorldTrace
    {
        public ParticleView Read() => throw new InvalidOperationException();

        public float FractionVisible(Vector origin, ref int queryHandle) => throw new InvalidOperationException();

        public ParticleTrace TraceLine(Vector start, Vector end) => throw new InvalidOperationException();
    }

    private static (PoolEmitters Pool, RecordingParticleExecutor Executor, ClientParticleEffects Effects) Make()
    {
        var executor = new RecordingParticleExecutor();
        var effects = new ClientParticleEffects(executor, new ParticleEffectKinds());
        var sinks = new Sinks();
        var pool = new PoolEmitters(effects, new ParticleViews(sinks), sinks, sinks, new FxTestRig(), new RandomSeeds(1));
        return (pool, executor, effects);
    }

    private static FxSimpleParticle Simple(byte size) => new() { DieTime = 0.1f, R = 255, G = 128, B = 64, StartAlpha = 255, StartSize = size, EndSize = size, Roll = 90 };

    [Fact]
    public void A_local_space_flash_starts_one_effect_per_material_in_the_attachments_space()
    {
        var (pool, executor, _) = Make();
        int e = pool.Create(FxEmitterKind.LocalSpace, "MuzzleFlash", 77, 2, 0x1);
        pool.DrawBeforeViewModel(e);
        pool.SetBBox(e, new Vector(-16, -16, -16), new Vector(16, 16, 16));
        int a = pool.Add(e, "effects/muzzleflash1", new Vector(2, 0, 0));
        pool.SetSimple(e, a, Simple(10));
        int b = pool.Add(e, "effects/muzzleflash2", new Vector(4, 0, 0));
        pool.SetSimple(e, b, Simple(9));
        int c = pool.Add(e, "effects/muzzleflash1", new Vector(6, 0, 0));
        pool.SetSimple(e, c, Simple(8));

        var built = pool.Build(e).Cast<SimpleSheet>().ToList();
        Assert.Equal(["effects/muzzleflash1", "effects/muzzleflash2"], built.Select(s => s.Material));
        Assert.Equal([new Vector(2, 0, 0), new Vector(6, 0, 0)], built[0].Pending.Select(p => p.Position));
        Assert.Equal([10, 8], built[0].Pending.Select(p => (int)p.StartSize));
        Assert.True(built.All(s => s.Box is null), "a local-space emitter's box is the executor's (the entity's), not the flash's");
        Assert.True(built.All(s => s.Emitter.Rules.GetType() == typeof(SimpleEmitterRules)));

        pool.Release(e);
        // Attachment | ViewModel | BeforeViewModel, on the handle and attachment the flash named.
        Assert.Equal(2, executor.Calls.Count(x => x.StartsWith("createat ", StringComparison.Ordinal)));
        Assert.Equal(["space 4294967295 1 77 2 7", "space 4294967295 2 77 2 7"],
                     executor.Calls.Where(x => x.StartsWith("space ", StringComparison.Ordinal)));
        Assert.Equal((1, 2), (pool.Emitters, pool.Effects));
    }

    [Fact]
    public void A_world_flash_keeps_its_box_and_asks_only_to_draw_before_the_view_model()
    {
        var (pool, executor, _) = Make();
        int e = pool.Create(FxEmitterKind.Simple, "MuzzleFlash_Pistol_Player", 77, 1, 0);
        pool.DrawBeforeViewModel(e);
        pool.SetBBox(e, new Vector(96, -4, -4), new Vector(104, 4, 4));
        pool.SetSimple(e, pool.Add(e, "effects/muzzleflash3_noz", new Vector(100, 0, 0)), Simple(6));

        var sheet = Assert.IsType<SimpleSheet>(Assert.Single(pool.Build(e)));
        Assert.Equal((new Vector(96, -4, -4), new Vector(104, 4, 4)), sheet.Box);
        pool.Release(e);
        Assert.Equal(["space 4294967295 1 77 1 4"], executor.Calls.Where(x => x.StartsWith("space ", StringComparison.Ordinal)));
        Assert.Contains(executor.Calls, x => x == "createat 1 effects/muzzleflash3_noz 100 0 0 0 False");
    }

    [Fact]
    public void An_unplaced_world_emitter_sets_no_space()
    {
        var (pool, executor, _) = Make();
        int e = pool.Create(FxEmitterKind.Ember, "muzzle_embers", 0, 0, 0);
        pool.SetSortOrigin(e, new Vector(5, 6, 7));
        pool.SetSimple(e, pool.Add(e, "effects/muzzleflash1", new Vector(5, 6, 7)), Simple(1));
        var sheet = Assert.IsType<SimpleSheet>(Assert.Single(pool.Build(e)));
        Assert.IsType<EmberRules>(sheet.Emitter.Rules);
        pool.Release(e);
        Assert.DoesNotContain(executor.Calls, x => x.StartsWith("space ", StringComparison.Ordinal));
        Assert.Contains(executor.Calls, x => x == "createat 1 effects/muzzleflash1 5 6 7 0 False");
    }

    [Fact]
    public void A_trail_emitter_carries_its_flag_gravity_and_sparks()
    {
        var (pool, _, _) = Make();
        int e = pool.Create(FxEmitterKind.Trail, "MuzzleFlash_Shotgun_NPC", 0, 0, 0);
        pool.SetTrailFlag(e, 0x4);
        pool.SetCollisionGravity(e, 0.0f);
        int p = pool.Add(e, "effects/muzzleflash1", new Vector(1, 2, 3));
        pool.SetTrail(e, p, new FxTrailParticle { DieTime = 0.15f, Velocity = new Vector(512, 0, 0), R = 255, G = 242, B = 191, A = 255, Length = 0.05f, Width = 0.3f });
        pool.SetSimple(e, p, Simple(4));   // not a trail's: ignored

        var trail = Assert.IsType<TrailEmitter>(Assert.Single(pool.Build(e)));
        Assert.Equal(TrailFlags.Fade, trail.Flags);
        Assert.Equal(0.0f, trail.Collision.Gravity);
        TrailParticle t = Assert.Single(trail.Pending);
        Assert.Equal((new Vector(1, 2, 3), new Vector(512, 0, 0), 0.15f, 0.05f, 0.3f, (byte)242), (t.Position, t.Velocity, t.DieTime, t.Length, t.Width, t.G));
    }

    [Fact]
    public void An_emitter_holds_sixty_four_particles_then_refuses_as_the_pools_row_did()
    {
        var (pool, _, _) = Make();
        int e = pool.Create(FxEmitterKind.Simple, "x", 0, 0, 0);
        for (int i = 0; i < PoolEmitters.MaxParticles; i++)
            Assert.Equal(i, pool.Add(e, "m", default));
        Assert.Equal(-1, pool.Add(e, "m", default));
        Assert.Equal(-1, pool.Add(999, "m", default));
    }

    [Fact]
    public void A_released_emitter_is_closed()
    {
        var (pool, executor, _) = Make();
        int e = pool.Create(FxEmitterKind.Simple, "x", 0, 0, 0);
        pool.Release(e);
        Assert.Equal(-1, pool.Add(e, "m", default));
        Assert.Empty(executor.Calls);
        Assert.Equal((1, 0), (pool.Emitters, pool.Effects));
    }
}
