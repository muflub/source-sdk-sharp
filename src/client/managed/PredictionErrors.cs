//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Buffers.Binary;

namespace SourceSharp.Abi;

/// <summary>
/// One component of one field, as the two sides saw it.
///
/// <para>
/// A VECTOR IS THREE OF THESE AND NOT ONE NUMBER, which is the whole reason
/// this type exists. "Origin differs" is what the console line could say; it
/// cannot say that x held, y held, and z is out by 3.2 units — and that
/// difference is the difference between a rounding artefact and a player
/// standing somewhere else.
/// </para>
/// </summary>
/// <param name="Predicted">What this side computed for the acknowledged command.</param>
/// <param name="Server">What the server said actually happened.</param>
/// <param name="Delta">Predicted minus Server. Signed, deliberately: a drift
/// that is always the same sign is a systematic error, and one that alternates
/// is noise. Taking the absolute value here would erase that.</param>
public readonly record struct PredictionComponent(double Predicted, double Server, double Delta);

/// <summary>
/// One declared field that differed, with everything needed to say WHETHER IT
/// MATTERS rather than only that it happened.
/// </summary>
public sealed class PredictionFieldError
{
    public PredictionFieldError(string name, int kind, int offset, int bytes,
                                float tolerance, bool tolerated, int differingBytes,
                                IReadOnlyList<PredictionComponent> components)
    {
        Name           = name;
        Kind           = kind;
        Offset         = offset;
        Bytes          = bytes;
        Tolerance      = tolerance;
        Tolerated      = tolerated;
        DifferingBytes = differingBytes;
        Components     = components;
    }

    /// <summary>The declared field name, or "(padding@N)" for a byte no field claims.</summary>
    public string Name { get; }

    /// <summary>The ABI's kind numbering — <see cref="StandaloneNetStates.TypeCodeOf"/>.</summary>
    public int Kind { get; }

    /// <summary>Blob offset of the field's first byte, or the differing byte for padding.</summary>
    public int Offset { get; }

    public int Bytes { get; }

    /// <summary>The field's <c>[Predicted]</c> tolerance, or 0 for "exact".</summary>
    public float Tolerance { get; }

    /// <summary>Whether the difference sat inside that tolerance and was forgiven.</summary>
    public bool Tolerated { get; }

    /// <summary>How many of this field's bytes actually differ.</summary>
    public int DifferingBytes { get; }

    public IReadOnlyList<PredictionComponent> Components { get; }

    /// <summary>
    /// Whether a tolerance CAN apply to this kind at all. Only the float-shaped
    /// kinds are tolerable, per component, exactly as stock's CompareVector does
    /// — an integer either matches or it does not.
    ///
    /// <para>
    /// Reported rather than left to be inferred from a zero tolerance, because
    /// "declared no tolerance" and "could not have one" are different facts and
    /// a reader who conflates them concludes a field is being held to a stricter
    /// standard than it is.
    /// </para>
    /// </summary>
    public bool Tolerable => Kind is 1 or 2 or 7;

    /// <summary>The largest absolute component delta, which is what a tolerance is tested against.</summary>
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

    /// <summary>
    /// How far past its own tolerance this field went: 1.0 is exactly at the
    /// limit, 8.0 is eight times out. Null when no tolerance is engaged, which
    /// is the case the ratio must not invent a number for.
    ///
    /// <para>
    /// THIS IS THE NUMBER THE CONSOLE LINE COULD NOT PRINT. "10 bytes differ"
    /// is the same for a hair and for a metre; a ratio is comparable across
    /// fields whose units are not, so Origin at 4x and Velocity at 1.1x sort
    /// correctly against each other.
    /// </para>
    /// </summary>
    public double? ToleranceRatio
        => Tolerance > 0f ? MaxAbsDelta / Tolerance : null;
}

/// <summary>
/// One compare that found a difference: which command, which fields, and by how
/// much.
/// </summary>
public sealed class PredictionErrorRecord
{
    public PredictionErrorRecord(long sequence, int command, uint typeId, string className,
                                 int blobBytes, int standingBytes, int firstDiffOffset,
                                 int toleratedFields, DateTime timeUtc,
                                 IReadOnlyList<PredictionFieldError> fields)
    {
        Sequence        = sequence;
        Command         = command;
        TypeId          = typeId;
        ClassName       = className;
        BlobBytes       = blobBytes;
        StandingBytes   = standingBytes;
        FirstDiffOffset = firstDiffOffset;
        ToleratedFields = toleratedFields;
        TimeUtc         = timeUtc;
        Fields          = fields;
    }

    /// <summary>Monotonic, so a reader can ask for "everything after 412" across a moving ring.</summary>
    public long Sequence { get; }

    /// <summary>The acknowledged command this compare judged. The x axis of the drift view.</summary>
    public int Command { get; }

    public uint TypeId { get; }

    /// <summary>The entity class, resolved from the type id. Empty when this side does not declare it.</summary>
    public string ClassName { get; }

    public int BlobBytes { get; }

    /// <summary>
    /// Bytes that STAND as mispredicted after tolerances — the number the
    /// console line prints. Zero means every difference was forgiven, which is a
    /// prediction that held and is deliberately still recorded: a tolerated
    /// difference that is growing is the early warning for one that will not be.
    /// </summary>
    public int StandingBytes { get; }

    public int FirstDiffOffset { get; }

    public int ToleratedFields { get; }

    public DateTime TimeUtc { get; }

    public IReadOnlyList<PredictionFieldError> Fields { get; }

    /// <summary>Whether anything survived tolerances. False is a hold, not a miss.</summary>
    public bool Mispredicted => StandingBytes > 0;
}

/// <summary>
/// Turns two images of one blob into the record above.
///
/// <para>
/// THE SAME WALK AS THE JUDGE, deliberately: whole-field advance, the same
/// "(padding@N)" label for a byte no field claims, and the tolerance decided by
/// <see cref="PredictionJudge.Tolerated"/> rather than by a second copy of the
/// rule. A visualiser that disagreed with the meter it visualises would be worse
/// than no visualiser, and the way to make sure it cannot is to call the same
/// function rather than to write the same code.
/// </para>
/// </summary>
public static class PredictionErrorAnalysis
{
    /// <summary>How many fields one record will describe before it stops adding them.</summary>
    /// <remarks>
    /// A blob whose every field moved is a reload or a wrong-length compare, not
    /// a misprediction, and describing two hundred fields of it would cost more
    /// than it says. The count of differing bytes is kept whole regardless.
    /// </remarks>
    public const int MaxFieldsPerRecord = 32;

    /// <summary>
    /// Describes a compare. Returns null when the two images are identical,
    /// which is the caller's cheap way of never building a record for a
    /// prediction that held byte for byte.
    /// </summary>
    /// <param name="catalog">
    /// This side's declared states (statics to zero), which name a type id's
    /// fields; null names none, and every difference reads as padding.
    /// </param>
    public static PredictionErrorRecord? Describe(NetStateCatalog? catalog, long sequence, uint typeId, int command,
                                                  ReadOnlySpan<byte> predicted,
                                                  ReadOnlySpan<byte> arrived,
                                                  DateTime timeUtc)
    {
        int bytes = Math.Min(predicted.Length, arrived.Length);

        if (bytes <= 0)
            return null;

        var fields = new List<PredictionFieldError>();
        int standing = 0;
        int tolerated = 0;
        int firstDiff = -1;
        int i = 0;

        while (i < bytes)
        {
            if (predicted[i] == arrived[i])
            {
                i++;
                continue;
            }

            if (firstDiff < 0)
                firstDiff = i;

            string name = ""; int start = -1, size = 0, kind = -1; float tolerance = 0f;
            int answer = catalog?.DescribeOffset(typeId, i,
                out name, out start, out size, out kind, out tolerance) ?? -1;

            int advanceTo = i + 1;
            bool isField = answer == 1 && start >= 0 && start + size <= bytes;
            bool forgiven = false;

            if (isField)
            {
                forgiven = PredictionJudge.Tolerated(kind, tolerance,
                    predicted.Slice(start, size), arrived.Slice(start, size));

                // The whole field is judged at once; skip to its end so a vector
                // that moved is one entry, not twelve.
                advanceTo = start + size;
            }

            int differing = 0;
            for (int b = i; b < advanceTo && b < bytes; b++)
            {
                if (predicted[b] != arrived[b])
                    differing++;
            }

            if (forgiven)
                tolerated++;
            else
                standing += differing;

            if (fields.Count < MaxFieldsPerRecord)
            {
                fields.Add(isField
                    ? new PredictionFieldError(name, kind, start, size, tolerance, forgiven,
                        differing, Components(kind, predicted.Slice(start, size), arrived.Slice(start, size)))
                    : new PredictionFieldError($"(padding@{i})", -1, i, 1, 0f, false,
                        differing, Array.Empty<PredictionComponent>()));
            }

            i = Math.Max(advanceTo, i + 1);
        }

        if (fields.Count == 0 && firstDiff < 0)
            return null;

        return new PredictionErrorRecord(sequence, command, typeId,
            catalog?.ClassNameOf(typeId) ?? "", bytes, standing, firstDiff,
            tolerated, timeUtc, fields);
    }

    /// <summary>
    /// One field's components as numbers, decoded through its kind.
    ///
    /// <para>
    /// A STRING GETS NONE, and that is the honest answer rather than a gap: its
    /// bytes are text, subtracting them would produce a number that means
    /// nothing, and a reader shown "delta 47" for a name field would be being
    /// misled by their own tool. The differing byte count still describes it.
    /// </para>
    /// </summary>
    public static IReadOnlyList<PredictionComponent> Components(
        int kind, ReadOnlySpan<byte> predicted, ReadOnlySpan<byte> arrived)
    {
        // The float-shaped kinds, which are also the only tolerable ones.
        int floats = kind switch
        {
            1 => 1,  // float
            2 => 3,  // vector
            7 => 3,  // qangle
            _ => 0,
        };

        if (floats > 0)
        {
            if (predicted.Length < floats * 4 || arrived.Length < floats * 4)
                return Array.Empty<PredictionComponent>();

            var components = new PredictionComponent[floats];

            for (int c = 0; c < floats; c++)
            {
                double was = BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(predicted.Slice(c * 4, 4)));
                double now = BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32LittleEndian(arrived.Slice(c * 4, 4)));

                components[c] = new PredictionComponent(was, now, was - now);
            }

            return components;
        }

        // A string is text; see the remark above.
        if (kind == 4 || kind < 0)
            return Array.Empty<PredictionComponent>();

        // Everything else is an integer of the field's own width. Read unsigned
        // for kinds 3, 5 and 6, signed for 0, so a negative int reads as one.
        if (!Integer(predicted, kind == 0, out double before) ||
            !Integer(arrived, kind == 0, out double after))
        {
            return Array.Empty<PredictionComponent>();
        }

        return new[] { new PredictionComponent(before, after, before - after) };
    }

    private static bool Integer(ReadOnlySpan<byte> span, bool signed, out double value)
    {
        switch (span.Length)
        {
            case 1:
                value = signed ? (sbyte)span[0] : span[0];
                return true;
            case 2:
                value = signed
                    ? BinaryPrimitives.ReadInt16LittleEndian(span)
                    : BinaryPrimitives.ReadUInt16LittleEndian(span);
                return true;
            case 4:
                value = signed
                    ? BinaryPrimitives.ReadInt32LittleEndian(span)
                    : BinaryPrimitives.ReadUInt32LittleEndian(span);
                return true;
            case 8:
                value = signed
                    ? BinaryPrimitives.ReadInt64LittleEndian(span)
                    : BinaryPrimitives.ReadUInt64LittleEndian(span);
                return true;
            default:
                value = 0;
                return false;
        }
    }
}

/// <summary>
/// The last N compares that found a difference, readable from another thread.
///
/// <para>
/// A ring rather than a growing list, for <see cref="LogRing"/>'s reason: a game
/// left running mispredicts continuously and nothing reads the far past. Records
/// carry a sequence number so a reader can follow along without the races an
/// index into a moving buffer would have.
/// </para>
///
/// <para>
/// The counters are NOT derivable from what the ring still holds and are kept
/// separately for that reason: a ring 256 deep has forgotten most of a session,
/// and a rate computed from what survives would read as though the game had only
/// ever done 256 compares.
/// </para>
/// </summary>
public sealed class PredictionErrorRing
{
    /// <summary>
    /// Records held. Deep enough to cover the tens of seconds a reader watches,
    /// small enough that the memory is not worth thinking about.
    /// </summary>
    public const int DefaultCapacity = 256;

    private readonly object _lock = new();
    private readonly NetStateCatalog? _catalog;
    private readonly PredictionErrorRecord[] _records;
    private long _nextSequence;
    private int _count;
    private int _head;
    private long _observed;
    private long _mispredicted;
    private long _toleratedOnly;

    /// <param name="capacity">How many records are kept.</param>
    /// <param name="catalog">
    /// The side's declared states, which <see cref="Observe"/> names fields
    /// through (statics to zero); null names none.
    /// </param>
    public PredictionErrorRing(int capacity = DefaultCapacity, NetStateCatalog? catalog = null)
    {
        _catalog = catalog;
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        _records = new PredictionErrorRecord[capacity];
    }

    public int Capacity => _records.Length;

    /// <summary>The sequence the next record will get.</summary>
    public long NextSequence
    {
        get { lock (_lock) return _nextSequence; }
    }

    /// <summary>Compares that found a difference at all, since the last reset.</summary>
    public long Observed
    {
        get { lock (_lock) return _observed; }
    }

    /// <summary>Of those, how many had something standing after tolerances.</summary>
    public long Mispredicted
    {
        get { lock (_lock) return _mispredicted; }
    }

    /// <summary>Of those, how many were entirely forgiven by declared tolerances.</summary>
    public long ToleratedOnly
    {
        get { lock (_lock) return _toleratedOnly; }
    }

    public int Count
    {
        get { lock (_lock) return _count; }
    }

    /// <summary>
    /// Describes a compare and keeps it, if it found anything. Returns the
    /// record, or null when the two images agreed — so a caller can be sure that
    /// a prediction that held costs one span comparison and no allocation.
    /// </summary>
    public PredictionErrorRecord? Observe(uint typeId, int command,
                                          ReadOnlySpan<byte> predicted,
                                          ReadOnlySpan<byte> arrived,
                                          DateTime timeUtc)
    {
        long sequence;

        lock (_lock)
            sequence = _nextSequence;

        var record = PredictionErrorAnalysis.Describe(_catalog, sequence, typeId, command,
                                                      predicted, arrived, timeUtc);

        if (record is null)
            return null;

        Add(record);
        return record;
    }

    /// <summary>Keeps a record already built. Separate so a test can hand one in.</summary>
    public void Add(PredictionErrorRecord record)
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

    /// <summary>
    /// Every record with a sequence at or after <paramref name="since"/>, oldest
    /// first, up to a limit. A reader further behind than the ring is deep gets
    /// what is left rather than an error — losing the oldest is what a ring is for.
    /// </summary>
    public List<PredictionErrorRecord> Since(long since, int max = 256)
    {
        var result = new List<PredictionErrorRecord>();

        lock (_lock)
        {
            int start = (_head - _count + _records.Length) % _records.Length;

            for (int i = 0; i < _count && result.Count < max; i++)
            {
                var record = _records[(start + i) % _records.Length];

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
