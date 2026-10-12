using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client halves of the six classes in <c>smoke_trail.cpp</c>, each
/// simulated here (the trail sims in Effects, lane c2-sims) and drawn by the
/// native particle executor: one executor channel per material, as stock's
/// emitters keep one list per material.
///
/// <para>
/// A frame runs the sim once, on whichever channel the particle manager asks
/// first (the frame is told by its curtime), and hands each channel the ids of
/// the particles that frame added to it. Each channel's simulate steps only its
/// own particles and its draw returns only theirs, newest (highest id) first.
/// The bounds handed back each frame are the channel's particles', so a trail
/// that has moved on is not culled by where its owner is.
/// </para>
///
/// <para>
/// Stock's <c>Start</c> runs on the first frame, from the place the executor
/// reports: what stock read from <c>GetAbsOrigin</c> when the entity was made.
/// Every trail carries its sim through <c>cs_cl_reload</c> (<see cref="ISaveCustom"/>):
/// the executor's effects are native and outlive the assembly, so a restored
/// trail keeps feeding the channels it made.
/// </para>
/// </summary>
public abstract class ClientTrailBase : ClientStandaloneEntity, IManagedParticleSource, ISaveCustom
{
    protected ClientTrailBase(IEngineEntityRender render, IClientParticleExecutor executor, IRandomSeeds seeds)
        : base(render)
    {
        _executor = executor;
        Random = new ParticleStream(seeds.Next());
    }

    private readonly IClientParticleExecutor _executor;
    private List<int>[]? _pending;
    private readonly List<(int Channel, int Id)> _added = new();
    private bool _created;
    private bool _started;
    private float _lastFrame = float.NaN;

    protected readonly IParticleRandom Random;

    /// <summary>Whether the channels exist (the first update made them).</summary>
    public bool Created => _created;

    /// <summary>How many executor channels (materials) the trail draws with.</summary>
    public abstract int ChannelCount { get; }

    public abstract string MaterialOf(int channel);

    /// <summary>The replicated numbers into the sim. <paramref name="first"/>: the update that made the channels.</summary>
    protected abstract void Feed(bool first);

    /// <summary>Stock's <c>Start</c>, at the trail's first frame.</summary>
    protected abstract void Start(Vector origin);

    /// <summary>One frame of stock's <c>Update</c>: each added particle by channel.</summary>
    protected abstract void Step(in ParticleEmit emit, List<(int Channel, int Id)> added);

    protected abstract void SimulateChannel(int channel, float dt);

    protected abstract bool Holds(int channel, int id);

    /// <summary>The channel's particles' quads, newest first; the count, or the count needed when it exceeds the span.</summary>
    protected abstract int DrawChannel(int channel, Span<ParticleQuadRaw> quadsOut, out Vector mins, out Vector maxs);

    protected abstract void SaveSim(BinaryWriter w);

    protected abstract void RestoreSim(BinaryReader r);

    /// <summary>Whether the channels may be made now (an effect that needs its model says no without one).</summary>
    protected virtual bool CanCreate() => true;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        bool first = !_created;
        if (first)
        {
            if (!CanCreate())
                return;
            _created = true;
            for (int c = 0; c < ChannelCount && _created; c++)
                _created = _executor.Create(Handle, c, MaterialOf(c), 0f, alwaysSimulate: true);
            if (!_created)
            {
                for (int c = 0; c < ChannelCount; c++)
                    _executor.Destroy(Handle, c);
                return;
            }
        }

        Feed(first);
    }

    public override void OnRemoved()
    {
        if (_created)
        {
            for (int c = 0; c < ChannelCount; c++)
                _executor.Destroy(Handle, c);
        }

        _created = false;
    }

    int IManagedParticleSource.Emit(int channel, ref ParticleEmit emit, Span<int> idsOut)
    {
        if (channel < 0 || channel >= ChannelCount)
            return -1;

        _pending ??= Enumerable.Range(0, ChannelCount).Select(_ => new List<int>()).ToArray();
        if (emit.CurTime != _lastFrame)
        {
            _lastFrame = emit.CurTime;
            foreach (var p in _pending)
                p.Clear();
            if (!_started)
            {
                _started = true;
                Start(emit.Origin);
            }

            _added.Clear();
            Step(emit, _added);
            foreach (var (c, id) in _added)
                _pending[c].Add(id);
        }

        var mine = _pending[channel];
        int n = Math.Min(mine.Count, idsOut.Length);
        for (int k = 0; k < n; k++)
            idsOut[k] = mine[k];
        mine.Clear();

        if (DrawChannel(channel, [], out Vector mins, out Vector maxs) > 0)
        {
            emit.HasBounds = 1;
            emit.Mins = mins;
            emit.Maxs = maxs;
        }
        else
            emit.HasBounds = 0;
        return n;
    }

    int IManagedParticleSource.Simulate(int channel, float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut)
    {
        if (channel < 0 || channel >= ChannelCount)
            return -1;
        SimulateChannel(channel, dt);
        for (int i = 0; i < ids.Length; i++)
            aliveOut[i] = Holds(channel, ids[i]) ? (byte)1 : (byte)0;
        return 1;
    }

    int IManagedParticleSource.Draw(int channel, Span<ParticleQuadRaw> quadsOut)
        => channel < 0 || channel >= ChannelCount ? -1 : DrawChannel(channel, quadsOut, out _, out _);

    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        using var buffer = new MemoryStream();
        SaveSim(new BinaryWriter(buffer));
        writer.Write(ClassName + ".sim", Convert.ToBase64String(buffer.ToArray()));
        writer.Write(ClassName + ".created", _created);
        writer.Write(ClassName + ".started", _started);
    }

    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        string carried = reader.ReadString(ClassName + ".sim");
        if (carried.Length == 0)
            return;
        RestoreSim(new BinaryReader(new MemoryStream(Convert.FromBase64String(carried))));
        _created = reader.ReadBool(ClassName + ".created");
        _started = reader.ReadBool(ClassName + ".started");
    }

    /// <summary>A <see cref="SimpleEmitter"/>'s particles of one material as quads, newest first, with their bounds.</summary>
    protected static int DrawLane(SimpleEmitter emitter, int material, Span<ParticleQuadRaw> quadsOut, out Vector mins, out Vector maxs)
    {
        int n = 0;
        mins = default;
        maxs = default;
        foreach (var p in emitter.Particles)
        {
            if (p.Material != material)
                continue;
            ParticleQuad q = emitter.Draw(p);
            Grow(ref mins, ref maxs, q.Position, q.Size, n == 0);
            if (n < quadsOut.Length)
                quadsOut[n] = q.ToRaw(p.Id);
            n++;
        }

        return n;
    }

    protected static void Grow(ref Vector mins, ref Vector maxs, Vector at, float size, bool first)
    {
        Vector lo = new(at.X - size, at.Y - size, at.Z - size);
        Vector hi = new(at.X + size, at.Y + size, at.Z + size);
        mins = first ? lo : ParticleMath.Min(mins, lo);
        maxs = first ? hi : ParticleMath.Max(maxs, hi);
    }
}

/// <summary>
/// env_fire_trail: flame and smoke filling the gap between where the entity was
/// last frame and where it is now (<see cref="FireTrailSim"/>), seven channels.
/// Its two numbers are not reachable from a map (both are <c>DEFINE_FIELD</c>
/// on stock's datadesc), and the attachment is read by nothing on the client.
/// </summary>
[ClientStandaloneEntity(EntityNames.EnvFireTrail)]
public sealed class ClientFireTrail(IEngineEntityRender render, IClientParticleExecutor executor, IRandomSeeds seeds)
    : ClientTrailBase(render, executor, seeds)
{
    private readonly List<SimpleParticle> _scratch = new();

    public override string ClassName => EntityNames.EnvFireTrail;

    [NetState] public FireTrailNetState State = new();

    public FireTrailSim Sim { get; } = new();

    public override int ChannelCount => FireTrailSim.MaterialCount;

    public override string MaterialOf(int channel) => FireTrailSim.MaterialName(channel);

    protected override void Feed(bool first) => Sim.Lifetime = State.Lifetime;

    protected override void Start(Vector origin) => Sim.Start(origin);

    protected override void Step(in ParticleEmit emit, List<(int Channel, int Id)> added)
    {
        _scratch.Clear();
        Sim.Emit(emit.CurTime, emit.Origin, Random, _scratch);
        foreach (var p in _scratch)
            added.Add((p.Material, p.Id));
    }

    protected override void SimulateChannel(int channel, float dt) => Sim.Emitter.Simulate(dt, Random, channel);

    protected override bool Holds(int channel, int id) => Sim.Emitter.IndexOf(id) >= 0;

    protected override int DrawChannel(int channel, Span<ParticleQuadRaw> quadsOut, out Vector mins, out Vector maxs)
        => DrawLane(Sim.Emitter, channel, quadsOut, out mins, out maxs);

    protected override void SaveSim(BinaryWriter w) => Sim.Save(w);

    protected override void RestoreSim(BinaryReader r) => Sim.Restore(r);
}

/// <summary>
/// env_smoketrail: a column of smoke (<see cref="SmokeTrailSim"/>), two
/// channels. The colours cross in 0-1 space: the space stock's server divides
/// the mapper's key into and the client multiplies straight into a puff. The
/// end colour is on the wire in stock and read by nothing.
/// </summary>
[ClientStandaloneEntity(EntityNames.EnvSmoketrail)]
public sealed class ClientSmokeTrail(IEngineEntityRender render, IClientParticleExecutor executor, IRandomSeeds seeds)
    : ClientTrailBase(render, executor, seeds)
{
    private readonly List<SimpleParticle> _scratch = new();

    public override string ClassName => EntityNames.EnvSmoketrail;

    [NetState] public SmokeTrailNetState State = new();

    public SmokeTrailSim Sim { get; } = new();

    public override int ChannelCount => SmokeTrailSim.MaterialCount;

    public override string MaterialOf(int channel) => SmokeTrailSim.MaterialName(channel);

    /// <summary>
    /// The numbers, then the rate: Start arms the timer from it, and every
    /// update after the first re-arms it through SetSpawnRate, as stock's
    /// OnDataChanged does.
    /// </summary>
    protected override void Feed(bool first)
    {
        Sim.StartColor = State.StartColor;
        Sim.Opacity = State.Opacity;
        Sim.ParticleLifetime = State.ParticleLifetime;
        Sim.StopEmitTime = State.StopEmitTime;
        Sim.MinSpeed = State.MinSpeed;
        Sim.MaxSpeed = State.MaxSpeed;
        Sim.MinDirectedSpeed = State.MinDirectedSpeed;
        Sim.MaxDirectedSpeed = State.MaxDirectedSpeed;
        Sim.StartSize = State.StartSize;
        Sim.EndSize = State.EndSize;
        Sim.SpawnRadius = State.SpawnRadius;
        Sim.Emitting = State.Emit;
        if (first)
            Sim.SpawnRate = State.SpawnRate;
        else
            Sim.SetSpawnRate(State.SpawnRate);
    }

    protected override void Start(Vector origin) => Sim.Start();

    /// <summary>Stock reads <c>GetAbsVelocity</c>; a managed trail's is never set, so zero, as the shell it replaces had it.</summary>
    protected override void Step(in ParticleEmit emit, List<(int Channel, int Id)> added)
    {
        _scratch.Clear();
        Sim.Emit(emit.Dt, emit.CurTime, emit.Origin, emit.Angles, default, Random, _scratch);
        foreach (var p in _scratch)
            added.Add((p.Material, p.Id));
    }

    protected override void SimulateChannel(int channel, float dt) => Sim.Emitter.Simulate(dt, Random, channel);

    protected override bool Holds(int channel, int id) => Sim.Emitter.IndexOf(id) >= 0;

    protected override int DrawChannel(int channel, Span<ParticleQuadRaw> quadsOut, out Vector mins, out Vector maxs)
        => DrawLane(Sim.Emitter, channel, quadsOut, out mins, out maxs);

    protected override void SaveSim(BinaryWriter w) => Sim.Save(w);

    protected override void RestoreSim(BinaryReader r) => Sim.Restore(r);
}

/// <summary>
/// env_dusttrail: <see cref="ClientSmokeTrail"/>'s twin with one colour and one
/// channel (<see cref="DustTrailSim"/>). Stock's emit start time is its
/// constructor's curtime: here, the update that made the channel.
/// </summary>
[ClientStandaloneEntity(EntityNames.EnvDusttrail)]
public sealed class ClientDustTrail(IEngineEntityRender render, IClientParticleExecutor executor, IRandomSeeds seeds, IGameClock clock)
    : ClientTrailBase(render, executor, seeds)
{
    private readonly List<SimpleParticle> _scratch = new();

    public override string ClassName => EntityNames.EnvDusttrail;

    [NetState] public DustTrailNetState State = new();

    public DustTrailSim Sim { get; } = new();

    public override int ChannelCount => 1;

    public override string MaterialOf(int channel) => DustTrailSim.Material;

    protected override void Feed(bool first)
    {
        Sim.Color = State.Color;
        Sim.Opacity = State.Opacity;
        Sim.ParticleLifetime = State.ParticleLifetime;
        Sim.StopEmitTime = State.StopEmitTime;
        Sim.MinSpeed = State.MinSpeed;
        Sim.MaxSpeed = State.MaxSpeed;
        Sim.MinDirectedSpeed = State.MinDirectedSpeed;
        Sim.MaxDirectedSpeed = State.MaxDirectedSpeed;
        Sim.StartSize = State.StartSize;
        Sim.EndSize = State.EndSize;
        Sim.SpawnRadius = State.SpawnRadius;
        Sim.Emitting = State.Emit;
        if (first)
        {
            Sim.StartEmitTime = clock.Now;
            Sim.SpawnRate = State.SpawnRate;
        }
        else
            Sim.SetSpawnRate(State.SpawnRate);
    }

    protected override void Start(Vector origin) => Sim.Start();

    protected override void Step(in ParticleEmit emit, List<(int Channel, int Id)> added)
    {
        _scratch.Clear();
        Sim.Emit(emit.Dt, emit.CurTime, emit.Origin, emit.Angles, default, Random, _scratch);
        foreach (var p in _scratch)
            added.Add((0, p.Id));
    }

    protected override void SimulateChannel(int channel, float dt) => Sim.Emitter.Simulate(dt, Random);

    protected override bool Holds(int channel, int id) => Sim.Emitter.IndexOf(id) >= 0;

    protected override int DrawChannel(int channel, Span<ParticleQuadRaw> quadsOut, out Vector mins, out Vector maxs)
        => DrawLane(Sim.Emitter, 0, quadsOut, out mins, out maxs);

    protected override void SaveSim(BinaryWriter w) => Sim.Save(w);

    protected override void RestoreSim(BinaryReader r) => Sim.Restore(r);
}

/// <summary>
/// env_rockettrail: smoke with a flare at the nozzle (<see cref="RocketTrailSim"/>).
/// Ten channels: the two puff materials (0, 1), the four flash sprites (2..5)
/// and the four ember flames (6..9). <c>RocketTrail</c> has no map key; what it
/// networks is stock's constructor.
/// </summary>
[ClientStandaloneEntity(EntityNames.EnvRockettrail)]
public sealed class ClientRocketTrail(IEngineEntityRender render, IClientParticleExecutor executor, IRandomSeeds seeds)
    : ClientTrailBase(render, executor, seeds)
{
    private readonly List<SimpleParticle> _flash = new(), _puffs = new(), _embers = new();

    public override string ClassName => EntityNames.EnvRockettrail;

    [NetState] public RocketTrailNetState State = new();

    public RocketTrailSim Sim { get; } = new();

    public override int ChannelCount => 10;

    public override string MaterialOf(int channel) => channel switch
    {
        < 2 => RocketTrailSim.PuffMaterial(channel),
        < 6 => RocketTrailSim.FlashMaterial(channel - 1),
        _ => RocketTrailSim.EmberMaterial(channel - 5),
    };

    private (SimpleEmitter Emitter, int Material) Lane(int channel) => channel switch
    {
        < 2 => (Sim.Puffs, channel),
        < 6 => (Sim.Flash, channel - 1),
        _ => (Sim.Embers, channel - 5),
    };

    protected override void Feed(bool first)
    {
        Sim.StartColor = State.StartColor;
        Sim.Opacity = State.Opacity;
        Sim.ParticleLifetime = State.ParticleLifetime;
        Sim.StopEmitTime = State.StopEmitTime;
        Sim.MinSpeed = State.MinSpeed;
        Sim.MaxSpeed = State.MaxSpeed;
        Sim.StartSize = State.StartSize;
        Sim.EndSize = State.EndSize;
        Sim.SpawnRadius = State.SpawnRadius;
        Sim.Emitting = State.Emit;
        Sim.Damaged = State.Damaged;
        Sim.FlareScale = State.FlareScale;
        Sim.SpawnRate = State.SpawnRate;
    }

    protected override void Start(Vector origin) => Sim.Start(origin);

    protected override void Step(in ParticleEmit emit, List<(int Channel, int Id)> added)
    {
        _flash.Clear();
        _puffs.Clear();
        _embers.Clear();
        Sim.Emit(emit.FrameTime, emit.Origin, emit.Angles, Random, _flash, _puffs, _embers);
        foreach (var p in _puffs)
            added.Add((p.Material, p.Id));
        foreach (var p in _flash)
            added.Add((1 + p.Material, p.Id));
        foreach (var p in _embers)
            added.Add((5 + p.Material, p.Id));
    }

    /// <summary>The flash and embers born this frame are not stepped (stock's fresh emitters skip their first frame).</summary>
    protected override void SimulateChannel(int channel, float dt)
    {
        var (emitter, material) = Lane(channel);
        emitter.Simulate(dt, Random, material, channel < 2 ? int.MaxValue : Sim.FrameFirstId);
    }

    protected override bool Holds(int channel, int id) => Lane(channel).Emitter.IndexOf(id) >= 0;

    protected override int DrawChannel(int channel, Span<ParticleQuadRaw> quadsOut, out Vector mins, out Vector maxs)
    {
        var (emitter, material) = Lane(channel);
        return DrawLane(emitter, material, quadsOut, out mins, out maxs);
    }

    protected override void SaveSim(BinaryWriter w) => Sim.Save(w);

    protected override void RestoreSim(BinaryReader r) => Sim.Restore(r);
}

/// <summary>
/// env_sporetrail: a thin trail of embers in the trail's own space, drawn
/// through its place (<see cref="SporeTrailSim"/>). Inert as a map places one,
/// in stock as here: nothing raises its emit flag (see EnvSporeTrail). The
/// colour, rate and sizes it networks are stock's wire and are not read.
/// </summary>
[ClientStandaloneEntity(EntityNames.EnvSporetrail)]
public sealed class ClientSporeTrail(IEngineEntityRender render, IClientParticleExecutor executor, IRandomSeeds seeds)
    : ClientTrailBase(render, executor, seeds)
{
    private readonly List<SimpleParticle> _scratch = new();
    private readonly float[] _attachment = new float[12];

    public override string ClassName => EntityNames.EnvSporetrail;

    [NetState] public SporeTrailNetState State = new();

    public SporeTrailSim Sim { get; } = new();

    public override int ChannelCount => 1;

    public override string MaterialOf(int channel) => SporeTrailSim.Material;

    protected override void Feed(bool first) => Sim.Emitting = State.Emit;

    protected override void Start(Vector origin) => Sim.Start();

    protected override void Step(in ParticleEmit emit, List<(int Channel, int Id)> added)
    {
        SporeTrailSim.AttachmentMatrix(emit.Angles, emit.Origin, _attachment);
        _scratch.Clear();
        Sim.Emit(emit.Dt, Random, _scratch);
        foreach (var p in _scratch)
            added.Add((0, p.Id));
    }

    protected override void SimulateChannel(int channel, float dt) => Sim.Simulate(dt);

    protected override bool Holds(int channel, int id) => Sim.IndexOf(id) >= 0;

    protected override int DrawChannel(int channel, Span<ParticleQuadRaw> quadsOut, out Vector mins, out Vector maxs)
    {
        mins = default;
        maxs = default;
        if (!Sim.Emitting)
            return 0;
        int n = 0;
        foreach (var p in Sim.Particles)
        {
            ParticleQuad q = SporeTrailSim.Draw(p, _attachment);
            Grow(ref mins, ref maxs, q.Position, q.Size, n == 0);
            if (n < quadsOut.Length)
                quadsOut[n] = q.ToRaw(p.Id);
            n++;
        }

        return n;
    }

    protected override void SaveSim(BinaryWriter w)
    {
        Sim.Save(w);
        foreach (float f in _attachment)
            w.Write(f);
    }

    protected override void RestoreSim(BinaryReader r)
    {
        Sim.Restore(r);
        for (int i = 0; i < _attachment.Length; i++)
            _attachment[i] = r.ReadSingle();
    }
}

/// <summary>
/// env_sporeexplosion: a cloud of spores a map switches on and off
/// (<see cref="SporeExplosionSim"/>), at half rate beyond
/// <c>cl_sporeclipdistance</c> of the view. Stock's OnDataChanged re-arms the
/// rate on creation and on any later update that finds it emitting (a map's
/// Enable), and so does this.
/// </summary>
[ClientStandaloneEntity(EntityNames.EnvSporeexplosion)]
public sealed class ClientSporeExplosion(IEngineEntityRender render, IClientParticleExecutor executor, IRandomSeeds seeds, IEngineConsole console)
    : ClientTrailBase(render, executor, seeds)
{
    private readonly List<SimpleParticle> _scratch = new();

    public override string ClassName => EntityNames.EnvSporeexplosion;

    [NetState] public SporeExplosionNetState State = new();

    public SporeExplosionSim Sim { get; } = new();

    public override int ChannelCount => SporeExplosionSim.MaterialCount;

    public override string MaterialOf(int channel) => SporeExplosionSim.MaterialName(channel);

    protected override void Feed(bool first)
    {
        Sim.SpawnRate = State.SpawnRate;
        Sim.ParticleLifetime = State.ParticleLifetime;
        Sim.StartSize = State.StartSize;
        Sim.EndSize = State.EndSize;
        Sim.SpawnRadius = State.SpawnRadius;
        Sim.Emitting = State.Emit;
        if (first || Sim.Emitting)
            Sim.Arm(State.SpawnRate);
    }

    protected override void Start(Vector origin)
    {
    }

    private float ClipDistance
        => float.TryParse(console.FindConVarValue("cl_sporeclipdistance"), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 512f;

    protected override void Step(in ParticleEmit emit, List<(int Channel, int Id)> added)
    {
        _scratch.Clear();
        Sim.Emit(emit.Dt, emit.Origin, emit.Angles, emit.ViewOrigin, ClipDistance, Random, _scratch);
        foreach (var p in _scratch)
            added.Add((p.Material, p.Id));
    }

    protected override void SimulateChannel(int channel, float dt) => Sim.Emitter.Simulate(dt, Random, channel);

    protected override bool Holds(int channel, int id) => Sim.Emitter.IndexOf(id) >= 0;

    protected override int DrawChannel(int channel, Span<ParticleQuadRaw> quadsOut, out Vector mins, out Vector maxs)
        => DrawLane(Sim.Emitter, channel, quadsOut, out mins, out maxs);

    protected override void SaveSim(BinaryWriter w) => Sim.Save(w);

    protected override void RestoreSim(BinaryReader r) => Sim.Restore(r);
}
