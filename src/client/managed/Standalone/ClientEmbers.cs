using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of <c>env_embers</c>: it owns the embers' simulation
/// (<see cref="EmbersSim"/>, lane c2-sims), drawn by the native particle
/// executor on one channel (<c>particle/fire</c>).
///
/// <para>
/// The embers spawn inside the brush's MODEL bounds, read once when the
/// channel is made: stock read them from the model when the class was built,
/// and refused a volume with no model (one would emit every ember at the
/// world origin). Stock's OnDataChanged starts the spawn timer on creation
/// only; every later update re-reads the numbers.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.EnvEmbers)]
public sealed class ClientEmbers(IEngineEntityRender render, IClientParticleExecutor executor, IRandomSeeds seeds,
                                 IEngineWorldQuery world, IEngineEntities entities)
    : ClientTrailBase(render, executor, seeds)
{
    private readonly List<SimpleParticle> _scratch = new();

    public override string ClassName => EntityNames.EnvEmbers;

    [NetState] public EmbersNetState State = new();

    public EmbersSim Sim { get; } = new();

    public override int ChannelCount => 1;

    public override string MaterialOf(int channel) => EmbersSim.Material;

    protected override bool CanCreate()
    {
        if (!world.ClientEntityModelBounds(entities.GetIndex(Handle), out Vector mins, out Vector maxs, out _))
            return false;
        Sim.Mins = mins;
        Sim.Maxs = maxs;
        return true;
    }

    protected override void Feed(bool first)
    {
        Sim.Density = State.Density;
        Sim.Lifetime = State.Lifetime;
        Sim.Speed = State.Speed;
        Sim.Emitting = State.Emit;
    }

    protected override void Start(Vector origin) => Sim.Start();

    protected override void Step(in ParticleEmit emit, List<(int Channel, int Id)> added)
    {
        _scratch.Clear();
        Sim.Emit(emit.FrameTime, emit.Angles, Random, _scratch);
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
