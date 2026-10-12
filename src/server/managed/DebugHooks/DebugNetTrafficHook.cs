using SourceSharp.Abi;
using SourceSharp.Server.Interface;

namespace SourceSharp.Server;

/// <summary>
/// <see cref="IDebugNetTraffic"/> over the server's traffic profiler (group G6).
/// A null report function is a server that does not report traffic.
/// </summary>
public sealed class DebugNetTrafficHook(Func<NetTrafficReport>? report, Action reset) : IDebugNetTraffic
{
    private readonly Action _reset = reset ?? throw new ArgumentNullException(nameof(reset));

    public void Reset() => _reset();

    public DebugNetTraffic? Report()
    {
        if (report?.Invoke() is not { } r)
            return null;

        return new DebugNetTraffic(
            r.Sampling, r.ElapsedSeconds, r.TrackedEntities,
            NetTrafficSampler.BitsPerChangedWord, NetTrafficSampler.WindowSeconds, NetTrafficSampler.WordBytes,
            r.Classes.Select(c => new DebugTrafficClass(
                c.ClassName, c.Seen, c.ChangedFrames, c.WordsTouched, c.BytesChanged,
                c.BytesPerSecond, c.RecentBytesPerSecond,
                c.Fields.Select(f => new DebugTrafficField(f.Name, f.Words, f.BytesPerSecond)).ToArray()))
             .ToArray());
    }
}
