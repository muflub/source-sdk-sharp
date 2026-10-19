namespace Debug.Api.Server.Interfaces;

/// <summary>One field's share of a class's payload traffic (group G6).</summary>
public sealed record DebugTrafficField(string Name, long Words, double BytesPerSecond);

/// <summary>One class's payload traffic (group G6).</summary>
/// <param name="Seen">Entity frames seen.</param>
/// <param name="ChangedFrames">Entity frames whose payload changed.</param>
/// <param name="WordsTouched">Payload words that changed.</param>
/// <param name="BytesChanged">Payload bytes that changed.</param>
public sealed record DebugTrafficClass(
    string ClassName, long Seen, long ChangedFrames, long WordsTouched, long BytesChanged,
    double BytesPerSecond, double RecentBytesPerSecond, IReadOnlyList<DebugTrafficField> Fields);

/// <summary>
/// What the networked payload has cost (group G6), with the wire units the
/// figures are in.
/// </summary>
/// <param name="Sampling">Whether the profiler is sampling now; false means the figures are the last ones measured.</param>
/// <param name="BitsPerChangedWord">What one changed payload word costs on the wire.</param>
/// <param name="WindowSeconds">The window the recent rates cover.</param>
/// <param name="WordBytes">The bytes in a payload word.</param>
public sealed record DebugNetTraffic(
    bool Sampling, double ElapsedSeconds, int TrackedEntities,
    int BitsPerChangedWord, int WindowSeconds, int WordBytes,
    IReadOnlyList<DebugTrafficClass> Classes);

/// <summary>The server's payload traffic profiler (group G6).</summary>
public interface IDebugNetTraffic
{
    /// <summary>
    /// What the profiler has totalled, or null when this server does not
    /// report traffic. Reads only what the game thread has already totalled.
    /// </summary>
    DebugNetTraffic? Report();

    /// <summary>Clears the totals, for a reader who wants a clean window.</summary>
    void Reset();
}
