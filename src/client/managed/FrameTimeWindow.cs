namespace SourceSharp.Client;

/// <summary>
/// The client's frame times over the last second of wall time: what
/// <c>CFPSPanel</c> drew (fps, and the frame time's min, mean and max), kept
/// for the dev API's <c>frame</c> route (plan_client_novalve §4.4). One per
/// client composition, from the container; fed once per frame by the module.
/// </summary>
public sealed class FrameTimeWindow(TimeProvider time)
{
    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));
    private readonly Queue<(long At, double Ms)> _frames = new();

    /// <summary>The window's length.</summary>
    public static readonly TimeSpan Length = TimeSpan.FromSeconds(1);

    /// <summary>One frame, of <paramref name="frameTime"/> seconds by the engine's clock.</summary>
    public void Add(float frameTime)
    {
        long now = _time.GetTimestamp();
        _frames.Enqueue((now, frameTime * 1000.0));
        Trim(now);
    }

    /// <summary>The frames in the window ending now, and their frame times in ms (all 0 for none).</summary>
    public (int Frames, double Min, double Avg, double Max) Read()
    {
        Trim(_time.GetTimestamp());
        if (_frames.Count == 0)
            return (0, 0, 0, 0);

        double min = double.MaxValue, max = 0, sum = 0;
        foreach (var (_, ms) in _frames)
        {
            min = Math.Min(min, ms);
            max = Math.Max(max, ms);
            sum += ms;
        }

        return (_frames.Count, min, sum / _frames.Count, max);
    }

    /// <summary>Forgets every frame: a level change starts a new window.</summary>
    public void Clear() => _frames.Clear();

    private void Trim(long now)
    {
        while (_frames.Count > 0 && _time.GetElapsedTime(_frames.Peek().At, now) > Length)
            _frames.Dequeue();
    }
}
