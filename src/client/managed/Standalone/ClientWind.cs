using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of <c>env_wind</c>: it owns the wind's simulation
/// (<see cref="WindSim"/>, lane c2-sims) and hands it to the client's winds,
/// which think it once a frame and publish what native's
/// <c>GetWindspeedAtTime</c> answers.
///
/// <para>
/// Every update re-starts the wind, which is stock's OnDataChanged for the
/// same class: the simulation is deterministic in its parameters, so a re-Init
/// IS the update. The wind keeps its place in the list.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.EnvWind)]
public sealed class ClientWind : ClientStandaloneEntity, ISaveCustom
{
    public ClientWind(IEngineEntityRender render, IClientWinds winds)
        : base(render)
    {
        _winds = winds;
    }

    private readonly IClientWinds _winds;

    public override string ClassName => EntityNames.EnvWind;

    [NetState] public EnvWindNetState State = new();

    public WindThinker Wind { get; } = new();

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        var s = State;
        var sim = Wind.Sim;
        sim.MinWind = s.MinWind;
        sim.MaxWind = s.MaxWind;
        sim.MinGust = s.MinGust;
        sim.MaxGust = s.MaxGust;
        sim.MinGustDelay = s.MinGustDelay;
        sim.MaxGustDelay = s.MaxGustDelay;
        sim.GustDirChange = s.GustDirChange;
        sim.GustDuration = s.GustDuration;
        Wind.Start(s.WindSeed, s.StartTime, s.InitialWindDir, s.InitialWindSpeed);
        _winds.Add(Handle, Wind);
    }

    public override void OnRemoved() => _winds.Remove(Handle);

    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        using var buffer = new MemoryStream();
        Wind.Save(new BinaryWriter(buffer));
        writer.Write("wind.sim", Convert.ToBase64String(buffer.ToArray()));
    }

    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        string carried = reader.ReadString("wind.sim");
        if (carried.Length == 0)
            return;
        Wind.Restore(new BinaryReader(new MemoryStream(Convert.FromBase64String(carried))));
        _winds.Add(Handle, Wind);
    }
}
