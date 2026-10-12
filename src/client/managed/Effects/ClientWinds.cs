using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// One wind and its think schedule (stock's <c>C_EnvWind</c> with our
/// <c>CWindThinkSchedule</c>): armed on start, due when the clock reaches the
/// time its last think asked for, disarmed for a wind whose gusts could never
/// advance (no delay and no duration: stock's think would loop on its first
/// switch).
/// </summary>
public sealed class WindThinker
{
    public WindSim Sim { get; } = new();

    public bool Armed { get; private set; }

    public float NextThink { get; private set; }

    /// <summary>Stock's <c>Start</c>: Init, armed to think at once, unless the gusts cannot advance.</summary>
    public void Start(int seed, float startTime, int initialYaw, float initialSpeed)
    {
        Sim.Init(seed, startTime, initialYaw, initialSpeed);
        Armed = Sim.CanAdvance;
        NextThink = 0.0f;
    }

    /// <summary>One think when due; whether it thought.</summary>
    public bool ThinkIfDue(float curTime)
    {
        if (!Armed || curTime < NextThink)
            return false;
        NextThink = Sim.Think(curTime);
        return true;
    }

    public void Save(BinaryWriter w)
    {
        w.Write(Armed);
        w.Write(NextThink);
        Sim.Save(w);
    }

    public void Restore(BinaryReader r)
    {
        Armed = r.ReadBoolean();
        NextThink = r.ReadSingle();
        Sim.Restore(r);
    }
}

/// <summary>The client's winds, in the order they were made (stock's <c>s_windControllers</c>).</summary>
public interface IClientWinds
{
    /// <summary>Adds the wind once; a wind already held keeps its place.</summary>
    void Add(EntityHandle owner, WindThinker wind);

    void Remove(EntityHandle owner);

    int Count { get; }

    /// <summary>
    /// <c>GetWindspeedAtTime</c>'s answer: the head wind's vector, zero with
    /// none (what the winds publish to native, read here directly).
    /// </summary>
    Vector Windspeed { get; }
}

/// <summary>
/// The once-a-frame think the native CManagedWindThink did (lane c2-sims): each
/// due wind thinks at the clock, then the head wind's vector (what
/// <c>GetWindspeedAtTime</c> answers natively) and the last thinking wind's
/// tree sway are published.
/// </summary>
public sealed class ClientWinds(IClientWindPublisher publisher, IGameClock clock) : IClientWinds
{
    private readonly List<(EntityHandle Owner, WindThinker Wind)> _winds = new();

    public int Count => _winds.Count;

    public Vector Windspeed => _winds.Count > 0 ? _winds[0].Wind.Sim.CurrentWind : default;

    public void Add(EntityHandle owner, WindThinker wind)
    {
        int i = _winds.FindIndex(w => w.Owner == owner);
        if (i < 0)
            _winds.Add((owner, wind));
        else
            _winds[i] = (owner, wind);
    }

    public void Remove(EntityHandle owner) => _winds.RemoveAll(w => w.Owner == owner);

    public void Frame()
    {
        float now = clock.Now;
        Vector? sway = null;
        foreach (var (_, wind) in _winds)
        {
            if (wind.ThinkIfDue(now))
                sway = wind.Sim.SwayParameter;
        }

        publisher.Publish(_winds.Count > 0 ? _winds[0].Wind.Sim.CurrentWind : default, sway, _winds.Count);
    }

    /// <summary>A level change takes every wind with it.</summary>
    public void Reset() => _winds.Clear();
}
