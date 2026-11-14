namespace SourceSharp.Ai;

/// <summary>The parts of a brain step that are timed apart (<see cref="StepCost"/>).</summary>
public enum StepPart
{
    /// <summary>The memory's event intake: every event since the last step applied to the board.</summary>
    Events = 0,

    /// <summary>The memory's reading of the tick's view: the bot's own state and everyone in sight.</summary>
    Observe,

    /// <summary>Every unseen opponent's ammo estimate moved on.</summary>
    Ammo,

    /// <summary>Every unseen opponent's belief spread (<see cref="PositionPredictor.Step"/>); its units are the hops made.</summary>
    Predict,

    /// <summary>Confidence and habits fading.</summary>
    Decay,

    /// <summary>The decider's decide: its choice of target, its tree, its hands and all it does beside them.</summary>
    Decide,
}

/// <summary>
/// What one brain's steps cost, part by part, in the time of the clock it is given (the running
/// thread's CPU time, for an instrument on a shared machine): the offline cost instrument's
/// (<c>tools/ai-replay cost</c>) and the cost facts'. A step begins it and each part marks its end,
/// so a part is charged the time since the mark before. One per brain: a brain's steps run one
/// after another, never two at once. A counter, never an input: nothing a brain decides reads it.
/// </summary>
public sealed class StepCost(Func<double> clock)
{
    /// <summary>How many parts there are.</summary>
    public const int Parts = (int)StepPart.Decide + 1;

    private readonly double[] _micros = new double[Parts];
    private readonly long[] _units = new long[Parts];
    private readonly double[] _max = new double[Parts];
    private readonly double[] _step = new double[Parts];
    private double _at;

    /// <summary>Steps begun.</summary>
    public long Steps { get; private set; }

    /// <summary>A step begins: the time from now is its first part's.</summary>
    public void Begin()
    {
        Steps++;
        Array.Clear(_step);
        _at = clock();
    }

    /// <summary>The time since the step began, or since the last mark, is <paramref name="part"/>'s, which did <paramref name="units"/> of its work in it.</summary>
    public void Mark(StepPart part, int units = 0)
    {
        double now = clock();
        double spent = now - _at;
        _at = now;
        int i = (int)part;
        _micros[i] += spent;
        _units[i] += units;
        _step[i] += spent;
        if (_step[i] > _max[i])
            _max[i] = _step[i];
    }

    /// <summary>All the time charged to <paramref name="part"/>, in microseconds.</summary>
    public double Micros(StepPart part) => _micros[(int)part];

    /// <summary>The most <paramref name="part"/> took in one step.</summary>
    public double Max(StepPart part) => _max[(int)part];

    /// <summary>The units of work <paramref name="part"/> counted.</summary>
    public long Units(StepPart part) => _units[(int)part];

    /// <summary>Every part's time together.</summary>
    public double Total => _micros.Sum();
}
