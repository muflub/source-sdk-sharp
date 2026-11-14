using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Horde.Session;

namespace TF2.Descent.Server.Horde.Attacks;

/// <summary>
/// The hands' own main-thread cost (HO7, plan 3.11's method): while switched on, the thread CPU
/// the attacks spend in a server tick (their <c>Tick</c> and every <c>Act</c> the tick's intents
/// make), one sample a tick, summed; the distribution on stop.
/// </summary>
public sealed class AttackCosts
{
    public const int Capacity = 100_000;

    private readonly double[] _cpu = new double[Capacity];
    private double _tick;
    private double _since;

    public bool On { get; private set; }

    public int Samples { get; private set; }

    public void Start()
    {
        Samples = 0;
        _tick = -1;   // the tick under way when switched on is not whole: no sample of it
        On = true;
    }

    public void Stop() => On = false;

    /// <summary>A tick begins: the last one's sum becomes a sample.</summary>
    public void NextTick()
    {
        if (!On)
            return;
        if (Samples < Capacity && _tick >= 0)
            _cpu[Samples++] = _tick;
        _tick = 0;
    }

    public void Enter()
    {
        if (On)
            _since = HordeThreadClock.Micros();
    }

    public void Leave()
    {
        if (On && _tick >= 0)
            _tick += HordeThreadClock.Micros() - _since;
    }

    public string Report()
    {
        int n = Samples;
        if (n == 0)
            return "n 0";
        double[] s = _cpu[..n];
        Array.Sort(s);
        return string.Create(CultureInfo.InvariantCulture,
            $"n {n} mean {s.Average():0.00} p50 {s[n / 2]:0.00} p95 {s[(int)(n * 0.95)]:0.00} p99 {s[(int)(n * 0.99)]:0.00} max {s[n - 1]:0.00} us cpu a tick");
    }
}

/// <summary><c>tf2_descent_horde_attacks_time &lt;start|stop&gt;</c>: the attacks' main-thread CPU a tick, summed over their tick and every act.</summary>
[ConCommand("tf2_descent_horde_attacks_time", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_horde_attacks_time <start|stop>: time the robots' attacks on the game thread (thread CPU a tick, their tick and every act summed)")]
public sealed class HordeAttacksTimeCommand(RobotAttacks attacks, HordeService horde, ILog log)
{
    public void Execute(string verb)
    {
        if (verb == "start")
        {
            attacks.Costs.Start();
            log.Msg("[TF2Descent/Horde] attacks time: started\n");
            return;
        }

        attacks.Costs.Stop();
        int lent = horde.Current?.Bodies?.Lent ?? 0;
        log.Msg($"[TF2Descent/Horde] attacks time: {lent} lent bodies: {attacks.Costs.Report()}; asked {attacks.TotalAsked}, fired {attacks.TotalFired}\n");
    }
}
