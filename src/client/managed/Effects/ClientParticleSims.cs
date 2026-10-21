using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;

namespace SourceSharp.Client.Effects;

/// <summary>
/// A client entity that owns one or more managed particle sims, each on a
/// channel the native executor keys it by (lane c2-sims).
/// </summary>
public interface IManagedParticleSource
{
    int Emit(int channel, ref ParticleEmit emit, Span<int> idsOut);

    int Simulate(int channel, float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut);

    int Draw(int channel, Span<ParticleQuadRaw> quadsOut);
}

/// <summary>
/// <see cref="IClientParticleSims"/> over the entity registry: the owner's
/// handle names the entity, and the entity answers for its channel.
/// </summary>
public sealed class ClientParticleSims(ClientStandaloneRegistry registry, ClientParticleEffects effects) : IClientParticleSims
{
    private IManagedParticleSource? Source(uint owner) => registry.Get(new EntityHandle(owner)) as IManagedParticleSource;

    // The no-owner handle is an effect of ClientParticleEffects, the channel its id.
    private static bool Ownerless(uint owner) => owner == ClientParticleEffects.NoOwner.Value;

    public int Emit(uint owner, int channel, ref ParticleEmit emit, Span<int> idsOut)
        => Ownerless(owner) ? effects.Emit(channel, ref emit, idsOut) : Source(owner)?.Emit(channel, ref emit, idsOut) ?? -1;

    public int Simulate(uint owner, int channel, float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut)
        => Ownerless(owner)
            ? effects.Simulate(channel, dt, wasDrawnPrevFrame, ids, aliveOut)
            : Source(owner)?.Simulate(channel, dt, wasDrawnPrevFrame, ids, aliveOut) ?? -1;

    public int TakeSimulateSpawns(uint owner, int channel, Span<int> idsOut)
        => Ownerless(owner) ? effects.TakeSimulateSpawns(channel, idsOut) : 0;

    public int Draw(uint owner, int channel, Span<ParticleQuadRaw> quadsOut)
        => Ownerless(owner) ? effects.Draw(channel, quadsOut) : Source(owner)?.Draw(channel, quadsOut) ?? -1;
}

/// <summary>
/// The world's light at a point, as <c>engine-&gt;GetLightForPoint</c> answers
/// it (particle_util.h's <c>WorldGetLightForPoint</c>): what the particle sims
/// tint by. Black when no engine answers.
/// </summary>
public interface IClientWorldLight
{
    Vector LightForPoint(Vector position, bool clamp);
}

/// <summary>The real one, over IVEngineClient, acquired on first use.</summary>
public sealed class ClientWorldLight(IFwdEngineClient engine) : IClientWorldLight
{
    public Vector LightForPoint(Vector position, bool clamp)
        => engine.TryLightForPoint(position, clamp, out Vector light) ? light : default;
}

/// <summary>A sim's quad as the executor reads it.</summary>
public static class ParticleQuadRawExtensions
{
    public static ParticleQuadRaw ToRaw(this ParticleQuad q, int id) => new()
    {
        Id = id, Kind = (int)q.Kind, Position = q.Position, Color = q.Color, Alpha = q.Alpha,
        Size = q.Size, Roll = q.Roll, Normal = q.Normal, FadeNear = q.FadeNear, FadeFar = q.FadeFar,
        FixedSort = q.FixedSortKey ? 1 : q.TruncatedDepthSort ? 2 : 0, SortKey = q.SortKey,
        Corner2 = q.Corner2, Corner3 = q.Corner3,
    };
}

/// <summary>A client entity that lays out a point_worldtext's draw (lane c2-sims).</summary>
public interface IManagedWorldTextSource
{
    int Draw(in WorldTextView view, Span<WorldTextVertexRaw> verticesOut);
}

/// <summary><see cref="IClientWorldTexts"/> over the entity registry: the owner's handle names the entity.</summary>
public sealed class ClientWorldTexts(ClientStandaloneRegistry registry) : IClientWorldTexts
{
    public int Draw(uint owner, in WorldTextView view, Span<WorldTextVertexRaw> verticesOut)
        => registry.Get(new EntityHandle(owner)) is IManagedWorldTextSource source ? source.Draw(view, verticesOut) : -1;
}
