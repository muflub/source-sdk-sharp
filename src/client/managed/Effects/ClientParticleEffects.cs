using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// A particle effect that belongs to no entity: what stock's
/// <c>CSimpleEmitter::Create( "name" )</c> made for an impact, a spark shower
/// or a splash, placed once at a world point and gone when its particles are
/// (lane c5-particles; the base the fx.cpp family's ports stand on). The
/// executor's three questions, without a channel, plus whether it is done.
/// </summary>
public interface IClientParticleEffect
{
    /// <summary>The material the executor binds (one per effect; stock's emitters with two materials are two effects).</summary>
    string Material { get; }

    /// <summary>Stock's <c>Update</c>: this frame's spawns' ids, newest last, and the bounds.</summary>
    int Emit(ref ParticleEmit emit, Span<int> idsOut);

    /// <summary>Stock's <c>SimulateParticles</c>: 1 and the alive flags per id, or 0 when the step returned early.</summary>
    int Simulate(float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut);

    /// <summary>Stock's <c>RenderParticles</c> numbers: every live particle's quad, ids descending; the count.</summary>
    int Draw(Span<ParticleQuadRaw> quadsOut);

    /// <summary>
    /// No live particles and nothing more to spawn: stock's emitter deleted
    /// itself here (the binding's auto-delete when empty).
    /// </summary>
    bool Finished { get; }

    /// <summary>The kind <see cref="ParticleEffectKinds"/> rebuilds it by after a reload.</summary>
    string ReloadKind { get; }

    void Save(BinaryWriter writer);

    void Restore(BinaryReader reader);
}

/// <summary>
/// An effect that adds particles while it simulates (stock's
/// <c>CSmokeEmitter</c>): the ids its last step added and kept, handed to the
/// executor in that step's crossing so it links them as stock's
/// <c>AddParticle</c> inside <c>SimulateParticles</c> did.
/// </summary>
public interface IClientParticleSimulateSpawns
{
    int TakeSimulateSpawns(Span<int> idsOut);
}

/// <summary>
/// The kinds of <see cref="IClientParticleEffect"/> that can be rebuilt after
/// a managed reload, by name. Each fx port registers its own; an effect of a
/// kind nobody registered is not carried (its executor is released).
/// </summary>
public sealed class ParticleEffectKinds
{
    private readonly Dictionary<string, Func<IClientParticleEffect>> _kinds = new(StringComparer.Ordinal);

    public void Register(string kind, Func<IClientParticleEffect> create) => _kinds[kind] = create;

    public IClientParticleEffect? Create(string kind) => _kinds.TryGetValue(kind, out var create) ? create() : null;
}

/// <summary>
/// The managed half of stock's <c>CParticleMgr</c> for effects with no
/// owner (lane c5-particles). Each started effect gets an id and a native
/// executor (IShimParticlesClient::ParticlesCreateAt, the same
/// CManagedParticleEffect the entity sims draw through, keyed by the
/// no-owner handle and the id); the executor's questions arrive here through
/// <see cref="ClientParticleSims"/>; an effect that reports
/// <see cref="IClientParticleEffect.Finished"/> has its executor released at
/// the next frame, outside the manager's own iteration. Carried across
/// <c>cs_cl_reload</c> as bytes, since the executors outlive the swap.
/// </summary>
public sealed class ClientParticleEffects(IClientParticleExecutor executor, ParticleEffectKinds kinds) : IReloadParticipant
{
    /// <summary>The no-owner handle the executor keys these by (EntityHandle.None: the invalid handle is 0xFFFFFFFF).</summary>
    public static EntityHandle NoOwner => EntityHandle.None;

    private readonly Dictionary<int, IClientParticleEffect> _effects = new();
    private int _nextId = 1;

    public int Count => _effects.Count;

    public IClientParticleEffect? Get(int id) => _effects.GetValueOrDefault(id);

    /// <summary>
    /// Starts <paramref name="effect"/> at <paramref name="origin"/>: its id, or
    /// 0 when the executor refused (no material).
    /// </summary>
    public int Start(IClientParticleEffect effect, Vector origin, float cullRadius = 0f, bool alwaysSimulate = false)
    {
        int id = _nextId++;
        if (!executor.CreateAt(id, effect.Material, origin, cullRadius, alwaysSimulate))
            return 0;
        _effects[id] = effect;
        return id;
    }

    public int Emit(int id, ref ParticleEmit emit, Span<int> idsOut)
        => _effects.TryGetValue(id, out var e) ? e.Emit(ref emit, idsOut) : -1;

    public int Simulate(int id, float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut)
        => _effects.TryGetValue(id, out var e) ? e.Simulate(dt, wasDrawnPrevFrame, ids, aliveOut) : -1;

    /// <summary>Puts a started effect's executor in an entity attachment's space, or before the view model.</summary>
    public void SetSpace(int id, uint entity, int attachment, ParticleSpace flags)
    {
        if (_effects.ContainsKey(id))
            executor.SetSpace(NoOwner, id, entity, attachment, flags);
    }

    public int TakeSimulateSpawns(int id, Span<int> idsOut)
        => _effects.TryGetValue(id, out var e) && e is IClientParticleSimulateSpawns s ? s.TakeSimulateSpawns(idsOut) : 0;

    public int Draw(int id, Span<ParticleQuadRaw> quadsOut)
        => _effects.TryGetValue(id, out var e) ? e.Draw(quadsOut) : -1;

    /// <summary>Once a frame: the finished effects' executors are released.</summary>
    public void Frame()
    {
        List<int>? done = null;
        foreach (var (id, effect) in _effects)
        {
            if (effect.Finished)
                (done ??= []).Add(id);
        }

        if (done is null)
            return;
        foreach (int id in done)
        {
            _effects.Remove(id);
            executor.Destroy(NoOwner, id);
        }
    }

    /// <summary>The level is going: the native executors go with it (the executor's own level shutdown).</summary>
    public void Reset() => _effects.Clear();

    public void Save(ReloadWriter writer)
    {
        writer.Write(_nextId);
        writer.Write(_effects.Count);
        foreach (var (id, effect) in _effects)
        {
            writer.Write(id);
            writer.Write(effect.ReloadKind);
            using var buffer = new MemoryStream();
            effect.Save(new BinaryWriter(buffer));
            writer.Write(buffer.ToArray());
        }
    }

    public void Restore(ReloadReader reader)
    {
        _effects.Clear();
        _nextId = reader.ReadInt();
        int count = reader.ReadInt();
        for (int i = 0; i < count; i++)
        {
            int id = reader.ReadInt();
            string kind = reader.ReadString();
            byte[] state = reader.ReadBytes();
            if (kinds.Create(kind) is not { } effect)
            {
                // Nobody can rebuild it: let its executor go rather than leave
                // a native effect no answer will ever end.
                executor.Destroy(NoOwner, id);
                continue;
            }
            effect.Restore(new BinaryReader(new MemoryStream(state)));
            _effects[id] = effect;
        }
    }
}
