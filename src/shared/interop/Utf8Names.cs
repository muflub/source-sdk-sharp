namespace SourceSharp.Abi;

/// <summary>
/// Turns a native UTF-8 name into a managed string without allocating one every
/// time it is asked.
///
/// The game-event path is why this exists. Native fires an event, the stub
/// decodes the name so the registry can look up its handlers, and the registry
/// throws the string away — and events fire many times per tick, so that is a
/// fresh allocation per event per side, forever. It is a small allocation, which
/// is exactly why it is worth removing: a game does not lose a frame to the cost
/// of allocating, it loses one to the Gen0 collection that all those
/// allocations eventually add up to, and that collection lands in the middle of
/// whichever frame happens to be unlucky.
///
/// The set of names this ever sees is bounded and tiny — a mod subscribes to the
/// events it declares, and native only forwards those — so the names are decoded
/// once and matched thereafter by comparing raw bytes, which allocates nothing
/// and never touches the GC.
///
/// An instance, owned by whoever decodes: a managed-built binding holds its own,
/// so its cache loads and unloads with the side (plan_engineapi_vtables E12).
/// It holds nothing but byte arrays and strings, so no cache keeps a
/// collectible ALC alive. There is no shared one (statics to zero).
/// </summary>
public sealed unsafe class Utf8NameCache
{
    /// <summary>
    /// How many distinct names are worth remembering.
    ///
    /// A cap rather than unbounded growth, because "bounded and tiny" is a fact
    /// about the callers this has today, not a guarantee about every caller it
    /// will ever have. Past the cap it still returns the right string — it just
    /// allocates it, which is what the code did before this class existed. A
    /// silently-growing cache would be a leak; refusing to answer would be a
    /// bug. Degrading to the old cost is neither.
    /// </summary>
    private const int Capacity = 64;

    private readonly byte[]?[] _keys = new byte[]?[Capacity];
    private readonly string[] _values = new string[Capacity];
    private int _count;

    /// <summary>Names cached so far. For tests and diagnostics.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>
    /// The string for a NUL-terminated native UTF-8 name, cached across calls.
    /// Returns the SAME instance for the same bytes, so a caller may compare by
    /// reference — though nothing does, and nothing should start.
    /// </summary>
    public string Get(byte* p)
    {
        if (p == null)
            return string.Empty;

        int length = 0;
        while (p[length] != 0)
            length++;

        return Get(new ReadOnlySpan<byte>(p, length));
    }

    /// <summary>
    /// The string for a UTF-8 name, cached across calls.
    ///
    /// Read without a lock and published with one. Every managed entry point
    /// runs on the engine's own thread, so in practice there is one writer and
    /// the loop below sees a consistent prefix; the volatile read of the count
    /// is what makes "a consistent prefix" true rather than merely likely, and
    /// a race can only cost a duplicate entry, never a wrong answer.
    /// </summary>
    public string Get(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
            return string.Empty;

        int count = Volatile.Read(ref _count);
        var keys = _keys;
        var values = _values;

        for (int i = 0; i < count && i < keys.Length; i++)
        {
            if (keys[i] is { } key && utf8.SequenceEqual(key))
                return values[i];
        }

        // A linear scan rather than a hash: at this size comparing a dozen
        // bytes a handful of times is cheaper than hashing them once, and it
        // needs no allocation to ask the question.
        string decoded = System.Text.Encoding.UTF8.GetString(utf8);

        lock (_values)
        {
            // Re-check under the lock: a concurrent caller may have added the
            // same name while this one was decoding it, and two entries for one
            // name would break the same-instance promise above.
            for (int i = 0; i < _count; i++)
            {
                if (_keys[i] is { } key && utf8.SequenceEqual(key))
                    return _values[i];
            }

            if (_count < Capacity)
            {
                _keys[_count] = utf8.ToArray();
                _values[_count] = decoded;
                Volatile.Write(ref _count, _count + 1);
            }
        }

        return decoded;
    }
}
