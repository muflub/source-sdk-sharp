using System.Diagnostics;

namespace SourceSharp.Abi;

/// <summary>
/// What the generic payload actually costs on the wire, in C# field names.
///
/// <para>
/// plan_nettable_generic.md Part 13 and Part 14 Step 9. Part 17 claims a
/// bandwidth trade and a CPU trade for the whole design and neither should stay
/// a claim; this is the instrument that answers them, and it is also what
/// Part 10's <c>Group</c> hint has to be tuned against — grouping fields that
/// change together is a guess until something says which fields change together.
/// </para>
///
/// <para>
/// SAMPLED FROM THE BLOB, which is not a compromise. The encoder lives in
/// engine.so and offers no hook, so nothing here can read the bitstream. It does
/// not need to: the delta is <em>determined</em> by which payload words differ
/// between encodes, and that is directly observable by keeping the previous
/// image and comparing.
/// </para>
///
/// <para>
/// THE UNIT IS THE DWORD, NOT THE BYTE, and this is a correction to Part 13.
/// That part says the diff "IS the delta, byte for byte, because each byte is
/// its own prop" — which was true of the byte-granular draft and stopped being
/// true when Part 1B made the payload element a dword. One byte changing costs
/// the whole word it sits in, so counting bytes would understate the wire cost
/// by up to four times. Words touched is the number that maps to bits on the
/// wire; bytes changed is kept beside it because it is what says how much of the
/// word was real.
/// </para>
/// </summary>
public sealed class NetTrafficSampler
{
    /// <summary>
    /// Bits a changed payload word costs: seven for the delta's prop-index gap
    /// plus the thirty-two it carries.
    ///
    /// <para>
    /// Part 1B's arithmetic, and the reason the payload element is a dword at
    /// all. <c>CDeltaBitsWriter::WritePropIndex</c> writes the GAP from the last
    /// index written and charges 7 bits for a gap of 16 or less, so a run of
    /// changed words costs 39 bits each. Consecutive words are the common case
    /// here, so this is the right estimate rather than a floor.
    /// </para>
    /// </summary>
    public const int BitsPerChangedWord = 39;

    /// <summary>Payload word size, matching SOURCESHARP_PAYLOAD_WORD_BYTES.</summary>
    public const int WordBytes = 4;

    /// <summary>How many one-second buckets the rolling rate is averaged over.</summary>
    public const int WindowSeconds = 10;

    private readonly Dictionary<int, byte[]> _previous = new();
    private readonly Dictionary<string, ClassAccumulator> _byClass =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Func<double> _seconds;
    private double _startedAt;

    public NetTrafficSampler() : this(StopwatchSeconds())
    {
    }

    /// <summary>
    /// With the clock the rates are measured against, in seconds. The game's is
    /// a Stopwatch; a test hands one it advances itself, so a rate is an exact
    /// number rather than whatever a few milliseconds of wall clock gave.
    /// </summary>
    public NetTrafficSampler(Func<double> seconds)
    {
        _seconds = seconds;
        _startedAt = seconds();
    }

    private static Func<double> StopwatchSeconds()
    {
        var watch = Stopwatch.StartNew();
        return () => watch.Elapsed.TotalSeconds;
    }

    /// <summary>Seconds since the sampler was started or last reset.</summary>
    public double Elapsed => _seconds() - _startedAt;

    /// <summary>How many entities are being tracked, i.e. how many snapshots are held.</summary>
    public int Tracked => _previous.Count;

    /// <summary>
    /// Offers one entity's current payload for this frame.
    ///
    /// <para>
    /// The FIRST sighting of an entity records its image and charges nothing.
    /// That is deliberate and it is the difference between measuring traffic and
    /// measuring arrivals: an entity seen for the first time has no previous
    /// image to differ from, so charging its whole payload would report a spawn
    /// as the most expensive thing on the map.
    /// </para>
    /// </summary>
    /// <param name="className">The managed class, which is what costs are grouped by.</param>
    /// <param name="entityIndex">Identifies the snapshot to diff against.</param>
    /// <param name="blob">The entity's payload as it stands this frame.</param>
    /// <param name="layout">
    /// The class's declared fields, for attributing changed words to names. Null
    /// is allowed and means "count the words, name no fields" — a class whose
    /// layout this side does not have is still worth its total.
    /// </param>
    public void Observe(string className, int entityIndex, ReadOnlySpan<byte> blob,
                        IReadOnlyList<NetSchemaField>? layout)
    {
        if (string.IsNullOrEmpty(className) || blob.IsEmpty)
            return;

        var accumulator = Class(className);
        accumulator.Seen++;

        if (!_previous.TryGetValue(entityIndex, out byte[]? last) || last.Length != blob.Length)
        {
            _previous[entityIndex] = blob.ToArray();
            return;
        }

        int wordsTouched = 0;
        int bytesChanged = 0;
        int words = blob.Length / WordBytes;

        for (int w = 0; w < words; w++)
        {
            int at = w * WordBytes;

            ReadOnlySpan<byte> now = blob.Slice(at, WordBytes);
            ReadOnlySpan<byte> was = last.AsSpan(at, WordBytes);

            if (now.SequenceEqual(was))
                continue;

            wordsTouched++;

            for (int b = 0; b < WordBytes; b++)
            {
                if (now[b] != was[b])
                    bytesChanged++;
            }

            AttributeWord(accumulator, layout, at);
        }

        if (wordsTouched > 0)
        {
            accumulator.Changed++;
            accumulator.WordsTouched += wordsTouched;
            accumulator.BytesChanged += bytesChanged;
            accumulator.Bucket(Second()).Words += wordsTouched;

            blob.CopyTo(last);
        }
    }

    /// <summary>
    /// Charges a changed word to every field overlapping it.
    ///
    /// <para>
    /// EVERY field, not the first, and the per-field column can therefore add up
    /// to more than the class total. That is the honest shape: a word holding
    /// two fields is one word on the wire whichever of them moved, so there is
    /// no non-arbitrary way to split it — and reporting only the first would
    /// hide the second entirely, which is the opposite of what this view is for.
    /// The JSON says so rather than leaving a reader to discover it by adding
    /// the column up.
    /// </para>
    /// </summary>
    private static void AttributeWord(ClassAccumulator accumulator,
                                      IReadOnlyList<NetSchemaField>? layout, int wordOffset)
    {
        if (layout is null)
            return;

        int wordEnd = wordOffset + WordBytes;

        foreach (var field in layout)
        {
            int fieldEnd = field.Offset + field.Bytes;

            if (field.Offset < wordEnd && wordOffset < fieldEnd)
                accumulator.Field(field.Name).Words++;
        }
    }

    /// <summary>Everything measured so far, ordered by what costs most.</summary>
    public IReadOnlyList<NetTrafficClass> Report(int topFields = 8)
    {
        double elapsed = Math.Max(Elapsed, 0.001);
        int now = Second();

        var rows = new List<NetTrafficClass>(_byClass.Count);

        foreach (var (className, accumulator) in _byClass)
        {
            var fields = accumulator.Fields
                .OrderByDescending(f => f.Value.Words)
                .ThenBy(f => f.Key, StringComparer.Ordinal)
                .Take(topFields)
                .Select(f => new NetTrafficField(
                    f.Key, f.Value.Words,
                    f.Value.Words * BitsPerChangedWord / 8.0 / elapsed))
                .ToArray();

            rows.Add(new NetTrafficClass(
                className,
                accumulator.Seen,
                accumulator.Changed,
                accumulator.WordsTouched,
                accumulator.BytesChanged,
                accumulator.WordsTouched * BitsPerChangedWord / 8.0 / elapsed,
                accumulator.RecentWords(now) * BitsPerChangedWord / 8.0 / WindowSeconds,
                fields));
        }

        return rows.OrderByDescending(r => r.BytesPerSecond)
                   .ThenBy(r => r.ClassName, StringComparer.Ordinal)
                   .ToArray();
    }

    /// <summary>
    /// Forgets the totals but KEEPS the snapshots.
    ///
    /// Keeping them is the point: dropping the images would make the first frame
    /// after a reset charge every entity its whole payload, so a reader who hit
    /// reset to get a clean window would get one enormous sample instead.
    /// </summary>
    public void Reset()
    {
        _byClass.Clear();
        _startedAt = _seconds();
    }

    /// <summary>Drops an entity's snapshot, so its next sighting charges nothing.</summary>
    public void Forget(int entityIndex) => _previous.Remove(entityIndex);

    /// <summary>Drops everything, for a level change or a managed reload.</summary>
    public void Clear()
    {
        _previous.Clear();
        Reset();
    }

    private int Second() => (int)_seconds();

    private ClassAccumulator Class(string className)
    {
        if (!_byClass.TryGetValue(className, out var accumulator))
            _byClass[className] = accumulator = new ClassAccumulator();

        return accumulator;
    }

    private sealed class FieldAccumulator
    {
        public long Words;
    }

    private sealed class SecondBucket
    {
        public int Second = -1;
        public long Words;
    }

    private sealed class ClassAccumulator
    {
        public long Seen;
        public long Changed;
        public long WordsTouched;
        public long BytesChanged;

        public readonly Dictionary<string, FieldAccumulator> Fields =
            new(StringComparer.Ordinal);

        private readonly SecondBucket[] _window = CreateWindow();

        private static SecondBucket[] CreateWindow()
        {
            var window = new SecondBucket[WindowSeconds];

            for (int i = 0; i < window.Length; i++)
                window[i] = new SecondBucket();

            return window;
        }

        public FieldAccumulator Field(string name)
        {
            if (!Fields.TryGetValue(name, out var field))
                Fields[name] = field = new FieldAccumulator();

            return field;
        }

        /// <summary>
        /// The bucket for this second, cleared if the ring has come round to it
        /// again — which is what makes the window roll rather than accumulate.
        /// </summary>
        public SecondBucket Bucket(int second)
        {
            var bucket = _window[((second % WindowSeconds) + WindowSeconds) % WindowSeconds];

            if (bucket.Second != second)
            {
                bucket.Second = second;
                bucket.Words = 0;
            }

            return bucket;
        }

        /// <summary>Words in the last <see cref="WindowSeconds"/> seconds.</summary>
        public long RecentWords(int now)
        {
            long total = 0;

            foreach (var bucket in _window)
            {
                if (bucket.Second > now - WindowSeconds && bucket.Second <= now)
                    total += bucket.Words;
            }

            return total;
        }
    }
}

/// <summary>One class's share of the payload traffic.</summary>
/// <param name="Seen">Entity-frames offered, i.e. instances times frames sampled.</param>
/// <param name="ChangedFrames">Entity-frames in which anything at all changed.</param>
/// <param name="WordsTouched">Payload dwords that differed — the wire's own unit.</param>
/// <param name="BytesChanged">Bytes within those words that actually differed.</param>
/// <param name="BytesPerSecond">Estimated wire cost since the sampler started.</param>
/// <param name="RecentBytesPerSecond">The same over the rolling window.</param>
public readonly record struct NetTrafficClass(
    string ClassName,
    long Seen,
    long ChangedFrames,
    long WordsTouched,
    long BytesChanged,
    double BytesPerSecond,
    double RecentBytesPerSecond,
    IReadOnlyList<NetTrafficField> Fields);

/// <summary>One field's share, by the words its bytes sit in.</summary>
public readonly record struct NetTrafficField(string Name, long Words, double BytesPerSecond);

/// <summary>
/// Everything the traffic view reports in one reply.
///
/// <para>
/// <c>Sampling</c> is separate from an empty class list on purpose: a view that
/// is not sampling looks exactly like a world in which nothing moved, and a
/// reader who cannot tell them apart will conclude the payload is free.
/// </para>
/// </summary>
public readonly record struct NetTrafficReport(
    bool Sampling,
    double ElapsedSeconds,
    int TrackedEntities,
    IReadOnlyList<NetTrafficClass> Classes);
