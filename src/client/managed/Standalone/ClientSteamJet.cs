using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client halves of <c>env_steam</c> and <c>env_steamjet</c>.
///
/// <para>
/// Stock's jet is drawn by the client class the engine builds when it receives
/// <c>DT_SteamJet</c>, and a managed entity never arrives as that class. The
/// jet was therefore built client-side by native shells and fed from here;
/// since lane c2-sims (plan_client_novalve C2) its whole simulation is
/// <see cref="SteamJetSim"/>, owned by this entity, and what is native is the
/// draw executor (shell_managed_particles.cpp): a particle-manager effect that
/// asks this entity, on channel 0, for stock's Update, SimulateParticles and
/// RenderParticles in turn.
/// </para>
///
/// <para>
/// Re-read on every update rather than only the first, because four of the
/// values are live: JetLength, SpreadSpeed, Speed and Rate are
/// <c>DEFINE_INPUT</c> on stock's entity, and a <c>physics_cannister</c> drives
/// all four every time its thrust changes.
/// </para>
/// </summary>
public abstract class ClientSteamJetBase : ClientStandaloneEntity, IManagedParticleSource, ISaveCustom
{
    public ClientSteamJetBase(IEngineEntityRender render, IClientParticleExecutor executor,
                              IClientWorldLight light, IRandomSeeds seeds)
        : base(render)
    {
        _executor = executor;
        _light = light;
        _random = new ParticleStream(seeds.Next());
    }

    private readonly IClientParticleExecutor _executor;
    private readonly IClientWorldLight _light;
    private readonly IParticleRandom _random;
    private readonly SteamJetSim _sim = new();

    /// <summary>Whether the executor exists (it outlives a reload, so this is carried).</summary>
    private bool _created;

    /// <summary>Stock's <c>Start</c> waits for the executor's first Update, which brings the pose.</summary>
    private bool _startPending;

    /// <summary>The executor channel this jet draws on.</summary>
    private const int Channel = 0;

    /// <summary>The simulation, for the report and the tests.</summary>
    public SteamJetSim Sim => _sim;

    /// <summary>The two classnames' tables, read through one interface.</summary>
    protected abstract ISteamJetNetState Jet { get; }

    /// <summary>
    /// Everything the map decided, on every update: stock's
    /// SourceSharp_UpdateSteamJet, which set the members, started the jet on
    /// its first update and re-derived the lifetime and cull radius on every one.
    /// </summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        var j = Jet;

        _sim.SpreadSpeed = j.SpreadSpeed;
        _sim.Speed = j.Speed;
        _sim.StartSize = j.StartSize;
        _sim.EndSize = j.EndSize;
        _sim.Rate = j.Rate;
        _sim.JetLength = j.JetLength;
        _sim.Emitting = j.Emit;
        _sim.FaceLeft = j.FaceLeft;
        _sim.Type = j.Type;
        _sim.SpawnFlags = j.SpawnFlags;
        _sim.RollSpeed = j.RollSpeed;
        _sim.Red = (byte)j.Red;
        _sim.Green = (byte)j.Green;
        _sim.Blue = (byte)j.Blue;

        if (!_created)
        {
            _created = _executor.Create(Handle, Channel, _sim.Material, _sim.CullRadius, alwaysSimulate: false);
            _startPending = _created;
        }
        else
        {
            _executor.Configure(Handle, Channel, _sim.CullRadius);
        }

        _sim.Rederive();
    }

    /// <summary>The jet goes when the entity does.</summary>
    public override void OnRemoved()
    {
        if (_created)
            _executor.Destroy(Handle, Channel);
        _created = false;
    }

    private Vector LightAt(Vector at) => _light.LightForPoint(at, false);

    int IManagedParticleSource.Emit(int channel, ref ParticleEmit emit, Span<int> idsOut)
    {
        if (channel != Channel)
            return -1;

        if (_startPending)
        {
            _sim.Start(emit.Origin, emit.Angles, LightAt);
            _sim.Rederive();
            _startPending = false;
        }

        int added = _sim.Emit(emit.Dt, emit.WasDrawnPrevFrame != 0, emit.Origin, emit.Angles, _random, LightAt, out var bounds);

        emit.HasBounds = bounds is null ? 0 : 1;
        if (bounds is { } b)
        {
            emit.Mins = b.Min;
            emit.Maxs = b.Max;
        }

        // Newest first in the sim; native wants spawn order.
        int n = Math.Min(added, idsOut.Length);
        for (int k = 0; k < n; k++)
            idsOut[k] = _sim.Puffs[added - 1 - k].Id;
        return n;
    }

    int IManagedParticleSource.Simulate(int channel, float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut)
    {
        if (channel != Channel)
            return -1;
        if (!_sim.Simulate(dt, wasDrawnPrevFrame))
            return 0;
        for (int i = 0; i < ids.Length; i++)
            aliveOut[i] = _sim.IndexOf(ids[i]) >= 0 ? (byte)1 : (byte)0;
        return 1;
    }

    int IManagedParticleSource.Draw(int channel, Span<ParticleQuadRaw> quadsOut)
    {
        if (channel != Channel)
            return -1;
        var puffs = _sim.Puffs;
        if (puffs.Count > quadsOut.Length)
            return puffs.Count;
        for (int i = 0; i < puffs.Count; i++)
        {
            ParticleQuad q = _sim.Draw(puffs[i], _random);
            quadsOut[i] = new ParticleQuadRaw
            {
                Id = puffs[i].Id, Kind = (int)q.Kind, Position = q.Position, Color = q.Color,
                Alpha = q.Alpha, Size = q.Size, Roll = q.Roll, Normal = q.Normal,
            };
        }
        return puffs.Count;
    }

    // The puffs in flight, the timer and the ramp cross cs_cl_reload as bytes;
    // the executor (and its particles, keyed by the same ids) stays native.
    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        using var buffer = new MemoryStream();
        _sim.Save(new BinaryWriter(buffer));
        writer.Write("steamjet.sim", Convert.ToBase64String(buffer.ToArray()));
        writer.Write("steamjet.created", _created);
        writer.Write("steamjet.startPending", _startPending);
    }

    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        string carried = reader.ReadString("steamjet.sim");
        if (carried.Length == 0)
            return;
        _sim.Restore(new BinaryReader(new MemoryStream(Convert.FromBase64String(carried))));
        _created = reader.ReadBool("steamjet.created");
        _startPending = reader.ReadBool("steamjet.startPending");
    }
}

/// <summary>env_steam: points forward.</summary>
[ClientStandaloneEntity(EntityNames.EnvSteam)]
public sealed class ClientSteamJet : ClientSteamJetBase
{
    public ClientSteamJet(IEngineEntityRender render, IClientParticleExecutor executor,
                          IClientWorldLight light, IRandomSeeds seeds)
        : base(render, executor, light, seeds)
    {
    }

    public override string ClassName => EntityNames.EnvSteam;

    [NetState] public EnvSteamNetState State = new();

    protected override ISteamJetNetState Jet => State;
}

/// <summary>env_steamjet: the legacy name, which points left.</summary>
[ClientStandaloneEntity(EntityNames.EnvSteamjet)]
public sealed class ClientSteamJetLegacy : ClientSteamJetBase
{
    public ClientSteamJetLegacy(IEngineEntityRender render, IClientParticleExecutor executor,
                                IClientWorldLight light, IRandomSeeds seeds)
        : base(render, executor, light, seeds)
    {
    }

    public override string ClassName => EntityNames.EnvSteamjet;

    [NetState] public EnvSteamJetNetState State = new();

    protected override ISteamJetNetState Jet => State;
}
