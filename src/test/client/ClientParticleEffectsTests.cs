using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed CParticleMgr's ownerless effects (lane c5-particles): an effect
/// is started at a world point on its own executor, the executor's questions
/// for the no-owner handle reach it by id, a finished effect's executor is
/// released at the next frame, and the effects are carried across a reload
/// by kind.
/// </summary>
public class ClientParticleEffectsTests
{
    /// <summary>A simple emitter: a few particles that live a second, then done.</summary>
    private sealed class Puffs : IClientParticleEffect
    {
        private readonly SimpleEmitter _emitter = new(new SimpleEmitterRules());
        private int _toSpawn = 3;
        private int _nextId = 1;

        public string Material => "particle/particle_smokegrenade";

        public string ReloadKind => "test.puffs";

        public bool Finished => _toSpawn == 0 && _emitter.Particles.Count == 0;

        public int Emit(ref ParticleEmit emit, Span<int> idsOut)
        {
            int n = 0;
            for (; _toSpawn > 0 && n < idsOut.Length; _toSpawn--, n++)
            {
                var p = new SimpleParticle { Id = _nextId++, Position = emit.Origin, DieTime = 1, StartAlpha = 255, StartSize = 4 };
                _emitter.Add(p);
                idsOut[n] = p.Id;
            }
            return n;
        }

        public int Simulate(float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut)
        {
            _emitter.Simulate(dt, new UniformStream(1));
            for (int i = 0; i < ids.Length; i++)
                aliveOut[i] = _emitter.IndexOf(ids[i]) >= 0 ? (byte)1 : (byte)0;
            return 1;
        }

        public int Draw(Span<ParticleQuadRaw> quadsOut)
        {
            var ps = _emitter.Particles;
            if (ps.Count > quadsOut.Length)
                return ps.Count;
            for (int i = 0; i < ps.Count; i++)
                quadsOut[i] = _emitter.Draw(ps[i]).ToRaw(ps[i].Id);
            return ps.Count;
        }

        public void Save(BinaryWriter writer)
        {
            writer.Write(_toSpawn);
            writer.Write(_nextId);
            _emitter.Save(writer);
        }

        public void Restore(BinaryReader reader)
        {
            _toSpawn = reader.ReadInt32();
            _nextId = reader.ReadInt32();
            _emitter.Restore(reader);
        }
    }

    private static (ClientParticleEffects Effects, RecordingParticleExecutor Executor, ClientParticleSims Sims) Make(ParticleEffectKinds? kinds = null)
    {
        var executor = new RecordingParticleExecutor();
        var effects = new ClientParticleEffects(executor, kinds ?? new ParticleEffectKinds());
        return (effects, executor, new ClientParticleSims(new ClientStandaloneRegistry(new ServiceContainer()), effects));
    }

    private static readonly uint NoOwner = EntityHandle.None.Value;

    [Fact]
    public void An_effect_starts_on_its_own_executor_at_its_point_and_answers_by_id()
    {
        var (effects, executor, sims) = Make();
        int id = effects.Start(new Puffs(), new Vector(1, 2, 3));

        Assert.Equal(1, id);
        Assert.Equal(["createat 1 particle/particle_smokegrenade 1 2 3 0 False"], executor.Calls);

        var emit = new ParticleEmit { Dt = 0.1f, Origin = new Vector(1, 2, 3) };
        Span<int> ids = stackalloc int[8];
        Assert.Equal(3, sims.Emit(NoOwner, id, ref emit, ids));
        var quads = new ParticleQuadRaw[8];
        Assert.Equal(3, sims.Draw(NoOwner, id, quads));
        Assert.Equal(-1, sims.Draw(NoOwner, id + 1, quads));
    }

    [Fact]
    public void A_finished_effect_lets_its_executor_go_at_the_next_frame_and_not_before()
    {
        var (effects, executor, sims) = Make();
        int id = effects.Start(new Puffs(), default);
        var emit = new ParticleEmit { Dt = 0.1f };
        Span<int> ids = stackalloc int[8];
        int n = sims.Emit(NoOwner, id, ref emit, ids);

        effects.Frame();
        Assert.DoesNotContain(executor.Calls, c => c.StartsWith("destroy", StringComparison.Ordinal));

        Span<byte> alive = stackalloc byte[8];
        Assert.Equal(1, sims.Simulate(NoOwner, id, 1.5f, true, ids[..n], alive[..n]));
        Assert.Equal([0, 0, 0], alive[..n].ToArray());

        effects.Frame();
        Assert.Contains($"destroy {NoOwner} {id}", executor.Calls);
        Assert.Equal(0, effects.Count);
    }

    [Fact]
    public void A_refused_executor_starts_nothing()
    {
        var (effects, _, _) = Make();
        var puffs = new RefusedMaterial();
        Assert.Equal(0, effects.Start(puffs, default));
        Assert.Equal(0, effects.Count);
    }

    private sealed class RefusedMaterial : IClientParticleEffect
    {
        public string Material => "";
        public string ReloadKind => "test.none";
        public bool Finished => true;
        public int Emit(ref ParticleEmit emit, Span<int> idsOut) => 0;
        public int Simulate(float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut) => 0;
        public int Draw(Span<ParticleQuadRaw> quadsOut) => 0;
        public void Save(BinaryWriter writer) { }
        public void Restore(BinaryReader reader) { }
    }

    [Fact]
    public void A_reload_carries_the_effects_by_kind_and_releases_the_ones_nobody_can_rebuild()
    {
        var kinds = new ParticleEffectKinds();
        kinds.Register("test.puffs", () => new Puffs());
        var (before, _, beforeSims) = Make(kinds);
        int id = before.Start(new Puffs(), default);
        int orphan = before.Start(new Orphan(), default);
        var emit = new ParticleEmit { Dt = 0.1f };
        Span<int> ids = stackalloc int[8];
        beforeSims.Emit(NoOwner, id, ref emit, ids);

        var writer = new ReloadWriter();
        before.Save(writer);

        var (after, executor, sims) = Make(kinds);
        after.Restore(new ReloadReader(writer.ToArray()));

        Assert.Equal(3, sims.Draw(NoOwner, id, new ParticleQuadRaw[8]));
        Assert.Equal([$"destroy {NoOwner} {orphan}"], executor.Calls);
        Assert.Equal(3, after.Start(new Puffs(), default));   // ids continue, never reused
    }

    private sealed class Orphan : IClientParticleEffect
    {
        public string Material => "x";
        public string ReloadKind => "test.unregistered";
        public bool Finished => false;
        public int Emit(ref ParticleEmit emit, Span<int> idsOut) => 0;
        public int Simulate(float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut) => 0;
        public int Draw(Span<ParticleQuadRaw> quadsOut) => 0;
        public void Save(BinaryWriter writer) { }
        public void Restore(BinaryReader reader) { }
    }
}
