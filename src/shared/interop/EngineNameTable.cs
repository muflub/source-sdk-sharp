using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// The engine's names as strings, each made once. Model and class names are a
/// short list that game logic reads per bot per tick, each read a native call
/// answering bytes: looking the bytes up costs no string after the first.
/// Bounded, and past the bound it just makes strings. Locked because a side's
/// reads are not all on its main thread.
/// </summary>
public sealed class EngineNameTable
{
    /// <summary>Far above the names a map loads; a table that hits it is being fed something else.</summary>
    public const int MaxNames = 4096;

    private const int LongestLookedUp = 256;

    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    /// <summary>How many names it holds.</summary>
    public int Count
    {
        get
        {
            lock (_names)
                return _names.Count;
        }
    }

    /// <summary>The name these bytes decode to, the same string each time.</summary>
    public string Of(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
            return string.Empty;
        if (utf8.Length > LongestLookedUp)
            return Encoding.UTF8.GetString(utf8);

        Span<char> chars = stackalloc char[LongestLookedUp];
        chars = chars[..Encoding.UTF8.GetChars(utf8, chars)];

        lock (_names)
        {
            if (_names.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(chars, out string? known))
                return known;

            string name = new(chars);
            if (_names.Count < MaxNames)
                _names.Add(name);
            return name;
        }
    }
}
