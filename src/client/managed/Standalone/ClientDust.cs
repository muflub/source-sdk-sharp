using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of func_dustmotes and func_dustcloud: it owns the motes
/// (<see cref="DustVolumeSim"/>, lane c5-particles), drawn by the native
/// particle executor on one channel (<c>particle/sparkles</c>).
///
/// <para>
/// The motes go INSIDE the brush: each spawn event tries random points of the
/// model's box and keeps the first the brush is solid at, a question native
/// carries out over this entity's own brush
/// (<see cref="IClientBrushContents"/>). The box is the model's bounds, read when
/// the channel is made; a volume with no model emits nothing and is not made.
/// </para>
///
/// <para>
/// The dust is SERVER-DRIVEN, which stock's is not: stock builds its volumes
/// from the map's entity lump at load and they are fixed for the level, while
/// one of these can be created, moved, recoloured or switched off at runtime
/// because a managed entity decided to. Every update re-arms the spawner at
/// the rate, as stock's OnDataChanged did; the frozen batch is owed on the
/// first.
/// </para>
/// </summary>
public abstract class ClientDustBase : ClientStandaloneEntity, IManagedParticleSource, ISaveCustom, IDustBrushPointTest
{
    protected ClientDustBase(IEngineEntityRender render, IClientParticleExecutor executor, IClientBrushContents brush,
                             IClientWinds winds, ParticleViews views, IRandomSeeds seeds,
                             IEngineWorldQuery world, IEngineEntities entities)
        : base(render)
    {
        _executor = executor;
        _brush = brush;
        _winds = winds;
        _views = views;
        _world = world;
        _entities = entities;
        _random = new ParticleStream(seeds.Next());
    }

    private const int Channel = 0;

    private readonly IClientParticleExecutor _executor;
    private readonly IClientBrushContents _brush;
    private readonly IClientWinds _winds;
    private readonly ParticleViews _views;
    private readonly IEngineWorldQuery _world;
    private readonly IEngineEntities _entities;
    private readonly IParticleRandom _random;
    private readonly List<int> _added = new();
    private bool _created;
    private float _lastFrame = float.NaN;
    private float _frameTime;

    public DustVolumeSim Sim { get; } = new();

    /// <summary>Whether the channel exists (the first update with a model made it).</summary>
    public bool Created => _created;

    /// <summary>The two classnames' tables, read through one interface.</summary>
    protected abstract IDustNetState Dust { get; }

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        var d = Dust;
        Sim.R = (byte)d.Red; Sim.G = (byte)d.Green; Sim.B = (byte)d.Blue; Sim.A = (byte)d.Alpha;
        Sim.SpawnRate = d.SpawnRate;
        Sim.SizeMin = d.SizeMin; Sim.SizeMax = d.SizeMax;
        Sim.SpeedMax = d.SpeedMax;
        Sim.LifetimeMin = d.LifetimeMin; Sim.LifetimeMax = d.LifetimeMax;
        Sim.DistMax = d.DistMax;
        Sim.FallSpeed = d.FallSpeed;
        Sim.Flags = d.DustFlags;

        bool created = false;
        if (!_created)
        {
            if (!_world.ClientEntityModelBounds(_entities.GetIndex(Handle), out Vector mins, out Vector maxs, out _))
                return;
            Sim.VolumeMins = mins;
            Sim.VolumeMaxs = maxs;
            _created = _executor.Create(Handle, Channel, DustVolumeSim.Material, 0f, alwaysSimulate: true);
            if (!_created)
                return;
            created = true;
        }

        Sim.ApplyUpdate(created);
    }

    /// <summary>The motes go when the volume does.</summary>
    public override void OnRemoved()
    {
        if (_created)
            _executor.Destroy(Handle, Channel);
        _created = false;
    }

    int IDustBrushPointTest.PointContents(Vector point) => _brush.PointContents(Handle, point);

    int IManagedParticleSource.Emit(int channel, ref ParticleEmit emit, Span<int> idsOut)
    {
        if (channel != Channel)
            return -1;

        _added.Clear();
        if (emit.CurTime != _lastFrame)
        {
            _lastFrame = emit.CurTime;
            _frameTime = emit.FrameTime;
            Ticks++;
            Sim.Tick(emit.FrameTime, emit.Origin, this, _random, _added);
        }

        Sim.Bounds(out Vector mins, out Vector maxs);
        emit.HasBounds = 1;
        emit.Mins = mins;
        emit.Maxs = maxs;

        int n = Math.Min(_added.Count, idsOut.Length);
        for (int k = 0; k < n; k++)
            idsOut[k] = _added[k];
        return n;
    }

    int IManagedParticleSource.Simulate(int channel, float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut)
    {
        if (channel != Channel)
            return -1;
        Steps++;
        Sim.Simulate(dt, _frameTime, _winds.Windspeed);
        for (int i = 0; i < ids.Length; i++)
            aliveOut[i] = Sim.Holds(ids[i]) ? (byte)1 : (byte)0;
        return 1;
    }

    int IManagedParticleSource.Draw(int channel, Span<ParticleQuadRaw> quadsOut)
    {
        if (channel != Channel)
            return -1;
        int n = Sim.Draw(_views.Current, quadsOut);
        if (n <= quadsOut.Length)
        {
            Draws++;
            QuadsDrawn += n;
        }
        return n;
    }

    /// <summary>Frames ticked (spawns) and steps simulated (cs_cl_dust_report): one each a frame when drawn.</summary>
    public int Ticks { get; private set; }

    public int Steps { get; private set; }

    /// <summary>Draws answered, and the mote quads they held (cs_cl_dust_report).</summary>
    public int Draws { get; private set; }

    public long QuadsDrawn { get; private set; }

    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        using var buffer = new MemoryStream();
        Sim.Save(new BinaryWriter(buffer));
        writer.Write(ClassName + ".sim", Convert.ToBase64String(buffer.ToArray()));
        writer.Write(ClassName + ".created", _created);
    }

    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        string carried = reader.ReadString(ClassName + ".sim");
        if (carried.Length == 0)
            return;
        Sim.Restore(new BinaryReader(new MemoryStream(Convert.FromBase64String(carried))));
        _created = reader.ReadBool(ClassName + ".created");
    }
}

/// <summary>func_dustmotes: motes that keep their size on screen.</summary>
[ClientStandaloneEntity(EntityNames.FuncDustmotes)]
public sealed class ClientDustMotes : ClientDustBase
{
    public ClientDustMotes(IEngineEntityRender render, IClientParticleExecutor executor, IClientBrushContents brush,
                           IClientWinds winds, ParticleViews views, IRandomSeeds seeds,
                           IEngineWorldQuery world, IEngineEntities entities)
        : base(render, executor, brush, winds, views, seeds, world, entities)
    {
    }

    public override string ClassName => EntityNames.FuncDustmotes;

    [NetState] public DustMotesNetState State = new();

    protected override IDustNetState Dust => State;
}

/// <summary>func_dustcloud: motes that shrink with distance like anything else.</summary>
[ClientStandaloneEntity(EntityNames.FuncDustcloud)]
public sealed class ClientDustCloud : ClientDustBase
{
    public ClientDustCloud(IEngineEntityRender render, IClientParticleExecutor executor, IClientBrushContents brush,
                           IClientWinds winds, ParticleViews views, IRandomSeeds seeds,
                           IEngineWorldQuery world, IEngineEntities entities)
        : base(render, executor, brush, winds, views, seeds, world, entities)
    {
    }

    public override string ClassName => EntityNames.FuncDustcloud;

    [NetState] public DustCloudNetState State = new();

    protected override IDustNetState Dust => State;
}

/// <summary>
/// What the dust volumes are doing (step 7a's report, managed since the motes
/// moved to C#): each volume's box, its live motes, its spawn counters and its
/// draws. A zero in the 7a gate's mote row has four causes (no volume, no
/// point inside the brush, no draw, every mote past distmax) and this tells
/// them apart.
/// </summary>
[ConCommand("cs_cl_dust_report",
            Help = "SourceSharp: the managed dust volumes this client holds, and their live particles.")]
public sealed class ClientDustReportCommand(ILog log, IClientStandalones standalones) : IConCommand
{
    public void Execute(string args)
    {
        var volumes = standalones.Live().OfType<ClientDustBase>().ToList();
        log.Msg($"[SourceSharp/Client] managed dust volumes: {volumes.Count}  " +
                $"({volumes.Count(v => v.Created)} with a channel)\n");
        for (int i = 0; i < volumes.Count; i++)
        {
            ClientDustBase v = volumes[i];
            DustVolumeSim sim = v.Sim;
            log.Msg($"  [{i}] {v.ClassName} handle {v.Handle.Value}  origin ({sim.Origin.X:0} {sim.Origin.Y:0} {sim.Origin.Z:0})  " +
                    $"brush ({sim.VolumeMins.X:0} {sim.VolumeMins.Y:0} {sim.VolumeMins.Z:0})..({sim.VolumeMaxs.X:0} {sim.VolumeMaxs.Y:0} {sim.VolumeMaxs.Z:0})\n" +
                    $"       {sim.Motes.Count} live particle(s), spawn rate {sim.SpawnRate}, flags 0x{sim.Flags:x}, distmax {sim.DistMax}\n" +
                    $"       spawn: {sim.SpawnCalls} call(s), {sim.SpawnSolid} found solid, {sim.SpawnMade} particle(s) made\n" +
                    $"       tick: {v.Ticks} tick(s), {v.Steps} step(s); draw: {v.Draws} draw(s), {v.QuadsDrawn} mote quad(s)\n");
        }
    }
}
