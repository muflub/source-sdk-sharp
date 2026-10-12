namespace Debug.Api;

/// <summary>One component of a field's difference: what was predicted, what arrived, and the gap.</summary>
public readonly record struct WirePredictionComponent(double Predicted, double Server, double Delta);

/// <summary>
/// One field of a prediction error as the debug channel carries it
/// (plan_devapi_interfaces §5.6): the dev API's own copy of the game's
/// record, so neither half of the dev API names the Abi.
/// </summary>
public sealed class WirePredictionField(
    string name, int kind, int offset, int bytes, float tolerance, bool tolerated, int differingBytes,
    IReadOnlyList<WirePredictionComponent> components)
{
    public string Name { get; } = name;

    public int Kind { get; } = kind;

    public int Offset { get; } = offset;

    public int Bytes { get; } = bytes;

    public float Tolerance { get; } = tolerance;

    public bool Tolerated { get; } = tolerated;

    public int DifferingBytes { get; } = differingBytes;

    public IReadOnlyList<WirePredictionComponent> Components { get; } = components;

    /// <summary>Whether its kind takes a tolerance at all: float, vector or angles.</summary>
    public bool Tolerable => Kind is 1 or 2 or 7;

    /// <summary>The largest component difference, by magnitude.</summary>
    public double MaxAbsDelta
    {
        get
        {
            double worst = 0;
            foreach (var component in Components)
            {
                double magnitude = Math.Abs(component.Delta);
                if (magnitude > worst)
                    worst = magnitude;
            }

            return worst;
        }
    }

    /// <summary>How many tolerances the worst difference is, or null with no tolerance.</summary>
    public double? ToleranceRatio => Tolerance > 0f ? MaxAbsDelta / Tolerance : null;
}

/// <summary>One prediction error as the debug channel carries it (§5.6).</summary>
public sealed class WirePredictionRecord(
    long sequence, int command, uint typeId, string className, int blobBytes, int standingBytes,
    int firstDiffOffset, int toleratedFields, DateTime timeUtc, IReadOnlyList<WirePredictionField> fields)
{
    public long Sequence { get; } = sequence;

    public int Command { get; } = command;

    public uint TypeId { get; } = typeId;

    public string ClassName { get; } = className;

    public int BlobBytes { get; } = blobBytes;

    public int StandingBytes { get; } = standingBytes;

    public int FirstDiffOffset { get; } = firstDiffOffset;

    public int ToleratedFields { get; } = toleratedFields;

    public DateTime TimeUtc { get; } = timeUtc;

    public IReadOnlyList<WirePredictionField> Fields { get; } = fields;

    /// <summary>Whether any bytes stood outside every tolerance.</summary>
    public bool Mispredicted => StandingBytes > 0;
}

/// <summary>
/// The prediction errors a dev API holds: on the server, what clients pushed
/// up the debug channel. A bounded ring with the same counting as the game's
/// own ring (PredictionMirrorTests holds the two equal).
/// </summary>
public sealed class PredictionMirror
{
    /// <summary>How many records a ring holds by default.</summary>
    public const int DefaultCapacity = 256;

    private readonly object _lock = new();
    private readonly WirePredictionRecord?[] _records;
    private long _nextSequence;
    private int _count;
    private int _head;
    private long _observed;
    private long _mispredicted;
    private long _toleratedOnly;

    public PredictionMirror(int capacity = DefaultCapacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _records = new WirePredictionRecord?[capacity];
    }

    public int Capacity => _records.Length;

    public long NextSequence { get { lock (_lock) return _nextSequence; } }

    public long Observed { get { lock (_lock) return _observed; } }

    public long Mispredicted { get { lock (_lock) return _mispredicted; } }

    public long ToleratedOnly { get { lock (_lock) return _toleratedOnly; } }

    public int Count { get { lock (_lock) return _count; } }

    public void Add(WirePredictionRecord record)
    {
        lock (_lock)
        {
            _nextSequence = Math.Max(_nextSequence, record.Sequence) + 1;
            _observed++;

            if (record.Mispredicted)
                _mispredicted++;
            else
                _toleratedOnly++;

            _records[_head] = record;
            _head = (_head + 1) % _records.Length;

            if (_count < _records.Length)
                _count++;
        }
    }

    /// <summary>The records still held whose sequence is at least <paramref name="since"/>, oldest first.</summary>
    public List<WirePredictionRecord> Since(long since, int max = DefaultCapacity)
    {
        var result = new List<WirePredictionRecord>();

        lock (_lock)
        {
            int start = (_head - _count + _records.Length) % _records.Length;

            for (int i = 0; i < _count && result.Count < max; i++)
            {
                var record = _records[(start + i) % _records.Length]!;
                if (record.Sequence >= since)
                    result.Add(record);
            }
        }

        return result;
    }

    /// <summary>Forgets everything, counters included, for a reader who wants a clean window.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            Array.Clear(_records);
            _count = 0;
            _head = 0;
            _observed = 0;
            _mispredicted = 0;
            _toleratedOnly = 0;
        }
    }
}
