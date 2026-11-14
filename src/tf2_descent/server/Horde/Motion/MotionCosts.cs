using System.Diagnostics;
using System.Globalization;

namespace TF2.Descent.Server.Horde.Motion;

/// <summary>
/// What the motion costs the main thread, a tick at a time, while measuring
/// (<c>tf2_descent_horde_motion_time</c>): the whole tick by stopwatch and by
/// thread CPU, the part of it the inline steps took, the accept sweeps and the
/// engine writes, and how old each applied step set was (0 or 1 tick; a tick
/// that applied none counts as "none"). The samples are kept in arrays filled
/// up to <see cref="Capacity"/>, so measuring allocates nothing.
/// </summary>
public sealed class MotionCosts
{
    public const int Capacity = 100_000;

    private readonly double[] _wall = new double[Capacity];
    private readonly double[] _cpu = new double[Capacity];
    private readonly double[] _step = new double[Capacity];
    private readonly long[] _ages = new long[3];

    public bool On { get; private set; }

    public int Samples { get; private set; }

    public long Sweeps { get; private set; }

    public long Places { get; private set; }

    public long Gaits { get; private set; }

    public long Poses { get; private set; }

    public long Bodies { get; private set; }

    public void Start()
    {
        Samples = 0;
        Sweeps = Places = Gaits = Poses = Bodies = 0;
        Array.Clear(_ages);
        On = true;
    }

    public void Stop() => On = false;

    internal void Tick(double wall, double cpu, double step, int age, long sweeps, long places, long gaits, long poses, int bodies)
    {
        if (!On || Samples >= Capacity)
            return;
        _wall[Samples] = wall;
        _cpu[Samples] = cpu;
        _step[Samples] = step;
        Samples++;
        _ages[Math.Clamp(age, 0, 2)]++;
        Sweeps += sweeps;
        Places += places;
        Gaits += gaits;
        Poses += poses;
        Bodies += bodies;
    }

    public string Report() => string.Create(CultureInfo.InvariantCulture,
        $"ticks {Samples}, bodies stepped {Bodies}: main wall {Dist(_wall)}; main cpu {Dist(_cpu)}; of which inline steps cpu {Dist(_step)}; " +
        $"accept sweeps {Sweeps}, places {Places}, gait writes {Gaits}, pose writes {Poses}; applied step age: 0 ticks {_ages[0]}, 1 tick {_ages[1]}, none {_ages[2]}");

    private string Dist(double[] samples)
    {
        int n = Samples;
        if (n == 0)
            return "n 0";
        double[] s = samples[..n];
        Array.Sort(s);
        return string.Create(CultureInfo.InvariantCulture,
            $"mean {s.Average():0.0} p50 {s[n / 2]:0.0} p95 {s[(int)(n * 0.95)]:0.0} max {s[n - 1]:0.0} us");
    }

    /// <summary>A stopwatch stamp now.</summary>
    public static long Stamp() => Stopwatch.GetTimestamp();
}
