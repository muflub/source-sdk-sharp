using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of func_precipitation: one volume of the managed weather
/// (<see cref="ClientPrecipitations"/>, stock's CClient_Precipitation ported
/// in PrecipitationSim.cs).
///
/// <para>
/// The volume is the brush's model bounds, read at the first update, as
/// stock's client built it (SetCollisionBounds from the model). The density
/// arrives as its own field where stock kept it in the render alpha, and is
/// the same number. A change of kind builds a new volume, as stock's shell did.
/// </para>
///
/// <para>
/// Rain and snow are drawn by DrawPrecipitation's executor from the system's
/// render plan. Ash, and the snowfall manager's flakes, are simple particles
/// drawn by the particle executor through this entity's channels: one per ash
/// material, and one for the flakes when this volume made the manager.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.FuncPrecipitation)]
public sealed class ClientPrecipitation(IEngineEntityRender render, ClientPrecipitations weather,
                                        IClientParticleExecutor executor, IEngineWorldQuery world,
                                        IEngineEntities entities, IGameClock clock)
    : ClientStandaloneEntity(render), IManagedParticleSource
{
    private const int AshChannels = 4;
    private const string SnowMaterial = "particle/snow";

    private readonly HashSet<int> _handed = new();
    private readonly List<int> _died = new();
    private readonly SimpleEmitter _flakeDraw = new(new SimpleEmitterRules());
    private PrecipitationVolume? _volume;
    private int _channels;
    private float _simulatedAt = float.NaN;

    public override string ClassName => EntityNames.FuncPrecipitation;

    [NetState] public PrecipitationNetState State = new();

    /// <summary>The managed volume, once the first update has built it.</summary>
    public PrecipitationVolume? Volume => _volume;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        var type = (PrecipitationType)State.PrecipType;
        if (_volume is not null && _volume.Type != type)
            Teardown();

        if (_volume is null)
        {
            if (!world.ClientEntityModelBounds(entities.GetIndex(Handle), out Vector mins, out Vector maxs, out _))
                return;
            _volume = weather.System.Create(type, State.Density, mins, maxs, Handle);
            _channels = type == PrecipitationType.Ash ? AshChannels : type == PrecipitationType.SnowFall ? 1 : 0;
            for (int c = 0; c < _channels; c++)
                executor.Create(Handle, c, MaterialOf(c), 0f, alwaysSimulate: true);
            return;
        }

        // m_flDensity, as stock's OnDataChanged recomputes it every update.
        _volume.Density = FxMath.RemapVal(State.Density, 0, 255, 0, 0.001f);
    }

    public override void OnRemoved() => Teardown();

    private void Teardown()
    {
        for (int c = 0; c < _channels; c++)
            executor.Destroy(Handle, c);
        _channels = 0;
        if (_volume is not null)
            weather.System.Remove(_volume);
        _volume = null;
        _handed.Clear();
    }

    private string MaterialOf(int channel)
        => _volume?.Type == PrecipitationType.Ash ? PrecipitationVolume.AshMaterial(channel) : SnowMaterial;

    // The particles this channel draws: the ash's of its material, or the
    // manager's flakes when this volume holds them.
    private IEnumerable<SimpleParticle> ChannelParticles(int channel)
    {
        if (_volume is null)
            return [];
        if (_volume.Type == PrecipitationType.Ash)
            return _volume.AshEmitter?.Particles.Where(p => p.Material == channel) ?? [];
        if (_volume.Type == PrecipitationType.SnowFall && weather.System.SnowFallHolder == _volume)
            return weather.System.SnowFall?.Particles ?? (IEnumerable<SimpleParticle>)[];
        return [];
    }

    int IManagedParticleSource.Emit(int channel, ref ParticleEmit emit, Span<int> idsOut)
    {
        if (channel < 0 || channel >= _channels)
            return -1;

        int n = 0;
        Vector mins = default, maxs = default;
        bool any = false;
        foreach (SimpleParticle p in ChannelParticles(channel))
        {
            if (n < idsOut.Length && _handed.Add(p.Id))
                idsOut[n++] = p.Id;
            Grow(ref mins, ref maxs, p.Position, !any);
            any = true;
        }

        emit.HasBounds = any ? 1 : 0;
        emit.Mins = mins;
        emit.Maxs = maxs;
        return n;
    }

    int IManagedParticleSource.Simulate(int channel, float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut)
    {
        if (channel < 0 || channel >= _channels || _volume is null)
            return -1;

        // The particle manager's step, once a frame for the whole volume.
        float now = clock.Now;
        if (now != _simulatedAt)
        {
            _simulatedAt = now;
            if (_volume.Type == PrecipitationType.Ash)
                _volume.SimulateAsh(now, dt);
            else if (weather.System.SnowFallHolder == _volume)
            {
                _died.Clear();
                weather.System.SnowFall?.Simulate(now, dt, _died);
            }
        }

        var alive = ChannelParticles(channel).Select(p => p.Id).ToHashSet();
        for (int i = 0; i < ids.Length; i++)
        {
            aliveOut[i] = alive.Contains(ids[i]) ? (byte)1 : (byte)0;
            if (aliveOut[i] == 0)
                _handed.Remove(ids[i]);
        }

        return 1;
    }

    int IManagedParticleSource.Draw(int channel, Span<ParticleQuadRaw> quadsOut)
    {
        if (channel < 0 || channel >= _channels || _volume is null)
            return -1;

        SimpleEmitter drawer = _volume.Type == PrecipitationType.Ash && _volume.AshEmitter is { } ash ? ash : _flakeDraw;
        int n = 0;
        foreach (SimpleParticle p in ChannelParticles(channel))
        {
            if (n < quadsOut.Length)
                quadsOut[n] = drawer.Draw(p).ToRaw(p.Id);
            n++;
        }

        return n;
    }

    private static void Grow(ref Vector mins, ref Vector maxs, Vector at, bool first)
    {
        mins = first ? at : ParticleMath.Min(mins, at);
        maxs = first ? at : ParticleMath.Max(maxs, at);
    }
}
