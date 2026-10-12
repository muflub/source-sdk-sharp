using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of func_smokevolume.
///
/// <para>
/// It hands over ten numbers and a material name and draws nothing. What draws
/// is a 3D grid of puffs filling the brush, traded between neighbouring cells as
/// the smoke drifts, each one lit from the world, rotated, and built into a
/// mesh — six hundred lines of it, and every line the renderer.
/// </para>
///
/// <para>
/// Unlike the dust and the rain, three of the numbers are live: a map drives
/// SetDensity, SetRotationSpeed and SetMovementSpeed, and the density is the one
/// that matters — fading smoke in and out is what the ramp speed exists for.
/// </para>
///
/// <para>
/// Since lane c2-sims (plan_client_novalve C2) the whole simulation is
/// <see cref="SmokeVolumeSim"/>, owned here: the grid fill asks the world
/// through <see cref="SmokeVolumeSim.IWorld"/>, and the native draw executor
/// (shell_managed_particles.cpp) asks this entity, on channel 0, for stock's
/// three steps.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.FuncSmokevolume)]
public sealed class ClientSmokeVolume : ClientStandaloneEntity, IManagedParticleSource, ISaveCustom, SmokeVolumeSim.IWorld
{
    public ClientSmokeVolume(IEngineEntityRender render, IClientParticleExecutor executor, IEngineWorldQuery world,
                             IEngineEntities entities, IClientWorldLight light, IClientPlaneTrace trace,
                             IEngineConsole console, IRandomSeeds seeds)
        : base(render)
    {
        _executor = executor;
        _world = world;
        _entities = entities;
        _light = light;
        _trace = trace;
        _console = console;
        _random = new ParticleStream(seeds.Next());
    }

    private readonly IClientParticleExecutor _executor;
    private readonly IEngineWorldQuery _world;
    private readonly IEngineEntities _entities;
    private readonly IClientWorldLight _light;
    private readonly IClientPlaneTrace _trace;
    private readonly IEngineConsole _console;
    private readonly IParticleRandom _random;
    private readonly SmokeVolumeSim _sim = new();
    private bool _created;

    /// <summary>Puffs the fill made that the executor has not been handed yet.</summary>
    private readonly Queue<int> _unannounced = new();

    private const int Channel = 0;

    /// <summary><c>MASK_SOLID_BRUSHONLY</c>: solid, moveable, window, grate.</summary>
    private const int MaskSolidBrushOnly = 0x1 | 0x4000 | 0x2 | 0x8;

    public override string ClassName => EntityNames.FuncSmokevolume;

    [NetState] public SmokeVolumeNetState State = new();

    /// <summary>The simulation, for the report and the tests.</summary>
    public SmokeVolumeSim Sim => _sim;

    /// <summary>mat_reduceparticles is the engine's convar; read by name as stock's ConVarRef did.</summary>
    private bool ReduceParticles
        => float.TryParse(_console.FindConVarValue("mat_reduceparticles"), System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out float v) && (int)v != 0;

    /// <summary>
    /// Stock's SourceSharp_UpdateSmokeVolume: the members on every update, the
    /// volume built on the first one the brush and the spacing allow (its
    /// model's bounds; a spacing that is not a positive number builds nothing).
    /// </summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        _sim.Red1 = State.Red1; _sim.Green1 = State.Green1; _sim.Blue1 = State.Blue1;
        _sim.Red2 = State.Red2; _sim.Green2 = State.Green2; _sim.Blue2 = State.Blue2;
        _sim.ParticleDrawWidth = State.ParticleDrawWidth;
        _sim.ParticleSpacingDistance = State.ParticleSpacingDistance;
        _sim.DensityRampSpeed = State.DensityRampSpeed;
        _sim.RotationSpeed = State.RotationSpeed;
        _sim.MovementSpeed = State.MovementSpeed;
        _sim.Density = State.Density;
        _sim.SpawnFlags = State.SpawnFlags;

        if (!_created)
        {
            if (!(float.IsFinite(State.ParticleSpacingDistance) && State.ParticleSpacingDistance > 0))
                return;
            if (!_world.ClientEntityModelBounds(_entities.GetIndex(Handle), out Vector mins, out Vector maxs, out _))
                return;

            _sim.VolumeMins = mins;
            _sim.VolumeMaxs = maxs;
            _created = _executor.Create(Handle, Channel, State.Material, State.ParticleDrawWidth * 0.5f, alwaysSimulate: true);
            if (!_created)
                return;

            _sim.ApplyUpdate(created: true, ReduceParticles, this, _random);
            foreach (var puff in _sim.Puffs)
                _unannounced.Enqueue(puff.Id);
        }
        else
        {
            _sim.ApplyUpdate(created: false, ReduceParticles, this, _random);
        }

        _executor.Configure(Handle, Channel, _sim.ParticleRadius);
    }

    public override void OnRemoved()
    {
        if (_created)
            _executor.Destroy(Handle, Channel);
        _created = false;
    }

    int SmokeVolumeSim.IWorld.PointContents(Vector position) => (int)_world.PointContents(position);

    Vector SmokeVolumeSim.IWorld.Light(Vector position) => _light.LightForPoint(position, true);

    (float Fraction, Vector Normal, float PlaneDist) SmokeVolumeSim.IWorld.Trace(Vector start, Vector end)
        => _trace.TraceLine(start, end, MaskSolidBrushOnly);

    int IManagedParticleSource.Emit(int channel, ref ParticleEmit emit, Span<int> idsOut)
    {
        if (channel != Channel)
            return -1;

        _sim.Update(emit.Dt, emit.Origin, emit.Angles, _random, out var bounds);
        emit.HasBounds = bounds is null ? 0 : 1;
        if (bounds is { } b)
        {
            emit.Mins = b.Min;
            emit.Maxs = b.Max;
        }

        int n = 0;
        while (n < idsOut.Length && _unannounced.Count > 0)
            idsOut[n++] = _unannounced.Dequeue();
        return n;
    }

    int IManagedParticleSource.Simulate(int channel, float dt, bool wasDrawnPrevFrame, ReadOnlySpan<int> ids, Span<byte> aliveOut)
    {
        if (channel != Channel)
            return -1;
        if (!_sim.Simulate(dt))
            return 0;
        aliveOut.Fill(1);   // a smoke volume's puffs never die; they spin
        return 1;
    }

    int IManagedParticleSource.Draw(int channel, Span<ParticleQuadRaw> quadsOut)
    {
        if (channel != Channel)
            return -1;
        var puffs = _sim.Puffs;
        bool reduce = ReduceParticles;
        if (puffs.Count > quadsOut.Length)
            return puffs.Count;

        int n = 0;
        for (int i = puffs.Count - 1; i >= 0; i--)
        {
            if (_sim.Draw(puffs[i], reduce) is not { } q)
                continue;
            quadsOut[n++] = new ParticleQuadRaw
            {
                Id = puffs[i].Id, Kind = (int)q.Kind, Position = q.Position, Color = q.Color, Alpha = q.Alpha,
                Size = q.Size, Roll = q.Roll, FadeNear = q.FadeNear, FadeFar = q.FadeFar,
                FixedSort = q.FixedSortKey ? 1 : 0, SortKey = q.SortKey,
            };
        }
        return n;
    }

    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        using var buffer = new MemoryStream();
        var w = new BinaryWriter(buffer);
        _sim.Save(w);
        w.Write(_unannounced.Count);
        foreach (int id in _unannounced)
            w.Write(id);
        writer.Write("smokevolume.sim", Convert.ToBase64String(buffer.ToArray()));
        writer.Write("smokevolume.created", _created);
    }

    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        string carried = reader.ReadString("smokevolume.sim");
        if (carried.Length == 0)
            return;
        var r = new BinaryReader(new MemoryStream(Convert.FromBase64String(carried)));
        _sim.Restore(r);
        _unannounced.Clear();
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
            _unannounced.Enqueue(r.ReadInt32());
        _created = reader.ReadBool("smokevolume.created");
    }
}
