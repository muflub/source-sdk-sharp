namespace SourceSharp.Shared;

/// <summary>
/// A buffer its owner keeps in a field and reuses from one call to the next,
/// grown on demand. Both answer a span of exactly the count asked for, so
/// nothing reads the buffer's capacity as if it were how many items it holds.
/// </summary>
public static class Scratch
{
    /// <summary>
    /// The buffer's first <paramref name="count"/> elements, holding whatever
    /// the last use left (or zeros after growing, which keeps nothing): for a
    /// caller that writes all of them before it reads any.
    /// </summary>
    public static Span<T> Sized<T>(ref T[] buffer, int count)
    {
        if (buffer.Length < count)
            buffer = new T[Math.Max(count, Math.Max(16, buffer.Length * 2))];
        return buffer.AsSpan(0, count);
    }

    /// <summary>
    /// The buffer's first <paramref name="count"/> elements, zeroed as a new
    /// array's would be: for an answer another party may leave words of unwritten.
    /// </summary>
    public static Span<T> Cleared<T>(ref T[] buffer, int count)
    {
        Span<T> span = Sized(ref buffer, count);
        span.Clear();
        return span;
    }
}
