namespace Debug.Api;

/// <summary>What happened. Kept as a small closed set so a view can colour and filter by it.</summary>
public enum EntityEventKind
{
    /// <summary>An entity that was not in the previous sample.</summary>
    Spawned,

    /// <summary>An entity that was in the previous sample and is not now.</summary>
    Removed,

    /// <summary>An input this tool fired at an entity.</summary>
    Input,

    /// <summary>An output a scripted entity fired.</summary>
    Output,

    /// <summary>A level boundary. Everything before one refers to entities that no longer exist.</summary>
    Level,
}

/// <summary>One thing that happened to an entity, with the number a reader follows along by.</summary>
public readonly record struct EntityEvent(
    long Sequence,
    float Time,
    EntityEventKind Kind,
    string ClassName,
    string Name,
    string Detail,
    int Index,
    uint Handle);

/// <summary>
/// A history of what entities did.
///
/// The log answers the question a live view cannot: an entity that spawned and
/// was gone again before the next poll leaves no trace in a snapshot, and that
/// is precisely the entity worth knowing about. A ring rather than a list for
/// the same reason as the console log -- this is meant to be left running.
///
/// Sequence numbers rather than timestamps for reading: two events in the same
/// frame share a time, and a reader asking for "everything after what I have"
/// would either repeat them or skip them.
/// </summary>
public sealed class EntityEventLog
{
    private readonly object _lock = new();
    private readonly EntityEvent[] _events;
    private long _nextSequence;
    private int _count;
    private int _head;

    public EntityEventLog(int capacity = 4096)
    {
        _events = new EntityEvent[capacity];
    }

    public long NextSequence
    {
        get { lock (_lock) return _nextSequence; }
    }

    public void Add(EntityEventKind kind, string className, string name, string detail,
                    float time, int index = -1, uint handle = 0)
    {
        lock (_lock)
        {
            _events[_head] = new EntityEvent(_nextSequence++, time, kind, className, name, detail, index, handle);
            _head = (_head + 1) % _events.Length;

            if (_count < _events.Length)
                _count++;
        }
    }

    /// <summary>
    /// The events at or after a sequence that a filter matches, oldest first, up
    /// to a limit -- and <paramref name="resumeAt"/>, the sequence a reader
    /// continues from: the first match NOT answered when the limit cut the read
    /// short, else the ring's head, because everything before it was looked at.
    /// A filtered reader that resumed from its last match would re-scan the
    /// unmatched tail on every poll.
    /// </summary>
    public List<EntityEvent> Since(long since, int max, EventFilter filter, out long resumeAt)
    {
        var result = new List<EntityEvent>();

        lock (_lock)
        {
            resumeAt = _nextSequence;
            int start = (_head - _count + _events.Length) % _events.Length;

            for (int i = 0; i < _count; i++)
            {
                var entry = _events[(start + i) % _events.Length];

                if (entry.Sequence < since || !filter.Matches(in entry))
                    continue;

                if (result.Count == max)
                {
                    resumeAt = entry.Sequence;
                    break;
                }

                result.Add(entry);
            }
        }

        return result;
    }

    /// <summary>Events at or after a sequence, oldest first, up to a limit.</summary>
    public List<EntityEvent> Since(long since, int max = 500)
    {
        var result = new List<EntityEvent>();

        lock (_lock)
        {
            int start = (_head - _count + _events.Length) % _events.Length;

            for (int i = 0; i < _count && result.Count < max; i++)
            {
                var entry = _events[(start + i) % _events.Length];

                if (entry.Sequence >= since)
                    result.Add(entry);
            }
        }

        return result;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _count = 0;
            _head = 0;
        }
    }
}

/// <summary>
/// Notices entities appearing and disappearing.
///
/// There is no engine callback for "an entity spawned" reachable from here, so
/// this compares one whole-map sample against the last. That is not free -- it
/// walks every entity in the map -- which is why it only runs while something is
/// actually reading the log, and only every so often rather than every frame.
///
/// Sampling means an entity that came and went between two samples is missed.
/// That is the honest limit of doing it this way, and it is stated in the feed
/// rather than left for someone to discover.
/// </summary>
public sealed class EntitySampler
{
    /// <summary>How often to look. Often enough to catch a spawn, rare enough not to be felt.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(0.5);

    /// <summary>
    /// How long a poll keeps sampling alive. Comfortably more than the UI's
    /// poll period, so an active reader never sees a gap, and short enough that
    /// a closed browser stops costing anything.
    /// </summary>
    public static readonly TimeSpan InterestWindow = TimeSpan.FromSeconds(10);

    private readonly EntityEventLog _log;
    private readonly Dictionary<uint, string> _seen = new();

    private DateTime _lastSample = DateTime.MinValue;
    private DateTime _interestUntil = DateTime.MinValue;
    private bool _primed;

    public EntitySampler(EntityEventLog log)
    {
        _log = log;
    }

    /// <summary>Someone is reading, so keep sampling for a while yet.</summary>
    public void NoteInterest() => _interestUntil = DateTime.UtcNow + InterestWindow;

    /// <summary>Whether a sample is currently being taken, which the feed reports.</summary>
    public bool IsSampling => DateTime.UtcNow < _interestUntil;

    /// <summary>
    /// Forgets everything known about the previous level.
    ///
    /// Without this every entity in the old map would be reported as removed and
    /// every entity in the new one as spawned, which is true but useless -- the
    /// level change is the one event worth having.
    /// </summary>
    public void Reset()
    {
        _seen.Clear();
        _primed = false;
    }

    /// <summary>
    /// Takes a sample if one is due. Called from the game thread, where reading
    /// entities is legal.
    /// </summary>
    public void Update(Func<IEnumerable<(uint Handle, int Index, string ClassName)>> enumerate, float gameTime)
    {
        var now = DateTime.UtcNow;

        if (now >= _interestUntil || now - _lastSample < Interval)
            return;

        _lastSample = now;

        var present = new Dictionary<uint, string>();

        foreach (var (handle, index, className) in enumerate())
        {
            present[handle] = className;

            if (_seen.ContainsKey(handle))
                continue;

            // The first sample is the world as it already was, not six hundred
            // things that just happened. Reporting it as spawns would bury the
            // real ones under a wall of noise on every reload.
            if (_primed)
                _log.Add(EntityEventKind.Spawned, className, "", "appeared", gameTime, index, handle);
        }

        if (_primed)
        {
            foreach (var (handle, className) in _seen)
            {
                if (!present.ContainsKey(handle))
                    _log.Add(EntityEventKind.Removed, className, "", "gone", gameTime, -1, handle);
            }
        }

        _seen.Clear();
        foreach (var entry in present)
            _seen[entry.Key] = entry.Value;

        _primed = true;
    }
}
