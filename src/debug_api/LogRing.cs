namespace Debug.Api;

/// <summary>
/// One logged line: the number a reader uses to ask for what came after it, and
/// when it was said.
///
/// The time is wall clock rather than game time, because it is read next to a
/// wristwatch and next to other logs — "did this happen when I pressed the
/// button" is the question, and the game's own clock restarts every map.
/// </summary>
public readonly record struct LogLine(long Sequence, string Text, bool IsWarning, DateTime TimeUtc);

/// <summary>
/// The last N log lines, readable from another thread.
///
/// A ring rather than a growing list: the game logs continuously and nothing
/// ever reads the far past, so an unbounded buffer would be a slow leak for a
/// tool that is meant to be left running.
///
/// Lines carry a sequence number so a reader can ask for "everything after 412"
/// and get exactly the new lines, without the races that an index into a moving
/// buffer would have.
/// </summary>
public sealed class LogRing
{
    private readonly object _lock = new();
    private readonly LogLine[] _lines;
    private long _nextSequence;
    private int _count;
    private int _head;

    public LogRing(int capacity = 4096)
    {
        _lines = new LogLine[capacity];
    }

    /// <summary>The sequence the next line will get. A reader starts from here to follow along.</summary>
    public long NextSequence
    {
        get { lock (_lock) return _nextSequence; }
    }

    public void Add(string text, bool isWarning)
    {
        // Log calls arrive already newline-terminated, which is noise once each
        // line is a separate record.
        text = text.TrimEnd('\n', '\r');

        if (text.Length == 0)
            return;

        lock (_lock)
        {
            _lines[_head] = new LogLine(_nextSequence++, text, isWarning, DateTime.UtcNow);
            _head = (_head + 1) % _lines.Length;

            if (_count < _lines.Length)
                _count++;
        }
    }

    /// <summary>
    /// Every line with a sequence at or after <paramref name="since"/>, oldest
    /// first, up to a limit. A reader that has fallen further behind than the
    /// ring is deep silently gets what is left rather than an error — losing the
    /// oldest lines is what a ring is for.
    /// </summary>
    public List<LogLine> Since(long since, int max = 500)
    {
        var result = new List<LogLine>();

        lock (_lock)
        {
            int start = (_head - _count + _lines.Length) % _lines.Length;

            for (int i = 0; i < _count && result.Count < max; i++)
            {
                var line = _lines[(start + i) % _lines.Length];

                if (line.Sequence >= since)
                    result.Add(line);
            }
        }

        return result;
    }
}
