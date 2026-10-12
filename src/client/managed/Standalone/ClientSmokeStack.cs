using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of <c>env_smokestack</c>.
///
/// <para>
/// It hands over nineteen values and draws nothing. What draws is a column of
/// puffs lit by an inverse-square falloff from two point lights, twisted about
/// its own axis and pushed sideways by a wind vector — and the two lights are
/// the part that is not a mapper's key at all: the server copies them out of the
/// <c>env_particlelight</c> entities that name the stack. See
/// <see cref="Server.EnvSmokeStack"/>.
/// </para>
///
/// <para>
/// It exists for the reason the whole particle and trail family turns on. A
/// managed entity is never on the wire as <c>CSmokeStack</c> — it arrives as one
/// of the six generic tier tables in <c>generic_nettable.h</c> — so
/// <c>DT_SmokeStack</c> stops being sent the moment the classname moves and the
/// <c>C_SmokeStack</c> that draws is never built. Since lane c2-sims
/// (plan_client_novalve C2) the whole simulation is <see cref="SmokeStackSim"/>,
/// owned here; what is native is the draw executor (shell_managed_particles.cpp)
/// with the sphere renderer the lights feed, which follows this entity every
/// frame and asks it, on channel 0, for stock's three steps.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.EnvSmokestack)]
public sealed class ClientSmokeStack : ClientStandaloneEntity, IManagedParticleSource, ISaveCustom
{
    public ClientSmokeStack(IEngineEntityRender render, IClientParticleExecutor executor,
                            IClientModelNames models, IGameClock clock, IRandomSeeds seeds)
        : base(render)
    {
        _executor = executor;
        _models = models;
        _clock = clock;
        _random = new ParticleStream(seeds.Next());
    }

    private readonly IClientParticleExecutor _executor;
    private readonly IClientModelNames _models;
    private readonly IGameClock _clock;
    private readonly IParticleRandom _random;
    private readonly SmokeStackSim _sim = new();
    private bool _created;

    // Whether the material lights on the GPU ($USINGPIXELSHADER), which skips
    // the directional light in the quads' colour. Asked of the executor on the
    // first draw, so a reload asks again rather than carrying it.
    private bool? _usingPixelShaders;

    private const int Channel = 0;

    public override string ClassName => EntityNames.EnvSmokestack;

    [NetState] public SmokeStackNetState State = new();

    /// <summary>The simulation, for the report and the tests.</summary>
    public SmokeStackSim Sim => _sim;

    /// <summary>
    /// Stock's SourceSharp_UpdateSmokeStack: the members, the lights, Start on
    /// the first update (material from the networked model, five seconds of
    /// always-simulate from now), the lifetime re-derived on every one.
    /// </summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        _sim.SpreadSpeed = State.SpreadSpeed;
        _sim.Speed = State.Speed;
        _sim.StartSize = State.StartSize;
        _sim.EndSize = State.EndSize;
        _sim.Rate = State.Rate;
        _sim.JetLength = State.JetLength;
        _sim.RollSpeed = State.RollSpeed;
        _sim.BaseSpread = State.BaseSpread;
        _sim.Twist = State.Twist;
        _sim.Emitting = State.Emit;
        _sim.Wind = State.Wind;
        _sim.Alpha = State.Alpha;

        if (!_created)
        {
            // Stock's cull radius is the binding's default 0 (it never sets one).
            _created = _executor.Create(Handle, Channel, Material(State.MaterialModel), 0f, alwaysSimulate: true);
            if (_created)
                _sim.Start(_clock.Now);
        }

        if (_created)
        {
            Span<float> lights =
            [
                State.Red / 255.0f, State.Green / 255.0f, State.Blue / 255.0f,
                State.AmbientPos.X, State.AmbientPos.Y, State.AmbientPos.Z,
                State.AmbientColor.X, State.AmbientColor.Y, State.AmbientColor.Z, State.AmbientIntensity,
                State.DirPos.X, State.DirPos.Y, State.DirPos.Z,
                State.DirColor.X, State.DirColor.Y, State.DirColor.Z, State.DirIntensity,
            ];
            _executor.SetLights(Handle, Channel, lights);
        }

        _sim.Rederive();
    }

    /// <summary>Stock's material: the model's path with its ".vmt" cut, or "unset_material".</summary>
    private string Material(int modelIndex)
    {
        string? name = _models.NameOf(modelIndex);
        if (name is null)
            return "unset_material";
        int ext = name.IndexOf(".vmt", StringComparison.OrdinalIgnoreCase);
        return ext >= 0 ? name[..ext] : name;
    }

    /// <summary>The column goes when the entity does.</summary>
    public override void OnRemoved()
    {
        if (_created)
            _executor.Destroy(Handle, Channel);
        _created = false;
    }

    int IManagedParticleSource.Emit(int channel, ref ParticleEmit emit, Span<int> idsOut)
    {
        if (channel != Channel)
            return -1;
        int added = _sim.Emit(emit.Dt, emit.WasDrawnPrevFrame != 0, emit.CurTime, emit.FrameTime,
                              emit.Origin, emit.Angles, _random);
        emit.AlwaysSimulate = _sim.AlwaysSimulate ? 1 : 0;
        emit.HasBounds = 0;
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
        _usingPixelShaders ??= _executor.MaterialVarInt(Handle, Channel, "$USINGPIXELSHADER", out int ps) && ps != 0;
        var baseColor = new Vector(State.Red / 255.0f, State.Green / 255.0f, State.Blue / 255.0f);
        var ambient = new ParticleLight(State.AmbientPos, State.AmbientColor, State.AmbientIntensity);
        var directional = new ParticleLight(State.DirPos, State.DirColor, State.DirIntensity);
        for (int i = 0; i < puffs.Count; i++)
        {
            ParticleQuad q = _sim.Draw(puffs[i]);
            quadsOut[i] = new ParticleQuadRaw
            {
                Id = puffs[i].Id, Kind = (int)q.Kind, Position = q.Position,
                // CParticleSphereRenderer's lighting, 0..255 (SphereLighting).
                Color = SphereLighting.Lit(baseColor, ambient, directional, _usingPixelShaders.Value, q.Position),
                Alpha = q.Alpha, Size = q.Size, Roll = q.Roll,
            };
        }
        return puffs.Count;
    }

    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        using var buffer = new MemoryStream();
        _sim.Save(new BinaryWriter(buffer));
        writer.Write("smokestack.sim", Convert.ToBase64String(buffer.ToArray()));
        writer.Write("smokestack.created", _created);
    }

    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        string carried = reader.ReadString("smokestack.sim");
        if (carried.Length == 0)
            return;
        _sim.Restore(new BinaryReader(new MemoryStream(Convert.FromBase64String(carried))));
        _created = reader.ReadBool("smokestack.created");
    }
}
